using Hangfire;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cobros
{
    /// <summary>Nivel del interruptor del aviso de facturas vencidas (NestoAPI#534/#544).</summary>
    public enum ModoAvisoFacturasVencidas
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila, "0" o cualquier cosa no reconocida).</summary>
        Apagado,
        /// <summary>Calcula la lista y la manda SOLO a administración. No escribe a ningún cliente ni registra nada.</summary>
        Sombra,
        /// <summary>NestoAPI#544: escribe a los clientes, registra cada aviso y manda el resumen a administración.</summary>
        Activo
    }

    /// <summary>
    /// Dependencias del job, inyectables para testearlo sin BD, SMTP, OpenAI ni generador de PDF.
    /// </summary>
    internal class DependenciasAvisosFacturasVencidas
    {
        public ILectorParametrosUsuario Lector { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        public IAlmacenAvisosFacturasVencidas Almacen { get; set; }
        /// <summary>(días de umbral, hoy) → candidatos con su memoria aplicada.</summary>
        public Func<int, DateTime, Task<List<AvisoFacturaVencidaDTO>>> CalcularCandidatos { get; set; }
        /// <summary>Clientes → sus apuntes con pendiente negativo (b).</summary>
        public Func<IEnumerable<string>, Task<List<ApunteNegativoClienteDTO>>> LeerApuntesNegativos { get; set; }
        /// <summary>IBAN y titular (de Bancos y Empresas). Un fallo aquí no para el aviso.</summary>
        public Func<DatosPagoAviso> LeerDatosPago { get; set; }
        /// <summary>Nombres → saludo por nombre (OpenAI + SanearSaludo, #484). Un fallo aquí no para el aviso.</summary>
        public Func<List<string>, Task<Dictionary<string, string>>> GenerarSaludos { get; set; }
        /// <summary>(empresa, factura) → PDF. Un fallo aquí no para el aviso: va sin ese adjunto y se apunta en ELMAH.</summary>
        public Func<string, string, byte[]> LeerFacturaPdf { get; set; }
        /// <summary>Fecha y hora del envío (para el saludo y para «hoy»).</summary>
        public DateTime Ahora { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// NestoAPI#534/#544: job diario del aviso de facturas vencidas por transferencia. El criterio
    /// vive en <see cref="SelectorAvisosFacturasVencidas"/>, la cadencia en
    /// <see cref="CadenciaAvisosFacturasVencidas"/>, la memoria en
    /// <see cref="IAlmacenAvisosFacturasVencidas"/> y el texto en <see cref="PlantillaAvisoFacturaVencida"/>.
    ///
    /// Interruptor: parámetro <c>AvisoFacturasVencidas</c> bajo «(defecto)» en ParámetrosUsuario.
    /// Sin fila (así nace) = APAGADO. "Sombra": cada mañana UN correo a administración con lo que se
    /// mandaría hoy (con su número de aviso y cuándo tocaría el siguiente), lo que se queda fuera y
    /// por qué, los clientes con cobros/abonos por liquidar y el correo que recibiría el primero;
    /// NO escribe a clientes NI en la tabla. "Activo": un correo por cliente con todas sus facturas
    /// vencidas y los PDF, registro de cada aviso, y resumen a administración.
    /// </summary>
    public static class AvisosFacturasVencidasJobsService
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        internal const string VALOR_SOMBRA = "Sombra";
        internal const string VALOR_ACTIVO = "Activo";
        internal const string NOMBRE_REMITENTE = "Administración - Nueva Visión";

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
                    AlmacenAvisosFacturasVencidasSql almacen = new AlmacenAvisosFacturasVencidasSql(db);
                    SelectorAvisosFacturasVencidas selector = new SelectorAvisosFacturasVencidas(db, almacen: almacen);
                    ServicioFacturas servicioFacturas = new ServicioFacturas(db);
                    GestorFacturas gestorFacturas = new GestorFacturas(servicioFacturas);
                    await Procesar(new DependenciasAvisosFacturasVencidas
                    {
                        Lector = new LectorParametrosUsuario(),
                        Correo = new ServicioCorreoElectronico(),
                        Almacen = almacen,
                        CalcularCandidatos = (dias, hoy) => selector.Candidatos(EMPRESA, dias, hoy),
                        LeerApuntesNegativos = clientes => selector.ApuntesNegativos(EMPRESA, clientes),
                        LeerDatosPago = () => new DatosPagoAviso
                        {
                            Iban = NormalizarIban(servicioFacturas.CuentaBancoEmpresa(EMPRESA)),
                            Titular = db.Empresas.Where(e => e.Número == EMPRESA).Select(e => e.Nombre).FirstOrDefault()?.Trim()
                        },
                        GenerarSaludos = nombres => new GeneradorContenidoCorreoPostCompra(new ServicioOpenAI()).GenerarSaludosAsync(nombres),
                        LeerFacturaPdf = (empresa, factura) => gestorFacturas.FacturaEnPDF(empresa, factura).ReadAsByteArrayAsync().Result,
                        Ahora = DateTime.Now
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Aviso facturas vencidas #534] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>Núcleo del job con las dependencias inyectadas. Devuelve el modo con el que se ha ejecutado.</summary>
        internal static async Task<ModoAvisoFacturasVencidas> Procesar(DependenciasAvisosFacturasVencidas deps)
        {
            ModoAvisoFacturasVencidas modo = LeerModo(deps.Lector);
            if (modo == ModoAvisoFacturasVencidas.Apagado)
            {
                return modo;
            }

            DateTime hoy = deps.Ahora.Date;
            int dias = LeerDias(deps.Lector);
            List<AvisoFacturaVencidaDTO> candidatos = await deps.CalcularCandidatos(dias, hoy).ConfigureAwait(false)
                ?? new List<AvisoFacturaVencidaDTO>();

            DatosPagoAviso datosPago;
            try
            {
                datosPago = deps.LeerDatosPago?.Invoke() ?? new DatosPagoAviso();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Aviso facturas vencidas #544] No se ha podido leer el IBAN o el titular; el aviso sale con el texto genérico: " + ex.Message, ex));
                datosPago = new DatosPagoAviso(); // La plantilla ya pone un texto genérico si no hay cuenta
            }

            List<AvisoClienteFacturasVencidasDTO> porCliente = AgruparPorCliente(candidatos.Where(c => c.SeAvisaria));
            List<AvisoClienteFacturasVencidasDTO> tocanHoy = porCliente.Where(c => c.Facturas.Any(f => f.TocaHoy)).ToList();
            List<AvisoClienteFacturasVencidasDTO> enEspera = porCliente.Where(c => !c.Facturas.Any(f => f.TocaHoy)).ToList();
            await ResolverSaludos(tocanHoy, deps).ConfigureAwait(false);

            List<string> clientesConNegativos = candidatos
                .Where(c => c.Motivo == SelectorAvisosFacturasVencidas.MOTIVO_CLIENTE_CON_NEGATIVOS)
                .Select(c => c.Cliente).Distinct().ToList();
            List<ApunteNegativoClienteDTO> negativos = new List<ApunteNegativoClienteDTO>();
            if (clientesConNegativos.Any() && deps.LeerApuntesNegativos != null)
            {
                try
                {
                    negativos = await deps.LeerApuntesNegativos(clientesConNegativos).ConfigureAwait(false) ?? negativos;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Aviso facturas vencidas #544] No se han podido leer los apuntes negativos: " + ex.Message, ex));
                }
            }

            if (modo == ModoAvisoFacturasVencidas.Sombra)
            {
                using (MailMessage correo = ConstruirCorreoSombra(candidatos, tocanHoy, enEspera, negativos, dias, datosPago, deps.Ahora))
                {
                    if (!deps.Correo.EnviarCorreoSMTP(correo))
                    {
                        ElmahHelper.Log(new Exception(
                            $"[Aviso facturas vencidas #534] No se ha podido mandar el correo sombra a administración ({candidatos.Count} efectos)."));
                    }
                }
                return modo;
            }

            // Activo: un correo por cliente, registro y resumen
            List<AvisoClienteFacturasVencidasDTO> enviados = new List<AvisoClienteFacturasVencidasDTO>();
            List<(AvisoClienteFacturasVencidasDTO Aviso, string Error)> fallidos = new List<(AvisoClienteFacturasVencidasDTO, string)>();
            foreach (AvisoClienteFacturasVencidasDTO aviso in tocanHoy)
            {
                try
                {
                    using (MailMessage correo = ConstruirCorreoCliente(aviso, datosPago, deps.LeerFacturaPdf))
                    {
                        if (!deps.Correo.EnviarCorreoSMTP(correo))
                        {
                            fallidos.Add((aviso, "El servidor de correo no ha aceptado el envío."));
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Aviso facturas vencidas #544] No se ha podido mandar el aviso al cliente {aviso.Cliente}/{aviso.Contacto}: " + ex.Message, ex));
                    fallidos.Add((aviso, ex.Message));
                    continue;
                }
                enviados.Add(aviso);
                try
                {
                    await deps.Almacen.Registrar(aviso.Facturas.Select(f => new AvisoFacturaVencidaRegistrado
                    {
                        Empresa = EMPRESA,
                        Cliente = aviso.Cliente,
                        Contacto = aviso.Contacto,
                        NumOrden = f.NOrden,
                        Factura = f.Factura,
                        NumeroAviso = f.NumeroAviso,
                        Fecha = hoy,
                        ImportePendiente = f.Importe,
                        Destinatarios = aviso.Destinatarios
                    })).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Grave: sin registro, mañana se repetiría. Que quede en ELMAH y en el resumen.
                    ElmahHelper.Log(new Exception($"[Aviso facturas vencidas #544] Aviso MANDADO al cliente {aviso.Cliente}/{aviso.Contacto} pero NO REGISTRADO en AvisosFacturasVencidas (mañana se repetiría): " + ex.Message, ex));
                    fallidos.Add((aviso, "MANDADO pero no registrado en la tabla: " + ex.Message));
                }
            }

            if (enviados.Any() || fallidos.Any() || negativos.Any())
            {
                using (MailMessage resumen = ConstruirCorreoResumenAdministracion(enviados, fallidos, negativos, deps.Ahora))
                {
                    if (!deps.Correo.EnviarCorreoSMTP(resumen))
                    {
                        ElmahHelper.Log(new Exception("[Aviso facturas vencidas #544] No se ha podido mandar el resumen del día a administración."));
                    }
                }
            }
            return modo;
        }

        /// <summary>Un aviso por cliente/contacto con todas sus facturas avisables.</summary>
        internal static List<AvisoClienteFacturasVencidasDTO> AgruparPorCliente(IEnumerable<AvisoFacturaVencidaDTO> avisables)
            => (avisables ?? Enumerable.Empty<AvisoFacturaVencidaDTO>())
                .GroupBy(a => new { Cliente = a.Cliente?.Trim(), Contacto = a.Contacto?.Trim() })
                .Select(g => new AvisoClienteFacturasVencidasDTO
                {
                    Cliente = g.Key.Cliente,
                    Contacto = g.Key.Contacto,
                    Nombre = g.Select(a => a.Nombre).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Destinatarios = g.Select(a => a.Destinatarios).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)),
                    NombrePersonaContacto = g.Select(a => a.NombrePersonaContacto).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                    Facturas = g.OrderBy(a => a.Vencimiento).ThenBy(a => a.Factura).ToList()
                })
                .OrderBy(c => c.Cliente).ThenBy(c => c.Contacto)
                .ToList();

        /// <summary>
        /// NestoAPI#544 (d): el saludo de cada aviso. Al modelo se le pasa el nombre de la persona de
        /// contacto si lo hay (mejor que la razón social) y él decide persona/empresa; de lo que
        /// devuelve solo se usa el nombre de pila, y la fórmula la pone la hora del envío. Si OpenAI
        /// falla o no está configurado, saludo genérico: el aviso nunca se bloquea por el saludo.
        /// </summary>
        internal static async Task ResolverSaludos(List<AvisoClienteFacturasVencidasDTO> avisos, DependenciasAvisosFacturasVencidas deps)
        {
            Dictionary<string, string> saludos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<string> nombres = avisos
                .Select(a => NombreParaSaludar(a))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nombres.Any() && deps.GenerarSaludos != null)
            {
                try
                {
                    saludos = await deps.GenerarSaludos(nombres).ConfigureAwait(false)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception("[Aviso facturas vencidas #544] No se han podido generar los saludos; salen genéricos: " + ex.Message, ex));
                }
            }
            foreach (AvisoClienteFacturasVencidasDTO aviso in avisos)
            {
                string nombre = NombreParaSaludar(aviso);
                string nombrePila = nombre != null && saludos.TryGetValue(nombre, out string saludo)
                    ? GeneradorContenidoCorreoPostCompra.NombreDelSaludo(saludo)
                    : null;
                aviso.Saludo = PlantillaAvisoFacturaVencida.Saludo(nombrePila, deps.Ahora);
            }
        }

        internal static string NombreParaSaludar(AvisoClienteFacturasVencidasDTO aviso)
            => !string.IsNullOrWhiteSpace(aviso.NombrePersonaContacto) ? aviso.NombrePersonaContacto.Trim()
                : !string.IsNullOrWhiteSpace(aviso.Nombre) ? aviso.Nombre.Trim() : null;

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
        {
            string limpio = valor?.Trim();
            if (string.Equals(limpio, VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase))
            {
                return ModoAvisoFacturasVencidas.Sombra;
            }
            if (string.Equals(limpio, VALOR_ACTIVO, StringComparison.OrdinalIgnoreCase))
            {
                return ModoAvisoFacturasVencidas.Activo;
            }
            return ModoAvisoFacturasVencidas.Apagado;
        }

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

        /// <summary>
        /// El correo real al cliente: de administración, responder a administración y CCO a
        /// administración (para poder buscarlo en Outlook), con las facturas en PDF.
        /// </summary>
        internal static MailMessage ConstruirCorreoCliente(AvisoClienteFacturasVencidasDTO aviso, DatosPagoAviso datosPago,
            Func<string, string, byte[]> leerFacturaPdf)
        {
            if (string.IsNullOrWhiteSpace(aviso?.Destinatarios))
            {
                throw new InvalidOperationException("El aviso no tiene destinatarios.");
            }
            MailMessage mail = new MailMessage
            {
                From = new MailAddress(Constantes.Correos.CORREO_ADMON, NOMBRE_REMITENTE),
                Subject = PlantillaAvisoFacturaVencida.Asunto(aviso),
                Body = PlantillaAvisoFacturaVencida.CuerpoHtml(aviso, datosPago),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in aviso.Destinatarios.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0))
            {
                mail.To.Add(new MailAddress(destinatario));
            }
            mail.ReplyToList.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            mail.Bcc.Add(new MailAddress(Constantes.Correos.CORREO_ADMON));
            foreach (string factura in aviso.NumerosFactura)
            {
                try
                {
                    byte[] pdf = leerFacturaPdf?.Invoke(EMPRESA, factura);
                    if (pdf != null && pdf.Length > 0)
                    {
                        mail.Attachments.Add(new Attachment(new MemoryStream(pdf), factura + ".pdf"));
                    }
                }
                catch (Exception ex)
                {
                    // El aviso lleva todos los datos en el texto: sale sin ese PDF y queda constancia
                    ElmahHelper.Log(new Exception($"[Aviso facturas vencidas #544] No se ha podido generar el PDF de la factura {factura} para el aviso al cliente {aviso.Cliente}; el aviso sale sin él: " + ex.Message, ex));
                }
            }
            return mail;
        }

        /// <summary>El correo del modo sombra: solo a administración, nunca al cliente.</summary>
        internal static MailMessage ConstruirCorreoSombra(List<AvisoFacturaVencidaDTO> candidatos,
            List<AvisoClienteFacturasVencidasDTO> tocanHoy, List<AvisoClienteFacturasVencidasDTO> enEspera,
            List<ApunteNegativoClienteDTO> negativos, int dias, DatosPagoAviso datosPago, DateTime ahora)
        {
            List<AvisoFacturaVencidaDTO> fuera = candidatos.Where(c => !c.SeAvisaria).ToList();
            int facturasHoy = tocanHoy.Sum(c => c.Facturas.Count);

            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"[Sombra] Aviso de facturas vencidas {PlantillaAvisoFacturaVencida.FormatearFecha(ahora)}: " +
                    $"{tocanHoy.Count} clientes ({facturasHoy} facturas) se avisarían, {enEspera.Count} en espera, {fuera.Count} se quedan fuera",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);
            mail.Body = GenerarHtmlSombra(tocanHoy, enEspera, fuera, negativos, dias, datosPago, ahora);
            return mail;
        }

        internal static string GenerarHtmlSombra(List<AvisoClienteFacturasVencidasDTO> tocanHoy, List<AvisoClienteFacturasVencidasDTO> enEspera,
            List<AvisoFacturaVencidaDTO> fuera, List<ApunteNegativoClienteDTO> negativos, int dias, DatosPagoAviso datosPago, DateTime ahora)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Aviso de facturas vencidas por transferencia (modo sombra)</h2>");
            sb.AppendLine("<p><b>Esto es una prueba: no se ha escrito a ningún cliente ni se ha registrado nada.</b> Es lo que se mandaría hoy " +
                "(NestoAPI#534/#544), para revisar el criterio, la cadencia y el texto antes de encenderlo de verdad.</p>");
            sb.AppendLine($"<p>Criterio: efectos pendientes por transferencia (sin prepago ni contado), vencidos hace {dias} días o más " +
                $"y con vencimiento desde el {PlantillaAvisoFacturaVencida.FormatearFecha(SelectorAvisosFacturasVencidas.FECHA_CORTE_VENCIMIENTOS)}, " +
                "solo si la agencia ya ha entregado el pedido (igual que la remesa). Un correo por cliente con todas sus facturas vencidas. " +
                "Cadencia: 1.º al cumplir el umbral, 2.º a los 10 días, 3.º a los 5, 4.º a los 2 y después diario (laborables); " +
                "si el cliente paga parte, la cuenta empieza de nuevo.</p>");

            decimal totalHoy = tocanHoy.Sum(c => c.Total);
            sb.AppendLine($"<h3>Se avisarían hoy: {tocanHoy.Count} clientes, {tocanHoy.Sum(c => c.Facturas.Count)} facturas ({PlantillaAvisoFacturaVencida.FormatearImporte(totalHoy)})</h3>");
            if (tocanHoy.Any())
            {
                AppendTablaClientes(sb, tocanHoy, ahora);
            }
            else
            {
                sb.AppendLine("<p>Hoy no se avisaría a nadie.</p>");
            }

            if (enEspera.Any())
            {
                sb.AppendLine($"<h3>Avisables pero hoy no toca ({enEspera.Count} clientes)</h3>");
                sb.AppendLine("<p>Ya se les avisó y aún no ha pasado el plazo de la cadencia.</p>");
                AppendTablaClientes(sb, enEspera, ahora);
            }

            if (fuera.Any())
            {
                sb.AppendLine($"<h3>Cumplen el criterio pero se quedan fuera ({fuera.Count})</h3>");
                AppendTablaEfectos(sb, fuera);
            }

            if (negativos.Any())
            {
                sb.AppendLine($"<h3>Clientes con cobros o abonos pendientes de liquidar ({negativos.Select(n => n.Cliente).Distinct().Count()})</h3>");
                sb.AppendLine("<p>No se les avisa mientras tengan apuntes negativos en el extracto. Cuando se enciende de verdad, esto va en un correo aparte a administración para liquidarlos.</p>");
                AppendTablaNegativos(sb, negativos);
            }

            AvisoClienteFacturasVencidasDTO muestra = tocanHoy.FirstOrDefault();
            if (muestra != null)
            {
                sb.AppendLine("<h3>Muestra: el correo que recibiría el primero</h3>");
                sb.AppendLine("<div style='border: 1px solid #ccc; padding: 12px; background-color: #fafafa;'>");
                sb.AppendLine($"<p><b>De:</b> {Constantes.Correos.CORREO_ADMON} &nbsp; <b>Para:</b> {PlantillaAvisoFacturaVencida.Html(muestra.Destinatarios)} " +
                    $"&nbsp; <b>Responder a:</b> {Constantes.Correos.CORREO_ADMON} &nbsp; <b>CCO:</b> {Constantes.Correos.CORREO_ADMON}</p>");
                sb.AppendLine($"<p><b>Asunto:</b> {PlantillaAvisoFacturaVencida.Html(PlantillaAvisoFacturaVencida.Asunto(muestra))}</p>");
                sb.AppendLine($"<p><i>(Adjuntos: {PlantillaAvisoFacturaVencida.Html(string.Join(", ", muestra.NumerosFactura.Select(f => f + ".pdf")))})</i></p>");
                sb.AppendLine(PlantillaAvisoFacturaVencida.CuerpoHtml(muestra, datosPago));
                sb.AppendLine("</div>");
            }

            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {ahora:dd/MM/yyyy HH:mm}. " +
                "Para apagarlo: parámetro AvisoFacturasVencidas de (defecto) a 0; para encenderlo de verdad, 'Activo' (Scripts/Issue544_AvisosFacturasVencidas.sql).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>El resumen del día a administración cuando está activo (b): enviados, fallidos y apuntes negativos por liquidar.</summary>
        internal static MailMessage ConstruirCorreoResumenAdministracion(List<AvisoClienteFacturasVencidasDTO> enviados,
            List<(AvisoClienteFacturasVencidasDTO Aviso, string Error)> fallidos, List<ApunteNegativoClienteDTO> negativos, DateTime ahora)
        {
            MailMessage mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Aviso de facturas vencidas {PlantillaAvisoFacturaVencida.FormatearFecha(ahora)}: {enviados.Count} avisos mandados" +
                    (fallidos.Any() ? $", {fallidos.Count} fallidos" : string.Empty) +
                    (negativos.Any() ? $", {negativos.Select(n => n.Cliente).Distinct().Count()} clientes con apuntes por liquidar" : string.Empty),
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            mail.To.Add(Constantes.Correos.CORREO_ADMON);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine("<h2>Aviso de facturas vencidas por transferencia: resumen del día</h2>");
            sb.AppendLine($"<h3>Avisos mandados a clientes ({enviados.Count})</h3>");
            if (enviados.Any())
            {
                AppendTablaClientes(sb, enviados, ahora);
            }
            else
            {
                sb.AppendLine("<p>Hoy no tocaba avisar a nadie.</p>");
            }
            if (fallidos.Any())
            {
                sb.AppendLine($"<h3 style='color: #b00;'>Avisos que han fallado ({fallidos.Count})</h3>");
                sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
                sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Cliente</th><th>Nombre</th><th>Facturas</th><th>Error</th></tr>");
                foreach ((AvisoClienteFacturasVencidasDTO aviso, string error) in fallidos)
                {
                    sb.AppendLine($"<tr><td>{PlantillaAvisoFacturaVencida.Html(aviso.Cliente)}/{PlantillaAvisoFacturaVencida.Html(aviso.Contacto)}</td>" +
                        $"<td>{PlantillaAvisoFacturaVencida.Html(aviso.Nombre)}</td><td>{PlantillaAvisoFacturaVencida.Html(string.Join(", ", aviso.NumerosFactura))}</td>" +
                        $"<td>{PlantillaAvisoFacturaVencida.Html(error)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            if (negativos.Any())
            {
                sb.AppendLine($"<h3>Clientes a los que NO se avisa por tener cobros o abonos pendientes de liquidar ({negativos.Select(n => n.Cliente).Distinct().Count()})</h3>");
                sb.AppendLine("<p>Tienen facturas vencidas por transferencia que cumplen el criterio, pero con apuntes negativos en el extracto puede que ya hayan pagado. " +
                    "Estos son los apuntes: al liquidarlos, el aviso sale en la siguiente pasada.</p>");
                AppendTablaNegativos(sb, negativos);
            }
            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {ahora:dd/MM/yyyy HH:mm} (NestoAPI#544).</p>");
            sb.AppendLine("</body></html>");
            mail.Body = sb.ToString();
            return mail;
        }

        private static void AppendTablaClientes(StringBuilder sb, List<AvisoClienteFacturasVencidasDTO> clientes, DateTime ahora)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.Append("<tr style='background-color: #4A90D9; color: white;'>");
            sb.Append("<th>Cliente</th><th>Nombre</th><th>Factura</th><th>Fecha</th><th>Vencimiento</th><th>Importe</th><th>Días vencida</th>" +
                "<th>Aviso</th><th>Último aviso</th><th>Siguiente</th><th>Destinatario</th><th>Saludo</th>");
            sb.AppendLine("</tr>");
            bool alternar = false;
            foreach (AvisoClienteFacturasVencidasDTO c in clientes)
            {
                foreach (AvisoFacturaVencidaDTO f in c.Facturas)
                {
                    string aviso = CadenciaAvisosFacturasVencidas.Ordinal(f.NumeroAviso)
                        + (f.ReinicioPorPagoParcial ? " (de nuevo: ha pagado parte)" : string.Empty)
                        + (!f.TocaHoy ? " (aún no toca)" : string.Empty);
                    string siguiente = f.TocaHoy
                        ? PlantillaAvisoFacturaVencida.FormatearFecha(ahora.Date.AddDays(CadenciaAvisosFacturasVencidas.DiasHastaElSiguiente(f.NumeroAviso)))
                        : PlantillaAvisoFacturaVencida.FormatearFecha(f.FechaSiguienteAviso);
                    sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(c.Cliente)}/{PlantillaAvisoFacturaVencida.Html(c.Contacto)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(c.Nombre)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Factura)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.FechaFactura)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.Vencimiento)}</td>");
                    sb.Append($"<td style='text-align: right;'>{PlantillaAvisoFacturaVencida.FormatearImporte(f.Importe)}</td>");
                    sb.Append($"<td style='text-align: center;'>{f.DiasVencida}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(aviso)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.FechaUltimoAviso)}</td>");
                    sb.Append($"<td>{siguiente}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(c.Destinatarios)}</td>");
                    sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(c.Saludo)}</td>");
                    sb.AppendLine("</tr>");
                }
                if (c.Facturas.Count > 1)
                {
                    sb.AppendLine($"<tr style='font-weight: bold; background-color: #e8f0fa;'><td colspan='5'>Total {PlantillaAvisoFacturaVencida.Html(c.Cliente)} ({c.Facturas.Count} facturas, {CadenciaAvisosFacturasVencidas.Ordinal(c.NumeroAviso)})</td>" +
                        $"<td style='text-align: right;'>{PlantillaAvisoFacturaVencida.FormatearImporte(c.Total)}</td><td colspan='6'></td></tr>");
                }
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendTablaEfectos(StringBuilder sb, List<AvisoFacturaVencidaDTO> filas)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.Append("<tr style='background-color: #4A90D9; color: white;'>");
            sb.Append("<th>Cliente</th><th>Nombre</th><th>Factura</th><th>Fecha</th><th>Vencimiento</th><th>Importe</th><th>Días vencida</th><th>Destinatario</th><th>Motivo</th>");
            sb.AppendLine("</tr>");
            bool alternar = false;
            foreach (AvisoFacturaVencidaDTO f in filas)
            {
                sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Cliente)}/{PlantillaAvisoFacturaVencida.Html(f.Contacto)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Nombre)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Factura)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.FechaFactura)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(f.Vencimiento)}</td>");
                sb.Append($"<td style='text-align: right;'>{PlantillaAvisoFacturaVencida.FormatearImporte(f.Importe)}</td>");
                sb.Append($"<td style='text-align: center;'>{f.DiasVencida}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Destinatarios)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(f.Motivo)}</td>");
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendTablaNegativos(StringBuilder sb, List<ApunteNegativoClienteDTO> negativos)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.Append("<tr style='background-color: #4A90D9; color: white;'>");
            sb.Append("<th>Cliente</th><th>Nombre</th><th>Nº Orden</th><th>Fecha</th><th>Importe</th><th>Tipo</th><th>Documento</th><th>Concepto</th>");
            sb.AppendLine("</tr>");
            bool alternar = false;
            foreach (ApunteNegativoClienteDTO a in negativos.OrderBy(n => n.Cliente).ThenBy(n => n.Fecha))
            {
                sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(a.Cliente)}/{PlantillaAvisoFacturaVencida.Html(a.Contacto)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(a.Nombre)}</td>");
                sb.Append($"<td style='text-align: right;'>{a.NOrden}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.FormatearFecha(a.Fecha)}</td>");
                sb.Append($"<td style='text-align: right;'>{PlantillaAvisoFacturaVencida.FormatearImporte(a.Importe)}</td>");
                sb.Append($"<td style='text-align: center;'>{PlantillaAvisoFacturaVencida.Html(DescribirTipoApunte(a.TipoApunte))}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(a.Documento)}</td>");
                sb.Append($"<td>{PlantillaAvisoFacturaVencida.Html(a.Concepto)}</td>");
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        internal static string DescribirTipoApunte(string tipo)
        {
            switch (tipo?.Trim())
            {
                case Constantes.ExtractosCliente.TiposApunte.FACTURA: return "1 (factura)";
                case Constantes.ExtractosCliente.TiposApunte.CARTERA: return "2 (cartera)";
                case Constantes.ExtractosCliente.TiposApunte.PAGO: return "3 (pago)";
                case Constantes.ExtractosCliente.TiposApunte.IMPAGADO: return "4 (impagado)";
                default: return tipo ?? string.Empty;
            }
        }
    }
}
