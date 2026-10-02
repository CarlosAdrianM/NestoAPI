using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que se escribe al dar entrada a una reposición, todo dentro de una transacción.</summary>
    public interface ITransaccionCierreReposicion
    {
        Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia);
        /// <summary>Almacenes.DiarioEntradaRep del almacén de destino. Null si no tiene.</summary>
        Task<string> DiarioDeEntrada(string empresa, string almacen);
        /// <summary>Los traspasos que hay en ese diario del almacén, pendientes de contabilizar.</summary>
        Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario);
        /// <summary>prdExtrProducto del diario entero, como hoy desde Nesto viejo.</summary>
        Task Contabilizar(string empresa, string diario, string usuario);
        Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas);
    }

    public interface IRepositorioCierreReposiciones
    {
        Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> trabajo);
    }

    /// <summary>
    /// NestoAPI#553: dar entrada a una reposición es lo que hace hoy Nesto viejo: contabilizar con prdExtrProducto el
    /// diario de entrada de reposiciones (Almacenes.DiarioEntradaRep) del almacén de destino, con el usuario que la da.
    /// prdExtrProducto contabiliza el diario ENTERO (en los últimos 60 días, a veces entraban dos traspasos a la vez).
    /// </summary>
    public class RepositorioCierreReposiciones : IRepositorioCierreReposiciones, IDisposable
    {
        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioCierreReposiciones(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioCierreReposiciones ConContextoPropio()
        {
            return new RepositorioCierreReposiciones(new NVEntities()) { contextoPropio = true };
        }

        public async Task<ResultadoTerminarRecepcionDTO> EnTransaccion(Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> trabajo)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    ResultadoTerminarRecepcionDTO resultado = await trabajo(new TransaccionCierreReposicionSql(db)).ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception ex)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (Exception)
                    {
                        // El ROLLBACK del procedimiento ya la ha deshecho
                    }
                    SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
                    if (sql != null && sql.Class >= 11 && sql.Class <= 16)
                    {
                        throw new NestoBusinessException(EvidenciasRecepcionSql.Traducir(sql), ex);
                    }
                    throw;
                }
            }
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db?.Dispose();
            }
        }
    }

    public class TransaccionCierreReposicionSql : ITransaccionCierreReposicion
    {
        internal const string SQL_DIARIO_ENTRADA = @"
SELECT RTRIM(DiarioEntradaRep) FROM Almacenes WHERE Empresa = @p0 AND [Número] = @p1";

        // El mismo origen que la lectura de lo pendiente (RepositorioRecepcionReposiciones)
        internal const string SQL_TRASPASOS_EN_DIARIO = @"
SELECT DISTINCT p.[NºTraspaso] FROM PreExtrProducto p WITH (UPDLOCK)
WHERE p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.Diario = @p2 AND p.[NºTraspaso] > 0";

        private readonly NVEntities db;
        private readonly IServicioExtractoProducto extractos;

        public TransaccionCierreReposicionSql(NVEntities db, IServicioExtractoProducto extractos = null)
        {
            this.db = db;
            this.extractos = extractos ?? new ServicioExtractoProducto();
        }

        public Task<bool> YaRegistrada(string empresa, IEnumerable<Guid> idsEvidencia)
        {
            return EvidenciasRecepcionSql.YaRegistrada(db, idsEvidencia);
        }

        public async Task<string> DiarioDeEntrada(string empresa, string almacen)
        {
            string diario = await db.Database.SqlQuery<string>(SQL_DIARIO_ENTRADA, empresa, almacen).FirstOrDefaultAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(diario) ? null : diario.Trim();
        }

        public Task<List<int>> TraspasosEnDiario(string empresa, string almacen, string diario)
        {
            return db.Database.SqlQuery<int>(SQL_TRASPASOS_EN_DIARIO, empresa, almacen, diario).ToListAsync();
        }

        public Task Contabilizar(string empresa, string diario, string usuario)
        {
            return extractos.ContabilizarDiario(db, empresa, diario, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        public Task RegistrarEvidencia(string empresa, IEnumerable<EvidenciaRecepcion> filas)
        {
            return EvidenciasRecepcionSql.Registrar(db, empresa, OrigenRecepcionReposiciones.TIPO, filas);
        }
    }

    /// <summary>La evidencia de las recepciones en PreparacionEscaneos (Fase RECE), común a todos los tipos.</summary>
    public static class EvidenciasRecepcionSql
    {
        public const string FASE = "RECE";

        internal const string SQL_YA_REGISTRADA = "SELECT COUNT(*) FROM PreparacionEscaneos WHERE IdCliente IN ({0})";

        internal const string SQL_EVIDENCIA = @"
INSERT INTO PreparacionEscaneos
       (IdCliente, Empresa, NumeroOrigen, Pedido, LineaPedido, Producto, Fase, Cantidad, Metodo, Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo, TipoOrigen)
SELECT @p0, @p1, @p2, NULL, NULL, @p3, '" + FASE + @"', @p4, 'SCAN', NULL, @p5, @p6, @p7, GETDATE(), @p8
WHERE NOT EXISTS (SELECT 1 FROM PreparacionEscaneos WHERE IdCliente = @p0)";

        public static async Task<bool> YaRegistrada(NVEntities db, IEnumerable<Guid> idsEvidencia)
        {
            List<Guid> ids = (idsEvidencia ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (!ids.Any())
            {
                return false;
            }
            string parametros = string.Join(", ", ids.Select((id, i) => "@p" + i));
            int encontrados = await db.Database
                .SqlQuery<int>(string.Format(SQL_YA_REGISTRADA, parametros), ids.Cast<object>().ToArray())
                .SingleAsync().ConfigureAwait(false);
            return encontrados > 0;
        }

        public static async Task Registrar(NVEntities db, string empresa, string tipoOrigen, IEnumerable<EvidenciaRecepcion> filas)
        {
            foreach (EvidenciaRecepcion fila in filas ?? Enumerable.Empty<EvidenciaRecepcion>())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_EVIDENCIA, fila.IdCliente, empresa, fila.NumeroOrigen, fila.Producto,
                    (short)Math.Min(fila.Cantidad, short.MaxValue), $"Recepción {fila.IdRecepcion}",
                    Recortar(UsuarioAuditoriaHelper.ParaAuditoria(fila.Usuario), 50), (object)Recortar(fila.Dispositivo, 50) ?? DBNull.Value, tipoOrigen)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>El IdCliente de la evidencia de un producto en una recepción: siempre el mismo (un reenvío se reconoce).</summary>
        public static Guid IdEvidencia(Guid idRecepcion, string producto)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] datos = idRecepcion.ToByteArray()
                    .Concat(System.Text.Encoding.UTF8.GetBytes(PlanificadorRecepcionCompra.Normalizar(producto) ?? string.Empty))
                    .ToArray();
                return new Guid(md5.ComputeHash(datos));
            }
        }

        /// <summary>Un error de SQL de la recepción, en palabras del almacén.</summary>
        internal static string Traducir(SqlException ex)
        {
            string mensaje = ex?.Message ?? string.Empty;
            if (mensaje.Contains("CK_PreparacionEscaneos"))
            {
                return "La base de datos todavía no admite la evidencia de las recepciones: falta lanzar el script " +
                    "Scripts/Issue559_Ariadna_RecepcionCompras.sql. No se ha recibido nada.";
            }
            return mensaje;
        }

        private static string Recortar(string texto, int maximo)
        {
            return texto == null ? null : (texto.Length > maximo ? texto.Substring(0, maximo) : texto);
        }
    }
}
