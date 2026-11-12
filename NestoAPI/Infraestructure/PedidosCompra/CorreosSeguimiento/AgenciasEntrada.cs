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
    }

    /// <summary>
    /// NestoAPI#857: una agencia que nos TRAE mercancía de un proveedor (no las de nuestros envíos: <c>PerfilesAgencias</c>). Una
    /// clase por agencia, registrada en <see cref="AgenciasEntradaRegistradas"/>. La predicción de la fase 3 es una constante por
    /// agencia (<see cref="HorasHastaEntrega"/>) desde que consta que la mercancía está en ella.
    /// </summary>
    public interface IAgenciaEntrada
    {
        string Nombre { get; }
        int HorasHastaEntrega { get; }
        bool Reconoce(CorreoCompras correo);
        List<SeguimientoCorreo> ExtraerSeguimientos(CorreoCompras correo);
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
            @"(?:n[ºo°]\.?\s*de\s*(?:seguimiento|env[ií]o|expedici[oó]n)|n[uú]mero\s+de\s+(?:seguimiento|env[ií]o|expedici[oó]n)|c[oó]digo\s+de\s+(?:seguimiento|env[ií]o)|seguimiento|tracking(?:\s*(?:number|n[ºo°]|id|code))?|localizador|expedici[oó]n|sendungsnummer|awb)\s*(?:n[ºo°]\.?)?\s*[:#]?\s*(?:(?:GLS|UPS|DHL|SEUR|MRW|TNT|DPD|CTT|NACEX|TIPSA)\s+)?([A-Za-z0-9][A-Za-z0-9\-]{5,39})\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public abstract string Nombre { get; }
        public virtual int HorasHastaEntrega => HORAS_PAQUETERIA;
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

        public bool EsDominio(string host) => !string.IsNullOrEmpty(host) && Dominios.Any(d => TextoCorreo.HostDe(host, d));

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
                if (!Uri.TryCreate(enlace, UriKind.Absolute, out Uri uri) || Excluido(enlace)
                    || DominiosPropios.Any(d => TextoCorreo.HostDe(uri.Host, d)))
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
            new AgenciaEntradaTntFedex(), new AgenciaEntradaDachser(), new AgenciaEntradaDsv(), new AgenciaEntradaTipsa(),
            new AgenciaEntradaEnvialia(), new AgenciaEntradaZeleris(), new AgenciaEntradaRedurRhenus(), new AgenciaEntradaXpoGxo(),
            new AgenciaEntradaSchenker(), new AgenciaEntradaDpd(), new AgenciaEntradaSending(), new AgenciaEntradaPaack(),
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

        public static IAgenciaEntrada Buscar(CorreoCompras correo, IEnumerable<IAgenciaEntrada> agencias)
        {
            if (correo == null || agencias == null)
            {
                return null;
            }
            List<string> ventanas = Ventanas((correo.Asunto ?? string.Empty) + "\n" + correo.Texto);
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

    public class AgenciaEntradaSeur : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "SEUR";
        protected override string[] Dominios => new[] { "seur.com", "seur.es", "seur.info" };
        protected override string[] ParametrosCodigo => new[] { "segonlineidentificador", "identificador", "expedicion", "id" };
    }

    public class AgenciaEntradaMrw : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "MRW";
        protected override string[] Dominios => new[] { "mrw.es", "mrw.com", "mrw.pt" };
        protected override string[] ParametrosCodigo => new[] { "envio", "numeroenvio", "numenvio", "albaran" };
    }

    public class AgenciaEntradaDhl : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "DHL";
        protected override string[] Dominios => new[] { "dhl.com", "dhl.es", "dhl.de", "dhlparcel.es", "dhlparcel.com", "dhl.at", "dhl.fr", "dhl.it" };
        protected override string[] ParametrosCodigo => new[] { "tracking-id", "trackingnumber", "awb", "idc", "piececode", "tracking" };
        public override string[] NombresEnTexto => new[] { "DHL", "DHL Express", "DHL Parcel" };
    }

    public class AgenciaEntradaUps : AgenciaEntradaPorPatrones
    {
        private static readonly Regex Ups1Z = new Regex(@"\b(1Z[0-9A-Z]{16})\b", RegexOptions.Compiled);
        public override string Nombre => "UPS";
        protected override string[] Dominios => new[] { "ups.com" };
        protected override string[] ParametrosCodigo => new[] { "tracknum", "tracknums", "inquirynumber1", "trackingnumber" };
        protected override Regex PatronInconfundible => Ups1Z;
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

    public class AgenciaEntradaNacex : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "Nacex";
        protected override string[] Dominios => new[] { "nacex.es", "nacex.com", "nacex.pt" };
        protected override string[] ParametrosCodigo => new[] { "numero_albaran", "albaran", "expe_codigo", "codigo_exp", "ref" };
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

    public class AgenciaEntradaDsv : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "DSV";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "dsv.com", "mydsv.com" };
        protected override string[] ParametrosCodigo => new[] { "trackingnumber", "ref", "reference", "shipmentid" };
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

    public class AgenciaEntradaSchenker : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "DB Schenker";
        public override int HorasHastaEntrega => HORAS_GRUPAJE;
        protected override string[] Dominios => new[] { "dbschenker.com", "schenker.es", "dbschenker.es" };
        protected override string[] ParametrosCodigo => new[] { "refnumber", "reference" };
        public override string[] NombresEnTexto => new[] { "DB Schenker", "DBSchenker", "Schenker" };
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

    /// <summary>Mail Boxes Etc. (la usan proveedores pequeños: «El pedido será enviado por: MAILBOXES»).</summary>
    public class AgenciaEntradaMbe : AgenciaEntradaPorPatrones
    {
        public override string Nombre => "MBE";
        protected override string[] Dominios => new[] { "mbe.es", "mbe.it", "mbeglobal.com", "mbe-hub.com" };
        protected override string[] ParametrosCodigo => new[] { "c", "tracking", "trackingnumber", "code" };
        public override string[] NombresEnTexto => new[] { "MBE", "Mail Boxes", "MailBoxes", "Mail Boxes Etc", "MailBoxes Etc" };
    }

    #endregion
}
