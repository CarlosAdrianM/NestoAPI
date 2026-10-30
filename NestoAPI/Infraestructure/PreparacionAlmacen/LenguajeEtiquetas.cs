using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#685: el idioma que entiende una impresora de etiquetas.</summary>
    public enum LenguajeEtiquetas
    {
        /// <summary>EPL2: las «Generic / Text Only» de Algete y Reina (y lo que no sabemos qué es: como hasta ahora).</summary>
        Epl,
        /// <summary>ZPL II: las Zebra con driver ZDesigner ZPL (Alcobendas ZD230, Etiquetas3 ZD420).</summary>
        Zpl
    }

    /// <summary>
    /// NestoAPI#685: un trabajo de etiquetas nuestro (producto, hueco, bulto, prueba) escrito en los dos idiomas. Quien imprime
    /// (<see cref="ImpresoraEtiquetasConAjuste"/>) elige el de la impresora a la que va; los servicios no saben de idiomas.
    /// </summary>
    public class TrabajoEtiquetas
    {
        public TrabajoEtiquetas(string epl, string zpl, string nombre)
        {
            Epl = epl;
            Zpl = zpl;
            Nombre = nombre;
        }

        public string Epl { get; }
        public string Zpl { get; }
        /// <summary>El nombre del trabajo en la cola de Windows.</summary>
        public string Nombre { get; }

        public string En(LenguajeEtiquetas lenguaje) => lenguaje == LenguajeEtiquetas.Zpl ? Zpl : Epl;
    }

    /// <summary>NestoAPI#685: imprime nuestras etiquetas en el idioma de cada impresora y con su ajuste de posición.</summary>
    public interface IImpresoraEtiquetasAlmacen
    {
        /// <returns>El idioma en el que se ha mandado.</returns>
        /// <exception cref="ImpresionEtiquetasException">Si Windows no deja abrir la impresora o escribir en ella.</exception>
        LenguajeEtiquetas Imprimir(string impresora, TrabajoEtiquetas trabajo);
    }

    /// <summary>NestoAPI#685: qué idioma habla cada impresora. Un solo sitio para decidirlo.</summary>
    public interface ILenguajeImpresoras
    {
        LenguajeEtiquetas De(string impresora);
    }

    /// <summary>
    /// NestoAPI#685: el idioma de cada impresora de etiquetas, decidido en un solo sitio. La de Alcobendas (Zebra ZD230 con el
    /// driver «ZDesigner ZD230-203dpi ZPL») sacaba en blanco las etiquetas en EPL; las «Generic / Text Only» de Algete y Reina
    /// solo pasan lo que les llega y la impresora lo entiende en EPL.
    /// <para>Cómo se decide, por orden:</para>
    /// <list type="number">
    /// <item>Forzado en Web.config (appSettings), por si un driver engaña: <c>ImpresorasEtiquetasZpl</c> y
    /// <c>ImpresorasEtiquetasEpl</c>, con las impresoras separadas por punto y coma, tal cual están en el parámetro
    /// ImpresoraCodBarras (sin distinguir mayúsculas), p. ej.
    /// <c>&lt;add key="ImpresorasEtiquetasZpl" value="\\RDS2016\etiquetas4" /&gt;</c>. Ojo: el deploy publica el Web.config
    /// del repositorio, así que el forzado se commitea, no se toca a mano en el servidor.</item>
    /// <item>Por el nombre del driver de la cola de Windows (el mismo OpenPrinter con el que se imprime, nivel 2 de
    /// GetPrinter): con «EPL» en el nombre, EPL (los ZDesigner de las Zebra EPL se llaman «ZDesigner GK420d (EPL)» o
    /// «ZDesigner LP 2844»… ojo, este último no lo dice); si no, con «ZPL» o «ZDesigner», ZPL; cualquier otro (Generic / Text
    /// Only, Honeywell…), EPL, como hasta ahora.</item>
    /// <item>Si no se puede leer el driver, EPL (como hasta ahora) y se apunta en ELMAH; no se guarda, se vuelve a intentar en
    /// el siguiente trabajo.</item>
    /// </list>
    /// <para>Lo leído se guarda mientras viva la aplicación (un reciclado del grupo de IIS lo vuelve a leer): cambiar el driver
    /// de una cola necesita reciclar.</para>
    /// </summary>
    public class LenguajeImpresorasWindows : ILenguajeImpresoras
    {
        public const string CLAVE_FORZAR_ZPL = "ImpresorasEtiquetasZpl";
        public const string CLAVE_FORZAR_EPL = "ImpresorasEtiquetasEpl";

        /// <summary>La de producción: una sola caché para toda la aplicación.</summary>
        public static LenguajeImpresorasWindows Compartido { get; } = new LenguajeImpresorasWindows(
            LeerDriverWindows, () => Forzadas(ConfigurationManager.AppSettings[CLAVE_FORZAR_ZPL], ConfigurationManager.AppSettings[CLAVE_FORZAR_EPL]));

        private readonly Func<string, string> leerDriver;
        private readonly Func<IDictionary<string, LenguajeEtiquetas>> leerForzadas;
        private readonly ConcurrentDictionary<string, LenguajeEtiquetas> cache = new ConcurrentDictionary<string, LenguajeEtiquetas>();

        /// <param name="leerDriver">El nombre del driver de la cola; puede lanzar si no se puede leer.</param>
        /// <param name="leerForzadas">Las forzadas, con la clave ya normalizada (<see cref="ServicioAjustesImpresoras.Normalizar"/>).</param>
        public LenguajeImpresorasWindows(Func<string, string> leerDriver, Func<IDictionary<string, LenguajeEtiquetas>> leerForzadas = null)
        {
            this.leerDriver = leerDriver ?? throw new ArgumentNullException(nameof(leerDriver));
            this.leerForzadas = leerForzadas ?? (() => new Dictionary<string, LenguajeEtiquetas>());
        }

        public LenguajeEtiquetas De(string impresora)
        {
            string clave = ServicioAjustesImpresoras.Normalizar(impresora);
            if (clave == null)
            {
                return LenguajeEtiquetas.Epl;
            }
            IDictionary<string, LenguajeEtiquetas> forzadas = leerForzadas() ?? new Dictionary<string, LenguajeEtiquetas>();
            if (forzadas.TryGetValue(clave, out LenguajeEtiquetas forzado))
            {
                return forzado;
            }
            if (cache.TryGetValue(clave, out LenguajeEtiquetas guardado))
            {
                return guardado;
            }
            string driver;
            try
            {
                driver = leerDriver(impresora.Trim());
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new InvalidOperationException(
                    $"NestoAPI#685: no se ha podido leer el driver de la impresora {impresora}; se manda en EPL.", ex));
                return LenguajeEtiquetas.Epl;
            }
            return cache.GetOrAdd(clave, DelDriver(driver));
        }

        /// <summary>El idioma según el nombre del driver de Windows (ver el resumen de la clase).</summary>
        public static LenguajeEtiquetas DelDriver(string driver)
        {
            string nombre = driver?.ToUpperInvariant() ?? string.Empty;
            if (Regex.IsMatch(nombre, @"\bEPL"))
            {
                return LenguajeEtiquetas.Epl;
            }
            return nombre.Contains("ZPL") || nombre.Contains("ZDESIGNER") ? LenguajeEtiquetas.Zpl : LenguajeEtiquetas.Epl;
        }

        /// <summary>Las impresoras forzadas de las dos claves (si alguna sale en las dos, gana ZPL).</summary>
        public static IDictionary<string, LenguajeEtiquetas> Forzadas(string zpl, string epl)
        {
            var forzadas = new Dictionary<string, LenguajeEtiquetas>(StringComparer.Ordinal);
            void Anadir(string lista, LenguajeEtiquetas lenguaje)
            {
                foreach (string clave in (lista ?? string.Empty).Split(';').Select(ServicioAjustesImpresoras.Normalizar).Where(c => c != null))
                {
                    forzadas[clave] = lenguaje;
                }
            }
            Anadir(epl, LenguajeEtiquetas.Epl);
            Anadir(zpl, LenguajeEtiquetas.Zpl);
            return forzadas;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool OpenPrinter(string nombre, out IntPtr impresora, IntPtr opciones);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool ClosePrinter(IntPtr impresora);

        [DllImport("winspool.Drv", EntryPoint = "GetPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool GetPrinter(IntPtr impresora, int nivel, IntPtr datos, int tamano, out int necesario);

        /// <summary>
        /// El pDriverName de PRINTER_INFO_2, con el mismo OpenPrinter que imprime: vale para «\\RDS2016\etiquetasalcobendas»
        /// igual que para una cola local.
        /// </summary>
        public static string LeerDriverWindows(string impresora)
        {
            if (!OpenPrinter(impresora, out IntPtr manejador, IntPtr.Zero))
            {
                throw new ImpresionEtiquetasException($"No se puede abrir la impresora {impresora} (error de Windows {Marshal.GetLastWin32Error()}).");
            }
            try
            {
                _ = GetPrinter(manejador, 2, IntPtr.Zero, 0, out int necesario);
                if (necesario <= 0)
                {
                    throw new ImpresionEtiquetasException($"No se pueden leer los datos de la impresora {impresora} (error de Windows {Marshal.GetLastWin32Error()}).");
                }
                IntPtr datos = Marshal.AllocHGlobal(necesario);
                try
                {
                    if (!GetPrinter(manejador, 2, datos, necesario, out _))
                    {
                        throw new ImpresionEtiquetasException($"No se pueden leer los datos de la impresora {impresora} (error de Windows {Marshal.GetLastWin32Error()}).");
                    }
                    // PRINTER_INFO_2W: pServerName, pPrinterName, pShareName, pPortName, pDriverName, …
                    IntPtr driver = Marshal.ReadIntPtr(datos, 4 * IntPtr.Size);
                    return driver == IntPtr.Zero ? null : Marshal.PtrToStringUni(driver);
                }
                finally
                {
                    Marshal.FreeHGlobal(datos);
                }
            }
            finally
            {
                _ = ClosePrinter(manejador);
            }
        }
    }

    /// <summary>
    /// NestoAPI#685: piezas de ZPL II para nuestras etiquetas, con las mismas medidas que el EPL (203 ppp, 1 mm = 8 puntos).
    /// Sin ^PW ni ^LL: la Zebra de Alcobendas imprime también las etiquetas de agencia y esos valores se quedan en la
    /// impresora para los trabajos siguientes. Sí ^LH0,0 (también se queda: así lo dejamos siempre igual) y ^CI28 (UTF-8),
    /// con los caracteres que no son ASCII en hexadecimal (^FH), para no depender de cómo viajen los bytes.
    /// </summary>
    public static class ComandosZpl
    {
        /// <summary>Equivalencias de las fuentes EPL (celda alto × ancho en puntos) para el texto escalable ^A0 de ZPL.</summary>
        private static readonly Dictionary<int, int[]> fuentesEpl = new Dictionary<int, int[]>
        {
            [1] = new[] { 12, 8 },
            [2] = new[] { 16, 10 },
            [3] = new[] { 20, 12 },
            [4] = new[] { 24, 14 },
            [5] = new[] { 48, 32 }
        };

        public static string Inicio() => "^XA^CI28^LH0,0\r\n";

        public static string Fin(int copias = 1) => "^PQ" + Math.Max(1, copias).ToString(CultureInfo.InvariantCulture) + "^XZ\r\n";

        /// <summary>Code128 con elección automática de juego (como el «1» de EPL), sin texto debajo.</summary>
        public static string Codigo128(int x, int y, int estrecho, int alto, string datos)
            => F("^FO{0},{1}^BY{2}^BCN,{3},N,N,N,A^FH^FD{4}^FS", x, y, estrecho, alto, Escapar(datos));

        /// <summary>
        /// NestoAPI#762: EAN-13 (^BE) con las cifras debajo. Van las 12 primeras: la impresora calcula el dígito de control (si se
        /// le dan 13, unas versiones lo ignoran y otras lo imprimen mal).
        /// </summary>
        public static string Ean13(int x, int y, int modulo, int alto, string codigo)
        {
            string cifras = new string((codigo ?? string.Empty).Where(char.IsDigit).ToArray());
            return F("^FO{0},{1}^BY{2}^BEN,{3},Y,N^FD{4}^FS", x, y, modulo, alto, cifras.Length > 12 ? cifras.Substring(0, 12) : cifras);
        }

        /// <summary>
        /// Texto en la fuente D de ZPL (18 × 10 más 2 de separación): la misma celda de 12 puntos de ancho que la fuente 3 de
        /// EPL, monoespaciada, así que las posiciones y el centrado que se calculan para EPL valen igual.
        /// </summary>
        public static string TextoMonoespaciado(int x, int y, string texto)
            => F("^FO{0},{1}^ADN,18,10^FH^FD{2}^FS", x, y, Escapar(texto));

        /// <summary>Texto escalable (^A0) del tamaño de la fuente EPL <paramref name="fuenteEpl"/>.</summary>
        public static string Texto(int x, int y, int fuenteEpl, string texto)
        {
            int[] medida = fuentesEpl.TryGetValue(fuenteEpl, out int[] m) ? m : fuentesEpl[3];
            return F("^FO{0},{1}^A0N,{2},{3}^FH^FD{4}^FS", x, y, medida[0], medida[1], Escapar(texto));
        }

        /// <summary>Un rectángulo (o una raya si uno de los lados es el grosor).</summary>
        public static string Caja(int x, int y, int ancho, int alto, int grosor)
            => F("^FO{0},{1}^GB{2},{3},{4}^FS", x, y, ancho, alto, grosor);

        /// <summary>
        /// Para ^FH^FD: los caracteres de control fuera; ^, ~, _ y todo lo que no es ASCII, en hexadecimal UTF-8 («_C3_91» = Ñ).
        /// </summary>
        public static string Escapar(string texto)
        {
            var resultado = new StringBuilder();
            foreach (char c in (texto ?? string.Empty).Where(c => !char.IsControl(c)))
            {
                if (c >= 32 && c < 127 && c != '^' && c != '~' && c != '_')
                {
                    _ = resultado.Append(c);
                    continue;
                }
                foreach (byte b in Encoding.UTF8.GetBytes(c.ToString()))
                {
                    _ = resultado.Append('_').Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }
            return resultado.ToString();
        }

        private static string F(string formato, params object[] valores)
            => string.Format(CultureInfo.InvariantCulture, formato, valores) + "\r\n";
    }

    /// <summary>
    /// NestoAPI#685: el ajuste de posición de cada impresora (NestoAPI#672) en ZPL. Como <see cref="DesplazadorEpl"/>: suma
    /// a las coordenadas de cada ^FO y ^FT en vez de mandar ^LH con el desplazamiento (no admite negativos y se queda en la
    /// impresora). Con 0 y 0 el trabajo sale idéntico.
    /// </summary>
    public static class DesplazadorZpl
    {
        private static readonly Regex origen = new Regex(@"\^(FO|FT)(-?\d+),(-?\d+)", RegexOptions.Compiled);

        public static string Desplazar(string zpl, int x, int y)
        {
            if (string.IsNullOrEmpty(zpl) || (x == 0 && y == 0))
            {
                return zpl;
            }
            return origen.Replace(zpl, m =>
            {
                int nuevaX = Math.Max(0, int.Parse(m.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) + x);
                int nuevaY = Math.Max(0, int.Parse(m.Groups[3].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) + y);
                return string.Format(CultureInfo.InvariantCulture, "^{0}{1},{2}", m.Groups[1].Value, nuevaX, nuevaY);
            });
        }
    }
}
