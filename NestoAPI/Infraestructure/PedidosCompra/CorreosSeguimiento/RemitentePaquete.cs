using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857 (fase 3): un remitente del paquete que Compras ha dicho de qué proveedor es (dbo.ComprasRemitentesPaquete).</summary>
    public class RemitentePaqueteRecordado
    {
        public int Id { get; set; }
        /// <summary>Normalizado con <see cref="ExtractorRemitentePaquete.Normalizar"/>.</summary>
        public string RemitentePaquete { get; set; }
        public string Empresa { get; set; }
        public string Proveedor { get; set; }
        public string Usuario { get; set; }
        public System.DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#905 (Carlos 24/11/26, caso Postquam: SEUR «Tu envío PV26-57984 de Postquam» quedaba por asignar aunque POSTQUAM COSMETIC, S.L.
    /// solo tenía un pedido en el aire): el remitente del paquete que nombra la agencia se compara con el NOMBRE de los proveedores con pedidos
    /// en el aire, cuando ningún otro criterio ha dado proveedor. PURA. Casan si comparten una palabra DISTINTIVA entera (sin forma
    /// societaria, tildes ni mayúsculas; <see cref="LONGITUD_MINIMA"/> letras o más; nunca una genérica, <see cref="Genericas"/>). Solo si
    /// casa UN proveedor: dos que comparten la palabra (el falso amigo) no dan ninguno.
    /// </summary>
    public static class ReglaNombreRemitentePaquete
    {
        public const int LONGITUD_MINIMA = 5;

        /// <summary>Palabras que muchos proveedores llevan en el nombre y no distinguen a ninguno (en mayúsculas y sin tildes).</summary>
        internal static readonly HashSet<string> Genericas = new HashSet<string>
        {
            "COSMETIC", "COSMETICS", "COSMETICA", "COSMETICAS", "COSMETICO", "COSMETICOS", "COSMETIQUE", "BEAUTY", "BELLEZA", "ESTETICA", "ESTETICO",
            "DISTRIBUCION", "DISTRIBUCIONES", "DISTRIBUIDORA", "DISTRIBUIDOR", "DISTRIBUTION", "DISTRIBUTIONS", "DISTRIBUTOR", "PRODUCTOS", "PRODUCTS",
            "PRODUCT", "COMERCIAL", "COMERCIALES", "COMERCIO", "INTERNACIONAL", "INTERNATIONAL", "GROUP", "GRUPO", "INDUSTRIAS", "INDUSTRIAL",
            "LABORATORIO", "LABORATORIOS", "LABORATORIES", "PROFESIONAL", "PROFESSIONAL", "IBERICA", "ESPANA", "SPAIN", "EUROPA", "EUROPE",
            "TRADING", "IMPORT", "EXPORT", "IMPORTACIONES", "EXPORTACIONES", "STORE", "TIENDA", "ONLINE", "SERVICIOS", "SERVICES", "LOGISTICA",
            "LOGISTICS", "SOLUTIONS", "SOLUCIONES", "SISTEMAS", "SYSTEMS", "PELUQUERIA", "HAIRDRESSING", "NATURAL", "NATURE", "CENTRO", "COMPANY",
            "COMPANIA", "HOLDING", "IBERIA", "MADRID", "BARCELONA", "VALENCIA", "SEVILLA", "GLOBAL", "MARKETING", "PHARMA", "FARMA", "MEDICAL"
        };

        /// <summary>Las palabras distintivas de un nombre: «POSTQUAM COSMETIC, S.L.» → { POSTQUAM }.</summary>
        public static HashSet<string> PalabrasDistintivas(string nombre)
        {
            return new HashSet<string>(AsignadorPedidosCompra.NombreSinSociedad(nombre)
                .Split(' ')
                .Where(p => p.Length >= LONGITUD_MINIMA && p.Count(char.IsLetter) >= LONGITUD_MINIMA && !Genericas.Contains(p)));
        }

        /// <summary>Los pedidos en el aire del ÚNICO proveedor cuyo nombre casa con el remitente del paquete; vacío si ninguno o varios.</summary>
        public static List<PedidoCompraEnAire> Pedidos(IEnumerable<PedidoCompraEnAire> enAire, string remitentePaquete)
        {
            HashSet<string> delRemitente = PalabrasDistintivas(remitentePaquete);
            List<PedidoCompraEnAire> pedidos = (enAire ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null).ToList();
            if (!delRemitente.Any() || !pedidos.Any())
            {
                return new List<PedidoCompraEnAire>();
            }
            List<PedidoCompraEnAire> casan = pedidos.Where(p => PalabrasDistintivas(p.NombreProveedor).Overlaps(delRemitente)).ToList();
            int proveedores = casan.Select(p => p.Proveedor?.Trim() ?? string.Empty).Distinct(System.StringComparer.OrdinalIgnoreCase).Count();
            return proveedores == 1 ? casan : new List<PedidoCompraEnAire>();
        }
    }

    /// <summary>
    /// NestoAPI#905: qué remitente del paquete se recuerda al quedar aplicado un envío. 25/11/26: no solo si se acertó por su nombre; también
    /// cuando el proveedor se reconoció por otro criterio del PROPIO correo (número, correo, dominio, nombre, PDF, líneas…; nunca lo heredado
    /// de otro correo del envío, que puede ser el de un intermediario), si ese remitente aún no está en la memoria. PURA.
    /// </summary>
    public static class ReglaRecordarRemitentePaquete
    {
        /// <summary>
        /// El remitente (normalizado) de un correo del envío cuyos candidatos propios llevan el proveedor del pedido aplicado (primero los que
        /// salieron por <see cref="AsignadorPedidosCompra.MOTIVO_NOMBRE_REMITENTE_PAQUETE"/>) y que no está ya en <paramref name="memoria"/>
        /// (con ningún proveedor: lo que dijo Compras no se pisa); null si no hay ninguno.
        /// </summary>
        public static string QueRecordar(IEnumerable<CorreoSeguimientoFila> correos, CandidatoPedidoCompra pedido,
            IEnumerable<RemitentePaqueteRecordado> memoria = null)
        {
            if (pedido == null || string.IsNullOrWhiteSpace(pedido.Proveedor))
            {
                return null;
            }
            var yaRecordados = new HashSet<string>((memoria ?? Enumerable.Empty<RemitentePaqueteRecordado>())
                .Select(r => ExtractorRemitentePaquete.Normalizar(r?.RemitentePaquete)).Where(r => r != null));
            return (correos ?? Enumerable.Empty<CorreoSeguimientoFila>())
                .Where(f => f != null)
                .Select(f => new
                {
                    Remitente = ExtractorRemitentePaquete.Normalizar(f.RemitentePaquete),
                    Suyos = MismoEnvio.Propios(f).Where(c => string.Equals(c.Proveedor?.Trim(), pedido.Proveedor.Trim(), System.StringComparison.OrdinalIgnoreCase)).ToList()
                })
                .Where(x => x.Remitente != null && x.Suyos.Any() && !yaRecordados.Contains(x.Remitente))
                .OrderByDescending(x => x.Suyos.Any(c => c.Motivo == AsignadorPedidosCompra.MOTIVO_NOMBRE_REMITENTE_PAQUETE))
                .Select(x => x.Remitente)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// NestoAPI#857 (fase 3): QUIÉN EXPIDE el paquete según el aviso de la agencia, que no siempre es el proveedor (caso real de UPS,
    /// 12/11/26: «¡Tu paquete está de camino!» / «De MBE STORE C/O MULTIBOXES S.L»: una tienda de Mail Boxes que envía por un
    /// proveedor). PURA. Por este orden: (1) tras una etiqueta «Remitente:», «Expedidor:», «Enviado por:» o «Shipper:» en cualquier
    /// línea; (2) la línea «De X» (sin dos puntos) justo tras el titular, en las primeras <see cref="LINEAS_TITULAR"/> líneas (UPS);
    /// (3) la frase «tu envío de X (y) está en camino» (Nacex); (4) tras «From:» al principio de una línea. Nunca nosotros (Nueva Visión) ni el nombre de una agencia a secas («Enviado por:
    /// SEUR» es la agencia, no el remitente). Se devuelve normalizado (<see cref="Normalizar"/>), que es también la clave de la memoria.
    /// </summary>
    public static class ExtractorRemitentePaquete
    {
        public const int LONGITUD = 100;
        public const int LINEAS_TITULAR = 6;
        private const int MINIMO_LETRAS = 3;

        // Por línea: el valor va detrás de la etiqueta o, si no hay nada, en la siguiente línea no vacía («Remitente:\nWeelko Barcelona S. L.», MBE)
        private static readonly Regex Etiqueta = new Regex(
            @"(?<![\p{L}\p{N}])(?:remitente|expedidor|enviado\s+por|shipper)\s*\*?\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EtiquetaFrom = new Regex(@"^\s*\*?\s*from\s*\*?\s*:\s*(.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>«De MBE STORE C/O MULTIBOXES S.L»: «De»/«From» y el nombre, sin dos puntos en la línea.</summary>
        private static readonly Regex LineaDe = new Regex(@"^\s*(?:de|from)\s+(\p{L}[^:]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// «ya tenemos tu envío de LASHES AND MORE SL y está en camino con el número de seguimiento…» (Nacex, 13/11/26): el nombre va
        /// entre «tu/su envío (o paquete) de» y lo que sigue («(y) está», «con el número…», «ha…», «llegará»), en la misma línea. Sin
        /// ese final no cuenta. No se corta en comas ni puntos: son parte del nombre («COSMÉTICOS, S.L.»).
        /// </summary>
        private static readonly Regex FraseEnvioDe = new Regex(
            // NestoAPI#857f (SEUR): con la referencia del remitente delante del «de» («tu envío PV26-56989 de Postquam en C/ Rio Tietar 11…»)
            // y terminando en la dirección de entrega («en C/», «en Calle»…)
            @"(?<![\p{L}\p{N}])(?:tu|su|vuestro)\s+(?:env[ií]o|paquete)\s+(?:(?=[A-Za-z0-9\-]*\d)[A-Za-z0-9][A-Za-z0-9\-]{3,39}\s+)?de\s+(\p{L}.{1,100}?)"
            + @",?\s+(?:(?:y\s+)?(?:ya\s+)?(?:est[aá]|ha|llegar[aá])(?![\p{L}\p{N}])"
            + @"|con\s+(?:el\s+)?(?:n[uú]mero|n[ºo°](?![\p{L}\p{N}])|seguimiento|c[oó]digo|localizador)"
            + @"|en\s+(?:c\s*/|calle|avda|avenida|av\.|plaza|pza|paseo|camino|carretera|ctra|pol[ií]gono|pol\.)(?![\p{L}]))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Lo que viene detrás en la misma línea («… Destinatario: NUEVA VISION»): se corta antes de la siguiente etiqueta.</summary>
        private static readonly Regex SiguienteEtiqueta = new Regex(@"\s+\p{L}+\s*:", RegexOptions.Compiled);

        private static readonly Regex Direccion = new Regex(@"<?[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}>?", RegexOptions.Compiled);
        private static readonly Regex Espacios = new Regex(@"\s+", RegexOptions.Compiled);

        /// <summary>
        /// NestoAPI#857f: lo mismo y, si el cuerpo no lo dice, la frase del ASUNTO («🚚 ¡Nueva Vision, tu envío de IBEROCEL - PARAISO está en
        /// reparto!», SEUR).
        /// </summary>
        public static string Extraer(string texto, string asunto)
        {
            string delTexto = Extraer(texto);
            if (delTexto != null || string.IsNullOrWhiteSpace(asunto))
            {
                return delTexto;
            }
            Match m = FraseEnvioDe.Match(asunto);
            return m.Success ? Valido(m.Groups[1].Value) : null;
        }

        public static string Extraer(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            List<string> lineas = texto.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            string porEtiqueta = TrasEtiqueta(lineas, Etiqueta);
            if (porEtiqueta != null)
            {
                return porEtiqueta;
            }

            for (int i = 1; i < lineas.Count && i < LINEAS_TITULAR; i++)
            {
                Match m = LineaDe.Match(lineas[i]);
                if (m.Success)
                {
                    string valor = Valido(m.Groups[1].Value);
                    if (valor != null)
                    {
                        return valor;
                    }
                }
            }

            foreach (string linea in lineas)
            {
                Match m = FraseEnvioDe.Match(linea);
                if (m.Success)
                {
                    string valor = Valido(m.Groups[1].Value);
                    if (valor != null)
                    {
                        return valor;
                    }
                }
            }

            return TrasEtiqueta(lineas, EtiquetaFrom);
        }

        /// <summary>El primer valor válido tras la etiqueta: en la misma línea o, si está vacía, en la siguiente (ya sin líneas vacías).</summary>
        private static string TrasEtiqueta(List<string> lineas, Regex etiqueta)
        {
            for (int i = 0; i < lineas.Count; i++)
            {
                Match m = etiqueta.Match(lineas[i]);
                if (!m.Success)
                {
                    continue;
                }
                string bruto = m.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(bruto) && i + 1 < lineas.Count && !lineas[i + 1].Contains(":"))
                {
                    bruto = lineas[i + 1];
                }
                string valor = Valido(bruto);
                if (valor != null)
                {
                    return valor;
                }
            }
            return null;
        }

        private static string Valido(string bruto)
        {
            if (string.IsNullOrWhiteSpace(bruto))
            {
                return null;
            }
            Match siguiente = SiguienteEtiqueta.Match(bruto);
            string valor = siguiente.Success ? bruto.Substring(0, siguiente.Index) : bruto;
            string normalizado = Normalizar(valor);
            if (normalizado == null || normalizado.Count(char.IsLetter) < MINIMO_LETRAS)
            {
                return null;
            }
            if (Regex.IsMatch(normalizado, @"(?<![a-z])nueva\s+vision(?![a-z])"))
            {
                return null;
            }
            // El nombre de una agencia a secas es la agencia, no quien expide
            string comparable = TextoCorreo.Normalizar(normalizado);
            bool esAgencia = AgenciasEntradaRegistradas.Todas.OfType<AgenciaEntradaPorPatrones>()
                .SelectMany(a => (a.NombresEnTexto ?? new string[0]).Concat(new[] { a.Nombre }))
                .Any(n => TextoCorreo.Normalizar(n) == comparable);
            return esAgencia ? null : normalizado;
        }

        /// <summary>
        /// La forma en que se guarda y se compara: sin direcciones de correo, sin tildes, en minúsculas, con un solo espacio y sin signos
        /// al principio ni al final; como mucho <see cref="LONGITUD"/> caracteres. «  MBE Store C/O  Multiboxes S.L. » → «mbe store c/o
        /// multiboxes s.l». Null si no queda nada.
        /// </summary>
        public static string Normalizar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string sinCorreos = Direccion.Replace(texto, " ");
            string descompuesto = sinCorreos.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            string resultado = Espacios.Replace(sb.ToString().Normalize(NormalizationForm.FormC), " ")
                .Trim(' ', '.', ',', ';', ':', '-', '*', '"', '\'', '(', ')', '[', ']', '<', '>', '|', '¡', '!', '¿', '?');
            if (resultado.Length > LONGITUD)
            {
                resultado = resultado.Substring(0, LONGITUD).TrimEnd();
            }
            return resultado.Length == 0 ? null : resultado;
        }
    }
}
