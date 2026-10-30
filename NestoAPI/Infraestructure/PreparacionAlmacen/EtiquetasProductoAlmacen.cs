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
        /// <summary>NestoAPI#762: el EAN-13 (13 cifras) de la etiqueta EAN-13; null en la de la referencia.</summary>
        public string CodigoBarras { get; set; }
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
        /// NestoAPI#685: lo mismo en ZPL, para las Zebra con driver ZPL: mismas posiciones, texto escalable del tamaño de cada
        /// fuente EPL (va a la izquierda, no hace falta que sea monoespaciado) y <paramref name="copias"/> con ^PQ.
        /// </summary>
        public static string Zpl(EtiquetaProducto etiqueta, int copias)
        {
            string numero = Limpio(etiqueta.Producto);
            var zpl = new StringBuilder();
            zpl.Append(ComandosZpl.Inicio());
            for (int i = 0; i < etiqueta.Lineas.Count; i++)
            {
                zpl.Append(ComandosZpl.Texto(MARGEN_X, TEXTO_Y + i * TEXTO_INTERLINEA, etiqueta.Fuente, Limpio(etiqueta.Lineas[i])));
            }
            int estrecho = AnchoBarra(numero, out int x);
            zpl.Append(ComandosZpl.Codigo128(x, CODIGO_Y, estrecho, CODIGO_ALTO, numero));
            zpl.Append(ComandosZpl.Texto(x, NUMERO_Y, NUMERO_FUENTE, numero));
            zpl.Append(ComandosZpl.Fin(copias));
            return zpl.ToString();
        }

        /// <summary>NestoAPI#685: el trabajo en los dos idiomas; lo manda quien sabe cuál habla la impresora.</summary>
        public static TrabajoEtiquetas Trabajo(EtiquetaProducto etiqueta, int copias)
            => new TrabajoEtiquetas(Epl(etiqueta, copias), Zpl(etiqueta, copias), $"Etiqueta producto {etiqueta.Producto}");

        // ---------------------------------------------------------------- NestoAPI#762: la etiqueta con el EAN-13

        /// <summary>
        /// NestoAPI#762: el EAN-13 al ~80 % del tamaño nominal (módulo de 0,33 mm): a 203 ppp, módulo de 2 puntos (0,25 mm).
        /// 95 módulos = 190 puntos (23,75 mm); centrado en los 240 de ancho quedan 25 puntos a cada lado, más que las zonas de
        /// silencio (11 y 7 módulos = 22 y 14 puntos). Constantes para ajustarlas tras leer la etiqueta de prueba con la PDA.
        /// </summary>
        public const int EAN_MODULO = 2;
        public const int EAN_MODULOS = 95;
        public const int EAN_Y = 34;
        /// <summary>Las barras: 80 puntos (10 mm). Debajo, las 13 cifras que pone la impresora (~20 puntos): acaba hacia el 136 de 160.</summary>
        public const int EAN_ALTO = 80;
        /// <summary>Dos líneas de texto arriba en la fuente pequeña (8 × 12): el nombre y «Ref. 17404 · 100 m».</summary>
        public const int EAN_TEXTO_INTERLINEA = 14;
        public const int EAN_LINEAS = 2;

        /// <summary>La x del EAN-13: centrado.</summary>
        public static int EanX => (GeneradorEtiquetasHueco.ANCHO - (EAN_MODULOS * EAN_MODULO)) / 2;

        /// <summary>
        /// NestoAPI#762: arriba el nombre (una línea, cortado si no cabe) y «Ref. 17404 · 100 m»; debajo, el EAN-13 con sus cifras.
        /// </summary>
        public static EtiquetaProducto ComponerEan13(FichaProductoAlmacen ficha, string codigo)
        {
            string limpio = codigo?.Trim();
            if (limpio == null || limpio.Length != 13 || !Productos.ValidadorGtin.EsCodigoValido(limpio))
            {
                throw new ArgumentException($"«{codigo}» no es un EAN-13 válido.", nameof(codigo));
            }
            string producto = ficha?.Producto?.Trim();
            string tamano = ficha?.Tamano > 0
                ? (ficha.Tamano.Value.ToString(CultureInfo.InvariantCulture) + " " + (ficha.UnidadMedida ?? string.Empty).Trim()).Trim()
                : null;
            string nombre = Partir(ficha?.Nombre, CARACTERES_PEQUENA).FirstOrDefault() ?? string.Empty;
            string referencia = string.Join(" · ", new[] { "Ref. " + producto, tamano }.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (referencia.Length > CARACTERES_PEQUENA)
            {
                referencia = referencia.Substring(0, CARACTERES_PEQUENA).TrimEnd();
            }
            return new EtiquetaProducto
            {
                Producto = producto,
                Lineas = new[] { nombre, referencia }.Where(l => !string.IsNullOrWhiteSpace(l)).ToList(),
                Fuente = FUENTE_PEQUENA,
                CodigoBarras = limpio
            };
        }

        /// <summary>NestoAPI#762: en EPL2, EAN-13 (E30) con las cifras debajo (B). La impresora pone el dígito de control: van 12.</summary>
        public static string EplEan13(EtiquetaProducto etiqueta, int copias)
        {
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n");
            epl.Append("q").Append(GeneradorEtiquetasHueco.ANCHO).Append("\r\n");
            epl.Append("Q").Append(GeneradorEtiquetasHueco.ALTO).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            epl.Append("N\r\n");
            for (int i = 0; i < etiqueta.Lineas.Count; i++)
            {
                epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                    MARGEN_X, TEXTO_Y + i * EAN_TEXTO_INTERLINEA, FUENTE_PEQUENA, Texto(etiqueta.Lineas[i])));
            }
            epl.Append(string.Format(CultureInfo.InvariantCulture, "B{0},{1},0,E30,{2},{2},{3},B,\"{4}\"\r\n",
                EanX, EAN_Y, EAN_MODULO, EAN_ALTO, etiqueta.CodigoBarras.Substring(0, 12)));
            epl.Append("P").Append(copias.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            return epl.ToString();
        }

        /// <summary>NestoAPI#762: en ZPL, ^BE con la línea de cifras debajo. También 12 cifras: la impresora pone el control.</summary>
        public static string ZplEan13(EtiquetaProducto etiqueta, int copias)
        {
            var zpl = new StringBuilder();
            zpl.Append(ComandosZpl.Inicio());
            for (int i = 0; i < etiqueta.Lineas.Count; i++)
            {
                zpl.Append(ComandosZpl.Texto(MARGEN_X, TEXTO_Y + i * EAN_TEXTO_INTERLINEA, FUENTE_PEQUENA, Limpio(etiqueta.Lineas[i])));
            }
            zpl.Append(ComandosZpl.Ean13(EanX, EAN_Y, EAN_MODULO, EAN_ALTO, etiqueta.CodigoBarras));
            zpl.Append(ComandosZpl.Fin(copias));
            return zpl.ToString();
        }

        /// <summary>NestoAPI#762: el trabajo de la etiqueta EAN-13 en los dos idiomas.</summary>
        public static TrabajoEtiquetas TrabajoEan13(EtiquetaProducto etiqueta, int copias)
            => new TrabajoEtiquetas(EplEan13(etiqueta, copias), ZplEan13(etiqueta, copias), $"Etiqueta EAN-13 producto {etiqueta.Producto}");

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
            => Limpio(texto).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Sin caracteres de control ni espacios de más (ZPL escapa lo suyo en <see cref="ComandosZpl.Escapar"/>).</summary>
        private static string Limpio(string texto)
            => Compactar(new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()));
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
        private readonly IImpresoraEtiquetasAlmacen impresora;

        public const string FORMATO_REFERENCIA = "Referencia";
        public const string FORMATO_EAN13 = "Ean13";

        private readonly Productos.IServicioCodigosBarras codigos;

        public ServicioEtiquetasProducto(IRepositorioFichasProducto fichas, ILectorParametrosUsuario parametros, IImpresoraEtiquetasAlmacen impresora)
            : this(fichas, parametros, impresora, null)
        {
        }

        /// <param name="codigos">NestoAPI#762: para la etiqueta EAN-13 (el código del producto o su código interno). Sin él, solo la de la referencia.</param>
        public ServicioEtiquetasProducto(IRepositorioFichasProducto fichas, ILectorParametrosUsuario parametros, IImpresoraEtiquetasAlmacen impresora,
            Productos.IServicioCodigosBarras codigos)
        {
            this.fichas = fichas ?? throw new ArgumentNullException(nameof(fichas));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.codigos = codigos;
        }

        /// <summary>NestoAPI#762: true si piden la EAN-13; false si la de la referencia (o sin formato). Otro valor: ArgumentException.</summary>
        internal static bool EsEan13(string formato)
        {
            string limpio = formato?.Trim();
            if (string.IsNullOrEmpty(limpio) || string.Equals(limpio, FORMATO_REFERENCIA, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(limpio, FORMATO_EAN13, StringComparison.OrdinalIgnoreCase) || string.Equals(limpio, "EAN-13", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            throw new ArgumentException($"El formato de la etiqueta tiene que ser {FORMATO_REFERENCIA} o {FORMATO_EAN13}.");
        }

        /// <summary>
        /// NestoAPI#762: el EAN-13 que va en la etiqueta de la UNIDAD: uno activo de 13 cifras con Cantidad 1 que no sea interno
        /// (el principal primero); si no, el interno que ya tenga. Null si no tiene ninguno de los dos (hay que darle el interno).
        /// Un código de caja (Cantidad 100) nunca: cada lectura contaría 100.
        /// </summary>
        internal static Productos.CodigoBarrasProductoDTO CodigoDeLaUnidad(IEnumerable<Productos.CodigoBarrasProductoDTO> lista)
        {
            List<Productos.CodigoBarrasProductoDTO> validos = (lista ?? Enumerable.Empty<Productos.CodigoBarrasProductoDTO>())
                .Where(c => c != null && c.Activo && c.Cantidad == 1 && c.Codigo?.Trim().Length == 13 && Productos.ValidadorGtin.EsCodigoValido(c.Codigo))
                .ToList();
            return validos.Where(c => !Productos.CodigoBarrasInterno.EsInterno(c.Codigo)).OrderByDescending(c => c.Principal).FirstOrDefault()
                ?? validos.FirstOrDefault(c => Productos.CodigoBarrasInterno.EsInterno(c.Codigo));
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

            bool ean13 = EsEan13(peticion.Formato);
            if (ean13 && codigos == null)
            {
                throw new ArgumentException("Esta API no sabe imprimir la etiqueta EAN-13.");
            }

            FichaProductoAlmacen ficha = (await fichas.LeerFichas(empresa, new[] { producto }).ConfigureAwait(false) ?? new List<FichaProductoAlmacen>())
                .FirstOrDefault(f => string.Equals(f?.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No existe el producto {producto}.");

            if (ean13)
            {
                return await ImprimirEan13(empresa, ficha, copias, usuario, ensayo).ConfigureAwait(false);
            }

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
            _ = impresora.Imprimir(destino, GeneradorEtiquetasProducto.Trabajo(etiqueta, copias));
            resultado.Impresas = copias;
            resultado.Mensaje = $"{Etiquetas(copias)} de {etiqueta.Producto} mandada{(copias == 1 ? string.Empty : "s")} a {destino}.";
            return resultado;
        }

        /// <summary>
        /// NestoAPI#762: la 3×2 con el EAN-13 de la unidad; si el producto no tiene, se le da su código interno (en el ensayo
        /// solo se dice cuál le tocaría). La impresora se mira ANTES de dar el código: sin impresora no se crea nada.
        /// </summary>
        private async Task<ResultadoEtiquetasProductoDTO> ImprimirEan13(string empresa, FichaProductoAlmacen ficha, int copias, string usuario, bool ensayo)
        {
            string producto = ficha.Producto.Trim();
            string destino = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, empresa, usuario);
            Productos.CodigoBarrasProductoDTO elegido = CodigoDeLaUnidad(await codigos.Listar(empresa, producto).ConfigureAwait(false));
            bool asignado = false;
            // En el ensayo AsignarInterno no guarda: si el código sale de ahí, es que se le daría al imprimir
            bool porDar = elegido == null && ensayo;
            if (elegido == null)
            {
                Productos.ResultadoCodigoBarras interno = await codigos.AsignarInterno(empresa, producto, usuario, ensayo).ConfigureAwait(false);
                if (interno.Estado == Productos.EstadoOperacionCodigoBarras.NoEncontrado || interno.Estado == Productos.EstadoOperacionCodigoBarras.NoValido)
                {
                    throw new ArgumentException(interno.Mensaje);
                }
                elegido = interno.Codigo;
                asignado = interno.Estado == Productos.EstadoOperacionCodigoBarras.Creado;
            }
            var resultado = new ResultadoEtiquetasProductoDTO
            {
                Producto = producto,
                Copias = copias,
                Impresora = destino,
                CodigoBarras = elegido?.Codigo?.Trim(),
                CodigoInterno = elegido == null || Productos.CodigoBarrasInterno.EsInterno(elegido.Codigo),
                CodigoAsignado = asignado
            };
            if (elegido == null)
            {
                // Solo en el ensayo: le tocaría uno del contador, que no se sabe hasta darlo
                resultado.Lineas = GeneradorEtiquetasProducto.ComponerEan13(ficha, Productos.CodigoBarrasInterno.DeContador(1)).Lineas;
                resultado.Mensaje = $"Se imprimirían {Etiquetas(copias)} de {producto} con un código interno nuevo del contador en {destino}.";
                return resultado;
            }
            EtiquetaProducto etiqueta = GeneradorEtiquetasProducto.ComponerEan13(ficha, resultado.CodigoBarras);
            resultado.Lineas = etiqueta.Lineas;
            string queCodigo = resultado.CodigoInterno ? $"código interno {resultado.CodigoBarras}" : resultado.CodigoBarras;
            if (ensayo)
            {
                resultado.Mensaje = $"Se imprimirían {Etiquetas(copias)} de {producto} ({queCodigo}" +
                    (porDar ? ", que se le daría ahora" : string.Empty) + $") en {destino}.";
                return resultado;
            }
            _ = impresora.Imprimir(destino, GeneradorEtiquetasProducto.TrabajoEan13(etiqueta, copias));
            resultado.Impresas = copias;
            resultado.Mensaje = $"{Etiquetas(copias)} de {producto} ({queCodigo}" + (asignado ? ", recién dado" : string.Empty)
                + $") mandada{(copias == 1 ? string.Empty : "s")} a {destino}.";
            return resultado;
        }

        private static string Etiquetas(int n) => n == 1 ? "1 etiqueta" : $"{n} etiquetas";
    }
}
