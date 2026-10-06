using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// Las etiquetas de hueco de la estantería (Carlos 05/10/26): 30×20 mm, el código de barras PPPFFFCCC que lee Ariadna y,
    /// debajo, «Pasillo / Fila / Columna» como las que ya están pegadas. En EPL2, el idioma de la impresora de etiquetas de
    /// producto (Nesto ya le manda EPL: la etiqueta de las máquinas de alquiler). A 203 ppp, 1 mm = 8 puntos.
    /// <para>Las posiciones son constantes para ajustarlas tras la etiqueta de prueba sin tocar nada más.</para>
    /// </summary>
    public static class GeneradorEtiquetasHueco
    {
        // Etiqueta: 30 × 20 mm, separación entre etiquetas de 3 mm
        public const int ANCHO = 240;
        public const int ALTO = 160;
        public const int SEPARACION = 24;

        // Code128 de 9 cifras: unos 101 módulos × 2 puntos ≈ 202 puntos (25 mm), centrado
        public const int CODIGO_X = 18;
        public const int CODIGO_Y = 8;
        public const int CODIGO_ESTRECHO = 2;
        public const int CODIGO_ALTO = 56;

        // Tres líneas de texto en la fuente 3 (12 × 20 puntos): 11 caracteres = 132 puntos, centradas
        public const int TEXTO_X = 54;
        public const int TEXTO_Y = 72;
        public const int TEXTO_INTERLINEA = 24;
        public const int TEXTO_FUENTE = 3;

        /// <summary>Un solo trabajo con una etiqueta por hueco (códigos de 9 cifras ya normalizados).</summary>
        public static string Epl(IEnumerable<string> huecos)
        {
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n"); // juego de caracteres de 8 bits, España (como la etiqueta de las máquinas)
            epl.Append("q").Append(ANCHO).Append("\r\n");
            epl.Append("Q").Append(ALTO).Append(",").Append(SEPARACION).Append("\r\n");
            foreach (string hueco in huecos ?? Enumerable.Empty<string>())
            {
                epl.Append("N\r\n");
                epl.Append(string.Format(CultureInfo.InvariantCulture, "B{0},{1},0,1,{2},{2},{3},N,\"{4}\"\r\n",
                    CODIGO_X, CODIGO_Y, CODIGO_ESTRECHO, CODIGO_ALTO, hueco));
                string[] lineas = { "Pasillo:" + hueco.Substring(0, 3), "Fila...:" + hueco.Substring(3, 3), "Columna:" + hueco.Substring(6, 3) };
                for (int i = 0; i < lineas.Length; i++)
                {
                    epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                        TEXTO_X, TEXTO_Y + i * TEXTO_INTERLINEA, TEXTO_FUENTE, lineas[i]));
                }
                epl.Append("P1\r\n");
            }
            return epl.ToString();
        }
    }

    /// <summary>Manda un trabajo tal cual (RAW) a una impresora de Windows. Detrás de una interfaz para las pruebas.</summary>
    public interface IImpresoraEtiquetas
    {
        /// <exception cref="ImpresionEtiquetasException">Si Windows no deja abrir la impresora o escribir en ella.</exception>
        void Imprimir(string impresora, string contenido, string nombreTrabajo);
    }

    public class ImpresionEtiquetasException : Exception
    {
        public ImpresionEtiquetasException(string mensaje) : base(mensaje) { }
    }

    /// <summary>
    /// La impresión la hace la API en el servidor (RDS2016, donde están compartidas las impresoras), para que Ariadna pueda
    /// imprimir desde la PDA. Mismo mecanismo que el RawPrinterHelper de Nesto (winspool), con el texto en ANSI 1252.
    /// <para>La primera prueba real dirá si la identidad del grupo de aplicaciones de IIS puede imprimir en esa cola; si no,
    /// el error sale con el código de Windows (5 = acceso denegado) y hay que darle permiso de imprimir en la impresora.</para>
    /// </summary>
    public class ImpresoraEtiquetasWindows : IImpresoraEtiquetas
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOCINFOW
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool OpenPrinter(string nombre, out IntPtr impresora, IntPtr opciones);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool ClosePrinter(IntPtr impresora);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool StartDocPrinter(IntPtr impresora, int nivel, ref DOCINFOW documento);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndDocPrinter(IntPtr impresora);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool StartPagePrinter(IntPtr impresora);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool EndPagePrinter(IntPtr impresora);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
        private static extern bool WritePrinter(IntPtr impresora, byte[] bytes, int cuantos, out int escritos);

        public void Imprimir(string impresora, string contenido, string nombreTrabajo)
        {
            byte[] bytes = Encoding.GetEncoding(1252).GetBytes(contenido ?? string.Empty);
            if (!OpenPrinter(impresora, out IntPtr manejador, IntPtr.Zero))
            {
                throw Fallo($"No se puede abrir la impresora {impresora}");
            }
            try
            {
                var documento = new DOCINFOW { pDocName = nombreTrabajo ?? "Etiquetas", pDataType = "RAW" };
                if (!StartDocPrinter(manejador, 1, ref documento))
                {
                    throw Fallo($"La impresora {impresora} no acepta el trabajo");
                }
                try
                {
                    if (!StartPagePrinter(manejador))
                    {
                        throw Fallo($"La impresora {impresora} no acepta el trabajo");
                    }
                    bool escrito = WritePrinter(manejador, bytes, bytes.Length, out int escritos);
                    _ = EndPagePrinter(manejador);
                    if (!escrito || escritos != bytes.Length)
                    {
                        throw Fallo($"No se ha podido mandar todo a la impresora {impresora}");
                    }
                }
                finally
                {
                    _ = EndDocPrinter(manejador);
                }
            }
            finally
            {
                _ = ClosePrinter(manejador);
            }
        }

        private static ImpresionEtiquetasException Fallo(string que)
        {
            int codigo = Marshal.GetLastWin32Error();
            return new ImpresionEtiquetasException($"{que} (error de Windows {codigo}).");
        }
    }

    public interface IRepositorioEtiquetasHueco
    {
        /// <summary>Huecos (PPPFFFCCC) del pasillo con algo ubicado (Ubicaciones estado 0) dentro de los rangos, de 3 cifras.</summary>
        Task<List<string>> LeerHuecosEnUso(string empresa, string almacen, string pasillo, string filaDesde, string filaHasta,
            string columnaDesde, string columnaHasta);
    }

    public class RepositorioEtiquetasHuecoSql : IRepositorioEtiquetasHueco, IDisposable
    {
        // Pasillo, fila y columna son char(3) con ceros a la izquierda: el BETWEEN de texto ordena igual que el número
        internal const string SQL_HUECOS_EN_USO = @"
SELECT RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna)
FROM Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.Estado = 0 AND u.Pasillo = @p2
      AND u.Fila BETWEEN @p3 AND @p4 AND u.Columna BETWEEN @p5 AND @p6
