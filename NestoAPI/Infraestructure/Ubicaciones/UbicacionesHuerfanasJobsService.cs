using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Ubicaciones
{
    /// <summary>Una fila de Ubicaciones de un albarán que ya no existe.</summary>
    public class UbicacionHuerfana
    {
        public int NumeroOrden { get; set; }
        public string Producto { get; set; }
        public int? Pedido { get; set; }
        public int Albaran { get; set; }
        public string EmpresaAlbaran { get; set; }
        public int Cantidad { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>Lo que se avisa: una línea por albarán, pedido y producto.</summary>
    public class LineaUbicacionHuerfana
    {
        public int Albaran { get; set; }
        public int? Pedido { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>
    /// 11/11/26: vigilancia de ubicaciones huérfanas. Cada madrugada, de solo lectura, busca en Ubicaciones (ALG) filas con Estado &lt; 0
    /// creadas en los últimos 7 días (y nunca antes de <see cref="FECHA_ARRANQUE"/>) cuyo AlbaránVta no existe en CabAlbaránVta (en
    /// ninguna empresa): se deshizo un albarán y sus ubicaciones no volvieron (6 albaranes así en 60 días, tratados a mano). Si hay
    /// alguna, avisa a Dirección y Almacén en la campana de Nesto y por correo.
    /// <para>Rendimiento: Ubicaciones tiene ~3 millones de filas y no tiene índice por AlbaránVta ni por fecha. Las filas de un albarán
    /// las inserta el propio albarán, así que se entra por NºOrden (clave del índice agrupado, creciente): solo las del último
    /// <see cref="MARGEN_NUMERO_ORDEN"/> (unas 4-5 semanas). Medido en producción el 11/11/26: 181 lecturas lógicas en Ubicaciones y
    /// ~1.800 en CabAlbaránVta.</para>
    /// </summary>
    public class UbicacionesHuerfanasJobsService
    {
        public const string JOB_ID = "ubicaciones-huerfanas";
        /// <summary>Todos los días a las 4:30.</summary>
        public const string CRON = "30 4 * * *";
        public const string TIPO_NOTIFICACION = "UbicacionesHuerfanas";
        public const int DIAS = 7;
        /// <summary>Lo anterior ya está tratado: no se avisa de nada creado antes.</summary>
        public static readonly DateTime FECHA_ARRANQUE = new DateTime(2026, 11, 11);
        /// <summary>En unidades de NºOrden (no de filas): ~1.000.000 son unas 4-5 semanas de Ubicaciones; de sobra para 7 días.</summary>
        public const int MARGEN_NUMERO_ORDEN = 1000000;
        internal const string DOMINIO = "NUEVAVISION\\";
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";
        public const string TEXTO = "se deshizo un albarán y sus ubicaciones no volvieron";

        internal const string SQL = @"
SET NOCOUNT ON;
DECLARE @MinOrden int = (SELECT MAX(u.[NºOrden]) FROM dbo.Ubicaciones u WITH (NOLOCK)) - @Margen;
SELECT u.[NºOrden], RTRIM(u.[Número]), u.PedidoVta, u.[AlbaránVta], RTRIM(u.EmpresaAlbaranVta), u.Cantidad, u.[FechaCreación]
FROM dbo.Ubicaciones u WITH (NOLOCK)
WHERE u.[NºOrden] >= @MinOrden AND u.[Almacén] = 'ALG' AND u.Estado < 0 AND u.[AlbaránVta] IS NOT NULL AND u.[FechaCreación] >= @Desde
  AND NOT EXISTS (SELECT 1 FROM dbo.[CabAlbaránVta] c WITH (NOLOCK) WHERE c.[Número] = u.[AlbaránVta])
ORDER BY u.[NºOrden];";

        private readonly Func<DateTime, Task<List<UbicacionHuerfana>>> leer;
        private readonly Func<List<string>> usuarios;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly IServicioCorreoElectronico correo;

        public UbicacionesHuerfanasJobsService() : this(null)
        {
        }

        internal UbicacionesHuerfanasJobsService(Func<DateTime, Task<List<UbicacionHuerfana>>> leer, Func<List<string>> usuarios = null,
            IServicioNotificacionesPush notificaciones = null, IServicioCorreoElectronico correo = null)
        {
            this.leer = leer ?? LeerDeLaBD;
            this.usuarios = usuarios ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.DIRECCION)
                .Concat(MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN)).ToList());
            this.notificaciones = notificaciones ?? new ServicioNotificacionesPush();
            this.correo = correo ?? new ServicioCorreoElectronico();
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Comprobar()
        {
            _ = await new UbicacionesHuerfanasJobsService().ComprobarAsync(DateTime.Now).ConfigureAwait(false);
        }

        /// <summary>Desde cuándo se mira: los últimos 7 días, nunca antes de la fecha de arranque.</summary>
        internal static DateTime Desde(DateTime ahora)
        {
            DateTime hace = ahora.AddDays(-DIAS);
            return hace < FECHA_ARRANQUE ? FECHA_ARRANQUE : hace;
        }

        /// <summary>Cuántas líneas se han avisado (0 si no hay huérfanas). Nunca lanza.</summary>
        public async Task<int> ComprobarAsync(DateTime ahora)
        {
            try
            {
                DateTime desde = Desde(ahora);
                List<LineaUbicacionHuerfana> lineas = Agrupar((await leer(desde).ConfigureAwait(false) ?? new List<UbicacionHuerfana>())
                    .Where(u => u != null && u.FechaCreacion >= desde));
                if (lineas.Count == 0)
                {
                    return 0;
                }
                NotificacionPushDTO notificacion = CrearNotificacion(lineas);
                EnviarCorreo(notificacion);
                List<string> destinatarios = (usuarios() ?? new List<string>())
                    .Select(u => u?.Trim())
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string usuario in destinatarios)
                {
                    await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                return lineas.Count;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ubicaciones huérfanas] No se ha podido comprobar: {ex.Message}", ex), "Sistema (ubicaciones huérfanas)");
                return 0;
            }
        }

        /// <summary>Una línea por albarán, pedido y producto (cantidad en positivo), por albarán y producto.</summary>
        internal static List<LineaUbicacionHuerfana> Agrupar(IEnumerable<UbicacionHuerfana> filas) =>
            (filas ?? Enumerable.Empty<UbicacionHuerfana>())
                .Where(f => f != null)
                .GroupBy(f => new { f.Albaran, f.Pedido, Producto = f.Producto?.Trim() ?? string.Empty })
                .Select(g => new LineaUbicacionHuerfana
                {
                    Albaran = g.Key.Albaran,
                    Pedido = g.Key.Pedido,
                    Producto = g.Key.Producto,
                    Cantidad = Math.Abs(g.Sum(f => f.Cantidad))
                })
                .OrderBy(l => l.Albaran).ThenBy(l => l.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();

        internal static NotificacionPushDTO CrearNotificacion(List<LineaUbicacionHuerfana> lineas)
        {
            List<int> albaranes = lineas.Select(l => l.Albaran).Distinct().ToList();
            var cuerpo = new StringBuilder();
            _ = cuerpo.Append(albaranes.Count == 1
                ? $"En Algete hay ubicaciones del albarán {albaranes[0]}, que ya no existe: {TEXTO}. "
                : $"En Algete hay ubicaciones de {albaranes.Count} albaranes que ya no existen: {TEXTO}. ");
            _ = cuerpo.Append("Hay que devolverlas a su sitio (avise a Informática si no sabe cómo):");
            foreach (LineaUbicacionHuerfana linea in lineas)
            {
                _ = cuerpo.Append(Environment.NewLine)
                    .Append($"- Producto {linea.Producto}, pedido {(linea.Pedido.HasValue ? linea.Pedido.Value.ToString(CultureInfo.InvariantCulture) : "(sin pedido)")}, " +
                        $"albarán {linea.Albaran.ToString(CultureInfo.InvariantCulture)}, {linea.Cantidad.ToString(CultureInfo.InvariantCulture)} " +
                        $"{(linea.Cantidad == 1 ? "unidad" : "unidades")}");
            }
            return new NotificacionPushDTO
            {
                Titulo = albaranes.Count == 1 ? $"Ubicaciones huérfanas del albarán {albaranes[0]}" : $"Ubicaciones huérfanas de {albaranes.Count} albaranes",
                Cuerpo = cuerpo.ToString(),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["albaranes"] = string.Join(",", albaranes.Select(a => a.ToString(CultureInfo.InvariantCulture)))
                }
            };
        }

        private void EnviarCorreo(NotificacionPushDTO notificacion)
        {
            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(CORREO_REMITENTE),
                    Subject = notificacion.Titulo,
                    Body = notificacion.Cuerpo,
                    IsBodyHtml = false
                };
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_DIRECCION));
                mail.To.Add(new MailAddress(Constantes.Correos.ALMACEN));
                _ = correo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ubicaciones huérfanas] No se ha podido enviar el correo: {ex.Message}", ex), "Sistema (ubicaciones huérfanas)");
            }
        }

        private static async Task<List<UbicacionHuerfana>> LeerDeLaBD(DateTime desde)
        {
            var resultado = new List<UbicacionHuerfana>();
            using (var db = new NVEntities())
            using (var conexion = new SqlConnection(db.Database.Connection.ConnectionString))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand(SQL, conexion) { CommandTimeout = 120 })
                {
                    comando.Parameters.Add("@Margen", System.Data.SqlDbType.Int).Value = MARGEN_NUMERO_ORDEN;
                    comando.Parameters.Add("@Desde", System.Data.SqlDbType.DateTime).Value = desde;
                    using (SqlDataReader lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new UbicacionHuerfana
                            {
                                NumeroOrden = Convert.ToInt32(lector.GetValue(0)),
                                Producto = lector.IsDBNull(1) ? null : lector.GetString(1).Trim(),
                                Pedido = lector.IsDBNull(2) ? (int?)null : Convert.ToInt32(lector.GetValue(2)),
                                Albaran = Convert.ToInt32(lector.GetValue(3)),
                                EmpresaAlbaran = lector.IsDBNull(4) ? null : lector.GetString(4).Trim(),
                                Cantidad = lector.IsDBNull(5) ? 0 : Convert.ToInt32(lector.GetValue(5)),
                                FechaCreacion = lector.IsDBNull(6) ? DateTime.MinValue : lector.GetDateTime(6)
                            });
                        }
                    }
                }
            }
            return resultado;
        }
    }
}
