using Hangfire;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>Nivel del interruptor del aviso de facturas vencidas (NestoAPI#534).</summary>
    public enum ModoAvisoFacturasVencidas
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila, "0" o cualquier cosa no reconocida).</summary>
        Apagado,
        /// <summary>Calcula la lista y la manda SOLO a administración. No escribe a ningún cliente.</summary>
        Sombra
    }

    /// <summary>
    /// NestoAPI#534 (corte 1, modo sombra): job diario del aviso de facturas vencidas por
    /// transferencia. El criterio vive en <see cref="SelectorAvisosFacturasVencidas"/> y el texto en
    /// <see cref="PlantillaAvisoFacturaVencida"/>.
    ///
    /// Interruptor: parámetro <c>AvisoFacturasVencidas</c> bajo «(defecto)» en ParámetrosUsuario.
    /// Sin fila (así nace) = APAGADO. Con "Sombra" manda cada mañana UN correo a administración con
    /// la lista de efectos que se avisarían, los que se quedan fuera y por qué, y el correo que
    /// recibiría el primero. En este corte NO se escribe a clientes ni se registra nada: eso es el
    /// corte 2 (con su tabla de avisos para no repetir).
    /// </summary>
    public static class AvisosFacturasVencidasJobsService
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        internal const string VALOR_SOMBRA = "Sombra";

        // Sin reintentos: si falla, mañana vuelve a pasar. Y nunca dos a la vez.
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Procesar()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    await Procesar(
                        new LectorParametrosUsuario(),
                        new ServicioCorreoElectronico(),
                        (dias, hoy) => new SelectorAvisosFacturasVencidas(db).Candidatos(EMPRESA, dias, hoy),
                        () => new ServicioFacturas(db).CuentaBancoEmpresa(EMPRESA),
                        DateTime.Today).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Aviso facturas vencidas #534] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>
        /// Núcleo del job, con las dependencias inyectadas para testearlo sin BD ni SMTP.
        /// Devuelve el modo con el que se ha ejecutado.
        /// </summary>
        internal static async Task<ModoAvisoFacturasVencidas> Procesar(ILectorParametrosUsuario lector,
            IServicioCorreoElectronico servicioCorreo,
            Func<int, DateTime, Task<List<AvisoFacturaVencidaDTO>>> calcularCandidatos,
            Func<string> leerIban,
            DateTime hoy)
        {
            ModoAvisoFacturasVencidas modo = LeerModo(lector);
            if (modo == ModoAvisoFacturasVencidas.Apagado)
            {
                return modo;
            }

            int dias = LeerDias(lector);
            List<AvisoFacturaVencidaDTO> candidatos = await calcularCandidatos(dias, hoy).ConfigureAwait(false)
                ?? new List<AvisoFacturaVencidaDTO>();

            string iban;
            try
            {
                iban = NormalizarIban(leerIban?.Invoke());
            }
            catch
            {
                iban = null; // La plantilla ya pone un texto genérico si no hay cuenta
            }

            using (MailMessage correo = ConstruirCorreoSombra(candidatos, dias, iban, hoy))
            {
                if (!servicioCorreo.EnviarCorreoSMTP(correo))
                {
                    ElmahHelper.Log(new Exception(
                        $"[Aviso facturas vencidas #534] No se ha podido mandar el correo sombra a administración ({candidatos.Count} efectos)."));
                }
            }
            return modo;
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoAvisoFacturasVencidas LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS));
            }
            catch
            {
                return ModoAvisoFacturasVencidas.Apagado;
            }
        }

        internal static ModoAvisoFacturasVencidas InterpretarModo(string valor)
            => string.Equals(valor?.Trim(), VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase)
                ? ModoAvisoFacturasVencidas.Sombra
                : ModoAvisoFacturasVencidas.Apagado;

        internal static int LeerDias(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarDias(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS));
            }
            catch
            {
                return SelectorAvisosFacturasVencidas.DIAS_UMBRAL_POR_DEFECTO;
            }
        }

        internal static int InterpretarDias(string valor)
            => int.TryParse(valor?.Trim(), out int dias) && dias > 0
                ? dias
                : SelectorAvisosFacturasVencidas.DIAS_UMBRAL_POR_DEFECTO;

        /// <summary>CuentaBancoEmpresa devuelve una cuenta por línea: en el correo van en una frase.</summary>
        internal static string NormalizarIban(string cuentas)
        {
            if (string.IsNullOrWhiteSpace(cuentas))
            {
                return null;
            }
            return string.Join(" o ", cuentas
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => c.Length > 0));
        }

        /// <summary>El correo del modo sombra: solo a administración, nunca al cliente.</summary>
        internal static MailMessage ConstruirCorreoSombra(List<AvisoFacturaVencidaDTO> candidatos, int dias, string iban, DateTime hoy)
        {
            List<AvisoFacturaVencidaDTO> seAvisarian = candidatos.Where(c => c.SeAvisaria).ToList();
            List<AvisoFacturaVencidaDTO> fuera = candidatos.Where(c => !c.SeAvisaria).ToList();

            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"[Sombra] Aviso de facturas vencidas {PlantillaAvisoFacturaVencida.FormatearFecha(hoy)}: {seAvisarian.Count} se avisarían, " +
                    $"{fuera.Count} se quedan fuera",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);
            mail.Body = GenerarHtmlSombra(seAvisarian, fuera, dias, iban, hoy);
            return mail;
        }

        internal static string GenerarHtmlSombra(List<AvisoFacturaVencidaDTO> seAvisarian, List<AvisoFacturaVencidaDTO> fuera,
            int dias, string iban, DateTime hoy)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Aviso de facturas vencidas por transferencia (modo sombra)</h2>");
            sb.AppendLine("<p><b>Esto es una prueba: no se ha escrito a ningún cliente.</b> Es la lista a la que se mandaría hoy " +
                "el aviso de factura pendiente de pago (NestoAPI#534), para revisar el criterio antes de encenderlo de verdad.</p>");
            sb.AppendLine($"<p>Criterio: efectos pendientes por transferencia (sin prepago ni contado), vencidos hace {dias} días o más " +
                $"y con vencimiento desde el {PlantillaAvisoFacturaVencida.FormatearFecha(SelectorAvisosFacturasVencidas.FECHA_CORTE_VENCIMIENTOS)}, " +
                "solo si la agencia ya ha entregado el pedido (igual que la remesa).</p>");

            sb.AppendLine($"<h3>Se avisarían ({seAvisarian.Count}; {PlantillaAvisoFacturaVencida.FormatearImporte(seAvisarian.Sum(c => c.Importe))})</h3>");
            if (seAvisarian.Any())
            {
                AppendTabla(sb, seAvisarian, conMotivo: false);
            }
            else
            {
                sb.AppendLine("<p>Hoy no se avisaría a nadie.</p>");
            }

            if (fuera.Any())
            {
                sb.AppendLine($"<h3>Cumplen el criterio pero se quedan fuera ({fuera.Count})</h3>");
                AppendTabla(sb, fuera, conMotivo: true);
            }

            AvisoFacturaVencidaDTO muestra = seAvisarian.FirstOrDefault();
            if (muestra != null)
            {
                sb.AppendLine("<h3>Muestra: el correo que recibiría el primero</h3>");
                sb.AppendLine("<div style='border: 1px solid #ccc; padding: 12px; background-color: #fafafa;'>");
                sb.AppendLine($"<p><b>De:</b> {Constantes.Correos.CORREO_ADMON} &nbsp; <b>Para:</b> {WebUtility.HtmlEncode(muestra.Destinatarios)} " +
                    $"&nbsp; <b>CCO:</b> {Constantes.Correos.CORREO_ADMON}</p>");
                sb.AppendLine($"<p><b>Asunto:</b> {WebUtility.HtmlEncode(PlantillaAvisoFacturaVencida.Asunto(muestra))}</p>");
                sb.AppendLine("<p><i>(Adjunto: la factura en PDF)</i></p>");
                sb.AppendLine(PlantillaAvisoFacturaVencida.CuerpoHtml(muestra, iban));
                sb.AppendLine("</div>");
            }

            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. " +
                "Para apagarlo: parámetro AvisoFacturasVencidas de (defecto) a 0 (Scripts/Issue534_AvisoFacturasVencidas.sql).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private static void AppendTabla(StringBuilder sb, List<AvisoFacturaVencidaDTO> filas, bool conMotivo)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.Append("<tr style='background-color: #4A90D9; color: white;'>");
            sb.Append("<th>Cliente</th><th>Nombre</th><th>Factura</th><th>Fecha</th><th>Vencimiento</th><th>Importe</th><th>Días vencida</th><th>Destinatario</th>");
            if (conMotivo)
            {
                sb.Append("<th>Motivo</th>");
            }
            sb.AppendLine("</tr>");
            bool alternar = false;
            foreach (AvisoFacturaVencidaDTO f in filas)
            {
                sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                sb.Append($"<td>{WebUtility.HtmlEncode(f.Cliente)}/{WebUtility.HtmlEncode(f.Contacto)}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(f.Nombre)}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(f.Factura)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.FechaFactura)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.Vencimiento)}</td>");
                sb.Append($"<td style='text-align: right;'>{PlantillaAvisoFacturaVencida.FormatearImporte(f.Importe)}</td>");
                sb.Append($"<td style='text-align: center;'>{f.DiasVencida}</td>");
                sb.Append($"<td>{WebUtility.HtmlEncode(f.Destinatarios)}</td>");
                if (conMotivo)
                {
                    sb.Append($"<td>{WebUtility.HtmlEncode(f.Motivo)}</td>");
                }
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }
    }
}
