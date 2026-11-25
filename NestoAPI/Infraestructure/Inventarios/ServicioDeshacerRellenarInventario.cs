using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>DELETE api/Inventarios/Rellenar: lo que se ha borrado al deshacer el rellenado de una tienda.</summary>
    public class DeshacerRellenarInventarioRespuestaDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        /// <summary>Filas pendientes (Estado 1, sin traspaso) borradas de ese almacén ese día, de cualquier usuario.</summary>
        public int FilasBorradas { get; set; }
        /// <summary>De las borradas, cuántas tenían algo contado (StockReal distinto de 0).</summary>
        public int FilasContadas { get; set; }
        /// <summary>Suma de StockReal de las borradas.</summary>
        public int UnidadesContadas { get; set; }
    }

    /// <summary>Las filas borradas de un usuario (para el rastro).</summary>
    public class BorradasInventarioUsuario
    {
        public string Usuario { get; set; }
        public int Filas { get; set; }
        public int FilasContadas { get; set; }
        public int UnidadesContadas { get; set; }
    }

    /// <summary>Lo que decide <see cref="ReglaDeshacerRellenadoPropio"/>.</summary>
    public class DecisionDeshacerRellenado
    {
        public bool Puede { get; set; }
        /// <summary>Con Puede false: Forbidden (hay filas de otro usuario) o Conflict (ya hay filas contadas).</summary>
        public System.Net.HttpStatusCode? Estado { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// Rellenar por error (Paloma, 25/11/26: «no es el primero que rellenan sin querer»): LA REGLA (pura) de quién deshace el rellenado de
    /// un almacén y día. Dirección e Informática, siempre. Cualquier otro, solo si TODAS las filas pendientes son suyas (mismo usuario sin
    /// dominio ni mayúsculas) y NINGUNA está contada (StockReal distinto de 0, el criterio de FilasContadas); si no, 403 «hay filas de otro
    /// usuario» o 409 «ya hay N filas contadas». Sin filas pendientes no hay nada que impedir (deshacer no borra nada).
    /// </summary>
    public static class ReglaDeshacerRellenadoPropio
    {
        public static DecisionDeshacerRellenado Decidir(bool esDireccionOInformatica, string usuario, IEnumerable<ResumenPendientesInventarioUsuario> pendientes)
        {
            if (esDireccionOInformatica)
            {
                return new DecisionDeshacerRellenado { Puede = true };
            }
            List<ResumenPendientesInventarioUsuario> lista = (pendientes ?? Enumerable.Empty<ResumenPendientesInventarioUsuario>()).Where(p => p != null && p.Filas > 0).ToList();
            string yo = SinDominio(usuario);
            List<ResumenPendientesInventarioUsuario> deOtros = lista
                .Where(p => !string.Equals(SinDominio(p.Usuario), yo, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(yo))
                .ToList();
            if (deOtros.Count > 0)
            {
                string quienes = string.Join(", ", deOtros.OrderByDescending(p => p.Filas).Select(p => $"{SinDominio(p.Usuario)}: {p.Filas}"));
                return new DecisionDeshacerRellenado
                {
                    Puede = false,
                    Estado = System.Net.HttpStatusCode.Forbidden,
                    Mensaje = $"No puede deshacer el rellenado: hay filas de otro usuario ({quienes}). Pídaselo a Dirección."
                };
            }
            int contadas = lista.Sum(p => p.FilasContadas);
            if (contadas > 0)
            {
                return new DecisionDeshacerRellenado
                {
                    Puede = false,
                    Estado = System.Net.HttpStatusCode.Conflict,
                    Mensaje = $"No puede deshacer el rellenado: ya hay {(contadas == 1 ? "1 fila contada" : $"{contadas} filas contadas")}: pídaselo a Dirección."
                };
            }
            return new DecisionDeshacerRellenado { Puede = true };
        }

        /// <summary>«NUEVAVISION\Paloma» → «Paloma»; null → cadena vacía.</summary>
        internal static string SinDominio(string usuario)
        {
            string limpio = usuario?.Trim() ?? string.Empty;
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 ? limpio.Substring(barra + 1).Trim() : limpio;
        }
    }

    public interface IRepositorioDeshacerRellenarInventario
    {
        /// <summary>Null si el almacén no existe en la empresa.</summary>
        Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen);
        /// <summary>Abre la transacción con el MISMO bloqueo que el rellenado (InventarioRellenar:{empresa}:{almacen}).</summary>
        Task<IUnidadDeshacerRellenarInventario> Empezar(string empresa, string almacen);
    }

    /// <summary>Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadDeshacerRellenarInventario : IDisposable
    {
        /// <summary>
        /// Rellenar por error (25/11/26): las pendientes del almacén ese día por usuario, con lo contado, BLOQUEADAS hasta el final de la
        /// transacción (UPDLOCK, HOLDLOCK): nadie las cuenta entre comprobarlas y borrarlas.
        /// </summary>
        Task<List<ResumenPendientesInventarioUsuario>> LeerPendientes(string empresa, string almacen, DateTime fecha);
        /// <summary>Borra las filas pendientes (Estado 1 y NºTraspaso NULL) del almacén ese día y devuelve lo borrado por usuario.</summary>
        Task<List<BorradasInventarioUsuario>> BorrarPendientes(string empresa, string almacen, DateTime fecha);
        /// <summary>El rastro en Modificaciones.</summary>
        Task Anotar(string tabla, string anterior, string nuevo, string usuario);
        void Confirmar();
    }

    public enum EstadoDeshacerRellenarInventario
    {
        Ok,
        SinPermiso,
        PeticionIncorrecta,
        ConUbicaciones,
        /// <summary>Rellenar por error (25/11/26): quien lo rellenó no puede deshacerlo porque ya hay filas contadas (409).</summary>
        YaContadas
    }

    public class ResultadoDeshacerRellenarInventario
    {
        public EstadoDeshacerRellenarInventario Estado { get; set; }
        public string Mensaje { get; set; }
        public DeshacerRellenarInventarioRespuestaDTO Respuesta { get; set; }
    }

    /// <summary>
    /// Deshacer el rellenado (NestoAPI#789) del inventario de una tienda: borra TODAS las filas pendientes (Estado 1, sin traspaso) de
    /// ese almacén y ese día, de cualquier usuario, también lo ya contado. Dirección e Informática; y, desde el 25/11/26, quien lo rellenó si
    /// todo lo pendiente es suyo y no hay nada contado (<see cref="ReglaDeshacerRellenadoPropio"/>). Solo almacenes sin control de
    /// ubicaciones (Algete va por ubicaciones). Con el mismo applock que el rellenado y en una transacción; nunca toca filas
    /// contabilizadas ni ExtractoProducto. Rastro: UNA fila resumen en Modificaciones (Tabla <see cref="TABLA_MODIFICACIONES"/>).
    /// </summary>
    public class ServicioDeshacerRellenarInventario
    {
        public const string TABLA_MODIFICACIONES = "InventarioRellenadoDeshecho";
        public const string MENSAJE_SIN_PERMISO = "Deshacer el rellenado del inventario solo lo pueden hacer Dirección e Informática, " +
            "o quien lo rellenó si todas las filas son suyas y no hay nada contado.";

        private readonly IRepositorioDeshacerRellenarInventario repositorio;

        public ServicioDeshacerRellenarInventario(IRepositorioDeshacerRellenarInventario repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>Dirección e Informática (Administración no).</summary>
        public static bool PuedeDeshacer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <param name="puedeDeshacer">Dirección o Informática: deshace todo lo pendiente, sea de quien sea.</param>
        /// <param name="puedeDeshacerPropio">
        /// Rellenar por error (25/11/26): quien puede rellenar (Tiendas, Almacén, Administración…) deshace el SUYO si todas las filas
        /// pendientes son suyas y ninguna está contada (<see cref="ReglaDeshacerRellenadoPropio"/>; se mira con las filas bloqueadas).
        /// </param>
        public async Task<ResultadoDeshacerRellenarInventario> Deshacer(string empresa, string almacen, DateTime? fecha, bool puedeDeshacer, string usuario,
            bool puedeDeshacerPropio = false)
        {
            if (!puedeDeshacer && !puedeDeshacerPropio)
            {
                return Error(EstadoDeshacerRellenarInventario.SinPermiso, MENSAJE_SIN_PERMISO);
            }
            if (string.IsNullOrWhiteSpace(almacen))
            {
                return Error(EstadoDeshacerRellenarInventario.PeticionIncorrecta, "Falta el almacén.");
            }
            if (!fecha.HasValue)
            {
                return Error(EstadoDeshacerRellenarInventario.PeticionIncorrecta, "Falta la fecha del inventario que quiere deshacer.");
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string almacenLimpio = almacen.Trim().ToUpperInvariant();
            DateTime dia = fecha.Value.Date;

            AlmacenInventario datosAlmacen = await repositorio.LeerAlmacen(empresaLimpia, almacenLimpio).ConfigureAwait(false);
            if (datosAlmacen == null)
            {
                return Error(EstadoDeshacerRellenarInventario.PeticionIncorrecta, $"No existe el almacén {almacenLimpio} en la empresa {empresaLimpia}.");
            }
            if (datosAlmacen.ControlUbicaciones)
            {
                string nombre = string.IsNullOrWhiteSpace(datosAlmacen.Nombre) ? almacenLimpio : datosAlmacen.Nombre.Trim();
                return Error(EstadoDeshacerRellenarInventario.ConUbicaciones,
                    $"{nombre} se inventaría por ubicaciones: su inventario no se rellena de una vez y no se puede deshacer así.");
            }

            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            DeshacerRellenarInventarioRespuestaDTO respuesta;
            using (IUnidadDeshacerRellenarInventario unidad = await repositorio.Empezar(empresaLimpia, almacenLimpio).ConfigureAwait(false))
            {
                if (!puedeDeshacer)
                {
                    List<ResumenPendientesInventarioUsuario> pendientes = await unidad.LeerPendientes(empresaLimpia, almacenLimpio, dia).ConfigureAwait(false);
                    DecisionDeshacerRellenado decision = ReglaDeshacerRellenadoPropio.Decidir(false, usuario, pendientes);
                    if (!decision.Puede)
                    {
                        return Error(decision.Estado == System.Net.HttpStatusCode.Conflict ? EstadoDeshacerRellenarInventario.YaContadas : EstadoDeshacerRellenarInventario.SinPermiso,
                            decision.Mensaje);
                    }
                }
                List<BorradasInventarioUsuario> borradas = await unidad.BorrarPendientes(empresaLimpia, almacenLimpio, dia).ConfigureAwait(false)
                    ?? new List<BorradasInventarioUsuario>();
                respuesta = new DeshacerRellenarInventarioRespuestaDTO
                {
                    Almacen = almacenLimpio,
                    Fecha = dia,
                    FilasBorradas = borradas.Sum(b => b.Filas),
                    FilasContadas = borradas.Sum(b => b.FilasContadas),
                    UnidadesContadas = borradas.Sum(b => b.UnidadesContadas)
                };
                if (respuesta.FilasBorradas > 0)
                {
                    string anterior = JsonConvert.SerializeObject(new
                    {
                        Empresa = empresaLimpia,
                        respuesta.Almacen,
                        Fecha = dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        respuesta.FilasBorradas,
                        respuesta.FilasContadas,
                        respuesta.UnidadesContadas,
                        PorUsuario = borradas
                    });
                    await unidad.Anotar(TABLA_MODIFICACIONES, anterior, Descripcion(respuesta, usuarioAuditoria), usuarioAuditoria).ConfigureAwait(false);
                }
                unidad.Confirmar();
            }
            return new ResultadoDeshacerRellenarInventario { Estado = EstadoDeshacerRellenarInventario.Ok, Respuesta = respuesta };
        }

        /// <summary>«Rellenado de REI del 12/11/2026 deshecho: 2.950 filas pendientes borradas (12 con algo contado, 31 unidades). Por NUEVAVISION\Carlos».</summary>
        internal static string Descripcion(DeshacerRellenarInventarioRespuestaDTO r, string usuario) =>
            $"Rellenado de {r.Almacen} del {r.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} deshecho: " +
            $"{r.FilasBorradas.ToString(CultureInfo.InvariantCulture)} filas pendientes borradas " +
            $"({r.FilasContadas.ToString(CultureInfo.InvariantCulture)} con algo contado, {r.UnidadesContadas.ToString(CultureInfo.InvariantCulture)} unidades). " +
            $"Por {usuario}";

        private static ResultadoDeshacerRellenarInventario Error(EstadoDeshacerRellenarInventario estado, string mensaje) =>
            new ResultadoDeshacerRellenarInventario { Estado = estado, Mensaje = mensaje };
    }

    /// <summary>La BD de deshacer el rellenado, por ADO y en una transacción con sp_getapplock.</summary>
    public class RepositorioDeshacerRellenarInventarioSql : IRepositorioDeshacerRellenarInventario
    {
        // Solo pendientes (Estado 1 y sin traspaso) de ese almacén y ese día: nunca lo contabilizado.
        internal const string SQL_BORRAR = @"DECLARE @Borradas TABLE (Usuario varchar(50) NULL, StockReal int NULL);
DELETE FROM dbo.Inventarios
OUTPUT deleted.Usuario, deleted.StockReal INTO @Borradas
WHERE Empresa = @Empresa AND Almacén = @Almacen AND Fecha >= @Desde AND Fecha < @Hasta AND Estado = 1 AND NºTraspaso IS NULL;
SELECT RTRIM(Usuario), COUNT(*), SUM(CASE WHEN ISNULL(StockReal, 0) <> 0 THEN 1 ELSE 0 END), SUM(ISNULL(StockReal, 0))
FROM @Borradas
GROUP BY RTRIM(Usuario);";

        private readonly string cadenaConexion;
        private readonly RepositorioRellenarInventarioSql rellenar;

        public RepositorioDeshacerRellenarInventarioSql(NVEntities db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }
            cadenaConexion = db.Database.Connection.ConnectionString;
            rellenar = new RepositorioRellenarInventarioSql(db);
        }

        public Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen) => rellenar.LeerAlmacen(empresa, almacen);

        public async Task<IUnidadDeshacerRellenarInventario> Empezar(string empresa, string almacen)
        {
            var conexion = new SqlConnection(cadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                var unidad = new Unidad(conexion, conexion.BeginTransaction(IsolationLevel.ReadCommitted));
                await unidad.Bloquear($"InventarioRellenar:{empresa}:{almacen}").ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        private class Unidad : IUnidadDeshacerRellenarInventario
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
IF @r < 0 THROW 50001, 'Otro usuario está rellenando el inventario de este almacén ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";
                using (var comando = new SqlCommand(sql, conexion, transaccion))
                {
                    comando.Parameters.Add("@Recurso", SqlDbType.NVarChar, 255).Value = recurso;
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public async Task<List<ResumenPendientesInventarioUsuario>> LeerPendientes(string empresa, string almacen, DateTime fecha)
            {
                using (var comando = new SqlCommand(string.Empty, conexion, transaccion))
                {
                    return await SqlRellenadoInventario.LeerPendientesPorUsuario(comando, empresa, almacen, fecha, bloquear: true).ConfigureAwait(false);
                }
            }

            public async Task<List<BorradasInventarioUsuario>> BorrarPendientes(string empresa, string almacen, DateTime fecha)
            {
                var resultado = new List<BorradasInventarioUsuario>();
                using (var comando = new SqlCommand(SQL_BORRAR, conexion, transaccion))
                {
                    comando.CommandTimeout = 120;
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = fecha.Date;
                    comando.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = fecha.Date.AddDays(1);
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new BorradasInventarioUsuario
                            {
                                Usuario = lector.IsDBNull(0) ? null : lector.GetString(0),
                                Filas = Convert.ToInt32(lector.GetValue(1)),
                                FilasContadas = lector.IsDBNull(2) ? 0 : Convert.ToInt32(lector.GetValue(2)),
                                UnidadesContadas = lector.IsDBNull(3) ? 0 : Convert.ToInt32(lector.GetValue(3))
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task Anotar(string tabla, string anterior, string nuevo, string usuario)
            {
                using (var comando = new SqlCommand(RepositorioBorrarFilaInventarioSql.SQL_ANOTAR, conexion, transaccion))
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
