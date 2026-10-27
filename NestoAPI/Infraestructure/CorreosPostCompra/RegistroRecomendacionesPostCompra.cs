using Elmah;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#707: tipos de fila de CorreosPostCompraRecomendaciones (CK de la tabla).</summary>
    public static class TiposRecomendacionPostCompra
    {
        public const string COMPRADO = "Comprado";
        public const string RECOMENDADO = "Recomendado";
    }

    /// <summary>NestoAPI#707: una fila de CorreosPostCompraRecomendaciones (un producto de un correo enviado).</summary>
    public class FilaRecomendacionPostCompra
    {
        public DateTime FechaEnvio { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Correo { get; set; }
        public string Producto { get; set; }
        /// <summary><see cref="TiposRecomendacionPostCompra"/>.</summary>
        public string Tipo { get; set; }
        /// <summary>1, 2, 3… dentro de su bloque (comprados o recomendados), en el orden en que salen en el correo.</summary>
        public int Posicion { get; set; }
        /// <summary>Solo en los recomendados: pares, afinidad, popularidad u orden_original.</summary>
        public string Regla { get; set; }
        public decimal? Puntuacion { get; set; }
        public string VersionModelo { get; set; }
        /// <summary>Todas las filas de un cliente del grupo control (sus recomendados NO salieron en el correo).</summary>
        public bool GrupoControl { get; set; }
    }

    /// <summary>NestoAPI#707: la tabla CorreosPostCompraRecomendaciones (script Issue707) y la lectura del informe de conversión.</summary>
    public interface IRepositorioRecomendacionesPostCompra
    {
        /// <summary>¿Se ha lanzado Scripts/Issue707_CorreosPostCompraRecomendaciones.sql?</summary>
        Task<bool> TablaExiste();
        Task Insertar(IList<FilaRecomendacionPostCompra> filas);
        /// <summary>
        /// Las filas enviadas entre <paramref name="desde"/> (incluido) y <paramref name="hasta"/> (excluido), cada una con lo
        /// que el cliente compró de ese producto en los <see cref="InformeConversionPostCompra.DIAS_VENTANA"/> días siguientes.
        /// Vacía sin la tabla.
        /// </summary>
        Task<List<FilaConversionPostCompra>> LeerConversion(DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#707: SQL a mano, fuera del EDMX, como las tablas recientes (PedidosPromesaRiesgo #693, sugerencias #670): si el
    /// script no se ha lanzado, la API no se cae; simplemente no registra nada.
    /// </summary>
    public class RepositorioRecomendacionesPostCompraSql : IRepositorioRecomendacionesPostCompra
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.CorreosPostCompraRecomendaciones', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string COLUMNAS_INSERTAR = "FechaEnvio, Empresa, Cliente, Contacto, Correo, Producto, Tipo, Posicion, Regla, Puntuacion, VersionModelo, GrupoControl";

        // Cada fila con lo que ese cliente compró de ese producto: pedidos (CabPedidoVta.Fecha) desde el día del envío y durante
        // DIAS_VENTANA días, en cualquier estado desde pendiente (sin presupuestos, -3), solo líneas de producto con cantidad
        // positiva (sin devoluciones). El cruce con LinPedidoVta va por cliente y producto (índices por Nº Cliente o Producto).
        internal static readonly string SQL_CONVERSION = @"
SELECT r.FechaEnvio, RTRIM(r.Empresa) AS Empresa, RTRIM(r.Cliente) AS Cliente, RTRIM(r.Producto) AS Producto, r.Tipo, r.Regla,
       r.GrupoControl, ISNULL(c.Lineas, 0) AS Lineas, ISNULL(c.Unidades, 0) AS Unidades, ISNULL(c.BaseImponible, 0) AS BaseImponible
FROM dbo.CorreosPostCompraRecomendaciones r WITH (NOLOCK)
OUTER APPLY (
    SELECT COUNT(*) AS Lineas, SUM(CAST(l.Cantidad AS int)) AS Unidades, CAST(SUM(l.[Base Imponible]) AS decimal(19, 4)) AS BaseImponible
    FROM dbo.LinPedidoVta l WITH (NOLOCK)
    INNER JOIN dbo.CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
    WHERE l.Empresa = r.Empresa AND l.[Nº Cliente] = r.Cliente AND l.Producto = r.Producto
      AND l.TipoLinea = 1 AND l.Estado >= " + Constantes.EstadosLineaVenta.PENDIENTE + @" AND l.Cantidad > 0
      AND cab.Fecha >= CAST(CAST(r.FechaEnvio AS date) AS datetime)
      AND cab.Fecha < DATEADD(day, " + InformeConversionPostCompra.DIAS_VENTANA + @", CAST(CAST(r.FechaEnvio AS date) AS datetime))
) c
WHERE r.FechaEnvio >= @desde AND r.FechaEnvio < @hasta";

        public async Task<bool> TablaExiste()
        {
            using (var db = new NVEntities())
            {
                return await db.Database.SqlQuery<bool>(SQL_EXISTE).FirstAsync().ConfigureAwait(false);
            }
        }

        public async Task Insertar(IList<FilaRecomendacionPostCompra> filas)
        {
            if (filas == null || filas.Count == 0)
            {
                return;
            }
            var sql = new StringBuilder("INSERT INTO dbo.CorreosPostCompraRecomendaciones (" + COLUMNAS_INSERTAR + ") VALUES ");
            var parametros = new List<SqlParameter>();
            for (int i = 0; i < filas.Count; i++)
            {
                FilaRecomendacionPostCompra f = filas[i];
                if (i > 0)
                {
                    sql.Append(", ");
                }
                sql.Append($"(@f{i}, @e{i}, @c{i}, @k{i}, @m{i}, @p{i}, @t{i}, @o{i}, @r{i}, @s{i}, @v{i}, @g{i})");
                parametros.Add(new SqlParameter("@f" + i, SqlDbType.DateTime) { Value = f.FechaEnvio });
                parametros.Add(Texto("@e" + i, SqlDbType.Char, f.Empresa, 3));
                parametros.Add(Texto("@c" + i, SqlDbType.VarChar, f.Cliente, 10));
                parametros.Add(Texto("@k" + i, SqlDbType.VarChar, f.Contacto, 3));
                parametros.Add(Texto("@m" + i, SqlDbType.NVarChar, f.Correo, 200));
                parametros.Add(Texto("@p" + i, SqlDbType.VarChar, f.Producto, 15));
                parametros.Add(Texto("@t" + i, SqlDbType.VarChar, f.Tipo, 12));
                parametros.Add(new SqlParameter("@o" + i, SqlDbType.TinyInt) { Value = (byte)Math.Max(0, Math.Min(255, f.Posicion)) });
                parametros.Add(Texto("@r" + i, SqlDbType.VarChar, f.Regla, 30));
                parametros.Add(new SqlParameter("@s" + i, SqlDbType.Decimal) { Precision = 9, Scale = 4, Value = (object)f.Puntuacion ?? DBNull.Value });
                parametros.Add(Texto("@v" + i, SqlDbType.VarChar, f.VersionModelo, 30));
                parametros.Add(new SqlParameter("@g" + i, SqlDbType.Bit) { Value = f.GrupoControl });
            }
            using (var db = new NVEntities())
            {
                await db.Database.ExecuteSqlCommandAsync(sql.ToString(), parametros.Cast<object>().ToArray()).ConfigureAwait(false);
            }
        }

        public async Task<List<FilaConversionPostCompra>> LeerConversion(DateTime desde, DateTime hasta)
        {
            using (var db = new NVEntities())
            {
                bool existe = await db.Database.SqlQuery<bool>(SQL_EXISTE).FirstAsync().ConfigureAwait(false);
                if (!existe)
                {
                    return new List<FilaConversionPostCompra>();
                }
                return await db.Database.SqlQuery<FilaConversionPostCompra>(SQL_CONVERSION,
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@hasta", SqlDbType.DateTime) { Value = hasta }).ToListAsync().ConfigureAwait(false);
            }
        }

        private static SqlParameter Texto(string nombre, SqlDbType tipo, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (limpio != null && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            return new SqlParameter(nombre, tipo, largo) { Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio };
        }
    }

    /// <summary>
    /// NestoAPI#707 (corte 1): guarda en CorreosPostCompraRecomendaciones lo que de verdad lleva cada correo post-compra
    /// enviado: los comprados y los recomendados que salen (los mismos que pinta AplicarPlantilla: con enlace al vídeo y a la
    /// tienda) y, a los del grupo control, los recomendados que habrían salido. Best-effort: sin tabla o con un fallo al
    /// insertar, un aviso a ELMAH (uno por proceso, no uno por correo) y el correo sale igual.
    /// </summary>
    public static class RegistroRecomendacionesPostCompra
    {
        private static int _avisadoSinTabla;
        private static int _avisadoFallo;

        /// <summary>Las filas de un correo: los comprados (Posicion 1..n) y los recomendados (1..m) que salen o, en el grupo control, saldrían.</summary>
        public static List<FilaRecomendacionPostCompra> ConstruirFilas(CorreoPostCompraClienteDTO datos, DateTime fechaEnvio)
        {
            var filas = new List<FilaRecomendacionPostCompra>();
            if (datos == null || string.IsNullOrWhiteSpace(datos.ClienteId))
            {
                return filas;
            }
            string empresa = string.IsNullOrWhiteSpace(datos.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : datos.Empresa.Trim();

            FilaRecomendacionPostCompra Fila(string producto, string tipo, int posicion) => new FilaRecomendacionPostCompra
            {
                FechaEnvio = fechaEnvio,
                Empresa = empresa,
                Cliente = datos.ClienteId.Trim(),
                Contacto = datos.ClienteContacto?.Trim(),
                Correo = datos.ClienteEmail?.Trim(),
                Producto = producto.Trim(),
                Tipo = tipo,
                Posicion = posicion,
                GrupoControl = datos.GrupoControl
            };

            int posicionComprado = 0;
            foreach (ProductoCompradoConVideoDTO comprado in datos.ProductosComprados ?? new List<ProductoCompradoConVideoDTO>())
            {
                if (string.IsNullOrWhiteSpace(comprado?.ProductoId))
                {
                    continue;
                }
                filas.Add(Fila(comprado.ProductoId, TiposRecomendacionPostCompra.COMPRADO, ++posicionComprado));
            }

            IEnumerable<ProductoRecomendadoDTO> recomendados = datos.GrupoControl
                ? datos.ProductosRecomendadosNoMostrados
                : datos.ProductosRecomendados;
            int posicionRecomendado = 0;
            foreach (ProductoRecomendadoDTO recomendado in GeneradorContenidoCorreoPostCompra.RecomendadosConEnlaces(recomendados)
                ?? new List<ProductoRecomendadoDTO>())
            {
                if (string.IsNullOrWhiteSpace(recomendado?.ProductoId))
                {
                    continue;
                }
                FilaRecomendacionPostCompra fila = Fila(recomendado.ProductoId, TiposRecomendacionPostCompra.RECOMENDADO, ++posicionRecomendado);
                // Los programados antes de #707 no traen Regla: salieron en el orden de la consulta
                fila.Regla = string.IsNullOrWhiteSpace(recomendado.Regla) ? PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL : recomendado.Regla.Trim();
                fila.Puntuacion = recomendado.Puntuacion;
                fila.VersionModelo = datos.VersionModelo?.Trim();
                filas.Add(fila);
            }
            return filas;
        }

        /// <summary>
        /// Guarda las filas del correo. Nunca lanza: devuelve true si las ha guardado. <paramref name="registrarError"/> es para
        /// los tests (por defecto, ELMAH).
        /// </summary>
        public static async Task<bool> Registrar(IRepositorioRecomendacionesPostCompra repositorio, CorreoPostCompraClienteDTO datos,
            DateTime fechaEnvio, Action<Exception> registrarError = null)
        {
            Action<Exception> avisar = registrarError ?? (ex => ErrorLog.GetDefault(null)?.Log(new Error(ex)));
            try
            {
                List<FilaRecomendacionPostCompra> filas = ConstruirFilas(datos, fechaEnvio);
                if (filas.Count == 0 || repositorio == null)
                {
                    return false;
                }
                if (!await repositorio.TablaExiste().ConfigureAwait(false))
                {
                    if (registrarError != null || Interlocked.Exchange(ref _avisadoSinTabla, 1) == 0)
                    {
                        avisar(new Exception("[CorreosPostCompra] NestoAPI#707: no existe la tabla CorreosPostCompraRecomendaciones " +
                            "(falta lanzar Scripts/Issue707_CorreosPostCompraRecomendaciones.sql). Los correos salen, pero no se guarda qué llevaban."));
                    }
                    return false;
                }
                await repositorio.Insertar(filas).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    if (registrarError != null || Interlocked.Exchange(ref _avisadoFallo, 1) == 0)
                    {
                        avisar(new Exception($"[CorreosPostCompra] NestoAPI#707: no se han podido guardar las recomendaciones enviadas " +
                            $"(cliente {datos?.ClienteId}). El correo ha salido igual. {ex.Message}", ex));
                    }
                }
                catch
                {
                    // Ni el aviso puede cortar el envío
                }
                return false;
            }
        }
    }
}
