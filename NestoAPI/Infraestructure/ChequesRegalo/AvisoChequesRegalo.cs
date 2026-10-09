using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (aviso): lo que el correo y la nota al pie necesitan de una campaña. Importe, mínimo y fecha salen
    /// de aquí, nunca fijos en el código; la imagen del cheque también (cada campaña lleva la suya: la de octubre dice
    /// «50 €»).
    /// </summary>
    public class CampanaCorreoChequeRegalo
    {
        public string Codigo { get; set; }
        public decimal ImporteBase { get; set; }
        public decimal MinimoCanje { get; set; }
        public DateTime CanjeHasta { get; set; }
        public int DiasEsperaTrasEntrega { get; set; }
        /// <summary>Nombre del recurso incrustado en Resources\ChequesRegalo (cheque50.jpg). Null = correo sin imagen.</summary>
        public string ImagenCorreo { get; set; }
        public bool Activa { get; set; }
    }

    /// <summary>Un cheque generado que aún no se le ha mandado al cliente (FechaAvisoCorreo NULL).</summary>
    public class ChequeRegaloSinAviso
    {
        public int Id { get; set; }
        public string Cliente { get; set; }
        public string EmpresaFactura { get; set; }
        public string FacturaOrigen { get; set; }
        /// <summary>Lo que pasó la última vez: «(sin correo)», «(falló el envío)»… Null si nunca se intentó.</summary>
        public string CorreoAviso { get; set; }
        public string Codigo { get; set; }
        public decimal ImporteBase { get; set; }
        public decimal MinimoCanje { get; set; }
        public DateTime CanjeHasta { get; set; }
        public int DiasEsperaTrasEntrega { get; set; }
        public string ImagenCorreo { get; set; }
        public bool Activa { get; set; }

        public CampanaCorreoChequeRegalo Campana() => new CampanaCorreoChequeRegalo
        {
            Codigo = Codigo,
            ImporteBase = ImporteBase,
            MinimoCanje = MinimoCanje,
            CanjeHasta = CanjeHasta,
            DiasEsperaTrasEntrega = DiasEsperaTrasEntrega,
            ImagenCorreo = ImagenCorreo,
            Activa = Activa
        };
    }

    /// <summary>El cheque que generó una factura, para su nota al pie.</summary>
    public class ChequeRegaloDeFactura
    {
        public decimal ImporteBase { get; set; }
        public decimal MinimoCanje { get; set; }
        public DateTime CanjeHasta { get; set; }
        public DateTime? FechaAvisoCorreo { get; set; }
    }

    /// <summary>A quién se manda: el nombre del cliente y los correos de las facturas (cargo 22), separados por comas.</summary>
    public class DestinatarioChequeRegalo
    {
        public string Nombre { get; set; }
        public string Correos { get; set; }
    }

    /// <summary>El correo compuesto, sin mandar: HTML, texto plano y la imagen que va en línea.</summary>
    public class CorreoChequeRegalo
    {
        public string Asunto { get; set; }
        public string Html { get; set; }
        public string Texto { get; set; }
        public byte[] Imagen { get; set; }
        public string NombreImagen { get; set; }
    }

    public enum ResultadoAvisoChequeRegalo
    {
        Enviado,
        SinCorreo,
        FalloEnvio,
        /// <summary>Otro (la factura o el job) lo estaba mandando a la vez.</summary>
        YaAvisado
    }

    public class AvisoChequeRegalo
    {
        public int IdCheque { get; set; }
        public ResultadoAvisoChequeRegalo Resultado { get; set; }
        public string Correos { get; set; }

        /// <summary>La línea para quien factura (CrearFacturaResponseDTO.Avisos); null si no hay nada que decir.</summary>
        public string TextoParaQuienFactura
        {
            get
            {
                switch (Resultado)
                {
                    case ResultadoAvisoChequeRegalo.Enviado:
                        return $"Le hemos mandado el cheque por correo a {Correos}.";
                    case ResultadoAvisoChequeRegalo.SinCorreo:
                        return "El cliente no tiene correo para las facturas: no le llega el correo del cheque, avísale tú.";
                    case ResultadoAvisoChequeRegalo.FalloEnvio:
                        return "No se ha podido mandar el correo del cheque (se vuelve a intentar esta noche): si puedes, avísale tú.";
                    default:
                        return null;
                }
            }
        }
    }

    /// <summary>
    /// NestoAPI#593: el texto del correo (de Alberto, 09/10/26) y de la nota al pie de la factura. Sin base de datos:
    /// todo lo que cambia (importe, mínimo, fecha, imagen) viene de la campaña.
    /// </summary>
    public static class PlantillaCorreoChequeRegalo
    {
        internal const string ID_IMAGEN = "cheque-regalo";
        internal const string NOMBRE_POR_DEFECTO = "Nombre del cliente";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// Solo &amp;, &lt;, &gt; y comillas: WebUtility.HtmlEncode de .NET Framework convierte también las tildes y «»
        /// en entidades numéricas, y el HTML (UTF-8) queda ilegible para quien lo revise.
        /// </summary>
        internal static string Html(string texto)
            => (texto ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        /// <summary>50 → «50»; 50,5 → «50,50».</summary>
        internal static string Euros(decimal importe)
            => importe == decimal.Truncate(importe) ? importe.ToString("0", es) : importe.ToString("0.00", es);

        /// <summary>«7 de noviembre».</summary>
        internal static string Fecha(DateTime fecha) => fecha.ToString("d 'de' MMMM", es);

        public static string Asunto(CampanaCorreoChequeRegalo campana)
            => $"Con tu factura, {Euros(campana.ImporteBase)} € de descuento para tu próximo pedido";

        internal static string TextoAlternativoImagen(CampanaCorreoChequeRegalo campana)
            => $"Cheque regalo de {Euros(campana.ImporteBase)} € de descuento para tu próximo pedido, hasta el {Fecha(campana.CanjeHasta)}";

        private static string Uso(CampanaCorreoChequeRegalo campana)
        {
            string cuando = campana.DiasEsperaTrasEntrega > 0
                ? $"desde {campana.DiasEsperaTrasEntrega} días después de entregarte entero este pedido"
                : "desde el día de hoy";
            return $"Podrás utilizarlo {cuando} hasta el {Fecha(campana.CanjeHasta)} en un pedido de productos computables " +
                $"superior a {Euros(campana.MinimoCanje)} €, después de los descuentos habituales.";
        }

        internal const string LETRA_PEQUENA = "Importes expresados en base imponible, se aplicará el tratamiento fiscal correspondiente. " +
            "Para alcanzar el mínimo no cuentan portes, reembolso, cuotas, reparaciones, productos de peluquería ni Packs de " +
            "Navidad «PACK 26». Estos productos sí pueden incluirse en el pedido y beneficiarse del descuento. Un solo uso por " +
            "código de cliente.";

        private static List<string> Parrafos(CampanaCorreoChequeRegalo campana, string nombre) => new List<string>
        {
            $"Hola, {nombre}:",
            "Gracias por confiar en Nueva Visión. Junto con tu factura te damos una sorpresa: un cheque regalo de " +
                $"{Euros(campana.ImporteBase)} € de descuento para tu próximo pedido.",
            "Aprovéchalo para reponer tus imprescindibles o probar esa novedad que tienes en mente. " + Uso(campana),
            "¿Qué necesitas para tu centro? Contacta con tu comercial o con nuestra tienda y te ayudaremos a aprovecharlo."
        };

        private const string FIRMA = "El equipo de Nueva Visión.";

        /// <summary>
        /// El correo. <paramref name="srcImagen"/>: «cid:…» para mandarlo (imagen en línea) o un data URI para
        /// previsualizarlo en el navegador; null = sin imagen.
        /// </summary>
        public static CorreoChequeRegalo Componer(CampanaCorreoChequeRegalo campana, string nombreCliente, byte[] imagen, string srcImagen)
        {
            string nombre = string.IsNullOrWhiteSpace(nombreCliente) ? NOMBRE_POR_DEFECTO : nombreCliente.Trim();
            string asunto = Asunto(campana);
            List<string> parrafos = Parrafos(campana, nombre);
            bool conImagen = imagen != null && imagen.Length > 0 && !string.IsNullOrEmpty(srcImagen);

            const string FUENTE = "font-family:Arial,Helvetica,sans-serif;";
            var h = new StringBuilder();
            _ = h.AppendLine("<!DOCTYPE html>");
            _ = h.AppendLine("<html lang=\"es\">");
            _ = h.AppendLine("<head>");
            _ = h.AppendLine("<meta charset=\"utf-8\">");
            _ = h.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            _ = h.AppendLine($"<title>{Html(asunto)}</title>");
            _ = h.AppendLine("</head>");
            _ = h.AppendLine("<body style=\"margin:0;padding:0;background-color:#f4f4f4;\">");
            _ = h.AppendLine("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:#f4f4f4;\">");
            _ = h.AppendLine("<tr><td align=\"center\" style=\"padding:16px 8px;\">");
            _ = h.AppendLine("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"max-width:600px;width:100%;background-color:#ffffff;\">");
            if (conImagen)
            {
                _ = h.AppendLine("<tr><td style=\"padding:0;\">" +
                    $"<img src=\"{Html(srcImagen)}\" width=\"600\" alt=\"{Html(TextoAlternativoImagen(campana))}\" " +
                    "style=\"display:block;width:100%;max-width:600px;height:auto;border:0;\"></td></tr>");
            }
            _ = h.AppendLine($"<tr><td style=\"padding:24px 24px 8px 24px;{FUENTE}font-size:16px;line-height:1.5;color:#222222;\">");
            foreach (string parrafo in parrafos)
            {
                _ = h.AppendLine($"<p style=\"margin:0 0 16px 0;\">{Html(parrafo)}</p>");
            }
            _ = h.AppendLine($"<p style=\"margin:0 0 8px 0;\">{Html(FIRMA)}</p>");
            _ = h.AppendLine("</td></tr>");
            _ = h.AppendLine($"<tr><td style=\"padding:8px 24px 24px 24px;{FUENTE}font-size:12px;line-height:1.4;color:#666666;\">");
            _ = h.AppendLine($"<p style=\"margin:0;\"><strong>Letra pequeña:</strong> {Html(LETRA_PEQUENA)}</p>");
            _ = h.AppendLine("</td></tr>");
            _ = h.AppendLine("</table>");
            _ = h.AppendLine("</td></tr>");
            _ = h.AppendLine("</table>");
            _ = h.AppendLine("</body>");
            _ = h.AppendLine("</html>");

            var t = new StringBuilder();
            foreach (string parrafo in parrafos)
            {
                _ = t.AppendLine(parrafo).AppendLine();
            }
            _ = t.AppendLine(FIRMA).AppendLine();
            _ = t.AppendLine("Letra pequeña: " + LETRA_PEQUENA);

            return new CorreoChequeRegalo
            {
                Asunto = asunto,
                Html = h.ToString(),
                Texto = t.ToString(),
                Imagen = conImagen ? imagen : null,
                NombreImagen = conImagen ? campana.ImagenCorreo : null
            };
        }

        /// <summary>
        /// La nota al pie de la factura que generó el cheque. Si el correo no ha salido (cliente sin correo o fallo),
        /// las condiciones se las da su comercial.
        /// </summary>
        public static string NotaAlPie(ChequeRegaloDeFactura cheque)
        {
            string condiciones = cheque.FechaAvisoCorreo != null
                ? "Condiciones en el correo que te hemos enviado."
                : "Tu comercial o nuestra tienda te explicarán las condiciones.";
            return $"Con esta factura tienes un cheque regalo de {Euros(cheque.ImporteBase)} € para tu próximo pedido de más de " +
                $"{Euros(cheque.MinimoCanje)} € de producto computable, hasta el {cheque.CanjeHasta.ToString("d/M/yyyy", es)}. {condiciones}";
        }
    }

    /// <summary>
    /// Las imágenes del cheque, incrustadas en el ensamblado (Resources\ChequesRegalo, como el sello de Madrid
    /// Excelente): el correo no depende de ninguna URL. El nombre lo dice la campaña (ImagenCorreo).
    /// </summary>
    public static class ImagenesChequeRegalo
    {
        internal const string PREFIJO_RECURSO = "NestoAPI.Resources.ChequesRegalo.";
        private static readonly ConcurrentDictionary<string, byte[]> cache = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Los bytes de la imagen, o null si la campaña no tiene o no existe el recurso.</summary>
        public static byte[] Leer(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            return cache.GetOrAdd(nombre.Trim(), n =>
            {
                using (Stream stream = typeof(ImagenesChequeRegalo).Assembly.GetManifestResourceStream(PREFIJO_RECURSO + n))
                {
                    if (stream == null)
                    {
                        return null;
                    }
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            });
        }

        internal static string TipoMime(string nombre)
            => (nombre ?? string.Empty).EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : MediaTypeNames.Image.Jpeg;
    }

    public interface IRepositorioAvisoChequesRegalo
    {
        /// <summary>False mientras no se lance Issue593_ChequeRegalo_Aviso.sql (sin columnas, no se avisa).</summary>
        Task<bool> ColumnasDisponibles();
        /// <summary>Cheques en estado Generado, sin avisar, de campañas activas y en plazo de canje. Cliente null = todos.</summary>
        Task<List<ChequeRegaloSinAviso>> LeerChequesSinAviso(string cliente);
        /// <summary>La campaña, esté activa o no (para previsualizar). Null si no existe.</summary>
        Task<CampanaCorreoChequeRegalo> LeerCampana(string codigo);
        /// <summary>El nombre y los correos de las facturas del cliente: los del contacto de la factura y, si no tiene, los de otro contacto.</summary>
        Task<DestinatarioChequeRegalo> LeerDestinatario(string cliente, string empresaFactura, string factura);
        /// <summary>Marca el cheque como avisado ANTES de mandar; false si otro ya lo había marcado (no se manda dos veces).</summary>
        Task<bool> ReservarAviso(int id);
        Task ConfirmarAviso(int id, string correos);
        /// <summary>Deshace la reserva (el correo no ha salido) y apunta el motivo, para que la reconciliación lo vuelva a intentar.</summary>
        Task AnularReserva(int id, string motivo);
    }

    public interface IAvisadorChequesRegalo
    {
        /// <summary>Manda el correo de los cheques del cliente aún sin avisar (lo llama la factura que lo genera).</summary>
        Task<List<AvisoChequeRegalo>> AvisarCliente(string cliente);
        /// <summary>Manda los que falten (job de reconciliación). Devuelve cuántos han salido.</summary>
        Task<int> AvisarPendientes();
        /// <summary>El HTML del correo, con la imagen como data URI para verlo en el navegador. Null si la campaña no existe.</summary>
        Task<CorreoChequeRegalo> Previsualizar(string campana, string cliente);
        /// <summary>Manda el correo a una dirección de prueba sin marcar nada. Null si la campaña no existe.</summary>
        Task<bool?> EnviarPrueba(string campana, string correo, string cliente);
    }

    /// <summary>
    /// NestoAPI#593 (aviso): el correo al cliente cuando se genera su cheque. Best-effort, como la generación: un
    /// fallo va a ELMAH y la factura sigue. Para no mandarlo dos veces se marca FechaAvisoCorreo ANTES de mandar
    /// (UPDATE … WHERE FechaAvisoCorreo IS NULL) y se deshace si el correo no sale; el job de reconciliación manda los
    /// que se quedaron sin correo. Remitente y copia oculta, como los correos comerciales (post-compra).
    /// </summary>
    public class AvisadorChequesRegalo : IAvisadorChequesRegalo
    {
        internal const string SIN_CORREO = "(sin correo)";
        internal const string FALLO_ENVIO = "(falló el envío)";
        internal const string REMITENTE_POR_DEFECTO = "nuevavision@nuevavision.es";
        internal const string NOMBRE_REMITENTE = "Nueva Visión";
        internal const string COPIA_OCULTA_POR_DEFECTO = Constantes.Correos.INFORMATICA;

        private readonly IRepositorioAvisoChequesRegalo repositorio;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly Func<string, byte[]> leerImagen;
        private readonly Action<Exception> registrarError;

        public AvisadorChequesRegalo(IRepositorioAvisoChequesRegalo repositorio, IServicioCorreoElectronico servicioCorreo,
            Func<string, byte[]> leerImagen = null, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.servicioCorreo = servicioCorreo ?? throw new ArgumentNullException(nameof(servicioCorreo));
            this.leerImagen = leerImagen ?? ImagenesChequeRegalo.Leer;
            this.registrarError = registrarError ?? (ex => ElmahHelper.Log(ex));
        }

        public static AvisadorChequesRegalo Crear(NVEntities db)
            => new AvisadorChequesRegalo(new RepositorioAvisoChequesRegalo(db), new ServicioCorreoElectronico());

        /// <summary>appSetting ChequesRegalo:Remitente (por defecto nuevavision@, como los correos post-compra).</summary>
        internal static string Remitente
            => ConfigurationManager.AppSettings["ChequesRegalo:Remitente"] ?? REMITENTE_POR_DEFECTO;

        /// <summary>appSetting ChequesRegalo:CopiaOculta (vacío = ninguna; por defecto Informática, como los post-compra).</summary>
        internal static string CopiaOculta
            => ConfigurationManager.AppSettings["ChequesRegalo:CopiaOculta"] ?? COPIA_OCULTA_POR_DEFECTO;

        public async Task<List<AvisoChequeRegalo>> AvisarCliente(string cliente)
        {
            var avisos = new List<AvisoChequeRegalo>();
            if (string.IsNullOrWhiteSpace(cliente) || !await repositorio.ColumnasDisponibles().ConfigureAwait(false))
            {
                return avisos;
            }
            foreach (ChequeRegaloSinAviso cheque in await repositorio.LeerChequesSinAviso(cliente.Trim()).ConfigureAwait(false))
            {
                avisos.Add(await Avisar(cheque).ConfigureAwait(false));
            }
            return avisos;
        }

        public async Task<int> AvisarPendientes()
        {
            if (!await repositorio.ColumnasDisponibles().ConfigureAwait(false))
            {
                return 0;
            }
            int enviados = 0;
            foreach (ChequeRegaloSinAviso cheque in await repositorio.LeerChequesSinAviso(null).ConfigureAwait(false))
            {
                try
                {
                    if ((await Avisar(cheque).ConfigureAwait(false)).Resultado == ResultadoAvisoChequeRegalo.Enviado)
                    {
                        enviados++;
                    }
                }
                catch (Exception ex)
                {
                    // Uno que falle no para los demás
                    registrarError(new Exception($"[Cheques regalo #593] No se pudo avisar del cheque {cheque.Id} " +
                        $"(cliente {cheque.Cliente?.Trim()}): {ex.Message}", ex));
                }
            }
            return enviados;
        }

        public async Task<CorreoChequeRegalo> Previsualizar(string campana, string cliente)
        {
            CampanaCorreoChequeRegalo leida = await repositorio.LeerCampana(campana?.Trim()).ConfigureAwait(false);
            if (leida == null)
            {
                return null;
            }
            DestinatarioChequeRegalo destinatario = string.IsNullOrWhiteSpace(cliente)
                ? null
                : await repositorio.LeerDestinatario(cliente.Trim(), null, null).ConfigureAwait(false);
            byte[] imagen = leerImagen(leida.ImagenCorreo);
            string src = imagen == null ? null : $"data:{ImagenesChequeRegalo.TipoMime(leida.ImagenCorreo)};base64,{Convert.ToBase64String(imagen)}";
            return PlantillaCorreoChequeRegalo.Componer(leida, destinatario?.Nombre, imagen, src);
        }

        public async Task<bool?> EnviarPrueba(string campana, string correo, string cliente)
        {
            CampanaCorreoChequeRegalo leida = await repositorio.LeerCampana(campana?.Trim()).ConfigureAwait(false);
            if (leida == null)
            {
                return null;
            }
            DestinatarioChequeRegalo destinatario = string.IsNullOrWhiteSpace(cliente)
                ? null
                : await repositorio.LeerDestinatario(cliente.Trim(), null, null).ConfigureAwait(false);
            CorreoChequeRegalo compuesto = ComponerParaEnviar(leida, destinatario?.Nombre);
            using (MailMessage mail = CrearMensaje(compuesto, new List<string> { correo.Trim() }, copiaOculta: false))
            {
                mail.Subject = "[PRUEBA] " + mail.Subject;
                return servicioCorreo.EnviarCorreoSMTP(mail);
            }
        }

        private CorreoChequeRegalo ComponerParaEnviar(CampanaCorreoChequeRegalo campana, string nombre)
            => PlantillaCorreoChequeRegalo.Componer(campana, nombre, leerImagen(campana.ImagenCorreo), "cid:" + PlantillaCorreoChequeRegalo.ID_IMAGEN);

        private async Task<AvisoChequeRegalo> Avisar(ChequeRegaloSinAviso cheque)
        {
            DestinatarioChequeRegalo destinatario = await repositorio
                .LeerDestinatario(cheque.Cliente?.Trim(), cheque.EmpresaFactura?.Trim(), cheque.FacturaOrigen?.Trim()).ConfigureAwait(false);
            List<string> correos = CorreosValidos(destinatario?.Correos);
            if (!correos.Any())
            {
                // Se apunta una vez (ELMAH la primera); el job lo mira cada noche por si le ponen el correo
                if (cheque.CorreoAviso?.Trim() != SIN_CORREO)
                {
                    await repositorio.AnularReserva(cheque.Id, SIN_CORREO).ConfigureAwait(false);
                    registrarError(new Exception($"[Cheques regalo #593] El cliente {cheque.Cliente?.Trim()} no tiene correo para " +
                        $"las facturas: no se le ha mandado el correo del cheque de {cheque.Codigo}."));
                }
                return new AvisoChequeRegalo { IdCheque = cheque.Id, Resultado = ResultadoAvisoChequeRegalo.SinCorreo };
            }

            if (!await repositorio.ReservarAviso(cheque.Id).ConfigureAwait(false))
            {
                return new AvisoChequeRegalo { IdCheque = cheque.Id, Resultado = ResultadoAvisoChequeRegalo.YaAvisado };
            }

            string paraQuien = string.Join(", ", correos);
            bool enviado;
            try
            {
                CorreoChequeRegalo compuesto = ComponerParaEnviar(cheque.Campana(), destinatario.Nombre);
                using (MailMessage mail = CrearMensaje(compuesto, correos, copiaOculta: true))
                {
                    enviado = servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                await repositorio.AnularReserva(cheque.Id, FALLO_ENVIO).ConfigureAwait(false);
                registrarError(new Exception($"[Cheques regalo #593] Falló el correo del cheque de {cheque.Cliente?.Trim()} " +
                    $"a {paraQuien}: {ex.Message}", ex));
                return new AvisoChequeRegalo { IdCheque = cheque.Id, Resultado = ResultadoAvisoChequeRegalo.FalloEnvio, Correos = paraQuien };
            }
            if (!enviado)
            {
                await repositorio.AnularReserva(cheque.Id, FALLO_ENVIO).ConfigureAwait(false);
                registrarError(new Exception($"[Cheques regalo #593] No salió el correo del cheque de {cheque.Cliente?.Trim()} a {paraQuien} " +
                    "(se reintenta en la reconciliación de la noche)."));
                return new AvisoChequeRegalo { IdCheque = cheque.Id, Resultado = ResultadoAvisoChequeRegalo.FalloEnvio, Correos = paraQuien };
            }
            await repositorio.ConfirmarAviso(cheque.Id, paraQuien).ConfigureAwait(false);
            return new AvisoChequeRegalo { IdCheque = cheque.Id, Resultado = ResultadoAvisoChequeRegalo.Enviado, Correos = paraQuien };
        }

        internal static List<string> CorreosValidos(string correos)
        {
            var resultado = new List<string>();
            foreach (string trozo in (correos ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                try
                {
                    var direccion = new MailAddress(trozo);
                    if (!resultado.Contains(direccion.Address, StringComparer.OrdinalIgnoreCase))
                    {
                        resultado.Add(direccion.Address);
                    }
                }
                catch (FormatException)
                {
                    // un correo mal escrito en la ficha no impide mandar a los demás
                }
            }
            return resultado;
        }

        /// <summary>HTML con la imagen en línea (CID) y la versión en texto plano.</summary>
        internal static MailMessage CrearMensaje(CorreoChequeRegalo correo, IEnumerable<string> destinatarios, bool copiaOculta)
        {
            var mail = new MailMessage
            {
                From = new MailAddress(Remitente, NOMBRE_REMITENTE),
                Subject = correo.Asunto,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(destinatario);
            }
            if (copiaOculta)
            {
                foreach (string bcc in CorreosValidos(CopiaOculta))
                {
                    mail.Bcc.Add(bcc);
                }
            }
            AlternateView texto = AlternateView.CreateAlternateViewFromString(correo.Texto, Encoding.UTF8, MediaTypeNames.Text.Plain);
            AlternateView html = AlternateView.CreateAlternateViewFromString(correo.Html, Encoding.UTF8, MediaTypeNames.Text.Html);
            if (correo.Imagen != null)
            {
                var imagen = new LinkedResource(new MemoryStream(correo.Imagen), ImagenesChequeRegalo.TipoMime(correo.NombreImagen))
                {
                    ContentId = PlantillaCorreoChequeRegalo.ID_IMAGEN,
                    TransferEncoding = TransferEncoding.Base64
                };
                imagen.ContentType.Name = correo.NombreImagen;
                html.LinkedResources.Add(imagen);
            }
            // El último es el preferido: los clientes que saben HTML lo enseñan; los demás, el texto
            mail.AlternateViews.Add(texto);
            mail.AlternateViews.Add(html);
            return mail;
        }
    }

    /// <summary>NestoAPI#593 (aviso): las columnas del aviso por SQL directo (sin EDMX), como RepositorioChequesRegalo.</summary>
    public class RepositorioAvisoChequesRegalo : IRepositorioAvisoChequesRegalo
    {
        private readonly NVEntities db;

        public RepositorioAvisoChequesRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_COLUMNAS_DISPONIBLES = @"
SELECT CAST(CASE WHEN COL_LENGTH('dbo.ChequesRegalo', 'FechaAvisoCorreo') IS NOT NULL
                  AND COL_LENGTH('dbo.ChequesRegalo', 'CorreoAviso') IS NOT NULL
                  AND COL_LENGTH('dbo.ChequesRegaloCampanas', 'ImagenCorreo') IS NOT NULL THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_CHEQUES_SIN_AVISO = @"
SELECT c.Id, RTRIM(c.Cliente) AS Cliente, RTRIM(c.EmpresaFactura) AS EmpresaFactura, RTRIM(c.FacturaOrigen) AS FacturaOrigen,
       c.CorreoAviso, RTRIM(k.Codigo) AS Codigo, k.ImporteBase, k.MinimoCanje, CAST(k.CanjeHasta AS datetime) AS CanjeHasta,
       k.DiasEsperaTrasEntrega, RTRIM(k.ImagenCorreo) AS ImagenCorreo, k.Activa
FROM dbo.ChequesRegalo c
     INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
WHERE c.FechaAvisoCorreo IS NULL AND c.Estado = 'Generado' AND k.Activa = 1
      AND k.CanjeHasta >= CAST(GETDATE() AS date)
      AND (@p0 = '' OR c.Cliente = @p0)
ORDER BY c.Id";

        internal const string SQL_CAMPANA = @"
SELECT RTRIM(Codigo) AS Codigo, ImporteBase, MinimoCanje, CAST(CanjeHasta AS datetime) AS CanjeHasta, DiasEsperaTrasEntrega,
       RTRIM(ImagenCorreo) AS ImagenCorreo, Activa
FROM dbo.ChequesRegaloCampanas
WHERE Codigo = @p0";

        // Los correos de las facturas (cargo 22, como el envío diario de ServicioFacturas.LeerFacturasDia): primero un
        // contacto que los tenga, y de ellos el de la factura y luego el principal. Los clientes viven en la empresa 1
        // aunque la factura sea de la espejo.
        internal const string SQL_DESTINATARIO = @"
SELECT TOP 1 RTRIM(x.Nombre) AS Nombre, x.Correos
FROM (
    SELECT cl.Nombre, cl.Contacto, cl.ClientePrincipal,
           STUFF((SELECT ', ' + RTRIM(p.[CorreoElectrónico])
                  FROM dbo.PersonasContactoCliente p
                  WHERE p.Empresa = cl.Empresa AND p.[NºCliente] = cl.[Nº Cliente] AND p.Contacto = cl.Contacto
                        AND p.Cargo = @p2 AND p.[CorreoElectrónico] IS NOT NULL AND RTRIM(p.[CorreoElectrónico]) <> ''
                  FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') AS Correos
    FROM dbo.Clientes cl
    WHERE cl.Empresa = @p0 AND cl.[Nº Cliente] = @p1
) x
ORDER BY CASE WHEN x.Correos IS NULL THEN 1 ELSE 0 END,
         CASE WHEN x.Contacto = (SELECT f.Contacto FROM dbo.CabFacturaVta f WHERE f.Empresa = @p3 AND f.[Número] = @p4) THEN 0 ELSE 1 END,
         CASE WHEN x.ClientePrincipal = 1 THEN 0 ELSE 1 END,
         x.Contacto";

        internal const string SQL_RESERVAR = @"
UPDATE dbo.ChequesRegalo SET FechaAvisoCorreo = GETDATE() WHERE Id = @p0 AND FechaAvisoCorreo IS NULL";

        internal const string SQL_CONFIRMAR = @"
UPDATE dbo.ChequesRegalo SET CorreoAviso = @p1 WHERE Id = @p0";

        internal const string SQL_ANULAR = @"
UPDATE dbo.ChequesRegalo SET FechaAvisoCorreo = NULL, CorreoAviso = @p1 WHERE Id = @p0";

        // Para la nota al pie. Dinámico porque se lee en cada PDF de factura y la columna FechaAvisoCorreo puede no
        // existir aún (script sin lanzar): sin ella, como si no hubiera cheque.
        internal const string SQL_CHEQUE_DE_FACTURA = @"
IF OBJECT_ID('dbo.ChequesRegalo') IS NULL OR COL_LENGTH('dbo.ChequesRegalo', 'FechaAvisoCorreo') IS NULL
    SELECT CAST(0 AS decimal(10,2)) AS ImporteBase, CAST(0 AS decimal(10,2)) AS MinimoCanje, GETDATE() AS CanjeHasta,
           CAST(NULL AS datetime) AS FechaAvisoCorreo
    WHERE 1 = 0
ELSE
    EXEC sp_executesql N'
        SELECT TOP 1 k.ImporteBase, k.MinimoCanje, CAST(k.CanjeHasta AS datetime) AS CanjeHasta, c.FechaAvisoCorreo
        FROM dbo.ChequesRegalo c
             INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
        WHERE c.EmpresaFactura = @empresa AND c.FacturaOrigen = @factura AND c.Estado = ''Generado''
              AND k.CanjeHasta >= CAST(GETDATE() AS date)',
        N'@empresa char(3), @factura char(10)', @empresa = @p0, @factura = @p1";

        public async Task<bool> ColumnasDisponibles()
            => await db.Database.SqlQuery<bool>(SQL_COLUMNAS_DISPONIBLES).FirstAsync().ConfigureAwait(false);

        public async Task<List<ChequeRegaloSinAviso>> LeerChequesSinAviso(string cliente)
            => await db.Database.SqlQuery<ChequeRegaloSinAviso>(SQL_CHEQUES_SIN_AVISO,
                new SqlParameter("@p0", cliente?.Trim() ?? string.Empty)).ToListAsync().ConfigureAwait(false);

        public async Task<CampanaCorreoChequeRegalo> LeerCampana(string codigo)
            => string.IsNullOrWhiteSpace(codigo)
                ? null
                : await db.Database.SqlQuery<CampanaCorreoChequeRegalo>(SQL_CAMPANA, new SqlParameter("@p0", codigo.Trim()))
                    .FirstOrDefaultAsync().ConfigureAwait(false);

        public async Task<DestinatarioChequeRegalo> LeerDestinatario(string cliente, string empresaFactura, string factura)
            => await db.Database.SqlQuery<DestinatarioChequeRegalo>(SQL_DESTINATARIO,
                new SqlParameter("@p0", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", cliente ?? string.Empty),
                new SqlParameter("@p2", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO),
                new SqlParameter("@p3", (object)empresaFactura ?? string.Empty),
                new SqlParameter("@p4", (object)factura ?? string.Empty)).FirstOrDefaultAsync().ConfigureAwait(false);

        public async Task<bool> ReservarAviso(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR, new SqlParameter("@p0", id)).ConfigureAwait(false) > 0;

        public async Task ConfirmarAviso(int id, string correos)
            => await db.Database.ExecuteSqlCommandAsync(SQL_CONFIRMAR, new SqlParameter("@p0", id),
                new SqlParameter("@p1", Recortar(correos))).ConfigureAwait(false);

        public async Task AnularReserva(int id, string motivo)
            => await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR, new SqlParameter("@p0", id),
                new SqlParameter("@p1", Recortar(motivo))).ConfigureAwait(false);

        /// <summary>Síncrono: lo pide GestorFacturas.LeerFactura al componer el PDF. Null si la factura no generó cheque.</summary>
        public ChequeRegaloDeFactura LeerChequeDeFactura(string empresa, string numeroFactura)
            => db.Database.SqlQuery<ChequeRegaloDeFactura>(SQL_CHEQUE_DE_FACTURA,
                new SqlParameter("@p0", empresa?.Trim() ?? string.Empty),
                new SqlParameter("@p1", numeroFactura?.Trim() ?? string.Empty)).FirstOrDefault();

        private static string Recortar(string texto)
            => texto == null ? string.Empty : (texto.Length > 500 ? texto.Substring(0, 500) : texto);
    }
}
