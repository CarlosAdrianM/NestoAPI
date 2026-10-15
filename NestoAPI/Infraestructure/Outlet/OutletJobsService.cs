using ClosedXML.Excel;
using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Outlet
{
    /// <summary>
    /// NestoAPI#643: job nocturno «outlet-cuadre-nocturno». Cada madrugada compara las categorías Outlet con el
    /// estado de los productos (<see cref="ReglasOutlet"/>) y:
    /// <list type="bullet">
    /// <item>con <see cref="ServicioOutlet.CLAVE_INTERRUPTOR"/> APAGADO (por defecto): dry-run; no escribe nada y manda
    /// a Carlos el Excel de lo que entraría, saldría o cambiaría de grupo;</item>
    /// <item>encendido: lo aplica, encola en Nesto_sync los productos con ficha web (la pasada de los 5 minutos los
    /// republica de madrugada) y manda el mismo Excel con lo hecho.</item>
    /// </list>
    /// No hay un punto único de escritura del estado del producto en la API (la ficha la guarda Nesto contra la BD),
    /// así que el cuadre nocturno es el que mantiene el Outlet. Sin trigger en la BD (decisión de Carlos).
    /// Si no hay nada que cambiar, no manda correo.
    /// </summary>
    public class OutletJobsService
    {
        public const string ID_JOB = "outlet-cuadre-nocturno";

        /// <summary>Todos los días a las 04:00 (después de los jobs de la 1:00 a las 3:00; la cola de Nesto_sync se vacía antes de abrir).</summary>
        public const string CRON = "0 4 * * *";

        internal const string REMITENTE = "nesto@nuevavision.es";

        private readonly ServicioOutlet servicio;
        private readonly IServicioCorreoElectronico correo;
        private readonly Action<Exception> registrarError;
        private readonly string[] destinatarios;

        public OutletJobsService() : this(null)
        {
        }

        internal OutletJobsService(ServicioOutlet servicio, IServicioCorreoElectronico correo = null,
            Action<Exception> registrarError = null, string[] destinatarios = null)
        {
            this.servicio = servicio ?? new ServicioOutlet();
            this.correo = correo ?? new ServicioCorreoElectronico();
            this.registrarError = registrarError ?? ElmahHelper.Log;
            this.destinatarios = destinatarios ?? new[] { Constantes.Correos.INFORMATICA };
        }

        /// <summary>Punto de entrada del job recurrente.</summary>
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        public static async Task CuadrarProgramado()
        {
            _ = await new OutletJobsService().Ejecutar().ConfigureAwait(false);
        }

        /// <summary>Cuadra (o simula) y avisa. Nunca lanza: los errores van a ELMAH.</summary>
        public async Task<ResultadoCuadreOutlet> Ejecutar()
        {
            ResultadoCuadreOutlet resultado;
            try
            {
                resultado = await servicio.Cuadrar().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Outlet #643] Error en el cuadre nocturno del Outlet: {ex.Message}", ex));
                return null;
            }

            Console.WriteLine($"🏷️ [Hangfire] Outlet ({(resultado.Aplicado ? "aplicado" : "dry-run")}): " +
                $"entran {resultado.Entran}, salen {resultado.Salen}, cambian de grupo {resultado.CambianDeGrupo}" +
                (resultado.Aplicado ? $"; aplicados {resultado.Aplicados}, con error {resultado.ConError}, encolados {resultado.Encolados}" : string.Empty));

            if (resultado.Cambios.Count == 0)
            {
                return resultado;
            }

            try
            {
                byte[] excel = GenerarExcel(resultado);
                using (MailMessage mail = ConstruirCorreo(resultado, excel, destinatarios))
                {
                    if (!correo.EnviarCorreoSMTP(mail))
                    {
                        registrarError(new Exception("[Outlet #643] No se ha podido mandar el correo del cuadre del Outlet."));
                    }
                }
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Outlet #643] Error al generar o mandar el Excel del cuadre del Outlet: {ex.Message}", ex));
            }
            return resultado;
        }

        internal static string Asunto(ResultadoCuadreOutlet r)
        {
            return r.Aplicado
                ? $"Outlet de la tienda: {r.Entran} entran, {r.Salen} salen, {r.CambianDeGrupo} cambian de grupo"
                : $"Outlet de la tienda (simulación): {r.Entran} entrarían, {r.Salen} saldrían, {r.CambianDeGrupo} cambiarían de grupo";
        }

        internal static MailMessage ConstruirCorreo(ResultadoCuadreOutlet r, byte[] excel, string[] destinatarios)
        {
            if (destinatarios == null || destinatarios.Length == 0)
            {
                throw new InvalidOperationException("No hay destinatarios para el correo del Outlet.");
            }
            MailMessage mail = new MailMessage
            {
                From = new MailAddress(REMITENTE, "Nesto - Nueva Visión"),
                Subject = Asunto(r),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(new MailAddress(destinatario));
            }

            StringBuilder cuerpo = new StringBuilder();
            if (r.Aplicado)
            {
                cuerpo.Append("<p>El cuadre nocturno del Outlet (NestoAPI#643) ha dejado las categorías Outlet de acuerdo con el estado 4 de los productos que están en la tienda.</p>");
            }
            else
            {
                cuerpo.Append("<p>Simulación del cuadre nocturno del Outlet (NestoAPI#643): <b>no se ha cambiado nada</b>, porque el interruptor " +
                    $"<code>{ServicioOutlet.CLAVE_INTERRUPTOR}</code> está apagado. Esto es lo que haría al encenderlo:</p>");
            }
            cuerpo.Append("<ul>");
            cuerpo.Append($"<li>Entran: <b>{r.Entran}</b> (estado 4 y en la tienda, sin categoría Outlet).</li>");
            cuerpo.Append($"<li>Salen: <b>{r.Salen}</b> (tienen categoría Outlet y no están en estado 4 o no están en la tienda).</li>");
            cuerpo.Append($"<li>Cambian de grupo: <b>{r.CambianDeGrupo}</b>.</li>");
            cuerpo.Append($"<li>Se republican por Nesto_sync (los que tienen ficha web): {r.SeRepublicarian}.</li>");
            cuerpo.Append($"<li>En el Outlet tras el cuadre: {r.ProductosEnOutletTrasCuadre} (hoy tienen categoría Outlet {r.ProductosConCategoriaOutletHoy}).</li>");
            if (r.Estado4SinTienda > 0)
            {
                cuerpo.Append($"<li>De estado 4 con ficha web que la puerta de publicación no deja ir a la tienda (no entran): {r.Estado4SinTienda}.</li>");
            }
            if (r.Aplicado)
            {
                cuerpo.Append($"<li>Aplicados: {r.Aplicados}; con error: {r.ConError} (detalle en ELMAH); encolados en Nesto_sync: {r.Encolados}.</li>");
            }
            cuerpo.Append("</ul>");
            cuerpo.Append("<p>La lista completa va en el Excel adjunto y, en cualquier momento, en <code>GET api/Outlet/Previsualizar</code>.</p>");
            mail.Body = cuerpo.ToString();
            mail.Attachments.Add(new Attachment(new MemoryStream(excel), $"Outlet_{DateTime.Today:yyyy-MM-dd}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            return mail;
        }

        internal static byte[] GenerarExcel(ResultadoCuadreOutlet r)
        {
            using (XLWorkbook libro = new XLWorkbook())
            {
                IXLWorksheet hoja = libro.Worksheets.Add(r.Aplicado ? "Cambios aplicados" : "Simulación");
                string[] cabeceras = { "Cambio", "Producto", "Nombre", "Grupo", "Subgrupo", "Estado", "Se pone", "Se quita", "Se republica", "Motivo" };
                for (int i = 0; i < cabeceras.Length; i++)
                {
                    hoja.Cell(1, i + 1).Value = cabeceras[i];
                }
                hoja.Row(1).Style.Font.Bold = true;
                int fila = 2;
                foreach (CambioOutlet c in r.Cambios)
                {
                    hoja.Cell(fila, 1).Value = c.Tipo;
                    hoja.Cell(fila, 2).Value = c.Numero;
                    hoja.Cell(fila, 3).Value = c.Nombre;
                    hoja.Cell(fila, 4).Value = c.Grupo;
                    hoja.Cell(fila, 5).Value = c.Subgrupo;
                    hoja.Cell(fila, 6).Value = (int)c.Estado;
                    hoja.Cell(fila, 7).Value = c.Poner ?? string.Empty;
                    hoja.Cell(fila, 8).Value = string.Join(", ", c.Quitar ?? new List<string>());
                    hoja.Cell(fila, 9).Value = c.Republicar ? "Sí" : "No (sin ficha web)";
                    hoja.Cell(fila, 10).Value = c.Motivo;
                    fila++;
                }
                hoja.SheetView.FreezeRows(1);
                _ = hoja.Range(1, 1, Math.Max(1, fila - 1), cabeceras.Length).SetAutoFilter();
                _ = hoja.Columns().AdjustToContents();
                using (MemoryStream stream = new MemoryStream())
                {
                    libro.SaveAs(stream);
                    return stream.ToArray();
                }
            }
        }
    }
}
