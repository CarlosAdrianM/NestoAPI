using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>Épica #784 (incidencia 1186): una fila de Inventarios tal como está, para decidir si se puede borrar.</summary>
    public class FilaInventarioBorrar
    {
        public int NumeroOrden { get; set; }
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int StockCalculado { get; set; }
        public int StockReal { get; set; }
        /// <summary>1 = pendiente; 2 = contabilizada.</summary>
        public int Estado { get; set; }
        public int? Traspaso { get; set; }
        public string Usuario { get; set; }

        [JsonIgnore]
        public bool EstaPendiente => Estado == ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR && !Traspaso.HasValue;
    }

    /// <summary>Épica #784: una fila borrada del recuento, como quedó en el rastro (quién y cuándo la borró).</summary>
    public class FilaInventarioBorrada
    {
        public FilaInventarioBorrar Fila { get; set; }
        public string BorradaPor { get; set; }
        public DateTime BorradaEl { get; set; }
    }

    public interface IRepositorioBorrarFilaInventario
    {
        /// <summary>Null si no existe.</summary>
        Task<FilaInventarioBorrar> Leer(int numeroOrden);
        /// <summary>Abre la transacción con <paramref name="recurso"/> bloqueado (sp_getapplock).</summary>
        Task<IUnidadBorrarFilaInventario> Empezar(string recurso);
        /// <summary>Las filas borradas ese día (del rastro de Modificaciones), de todos los almacenes.</summary>
        Task<List<FilaInventarioBorrada>> Borradas(DateTime dia);
    }

    /// <summary>Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadBorrarFilaInventario : IDisposable
    {
        Task Bloquear(string recurso);
        /// <summary>Borra la fila solo si sigue pendiente (Estado 1, sin traspaso). Devuelve cuántas (0 o 1).</summary>
        Task<int> BorrarSiPendiente(int numeroOrden);
        /// <summary>El rastro en Modificaciones.</summary>
        Task Anotar(string tabla, string anterior, string nuevo, string usuario);
        void Confirmar();
    }

    public enum EstadoBorrarFilaInventario
    {
        Borrada,
        NoExiste,
        SinPermiso,
        /// <summary>409: contabilizada, de un día posterior a hoy o (de hoy) no es una fila que sobre.</summary>
        NoBorrable
    }

    public class ResultadoBorrarFilaInventario
    {
        public EstadoBorrarFilaInventario Estado { get; set; }
        public string Mensaje { get; set; }
        public FilaInventarioBorrar Fila { get; set; }
    }

    /// <summary>
    /// Épica #784 (Carlos 11/11/26): qué filas se pueden borrar. Borrar una fila NO la pone a 0: deja el producto sin contar (el SP de
    /// contabilizar solo toca las filas pendientes), así que borrar «lo que no hay» dejaría el stock del sistema. Solo se borran las
    /// filas pendientes de hoy que SOBRAN: las de los bloqueantes <see cref="CodigosComprobacionInventario.SIN_UBICACION"/> (producto que
    /// se ubica contado sin ubicación) y <see cref="CodigosComprobacionInventario.FILAS_DUPLICADAS"/> (en un grupo de dos o más siempre
    /// queda otra). Nunca una fila normal de hoy, ni la de un aviso, ni RecontadaSinContar. Y (12/11/26, incidencias 1227 y 1057)
    /// cualquier fila pendiente de un día ANTERIOR a hoy (<see cref="EsDeDiaAnterior"/>): se quedó sin contabilizar y bloquea el picking.
    /// Quién: Administración, Dirección e Informática, o con el parámetro de usuario <see cref="PARAMETRO_USUARIO"/> = "1".
    /// </summary>
    public static class ReglaBorrarFilaInventario
    {
        public const string PARAMETRO_USUARIO = "InventarioBorrarFilas";
        /// <summary>Épica #784: también TeoricoConReservas (11/11/26): la fila lleva en el teórico unidades reservadas; se borra y se vuelve a rellenar.</summary>
        public static readonly string[] CODIGOS_BORRABLES = { CodigosComprobacionInventario.SIN_UBICACION, CodigosComprobacionInventario.FILAS_DUPLICADAS,
            CodigosComprobacionInventario.TEORICO_CON_RESERVAS };

        public const string MENSAJE_NO_SOBRA = "Esta fila no se puede borrar: no es una fila que sobre. Si quiere decir que no hay ninguna unidad, " +
            "cuéntela a 0; si está mal contada, vuelva a contarla.";
        public const string MENSAJE_SIN_PERMISO = "No tiene permiso para borrar filas del inventario: avise a su encargado o a Dirección.";
        /// <summary>12/11/26: las de hoy, solo si sobran; las de días anteriores, siempre; las de días posteriores, nunca.</summary>
        public const string MENSAJE_FECHA_POSTERIOR = "Las filas de un día posterior a hoy no se pueden borrar: solo las de hoy que sobran y las que se quedaron " +
            "sin contabilizar de días anteriores.";

        /// <summary>Administración, Dirección e Informática siempre; el resto, con el parámetro a "1".</summary>
        public static bool PuedeBorrar(bool esGlobal, string valorParametro) =>
            esGlobal || string.Equals(valorParametro?.Trim(), "1", StringComparison.Ordinal);

        /// <summary>
        /// Incidencias 1227 y 1057 (Carlos 12/11/26): una fila pendiente de un día ANTERIOR a hoy se puede borrar siempre. Es, por
        /// definición, una fila del bloqueante <see cref="CodigosComprobacionInventario.OTRA_FECHA"/> cuando se comprueba hoy, y mientras
        /// siga pendiente los disparadores de Ubicaciones y ExtractoProducto paran el picking y los movimientos de ese producto.
        /// </summary>
        public static bool EsDeDiaAnterior(DateTime fecha, DateTime hoy) => fecha.Date < hoy.Date;

        /// <summary>La fila está ahora mismo en un bloqueante de los que se pueden borrar (y, si es duplicada, su grupo tiene otra).</summary>
        public static bool EsBorrable(ComprobacionesInventarioDTO comprobaciones, int numeroOrden)
        {
            if (comprobaciones?.Bloqueantes == null)
            {
                return false;
            }
            foreach (ComprobacionInventarioDTO bloqueante in comprobaciones.Bloqueantes.Where(b => b?.Filas != null && CODIGOS_BORRABLES.Contains(b.Codigo)))
            {
                FilaComprobacionInventarioDTO fila = bloqueante.Filas.FirstOrDefault(f => f != null && f.NºOrden == numeroOrden);
                if (fila == null)
                {
                    continue;
                }
                if (bloqueante.Codigo != CodigosComprobacionInventario.FILAS_DUPLICADAS)
                {
                    return true;
                }
                string clave = ClaveDuplicada(fila);
                if (bloqueante.Filas.Count(f => f != null && f.NºOrden.HasValue && ClaveDuplicada(f) == clave) > 1)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Marca <see cref="FilaComprobacionInventarioDTO.SePuedeBorrar"/> en las filas de hoy de los bloqueantes borrables y (12/11/26)
        /// en las del bloqueante <see cref="CodigosComprobacionInventario.OTRA_FECHA"/> de días anteriores a hoy, si quien pregunta puede
        /// borrar. Las demás, false (también las de hoy que salen en OtraFecha cuando se comprueba otro día).
        /// </summary>
        public static void Marcar(ComprobacionesInventarioDTO comprobaciones, bool puedeBorrar, DateTime hoy)
        {
            if (comprobaciones == null)
            {
                return;
            }
            List<ComprobacionInventarioDTO> bloqueantes = comprobaciones.Bloqueantes ?? new List<ComprobacionInventarioDTO>();
            foreach (FilaComprobacionInventarioDTO fila in bloqueantes.Concat(comprobaciones.Avisos ?? new List<ComprobacionInventarioDTO>())
                .Where(c => c?.Filas != null)
                .SelectMany(c => c.Filas)
                .Where(f => f != null))
            {
                fila.SePuedeBorrar = false;
            }
            if (!puedeBorrar)
            {
                return;
            }
            foreach (FilaComprobacionInventarioDTO fila in bloqueantes
                .Where(c => c?.Filas != null && CODIGOS_BORRABLES.Contains(c.Codigo))
                .SelectMany(c => c.Filas)
                .Where(f => f != null && f.NºOrden.HasValue && f.Fecha.HasValue && f.Fecha.Value.Date == hoy.Date))
            {
                fila.SePuedeBorrar = EsBorrable(comprobaciones, fila.NºOrden.Value);
            }
            foreach (FilaComprobacionInventarioDTO fila in bloqueantes
                .Where(c => c?.Filas != null && c.Codigo == CodigosComprobacionInventario.OTRA_FECHA)
                .SelectMany(c => c.Filas)
                .Where(f => f != null && f.NºOrden.HasValue && f.Fecha.HasValue && EsDeDiaAnterior(f.Fecha.Value, hoy)))
            {
                fila.SePuedeBorrar = true;
            }
        }

        /// <summary>
        /// El aviso «Filas borradas hoy del recuento (N)» con las borradas ese día (<paramref name="borradas"/> ya viene del rastro de
        /// ese día) en ese almacén (null si no hay). 12/11/26: salen todas las borradas ese día, también las de una fila de otro día
        /// (las pendientes de días anteriores que ahora se pueden borrar), con «(fila del dd/MM/yyyy)» detrás de la descripción.
        /// </summary>
        public static ComprobacionInventarioDTO AvisoBorradas(IEnumerable<FilaInventarioBorrada> borradas, string empresa, string almacen, DateTime dia)
        {
            List<FilaComprobacionInventarioDTO> filas = (borradas ?? Enumerable.Empty<FilaInventarioBorrada>())
                .Where(b => b?.Fila != null
                    && string.Equals(b.Fila.Empresa?.Trim(), empresa?.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(b.Fila.Almacen?.Trim(), almacen?.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.BorradaEl)
                .Select(b => new FilaComprobacionInventarioDTO
                {
                    NºOrden = null,
                    Producto = b.Fila.Producto?.Trim(),
                    Descripcion = b.Fila.Fecha.Date == dia.Date
                        ? b.Fila.Descripcion?.Trim()
                        : $"{b.Fila.Descripcion?.Trim()} (fila del {b.Fila.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)})".TrimStart(),
                    StockCalculado = b.Fila.StockCalculado,
                    StockReal = b.Fila.StockReal,
                    Pasillo = b.Fila.Pasillo?.Trim(),
                    Fila = b.Fila.Fila?.Trim(),
                    Columna = b.Fila.Columna?.Trim(),
                    Usuario = b.BorradaPor?.Trim(),
                    Fecha = b.BorradaEl,
                    SePuedeBorrar = false
                })
                .ToList();
            return filas.Count == 0
                ? null
                : new ComprobacionInventarioDTO
                {
                    Codigo = CodigosComprobacionInventario.FILAS_BORRADAS,
                    Titulo = $"Filas borradas hoy del recuento ({filas.Count}): esos productos no se contabilizan (se quedan como estaban)",
                    Filas = filas
                };
        }

        private static string ClaveDuplicada(FilaComprobacionInventarioDTO f) =>
            $"{f.Producto?.Trim().ToUpperInvariant()}#{f.Pasillo?.Trim().ToUpperInvariant()}|{f.Fila?.Trim().ToUpperInvariant()}|{f.Columna?.Trim().ToUpperInvariant()}";
    }

    /// <summary>
    /// Épica #784 (incidencia 1186, 11/11/26): borrar una fila pendiente del inventario que sobra (<see cref="ReglaBorrarFilaInventario"/>).
    /// Las contabilizadas no se borran nunca. Incidencias 1227 y 1057 (12/11/26): una pendiente de un día anterior a hoy se borra
    /// siempre (con los mismos permisos, bloqueos y rastro); una de un día posterior, nunca. Bloquea el almacén y la ubicación como el rellenado y el «a cero» y, ya con el bloqueo,
    /// comprueba que la fila sigue sobrando; frente a prdContabilizarInventario (que no usa applock) lo protege el propio DELETE, que
    /// solo borra si la fila sigue pendiente. Rastro en Modificaciones (Tabla <see cref="TABLA_MODIFICACIONES"/>, Anterior = la fila en
    /// JSON), del que sale el aviso «Filas borradas hoy del recuento».
    /// </summary>
    public class ServicioBorrarFilaInventario
    {
        public const string TABLA_MODIFICACIONES = "InventarioFilaBorrada";

        private readonly IRepositorioBorrarFilaInventario repositorio;

        public ServicioBorrarFilaInventario(IRepositorioBorrarFilaInventario repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <param name="motivoNoPuedeContar">(empresa, almacén) → null si puede contar en ese almacén; si no, el motivo (403).</param>
        /// <param name="puedeBorrar">Regla de quién (<see cref="ReglaBorrarFilaInventario.PuedeBorrar"/>).</param>
        /// <param name="sobra">La fila sobra ahora mismo (<see cref="ReglaBorrarFilaInventario.EsBorrable"/> sobre las comprobaciones de
        /// su almacén y día). Se llama ya con el bloqueo puesto.</param>
        public async Task<ResultadoBorrarFilaInventario> Borrar(int numeroOrden, Func<string, string, string> motivoNoPuedeContar, bool puedeBorrar,
            Func<FilaInventarioBorrar, Task<bool>> sobra, string usuario, DateTime hoy)
        {
            FilaInventarioBorrar fila = await repositorio.Leer(numeroOrden).ConfigureAwait(false);
            if (fila == null)
            {
                return Resultado(EstadoBorrarFilaInventario.NoExiste, $"No existe la fila {numeroOrden} del inventario.", null);
            }
            string motivo = motivoNoPuedeContar?.Invoke(fila.Empresa, fila.Almacen);
            if (motivo != null)
            {
                return Resultado(EstadoBorrarFilaInventario.SinPermiso, motivo, fila);
            }
            if (!puedeBorrar)
            {
                return Resultado(EstadoBorrarFilaInventario.SinPermiso, ReglaBorrarFilaInventario.MENSAJE_SIN_PERMISO, fila);
            }
            if (!fila.EstaPendiente)
            {
                return Resultado(EstadoBorrarFilaInventario.NoBorrable, MensajeNoPendiente(fila), fila);
            }
            if (fila.Fecha.Date > hoy.Date)
            {
                return Resultado(EstadoBorrarFilaInventario.NoBorrable,
                    $"La fila {numeroOrden} es del inventario del {Dia(fila.Fecha)}. " + ReglaBorrarFilaInventario.MENSAJE_FECHA_POSTERIOR, fila);
            }
            // Incidencias 1227 y 1057 (12/11/26): la de un día anterior se borra siempre (no hace falta que sobre).
            bool deDiaAnterior = ReglaBorrarFilaInventario.EsDeDiaAnterior(fila.Fecha, hoy);

            string empresa = fila.Empresa?.Trim();
            string almacen = fila.Almacen?.Trim().ToUpperInvariant();
            // Los mismos recursos que el rellenado (entero, parcial y por ubicación) y el «a cero», en el mismo orden: almacén y ubicación.
            using (IUnidadBorrarFilaInventario unidad = await repositorio.Empezar($"InventarioRellenar:{empresa}:{almacen}").ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(fila.Pasillo))
                {
                    await unidad.Bloquear($"InventarioRellenar:{empresa}:{almacen}:{fila.Pasillo.Trim().ToUpperInvariant()}:" +
                        $"{fila.Fila?.Trim().ToUpperInvariant()}:{fila.Columna?.Trim().ToUpperInvariant()}").ConfigureAwait(false);
                }
                // Con el bloqueo puesto: nadie puede estar borrando a la vez la otra fila del mismo duplicado.
                if (!deDiaAnterior && (sobra == null || !await sobra(fila).ConfigureAwait(false)))
                {
                    return Resultado(EstadoBorrarFilaInventario.NoBorrable, ReglaBorrarFilaInventario.MENSAJE_NO_SOBRA, fila);
                }
                int borradas = await unidad.BorrarSiPendiente(numeroOrden).ConfigureAwait(false);
                if (borradas == 0)
                {
                    FilaInventarioBorrar ahora = await repositorio.Leer(numeroOrden).ConfigureAwait(false);
                    return ahora == null
                        ? Resultado(EstadoBorrarFilaInventario.NoExiste, $"La fila {numeroOrden} del inventario ya no existe: la ha borrado otro usuario.", null)
                        : Resultado(EstadoBorrarFilaInventario.NoBorrable, MensajeNoPendiente(ahora), ahora);
                }
                string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
                await unidad.Anotar(TABLA_MODIFICACIONES, JsonConvert.SerializeObject(fila),
                    $"{Descripcion(fila)}. Borrada por {usuarioAuditoria}", usuarioAuditoria).ConfigureAwait(false);
                unidad.Confirmar();
            }
            return Resultado(EstadoBorrarFilaInventario.Borrada, null, fila);
        }

        /// <summary>Las filas borradas ese día (para el aviso de las comprobaciones). Nunca lanza: si falla, ninguna.</summary>
        public async Task<List<FilaInventarioBorrada>> Borradas(DateTime dia)
        {
            try
            {
                return await repositorio.Borradas(dia.Date).ConfigureAwait(false) ?? new List<FilaInventarioBorrada>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Inventario #784] No se han podido leer las filas borradas: {ex.Message}", ex), "Sistema (inventario)");
                return new List<FilaInventarioBorrada>();
            }
        }

        internal static string MensajeNoPendiente(FilaInventarioBorrar fila) =>
            $"La fila {fila.NumeroOrden} del inventario ya está contabilizada{(fila.Traspaso.HasValue ? $" (traspaso {fila.Traspaso.Value})" : string.Empty)}: " +
            "las filas contabilizadas no se borran.";

        /// <summary>«Fila 948787 del 11/11/2026: 44901 en ALG sin ubicación, teórico 3, contado 0, contó NUEVAVISION\Alfredo».</summary>
        internal static string Descripcion(FilaInventarioBorrar fila)
        {
            string ubicacion = string.IsNullOrWhiteSpace(fila.Pasillo)
                ? "sin ubicación"
                : $"{fila.Pasillo.Trim()}-{fila.Fila?.Trim()}-{fila.Columna?.Trim()}";
            return $"Fila {fila.NumeroOrden.ToString(CultureInfo.InvariantCulture)} del {Dia(fila.Fecha)}: {fila.Producto?.Trim()} en {fila.Almacen?.Trim()} " +
                $"{ubicacion}, teórico {fila.StockCalculado.ToString(CultureInfo.InvariantCulture)}, contado {fila.StockReal.ToString(CultureInfo.InvariantCulture)}, " +
                $"contó {fila.Usuario?.Trim()}";
        }

        private static string Dia(DateTime fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        private static ResultadoBorrarFilaInventario Resultado(EstadoBorrarFilaInventario estado, string mensaje, FilaInventarioBorrar fila) =>
            new ResultadoBorrarFilaInventario { Estado = estado, Mensaje = mensaje, Fila = fila };
    }

    /// <summary>Épica #784: la BD del borrado de una fila pendiente, por ADO y en una transacción con sp_getapplock.</summary>
    public class RepositorioBorrarFilaInventarioSql : IRepositorioBorrarFilaInventario
    {
        internal const string SQL_LEER = @"
SELECT i.NºOrden, RTRIM(i.Empresa), RTRIM(i.Almacén), i.Fecha, RTRIM(i.Número), RTRIM(i.Pasillo), RTRIM(i.Fila), RTRIM(i.Columna),
    ISNULL(i.StockCalculado, 0), ISNULL(i.StockReal, 0), i.Estado, i.NºTraspaso, RTRIM(i.Usuario), RTRIM(ISNULL(p.Nombre, i.Descripción))
FROM dbo.Inventarios i
LEFT JOIN dbo.Productos p ON p.Empresa = i.Empresa AND p.Número = i.Número
WHERE i.NºOrden = @NumeroOrden";

        internal const string SQL_BORRAR = "DELETE FROM dbo.Inventarios WHERE NºOrden = @NumeroOrden AND Estado = 1 AND NºTraspaso IS NULL";

        internal const string SQL_ANOTAR = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @Usuario)";

        // Modificaciones (unas 66.000 filas, agrupada por id): lo del día, con la fila borrada en JSON en Anterior.
        internal const string SQL_BORRADAS = "SELECT Anterior, RTRIM(Usuario), Fecha FROM dbo.Modificaciones WHERE Tabla = @Tabla AND Fecha >= @Desde AND Fecha < @Hasta";

        private readonly string cadenaConexion;

        public RepositorioBorrarFilaInventarioSql(NVEntities db)
        {
            cadenaConexion = (db ?? throw new ArgumentNullException(nameof(db))).Database.Connection.ConnectionString;
        }

        public async Task<FilaInventarioBorrar> Leer(int numeroOrden)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL_LEER, conexion))
                {
                    comando.Parameters.Add("@NumeroOrden", SqlDbType.Int).Value = numeroOrden;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await lector.ReadAsync().ConfigureAwait(false))
                        {
                            return null;
                        }
                        return new FilaInventarioBorrar
                        {
                            NumeroOrden = Convert.ToInt32(lector.GetValue(0)),
                            Empresa = Texto(lector, 1),
                            Almacen = Texto(lector, 2),
                            Fecha = lector.IsDBNull(3) ? DateTime.MinValue : lector.GetDateTime(3),
                            Producto = Texto(lector, 4),
                            Pasillo = Texto(lector, 5),
                            Fila = Texto(lector, 6),
                            Columna = Texto(lector, 7),
                            StockCalculado = Convert.ToInt32(lector.GetValue(8)),
                            StockReal = Convert.ToInt32(lector.GetValue(9)),
                            Estado = lector.IsDBNull(10) ? 0 : Convert.ToInt32(lector.GetValue(10)),
                            Traspaso = lector.IsDBNull(11) ? (int?)null : Convert.ToInt32(lector.GetValue(11)),
                            Usuario = Texto(lector, 12),
                            Descripcion = Texto(lector, 13)
                        };
                    }
                }
            }
        }

        public async Task<IUnidadBorrarFilaInventario> Empezar(string recurso)
        {
            var conexion = new SqlConnection(cadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                var unidad = new Unidad(conexion, conexion.BeginTransaction(IsolationLevel.ReadCommitted));
                await unidad.Bloquear(recurso).ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        public async Task<List<FilaInventarioBorrada>> Borradas(DateTime dia)
        {
            var resultado = new List<FilaInventarioBorrada>();
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL_BORRADAS, conexion))
                {
                    comando.Parameters.Add("@Tabla", SqlDbType.NVarChar, 50).Value = ServicioBorrarFilaInventario.TABLA_MODIFICACIONES;
                    comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = dia.Date;
                    comando.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = dia.Date.AddDays(1);
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            FilaInventarioBorrar fila = null;
                            try
                            {
                                fila = lector.IsDBNull(0) ? null : JsonConvert.DeserializeObject<FilaInventarioBorrar>(lector.GetString(0));
                            }
                            catch (JsonException)
                            {
                                // Un rastro que no es JSON: no sale en el aviso.
                            }
                            if (fila != null)
                            {
                                resultado.Add(new FilaInventarioBorrada
                                {
                                    Fila = fila,
                                    BorradaPor = Texto(lector, 1),
                                    BorradaEl = lector.GetDateTime(2)
                                });
                            }
                        }
                    }
                }
            }
            return resultado;
        }

        private static string Texto(SqlDataReader lector, int columna) =>
            lector.IsDBNull(columna) ? null : Convert.ToString(lector.GetValue(columna), CultureInfo.InvariantCulture).Trim();

        private class Unidad : IUnidadBorrarFilaInventario
        {
            private readonly SqlConnection conexion;
            private readonly SqlTransaction transaccion;
            private bool confirmada;

            public Unidad(SqlConnection conexion, SqlTransaction transaccion)
            {
                this.conexion = conexion;
                this.transaccion = transaccion;
            }

            public async Task Bloquear(string recurso)
            {
                const string sql = @"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 50001, 'Otro usuario está rellenando este inventario ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";
                using (var comando = new SqlCommand(sql, conexion, transaccion))
                {
                    comando.Parameters.Add("@Recurso", SqlDbType.NVarChar, 255).Value = recurso;
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public async Task<int> BorrarSiPendiente(int numeroOrden)
            {
                using (var comando = new SqlCommand(SQL_BORRAR, conexion, transaccion))
                {
                    comando.Parameters.Add("@NumeroOrden", SqlDbType.Int).Value = numeroOrden;
                    return await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public async Task Anotar(string tabla, string anterior, string nuevo, string usuario)
            {
                using (var comando = new SqlCommand(SQL_ANOTAR, conexion, transaccion))
                {
                    comando.Parameters.Add("@Tabla", SqlDbType.NVarChar, 50).Value = tabla;
                    comando.Parameters.Add("@Anterior", SqlDbType.NVarChar, -1).Value = (object)anterior ?? DBNull.Value;
                    comando.Parameters.Add("@Nuevo", SqlDbType.NVarChar, -1).Value = (object)nuevo ?? DBNull.Value;
                    string quien = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
                    comando.Parameters.Add("@Usuario", SqlDbType.NVarChar, 50).Value = quien.Length > 50 ? quien.Substring(0, 50) : quien;
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public void Confirmar()
            {
                transaccion.Commit();
                confirmada = true;
            }

            public void Dispose()
            {
                if (!confirmada)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ya no estaba viva: no hay nada que deshacer.
                    }
                }
                transaccion.Dispose();
                conexion.Dispose();
            }
        }
    }
}
