using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>NestoAPI#866 (corte 3): lo que se apunta de cada sugerencia recién registrada, además de lo que guarda EF.</summary>
    public class DatosSugerenciaRegistrada
    {
        /// <summary>Id de la fila de SugerenciasContacto (ya guardada).</summary>
        public int Id { get; set; }
        /// <summary>Por qué entró (<see cref="OrigenesSugerenciaContacto"/>).</summary>
        public string Origen { get; set; }
        /// <summary>JSON corto de <c>ReglasImpulsoOfertasContacto.ProductosJson</c>, o null.</summary>
        public string ProductosOferta { get; set; }
    }

    /// <summary>
    /// NestoAPI#866 (corte 3): las columnas opcionales de dbo.SugerenciasContacto (Scripts/Issue866_SugerenciasContactoOfertas.sql).
    /// No están en el EDMX: se escriben con SQL plano y, sin el script, no se hace nada.
    /// </summary>
    public interface IDatosSugerenciasContacto
    {
        /// <summary>Apunta el origen y los productos en oferta de las sugerencias recién registradas. Nunca lanza.</summary>
        void GuardarAlRegistrar(IList<DatosSugerenciaRegistrada> filas);
    }

    public class DatosSugerenciasContactoSql : IDatosSugerenciasContacto
    {
        public const string TABLA = "SugerenciasContacto";
        internal static readonly string[] COLUMNAS_REGISTRO = { "Origen", "ProductosOferta" };
        internal static readonly string[] COLUMNAS_RESULTADO = { "PedidoEn7Dias", "PedidoConOferta", "ImportePedido7Dias", "ResultadoCalculadoEl" };
        public const int LONGITUD_ORIGEN = 20;
        public const int LONGITUD_PRODUCTOS = 400;
        /// <summary>Filas por UPDATE (3 parámetros por fila: lejos del tope de 2100 de SQL Server).</summary>
        internal const int LOTE = 300;

        private readonly NVEntities db;

        public DatosSugerenciasContactoSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public void GuardarAlRegistrar(IList<DatosSugerenciaRegistrada> filas)
        {
            List<DatosSugerenciaRegistrada> validas = (filas ?? new List<DatosSugerenciaRegistrada>()).Where(f => f != null && f.Id > 0).ToList();
            if (!validas.Any())
            {
                return;
            }
            try
            {
                if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNAS_REGISTRO))
                {
                    return;
                }
                for (int i = 0; i < validas.Count; i += LOTE)
                {
                    List<DatosSugerenciaRegistrada> lote = validas.Skip(i).Take(LOTE).ToList();
                    _ = db.Database.ExecuteSqlCommand(SqlActualizar(lote.Count), Parametros(lote));
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#866: no se ha podido apuntar el origen y los productos en oferta de {validas.Count} sugerencias de contacto (la lista sale igual): {ex.Message}", ex));
            }
        }

        /// <summary>UN UPDATE con un VALUES de <paramref name="filas"/> filas (@p0, @p1, @p2 = Id, Origen, ProductosOferta de la primera…).</summary>
        internal static string SqlActualizar(int filas)
        {
            var valores = new StringBuilder();
            for (int i = 0; i < filas; i++)
            {
                _ = valores.Append(i == 0 ? string.Empty : ", ").Append($"(@p{3 * i}, @p{3 * i + 1}, @p{3 * i + 2})");
            }
            return "UPDATE s SET s.Origen = v.Origen, s.ProductosOferta = v.ProductosOferta FROM dbo.SugerenciasContacto s " +
                $"INNER JOIN (VALUES {valores}) v (Id, Origen, ProductosOferta) ON s.Id = v.Id;";
        }

        internal static object[] Parametros(IList<DatosSugerenciaRegistrada> filas)
        {
            var parametros = new List<object>(filas.Count * 3);
            for (int i = 0; i < filas.Count; i++)
            {
                DatosSugerenciaRegistrada f = filas[i];
                parametros.Add(new SqlParameter($"@p{3 * i}", f.Id));
                parametros.Add(new SqlParameter($"@p{3 * i + 1}", System.Data.SqlDbType.VarChar, LONGITUD_ORIGEN) { Value = (object)Recortar(f.Origen, LONGITUD_ORIGEN) ?? DBNull.Value });
                parametros.Add(new SqlParameter($"@p{3 * i + 2}", System.Data.SqlDbType.NVarChar, LONGITUD_PRODUCTOS) { Value = (object)Recortar(f.ProductosOferta, LONGITUD_PRODUCTOS) ?? DBNull.Value });
            }
            return parametros.ToArray();
        }

        private static string Recortar(string texto, int longitud)
            => string.IsNullOrWhiteSpace(texto) ? null : (texto.Length > longitud ? texto.Substring(0, longitud) : texto);
    }

    /// <summary>
    /// NestoAPI#866 (corte 3): cierra cada sugerencia cuando ya ha pasado su ventana de 7 días: si el cliente/contacto hizo un pedido
    /// entre el día de la sugerencia y 7 días después (incluidos; «pedido» como el modelo: empresa 1, TipoLinea 1, base &gt; 0, sin
    /// nota de entrega y Estado ≥ -1), su importe y, si la sugerencia llevaba productos en oferta, si el pedido llevaba alguno.
    /// Todo en UN UPDATE por pasada (sin N+1). Vale para todas las sugerencias, sea cual sea su origen: así se compara la
    /// conversión a pedido de cada origen, no solo la «atendida» (rapport del día), que ya existía.
    /// </summary>
    public static class SugerenciasContactoResultadoJobsService
    {
        public const string JOB_ID = "sugerencias-contacto-resultado";
        /// <summary>Todos los días a las 3:25 (Madrid).</summary>
        public const string CRON = "25 3 * * *";
        /// <summary>La ventana: el día de la sugerencia y los 7 siguientes.</summary>
        public const int DIAS_VENTANA = 7;
        /// <summary>Las de hace más de esto no se cierran (antes del script no había nada que cerrar; así la pasada es acotada).</summary>
        public const int DIAS_MAXIMO = 60;
        private const string USUARIO_ELMAH = "Sistema (sugerencias de contacto)";

        internal const string SQL_CERRAR = @"
SET NOCOUNT ON;
DECLARE @Desde date = DATEADD(day, -@DiasMaximo, @Hoy);
DECLARE @Hasta date = DATEADD(day, -@DiasVentana, @Hoy);
UPDATE s SET
    s.PedidoEn7Dias = CASE WHEN ISNULL(p.Pedidos, 0) > 0 THEN 1 ELSE 0 END,
    s.ImportePedido7Dias = ISNULL(p.Importe, 0),
    s.PedidoConOferta = CASE WHEN s.ProductosOferta IS NULL THEN NULL WHEN o.Hay = 1 THEN 1 ELSE 0 END,
    s.ResultadoCalculadoEl = GETDATE()
FROM dbo.SugerenciasContacto s
OUTER APPLY (
    SELECT COUNT(DISTINCT l.Número) Pedidos, SUM(l.[Base Imponible]) Importe
    FROM LinPedidoVta l WITH (NOLOCK)
    INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
    WHERE l.Empresa = '1' AND l.[Nº Cliente] = s.Cliente AND l.Contacto = s.Contacto
        AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
        AND cab.Fecha >= CAST(s.Fecha AS date) AND cab.Fecha < DATEADD(day, @DiasVentana + 1, CAST(s.Fecha AS date))
) p
OUTER APPLY (
    SELECT TOP 1 1 Hay
    FROM LinPedidoVta l WITH (NOLOCK)
    INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
    WHERE s.ProductosOferta IS NOT NULL AND l.Empresa = '1' AND l.[Nº Cliente] = s.Cliente AND l.Contacto = s.Contacto
        AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
        AND cab.Fecha >= CAST(s.Fecha AS date) AND cab.Fecha < DATEADD(day, @DiasVentana + 1, CAST(s.Fecha AS date))
        AND s.ProductosOferta LIKE N'%""p"":""' + RTRIM(l.Producto) + N'""%'
) o
WHERE s.ResultadoCalculadoEl IS NULL AND s.Fecha >= @Desde AND s.Fecha < @Hasta;
SELECT @@ROWCOUNT;";

        private static bool sinColumnasAvisado;

        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void Procesar()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    int cerradas = Cerrar(db, DateTime.Today);
                    Console.WriteLine($"[Sugerencias de contacto #866] {cerradas} sugerencias cerradas con su resultado a 7 días");
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Sugerencias de contacto #866] Error al cerrar las sugerencias con su resultado: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }

        /// <summary>Las cierra y devuelve cuántas. Sin las columnas, nada (ELMAH una vez por proceso).</summary>
        internal static int Cerrar(NVEntities db, DateTime hoy)
        {
            if (!ColumnasOpcionales.Existen(db, DatosSugerenciasContactoSql.TABLA,
                DatosSugerenciasContactoSql.COLUMNAS_REGISTRO.Concat(DatosSugerenciasContactoSql.COLUMNAS_RESULTADO).ToArray()))
            {
                if (!sinColumnasAvisado)
                {
                    sinColumnasAvisado = true;
                    ElmahHelper.Log(new Exception("[Sugerencias de contacto #866] Sin las columnas de Scripts/Issue866_SugerenciasContactoOfertas.sql: no se cierra ninguna sugerencia (se avisa una vez)."), USUARIO_ELMAH);
                }
                return 0;
            }
            sinColumnasAvisado = false;
            db.Database.CommandTimeout = 600;
            return db.Database.SqlQuery<int>(SQL_CERRAR,
                new SqlParameter("@Hoy", System.Data.SqlDbType.Date) { Value = hoy.Date },
                new SqlParameter("@DiasVentana", DIAS_VENTANA),
                new SqlParameter("@DiasMaximo", DIAS_MAXIMO)).FirstOrDefault();
        }
    }
}
