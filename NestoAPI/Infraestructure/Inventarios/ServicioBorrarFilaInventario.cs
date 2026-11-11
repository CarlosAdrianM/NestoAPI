using NestoAPI.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
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
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int StockCalculado { get; set; }
        public int StockReal { get; set; }
        /// <summary>1 = pendiente; 2 = contabilizada.</summary>
        public int Estado { get; set; }
        public int? Traspaso { get; set; }
        public string Usuario { get; set; }

        public bool EstaPendiente => Estado == ServicioRellenarInventario.ESTADO_SIN_CONTABILIZAR && !Traspaso.HasValue;
    }

    public interface IRepositorioBorrarFilaInventario
    {
        /// <summary>Null si no existe.</summary>
        Task<FilaInventarioBorrar> Leer(int numeroOrden);
        /// <summary>Abre la transacción con <paramref name="recurso"/> bloqueado (sp_getapplock).</summary>
        Task<IUnidadBorrarFilaInventario> Empezar(string recurso);
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
        NoPendiente
    }

    public class ResultadoBorrarFilaInventario
    {
        public EstadoBorrarFilaInventario Estado { get; set; }
        public string Mensaje { get; set; }
        public FilaInventarioBorrar Fila { get; set; }
    }

    /// <summary>
    /// Épica #784 (incidencia 1186, 11/11/26): borrar una fila pendiente del inventario (la fila sin ubicación que bloqueaba
    /// «Contabilizar inventario» y que hubo que quitar por SQL). Las contabilizadas no se borran nunca. Permisos: los de contar en el
    /// almacén de la fila (los decide el controlador); quien no es de Administración, Dirección o Informática, solo las de hoy.
    /// Bloquea el almacén y la ubicación como el rellenado y el «a cero»; frente a prdContabilizarInventario (que no usa applock) lo
    /// protege el propio DELETE, que solo borra si la fila sigue pendiente. Rastro en Modificaciones.
    /// </summary>
    public class ServicioBorrarFilaInventario
    {
        public const string TABLA_MODIFICACIONES = "Inventarios";
        public const string MENSAJE_SOLO_HOY =
            "Solo puede borrar filas del inventario de hoy. Las de otros días las borran Administración, Dirección o Informática.";

        private readonly IRepositorioBorrarFilaInventario repositorio;

        public ServicioBorrarFilaInventario(IRepositorioBorrarFilaInventario repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <param name="motivoNoPuede">(empresa, almacén) → null si puede contar en ese almacén; si no, el motivo (403).</param>
        /// <param name="esGlobal">Administración, Dirección o Informática: también las de otros días.</param>
        public async Task<ResultadoBorrarFilaInventario> Borrar(int numeroOrden, Func<string, string, string> motivoNoPuede, bool esGlobal,
            string usuario, DateTime hoy)
        {
            FilaInventarioBorrar fila = await repositorio.Leer(numeroOrden).ConfigureAwait(false);
            if (fila == null)
            {
                return Resultado(EstadoBorrarFilaInventario.NoExiste, $"No existe la fila {numeroOrden} del inventario.", null);
            }
            string motivo = motivoNoPuede?.Invoke(fila.Empresa, fila.Almacen);
            if (motivo != null)
            {
                return Resultado(EstadoBorrarFilaInventario.SinPermiso, motivo, fila);
            }
            if (!fila.EstaPendiente)
            {
                return Resultado(EstadoBorrarFilaInventario.NoPendiente, MensajeNoPendiente(fila), fila);
            }
            if (!esGlobal && fila.Fecha.Date != hoy.Date)
            {
                return Resultado(EstadoBorrarFilaInventario.SinPermiso,
                    $"La fila {numeroOrden} es del inventario del {Dia(fila.Fecha)}. " + MENSAJE_SOLO_HOY, fila);
            }

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
                int borradas = await unidad.BorrarSiPendiente(numeroOrden).ConfigureAwait(false);
                if (borradas == 0)
                {
                    FilaInventarioBorrar ahora = await repositorio.Leer(numeroOrden).ConfigureAwait(false);
                    return ahora == null
                        ? Resultado(EstadoBorrarFilaInventario.NoExiste, $"La fila {numeroOrden} del inventario ya no existe: la ha borrado otro usuario.", null)
                        : Resultado(EstadoBorrarFilaInventario.NoPendiente, MensajeNoPendiente(ahora), ahora);
                }
                string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
                await unidad.Anotar(TABLA_MODIFICACIONES, Descripcion(fila), $"Fila {numeroOrden} borrada por {usuarioAuditoria}", usuarioAuditoria)
                    .ConfigureAwait(false);
                unidad.Confirmar();
            }
            return Resultado(EstadoBorrarFilaInventario.Borrada, null, fila);
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
    ISNULL(i.StockCalculado, 0), ISNULL(i.StockReal, 0), i.Estado, i.NºTraspaso, RTRIM(i.Usuario)
FROM dbo.Inventarios i WHERE i.NºOrden = @NumeroOrden";

        internal const string SQL_BORRAR = "DELETE FROM dbo.Inventarios WHERE NºOrden = @NumeroOrden AND Estado = 1 AND NºTraspaso IS NULL";

        internal const string SQL_ANOTAR = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @Usuario)";

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
                            Usuario = Texto(lector, 12)
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
