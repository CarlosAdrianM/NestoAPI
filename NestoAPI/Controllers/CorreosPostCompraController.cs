using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Endpoints para probar y gestionar correos post-compra con videos personalizados.
    /// Issue #74: Sistema de correos automáticos con videos personalizados post-compra.
    /// </summary>
    [RoutePrefix("api/CorreosPostCompra")]
    public class CorreosPostCompraController : ApiController
    {
        private readonly IServicioRecomendacionesPostCompra _servicioRecomendaciones;
        private readonly IGeneradorContenidoCorreoPostCompra _generadorContenido;
        private readonly IServicioCorreoElectronico _servicioCorreo;

        private const string EMAIL_PRUEBAS = "carlosadrian@nuevavision.es";

        public CorreosPostCompraController()
        {
            _servicioRecomendaciones = new ServicioRecomendacionesPostCompra();
            _generadorContenido = new GeneradorContenidoCorreoPostCompra(new ServicioOpenAI());
            _servicioCorreo = new ServicioCorreoElectronico();
        }

        // Constructor para inyección de dependencias / testing
        public CorreosPostCompraController(
            IServicioRecomendacionesPostCompra servicioRecomendaciones,
            IGeneradorContenidoCorreoPostCompra generadorContenido,
            IServicioCorreoElectronico servicioCorreo = null)
        {
            _servicioRecomendaciones = servicioRecomendaciones;
            _generadorContenido = generadorContenido;
            _servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
        }

        /// <summary>
        /// Obtiene las recomendaciones de videos y productos para un pedido.
        /// Útil para ver qué datos se enviarían en el correo sin generar el contenido.
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedido">Número de pedido</param>
        /// <returns>Videos recomendados con productos (marcados como comprados o no)</returns>
        [HttpGet]
        [Route("Recomendaciones")]
        [ResponseType(typeof(RecomendacionPostCompraDTO))]
        public async Task<IHttpActionResult> GetRecomendaciones(string empresa, int pedido)
        {
            var recomendaciones = await _servicioRecomendaciones.ObtenerRecomendaciones(empresa, pedido).ConfigureAwait(false);

            if (recomendaciones == null)
            {
                return NotFound();
            }

            return Ok(recomendaciones);
        }

        /// <summary>
        /// Genera una vista previa del correo HTML que se enviaría al cliente.
        /// NO envía el correo, solo devuelve el HTML generado por OpenAI.
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedido">Número de pedido</param>
        /// <returns>HTML del correo generado</returns>
        [HttpGet]
        [Route("Preview")]
        [ResponseType(typeof(PreviewCorreoDTO))]
        public async Task<IHttpActionResult> GetPreviewCorreo(string empresa, int pedido)
        {
            var recomendaciones = await _servicioRecomendaciones.ObtenerRecomendaciones(empresa, pedido).ConfigureAwait(false);

            if (recomendaciones == null)
            {
                return NotFound();
            }

            if (recomendaciones.Videos == null || recomendaciones.Videos.Count == 0)
            {
                return Ok(new PreviewCorreoDTO
                {
                    ClienteNombre = recomendaciones.ClienteNombre,
                    ClienteEmail = recomendaciones.ClienteEmail,
                    PedidoNumero = recomendaciones.PedidoNumero,
                    HtmlGenerado = null,
                    Mensaje = "No hay videos disponibles para los productos de este pedido"
                });
            }

            var htmlGenerado = await _generadorContenido.GenerarContenidoHtml(recomendaciones).ConfigureAwait(false);

            return Ok(new PreviewCorreoDTO
            {
                ClienteNombre = recomendaciones.ClienteNombre,
                ClienteEmail = recomendaciones.ClienteEmail,
                PedidoNumero = recomendaciones.PedidoNumero,
                VideosIncluidos = recomendaciones.Videos.Count,
                ProductosComprados = recomendaciones.Videos.Count > 0
                    ? recomendaciones.Videos[0].ProductosComprados
                    : 0,
                ProductosSugeridos = recomendaciones.Videos.Count > 0
                    ? recomendaciones.Videos[0].ProductosNoComprados
                    : 0,
                HtmlGenerado = htmlGenerado,
                Mensaje = htmlGenerado != null ? "Correo generado correctamente" : "Error al generar el contenido"
            });
        }

        /// <summary>
        /// ENDPOINT DE PRUEBA: Genera y envía el correo a carlosadrian@nuevavision.es
        /// para poder ver cómo quedaría el correo real que recibiría el cliente.
        /// NO envía al cliente, solo a la dirección de pruebas.
        /// Usa GET para poder probarlo fácilmente desde el navegador.
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="pedido">Número de pedido</param>
        /// <returns>Resultado del envío</returns>
        [HttpGet]
        [Route("EnviarPrueba")]
        [ResponseType(typeof(ResultadoEnvioPruebaDTO))]
        public async Task<IHttpActionResult> GetEnviarPrueba(string empresa, int pedido)
        {
            var recomendaciones = await _servicioRecomendaciones.ObtenerRecomendaciones(empresa, pedido).ConfigureAwait(false);

            if (recomendaciones == null)
            {
                return NotFound();
            }

            if (recomendaciones.Videos == null || recomendaciones.Videos.Count == 0)
            {
                return Ok(new ResultadoEnvioPruebaDTO
                {
                    Enviado = false,
                    Mensaje = "No hay videos disponibles para los productos de este pedido",
                    EmailDestino = EMAIL_PRUEBAS
                });
            }

            // Generar el HTML del correo
            var htmlGenerado = await _generadorContenido.GenerarContenidoHtml(recomendaciones).ConfigureAwait(false);

            if (string.IsNullOrEmpty(htmlGenerado))
            {
                return Ok(new ResultadoEnvioPruebaDTO
                {
                    Enviado = false,
                    Mensaje = "Error al generar el contenido HTML con OpenAI",
                    EmailDestino = EMAIL_PRUEBAS
                });
            }

            // Construir el correo
            string asunto = $"[PRUEBA] Saca el máximo partido a tu compra, {recomendaciones.ClienteNombre}";

            // Envolver el HTML en una estructura completa de email
            string htmlCompleto = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{asunto}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif;"">
    <!-- DATOS DE PRUEBA -->
    <div style=""background-color: #fff3cd; padding: 10px; margin-bottom: 20px; border: 1px solid #ffc107;"">
        <strong>CORREO DE PRUEBA</strong><br/>
        Cliente original: {recomendaciones.ClienteNombre} ({recomendaciones.ClienteEmail})<br/>
        Pedido: {recomendaciones.PedidoNumero}<br/>
        Videos incluidos: {recomendaciones.Videos.Count}
    </div>
    <!-- CONTENIDO DEL CORREO -->
    {htmlGenerado}
</body>
</html>";

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("nesto@nuevavision.es", "Nueva Visión");
                mail.To.Add(EMAIL_PRUEBAS);
                mail.Subject = asunto;
                mail.Body = htmlCompleto;
                mail.IsBodyHtml = true;

                bool enviado = _servicioCorreo.EnviarCorreoSMTP(mail);

                return Ok(new ResultadoEnvioPruebaDTO
                {
                    Enviado = enviado,
                    Mensaje = enviado
                        ? $"Correo enviado correctamente a {EMAIL_PRUEBAS}"
                        : "Error al enviar el correo por SMTP",
                    EmailDestino = EMAIL_PRUEBAS,
                    ClienteOriginal = recomendaciones.ClienteNombre,
                    ClienteEmailOriginal = recomendaciones.ClienteEmail,
                    PedidoNumero = recomendaciones.PedidoNumero,
                    VideosIncluidos = recomendaciones.Videos.Count,
                    Asunto = asunto
                });
            }
        }
    }

    public class ResultadoEnvioPruebaDTO
    {
        public bool Enviado { get; set; }
        public string Mensaje { get; set; }
        public string EmailDestino { get; set; }
        public string ClienteOriginal { get; set; }
        public string ClienteEmailOriginal { get; set; }
        public int PedidoNumero { get; set; }
        public int VideosIncluidos { get; set; }
        public string Asunto { get; set; }
    }

    public class PreviewCorreoDTO
    {
        public string ClienteNombre { get; set; }
        public string ClienteEmail { get; set; }
        public int PedidoNumero { get; set; }
        public int VideosIncluidos { get; set; }
        public int ProductosComprados { get; set; }
        public int ProductosSugeridos { get; set; }
        public string HtmlGenerado { get; set; }
        public string Mensaje { get; set; }
    }
}
