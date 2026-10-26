using NestoAPI.Models.Informes;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Sugerencia 648: el correo con el Excel de lo facturado en la tienda adjunto (POST api/Informes/FacturacionTienda/Correo).
    /// Nesto corre en el escritorio remoto, donde Office no tiene licencia: el Excel se abre en el PC o en el móvil del usuario.
    /// El Excel es el mismo que el de GET /Excel (<see cref="GeneradorExcelFacturacionTienda"/>).
    /// </summary>
    public static class CorreoFacturacionTienda
    {
        public const string REMITENTE = "nesto@nuevavision.es";
        public const string NOMBRE_REMITENTE = "Nesto - Nueva Visión";
        /// <summary>Un correo que no es el del usuario solo puede ser de la empresa: el Excel lleva nombres y direcciones de clientes.</summary>
        public const string DOMINIO_EMPRESA = "@nuevavision.es";

        public const string MENSAJE_SIN_CORREO =
            "No tiene puesto su correo en Nesto (parámetro CorreoDefecto). Escriba a qué correo se lo mandamos o pida a Informática que se lo ponga.";
        public const string MENSAJE_SOLO_CORREOS_EMPRESA =
            "Por protección de datos, el informe solo se puede mandar a su correo o a uno de @nuevavision.es.";

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        public static string Asunto(FacturacionTiendaDTO informe)
            => $"Facturado en la tienda {informe.Tienda} del {informe.Desde.ToString("dd/MM/yyyy", es)} al {informe.Hasta.ToString("dd/MM/yyyy", es)}";

        /// <summary>
        /// El correo al que se manda: <paramref name="pedido"/> si viene (validado) o, si no, <paramref name="delUsuario"/>.
        /// Null con el motivo en <paramref name="error"/> si no hay ninguno o no vale.
        /// </summary>
        public static string ResolverDestinatario(string pedido, string delUsuario, out string error)
        {
            error = null;
            string suyo = string.IsNullOrWhiteSpace(delUsuario) ? null : delUsuario.Trim();
            if (string.IsNullOrWhiteSpace(pedido))
            {
                if (suyo == null || !EsCorreoValido(suyo))
                {
                    error = MENSAJE_SIN_CORREO;
                    return null;
                }
                return suyo;
            }
            string correo = pedido.Trim();
            if (!EsCorreoValido(correo))
            {
                error = $"«{correo}» no es un correo válido.";
                return null;
            }
            bool esElSuyo = suyo != null && string.Equals(correo, suyo, StringComparison.OrdinalIgnoreCase);
            if (!esElSuyo && !correo.EndsWith(DOMINIO_EMPRESA, StringComparison.OrdinalIgnoreCase))
            {
                error = MENSAJE_SOLO_CORREOS_EMPRESA;
                return null;
            }
            return correo;
        }

        public static bool EsCorreoValido(string correo)
        {
            if (string.IsNullOrWhiteSpace(correo) || correo.Contains(",") || correo.Contains(";") || correo.Contains(" "))
            {
                return false;
            }
            try
            {
                MailAddress direccion = new MailAddress(correo);
                return direccion.Address == correo && direccion.Host.Contains(".");
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static MailMessage Construir(FacturacionTiendaDTO informe, byte[] excel, string destinatario)
        {
            if (informe == null)
            {
                throw new ArgumentNullException(nameof(informe));
            }
            MailMessage mail = new MailMessage
            {
                From = new MailAddress(REMITENTE, NOMBRE_REMITENTE),
                Subject = Asunto(informe),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                Body = Cuerpo(informe)
            };
            mail.To.Add(destinatario);
            mail.Attachments.Add(new Attachment(new MemoryStream(excel), GeneradorExcelFacturacionTienda.NombreFichero(informe),
                GeneradorExcelFacturacionTienda.CONTENT_TYPE));
            return mail;
        }

        internal static string Cuerpo(FacturacionTiendaDTO informe)
        {
            int facturas = informe.NumeroFacturas;
            string periodo = $"del {informe.Desde.ToString("dd/MM/yyyy", es)} al {informe.Hasta.ToString("dd/MM/yyyy", es)}";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px;'>");
            sb.AppendLine($"<p>Le mandamos el Excel con lo facturado en la tienda {WebUtility.HtmlEncode(informe.Tienda)} {periodo} (cursos incluidos).</p>");
            if (facturas == 0)
            {
                sb.AppendLine("<p>En esas fechas no hay nada facturado.</p>");
            }
            else
            {
                sb.AppendLine("<p>");
                sb.AppendLine($"Facturas: <b>{facturas.ToString("N0", es)}</b><br>");
                sb.AppendLine($"Base imponible: <b>{informe.TotalBaseImponible.ToString("N2", es)} €</b><br>");
                sb.AppendLine($"Total (con IVA): <b>{informe.Total.ToString("N2", es)} €</b>");
                sb.AppendLine("</p>");
            }
            sb.AppendLine("<p>El detalle, factura a factura y por cliente, va en el Excel adjunto.</p>");
            sb.AppendLine("<p style='color:#888; font-size:12px;'>Correo enviado desde Nesto («Facturado en tienda»).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }
}
