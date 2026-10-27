using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public CorreosPostCompraController(
            IServicioRecomendacionesPostCompra servicioRecomendaciones,
            IGeneradorContenidoCorreoPostCompra generadorContenido,
            IServicioCorreoElectronico servicioCorreo = null)
        {
            _servicioRecomendaciones = servicioRecomendaciones;
            _generadorContenido = generadorContenido;
            _servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
        }

        /// <summary>NestoAPI#707: de donde lee el informe de conversión. Para tests.</summary>
        internal IRepositorioRecomendacionesPostCompra RepositorioRecomendaciones { get; set; } = new RepositorioRecomendacionesPostCompraSql();

        /// <summary>NestoAPI#707: hoy (para la ventana por defecto y saber qué ventanas están cerradas). Para tests.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        /// <summary>
        /// NestoAPI#707: conversión del correo post-compra. Por semana del envío, tipo (Comprado/Recomendado), regla y grupo
        /// control: productos enviados, clientes, cuántos compró ese cliente en los 30 días siguientes (pedidos desde el día
        /// del envío, sin presupuestos), clientes compradores, unidades y base imponible; y los totales del periodo para comparar
        /// enviados con grupo control. Sin <paramref name="desde"/>, las últimas 12 semanas; sin <paramref name="hasta"/>, hasta
        /// hoy incluido. Vacío si aún no se ha lanzado Scripts/Issue707_CorreosPostCompraRecomendaciones.sql. Solo Dirección e
        /// Informática.
        /// GET api/CorreosPostCompra/Conversion?desde=2026-11-01
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("Conversion")]
        [ResponseType(typeof(InformeConversionPostCompraDTO))]
        public async Task<IHttpActionResult> GetConversion(DateTime? desde = null, DateTime? hasta = null)
        {
            if (User == null || !(User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            DateTime hoy = Hoy().Date;
            DateTime inicio = (desde ?? hoy.AddDays(-7 * InformeConversionPostCompra.SEMANAS_POR_DEFECTO)).Date;
            DateTime fin = (hasta?.Date ?? hoy).AddDays(1); // exclusivo
            if (fin <= inicio)
            {
                return BadRequest("La fecha «hasta» no puede ser anterior a «desde».");
            }
            List<FilaConversionPostCompra> filas = await RepositorioRecomendaciones.LeerConversion(inicio, fin).ConfigureAwait(false);
            return Ok(InformeConversionPostCompra.Calcular(filas, inicio, fin, hoy));
        }

        /// <summary>
        /// Obtiene los correos post-compra que se generarían para la semana especificada.
        /// Si no se indican fechas, usa los últimos 7 días.
        /// </summary>
        [HttpGet]
        [Route("Recomendaciones")]
        [ResponseType(typeof(List<CorreoPostCompraClienteDTO>))]
        public async Task<IHttpActionResult> GetRecomendaciones(
            string empresa = "1",
            string fechaDesde = null,
            string fechaHasta = null)
        {
            DateTime desde = string.IsNullOrEmpty(fechaDesde)
                ? DateTime.Today.AddDays(-7)
                : DateTime.Parse(fechaDesde);
            DateTime hasta = string.IsNullOrEmpty(fechaHasta)
                ? DateTime.Today
                : DateTime.Parse(fechaHasta);

            var correos = await _servicioRecomendaciones
                .ObtenerCorreosSemana(empresa, desde, hasta)
                .ConfigureAwait(false);

            return Ok(correos);
        }

        /// <summary>
        /// Genera una vista previa del correo HTML para un cliente específico.
        /// </summary>
        [HttpGet]
        [Route("Preview")]
        [ResponseType(typeof(PreviewCorreoDTO))]
        public async Task<IHttpActionResult> GetPreviewCorreo(
            string empresa = "1",
            string cliente = null,
            string fechaDesde = null,
            string fechaHasta = null)
        {
            if (string.IsNullOrEmpty(cliente))
            {
                return BadRequest("El parámetro 'cliente' es obligatorio");
            }

            DateTime desde = string.IsNullOrEmpty(fechaDesde)
                ? DateTime.Today.AddDays(-7)
                : DateTime.Parse(fechaDesde);
            DateTime hasta = string.IsNullOrEmpty(fechaHasta)
                ? DateTime.Today
                : DateTime.Parse(fechaHasta);

            var correos = await _servicioRecomendaciones
                .ObtenerCorreosSemana(empresa, desde, hasta)
                .ConfigureAwait(false);

            var correoCliente = correos?.FirstOrDefault(c =>
                c.ClienteId?.Trim() == cliente.Trim());

            if (correoCliente == null)
            {
                return Ok(new PreviewCorreoDTO
                {
                    ClienteId = cliente,
                    HtmlGenerado = null,
                    Mensaje = "No se encontraron datos para este cliente en el rango de fechas indicado"
                });
            }

            var htmlGenerado = await _generadorContenido
                .GenerarContenidoHtml(correoCliente)
                .ConfigureAwait(false);

            return Ok(new PreviewCorreoDTO
            {
                ClienteId = correoCliente.ClienteId,
                ClienteNombre = correoCliente.ClienteNombre,
                ClienteEmail = correoCliente.ClienteEmail,
                ProductosComprados = correoCliente.ProductosComprados?.Count ?? 0,
                ProductosRecomendados = correoCliente.ProductosRecomendados?.Count ?? 0,
                HtmlGenerado = htmlGenerado,
                Mensaje = htmlGenerado != null
                    ? "Correo generado correctamente"
                    : "Error al generar el contenido"
            });
        }

        /// <summary>
        /// ENDPOINT DE PRUEBA: Genera y envía el correo a carlosadrian@nuevavision.es
        /// para un cliente específico en el rango de fechas indicado.
        /// </summary>
        [HttpGet]
        [Route("EnviarPrueba")]
        [ResponseType(typeof(ResultadoEnvioPruebaDTO))]
        public async Task<IHttpActionResult> GetEnviarPrueba(
            string empresa = "1",
            string cliente = null,
            string fechaDesde = null,
            string fechaHasta = null)
        {
            if (string.IsNullOrEmpty(cliente))
            {
                return BadRequest("El parámetro 'cliente' es obligatorio");
            }

            DateTime desde = string.IsNullOrEmpty(fechaDesde)
                ? DateTime.Today.AddDays(-7)
                : DateTime.Parse(fechaDesde);
            DateTime hasta = string.IsNullOrEmpty(fechaHasta)
                ? DateTime.Today
                : DateTime.Parse(fechaHasta);

            var correos = await _servicioRecomendaciones
                .ObtenerCorreosSemana(empresa, desde, hasta)
                .ConfigureAwait(false);

            var correoCliente = correos?.FirstOrDefault(c =>
                c.ClienteId?.Trim() == cliente.Trim());

            if (correoCliente == null)
            {
                return Ok(new ResultadoEnvioPruebaDTO
                {
                    Enviado = false,
                    Mensaje = "No se encontraron datos para este cliente en el rango de fechas indicado",
                    EmailDestino = EMAIL_PRUEBAS
                });
            }

            var htmlGenerado = await _generadorContenido
                .GenerarContenidoHtml(correoCliente)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(htmlGenerado))
            {
                return Ok(new ResultadoEnvioPruebaDTO
                {
                    Enviado = false,
                    Mensaje = "Error al generar el contenido HTML con OpenAI",
                    EmailDestino = EMAIL_PRUEBAS
                });
            }

            string asunto = $"[PRUEBA] Saca el máximo partido a tu compra, {correoCliente.ClienteNombre}";

            string htmlCompleto = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{asunto}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: Arial, sans-serif;"">
    <div style=""background-color: #fff3cd; padding: 10px; margin-bottom: 20px; border: 1px solid #ffc107;"">
        <strong>CORREO DE PRUEBA</strong><br/>
        Cliente original: {correoCliente.ClienteNombre} ({correoCliente.ClienteEmail})<br/>
        Productos comprados: {correoCliente.ProductosComprados?.Count ?? 0}<br/>
        Productos recomendados: {correoCliente.ProductosRecomendados?.Count ?? 0}
    </div>
    {htmlGenerado}
</body>
</html>";

            using (var mail = new MailMessage())
            {
                mail.From = new MailAddress("nesto@nuevavision.es", "El equipo de Nueva Visión");
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
                    ClienteOriginal = correoCliente.ClienteNombre,
                    ClienteEmailOriginal = correoCliente.ClienteEmail,
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
        public string Asunto { get; set; }
    }

    public class PreviewCorreoDTO
    {
        public string ClienteId { get; set; }
        public string ClienteNombre { get; set; }
        public string ClienteEmail { get; set; }
        public int ProductosComprados { get; set; }
        public int ProductosRecomendados { get; set; }
        public string HtmlGenerado { get; set; }
        public string Mensaje { get; set; }
    }
}
