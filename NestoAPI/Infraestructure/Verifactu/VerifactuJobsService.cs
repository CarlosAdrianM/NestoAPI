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
    /// 5. NestoAPI#522 (parte 1): a los clientes que recibieron por correo el justificante provisional
    ///    (VerifactuEnviadaProvisional = 1) les manda la factura definitiva en cuanto está registrada.
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
        private const int MAX_DEFINITIVAS_POR_PASADA = 50;
        private const string ESTADO_PENDIENTE = "Pendiente";
        // NestoAPI#348: factura nacida por un camino de facturación externo a la API (VB6),
        // sin datos fiscales persistidos: no puede declararse jamás (el nombre del destinatario
        // sale de NombreFiscal) y se saca del ciclo de reintentos marcándola con este estado.
        internal const string ESTADO_SIN_DATOS_FISCALES = "SinDatosFiscales";

        /// <summary>
        /// NestoAPI#551: factura sin declarar que se da por cerrada a mano (CV2600484/485, facturadas por el
        /// camino viejo antes de que Verifactu fuera obligatorio). Ni el job ni la ventana la vuelven a mirar.
        /// </summary>
        internal const string ESTADO_DESCARTADA = "DescartadaPreObligatoria";

        private readonly NVEntities db;
        private readonly IServicioVerifactu servicioVerifactu;
        private readonly IServicioValidacionNif servicioValidacionNif;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly Func<CabFacturaVta, Task<VerifactuResponse>> reenviar;
        private readonly Func<CabFacturaVta, Task<bool>> enviarDefinitiva;
        private readonly Func<CabFacturaVta, Task<string>> explicarNoProcesable;

        public VerifactuJobsService(NVEntities db = null, IServicioVerifactu servicioVerifactu = null,
            IServicioValidacionNif servicioValidacionNif = null, IServicioCorreoElectronico servicioCorreo = null,
            Func<CabFacturaVta, Task<VerifactuResponse>> reenviar = null,
            Func<CabFacturaVta, Task<bool>> enviarDefinitiva = null,
            Func<CabFacturaVta, Task<string>> explicarNoProcesable = null)
        {
            this.explicarNoProcesable = explicarNoProcesable ?? ExplicarNoProcesableConBaseDeDatos;
            this.db = db ?? new NVEntities();
            // #326: el proveedor lo decide ProveedorVerifactu (antes, un ServicioVerifacti nuevo por job).
            this.servicioVerifactu = servicioVerifactu ?? ProveedorVerifactu.Actual;
            this.servicioValidacionNif = servicioValidacionNif ?? new ServicioValidacionNif(this.db);
            this.servicioCorreo = servicioCorreo ?? new ServicioCorreoElectronico();
            this.reenviar = reenviar ?? ReenviarConServicioFacturas;
            this.enviarDefinitiva = enviarDefinitiva ?? EnviarDefinitivaConGestorFacturas;
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
                // Después de los reintentos: una factura declarada en esta misma pasada sale ya con su definitiva.
                await EnviarDefinitivasTrasProvisional(resumen);
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
                    // NestoAPI#522: el motivo de la AEAT queda en la factura para que administración lo vea
                    // en la ventana de facturas pendientes de Verifactu (antes solo iba en el correo).
                    string motivo = $"{estado.CodigoError} {estado.MensajeError}".Trim();
                    factura.VerifactuUltimoError = TruncarMotivo(string.IsNullOrEmpty(motivo)
                        ? $"La AEAT ha marcado el registro como {estadoNuevo} sin indicar el motivo"
                        : $"AEAT ({estadoNuevo}): {motivo}");
                    resumen.Rechazadas.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): " +
                        $"{estadoNuevo} - {estado.CodigoError} {estado.MensajeError}".Trim());
                    if (EsRechazoPorNif(estado.MensajeError))
                    {
                        await servicioValidacionNif.MarcarIncorrecto(factura.Nº_Cliente,
                            $"RECHAZO VERIFACTU: {estado.MensajeError}", "VerifactuJob").ConfigureAwait(false);
                        resumen.FichasMarcadasPorNif = true;
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
                    && (f.VerifactuEstado == null
                        || (f.VerifactuEstado != ESTADO_SIN_DATOS_FISCALES && f.VerifactuEstado != ESTADO_DESCARTADA)))
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
                    && !MapeadorFacturaVerifactu.EsFacturaSimplificada(factura)
                    && !MapeadorFacturaVerifactu.SeDeclaraSimplificadaPorMarca(factura)) // #392: F2 sin destinatario
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
                    // No procedía (p. ej. rectificativa sin vinculaciones) o error inesperado. El
                    // detalle técnico queda en ELMAH dentro de EnviarAVerifactu, pero el correo lo
                    // lee administración: tiene que decir qué pasa y qué hay que hacer (NestoAPI#570;
                    // antes decía «no se pudo procesar (ver ELMAH)»).
                    string motivo = await explicarNoProcesable(factura).ConfigureAwait(false);
                    if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, motivo))
                    {
                        resumen.SinDeclarar.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): {motivo}");
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
                // NestoAPI#522: pendiente por incidencia técnica de ANTES DE AYER: no se puede reenviar
                // (Verifacti solo admite el create hasta el día siguiente). Va a su propio apartado del correo a administración
                // (una vez por factura, deduplicado) y no pasa por el circuito del NIF.
                if (respuesta.CodigoError == Facturas.ServicioFacturas.CODIGO_INCIDENCIA_OTRO_DIA)
                {
                    if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, textoError))
                    {
                        resumen.PendientesPorIncidencia.Add(
                            $"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}, fecha {factura.Fecha:dd/MM/yyyy}): " +
                            respuesta.MensajeError);
                    }
                    continue;
                }
                if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, textoError))
                {
                    resumen.SinDeclarar.Add($"{factura.Número?.Trim()} (cliente {factura.Nº_Cliente?.Trim()}): {textoError}");
                }
                // NIF de un contacto sin unificar (sin letra, o de otra persona) con el principal ya
                // validado: se corrige la factura con el del principal y la siguiente pasada la
                // declara (casos NV2615575 y NV2615647, 23-24/09/26). Antes el rechazo por FORMATO no
                // se reconocía y la factura se reintentaba igual cada hora sin salir nunca.
                if ((EsRechazoPorNif(respuesta.MensajeError) || EsRechazoPorFormatoDeNif(respuesta.MensajeError))
                    && await servicioValidacionNif.CorregirNifFiscalFacturaConElPrincipal(factura, "VerifactuJob").ConfigureAwait(false))
                {
                    continue;
                }
                if (EsRechazoPorNif(respuesta.MensajeError))
                {
                    await servicioValidacionNif.MarcarIncorrecto(factura.Nº_Cliente,
                        $"RECHAZO VERIFACTU: {respuesta.MensajeError}", "VerifactuJob").ConfigureAwait(false);
                    resumen.FichasMarcadasPorNif = true;
                    // NestoAPI#383: el rechazo puede ser por el NOMBRE (cambio de apellido) con
                    // el NIF bueno. Si hay un nombre censal verificable, se corrige el persistido
                    // y la siguiente pasada la declara sola.
                    _ = await servicioValidacionNif.CorregirNombreFiscalFactura(factura, "VerifactuJob")
                        .ConfigureAwait(false);
                }
            }
            if (hayExcluidas)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// NestoAPI#522 (parte 1): el justificante provisional prometía «La recibirá por correo electrónico en
        /// cuanto se emita». Las facturas marcadas (el envío diario les mandó el provisional) que ya tienen
        /// registro se le mandan al cliente como definitivas y se quita la marca (a 0) para no repetir. Da igual
        /// quién las registrara (esta pasada, la ventana de administración o la facturación). Si el correo no
        /// sale, la marca se queda y se reintenta en la siguiente pasada (ELMAH una sola vez por factura).
        /// Una factura con UUID del sandbox sigue siendo un documento provisional: se espera.
        /// </summary>
        internal async Task EnviarDefinitivasTrasProvisional(ResumenJobVerifactu resumen)
        {
            DateTime fechaInicio = FechaInicioDeclaracion;
            List<CabFacturaVta> marcadas = await db.CabsFacturasVtas
                .Where(f => f.VerifactuEnviadaProvisional == true
                    && f.VerifactuUUID != null && f.VerifactuUUID != ""
                    && f.Fecha >= fechaInicio)
                .OrderBy(f => f.Fecha)
                .Take(MAX_DEFINITIVAS_POR_PASADA)
                .ToListAsync().ConfigureAwait(false);

            bool hayCambios = false;
            foreach (CabFacturaVta factura in marcadas)
            {
                if (Facturas.GestorFacturas.EsDocumentoProvisional(factura))
                {
                    continue; // registro del sandbox: el PDF todavía saldría como provisional
                }
                string claveRuido = $"definitiva|{factura.Empresa?.Trim()}|{factura.Número?.Trim()}";
                bool enviada;
                string motivo = null;
                try
                {
                    enviada = await enviarDefinitiva(factura).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    enviada = false;
                    motivo = ex.Message;
                }
                if (!enviada)
                {
                    string mensaje = $"[Verifactu job] No se ha podido mandar al cliente la factura definitiva {factura.Número?.Trim()} " +
                        $"(recibió el justificante provisional): se reintenta en la siguiente pasada (#522). {motivo}".Trim();
                    if (DeduplicadorErroresVerifactu.EsNovedad(claveRuido, mensaje))
                    {
                        ElmahHelper.Log(new Exception(mensaje));
                    }
                    continue;
                }
                factura.VerifactuEnviadaProvisional = false;
                hayCambios = true;
                resumen.DefinitivasEnviadas++;
                DeduplicadorErroresVerifactu.Limpiar(claveRuido);
            }
            if (hayCambios)
            {
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        private async Task<bool> EnviarDefinitivaConGestorFacturas(CabFacturaVta factura)
        {
            var gestor = new Facturas.GestorFacturas(new Facturas.ServicioFacturas(db));
            return await gestor.EnviarFacturaDefinitivaTrasProvisional(factura.Empresa, factura.Número).ConfigureAwait(false);
        }

        private async Task<VerifactuResponse> ReenviarConServicioFacturas(CabFacturaVta factura)
        {
            var servicioFacturas = new Facturas.ServicioFacturas(db);
            return RegistroSeriesVerifactu.EsSerieRectificativa(factura.Serie)
                ? await servicioFacturas.EnviarRectificativaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false)
                : await servicioFacturas.EnviarFacturaAVerifactu(factura.Empresa, factura.Número).ConfigureAwait(false);
        }

        /// <summary>
        /// NestoAPI#570: por qué no se ha podido ni preparar el envío de una factura, contado para
        /// administración. El caso conocido es la devolución facturada antes que su venta
        /// (RV2600067, 29/09/26: la venta era de fin de mes y seguía en albarán).
        /// </summary>
        private async Task<string> ExplicarNoProcesableConBaseDeDatos(CabFacturaVta factura)
        {
            try
            {
                if (!RegistroSeriesVerifactu.EsSerieRectificativa(factura.Serie))
                {
                    return TextoNoProcesable(false, null, null, factura.Fecha, factura.VerifactuUltimoError);
                }

                string empresa = factura.Empresa?.Trim();
                string numero = factura.Número?.Trim();
                bool vinculada = await db.LinFacturaVtaRectificaciones
                    .AnyAsync(r => r.Empresa == empresa && r.NumeroFactura.Trim() == numero).ConfigureAwait(false);
                if (vinculada)
                {
                    return TextoNoProcesable(false, null, null, factura.Fecha, factura.VerifactuUltimoError);
                }

                List<string> productos = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa && l.Nº_Factura.Trim() == numero && l.Cantidad < 0)
                    .Select(l => l.Producto).Distinct().ToListAsync().ConfigureAwait(false);
                string cliente = factura.Nº_Cliente;
                var venta = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa && l.Nº_Cliente == cliente && productos.Contains(l.Producto)
                        && l.Estado == Constantes.EstadosLineaVenta.ALBARAN && l.Cantidad > 0)
                    .OrderByDescending(l => l.Fecha_Albarán)
                    .Select(l => new { Pedido = l.Número, Albaran = l.Nº_Albarán })
                    .FirstOrDefaultAsync().ConfigureAwait(false);

                return TextoNoProcesable(true, venta?.Pedido, venta?.Albaran, factura.Fecha, factura.VerifactuUltimoError);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Verifactu job] No se pudo averiguar por qué no se declara {factura.Número?.Trim()}: {ex.Message}", ex));
                return TextoNoProcesable(false, null, null, factura.Fecha, null);
            }
        }

        /// <summary>
        /// El texto que lee administración. Sin nombres de herramientas ni de tablas: qué pasa y
        /// qué hay que hacer.
        /// </summary>
        internal static string TextoNoProcesable(bool esDevolucionSinFacturaDeOrigen, int? pedidoVenta, int? albaranVenta,
            DateTime fechaFactura, string ultimoError)
        {
            if (esDevolucionSinFacturaDeOrigen && pedidoVenta.HasValue)
            {
                // El alta solo se admite con fecha de hoy o de ayer: de ahí el plazo
                return $"es la devolución de una venta que todavía no está facturada (pedido {pedidoVenta}, " +
                    $"albarán {albaranVenta}). Se declarará sola en cuanto se facture esa venta. Como la devolución " +
                    $"tiene fecha {fechaFactura:dd/MM/yyyy}, hay que facturar la venta como muy tarde el " +
                    $"{fechaFactura.AddDays(1):dd/MM/yyyy}; si no da tiempo, avisad a Informática.";
            }
            if (esDevolucionSinFacturaDeOrigen)
            {
                return "es una devolución y no encontramos la factura de la venta que se devuelve. " +
                    "Hay que indicar a qué factura corresponde: avisad a Informática.";
            }
            if (!string.IsNullOrWhiteSpace(ultimoError))
            {
                return ultimoError.Trim();
            }
            return "no se ha podido preparar para declararla. Informática ya tiene el aviso con el detalle; no hay que hacer nada.";
        }

        private void EnviarResumenSiProcede(ResumenJobVerifactu resumen)
        {
            if (!resumen.Rechazadas.Any() && !resumen.SinDeclarar.Any() && !resumen.PendientesPorIncidencia.Any())
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
                        (resumen.PendientesPorIncidencia.Any()
                            ? "<p><b>Pendientes por incidencia técnica de hace más de un día (sin registrar en Verifactu; " +
                              "solo se pueden reenviar hasta el día siguiente a su fecha: hay que revisarlas a mano):</b></p><ul><li>" +
                              string.Join("</li><li>", resumen.PendientesPorIncidencia.Select(System.Net.WebUtility.HtmlEncode)) + "</li></ul>"
                            : string.Empty) +
                        // NestoAPI#570: esta frase salía siempre, aunque el motivo no tuviera nada que ver con el NIF
                        (resumen.FichasMarcadasPorNif
                            ? "<p>Cuando el motivo es el NIF del cliente, la ficha ya ha quedado marcada como incorrecta: " +
                              "corregidlo (se revalida y la factura se declara sola en la siguiente pasada).</p>"
                            : string.Empty)
                };
                mail.To.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
                _ = servicioCorreo.EnviarCorreoSMTP(mail);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Verifactu job] No se pudo enviar el resumen: {ex.Message}", ex));
            }
        }

        private static string TruncarMotivo(string texto)
        {
            return texto != null && texto.Length > 500 ? texto.Substring(0, 500) : texto;
        }

        /// <summary>Estados de Verifacti que significan rechazo (el resto: Correcto,
        /// AceptadoConErrores, Pendiente...).</summary>
        internal static bool EsEstadoDeRechazo(string estado)
        {
            return estado != null &&
                (estado.IndexOf("Incorrecto", StringComparison.OrdinalIgnoreCase) >= 0
                 || estado.IndexOf("Rechaz", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Verifacti rechaza el NIF por formato antes de mandarlo a la AEAT («El campo nif no tiene un
        /// formato válido»). No es el de id_otro: ese es de extranjeros y se corrige en la ventana.
        /// </summary>
        internal static bool EsRechazoPorFormatoDeNif(string mensaje)
        {
            return mensaje != null
                && mensaje.IndexOf("campo nif ", StringComparison.OrdinalIgnoreCase) >= 0
                && mensaje.IndexOf("formato", StringComparison.OrdinalIgnoreCase) >= 0;
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
        /// <summary>NestoAPI#570: alguna ficha se ha marcado con NIF incorrecto en esta pasada (para la nota del correo).</summary>
        public bool FichasMarcadasPorNif { get; set; }
        /// <summary>NestoAPI#522: pendientes por incidencia técnica de otro día, sin reenviar
        /// (a la espera de Verifacti). Visibles en el correo a administración.</summary>
        public List<string> PendientesPorIncidencia { get; } = new List<string>();
        /// <summary>NestoAPI#522 (parte 1): facturas definitivas mandadas a clientes que recibieron el justificante provisional.</summary>
        public int DefinitivasEnviadas { get; set; }
    }
}
