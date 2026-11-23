using NestoAPI.Models;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// NestoAPI#886: el correo mensual a cada tienda con lo dado de alta y de baja en su stock (HTML + Excel adjunto), con copia a
    /// Compras. Valor SIEMPRE a PVP: ni el correo ni el Excel llevan coste.
    /// </summary>
    public static class CorreoAltasBajasTienda
    {
        public const string REMITENTE = CorreoFacturacionTienda.REMITENTE;
        public const string NOMBRE_REMITENTE = CorreoFacturacionTienda.NOMBRE_REMITENTE;
        public const string COPIA = Constantes.Correos.COMPRAS;
        /// <summary>A partir de aquí la tabla del correo se corta: el resto va en el Excel.</summary>
        public const int MAXIMO_LINEAS_CORREO = 300;

        public const string FRASE_INICIAL = "Este es el resumen mensual de lo que se ha dado de alta y de baja en el stock de su tienda " +
            "(inventarios y productos marcados como «No está» al enviar una reposición). Revíselo y avise a Compras de lo que no le cuadre.";

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        /// <summary>«noviembre 2026» si el periodo es un mes natural entero; si no, «del 01/11/2026 al 15/11/2026».</summary>
        public static string Periodo(AltasBajasTiendaDTO informe)
        {
            DateTime desde = informe.Desde.Date;
            bool mesEntero = desde.Day == 1 && informe.Hasta.Date == desde.AddMonths(1).AddDays(-1);
            return mesEntero
                ? desde.ToString("MMMM yyyy", es)
                : $"del {desde.ToString("dd/MM/yyyy", es)} al {informe.Hasta.ToString("dd/MM/yyyy", es)}";
        }

        public static string Asunto(AltasBajasTiendaDTO informe)
            => $"Altas y bajas de stock de {informe.NombreTienda} · {Periodo(informe)}";

        public static MailMessage Construir(AltasBajasTiendaDTO informe, byte[] excel, string destinatario)
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
            mail.CC.Add(COPIA);
            if (excel != null)
            {
                mail.Attachments.Add(new Attachment(new MemoryStream(excel), GeneradorExcelAltasBajasTienda.NombreFichero(informe),
                    GeneradorExcelAltasBajasTienda.CONTENT_TYPE));
            }
            return mail;
        }

        private static string H(string texto) => WebUtility.HtmlEncode(texto ?? string.Empty);
        private static string Euros(decimal importe) => $"{importe.ToString("N2", es)} €";
        private static string Uds(int unidades) => unidades.ToString("N0", es);

        private const string ESTILO_TABLA = "border-collapse: collapse; font-size: 13px;";
        private const string ESTILO_TH = "border: 1px solid #ccc; padding: 4px 6px; background: #eee; text-align: left;";
        private const string ESTILO_TD = "border: 1px solid #ccc; padding: 4px 6px;";
        private const string ESTILO_TD_NUM = "border: 1px solid #ccc; padding: 4px 6px; text-align: right;";

        internal static string Cuerpo(AltasBajasTiendaDTO informe)
        {
            List<MovimientoAltaBajaTiendaDTO> movimientos = informe.Movimientos ?? new List<MovimientoAltaBajaTiendaDTO>();
            List<ProductoApareceDesapareceDTO> aparece = informe.ApareceYDesaparece ?? new List<ProductoApareceDesapareceDTO>();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 14px;'>");
            sb.AppendLine($"<h2 style='font-size: 18px;'>{H(Asunto(informe))}</h2>");
            sb.AppendLine($"<p>{FRASE_INICIAL}</p>"); // texto fijo, sin < ni &

            sb.AppendLine("<p>");
            sb.AppendLine($"Altas: <b>{Uds(informe.UnidadesAlta)} uds.</b> ({Euros(informe.ValorPvpAlta)} a PVP)<br>");
            sb.AppendLine($"Bajas: <b>{Uds(informe.UnidadesBaja)} uds.</b> ({Euros(informe.ValorPvpBaja)} a PVP)<br>");
            sb.AppendLine($"Neto: <b>{Uds(informe.UnidadesNeto)} uds.</b> ({Euros(informe.ValorPvpNeto)} a PVP)");
            sb.AppendLine("</p>");

            if (aparece.Count > 0)
            {
                sb.AppendLine("<h3 style='font-size: 15px;'>Aparece y desaparece</h3>");
                sb.AppendLine("<p>Productos que este mes se han dado de alta y también de baja.</p>");
                sb.AppendLine($"<table style='{ESTILO_TABLA}'><tr>");
                foreach (string c in GeneradorExcelAltasBajasTienda.COLUMNAS_APARECE)
                {
                    sb.Append($"<th style='{ESTILO_TH}'>{H(c)}</th>");
                }
                sb.AppendLine("</tr>");
                foreach (ProductoApareceDesapareceDTO p in aparece)
                {
                    sb.Append("<tr>")
                      .Append($"<td style='{ESTILO_TD}'>{H(p.Producto)}</td>")
                      .Append($"<td style='{ESTILO_TD}'>{H(p.Nombre)}</td>")
                      .Append($"<td style='{ESTILO_TD}'>{H(p.Familia)}</td>")
                      .Append($"<td style='{ESTILO_TD_NUM}'>{Uds(p.UnidadesAlta)}</td>")
                      .Append($"<td style='{ESTILO_TD_NUM}'>{Uds(p.UnidadesBaja)}</td>")
                      .Append($"<td style='{ESTILO_TD_NUM}'>{Uds(p.UnidadesNeto)}</td>")
                      .Append($"<td style='{ESTILO_TD_NUM}'>{Euros(p.ValorPvpNeto)}</td>")
                      .AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("<h3 style='font-size: 15px;'>Movimientos</h3>");
            sb.AppendLine($"<table style='{ESTILO_TABLA}'><tr>");
            foreach (string c in GeneradorExcelAltasBajasTienda.COLUMNAS_MOVIMIENTOS)
            {
                sb.Append($"<th style='{ESTILO_TH}'>{H(c)}</th>");
            }
            sb.AppendLine("</tr>");
            foreach (MovimientoAltaBajaTiendaDTO m in movimientos.Take(MAXIMO_LINEAS_CORREO))
            {
                sb.Append("<tr>")
                  .Append($"<td style='{ESTILO_TD}'>{m.FechaHora.ToString("dd/MM/yyyy HH:mm", es)}</td>")
                  .Append($"<td style='{ESTILO_TD}'>{H(m.Producto)}</td>")
                  .Append($"<td style='{ESTILO_TD}'>{H(m.Nombre)}</td>")
                  .Append($"<td style='{ESTILO_TD}'>{H(m.Familia)}</td>")
                  .Append($"<td style='{ESTILO_TD_NUM}'>{(m.Unidades > 0 ? "+" : string.Empty)}{Uds(m.Unidades)}</td>")
                  .Append($"<td style='{ESTILO_TD}'>{H(m.Motivo)}</td>")
                  .Append($"<td style='{ESTILO_TD}'>{H(m.Usuario)}</td>")
                  .Append($"<td style='{ESTILO_TD_NUM}'>{Euros(m.PvpUnitario)}</td>")
                  .Append($"<td style='{ESTILO_TD_NUM}'>{Euros(m.ValorPvp)}</td>")
                  .AppendLine("</tr>");
            }
            sb.AppendLine("</table>");
            if (movimientos.Count > MAXIMO_LINEAS_CORREO)
            {
                sb.AppendLine($"<p>Aquí van las {MAXIMO_LINEAS_CORREO} primeras de {Uds(movimientos.Count)} líneas; el resto, en el Excel adjunto.</p>");
            }
            else
            {
                sb.AppendLine("<p>El mismo detalle va en el Excel adjunto.</p>");
            }
            sb.AppendLine("<p style='color:#888; font-size:12px;'>Correo enviado desde Nesto («Altas y bajas de stock de las tiendas»).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }

    /// <summary>
    /// NestoAPI#886: el ÚNICO camino que manda el correo de una tienda (lo usan el job mensual y POST …/AltasBajasTienda/Correo).
    /// Sin movimientos no se manda nada.
    /// </summary>
    public class EnviadorAltasBajasTienda
    {
        private readonly IInformesService servicio;
        private readonly IServicioCorreoElectronico correo;

        public EnviadorAltasBajasTienda(IInformesService servicio, IServicioCorreoElectronico correo)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
            this.correo = correo ?? throw new ArgumentNullException(nameof(correo));
        }

        public const string MENSAJE_SIN_MOVIMIENTOS = "No hay altas ni bajas de stock en ese periodo: no se ha mandado nada.";

        /// <summary>Lanza si falla la lectura; si el SMTP no lo acepta, Enviado = false con el motivo.</summary>
        public async Task<CorreoAltasBajasTiendaDTO> EnviarAsync(string almacen, DateTime desde, DateTime hasta)
        {
            AltasBajasTiendaDTO informe = await servicio.LeerAltasBajasTiendaAsync(almacen, desde, hasta).ConfigureAwait(false);
            string para = GestorAltasBajasTienda.CorreoTienda(almacen);
            var resultado = new CorreoAltasBajasTiendaDTO
            {
                Almacen = informe.Almacen ?? almacen,
                Desde = informe.Desde,
                Hasta = informe.Hasta,
                NumeroMovimientos = informe.Movimientos?.Count ?? 0
            };
            if (resultado.NumeroMovimientos == 0)
            {
                resultado.Mensaje = MENSAJE_SIN_MOVIMIENTOS;
                return resultado;
            }
            if (para == null)
            {
                resultado.Mensaje = $"«{almacen}» no tiene buzón de tienda: no se ha mandado nada.";
                return resultado;
            }
            byte[] excel = new GeneradorExcelAltasBajasTienda().GenerarBytes(informe);
            using (MailMessage mail = CorreoAltasBajasTienda.Construir(informe, excel, para))
            {
                resultado.Para = mail.To.Select(t => t.Address).ToList();
                resultado.Copia = mail.CC.Select(t => t.Address).ToList();
                resultado.Enviado = correo.EnviarCorreoSMTP(mail);
            }
            resultado.Mensaje = resultado.Enviado
                ? $"Mandado a {string.Join(", ", resultado.Para)} con copia a {string.Join(", ", resultado.Copia)}."
                : $"No se ha podido mandar el correo a {para}. Inténtelo de nuevo dentro de un rato.";
            return resultado;
        }
    }
}
