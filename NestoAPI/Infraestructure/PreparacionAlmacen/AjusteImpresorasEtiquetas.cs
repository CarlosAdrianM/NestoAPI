using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#672: corre un trabajo EPL <paramref name="x"/> puntos a la derecha y <paramref name="y"/> hacia abajo, sumándolo
    /// a las coordenadas de cada comando que dibuja (A, B, b, GG, GW, LO, LE, LW, LS, X). Es el único sitio que aplica el ajuste
    /// de cada impresora: sirve para las etiquetas de producto, de hueco, de bulto y la de prueba.
    /// <para>Se suma a las coordenadas en vez de mandar el comando R de EPL2 porque R no admite valores negativos, se queda
    /// guardado en la impresora para los trabajos siguientes (quitar el ajuste no la devolvería a como estaba) y no sabemos cómo
    /// se lleva con el centrado del q en todas las impresoras (las «Generic / Text Only» y la ZD230 que emula EPL). Sumando, la
    /// impresora no guarda nada y con 0 y 0 el trabajo sale idéntico, byte a byte.</para>
    /// </summary>
    public static class DesplazadorEpl
    {
        // Los de dos letras antes que los de una; los que llevan dos puntos (inicio y fin) desplazan los campos 0, 1, 3 y 4
        private static readonly string[] comandosDosLetras = { "LO", "LE", "LW", "LS", "GW", "GG" };
        private static readonly string[] comandosUnaLetra = { "A", "B", "b", "X" };

        public static string Desplazar(string epl, int x, int y)
        {
            if (string.IsNullOrEmpty(epl) || (x == 0 && y == 0))
            {
                return epl;
            }
            var resultado = new StringBuilder(epl.Length + 64);
            int inicio = 0;
            while (inicio < epl.Length)
            {
                int salto = epl.IndexOf('\n', inicio);
                int fin = salto < 0 ? epl.Length : salto + 1;
                _ = resultado.Append(DesplazarLinea(epl.Substring(inicio, fin - inicio), x, y));
                inicio = fin;
            }
            return resultado.ToString();
        }

        internal static string DesplazarLinea(string linea, int x, int y)
        {
            // 19/10/26 (prueba en Reina): la anchura (q) crece lo mismo que se desplaza a la derecha. Si no, lo que pasa de la
            // anchura declarada (el marco de la etiqueta de prueba, X…,255 con q240) sale como un recuadro negro relleno.
            if (x > 0 && linea.Length > 1 && linea[0] == 'q' && char.IsDigit(linea[1]))
            {
                int fin = 1;
                while (fin < linea.Length && char.IsDigit(linea[fin]))
                {
                    fin++;
                }
                if (int.TryParse(linea.Substring(1, fin - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int ancho))
                {
                    return "q" + (ancho + x).ToString(CultureInfo.InvariantCulture) + linea.Substring(fin);
                }
            }
            string comando = Comando(linea);
            if (comando == null)
            {
                return linea;
            }
            bool dosPuntos = comando == "X" || comando == "LS";
            int cuantos = dosPuntos ? 5 : 2;
            var campos = new List<int>();
            int pos = comando.Length;
            for (int i = 0; i < cuantos; i++)
            {
                int empieza = pos;
                if (pos < linea.Length && linea[pos] == '-')
                {
                    pos++;
                }
                while (pos < linea.Length && char.IsDigit(linea[pos]))
                {
                    pos++;
                }
                if (!int.TryParse(linea.Substring(empieza, pos - empieza), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int valor))
                {
                    return linea; // no es lo que esperamos: mejor sin tocar
                }
                campos.Add(valor);
                if (i < cuantos - 1)
                {
                    if (pos >= linea.Length || linea[pos] != ',')
                    {
                        return linea;
                    }
                    pos++;
                }
            }
            campos[0] = Math.Max(0, campos[0] + x);
            campos[1] = Math.Max(0, campos[1] + y);
            if (dosPuntos)
            {
                campos[3] = Math.Max(0, campos[3] + x);
                campos[4] = Math.Max(0, campos[4] + y);
            }
            return comando + string.Join(",", campos.Select(c => c.ToString(CultureInfo.InvariantCulture))) + linea.Substring(pos);
        }

        private static string Comando(string linea)
        {
            bool EmpiezaNumero(int pos) => linea.Length > pos && (char.IsDigit(linea[pos]) || linea[pos] == '-');
            string dos = comandosDosLetras.FirstOrDefault(c => linea.StartsWith(c, StringComparison.Ordinal) && EmpiezaNumero(2));
            if (dos != null)
            {
                return dos;
            }
            return comandosUnaLetra.FirstOrDefault(c => linea.StartsWith(c, StringComparison.Ordinal) && EmpiezaNumero(1));
        }
    }

    /// <summary>
    /// NestoAPI#672: la etiqueta para ajustar una impresora. Un marco justo en el borde de la etiqueta de 30×20 mm y dos reglas
    /// con una marca cada milímetro (más larga cada 5 y cada 10, con el número), arriba para X y a la izquierda para Y. Si el
    /// marco sale cortado o separado del borde, las reglas dicen cuántos milímetros (8 puntos cada uno) hay que correrla.
    /// </summary>
    public static class GeneradorEtiquetaPrueba
    {
        public const int PUNTOS_POR_MM = 8;
        public const int GROSOR_MARCO = 2;
        public const int MARCA_MM = 6;
        public const int MARCA_5MM = 12;
        public const int MARCA_10MM = 18;

        /// <summary>El EPL sin desplazar; <paramref name="x"/> e <paramref name="y"/> solo se escriben en la etiqueta.</summary>
        public static string Epl(int x, int y)
        {
            int ancho = GeneradorEtiquetasHueco.ANCHO;
            int alto = GeneradorEtiquetasHueco.ALTO;
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n");
            epl.Append("q").Append(ancho).Append("\r\n");
            epl.Append("Q").Append(alto).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            epl.Append("N\r\n");
            epl.Append(F("X0,0,{0},{1},{2}", GROSOR_MARCO, ancho - 1, alto - 1));
            // Regla de X: arriba, hacia abajo
            for (int mm = 1; mm * PUNTOS_POR_MM < ancho; mm++)
            {
                epl.Append(F("LO{0},0,1,{1}", mm * PUNTOS_POR_MM, Largo(mm)));
                if (mm % 5 == 0)
                {
                    string numero = mm.ToString(CultureInfo.InvariantCulture);
                    epl.Append(F("A{0},{1},0,1,1,1,N,\"{2}\"", (mm * PUNTOS_POR_MM) - (numero.Length * 4), MARCA_10MM + 4, numero));
                }
            }
            // Regla de Y: a la izquierda, hacia la derecha
            for (int mm = 1; mm * PUNTOS_POR_MM < alto; mm++)
            {
                epl.Append(F("LO0,{0},{1},1", mm * PUNTOS_POR_MM, Largo(mm)));
                if (mm % 5 == 0)
                {
                    epl.Append(F("A{0},{1},0,1,1,1,N,\"{2}\"", MARCA_10MM + 4, (mm * PUNTOS_POR_MM) - 6, mm));
                }
            }
            epl.Append(F("A{0},{1},0,2,1,1,N,\"PRUEBA 30x20 mm\"", 56, 56));
            epl.Append(F("A{0},{1},0,2,1,1,N,\"X={2} Y={3} puntos\"", 56, 80, x, y));
            epl.Append(F("A{0},{1},0,1,1,1,N,\"8 puntos = 1 mm\"", 56, 104));
            epl.Append("P1\r\n");
            return epl.ToString();
        }

        /// <summary>NestoAPI#685: la misma etiqueta de prueba en ZPL (sin desplazar), para las Zebra con driver ZPL.</summary>
        public static string Zpl(int x, int y)
        {
            int ancho = GeneradorEtiquetasHueco.ANCHO;
            int alto = GeneradorEtiquetasHueco.ALTO;
            var zpl = new StringBuilder();
            zpl.Append(ComandosZpl.Inicio());
            zpl.Append(ComandosZpl.Caja(0, 0, ancho, alto, GROSOR_MARCO));
            for (int mm = 1; mm * PUNTOS_POR_MM < ancho; mm++)
            {
                zpl.Append(ComandosZpl.Caja(mm * PUNTOS_POR_MM, 0, 1, Largo(mm), 1));
                if (mm % 5 == 0)
                {
                    string numero = mm.ToString(CultureInfo.InvariantCulture);
                    zpl.Append(ComandosZpl.Texto((mm * PUNTOS_POR_MM) - (numero.Length * 4), MARCA_10MM + 4, 1, numero));
                }
            }
            for (int mm = 1; mm * PUNTOS_POR_MM < alto; mm++)
            {
                zpl.Append(ComandosZpl.Caja(0, mm * PUNTOS_POR_MM, Largo(mm), 1, 1));
                if (mm % 5 == 0)
                {
                    zpl.Append(ComandosZpl.Texto(MARCA_10MM + 4, (mm * PUNTOS_POR_MM) - 6, 1, mm.ToString(CultureInfo.InvariantCulture)));
                }
            }
            zpl.Append(ComandosZpl.Texto(56, 56, 2, "PRUEBA 30x20 mm ZPL"));
            zpl.Append(ComandosZpl.Texto(56, 80, 2, string.Format(CultureInfo.InvariantCulture, "X={0} Y={1} puntos", x, y)));
            zpl.Append(ComandosZpl.Texto(56, 104, 1, "8 puntos = 1 mm"));
            zpl.Append(ComandosZpl.Fin());
            return zpl.ToString();
        }

        private static int Largo(int mm) => mm % 10 == 0 ? MARCA_10MM : mm % 5 == 0 ? MARCA_5MM : MARCA_MM;

        private static string F(string formato, params object[] valores)
            => string.Format(CultureInfo.InvariantCulture, formato, valores) + "\r\n";
    }

    public interface IRepositorioAjustesImpresoras
    {
        /// <summary>El ajuste guardado de la impresora (ya normalizada). Null si no tiene o si la tabla no existe todavía.</summary>
        AjusteImpresoraEtiquetasDTO Leer(string impresora);
        /// <summary>Todos los ajustes guardados. Vacía si la tabla no existe todavía.</summary>
        List<AjusteImpresoraEtiquetasDTO> Listar();
        /// <summary>Crea o cambia el ajuste. False, sin hacer nada, si la tabla no existe todavía.</summary>
        bool Guardar(string impresora, int x, int y, string usuario);
    }

    /// <summary>
    /// NestoAPI#672: la tabla ImpresorasEtiquetasAjuste, con SQL a mano (no está en el EDMX). Sin la tabla (script sin lanzar),
    /// no hay ajuste para nadie y todo se imprime como hasta ahora.
    /// </summary>
    public class RepositorioAjustesImpresorasSql : IRepositorioAjustesImpresoras
    {
        internal const string SQL_EXISTE_TABLA = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ImpresorasEtiquetasAjuste', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_COLUMNAS = @"RTRIM(Impresora) AS Impresora, DesplazamientoX, DesplazamientoY, RTRIM(Usuario) AS Usuario, FechaModificacion";

        internal const string SQL_LEER = @"
SELECT " + SQL_COLUMNAS + @" FROM dbo.ImpresorasEtiquetasAjuste WHERE Impresora = @p0";

        internal const string SQL_LISTAR = @"
SELECT " + SQL_COLUMNAS + @" FROM dbo.ImpresorasEtiquetasAjuste ORDER BY Impresora";

        internal const string SQL_GUARDAR = @"
UPDATE dbo.ImpresorasEtiquetasAjuste SET DesplazamientoX = @p1, DesplazamientoY = @p2, Usuario = @p3, FechaModificacion = GETDATE()
WHERE Impresora = @p0;
IF @@ROWCOUNT = 0
    INSERT INTO dbo.ImpresorasEtiquetasAjuste (Impresora, DesplazamientoX, DesplazamientoY, Usuario, FechaModificacion)
    VALUES (@p0, @p1, @p2, @p3, GETDATE());";

        public AjusteImpresoraEtiquetasDTO Leer(string impresora)
        {
            using (var db = new NVEntities())
            {
                return !ExisteTabla(db) ? null
                    : db.Database.SqlQuery<AjusteImpresoraEtiquetasDTO>(SQL_LEER, Varchar("@p0", impresora, 100)).FirstOrDefault();
            }
        }

        public List<AjusteImpresoraEtiquetasDTO> Listar()
        {
            using (var db = new NVEntities())
            {
                return !ExisteTabla(db) ? new List<AjusteImpresoraEtiquetasDTO>()
                    : db.Database.SqlQuery<AjusteImpresoraEtiquetasDTO>(SQL_LISTAR).ToList();
            }
        }

        public bool Guardar(string impresora, int x, int y, string usuario)
        {
            using (var db = new NVEntities())
            {
                if (!ExisteTabla(db))
                {
                    return false;
                }
                _ = db.Database.ExecuteSqlCommand(SQL_GUARDAR, Varchar("@p0", impresora, 100),
                    new SqlParameter("@p1", SqlDbType.Int) { Value = x }, new SqlParameter("@p2", SqlDbType.Int) { Value = y },
                    Varchar("@p3", ReposicionesBultosSql.Recortar(usuario, 50), 50));
                return true;
            }
        }

        private static bool ExisteTabla(NVEntities db) => db.Database.SqlQuery<bool>(SQL_EXISTE_TABLA).First();

        private static SqlParameter Varchar(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.VarChar, largo) { Value = (object)valor ?? DBNull.Value };
    }

    /// <summary>
    /// NestoAPI#672: la impresora de etiquetas con el ajuste de cada impresora. Envuelve a la que imprime de verdad: lee el
    /// ajuste de la impresora a la que va el trabajo y lo aplica con <see cref="DesplazadorEpl"/>. Sin ajuste (o si no se puede
    /// leer), manda el trabajo tal cual. La usan las etiquetas de producto, de hueco y de bulto; la de agencia no (no es nuestra).
    /// <para>NestoAPI#685: es también el único sitio que elige el idioma (EPL o ZPL) de cada impresora, con
    /// <see cref="ILenguajeImpresoras"/>, y aplica el ajuste en ese idioma (<see cref="DesplazadorEpl"/> o
    /// <see cref="DesplazadorZpl"/>).</para>
    /// </summary>
    public class ImpresoraEtiquetasConAjuste : IImpresoraEtiquetasAlmacen
    {
        private readonly IImpresoraEtiquetas impresora;
        private readonly IRepositorioAjustesImpresoras ajustes;
        private readonly ILenguajeImpresoras lenguajes;

        public ImpresoraEtiquetasConAjuste(IImpresoraEtiquetas impresora, IRepositorioAjustesImpresoras ajustes, ILenguajeImpresoras lenguajes)
        {
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.ajustes = ajustes ?? throw new ArgumentNullException(nameof(ajustes));
            this.lenguajes = lenguajes ?? throw new ArgumentNullException(nameof(lenguajes));
        }

        /// <summary>La de producción: imprime por Windows, en el idioma del driver de cada cola y con el ajuste de la tabla.</summary>
        public static ImpresoraEtiquetasConAjuste EnWindows()
            => new ImpresoraEtiquetasConAjuste(new ImpresoraEtiquetasWindows(), new RepositorioAjustesImpresorasSql(), LenguajeImpresorasWindows.Compartido);

        public LenguajeEtiquetas Imprimir(string impresora, TrabajoEtiquetas trabajo)
        {
            if (trabajo == null)
            {
                throw new ArgumentNullException(nameof(trabajo));
            }
            LenguajeEtiquetas lenguaje = lenguajes.De(impresora);
            string contenido = trabajo.En(lenguaje);
            AjusteImpresoraEtiquetasDTO ajuste = null;
            string clave = ServicioAjustesImpresoras.Normalizar(impresora);
            if (clave != null)
            {
                try
                {
                    ajuste = ajustes.Leer(clave);
                }
                catch (Exception ex)
                {
                    // Un fallo leyendo el ajuste no puede dejar al almacén sin etiquetas: sale como hasta ahora
                    ElmahHelper.Log(ex);
                }
            }
            string desplazado = ajuste == null ? contenido : Desplazar(lenguaje, contenido, ajuste.DesplazamientoX, ajuste.DesplazamientoY);
            this.impresora.Imprimir(impresora, desplazado, trabajo.Nombre);
            return lenguaje;
        }

        /// <summary>NestoAPI#685: el ajuste de posición en el idioma del trabajo.</summary>
        public static string Desplazar(LenguajeEtiquetas lenguaje, string contenido, int x, int y)
            => lenguaje == LenguajeEtiquetas.Zpl ? DesplazadorZpl.Desplazar(contenido, x, y) : DesplazadorEpl.Desplazar(contenido, x, y);
    }

    public interface IServicioAjustesImpresoras
    {
        List<AjusteImpresoraEtiquetasDTO> Listar();
        /// <summary>El ajuste de la impresora; 0 y 0 (sin usuario ni fecha) si no tiene.</summary>
        /// <exception cref="ArgumentException">Sin impresora: el motivo es para el usuario.</exception>
        AjusteImpresoraEtiquetasDTO Leer(string impresora);
        /// <exception cref="ArgumentException">Impresora o valores que no valen: el motivo es para el usuario.</exception>
        /// <exception cref="InvalidOperationException">La tabla aún no existe.</exception>
        AjusteImpresoraEtiquetasDTO Guardar(AjusteImpresoraEtiquetasDTO ajuste, string usuario);
        /// <summary>
        /// La etiqueta de prueba en la impresora pedida (o, sin ella, en la del usuario), con el ajuste guardado o, si se pasan,
        /// con <paramref name="x"/> e <paramref name="y"/> (para probar un valor antes de guardarlo).
        /// </summary>
        /// <exception cref="ArgumentException">Valores que no valen o usuario sin impresora: el motivo es para el usuario.</exception>
        /// <exception cref="ImpresionEtiquetasException">Windows no ha dejado imprimir.</exception>
        ResultadoEtiquetaPruebaDTO ImprimirPrueba(string empresa, string impresora, int? x, int? y, string usuario);
    }

    /// <summary>NestoAPI#672: ver, guardar y probar el ajuste de cada impresora de etiquetas.</summary>
    public class ServicioAjustesImpresoras : IServicioAjustesImpresoras
    {
        public const int LARGO_IMPRESORA = 100;

        private readonly IRepositorioAjustesImpresoras repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetas impresora;
        private readonly ILenguajeImpresoras lenguajes;

        /// <param name="impresora">La que imprime tal cual (sin ajuste): la prueba ya lleva aplicado el que toca.</param>
        /// <param name="lenguajes">NestoAPI#685: el idioma de cada impresora (el mismo que usan las demás etiquetas).</param>
        public ServicioAjustesImpresoras(IRepositorioAjustesImpresoras repositorio, ILectorParametrosUsuario parametros, IImpresoraEtiquetas impresora,
            ILenguajeImpresoras lenguajes)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
            this.lenguajes = lenguajes ?? throw new ArgumentNullException(nameof(lenguajes));
        }

        /// <summary>La clave de la impresora en la tabla: sin espacios alrededor y en mayúsculas (Windows no las distingue).</summary>
        public static string Normalizar(string impresora)
        {
            string limpia = impresora?.Trim();
            return string.IsNullOrEmpty(limpia) ? null : limpia.ToUpperInvariant();
        }

        public List<AjusteImpresoraEtiquetasDTO> Listar() => repositorio.Listar() ?? new List<AjusteImpresoraEtiquetasDTO>();

        public AjusteImpresoraEtiquetasDTO Leer(string impresora)
        {
            string clave = Normalizar(impresora) ?? throw new ArgumentException("Falta la impresora.");
            return repositorio.Leer(clave) ?? new AjusteImpresoraEtiquetasDTO { Impresora = clave };
        }

        public AjusteImpresoraEtiquetasDTO Guardar(AjusteImpresoraEtiquetasDTO ajuste, string usuario)
        {
            string clave = Normalizar(ajuste?.Impresora) ?? throw new ArgumentException("Falta la impresora.");
            if (clave.Length > LARGO_IMPRESORA)
            {
                throw new ArgumentException($"La ruta de la impresora no puede pasar de {LARGO_IMPRESORA} caracteres.");
            }
            ComprobarValores(ajuste.DesplazamientoX, ajuste.DesplazamientoY);
            if (!repositorio.Guardar(clave, ajuste.DesplazamientoX, ajuste.DesplazamientoY, usuario))
            {
                throw new InvalidOperationException("Falta la tabla ImpresorasEtiquetasAjuste (script de NestoAPI#672 sin lanzar).");
            }
            return repositorio.Leer(clave) ?? new AjusteImpresoraEtiquetasDTO
            {
                Impresora = clave,
                DesplazamientoX = ajuste.DesplazamientoX,
                DesplazamientoY = ajuste.DesplazamientoY,
                Usuario = usuario
            };
        }

        public ResultadoEtiquetaPruebaDTO ImprimirPrueba(string empresa, string impresora, int? x, int? y, string usuario)
        {
            string destino = string.IsNullOrWhiteSpace(impresora)
                ? ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, empresa, usuario)
                : impresora.Trim();
            AjusteImpresoraEtiquetasDTO guardado = x.HasValue && y.HasValue ? null : repositorio.Leer(Normalizar(destino));
            int desplazamientoX = x ?? guardado?.DesplazamientoX ?? 0;
            int desplazamientoY = y ?? guardado?.DesplazamientoY ?? 0;
            ComprobarValores(desplazamientoX, desplazamientoY);

            LenguajeEtiquetas lenguaje = lenguajes.De(destino);
            string sinDesplazar = lenguaje == LenguajeEtiquetas.Zpl
                ? GeneradorEtiquetaPrueba.Zpl(desplazamientoX, desplazamientoY)
                : GeneradorEtiquetaPrueba.Epl(desplazamientoX, desplazamientoY);
            string trabajo = ImpresoraEtiquetasConAjuste.Desplazar(lenguaje, sinDesplazar, desplazamientoX, desplazamientoY);
            this.impresora.Imprimir(destino, trabajo, "Etiqueta de prueba");
            return new ResultadoEtiquetaPruebaDTO
            {
                Impresora = destino,
                DesplazamientoX = desplazamientoX,
                DesplazamientoY = desplazamientoY,
                Mensaje = $"Etiqueta de prueba mandada a {destino} en {(lenguaje == LenguajeEtiquetas.Zpl ? "ZPL" : "EPL")} con X={desplazamientoX} " +
                    $"e Y={desplazamientoY} puntos (8 puntos = 1 mm)."
            };
        }

        private static void ComprobarValores(int x, int y)
        {
            if (Math.Abs(x) > GeneradorEtiquetasHueco.ANCHO || Math.Abs(y) > GeneradorEtiquetasHueco.ALTO)
            {
                throw new ArgumentException($"El desplazamiento no puede pasar de {GeneradorEtiquetasHueco.ANCHO} puntos en X ni de " +
                    $"{GeneradorEtiquetasHueco.ALTO} en Y (el tamaño de la etiqueta); 8 puntos son 1 mm.");
            }
        }
    }
}
