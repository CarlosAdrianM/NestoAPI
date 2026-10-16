using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que lleva la etiqueta de un producto: las líneas de texto, en qué fuente y el número que va en el código.</summary>
    public class EtiquetaProducto
    {
        public string Producto { get; set; }
        public List<string> Lineas { get; set; } = new List<string>();
        public int Fuente { get; set; }
    }

    /// <summary>
    /// NestoAPI#654 (sugerencia 546 de Paloma): la etiqueta de código de barras de producto, igual que la que ya usan en tienda
    /// (foto del comentario 167): arriba el nombre, el subgrupo y el tamaño con su unidad («ROLLO PAPEL CAMILLA» /
    /// «Desechables» / «100 m»), debajo el código de barras con el número del producto y, debajo, el número en grande.
    /// <para>Mismo papel que las etiquetas de hueco (30×20 mm, en la impresora ImpresoraCodBarras) y mismo idioma, EPL2 a
    /// 203 ppp (1 mm = 8 puntos). Las posiciones son constantes para ajustarlas tras la etiqueta de prueba.</para>
    /// </summary>
    public static class GeneradorEtiquetasProducto
    {
        public const int MARGEN_X = 12;

        // Hasta tres líneas de texto arriba. Fuente 2 (10 × 16 puntos): 21 caracteres en 216 puntos; si el texto no cabe, la
        // fuente 1 (8 × 12): 27 caracteres
        public const int TEXTO_Y = 6;
        public const int TEXTO_INTERLINEA = 18;
        public const int MAXIMO_LINEAS = 3;
        public const int FUENTE_NORMAL = 2;
        public const int CARACTERES_NORMAL = 21;
        public const int FUENTE_PEQUENA = 1;
        public const int CARACTERES_PEQUENA = 27;

        // Code128 con el número del producto; el número va aparte, más grande (fuente 4, 14 × 24 puntos)
        public const int CODIGO_Y = 62;
        public const int CODIGO_ALTO = 52;
        public const int NUMERO_Y = 120;
        public const int NUMERO_FUENTE = 4;

        /// <summary>Las líneas de la etiqueta: en la fuente normal si cabe todo; si no, en la pequeña (recortando lo que sobre).</summary>
        public static EtiquetaProducto Componer(FichaProductoAlmacen ficha)
        {
            string tamano = ficha?.Tamano > 0
                ? (ficha.Tamano.Value.ToString(CultureInfo.InvariantCulture) + " " + (ficha.UnidadMedida ?? string.Empty).Trim()).Trim()
                : null;
            List<string> lineas = Lineas(ficha?.Nombre, ficha?.Subgrupo, tamano, CARACTERES_NORMAL, out bool completo);
            int fuente = FUENTE_NORMAL;
            if (!completo)
            {
                lineas = Lineas(ficha?.Nombre, ficha?.Subgrupo, tamano, CARACTERES_PEQUENA, out _);
                fuente = FUENTE_PEQUENA;
            }
            return new EtiquetaProducto { Producto = ficha?.Producto?.Trim(), Lineas = lineas, Fuente = fuente };
        }

        /// <summary>
        /// El nombre partido por palabras y, debajo, el subgrupo y el tamaño. Si no caben en tres líneas, el nombre ocupa dos
        /// como mucho y el subgrupo y el tamaño van juntos en la tercera. Completo es false si algo se ha quedado fuera.
        /// </summary>
        internal static List<string> Lineas(string nombre, string subgrupo, string tamano, int caracteres, out bool completo)
        {
            completo = true;
            List<string> detalle = new[] { subgrupo, tamano }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => Compactar(t))
                .ToList();
            List<string> lineasNombre = Partir(nombre, caracteres);
            if (lineasNombre.Count + detalle.Count <= MAXIMO_LINEAS)
            {
                return lineasNombre.Concat(detalle).ToList();
            }
            int lineasParaNombre = detalle.Count == 0 ? MAXIMO_LINEAS : MAXIMO_LINEAS - 1;
            if (lineasNombre.Count > lineasParaNombre)
            {
                completo = false;
            }
            var lineas = lineasNombre.Take(lineasParaNombre).ToList();
            if (detalle.Count > 0)
            {
                string junto = string.Join(" ", detalle);
                if (junto.Length > caracteres)
                {
                    completo = false;
                    junto = junto.Substring(0, caracteres).TrimEnd();
                }
                lineas.Add(junto);
            }
            return lineas;
        }

        /// <summary>Partido por palabras en líneas de como mucho <paramref name="caracteres"/>; una palabra más larga se corta.</summary>
        internal static List<string> Partir(string texto, int caracteres)
        {
            var lineas = new List<string>();
            var actual = new StringBuilder();
            foreach (string palabra in Compactar(texto).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string resto = palabra;
                while (resto.Length > 0)
                {
                    int sitio = actual.Length == 0 ? caracteres : caracteres - actual.Length - 1;
                    if (resto.Length <= sitio)
                    {
                        _ = actual.Length == 0 ? actual.Append(resto) : actual.Append(' ').Append(resto);
                        resto = string.Empty;
                    }
                    else if (actual.Length > 0)
                    {
                        lineas.Add(actual.ToString());
                        _ = actual.Clear();
                    }
                    else
                    {
                        lineas.Add(resto.Substring(0, caracteres));
                        resto = resto.Substring(caracteres);
                    }
                }
            }
            if (actual.Length > 0)
            {
                lineas.Add(actual.ToString());
            }
            return lineas;
        }

        /// <summary>Un trabajo con <paramref name="copias"/> etiquetas iguales del producto.</summary>
        public static string Epl(EtiquetaProducto etiqueta, int copias)
        {
            string numero = Texto(etiqueta.Producto);
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n"); // 8 bits, Windows 1252 (como lo escribe ImpresoraEtiquetasWindows), España
            epl.Append("q").Append(GeneradorEtiquetasHueco.ANCHO).Append("\r\n");
            epl.Append("Q").Append(GeneradorEtiquetasHueco.ALTO).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            epl.Append("N\r\n");
            for (int i = 0; i < etiqueta.Lineas.Count; i++)
            {
                epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                    MARGEN_X, TEXTO_Y + i * TEXTO_INTERLINEA, etiqueta.Fuente, Texto(etiqueta.Lineas[i])));
            }
            int estrecho = AnchoBarra(numero, out int x);
            epl.Append(string.Format(CultureInfo.InvariantCulture, "B{0},{1},0,1,{2},{2},{3},N,\"{4}\"\r\n",
                x, CODIGO_Y, estrecho, CODIGO_ALTO, numero));
            epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                x, NUMERO_Y, NUMERO_FUENTE, numero));
            epl.Append("P").Append(copias.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            return epl.ToString();
        }

        /// <summary>
        /// Barras de 2 puntos si el código cabe (un Code128 de n caracteres ocupa como mucho 11·n + 35 módulos); si no, de 1. La
        /// x es el margen de la etiqueta si cabe con él, o la que lo centra.
        /// </summary>
        private static int AnchoBarra(string codigo, out int x)
        {
            int modulos = (11 * codigo.Length) + 35;
            int disponible = GeneradorEtiquetasHueco.ANCHO - (2 * 4);
            int estrecho = modulos * 2 <= disponible ? 2 : 1;
            int ancho = modulos * estrecho;
            x = ancho <= GeneradorEtiquetasHueco.ANCHO - (2 * MARGEN_X) ? MARGEN_X : Math.Max(0, (GeneradorEtiquetasHueco.ANCHO - ancho) / 2);
            return estrecho;
        }

        private static string Compactar(string texto)
            => string.Join(" ", (texto ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Dentro de las comillas de EPL, la barra y las comillas van escapadas; sin saltos de línea.</summary>
        private static string Texto(string texto)
            => Compactar(new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()))
                .Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public interface IServicioEtiquetasProducto
    {
        /// <exception cref="ArgumentException">Producto que no existe, copias de más o usuario sin impresora: el motivo es para el usuario.</exception>
        /// <exception cref="ImpresionEtiquetasException">Windows no ha dejado imprimir.</exception>
        Task<ResultadoEtiquetasProductoDTO> Imprimir(string empresa, EtiquetasProductoDTO peticion, string usuario, bool ensayo);
    }

    /// <summary>
    /// NestoAPI#654: el único sitio que imprime etiquetas de código de barras de producto (Nesto: ficha del producto y Recibir
    /// reposición; mañana Ariadna). Los datos salen de la ficha que ya lee el almacén (FichasProductoAlmacen), la impresora y
    /// el envío son los de las etiquetas de hueco. Con ensayo, dice qué saldría y dónde, sin imprimir.
    /// </summary>
    public class ServicioEtiquetasProducto : IServicioEtiquetasProducto
    {
        public const int MAXIMO_COPIAS = 100;

        private readonly IRepositorioFichasProducto fichas;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetas impresora;

        public ServicioEtiquetasProducto(IRepositorioFichasProducto fichas, ILectorParametrosUsuario parametros, IImpresoraEtiquetas impresora)
        {
            this.fichas = fichas ?? throw new ArgumentNullException(nameof(fichas));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
        }

        public async Task<ResultadoEtiquetasProductoDTO> Imprimir(string empresa, EtiquetasProductoDTO peticion, string usuario, bool ensayo)
        {
            string producto = peticion?.Producto?.Trim();
            if (string.IsNullOrEmpty(producto))
            {
                throw new ArgumentException("Falta el producto del que imprimir la etiqueta.");
            }
            int copias = peticion.Copias <= 0 ? 1 : peticion.Copias;
            if (copias > MAXIMO_COPIAS)
            {
                throw new ArgumentException($"Como mucho se imprimen {MAXIMO_COPIAS} etiquetas de una vez.");
            }

            FichaProductoAlmacen ficha = (await fichas.LeerFichas(empresa, new[] { producto }).ConfigureAwait(false) ?? new List<FichaProductoAlmacen>())
                .FirstOrDefault(f => string.Equals(f?.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No existe el producto {producto}.");

            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.Componer(ficha);
            string destino = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, empresa, usuario);
            var resultado = new ResultadoEtiquetasProductoDTO
            {
                Producto = etiqueta.Producto,
                Lineas = etiqueta.Lineas,
                Copias = copias,
                Impresora = destino
            };
            if (ensayo)
            {
                resultado.Mensaje = $"Se imprimirían {Etiquetas(copias)} de {etiqueta.Producto} en {destino}.";
                return resultado;
            }
            impresora.Imprimir(destino, GeneradorEtiquetasProducto.Epl(etiqueta, copias), $"Etiqueta producto {etiqueta.Producto}");
            resultado.Impresas = copias;
            resultado.Mensaje = $"{Etiquetas(copias)} de {etiqueta.Producto} mandada{(copias == 1 ? string.Empty : "s")} a {destino}.";
            return resultado;
        }

        private static string Etiquetas(int n) => n == 1 ? "1 etiqueta" : $"{n} etiquetas";
    }
}
