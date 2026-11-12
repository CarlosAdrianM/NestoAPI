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
        ConUbicaciones
    }

    public class ResultadoDeshacerRellenarInventario
    {
        public EstadoDeshacerRellenarInventario Estado { get; set; }
        public string Mensaje { get; set; }
        public DeshacerRellenarInventarioRespuestaDTO Respuesta { get; set; }
    }

    /// <summary>
    /// Deshacer el rellenado (NestoAPI#789) del inventario de una tienda: borra TODAS las filas pendientes (Estado 1, sin traspaso) de
    /// ese almacén y ese día, de cualquier usuario, también lo ya contado. Solo Dirección e Informática; solo almacenes sin control de
    /// ubicaciones (Algete va por ubicaciones). Con el mismo applock que el rellenado y en una transacción; nunca toca filas
    /// contabilizadas ni ExtractoProducto. Rastro: UNA fila resumen en Modificaciones (Tabla <see cref="TABLA_MODIFICACIONES"/>).
    /// </summary>
    public class ServicioDeshacerRellenarInventario
    {
        public const string TABLA_MODIFICACIONES = "InventarioRellenadoDeshecho";
        public const string MENSAJE_SIN_PERMISO = "Deshacer el rellenado del inventario solo lo pueden hacer Dirección e Informática.";

        private readonly IRepositorioDeshacerRellenarInventario repositorio;

        public ServicioDeshacerRellenarInventario(IRepositorioDeshacerRellenarInventario repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        /// <summary>Dirección e Informática (Administración no).</summary>
        public static bool PuedeDeshacer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        public async Task<ResultadoDeshacerRellenarInventario> Deshacer(string empresa, string almacen, DateTime? fecha, bool puedeDeshacer, string usuario)
        {
            if (!puedeDeshacer)
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