GROUP BY u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) > 0";

        private readonly NVEntities db;

        public RepositorioEtiquetasHuecoSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioEtiquetasHuecoSql ConContextoPropio() => new RepositorioEtiquetasHuecoSql(new NVEntities());

        public Task<List<string>> LeerHuecosEnUso(string empresa, string almacen, string pasillo, string filaDesde, string filaHasta,
            string columnaDesde, string columnaHasta)
        {
            // Tipados como las columnas (char): con nvarchar SQL Server no usaría el índice
            SqlParameter Char(string nombre, int largo, string valor) => new SqlParameter(nombre, System.Data.SqlDbType.Char, largo) { Value = valor };
            return db.Database.SqlQuery<string>(SQL_HUECOS_EN_USO,
                Char("@p0", 3, empresa), Char("@p1", 3, almacen), Char("@p2", 3, pasillo),
                Char("@p3", 3, filaDesde), Char("@p4", 3, filaHasta), Char("@p5", 3, columnaDesde), Char("@p6", 3, columnaHasta))
                .ToListAsync();
        }

        public void Dispose() => db?.Dispose();
    }

    public interface IServicioEtiquetasHueco
    {
        /// <exception cref="ArgumentException">Huecos que no valen o usuario sin impresora de etiquetas: el motivo es para el usuario.</exception>
        /// <exception cref="ImpresionEtiquetasException">Windows no ha dejado imprimir.</exception>
        Task<ResultadoEtiquetasHuecoDTO> Imprimir(string empresa, string almacen, EtiquetasHuecoDTO peticion, string usuario, bool ensayo);
    }

    /// <summary>
    /// El único sitio que imprime etiquetas de hueco: lo usan la ventana de Nesto y Ariadna (Ubicar). Calcula los huecos (una
    /// lista o un rango de un pasillo, si se pide solo los que tienen algo), busca la impresora de etiquetas del usuario y manda
    /// un solo trabajo. Con ensayo, dice qué imprimiría y dónde, sin imprimir.
    /// </summary>
    public class ServicioEtiquetasHueco : IServicioEtiquetasHueco
    {
        public const int MAXIMO_ETIQUETAS = 500;
        public const string PARAMETRO_IMPRESORA = "ImpresoraCodBarras";
        private const string USUARIO_POR_DEFECTO = "(defecto)";

        private readonly IRepositorioEtiquetasHueco repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetas impresora;

        public ServicioEtiquetasHueco() : this(RepositorioEtiquetasHuecoSql.ConContextoPropio(), new LectorParametrosUsuario(), new ImpresoraEtiquetasWindows())
        {
        }

        public ServicioEtiquetasHueco(IRepositorioEtiquetasHueco repositorio, ILectorParametrosUsuario parametros, IImpresoraEtiquetas impresora)
        {
            this.repositorio = repositorio;
            this.parametros = parametros;
            this.impresora = impresora;
        }

        public async Task<ResultadoEtiquetasHuecoDTO> Imprimir(string empresa, string almacen, EtiquetasHuecoDTO peticion, string usuario, bool ensayo)
        {
            List<string> huecos = peticion?.SoloEnUso == true && EsRango(peticion)
                ? await HuecosEnUsoDelRango(empresa, almacen, peticion).ConfigureAwait(false)
                : HuecosPedidos(peticion, out string motivo) ?? throw new ArgumentException(motivo);

            if (peticion.SoloEnUso && !EsRango(peticion))
            {
                var enUso = new HashSet<string>();
                foreach (string pasillo in huecos.Select(h => h.Substring(0, 3)).Distinct())
                {
                    enUso.UnionWith(await repositorio.LeerHuecosEnUso(empresa, almacen, pasillo, "000", "999", "000", "999").ConfigureAwait(false));
                }
                huecos = huecos.Where(enUso.Contains).ToList();
            }

            string destino = ImpresoraDe(empresa, usuario);
            var resultado = new ResultadoEtiquetasHuecoDTO { Impresora = destino, Huecos = huecos };
            if (huecos.Count == 0)
            {
                resultado.Mensaje = "No hay ningún hueco con algo ubicado en lo que has pedido: no se imprime nada.";
                return resultado;
            }
            if (ensayo)
            {
                resultado.Mensaje = $"Se imprimirían {Etiquetas(huecos.Count)} en {destino}.";
                return resultado;
            }
            impresora.Imprimir(destino, GeneradorEtiquetasHueco.Epl(huecos), "Etiquetas de hueco");
            resultado.Impresas = huecos.Count;
            resultado.Mensaje = $"{Etiquetas(huecos.Count)} mandadas a {destino}.";
            return resultado;
        }

        private static string Etiquetas(int n) => n == 1 ? "1 etiqueta" : $"{n} etiquetas";

        private string ImpresoraDe(string empresa, string usuario) => ImpresoraDelUsuario(parametros, empresa, usuario);

        /// <summary>
        /// La impresora de etiquetas del usuario (ParámetrosUsuario ImpresoraCodBarras, sin el dominio; si no tiene, la de
        /// «(defecto)»). Un solo sitio: lo usan las etiquetas de hueco y la etiqueta de agencia (NestoAPI#595).
        /// </summary>
        /// <exception cref="ArgumentException">El usuario no tiene impresora de etiquetas: el motivo es para el usuario.</exception>
        public static string ImpresoraDelUsuario(ILectorParametrosUsuario parametros, string empresa, string usuario)
        {
            string sinDominio = (usuario ?? string.Empty).Substring((usuario ?? string.Empty).IndexOf('\\') + 1).Trim();
            string destino = string.IsNullOrEmpty(sinDominio) ? null : parametros.LeerParametro(empresa, sinDominio, PARAMETRO_IMPRESORA);
            if (string.IsNullOrWhiteSpace(destino))
            {
                destino = parametros.LeerParametro(empresa, USUARIO_POR_DEFECTO, PARAMETRO_IMPRESORA);
            }
            if (string.IsNullOrWhiteSpace(destino))
            {
                throw new ArgumentException($"El usuario {sinDominio} no tiene impresora de etiquetas (parámetro {PARAMETRO_IMPRESORA}). Pídesela a informática.");
            }
            return destino.Trim();
        }

        private static bool EsRango(EtiquetasHuecoDTO peticion)
            => peticion != null && (peticion.Huecos == null || peticion.Huecos.All(string.IsNullOrWhiteSpace));

        private async Task<List<string>> HuecosEnUsoDelRango(string empresa, string almacen, EtiquetasHuecoDTO peticion)
        {
            if (!LeerRango(peticion, out string pasillo, out int filaDesde, out int filaHasta, out int columnaDesde, out int columnaHasta, out string motivo))
            {
                throw new ArgumentException(motivo);
            }
            List<string> huecos = (await repositorio.LeerHuecosEnUso(empresa, almacen, pasillo, Tres(filaDesde), Tres(filaHasta),
                Tres(columnaDesde), Tres(columnaHasta)).ConfigureAwait(false))
                .Select(h => h?.Trim()).Where(h => h != null && h.Length == 9).Distinct().ToList();
            if (huecos.Count > MAXIMO_ETIQUETAS)
            {
                throw new ArgumentException(Demasiadas(huecos.Count));
            }
            return Ordenar(huecos);
        }

        /// <summary>
        /// Los huecos pedidos, de 9 cifras, sin repetir y en el orden del recorrido (pasillo, columna, fila). Null y el motivo si
        /// algo no vale. Con lista, cada hueco como lo lee el lector (002004001) o como se enseña (002/004/001).
        /// </summary>
        internal static List<string> HuecosPedidos(EtiquetasHuecoDTO peticion, out string motivo)
        {
            motivo = null;
            if (peticion == null)
            {
                motivo = "No ha llegado qué etiquetas imprimir.";
                return null;
            }
            var huecos = new List<string>();
            if (!EsRango(peticion))
            {
                foreach (string hueco in peticion.Huecos.Where(h => !string.IsNullOrWhiteSpace(h)))
                {
                    if (!ServicioUbicacionesAlmacen.LeerCodigoDeHueco(hueco, out string pasillo, out string fila, out string columna))
                    {
                        motivo = $"«{hueco.Trim()}» no es un hueco: tiene que ser pasillo, fila y columna (002004001 o 002/004/001).";
                        return null;
                    }
                    huecos.Add(pasillo + fila + columna);
                }
            }
            else
            {
                if (!LeerRango(peticion, out string pasillo, out int filaDesde, out int filaHasta, out int columnaDesde, out int columnaHasta, out motivo))
                {
                    return null;
                }
                long cuantos = (long)(filaHasta - filaDesde + 1) * (columnaHasta - columnaDesde + 1);
                if (cuantos > MAXIMO_ETIQUETAS)
                {
                    motivo = Demasiadas(cuantos);
                    return null;
                }
                for (int columna = columnaDesde; columna <= columnaHasta; columna++)
                {
                    for (int fila = filaDesde; fila <= filaHasta; fila++)
                    {
                        huecos.Add(pasillo + Tres(fila) + Tres(columna));
                    }
                }
            }
            huecos = huecos.Distinct().ToList();
            if (huecos.Count > MAXIMO_ETIQUETAS)
            {
                motivo = Demasiadas(huecos.Count);
                return null;
            }
            return Ordenar(huecos);
        }

        private static string Demasiadas(long cuantas)
            => $"Son {cuantas} etiquetas y como mucho se imprimen {MAXIMO_ETIQUETAS} de una vez: pide menos filas o columnas.";

        private static List<string> Ordenar(IEnumerable<string> huecos)
            => huecos.OrderBy(h => h.Substring(0, 3), StringComparer.Ordinal)
                .ThenBy(h => h.Substring(6, 3), StringComparer.Ordinal)
                .ThenBy(h => h.Substring(3, 3), StringComparer.Ordinal)
                .ToList();

        private static string Tres(int n) => n.ToString("000", CultureInfo.InvariantCulture);

        private static bool LeerRango(EtiquetasHuecoDTO peticion, out string pasillo, out int filaDesde, out int filaHasta,
            out int columnaDesde, out int columnaHasta, out string motivo)
        {
            filaDesde = filaHasta = columnaDesde = columnaHasta = 0;
            pasillo = null;
            motivo = null;
            if (!Numero(peticion.Pasillo, out int numeroPasillo))
            {
                motivo = "Di qué etiquetas imprimir: unos huecos, o un pasillo con sus filas y columnas.";
                return false;
            }
            pasillo = Tres(numeroPasillo);
            if (!Numero(peticion.FilaDesde, out filaDesde) || !Numero(peticion.ColumnaDesde, out columnaDesde))
            {
                motivo = "Falta desde qué fila y desde qué columna (números de hasta 3 cifras).";
                return false;
            }
            filaHasta = filaDesde;
            columnaHasta = columnaDesde;
            if ((!string.IsNullOrWhiteSpace(peticion.FilaHasta) && !Numero(peticion.FilaHasta, out filaHasta))
                || (!string.IsNullOrWhiteSpace(peticion.ColumnaHasta) && !Numero(peticion.ColumnaHasta, out columnaHasta)))
            {
                motivo = "La fila y la columna «hasta» tienen que ser números de hasta 3 cifras.";
                return false;
            }
            if (filaHasta < filaDesde || columnaHasta < columnaDesde)
            {
                motivo = "El «hasta» no puede ser menor que el «desde».";
                return false;
            }
            return true;
        }

        private static bool Numero(string texto, out int numero)
        {
            numero = 0;
            string limpio = texto?.Trim();
            return !string.IsNullOrEmpty(limpio) && limpio.Length <= 3 && limpio.All(char.IsDigit)
                && int.TryParse(limpio, NumberStyles.None, CultureInfo.InvariantCulture, out numero);
        }
    }
}
