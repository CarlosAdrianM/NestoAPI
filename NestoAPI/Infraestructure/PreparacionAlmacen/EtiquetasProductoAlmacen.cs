using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Lo que lleva la etiqueta de un producto: las líneas de texto, en qué fuente y el EAN-13 que va en el código.</summary>
    public class EtiquetaProducto
    {
        public string Producto { get; set; }
        public List<string> Lineas { get; set; } = new List<string>();
        public int Fuente { get; set; }
        /// <summary>NestoAPI#762: el EAN-13 (13 cifras) de la etiqueta.</summary>
        public string CodigoBarras { get; set; }
    }

    /// <summary>
    /// NestoAPI#654 (sugerencia 546 de Paloma) y NestoAPI#762/#791: la etiqueta de código de barras de producto, de 30×20 mm
    /// (impresora ImpresoraCodBarras), en EPL2 y ZPL a 203 ppp (1 mm = 8 puntos). Desde NestoAPI#791 es la única: arriba el
    /// nombre y «Ref. 17404 · 100 m»; debajo, el EAN-13 de la unidad (el del fabricante o el interno del 29) con sus cifras.
    /// Las posiciones son constantes para ajustarlas tras la etiqueta de prueba.
    /// </summary>
    public static class GeneradorEtiquetasProducto
    {
        public const int MARGEN_X = 12;
        public const int TEXTO_Y = 6;
        /// <summary>Fuente 1 (8 × 12 puntos): 27 caracteres en 216 puntos.</summary>
        public const int FUENTE_PEQUENA = 1;
        public const int CARACTERES_PEQUENA = 27;

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
    /// NestoAPI#654: el único sitio que imprime etiquetas de código de barras de producto (Nesto y Ariadna). Los datos salen de
    /// la ficha que ya lee el almacén (FichasProductoAlmacen), la impresora y el envío son los de las etiquetas de hueco. Con
    /// ensayo, dice qué saldría y dónde, sin imprimir. NestoAPI#791: siempre la EAN-13 (si el producto no tiene, se le da el
    /// interno del 29 al imprimir).
    /// </summary>
    public class ServicioEtiquetasProducto : IServicioEtiquetasProducto
    {
        public const int MAXIMO_COPIAS = 100;

        private readonly IRepositorioFichasProducto fichas;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetasAlmacen impresora;

        private readonly Productos.IServicioCodigosBarras codigos;

        /// <param name="codigos">NestoAPI#762: el código del producto o su código interno, que va en la etiqueta EAN-13.</param>
        public ServicioEtiquetasProducto(IRepositorioFichasProducto fichas, ILectorParametrosUsuario parametros, IImpresoraEtiquetasAlmacen impresora,
            Productos.IServicioCodigosBarras codigos)
        {
            this.fichas = fichas ?? throw new ArgumentNullException(nameof(fichas));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.codigos = codigos ?? throw new ArgumentNullException(nameof(codigos));
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

            // NestoAPI#791: la EAN-13 es la única etiqueta; peticion.Formato se acepta (Nesto y Ariadna lo mandan) y se ignora
            FichaProductoAlmacen ficha = (await fichas.LeerFichas(empresa, new[] { producto }).ConfigureAwait(false) ?? new List<FichaProductoAlmacen>())
                .FirstOrDefault(f => string.Equals(f?.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"No existe el producto {producto}.");

            return await ImprimirEan13(empresa, ficha, copias, usuario, ensayo).ConfigureAwait(false);
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
