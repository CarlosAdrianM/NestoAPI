using System;
using System.Net;
using System.Net.Mail;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#726: GET api/EnviosAgencias/{id}/Gestiones. Qué se puede hacer con un envío que ya tiene la agencia.</summary>
    public class GestionesEnvioDTO
    {
        public bool PararEnVuelo { get; set; }
        public bool Retorno { get; set; }
        public bool CambiarFecha { get; set; }
        public bool CambiarDireccion { get; set; }
        /// <summary>Por qué no se admite nada (o lo que no se admite); null si se admite todo lo que toca.</summary>
        public string Motivo { get; set; }
        internal bool Alguna => PararEnVuelo || Retorno || CambiarFecha || CambiarDireccion;
    }

    /// <summary>NestoAPI#726: cuerpo de POST api/EnviosAgencias/{id}/FechaEntrega.</summary>
    public class CambiarFechaEntregaEnvioDTO
    {
        public DateTime? Fecha { get; set; }
    }

    /// <summary>NestoAPI#726: cuerpo de POST api/EnviosAgencias/{id}/DireccionEntrega.</summary>
    public class CambiarDireccionEntregaEnvioDTO
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        /// <summary>ISO2; vacío = ES.</summary>
        public string Pais { get; set; }
        public string Telefono { get; set; }
        public string Correo { get; set; }
    }

    /// <summary>NestoAPI#726: respuesta de PararEnVuelo, FechaEntrega y DireccionEntrega.</summary>
    public class ResultadoGestionEnvioDTO
    {
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#726: respuesta de POST api/EnviosAgencias/{id}/Retorno.</summary>
    public class ResultadoRetornoEnvioDTO
    {
        /// <summary>El albarán del envío de retorno que ha dado la agencia.</summary>
        public string CodigoRetorno { get; set; }
        /// <summary>El EnviosAgencia.Numero de la fila nueva del retorno (null si no se pudo grabar: el código queda en la historia).</summary>
        public int? EnvioRetornoId { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#726: las reglas de la gestión en tránsito (qué operación admite cada envío) y el correo a logística
    /// (NestoAPI#722). Sin BD: el controlador carga el envío y la agencia y llama aquí.
    /// </summary>
    public static class GestionEnTransitoEnvio
    {
        public const string OPERACION_PARAR_EN_VUELO = "Parar en vuelo";
        public const string OPERACION_RETORNO = "Retorno";
        public const string OPERACION_FECHA = "Cambiar fecha de entrega";
        public const string OPERACION_DIRECCION = "Cambiar dirección de entrega";
        private const string CORREO_REMITENTE = "nesto@nuevavision.es";
        /// <summary>EnviosAgencia.DetalleEstado (varchar(100)) tras parar en vuelo; el Estado no cambia hasta que lo diga el seguimiento.</summary>
        public const string DETALLE_PARADO_EN_VUELO = "Parado en vuelo";

        /// <summary>
        /// Lo que admite el envío: la agencia tiene que tener gestión en tránsito (<paramref name="agencia"/> no null) y el
        /// envío, albarán. Parar / fecha / dirección: en curso (registrado), tramitado o incidentado. Retorno: entregado y
        /// que no sea ya un retorno (recogida en origen).
        /// </summary>
        public static GestionesEnvioDTO Calcular(EnviosAgencia envio, IGestionEnTransitoAgenciaRemota agencia, string nombreAgencia)
        {
            var gestiones = new GestionesEnvioDTO();
            if (envio == null)
            {
                gestiones.Motivo = "El envío no existe.";
                return gestiones;
            }
            string nombre = string.IsNullOrWhiteSpace(nombreAgencia) ? envio.Agencia.ToString() : nombreAgencia.Trim();
            if (agencia == null)
            {
                gestiones.Motivo = $"La agencia {nombre} no tiene gestión en tránsito.";
                return gestiones;
            }
            if (string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                gestiones.Motivo = "El envío aún no está registrado en la agencia (no tiene albarán).";
                return gestiones;
            }

            bool enTransito = envio.Estado == Constantes.Agencias.ESTADO_EN_CURSO
                || envio.Estado == Constantes.Agencias.ESTADO_TRAMITADO
                || envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO;
            bool entregado = envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO;
            bool esRetorno = envio.Retorno == CTT.AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN;

            gestiones.PararEnVuelo = enTransito;
            gestiones.CambiarFecha = enTransito;
            gestiones.CambiarDireccion = enTransito;
            gestiones.Retorno = entregado && !esRetorno;

            if (!gestiones.Alguna)
            {
                gestiones.Motivo = entregado ? "El envío ya es un retorno: no se puede pedir el retorno de un retorno."
                    : envio.Estado == Constantes.Agencias.ESTADO_DEVUELTO ? "El envío ya está devuelto a origen."
                    : envio.Estado == Constantes.Agencias.ESTADO_CERRADO_A_MANO ? "El envío está cerrado a mano."
                    : envio.Estado < Constantes.Agencias.ESTADO_EN_CURSO ? "El envío está pendiente: aún no lo tiene la agencia."
                    : "El estado del envío no admite gestiones en la agencia.";
            }
            else if (entregado)
            {
                gestiones.Motivo = "El envío ya está entregado: solo se puede pedir el retorno.";
            }
            else
            {
                gestiones.Motivo = "El retorno solo se puede pedir con el envío entregado.";
            }
            return gestiones;
        }

        /// <summary>El texto que se lee en la historia y en el correo: operación, usuario y resultado.</summary>
        public static string TextoHistoria(string operacion, string usuario, bool exito, string detalle)
            => $"{operacion} en la agencia ({usuario}): {(exito ? "hecho" : "rechazado")}." +
               (string.IsNullOrWhiteSpace(detalle) ? string.Empty : " " + detalle.Trim());

        /// <summary>NestoAPI#722: el aviso a logística de una gestión hecha en la agencia sobre un envío que ya viaja.</summary>
        public static MailMessage ComponerCorreoLogistica(EnviosAgencia envio, string nombreAgencia, string operacion, string detalle, string usuario)
        {
            string documento = envio.Pedido.HasValue ? $"pedido {envio.Pedido}" : $"envío {envio.Numero}";
            string agencia = string.IsNullOrWhiteSpace(nombreAgencia) ? envio.Agencia.ToString() : nombreAgencia.Trim();
            var mail = new MailMessage
            {
                From = new MailAddress(CORREO_REMITENTE),
                Subject = $"{operacion} - {documento} ({agencia}) - c/ {envio.Cliente?.Trim()}",
                IsBodyHtml = true,
                Body =
                    $"<p>Se ha pedido a <b>{Html(agencia)}</b> «<b>{Html(operacion)}</b>» del {Html(documento)}.</p>" +
                    "<ul>" +
                    (envio.Pedido.HasValue ? $"<li>Pedido: <b>{envio.Pedido}</b></li>" : string.Empty) +
                    $"<li>Cliente: <b>{Html(envio.Cliente?.Trim())}</b> - {Html(envio.Nombre?.Trim())}</li>" +
                    $"<li>Envío: {envio.Numero} - albarán <b>{Html(envio.CodigoBarras?.Trim())}</b></li>" +
                    $"<li>Resultado: {Html(detalle)}</li>" +
                    $"<li>Usuario: {Html(usuario)}</li>" +
                    "</ul>"
            };
            mail.To.Add(new MailAddress(Constantes.Correos.LOGISTICA));
            return mail;
        }

        private static string Html(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
    }
}
