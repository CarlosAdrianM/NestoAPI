using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#329: job recurrente que cierra el circuito de Verifactu por el final.
    /// 1. Consulta el estado de las facturas declaradas que siguen en 'Pendiente' y lo
    ///    actualiza (Verifacti confirma con la AEAT de forma asíncrona, por lotes).
    /// 2. Reintenta las facturas de series que tramitan y quedaron sin declarar (UUID null):
    ///    caída de Verifacti/AEAT (incidencia técnica: remitir al recuperarse es lo previsto
    ///    por la normativa) o NIF corregido después del rechazo. Autorreparable: se corrige
    ///    la ficha y en la siguiente pasada la factura se declara sola.
    /// 3. Si el rechazo es por el NIF del destinatario, marca la ficha como INCORRECTA (#327)
    ///    para que los avisos al meter pedido y el gate al facturar (#328) se activen solos.
    /// 4. Correo a administración solo cuando hay rechazos o facturas que no se han podido
    ///    declarar (sin ruido en pasadas sin novedades).
    /// </summary>
    public class VerifactuJobsService
    {
        // Guarda: NUNCA reintentar facturas anteriores al arranque de la sombra — el job
        // declararía el histórico entero (miles de facturas con UUID null legítimo).
        internal static DateTime FechaInicioDeclaracion { get; set; } = LeerFechaInicio();

        private static DateTime LeerFechaInicio()
        {
            string valor = System.Configuration.ConfigurationManager.AppSettings["Verifactu:FechaInicioDeclaracion"];
            return DateTime.TryParse(valor, out DateTime fecha) ? fecha : new DateTime(2026, 7, 20);
        }

        private const int MAX_CONSULTAS_POR_PASADA = 100;
        private const int MAX_REINTENTOS_POR_PASADA = 50;
        private const string ESTADO_PENDIENTE = "Pendiente";
        // NestoAPI#348: factura nacida por un camino de facturación externo a la API (VB6),
        // sin datos fiscales persistidos: no puede declararse jamás (el nombre del destinatario
        // sale de NombreFiscal) y se saca del ciclo de reintentos marcándola con este estado.
        internal const string ESTADO_SIN_DATOS_FISCALES = "SinDatosFiscales";

        private readonly NVEntities db;
        private readonly IServicioVerifactu servicioVerifactu;
        private readonly IServicioValidacionNif servicioValidacionNif;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly Func<CabFacturaVta, Task<VerifactuResponse>> reenviar;

        public VerifactuJobsService(NVEntities db = null, IServicioVerifactu servicioVerifactu = null,
            IServicioValidacionNif servicioValidacionNif = null, IServicioCorreoElectronico servicioCorreo = null,
            Func<CabFacturaVta, Task<VerifactuResponse>> reenviar = null)
        {
            this.db = db ?? new NVEntities();
            this.servicioVerifactu = servicioVerifactu ?? new Verifacti.ServicioVerifacti();
            this.servicioValidacionNif = servicioValidacionNif ?? new ServicioValidacionNif(this.db);
            this.servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
            this.reenviar = reenviar ?? ReenviarConServicioFacturas;
        }

        /// <summary>Punto de entrada de Hangfire (patrón del resto de jobs).</summary>
        public static async Task Procesar()
        {
            await new VerifactuJobsService().ProcesarPasada();
        }

        public async Task<ResumenJobVerifactu> ProcesarPasada()
        {
            var resumen = new ResumenJobVerifactu();
            if (!servicioVerifactu.EstaHabilitado)
            {
                return resumen; // sombra apagada: no-op
            }
            try
            {
                await ActualizarEstadosPendientes(resumen);
                await ReintentarNoDeclaradas(resumen);
                EnviarResumenSiProcede(resumen);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Verifactu job] Error en la pasada: {ex.Message}", ex));
            }
            return resumen;
        }

        internal async Task ActualizarEstadosPendientes(ResumenJobVerifactu resumen)
        {
            List<CabFacturaVta> pendientes = await db.CabsFacturasVtas
                .Where(f => f.VerifactuUUID != null && f.VerifactuUUID != ""
                    && f.VerifactuEstado == ESTADO_PENDIENTE)
                .OrderBy(f => f.Fecha)
                .Take(MAX_CONSULTAS_POR_PASADA)
                .ToListAsync().ConfigureAwait(false);

            bool hayCambios = false;
            foreach (CabFacturaVta factura in pendientes)
            {
                VerifactuResponse estado = await servicioVerifactu
                    .ConsultarEstadoAsync(factura.VerifactuUUID.Trim()).ConfigureAwait(false);
                if (estado == null || !estado.Exitoso || string.IsNullOrWhiteSpace(estado.Estado))
                {
                    continue; // sin veredicto aún (o Verifacti caído): se reintenta en la siguiente pasada
                }
                string estadoNuevo = estado.Estado.Length > 50 ? estado.Estado.Substring(0, 50) : estado.Estado;
                if (estadoNuevo == factura.VerifactuEstado?.Trim())
                {
                    continue;
                }
                factura.VerifactuEstado = estadoNuevo;
                hayCambios = true;
                resumen.EstadosActualizados++;

                if (EsEstadoDeRechazo(estadoNuevo))
                {
                    resumen.Rechazadas.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): " +
                        $"{estadoNuevo} - {estado.CodigoError} {estado.MensajeError}".Trim());
                    if (EsRechazoPorNif(estado.MensajeError))
                    {
                        await servicioValidacionNif.MarcarIncorrecto(factura.Nº_Cliente,
                            $"RECHAZO VERIFACTU: {estado.MensajeError}", "VerifactuJob").ConfigureAwait(false);
                    }
                }
            }
            if (hayCambios)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        internal async Task ReintentarNoDeclaradas(ResumenJobVerifactu resumen)
        {
            List<string> series = RegistroSeriesVerifactu.CodigosQueTramitan;
            DateTime fechaInicio = FechaInicioDeclaracion;
            List<CabFacturaVta> sinDeclarar = await db.CabsFacturasVtas
                .Where(f => (f.VerifactuUUID == null || f.VerifactuUUID == "")
                    && f.Fecha >= fechaInicio
                    && series.Contains(f.Serie)
                    && (f.VerifactuEstado == null || f.VerifactuEstado != ESTADO_SIN_DATOS_FISCALES))
                .OrderBy(f => f.Fecha)
                .Take(MAX_REINTENTOS_POR_PASADA)
                .ToListAsync().ConfigureAwait(false);

            bool hayExcluidas = false;
            foreach (CabFacturaVta factura in sinDeclarar)
            {
                // NestoAPI#348 (caso CV2600484/485): sin datos fiscales persistidos el mapeador
                // no tiene destinatario y Verifacti rechaza SIEMPRE ("el campo nombre es
                // obligatorio"). Las simplificadas (F2, sin destinatario) sí pueden declararse.
                // Se marca el estado para que la query las excluya en adelante (aviso único).
                if (string.IsNullOrWhiteSpace(factura.NombreFiscal)
                    && !MapeadorFacturaVerifactu.EsFacturaSimplificada(factura))
                {
                    factura.VerifactuEstado = ESTADO_SIN_DATOS_FISCALES;
                    factura.VerifactuUltimoError = "Factura de camino externo a la API (#348) sin datos " +
                        "fiscales: no puede declararse. Excluida de los reintentos del job.";
                    factura.VerifactuUltimoIntento = DateTime.Now;
                    hayExcluidas = true;
                    resumen.SinDeclarar.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): " +
                        "sin datos fiscales (camino viejo #348), EXCLUIDA de los reintentos");
                    continue;
                }

                VerifactuResponse respuesta = await reenviar(factura).ConfigureAwait(false);
                // NestoAPI#346: una factura atascada fallaría idéntico en cada pasada; al resumen
                // (y por tanto al correo a administración) solo van las NOVEDADES — primer fallo
                // o cambio de motivo — para no mandar el mismo correo 24 veces al día.
                string claveRuido = $"job|{factura.Empresa?.Trim()}|{factura.Número?.Trim()}";
                if (respuesta == null)
                {
                    // No procedía (p. ej. rectificativa sin vinculaciones) o error inesperado:
                    // el motivo ya queda en ELMAH dentro de EnviarAVerifactu.
                    if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, "no procesable"))
                    {
                        resumen.SinDeclarar.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): " +
                            "no se pudo procesar (ver ELMAH)");
                    }
                    continue;
                }
                if (respuesta.Exitoso)
                {
                    resumen.Declaradas++;
                    DeduplicadorErroresVerifactu.Limpiar(claveRuido);
                    continue;
                }
                string textoError = $"{respuesta.CodigoError} {respuesta.MensajeError}".Trim();
                if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, textoError))
                {
                    resumen.SinDeclarar.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): {textoError}");
                }
                if (EsRechazoPorNif(respuesta.MensajeError))
                {
                    await servicioValidacionNif.MarcarIncorrecto(factura.Nº_Cliente,
                        $"RECHAZO VERIFACTU: {respuesta.MensajeError}", "VerifactuJob").ConfigureAwait(false);
                }
            }
            if (hayExcluidas)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private async Task<VerifactuResponse> ReenviarConServicioFacturas(CabFacturaVta factura)
        {
            var servicioFacturas = new Facturas.ServicioFacturas(db);
            return RegistroSeriesVerifactu.EsSerieRectificativa(factura.Serie)
                ? await servicioFacturas.EnviarRectificativaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false)
                : await servicioFacturas.EnviarFacturaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false);
        }

        private void EnviarResumenSiProcede(ResumenJobVerifactu resumen)
        {
            if (!resumen.Rechazadas.Any() && !resumen.SinDeclarar.Any())
            {
                return; // sin novedades malas: sin ruido
            }
            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress("nesto@nuevavision.es"),
                    Subject = "Verifactu: facturas rechazadas o sin declarar",
                    IsBodyHtml = true,
                    Body =
                        (resumen.Rechazadas.Any()
                            ? "<p><b>Rechazadas por la AEAT:</b></p><ul><li>" + string.Join("</li><li>",
                                resumen.Rechazadas.Select(System.Net.WebUtility.HtmlEncode)) + "</li></ul>"
                            : string.Empty) +
                        (resumen.SinDeclarar.Any()
                            ? "<p><b>Sin poder declarar (se reintentará):</b></p><ul><li>" + string.Join("</li><li>",
                                resumen.SinDeclarar.Select(System.Net.WebUtility.HtmlEncode)) + "</li></ul>"
                            : string.Empty) +
                        "<p>Si el motivo es el NIF del cliente, la ficha ya ha quedado marcada como incorrecta: " +
                        "corregidlo (se revalida y la factura se declara sola en la siguiente pasada).</p>"
                };
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                _ = servicioCorreo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Verifactu job] No se pudo enviar el resumen: {ex.Message}", ex));
            }
        }

        /// <summary>Estados de Verifacti que significan rechazo (el resto: Correcto,
        /// AceptadoConErrores, Pendiente...).</summary>
        internal static bool EsEstadoDeRechazo(string estado)
        {
            return estado != null &&
                (estado.IndexOf("Incorrecto", StringComparison.OrdinalIgnoreCase) >= 0
                 || estado.IndexOf("Rechaz", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Detecta el rechazo por NIF del destinatario no censado (caso real 21/07:
        /// "El NIF/NOMBRE (90021192/...) del destinatario no se encuentra registrado").</summary>
        internal static bool EsRechazoPorNif(string mensaje)
        {
            return mensaje != null
                && mensaje.IndexOf("NIF", StringComparison.OrdinalIgnoreCase) >= 0
                && (mensaje.IndexOf("no se encuentra registrado", StringComparison.OrdinalIgnoreCase) >= 0
                    || mensaje.IndexOf("destinatario", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    public class ResumenJobVerifactu
    {
        public int EstadosActualizados { get; set; }
        public int Declaradas { get; set; }
        public List<string> Rechazadas { get; } = new List<string>();
        public List<string> SinDeclarar { get; } = new List<string>();
    }
}
