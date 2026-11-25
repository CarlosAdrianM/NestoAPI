using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857: un seguimiento encontrado en un correo. Url null = solo el código (p. ej. en el asunto).</summary>
    public class SeguimientoCorreo
    {
        public string Codigo { get; set; }
        public string Url { get; set; }
        /// <summary>
        /// NestoAPI#857g: la agencia que lleva ESTE código si no es la del correo (un intermediario como MBE manda el código del mensajero
        /// real: «Courier: UPS»). Null = la del correo. Su enlace lo compone esa agencia.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string Agencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#857g: lo que cuenta del ENVÍO el correo de una agencia más allá del código: quién lo expide, la referencia que le ha dado el
    /// remitente (el pedido del proveedor: «Ref.: SO252231WKOUT77626») y, en un INTERMEDIARIO (MBE), el mensajero real que lo lleva. Lo
    /// saca la clase de la agencia (<see cref="IAgenciaEntrada.LeerDatosEnvio"/>); fuera de ella no se sabe nada de su formato.
    /// </summary>
    public class DatosEnvioAgencia
    {
        /// <summary>Quién expide el paquete, normalizado (<see cref="ExtractorRemitentePaquete.Normalizar"/>); null = el extractor común.</summary>
        public string RemitentePaquete { get; set; }
        /// <summary>Las referencias del remitente (su pedido), tal cual.</summary>
        public List<string> ReferenciasRemitente { get; set; } = new List<string>();
        /// <summary>
        /// 25/11/26: números del correo que la agencia SABE que no son un pedido nuestro (la referencia del remitente de DHL, «Ref.Cliente.:»).
        /// La asignación no los cuenta como número de pedido.
        /// </summary>
        public List<string> NumerosQueNoSonPedido { get; set; } = new List<string>();
        /// <summary>Intermediario: la agencia que lleva el paquete de verdad (sus correos son los que dicen la entrega); null si no lo es.</summary>
        public IAgenciaEntrada Mensajero { get; set; }
        /// <summary>True/false si la agencia sabe si es una salida NUESTRA (un resumen de MBE con nuestros envíos); null = la regla común.</summary>
        public bool? EsSalidaNuestra { get; set; }
    }

    /// <summary>
    /// NestoAPI#857: una agencia que nos TRAE mercancía de un proveedor (no las de nuestros envíos: <c>PerfilesAgencias</c>). Una
    /// clase por agencia, registrada en <see cref="AgenciasEntradaRegistradas"/>. La predicción de la fase 3 es una constante por
    /// agencia (<see cref="HorasHastaEntrega"/>) desde que consta que la mercancía está en ella.
    /// </summary>
    /// <remarks>
    /// NestoAPI#857f (nivel 2, Carlos 19/11/26): todo lo propio de una agencia (dominios, formato de su código, enlace de seguimiento, cómo
    /// dice la entrega en SUS correos y, si sabe, cómo se le pregunta: <see cref="IConsultaEntregaAgencia"/>) vive en su clase; el resto del
    /// código solo habla con esta interfaz. Los proveedores que mandan por la misma agencia usan la misma clase.
    /// </remarks>
    public interface IAgenciaEntrada
    {
        string Nombre { get; }
        int HorasHastaEntrega { get; }
        /// <summary>
        /// NestoAPI#857g: <see cref="HorasHastaEntrega"/> se cuenta en días LABORABLES de Algete (sin fines de semana ni festivos: DB Schenker,
        /// 72 h = 3 laborables) y no en horas naturales. El cálculo es el común (<see cref="PlazoAgenciaEntrada"/>).
        /// </summary>
        bool PlazoEnLaborables { get; }
        /// <summary>NestoAPI#857g: quién expide, la referencia del remitente y, si es un intermediario, el mensajero real; null si no sabe nada.</summary>
        DatosEnvioAgencia LeerDatosEnvio(CorreoCompras correo);
        bool Reconoce(CorreoCompras correo);
        List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo);
        /// <summary>NestoAPI#857f: ¿lo manda la propia agencia (remitente, o remitente original de un reenvío, de sus dominios)?</summary>
        bool EsCorreoPropio(CorreoCompras correo);
        /// <summary>
        /// NestoAPI#857f: la entrega estimada y el estado que dice un correo DE LA PROPIA AGENCIA (<see cref="EsCorreoPropio"/>); null si no es
        /// suyo o no dice nada del envío. Por defecto, el extractor común (<see cref="ExtractorEntregaEstimada"/>) sin estado.
        /// </summary>
        EntregaAgencia LeerEntrega(CorreoCompras correo);
        /// <summary>NestoAPI#857f: el enlace público de seguimiento a partir del código; null si no se sabe.</summary>
        string UrlSeguimiento(string codigo);
        /// <summary>
        /// NestoAPI#857i: la delegación de ORIGEN del envío que va en su propio código, como clave corta («VIC» de «ESVIC262013788», DB
        /// Schenker); null si la agencia no la pone en el código o el código no es suyo. Sirve para aprender de qué proveedor suele ser
        /// lo que sale de allí (<see cref="ReglaOrigenAgencia"/>).
        /// </summary>
        string OrigenDelEnvio(string codigo);
        /// <summary>NestoAPI#857i: el nombre de esa delegación si la agencia lo sabe («Vic»); null si no (se enseña la clave).</summary>
        string NombreOrigen(string origen);
    }

    /// <summary>
    /// Núcleo común: una agencia es sus dominios (de los enlaces y del remitente), los parámetros donde lleva el código y, si
    /// hace falta, patrones del código en la ruta o en el texto. Un enlace de su dominio solo cuenta como seguimiento si trae un
    /// código o la ruta habla de seguimiento (los de la portada, la privacidad o las redes sociales no).
    /// </summary>
    public abstract class AgenciaEntradaPorPatrones : IAgenciaEntrada
    {
        public const int HORAS_PAQUETERIA = 24;
        public const int HORAS_GRUPAJE = 48;
        /// <summary>NestoAPI#857g: 3 días laborables (DB Schenker, Carlos 19/11/26).</summary>
        public const int HORAS_GRUPAJE_LABORABLES = 72;

        /// <summary>Parámetros que suelen llevar el código en cualquier agencia (después de los suyos).</summary>
        internal static readonly string[] ParametrosComunes =
        {
            "tracking", "trackingnumber", "tracking_number", "tracking-number", "trackingid", "tracking-id", "tracknum", "track",
            "trknbr", "awb", "codigo", "code", "ref", "reference", "referencia", "numero", "num", "envio", "expedicion", "albaran",
            "localizador", "shipment", "id", "q"
        };

        /// <summary>Palabras de la ruta o del host que dicen «esto es un seguimiento».</summary>
        internal static readonly string[] PalabrasSeguimiento =
        {
            "track", "seguimiento", "localiza", "rastreo", "trazab", "tracing", "sendungsverfolgung", "shipment", "expedicion",
            "consulta-envio", "consultaenvio", "estado-envio", "estadoenvio"
        };

        /// <summary>Enlaces que nunca son de seguimiento aunque sean de la agencia.</summary>
        internal static readonly string[] PalabrasExcluidas =
        {
            "unsubscribe", "darse-de-baja", "baja", "privacidad", "privacy", "cookie", "aviso-legal", "legal", "facebook.", "twitter.",
            "linkedin.", "instagram.", "youtube.", "tiktok.", "//x.com/", ".png", ".jpg", ".jpeg", ".gif", ".css", ".svg", "/static/", "/assets/",
            "apps.apple.com", "play.google.com"
        };

        private static readonly Regex CodigoValido = new Regex(@"^[A-Za-z0-9][A-Za-z0-9\-]{5,39}$", RegexOptions.Compiled);

        /// <summary>
        /// Código con etiqueta en el texto: «Nº de seguimiento: X», «tracking number X», «localizador X», «expedición X». Entre la
        /// etiqueta y el código puede ir el nombre corto de la agencia («Nº de seguimiento GLS 1378206148»).
        /// </summary>
        internal static readonly Regex CodigoConEtiqueta = new Regex(
            @"(?:n[ºo°]\.?\s*de\s*(?:seguimiento|env[ií]o|expedici[oó]n)|n[uú]mero\s+de\s+(?:seguimiento|env[ií]o|expedici[oó]n)|c[oó]digo\s+de\s+(?:seguimiento|env[ií]o)|seguimiento|tracking(?:\s*(?:number|n[ºo°]|id|code))?|localizador|expedici[oó]n|sendungsnummer|awb)(?:\s+MBE)?\s*(?:n[ºo°]\.?)?\s*[:#]?\s*(?:(?:GLS|UPS|DHL|SEUR|MRW|TNT|DPD|CTT|NACEX|TIPSA)\s+)?([A-Za-z0-9][A-Za-z0-9\-]{5,39})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public abstract string Nombre { get; }
        public virtual int HorasHastaEntrega => HORAS_PAQUETERIA;
        public virtual bool PlazoEnLaborables => false;
        public virtual DatosEnvioAgencia LeerDatosEnvio(CorreoCompras correo) => null;
        /// <summary>Dominios de la agencia (también subdominios: «gls-spain.es» vale para «mygls.gls-spain.es»).</summary>
        protected abstract string[] Dominios { get; }
        /// <summary>Parámetros propios de la agencia donde va el código (se miran antes que los comunes).</summary>
        protected virtual string[] ParametrosCodigo => new string[0];
        /// <summary>Patrones del código en la ruta (grupo 1).</summary>
        protected virtual Regex[] PatronesRuta => new Regex[0];
        /// <summary>Un formato de código tan propio que basta para reconocer la agencia aunque no haya enlace (p. ej. el 1Z de UPS).</summary>
        protected virtual Regex PatronInconfundible => null;
        /// <summary>
        /// Nombres con los que sale en el texto. Sirven para poner la agencia a un correo de un proveedor que solo la nombra tras una
        /// etiqueta («Transportista: …», «Enviado por: …»): <see cref="AgenciaPorNombreEnTexto"/>.
        /// </summary>
        public virtual string[] NombresEnTexto => new[] { Nombre };

        public bool EsDominio(string host) => !string.IsNullOrEmpty(host) && Dominios.Concat(DominiosRemitente).Any(d => TextoCorreo.HostDe(host, d));

        /// <summary>NestoAPI#857f: dominios desde los que manda sus correos, si no son ya de <see cref="Dominios"/> (p. ej. su plataforma de envíos).</summary>
        protected virtual string[] DominiosRemitente => new string[0];

        public virtual bool EsCorreoPropio(CorreoCompras correo)
        {
            if (correo == null)
            {
                return false;
            }
            return EsDominio(correo.RemitenteDominio)
                || TextoCorreo.RemitentesOriginales(correo.Texto).Any(r => EsDominio(TextoCorreo.Dominio(r)));
        }

        public virtual EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            EntregaEstimada entrega = ExtractorEntregaEstimada.Extraer(correo.Texto, correo.Recibido);
            return entrega == null ? null : new EntregaAgencia { Entrega = entrega };
        }

        public virtual string UrlSeguimiento(string codigo) => null;
        /// <summary>NestoAPI#857i: por defecto la agencia no pone el origen en el código.</summary>
        public virtual string OrigenDelEnvio(string codigo) => null;
        public virtual string NombreOrigen(string origen) => null;

        /// <summary>Para las clases de agencia: el código listo para ir en una URL (null si no es un código).</summary>
        protected static string CodigoParaUrl(string codigo)
        {
            string limpio = codigo?.Replace(" ", string.Empty).Trim();
            return EsCodigo(limpio) ? Uri.EscapeDataString(limpio) : null;
        }

        public virtual bool Reconoce(CorreoCompras correo)
        {
            if (correo == null)
            {
                return false;
            }
            if (EsDominio(correo.RemitenteDominio) || correo.Enlaces.Any(e => EsDominio(ExtractorEnlaces.Host(e))))
            {
                return true;
            }
            return PatronInconfundible != null && (PatronInconfundible.IsMatch(correo.Asunto ?? string.Empty) || PatronInconfundible.IsMatch(correo.Texto));
        }

        public virtual List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            var resultado = new List<SeguimientoCorreo>();
            if (correo == null)
            {
                return resultado;
            }
            foreach (string enlace in correo.Enlaces)
            {
                if (!Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) || !EsDominio(uri.Host) || Excluido(enlace))
                {
                    continue;
                }
                string codigo = CodigoDelEnlace(uri);
                if (codigo != null || HablaDeSeguimiento(uri.Host + uri.AbsolutePath))
                {
                    Anadir(resultado, codigo, enlace);
                }
            }
            if (!resultado.Any() && Reconoce(correo))
            {
                foreach (string codigo in CodigosEnTexto(correo))
                {
                    Anadir(resultado, codigo, null);
                }
            }
            return resultado;
        }

        /// <summary>Códigos del asunto y del cuerpo de un correo que ya se sabe que es de la agencia.</summary>
        protected virtual IEnumerable<string> CodigosEnTexto(CorreoCompras correo)
        {
            string texto = (correo.Asunto ?? string.Empty) + "\n" + correo.Texto;
            if (PatronInconfundible != null)
            {
                foreach (Match m in PatronInconfundible.Matches(texto))
                {
                    yield return m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                }
            }
            foreach (Match m in CodigoConEtiqueta.Matches(texto))
            {
                if (EsCodigo(m.Groups[1].Value))
                {
                    yield return m.Groups[1].Value;
                }
            }
        }

        internal string CodigoDelEnlace(Uri uri)
        {
            NameValueCollection parametros = string.IsNullOrEmpty(uri.Query) ? new NameValueCollection() : HttpUtility.ParseQueryString(uri.Query);
            var claves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string k in parametros.AllKeys.Where(k => k != null && !claves.ContainsKey(k)))
            {
                claves[k] = parametros[k];
            }
            foreach (string nombre in ParametrosCodigo.Concat(ParametrosComunes))
            {
                if (claves.TryGetValue(nombre, out string valor) && valor != null)
                {
                    // Algunos admiten varios separados por comas: el primero
                    string primero = valor.Split(',', ';', ' ').Select(v => v.Trim()).FirstOrDefault(EsCodigo);
                    if (primero != null)
                    {
                        return primero;
                    }
                }
            }
            foreach (Regex patron in PatronesRuta)
            {
                Match m = patron.Match(uri.AbsolutePath);
                if (m.Success && EsCodigo(m.Groups[1].Value))
                {
                    return m.Groups[1].Value;
                }
            }
            // Último tramo de una ruta de seguimiento: …/track/ABC123456
            string[] tramos = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (tramos.Length >= 2 && HablaDeSeguimiento(string.Join("/", tramos.Take(tramos.Length - 1))))
            {
                string ultimo = Uri.UnescapeDataString(tramos[tramos.Length - 1]);
                if (EsCodigo(ultimo))
                {
                    return ultimo;
                }
            }
            return null;
        }

        internal static bool EsCodigo(string valor) =>
            !string.IsNullOrEmpty(valor) && CodigoValido.IsMatch(valor) && valor.Any(char.IsDigit);

        internal static bool HablaDeSeguimiento(string texto)
        {
            string t = (texto ?? string.Empty).ToLowerInvariant();
            return PalabrasSeguimiento.Any(p => t.Contains(p));
        }

        internal static bool Excluido(string enlace)
        {
            string t = (enlace ?? string.Empty).ToLowerInvariant();
            return PalabrasExcluidas.Any(p => t.Contains(p));
        }

        internal static void Anadir(List<SeguimientoCorreo> lista, string codigo, string url)
        {
            if (lista.Any(s => (url != null && string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase))
                || (url == null && codigo != null && string.Equals(s.Codigo, codigo, StringComparison.OrdinalIgnoreCase))))
            {
                return;
            }
            lista.Add(new SeguimientoCorreo { Codigo = codigo, Url = url });
        }
    }

    /// <summary>
    /// NestoAPI#857: el genérico, para DESCUBRIR agencias nuevas. Capta cualquier enlace que hable de seguimiento (tracking,
    /// seguimiento, localizador…) de un dominio que no sea de una agencia conocida ni nuestro, y los códigos con etiqueta del
    /// texto. Lo que capta se guarda sin agencia (salvo que el texto la nombre tras una etiqueta: <see cref="AgenciaPorNombreEnTexto"/>): sus remitentes salen en GET api/Compras/AgenciasEntrada/Candidatos.
    /// </summary>
    public class AgenciaEntradaGenerica : AgenciaEntradaPorPatrones
    {
        public const string NOMBRE = "(genérico)";
        public static readonly string[] DominiosPropios = { "nuevavision.es" };

        public override string Nombre => NOMBRE;
        protected override string[] Dominios => new string[0];

        public override bool Reconoce(CorreoCompras correo) => ExtraerSeguimientos(correo).Any();

        public override List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            var resultado = new List<SeguimientoCorreo>();
            if (correo == null)
            {
                return resultado;
            }
            foreach (string enlace in correo.Enlaces)
            {
                // NestoAPI#857e: el rastreo de clics de un boletín («track.ejemplo.es/click.php?L=…&J=…») no es un seguimiento
                if (!Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) || Excluido(enlace)
                    || DominiosPropios.Any(d => TextoCorreo.HostDe(uri.Host, d)) || RedirectorMailing.Es(uri))
                {
                    continue;
                }
                if (HablaDeSeguimiento(uri.Host + uri.AbsolutePath + uri.Query))
                {
                    Anadir(resultado, CodigoDelEnlace(uri), enlace);
                }
            }
            if (!resultado.Any())
            {
                string texto = (correo.Asunto ?? string.Empty) + "\n" + correo.Texto;
                foreach (Match m in CodigoConEtiqueta.Matches(texto))
                {
                    if (EsCodigo(m.Groups[1].Value))
                    {
                        Anadir(resultado, m.Groups[1].Value, null);
                    }
                }
            }
            return resultado;
        }
    }

    /// <summary>
    /// NestoAPI#857: las agencias conocidas, en orden (la primera que saca algo, gana). Agencia nueva = una clase aquí. Dominios y
    /// parámetros de la primera tanda; se afinan con lo que se capte (Extracto y GET …/Candidatos).
    /// </summary>
    public static class AgenciasEntradaRegistradas
    {
        public static readonly IReadOnlyList<IAgenciaEntrada> Todas = new List<IAgenciaEntrada>
        {
            new AgenciaEntradaGls(), new AgenciaEntradaSeur(), new AgenciaEntradaMrw(), new AgenciaEntradaDhl(), new AgenciaEntradaUps(),
            new AgenciaEntradaCorreosExpress(), new AgenciaEntradaCorreos(), new AgenciaEntradaCttExpress(), new AgenciaEntradaNacex(),
            new AgenciaEntradaTntFedex(), new AgenciaEntradaDachser(), new AgenciaEntradaTipsa(),
            new AgenciaEntradaEnvialia(), new AgenciaEntradaZeleris(), new AgenciaEntradaRedurRhenus(), new AgenciaEntradaXpoGxo(),
            new AgenciaEntradaSchenker(), new AgenciaEntradaTxt(), new AgenciaEntradaDpd(), new AgenciaEntradaSending(), new AgenciaEntradaPaack(),
            new AgenciaEntradaPalletways(), new AgenciaEntradaMbe()
        }.AsReadOnly();

        public static readonly IAgenciaEntrada Generica = new AgenciaEntradaGenerica();

        /// <summary>La agencia conocida con ese nombre (sin distinguir mayúsculas), o null.</summary>
        public static IAgenciaEntrada PorNombre(string nombre) =>
            string.IsNullOrWhiteSpace(nombre) ? null : Todas.FirstOrDefault(a => string.Equals(a.Nombre, nombre.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>¿El host es de alguna agencia conocida?</summary>
        public static bool EsDeAgenciaConocida(string host) => Todas.OfType<AgenciaEntradaPorPatrones>().Any(a => a.EsDominio(host));
    }

    /// <summary>
    /// NestoAPI#857: la agencia de un correo de un PROVEEDOR que solo la nombra en el texto («Transportista: SCHENKER STANDARD»,
    /// «El pedido será enviado por: MAILBOXES»). Solo con etiqueta: el nombre suelto («gracias por confiar en Correos») no cuenta.
    /// Tras la etiqueta, el nombre (<see cref="AgenciaEntradaPorPatrones.NombresEnTexto"/>) tiene que estar en lo que queda de la
    /// línea (como mucho <see cref="VENTANA"/> caracteres), como palabra entera, sin distinguir mayúsculas ni tildes. Si casan
    /// varios, gana el nombre más largo («Correos Express» antes que «Correos»).
    /// </summary>
    public static class AgenciaPorNombreEnTexto
    {
        public const int VENTANA = 40;

        /// <summary>Etiquetas (en minúsculas y sin tildes) tras las que va el nombre de la agencia.</summary>
        private static readonly Regex Etiqueta = new Regex(
            @"(?<![a-z0-9])(?:transportista|transporte|agencia(?:\s+de\s+transporte)?|empresa\s+de\s+transporte|operador\s+logistico|mensajeria|courier|carrier"
            + @"|(?:enviad[oa]s?|expedid[oa]s?|remitid[oa]s?|despachad[oa]s?)\s+(?:con|por|mediante|a\s+traves\s+de)"
            + @"|(?:se\s+)?enviara\s+(?:con|por|mediante)"
            + @"|(?:shipped|sent|dispatched)\s+(?:via|with|by)|shipping\s+(?:carrier|company|method)|delivered\s+by"
            + @"|versand\s+(?:mit|durch|via|uber))(?![a-z0-9])\s*[:\-–]?",
            RegexOptions.Compiled);

        public static IAgenciaEntrada Buscar(CorreoCompras correo, IEnumerable<IAgenciaEntrada> agencias) =>
            correo == null ? null : Buscar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto, agencias);

        /// <summary>NestoAPI#857e: lo mismo sobre un texto suelto (el de un PDF adjunto).</summary>
        public static IAgenciaEntrada Buscar(string texto, IEnumerable<IAgenciaEntrada> agencias)
        {
            if (string.IsNullOrEmpty(texto) || agencias == null)
            {
                return null;
            }
            List<string> ventanas = Ventanas(texto);
            if (!ventanas.Any())
            {
                return null;
            }
            IAgenciaEntrada mejor = null;
            int longitudMejor = 0;
            foreach (AgenciaEntradaPorPatrones agencia in agencias.OfType<AgenciaEntradaPorPatrones>())
            {
                if (agencia is AgenciaEntradaGenerica)
                {
                    continue;
                }
                foreach (string nombre in (agencia.NombresEnTexto ?? new string[0]).Select(TextoCorreo.Normalizar).Where(n => n.Length > 0))
                {
                    if (nombre.Length > longitudMejor && ventanas.Any(v => (" " + v + " ").Contains(" " + nombre + " ")))
                    {
                        mejor = agencia;
                        longitudMejor = nombre.Length;
                    }
                }
            }
            return mejor;
        }

        /// <summary>Lo que sigue a cada etiqueta en su línea (normalizado: mayúsculas, sin tildes ni signos).</summary>
        internal static List<string> Ventanas(string texto)
        {
            var ventanas = new List<string>();
            foreach (string linea in (texto ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string minusculas = SinTildes(linea).ToLowerInvariant();
                foreach (Match m in Etiqueta.Matches(minusculas))
                {
                    int inicio = m.Index + m.Length;
                    string resto = linea.Substring(inicio, Math.Min(VENTANA, linea.Length - inicio));
                    string normalizado = TextoCorreo.Normalizar(resto);
                    if (normalizado.Length > 0)
                    {
                        ventanas.Add(normalizado);
                    }
                }
            }
            return ventanas;
        }

        /// <summary>Quita las tildes conservando la longitud (para que los índices valgan en el texto original).</summary>
        private static string SinTildes(string texto)
        {
            var sb = new System.Text.StringBuilder(texto.Length);
            foreach (char c in texto)
            {
                // NestoAPI#857f: un emoji (par sustituto) no se puede normalizar suelto: espacio (misma longitud)
                if (char.IsSurrogate(c))
                {
                    sb.Append(' ');
                    continue;
                }
                string d = c.ToString().Normalize(System.Text.NormalizationForm.FormD);
                sb.Append(d.Length > 0 ? d[0] : c);
            }
            return sb.ToString();
        }
    }

    #region Agencias (solo datos)

    public class AgenciaEntradaGls : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "GLS";
        protected override string[] Dominios => new[] { "gls-spain.es", "gls-spain.com", "gls-group.eu", "gls-group.com", "gls-group.net", "gls-info.nl", "gls-pakete.de", "gls-italy.com" };
        protected override string[] ParametrosCodigo => new[] { "match", "codigo", "refs", "ref", "parcelno", "txtcodigo" };
        protected override Regex[] PatronesRuta => new[] { new Regex(@"/track/([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase) };
    }

    /// <summary>
    /// SEUR. NestoAPI#857f (19/11/26): sus avisos (infoenvios@mail.seur.info) NO traen el código: todos los enlaces son redirecciones
    /// opacas (click.mail.seur.info/?qs=…). La identidad del envío es la referencia del remitente («Tu envío PV26-56989 de Postquam en…»),
    /// que se toma como código. Entrega: «Entrega prevista Jueves 19 de Noviembre» (sin año) o «Se entregará HOY de 13:45 a 14:45 horas».
    /// El cuerpo pinta la barra de estados entera («En camino En reparto Entregado»): el estado sale del asunto y de frases, no de esa barra.
    /// </summary>
    public class AgenciaEntradaSeur : AgenciaEntradaPorPatrones
    {
        private const string MESES = "enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre";
        private static readonly string[] NombresMeses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

        // Sobre el texto normalizado (minúsculas y sin tildes: ExtractorEntregaEstimada.Normalizar)
        private static readonly Regex Prevista = new Regex(
            @"entrega\s+prevista\s*:?\s*(?:(?:lunes|martes|miercoles|jueves|viernes|sabado|domingo)\s*,?\s*)?(\d{1,2})\s+de\s+(" + MESES + @")(?:\s+(?:de\s+)?(\d{4}))?(?!\d)",
            RegexOptions.Compiled);
        private static readonly Regex HoyOManana = new Regex(
            @"se\s+entregara\s+(hoy|manana)(?:\s*,?\s*(?:de|entre)(?:\s+las)?\s+(\d{1,2})[:.h](\d{2})\s*(?:a|y|-|hasta)(?:\s+las)?\s*(\d{1,2})[:.h](\d{2}))?",
            RegexOptions.Compiled);
        private static readonly Regex EnReparto = new Regex(@"(?<![a-z])(?:esta|va)\s+en\s+reparto(?![a-z])|se\s+entregara\s+(?:hoy|manana)", RegexOptions.Compiled);
        private static readonly Regex Entregado = new Regex(@"(?<![a-z])(?:ha\s+sido\s+entregad[oa]|se\s+ha\s+entregado|(?:envio|paquete)\s+entregad[oa])(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex Reprogramado = new Regex(@"(?<![a-z])(?:reprogramad[oa]|nueva\s+fecha\s+de\s+entrega)(?![a-z])", RegexOptions.Compiled);
        /// <summary>«Tu envío PV26-56989 de Postquam»: la referencia del remitente, delante del «de».</summary>
        private static readonly Regex Referencia = new Regex(@"(?<![\p{L}\p{N}])(?:tu|su)\s+env[ií]o\s+([A-Za-z0-9][A-Za-z0-9\-]{5,39})\s+de\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override string Nombre => "SEUR";
        protected override string[] Dominios => new[] { "seur.com", "seur.es", "seur.info" };
        protected override string[] DominiosRemitente => new[] { "mail.seur.info" };
        protected override string[] ParametrosCodigo => new[] { "segonlineidentificador", "identificador", "expedicion", "id" };

        public override List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            List<SeguimientoCorreo> resultado = base.ExtraerSeguimientos(correo);
            if (resultado.Any() || !EsCorreoPropio(correo))
            {
                return resultado;
            }
            foreach (Match m in Referencia.Matches((correo.Asunto ?? string.Empty) + "\n" + correo.Texto))
            {
                if (EsCodigo(m.Groups[1].Value))
                {
                    Anadir(resultado, m.Groups[1].Value, null);
                }
            }
            return resultado;
        }

        public override EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            string asunto = ExtractorEntregaEstimada.Normalizar(correo.Asunto ?? string.Empty);
            string texto = ExtractorEntregaEstimada.Normalizar(correo.Texto ?? string.Empty);
            if (Entregado.IsMatch(asunto) || Entregado.IsMatch(texto))
            {
                return new EntregaAgencia { Estado = EstadoEnvioAgencia.Entregado };
            }
            EntregaEstimada entrega = EntregaHoyOManana(texto, correo.Recibido) ?? EntregaPrevista(texto, correo.Recibido)
                ?? ExtractorEntregaEstimada.Extraer(correo.Texto, correo.Recibido);
            EstadoEnvioAgencia? estado = Reprogramado.IsMatch(asunto) || Reprogramado.IsMatch(texto) ? EstadoEnvioAgencia.Reprogramada
                : EnReparto.IsMatch(asunto) || EnReparto.IsMatch(texto) ? EstadoEnvioAgencia.EnReparto
                : entrega != null ? EstadoEnvioAgencia.EnCamino
                : (EstadoEnvioAgencia?)null;
            return estado.HasValue || entrega != null ? new EntregaAgencia { Entrega = entrega, Estado = estado } : null;
        }

        /// <summary>«Se entregará HOY de 13:45 a 14:45 horas»: HOY = el día del correo (MAÑANA, el siguiente).</summary>
        private static EntregaEstimada EntregaHoyOManana(string texto, DateTime recibido)
        {
            Match m = HoyOManana.Match(texto);
            if (!m.Success)
            {
                return null;
            }
            var entrega = new EntregaEstimada { Dia = recibido.Date.AddDays(m.Groups[1].Value == "hoy" ? 0 : 1) };
            if (m.Groups[2].Success)
            {
                TimeSpan? desde = Hora(m.Groups[2].Value, m.Groups[3].Value);
                TimeSpan? hasta = Hora(m.Groups[4].Value, m.Groups[5].Value);
                if (desde.HasValue && hasta.HasValue && desde <= hasta)
                {
                    entrega.Desde = desde;
                    entrega.Hasta = hasta;
                }
            }
            return entrega;
        }

        /// <summary>«Entrega prevista Jueves 19 de Noviembre»: sin año, el del correo (o el siguiente si cae antes del día del correo).</summary>
        private static EntregaEstimada EntregaPrevista(string texto, DateTime recibido)
        {
            Match m = Prevista.Match(texto);
            if (!m.Success)
            {
                return null;
            }
            string mes = m.Groups[2].Value == "setiembre" ? "septiembre" : m.Groups[2].Value;
            int? anio = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : (int?)null;
            DateTime? dia = ReglaAnioEntregaAgencia.Resolver(int.Parse(m.Groups[1].Value), Array.IndexOf(NombresMeses, mes) + 1, anio, recibido);
            return dia.HasValue && ExtractorEntregaEstimada.EnRango(dia.Value, recibido) ? new EntregaEstimada { Dia = dia.Value } : null;
        }

        private static TimeSpan? Hora(string horas, string minutos) =>
            int.TryParse(horas, out int h) && int.TryParse(minutos, out int mi) && h >= 0 && h <= 23 && mi >= 0 && mi <= 59
                ? new TimeSpan(h, mi, 0) : (TimeSpan?)null;
    }

    public class AgenciaEntradaMrw : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "MRW";
        protected override string[] Dominios => new[] { "mrw.es", "mrw.com", "mrw.pt" };
        protected override string[] ParametrosCodigo => new[] { "envio", "numeroenvio", "numenvio", "albaran" };
    }

    public class AgenciaEntradaDhl : AgenciaEntradaPorPatrones, IBultosKilosAgencia
    {
        public override string Nombre => "DHL";
        protected override string[] Dominios => new[] { "dhl.com", "dhl.es", "dhl.de", "dhlparcel.es", "dhlparcel.com", "dhl.at", "dhl.fr", "dhl.it" };
        protected override string[] ParametrosCodigo => new[] { "tracking-id", "trackingnumber", "awb", "idc", "piececode", "tracking" };
        public override string[] NombresEnTexto => new[] { "DHL", "DHL Express", "DHL Parcel" };

        // NestoAPI#897: los avisos de DHL eCommerce traen «Bultos.....: 8» y «Kilos.......: 123» (puntos de relleno hasta los dos puntos)
        private static readonly Regex BultosDhl = new Regex(@"(?<![\p{L}])bultos\s*\.*\s*:\s*(\d{1,4})(?![\d.,])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex KilosDhl = new Regex(@"(?<![\p{L}])kilos\s*\.*\s*:\s*(\d{1,6}(?:[.,]\d{1,3})?)(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 25/11/26: «¡Muy pronto recibirás el envío de MAYSTAR S,L !» (DHL eCommerce): quién lo expide, hasta el signo de admiración o el fin
        /// de la línea.
        /// </summary>
        private static readonly Regex RecibirasElEnvioDe = new Regex(@"recibir[aá]s\s+(?:el|tu|su)\s+env[ií]o\s+de\s+([^!\r\n]+?)\s*(?:!|$)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>25/11/26: «Ref.Cliente.: 128013» es la referencia que le da el REMITENTE (el proveedor es el cliente de DHL), no un pedido nuestro.</summary>
        private static readonly Regex RefCliente = new Regex(@"ref\s*\.?\s*cliente\s*\.*\s*:\s*([A-Za-z0-9][A-Za-z0-9\-/_.]{0,39})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Solo en SUS correos: el remitente del paquete y la referencia del remitente (que nunca cuenta como número de pedido nuestro).</summary>
        public override DatosEnvioAgencia LeerDatosEnvio(CorreoCompras correo)
        {
            if (correo == null || !EsCorreoPropio(correo))
            {
                return null;
            }
            string texto = (correo.Asunto ?? string.Empty) + "\n" + (correo.Texto ?? string.Empty);
            Match remitente = RecibirasElEnvioDe.Match(texto);
            Match referencia = RefCliente.Match(texto);
            if (!remitente.Success && !referencia.Success)
            {
                return null;
            }
            var datos = new DatosEnvioAgencia();
            if (remitente.Success)
            {
                datos.RemitentePaquete = ExtractorRemitentePaquete.Normalizar(remitente.Groups[1].Value);
            }
            if (referencia.Success)
            {
                string valor = referencia.Groups[1].Value.TrimEnd('.', '-', '/', '_');
                datos.ReferenciasRemitente.Add(valor);
                datos.NumerosQueNoSonPedido.Add(valor);
            }
            return datos;
        }

        public BultosKilosEnvio LeerBultosKilos(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            Match b = BultosDhl.Match(texto);
            Match k = KilosDhl.Match(texto);
            var leido = new BultosKilosEnvio
            {
                Bultos = b.Success && int.TryParse(b.Groups[1].Value, out int bultos) && bultos > 0 ? bultos : (int?)null,
                Kilos = k.Success ? BultosKilosCorreo.Decimal(k.Groups[1].Value) : null
            };
            return leido.Bultos.HasValue || leido.Kilos.HasValue ? leido : null;
        }
    }

    public class AgenciaEntradaUps : AgenciaEntradaPorPatrones
    {
        private static readonly Regex Ups1Z = new Regex(@"\b(1Z[0-9A-Z]{16})\b", RegexOptions.Compiled);
        public override string Nombre => "UPS";
        protected override string[] Dominios => new[] { "ups.com" };
        protected override string[] ParametrosCodigo => new[] { "tracknum", "tracknums", "inquirynumber1", "trackingnumber" };
        protected override Regex PatronInconfundible => Ups1Z;

        // NestoAPI#857f: sus avisos («Entrega estimada / Lunes 16/11/2026 / entre 10:45 - 14:45», «Entrega reprogramada …», «Tu paquete se ha
        // entregado. … Entregado Viernes 13/11/2026 10:45»). Sobre el texto normalizado.
        private static readonly Regex Entregado = new Regex(@"(?<![a-z])(?:se\s+ha\s+entregado|paquete\s+entregado|has\s+been\s+delivered|was\s+delivered)(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex Reprogramada = new Regex(@"(?<![a-z])(?:entrega\s+reprogramada|reprogramad[oa]|rescheduled)(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex EnReparto = new Regex(@"(?<![a-z])(?:en\s+reparto|out\s+for\s+delivery)(?![a-z])", RegexOptions.Compiled);

        public override EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            string t = ExtractorEntregaEstimada.Normalizar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
            if (Entregado.IsMatch(t))
            {
                return new EntregaAgencia { Estado = EstadoEnvioAgencia.Entregado };
            }
            EntregaEstimada entrega = ExtractorEntregaEstimada.Extraer(correo.Texto, correo.Recibido);
            EstadoEnvioAgencia? estado = Reprogramada.IsMatch(t) ? EstadoEnvioAgencia.Reprogramada
                : EnReparto.IsMatch(t) ? EstadoEnvioAgencia.EnReparto
                : entrega != null ? EstadoEnvioAgencia.EnCamino
                : (EstadoEnvioAgencia?)null;
            return estado.HasValue ? new EntregaAgencia { Entrega = entrega, Estado = estado } : null;
        }

        public override string UrlSeguimiento(string codigo)
        {
            string c = CodigoParaUrl(codigo);
            return c == null ? null : "https://www.ups.com/track?tracknum=" + c;
        }
    }

    public class AgenciaEntradaCorreosExpress : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Correos Express";
        protected override string[] Dominios => new[] { "correosexpress.com", "correosexpress.es" };
        protected override string[] ParametrosCodigo => new[] { "shippingnumber", "n", "envio" };
        public override string[] NombresEnTexto => new[] { "Correos Express", "CorreosExpress" };
    }

    public class AgenciaEntradaCorreos : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Correos";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "correos.es", "correos.com" };
        protected override string[] ParametrosCodigo => new[] { "tracking-number", "numero", "codigo", "numerorenvio" };
    }

    public class AgenciaEntradaCttExpress : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "CTT Express";
        protected override string[] Dominios => new[] { "cttexpress.com", "cttexpress.es", "ctt.pt" };
        protected override string[] ParametrosCodigo => new[] { "sc", "shipping_code", "codigo", "objects" };
        public override string[] NombresEnTexto => new[] { "CTT Express", "CTTExpress", "CTT" };
    }

    /// <summary>
    /// Nacex. NestoAPI#857g (correo real del 13/11/26, no-reply@nacex.com): «…tu envío de LASHES AND MORE SL y está en camino con el número de
    /// seguimiento 3300/11571609 … Tu entrega está programada para el 16/11/2026 antes de las 20:00». El número es agencia de origen / albarán:
    /// el código es el albarán (el mismo que su enlace seguimientoDetalle.do?…numero_albaran=…). Los horarios de los puntos NACEX.shop del pie
    /// («Lun - Vie : 10:00-13:30») no son la entrega. Sin enlace público propio: el de sus correos lleva más parámetros que no se conocen.
    /// </summary>
    public class AgenciaEntradaNacex : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Nacex";
        protected override string[] Dominios => new[] { "nacex.es", "nacex.com", "nacex.pt" };
        protected override string[] ParametrosCodigo => new[] { "numero_albaran", "albaran", "expe_codigo", "codigo_exp", "ref" };

        // Sobre el texto normalizado (minúsculas y sin tildes)
        private static readonly Regex Programada = new Regex(
            @"entrega\s+esta\s+programada\s+para\s+(?:el\s+)?(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+(?:antes\s+de\s+las|hasta\s+las)\s+(\d{1,2})[:.h](\d{2}))?",
            RegexOptions.Compiled);
        private static readonly Regex Entregado = new Regex(@"(?<![a-z])(?:ha\s+sido\s+entregad[oa]|se\s+ha\s+entregado|envio\s+entregado)(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex EnReparto = new Regex(@"(?<![a-z])(?:esta\s+en\s+reparto|en\s+reparto\s+hoy)(?![a-z])", RegexOptions.Compiled);
        private static readonly Regex EnCamino = new Regex(@"(?<![a-z])esta\s+en\s+camino(?![a-z])", RegexOptions.Compiled);
        /// <summary>«número de seguimiento 3300/11571609»: agencia de origen / albarán.</summary>
        private static readonly Regex NumeroSeguimiento = new Regex(@"seguimiento\s*:?\s*(\d{3,5})\s*/\s*(\d{6,12})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            List<SeguimientoCorreo> resultado = base.ExtraerSeguimientos(correo);
            if (correo == null || resultado.Any(s => !string.IsNullOrEmpty(s.Codigo)) || !EsCorreoPropio(correo))
            {
                return resultado;
            }
            foreach (Match m in NumeroSeguimiento.Matches((correo.Asunto ?? string.Empty) + "\n" + correo.Texto))
            {
                Anadir(resultado, m.Groups[2].Value, null);
            }
            return resultado;
        }

        public override EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            string t = ExtractorEntregaEstimada.Normalizar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
            if (Entregado.IsMatch(t))
            {
                return new EntregaAgencia { Estado = EstadoEnvioAgencia.Entregado };
            }
            EntregaEstimada entrega = EntregaProgramada(t, correo.Recibido) ?? ExtractorEntregaEstimada.Extraer(correo.Texto, correo.Recibido);
            EstadoEnvioAgencia? estado = EnReparto.IsMatch(t) ? EstadoEnvioAgencia.EnReparto
                : entrega != null || EnCamino.IsMatch(t) ? EstadoEnvioAgencia.EnCamino
                : (EstadoEnvioAgencia?)null;
            return estado.HasValue || entrega != null ? new EntregaAgencia { Entrega = entrega, Estado = estado } : null;
        }

        /// <summary>«Tu entrega está programada para el 16/11/2026 antes de las 20:00»: ese día, hasta esa hora.</summary>
        private static EntregaEstimada EntregaProgramada(string normalizado, DateTime recibido)
        {
            Match m = Programada.Match(normalizado);
            if (!m.Success)
            {
                return null;
            }
            DateTime? dia = ReglaAnioEntregaAgencia.Resolver(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), recibido);
            if (!dia.HasValue || !ExtractorEntregaEstimada.EnRango(dia.Value, recibido))
            {
                return null;
            }
            var entrega = new EntregaEstimada { Dia = dia.Value };
            if (m.Groups[4].Success && int.TryParse(m.Groups[4].Value, out int h) && int.TryParse(m.Groups[5].Value, out int mi) && h <= 23 && mi <= 59)
            {
                entrega.Hasta = new TimeSpan(h, mi, 0);
            }
            return entrega;
        }
    }

    public class AgenciaEntradaTntFedex : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "TNT/FedEx";
        protected override string[] Dominios => new[] { "tnt.com", "tnt.es", "fedex.com" };
        protected override string[] ParametrosCodigo => new[] { "trknbr", "tracknumbers", "cons", "consignmentnumber" };
        public override string[] NombresEnTexto => new[] { "TNT/FedEx", "TNT", "FedEx", "Fed Ex" };
    }

    public class AgenciaEntradaDachser : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Dachser";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "dachser.com", "dachser.es" };
        protected override string[] ParametrosCodigo => new[] { "q", "search", "trackingnumber", "reference" };
    }

    public class AgenciaEntradaTipsa : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "TIPSA";
        protected override string[] Dominios => new[] { "tip-sa.com", "tipsa.es", "tipsa.com" };
        protected override string[] ParametrosCodigo => new[] { "ref", "codigo", "albaran", "cod" };
        public override string[] NombresEnTexto => new[] { "TIPSA", "Tip-SA", "Tip SA" };
    }

    public class AgenciaEntradaEnvialia : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Envialia";
        protected override string[] Dominios => new[] { "envialia.com", "envialia.es" };
        protected override string[] ParametrosCodigo => new[] { "nexp", "expedicion", "ref", "albaran" };
    }

    public class AgenciaEntradaZeleris : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Zeleris";
        protected override string[] Dominios => new[] { "zeleris.com", "zeleris.es" };
        protected override string[] ParametrosCodigo => new[] { "id_seguimiento", "nexpedicion", "expedicion" };
    }

    public class AgenciaEntradaRedurRhenus : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Redur/Rhenus";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "redur.es", "redur.com", "rhenus.com", "rhenus.group" };
        public override string[] NombresEnTexto => new[] { "Redur/Rhenus", "Redur", "Rhenus" };
    }

    public class AgenciaEntradaXpoGxo : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "XPO/GXO";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "xpo.com", "gxo.com", "xpoeurope.com" };
        public override string[] NombresEnTexto => new[] { "XPO/GXO", "XPO", "GXO" };
    }

    /// <summary>
    /// TXT (TXT e-solutions), la que usa Weelko («El pedido será enviado por: TXT 2026.V2»). 20/11/26 (Carlos): 48 h «mientras no tengamos
    /// algo más exacto». Las 48 h son LABORABLES (un aviso del viernes a mediodía daba el domingo). Su correo (info@txt.es, caso real del
    /// 20/11/26): «Grupo TXT le efectuará en breve la entrega de la expedición 300-8518521 Enviado por: Nombre: WEELKO BARCELONA SL
    /// Dirección: … Referencia: SO253169WKOUT78316 Destinatario: …»: el código es la expedición; el remitente y sus referencias (la
    /// referencia entera y cada trozo de letras + cifras: «SO253169», «WKOUT78316») enlazan con la confirmación del proveedor.
    /// </summary>
    public class AgenciaEntradaTxt : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "TXT";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        public override bool PlazoEnLaborables => true;
        protected override string[] Dominios => new[] { "txt.es" };
        public override string[] NombresEnTexto => new[] { "TXT e-solutions", "Grupo TXT", "TXT" };

        private static readonly Regex Expedicion = new Regex(@"expedici[oó]n\s+(\d{2,4}-\d{5,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EnviadoPor = new Regex(@"enviado\s+por\s*:\s*nombre\s*:\s*(.+?)\s+direcci[oó]n\s*:", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ReferenciaRemitente = new Regex(@"(?<![\p{L}])referencia\s*:\s*([A-Za-z0-9][A-Za-z0-9/_.\-]{3,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrozoReferencia = new Regex(@"[A-Za-z]{2,}\d{3,}", RegexOptions.Compiled);
        private static readonly Regex EnBreve = new Regex(@"le\s+efectuara\s+en\s+breve\s+la\s+entrega", RegexOptions.Compiled);

        public override List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            List<SeguimientoCorreo> resultado = base.ExtraerSeguimientos(correo);
            if (correo == null || resultado.Any(s => !string.IsNullOrEmpty(s.Codigo)) || !EsCorreoPropio(correo))
            {
                return resultado;
            }
            foreach (Match m in Expedicion.Matches((correo.Asunto ?? string.Empty) + "\n" + correo.Texto))
            {
                Anadir(resultado, m.Groups[1].Value, null);
            }
            return resultado;
        }

        public override EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            EntregaAgencia comun = base.LeerEntrega(correo);
            if (comun != null)
            {
                return comun;
            }
            string t = ExtractorEntregaEstimada.Normalizar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
            return EnBreve.IsMatch(t) ? new EntregaAgencia { Estado = EstadoEnvioAgencia.EnCamino } : null;
        }

        public override DatosEnvioAgencia LeerDatosEnvio(CorreoCompras correo)
        {
            if (correo == null || !EsCorreoPropio(correo))
            {
                return null;
            }
            string texto = correo.Texto ?? string.Empty;
            Match remitente = EnviadoPor.Match(texto);
            Match referencia = ReferenciaRemitente.Match(texto);
            if (!remitente.Success && !referencia.Success)
            {
                return null;
            }
            var datos = new DatosEnvioAgencia();
            if (remitente.Success)
            {
                datos.RemitentePaquete = ExtractorRemitentePaquete.Normalizar(remitente.Groups[1].Value);
            }
            if (referencia.Success)
            {
                string entera = referencia.Groups[1].Value.TrimEnd('-', '/', '_', '.');
                datos.ReferenciasRemitente.Add(entera);
                foreach (Match trozo in TrozoReferencia.Matches(entera))
                {
                    if (!datos.ReferenciasRemitente.Contains(trozo.Value, StringComparer.OrdinalIgnoreCase))
                    {
                        datos.ReferenciasRemitente.Add(trozo.Value);
                    }
                }
            }
            return datos;
        }
    }

    public class AgenciaEntradaSchenker : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "DB Schenker";
        /// <summary>NestoAPI#857g (Carlos 19/11/26): 3 días LABORABLES (el aviso del 18/11 llegó el 23/11), no 48 h naturales.</summary>
        public override int HorasHastaEntrega => HORAS_GRUPAJE_LABORABLES;
        public override bool PlazoEnLaborables => true;
        // 20/11/26 (Carlos 19/11: «unir DSV con DB Schenker»): Schenker es ya DSV y un mismo envío manda correos desde los dos
        // (el aviso «ha generado un nuevo envío» de dbschenker y los de estado de noreply@mydsv.dsv.com). Una sola agencia: así
        // comparten código y caen en el mismo envío. No hay clase DSV aparte.
        protected override string[] Dominios => new[] { "dbschenker.com", "schenker.com", "schenker.es", "dbschenker.es", "dsv.com", "mydsv.com" };
        protected override string[] ParametrosCodigo => new[] { "refnumber", "reference", "trackingnumber", "ref", "shipmentid" };
        public override string[] NombresEnTexto => new[] { "DB Schenker", "DBSchenker", "Schenker", "DSV" };

        /// <summary>
        /// Su número de envío en España: «ES» + las tres letras de la delegación de salida + 9 cifras (ESMNR264773770, ESVIC262013536).
        /// Va en el texto («con número ESVIC262013536») o en el asunto («Nº deTracking Stt : ESMNR264773770»), no siempre en un enlace.
        /// </summary>
        private static readonly Regex NumeroEnvio = new Regex(@"(?<![A-Za-z0-9])(ES[A-Z]{3}\d{9})(?![A-Za-z0-9])", RegexOptions.Compiled);

        /// <summary>
        /// Solo en un correo que ya se sabe que es suyo (remitente o enlace de sus dominios): el formato no se usa para RECONOCER la
        /// agencia, porque el aviso de salida de un proveedor que lleva ese número sigue su propio camino (formato del proveedor).
        /// </summary>
        protected override IEnumerable<string> CodigosEnTexto(CorreoCompras correo)
        {
            string texto = (correo.Asunto ?? string.Empty) + "\n" + correo.Texto;
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in NumeroEnvio.Matches(texto))
            {
                if (vistos.Add(m.Groups[1].Value))
                {
                    yield return m.Groups[1].Value;
                }
            }
            if (vistos.Count > 0)
            {
                yield break; // con su número, lo demás del texto («el número: …») no es otro código
            }
            foreach (string codigo in base.CodigosEnTexto(correo))
            {
                yield return codigo;
            }
        }

        // Sobre el texto normalizado (minúsculas y sin tildes): «Estado del Envío: DOT (Salido a reparto)»
        private static readonly Regex EstadoDelEnvio = new Regex(@"estado\s+del\s+envio\s*:?\s*([a-z]{3})\s*\(", RegexOptions.Compiled);
        private static readonly Regex NuevoEnvio = new Regex(@"ha\s+generado\s+un\s+nuevo\s+envio|has\s+created\s+a\s+new\s+order", RegexOptions.Compiled);

        /// <summary>
        /// Lo que dicen SUS correos. Los de estado de DSV traen una sigla: ENM (llegado a la delegación) → en camino; DOT (salido a
        /// reparto) → en reparto; DLV (entregado) y POD (prueba de entrega) → entregado; otra sigla → en camino. El aviso «el remitente
        /// ha generado un nuevo envío» → en camino. Ninguno trae fecha: la pone el plazo (72 h laborables).
        /// </summary>
        public override EntregaAgencia LeerEntrega(CorreoCompras correo)
        {
            if (!EsCorreoPropio(correo))
            {
                return null;
            }
            string t = ExtractorEntregaEstimada.Normalizar((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
            EstadoEnvioAgencia? estado = null;
            foreach (Match m in EstadoDelEnvio.Matches(t))
            {
                EstadoEnvioAgencia este = EstadoDeLaSigla(m.Groups[1].Value);
                if (!estado.HasValue || este > estado.Value)
                {
                    estado = este; // si un correo trae varios, el más avanzado
                }
            }
            if (!estado.HasValue && NuevoEnvio.IsMatch(t))
            {
                estado = EstadoEnvioAgencia.EnCamino;
            }
            if (estado == EstadoEnvioAgencia.Entregado)
            {
                return new EntregaAgencia { Estado = estado };
            }
            EntregaEstimada entrega = ExtractorEntregaEstimada.Extraer(correo.Texto, correo.Recibido);
            return estado.HasValue || entrega != null ? new EntregaAgencia { Entrega = entrega, Estado = estado } : null;
        }

        internal static EstadoEnvioAgencia EstadoDeLaSigla(string sigla)
        {
            switch ((sigla ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "DLV":
                case "POD":
                    return EstadoEnvioAgencia.Entregado;
                case "DOT":
                    return EstadoEnvioAgencia.EnReparto;
                default:
                    return EstadoEnvioAgencia.EnCamino;
            }
        }

        /// <summary>
        /// NestoAPI#857f: el seguimiento público (Schenker es ya DSV). Su web NO se deja leer desde el servidor (429) y no se intenta: aquí
        /// solo se compone el enlace para Compras. Si se da de alta en la API oficial de DSV, la consulta va AQUÍ, implementando
        /// <see cref="IConsultaEntregaAgencia"/> en esta clase (clave en secretos.config); nada de fuera cambia.
        /// </summary>
        public override string UrlSeguimiento(string codigo)
        {
            string c = CodigoParaUrl(codigo);
            return c == null ? null : "https://www.dsv.com/mydsv/tracking-public/?language_region=es-ES_ES&refNumber=" + c;
        }

        private static readonly Regex NumeroEnvioEntero = new Regex(@"^ES([A-Z]{3})\d{9}$", RegexOptions.Compiled);

        /// <summary>
        /// NestoAPI#857i (Carlos 25/11/26): las delegaciones de salida conocidas. Solo las que se han visto; una que no esté aquí se enseña
        /// con su clave (y se aprende igual).
        /// </summary>
        internal static readonly Dictionary<string, string> Delegaciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["VIC"] = "Vic",
            ["MNR"] = "Manresa"
        };

        /// <summary>NestoAPI#857i: las tres letras de la delegación de salida de su número de envío («ESVIC262013788» → «VIC»).</summary>
        public override string OrigenDelEnvio(string codigo)
        {
            string limpio = codigo?.Replace(" ", string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            Match m = NumeroEnvioEntero.Match(limpio);
            return m.Success ? m.Groups[1].Value : null;
        }

        public override string NombreOrigen(string origen) =>
            origen != null && Delegaciones.TryGetValue(origen.Trim(), out string nombre) ? nombre : null;
    }

    public class AgenciaEntradaDpd : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "DPD";
        protected override string[] Dominios => new[] { "dpd.com", "dpd.es", "dpd.de", "dpd.fr", "dpd.pt", "dpdgroup.com" };
        protected override string[] ParametrosCodigo => new[] { "parcelnumber", "parcelno", "pknr" };
        protected override Regex[] PatronesRuta => new[] { new Regex(@"/parcel/([A-Za-z0-9\-]+)", RegexOptions.IgnoreCase) };
    }

    public class AgenciaEntradaSending : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Sending";
        protected override string[] Dominios => new[] { "sending.es" };
    }

    public class AgenciaEntradaPaack : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Paack";
        protected override string[] Dominios => new[] { "paack.co", "paack.app" };
    }

    public class AgenciaEntradaPalletways : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Palletways";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "palletways.com" };
    }

    /// <summary>
    /// Mail Boxes Etc. (la usan proveedores pequeños: «El pedido será enviado por: MAILBOXES»). NestoAPI#857g: es un INTERMEDIARIO. Su resumen
    /// («Sus envíos con MBE 12/11/2026», recogidas061@mbe.es, mbe225@mbe.es) trae por cada envío el remitente, la referencia del remitente
    /// («Ref.: SO252231WKOUT77626»), su código («Tracking MBE: ES0061-0L-000000VTHP») y el MENSAJERO real («Courier: UPS») con TODOS sus
    /// códigos («Tracking: 1Z…», uno por bulto). Los del mensajero van marcados con su agencia (<see cref="SeguimientoCorreo.Agencia"/>): así el
    /// correo queda en el mismo envío (<see cref="MismoEnvio"/>) que los avisos del mensajero, que son los que dicen la fecha; MBE no pone fecha
    /// propia (su plazo por defecto). El mismo resumen lleva también NUESTRAS salidas por MBE («Remitente: NUEVA VISION SA»): un envío nuestro
    /// del resumen no cuenta; si todos lo son, es una salida nuestra.
    /// </summary>
    public class AgenciaEntradaMbe : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "MBE";
        protected override string[] Dominios => new[] { "mbe.es", "mbe.it", "mbeglobal.com", "mbe-hub.com" };
        protected override string[] ParametrosCodigo => new[] { "c", "tracking", "trackingnumber", "code" };
        public override string[] NombresEnTexto => new[] { "MBE", "Mail Boxes", "MailBoxes", "Mail Boxes Etc", "MailBoxes Etc" };

        /// <summary>Cada envío del resumen empieza en «Envío: 1», «Envío: 2»… («Envío: Servicio…» es otra cosa).</summary>
        private static readonly Regex InicioEnvio = new Regex(@"(?<![\p{L}])Env[ií]o\s*:\s*\d+(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Remitente = new Regex(@"(?<![\p{L}])Remitente\s*:[ \t]*([^\n]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HastaOtraEtiqueta = new Regex(@"\s*(?<![\p{L}])(?:Destinatario|Env[ií]o|Servicio|Peso|Ref\.?|Tracking|Courier|Detalles)\s*(?:MBE\s*)?:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        /// <summary>El nombre acaba en la forma societaria («Weelko Barcelona S. L.»); si no la hay, en la primera coma o cifra (la dirección).</summary>
        private static readonly Regex NombreConSociedad = new Regex(
            @"^(.*?(?<![\p{L}])(?:S\.?\s?L\.?\s?U\.?|S\.?\s?L\.?\s?L\.?|S\.?\s?L\.?|S\.?\s?A\.?\s?U\.?|S\.?\s?A\.?|C\.?\s?B\.?|GmbH|Ltd\.?|S\.?r\.?l\.?)(?![\p{L}]))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Referencia = new Regex(@"(?<![\p{L}])Ref\.?\s*:\s*([A-Za-z0-9][A-Za-z0-9\-/_]{2,59})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrackingMbe = new Regex(@"(?<![\p{L}])Tracking\s+MBE\s*:\s*([A-Za-z0-9]+(?:-[A-Za-z0-9]+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrackingMensajero = new Regex(@"(?<![\p{L}])Tracking\s*:\s*([A-Za-z0-9]+(?:-[A-Za-z0-9]+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Courier = new Regex(@"(?<![\p{L}])Courier\s*:([^\n]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private class EnvioDelResumen
        {
            public string Texto { get; set; }
            public bool EsNuestro { get; set; }
        }

        public override List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo)
        {
            List<SeguimientoCorreo> deEnlaces = base.ExtraerSeguimientos(correo);
            if (correo == null || !EsCorreoPropio(correo))
            {
                return deEnlaces;
            }
            var resultado = new List<SeguimientoCorreo>();
            foreach (EnvioDelResumen envio in EnviosQueCuentan(correo.Texto))
            {
                IAgenciaEntrada mensajero = MensajeroDe(envio.Texto);
                // Primero los del mensajero (el seguimiento de verdad) y luego el de MBE
                foreach (Match m in TrackingMensajero.Matches(envio.Texto))
                {
                    string codigo = m.Groups[1].Value;
                    if (EsCodigo(codigo) && !resultado.Any(s => string.Equals(s.Codigo, codigo, StringComparison.OrdinalIgnoreCase)))
                    {
                        resultado.Add(new SeguimientoCorreo { Codigo = codigo, Agencia = mensajero?.Nombre });
                    }
                }
                foreach (Match m in TrackingMbe.Matches(envio.Texto))
                {
                    if (EsCodigo(m.Groups[1].Value))
                    {
                        Anadir(resultado, m.Groups[1].Value, null);
                    }
                }
            }
            // De lo común, solo los enlaces (los códigos del texto ya salen de cada envío, sin los de nuestras salidas)
            foreach (SeguimientoCorreo s in deEnlaces.Where(e => !string.IsNullOrEmpty(e.Url)))
            {
                Anadir(resultado, s.Codigo, s.Url);
            }
            return resultado;
        }

        public override DatosEnvioAgencia LeerDatosEnvio(CorreoCompras correo)
        {
            if (correo == null || !EsCorreoPropio(correo))
            {
                return null;
            }
            List<EnvioDelResumen> envios = Envios(correo.Texto);
            if (!envios.Any(e => Remitente.IsMatch(e.Texto)))
            {
                return null; // no es un resumen de envíos
            }
            var datos = new DatosEnvioAgencia { EsSalidaNuestra = envios.All(e => e.EsNuestro) };
            foreach (EnvioDelResumen envio in EnviosQueCuentan(correo.Texto))
            {
                datos.RemitentePaquete = datos.RemitentePaquete ?? NombreRemitente(envio.Texto);
                datos.Mensajero = datos.Mensajero ?? MensajeroDe(envio.Texto);
                foreach (Match m in Referencia.Matches(envio.Texto))
                {
                    string referencia = m.Groups[1].Value.TrimEnd('-', '/', '_');
                    if (!datos.ReferenciasRemitente.Contains(referencia, StringComparer.OrdinalIgnoreCase))
                    {
                        datos.ReferenciasRemitente.Add(referencia);
                    }
                }
            }
            return datos;
        }

        /// <summary>Los envíos del resumen (uno solo si no los numera).</summary>
        private static List<EnvioDelResumen> Envios(string texto)
        {
            string t = (texto ?? string.Empty).Replace("\r", string.Empty);
            List<int> inicios = InicioEnvio.Matches(t).Cast<Match>().Select(m => m.Index).ToList();
            var trozos = new List<string>();
            if (inicios.Count == 0)
            {
                trozos.Add(t);
            }
            for (int i = 0; i < inicios.Count; i++)
            {
                int fin = i + 1 < inicios.Count ? inicios[i + 1] : t.Length;
                trozos.Add(t.Substring(inicios[i], fin - inicios[i]));
            }
            return trozos.Select(x =>
            {
                string remitente = RemitenteEnBruto(x);
                return new EnvioDelResumen { Texto = x, EsNuestro = remitente != null && EnvioNuestroDeSalida.Es("Remitente: " + remitente) };
            }).ToList();
        }

        /// <summary>Los que cuentan: los que no son salidas nuestras; si todos lo son, todos (el correo se guarda como salida nuestra).</summary>
        private static List<EnvioDelResumen> EnviosQueCuentan(string texto)
        {
            List<EnvioDelResumen> envios = Envios(texto);
            List<EnvioDelResumen> entradas = envios.Where(e => !e.EsNuestro).ToList();
            return entradas.Any() ? entradas : envios;
        }

        /// <summary>Lo que va tras «Remitente:» (en su línea o, si está vacía, en la siguiente), hasta la siguiente etiqueta.</summary>
        private static string RemitenteEnBruto(string envio)
        {
            Match m = Remitente.Match(envio ?? string.Empty);
            if (!m.Success)
            {
                return null;
            }
            string valor = m.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(valor))
            {
                string resto = envio.Substring(m.Index + m.Length);
                valor = resto.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            }
            Match otra = HastaOtraEtiqueta.Match(valor);
            return (otra.Success ? valor.Substring(0, otra.Index) : valor).Trim();
        }

        private static string NombreRemitente(string envio)
        {
            string bruto = RemitenteEnBruto(envio);
            if (string.IsNullOrWhiteSpace(bruto))
            {
                return null;
            }
            Match conSociedad = NombreConSociedad.Match(bruto);
            string nombre;
            if (conSociedad.Success)
            {
                nombre = conSociedad.Groups[1].Value;
            }
            else
            {
                int corte = bruto.IndexOfAny(new[] { ',', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' });
                nombre = corte > 0 ? bruto.Substring(0, corte) : bruto;
            }
            return EnvioNuestroDeSalida.Es("Remitente: " + nombre) ? null : ExtractorRemitentePaquete.Normalizar(nombre);
        }

        /// <summary>La agencia de «Courier: UPS» (por los nombres de cada clase de agencia), o null.</summary>
        private static IAgenciaEntrada MensajeroDe(string envio)
        {
            Match m = Courier.Match(envio ?? string.Empty);
            if (!m.Success)
            {
                return null;
            }
            string valor = m.Groups[1].Value;
            Match otra = HastaOtraEtiqueta.Match(valor);
            string nombre = (otra.Success ? valor.Substring(0, otra.Index) : valor).Trim();
            IAgenciaEntrada agencia = AgenciaPorNombreEnTexto.Buscar("Courier: " + nombre, AgenciasEntradaRegistradas.Todas);
            return agencia is AgenciaEntradaMbe ? null : agencia;
        }
    }

    #endregion
}
