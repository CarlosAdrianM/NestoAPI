using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inmovilizados
{
    /// <summary>POST api/Inmovilizados/Etiquetas/Imprimir: qué imprimir.</summary>
    public class EtiquetasInmovilizadoDTO
    {
        /// <summary>Sin ella, la 1.</summary>
        public string Empresa { get; set; }
        /// <summary>De 1 a 50 números («1823»; también vale «I/1823»).</summary>
        public List<string> Numeros { get; set; } = new List<string>();
        /// <summary>De 1 a 10 por inmovilizado (0 o sin él, 1).</summary>
        public int Copias { get; set; } = 1;
        /// <summary>Sin ella, la de la delegación del inmovilizado (<see cref="ReglaImpresoraInmovilizado"/>).</summary>
        public string Impresora { get; set; }
    }

    /// <summary>Cómo ha ido cada inmovilizado pedido.</summary>
    public class EtiquetaInmovilizadoResultadoDTO
    {
        public string Numero { get; set; }
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Delegacion { get; set; }
        public string Impresora { get; set; }
        public bool Ok { get; set; }
        /// <summary>Por qué no se ha impreso (no existe, anulado, sin impresora, la impresora falla); null si Ok.</summary>
        public string Motivo { get; set; }
    }

    public class ResultadoEtiquetasInmovilizadoDTO
    {
        /// <summary>Etiquetas mandadas a imprimir (inmovilizados Ok × copias).</summary>
        public int Impresas { get; set; }
        public List<EtiquetaInmovilizadoResultadoDTO> Etiquetas { get; set; } = new List<EtiquetaInmovilizadoResultadoDTO>();
    }

    /// <summary>
    /// LA REGLA de la impresora de la etiqueta de un inmovilizado: la COMÚN de las etiquetas (<see cref="ReglaImpresoraEtiquetas"/>)
    /// con la DELEGACIÓN del inmovilizado como almacén: la que diga la petición; si no, ParametrosUsuario empresa 1 «(defecto)»
    /// <c>ImpresoraEtiquetasInmovilizado.{ALG|REI|ALC}</c> (la clave antigua, que gana) o <c>ImpresoraEtiquetas.{…}</c> (la general)
    /// y, sin fila, la de <see cref="POR_DEFECTO"/>; con otra delegación (VAR…), la ImpresoraCodBarras de quien imprime.
    /// </summary>
    public static class ReglaImpresoraInmovilizado
    {
        public const string PREFIJO_PARAMETRO = "ImpresoraEtiquetasInmovilizado.";

        public static IReadOnlyDictionary<string, string> POR_DEFECTO => ReglaImpresoraEtiquetas.POR_DEFECTO;

        /// <summary>La clave (antigua) del parámetro de esa delegación, o null si no es ALG, REI ni ALC.</summary>
        public static string ClaveParametro(string delegacion) => ReglaImpresoraEtiquetas.ClaveParametro(delegacion, PREFIJO_PARAMETRO);

        /// <param name="pedida">La impresora de la petición (manda si viene; se valida).</param>
        /// <param name="valorParametro">Lee el parámetro «(defecto)» de esa clave (null si no hay fila). Solo se llama con ALG, REI o ALC.</param>
        /// <param name="impresoraDelUsuario">La ImpresoraCodBarras de quien imprime; puede lanzar ArgumentException (no tiene).</param>
        public static string Elegir(string pedida, string delegacion, Func<string, string> valorParametro, Func<string> impresoraDelUsuario)
            => ReglaImpresoraEtiquetas.Elegir(pedida, delegacion, valorParametro, impresoraDelUsuario, PREFIJO_PARAMETRO).Impresora;
    }

    /// <summary>
    /// Etiquetas de inmovilizado (25/11/26): LA REGLA (pura) de la descripción en la etiqueta. Se parte en hasta <c>lineas</c> líneas de
    /// <c>caracteres</c> sin cortar palabras y el número de serie («n/s …», «s/n …», «nº serie …», sin distinguir mayúsculas: hasta el
    /// final) sale SIEMPRE entero: es lo que distingue una máquina de otra igual (hay varios Depiblend). Si no cabe todo, se recorta el
    /// NOMBRE (por palabras, con «…»); después, si hiciera falta, la referencia («ref.44276»); el número de serie, nunca.
    /// </summary>
    public static class ReglaDescripcionEtiquetaInmovilizado
    {
        public const string PUNTOS = "…";

        private static readonly System.Text.RegularExpressions.Regex serie = new System.Text.RegularExpressions.Regex(
            @"(?<![\p{L}\p{N}])(n\s*/\s*s|s\s*/\s*n|n[º°o]?\.?\s*(de\s+)?serie)(?![\p{L}\p{N}]).*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        private static readonly System.Text.RegularExpressions.Regex referencia = new System.Text.RegularExpressions.Regex(
            @"(?<![\p{L}\p{N}])ref\b\.?\s*\S+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static List<string> Lineas(string descripcion, int caracteres = GeneradorEtiquetasProducto.CARACTERES_PEQUENA, int lineas = 3)
        {
            string texto = Compactar(descripcion);
            if (texto.Length == 0)
            {
                return new List<string>();
            }
            List<string> entera = GeneradorEtiquetasProducto.Partir(texto, caracteres);
            if (entera.Count <= lineas)
            {
                return entera;
            }

            System.Text.RegularExpressions.Match deSerie = serie.Match(texto);
            int finNombre = deSerie.Success ? deSerie.Index : texto.Length;
            System.Text.RegularExpressions.Match deReferencia = referencia.Match(texto.Substring(0, finNombre));
            if (deReferencia.Success)
            {
                finNombre = deReferencia.Index;
            }
            string nombre = texto.Substring(0, finNombre).Trim();
            string textoReferencia = deReferencia.Success ? deReferencia.Value.Trim() : null;
            string textoSerie = deSerie.Success ? deSerie.Value.Trim() : null;
            string cola = Compactar(string.Join(" ", new[] { textoReferencia, textoSerie }.Where(t => !string.IsNullOrEmpty(t))));

            if (cola.Length == 0)
            {
                // Sin referencia ni serie: el principio y «…»
                return ConPuntos(entera.Take(lineas).ToList(), caracteres);
            }

            // Primero se quitan palabras del nombre (con «…»), la cola entera
            string[] palabras = nombre.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int n = palabras.Length - 1; n >= 0; n--)
            {
                string recortado = n == 0 ? PUNTOS : string.Join(" ", palabras.Take(n)) + PUNTOS;
                List<string> intento = GeneradorEtiquetasProducto.Partir(recortado + " " + cola, caracteres);
                if (intento.Count <= lineas)
                {
                    return intento;
                }
            }
            // Sin nombre no cabe: la referencia y la serie; si tampoco, la serie sola (y, si ni así, lo que quepa de ella)
            foreach (string resto in new[] { cola, textoSerie }.Where(t => !string.IsNullOrEmpty(t)))
            {
                List<string> intento = GeneradorEtiquetasProducto.Partir(resto, caracteres);
                if (intento.Count <= lineas)
                {
                    return intento;
                }
            }
            return GeneradorEtiquetasProducto.Partir(textoSerie ?? cola, caracteres).Take(lineas).ToList();
        }

        private static List<string> ConPuntos(List<string> lineas, int caracteres)
        {
            if (lineas.Count > 0)
            {
                string ultima = lineas[lineas.Count - 1];
                lineas[lineas.Count - 1] = (ultima.Length + PUNTOS.Length > caracteres ? ultima.Substring(0, caracteres - PUNTOS.Length).TrimEnd() : ultima) + PUNTOS;
            }
            return lineas;
        }

        private static string Compactar(string texto)
            => string.Join(" ", new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray())
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Lo que lleva la etiqueta de un inmovilizado.</summary>
    public class EtiquetaInmovilizado
    {
        public string Numero { get; set; }
        /// <summary>«I/1823»: en grande y en el Code128.</summary>
        public string Codigo { get; set; }
        public string Cabecera { get; set; }
        /// <summary>La descripción, en una o dos líneas.</summary>
        public List<string> Descripcion { get; set; } = new List<string>();
        public string Delegacion { get; set; }
    }

    /// <summary>
    /// Etiquetas de inmovilizado (Carlos 25/11/26): la MISMA 3×2 (30×20 mm, 240×160 puntos a 203 ppp) y el mismo camino que la de
    /// producto (<see cref="ImpresoraEtiquetasConAjuste"/>: idioma EPL o ZPL de cada impresora y su ajuste de posición). Arriba
    /// «INMOVILIZADO · NO ES STOCK»; «I/1823» en grande con la delegación a la derecha; la descripción en dos líneas; abajo un
    /// Code128 con el texto EXACTO «I/1823» (sin texto debajo: ya va en grande arriba). Constantes para ajustar tras la de prueba.
    /// </summary>
    public static class GeneradorEtiquetasInmovilizado
    {
        public const string CABECERA = "INMOVILIZADO · NO ES STOCK";

        public const int MARGEN_X = GeneradorEtiquetasProducto.MARGEN_X;
        public const int CABECERA_Y = 4;
        public const int FUENTE_PEQUENA = GeneradorEtiquetasProducto.FUENTE_PEQUENA;
        public const int CARACTERES_PEQUENA = GeneradorEtiquetasProducto.CARACTERES_PEQUENA;
        /// <summary>Ancho de un carácter de la fuente pequeña (EPL 1: 8 puntos), para poner la delegación a la derecha.</summary>
        public const int ANCHO_CARACTER_PEQUENA = 8;

        public const int CODIGO_Y = 18;
        public const int CODIGO_ALTO = GeneradorEtiquetasProducto.REFERENCIA_ALTO;
        public const int CODIGO_ANCHO = GeneradorEtiquetasProducto.REFERENCIA_ANCHO;
        public const int FUENTE_CODIGO_EPL = GeneradorEtiquetasProducto.FUENTE_REFERENCIA_EPL;
        /// <summary>La delegación, en pequeño a la altura de la base del código grande.</summary>
        public const int DELEGACION_Y = 28;

        /// <summary>Tres líneas en la fuente pequeña (12 de alto) cada 13 puntos: 46, 59 y 72 (acaba en el 84).</summary>
        public const int DESCRIPCION_Y = 46;
        public const int DESCRIPCION_INTERLINEA = 13;
        public const int DESCRIPCION_LINEAS = 3;

        /// <summary>4 de aire bajo la descripción. 56 puntos (7 mm) de barras: acaba en el 144 de 160 (por debajo de 48 no bajar).</summary>
        public const int BARRAS_Y = 88;
        public const int BARRAS_ALTO = 56;
        /// <summary>Módulo de 2 puntos (0,25 mm): «I/1823» son ~90 módulos = 180 puntos (22,5 mm).</summary>
        public const int BARRAS_ESTRECHO = 2;

        public static EtiquetaInmovilizado Componer(InmovilizadoDTO inmovilizado)
        {
            if (inmovilizado == null)
            {
                throw new ArgumentNullException(nameof(inmovilizado));
            }
            string numero = CodigoInmovilizado.Normalizar(inmovilizado.Numero);
            string codigo = CodigoInmovilizado.Componer(numero) ?? throw new ArgumentException("El inmovilizado no tiene número.", nameof(inmovilizado));
            return new EtiquetaInmovilizado
            {
                Numero = numero,
                Codigo = codigo,
                Cabecera = CABECERA,
                Descripcion = ReglaDescripcionEtiquetaInmovilizado.Lineas(inmovilizado.Descripcion, CARACTERES_PEQUENA, DESCRIPCION_LINEAS),
                Delegacion = Limpio(inmovilizado.Delegacion).ToUpperInvariant()
            };
        }

        /// <summary>
        /// Los módulos de un Code128 con elección automática de juego (lo que hacen el «1» de EPL y el modo A de ^BC): juego B y
        /// los tramos de 4 cifras o más en C (de dos en dos). Inicio, control y parada incluidos. Solo para centrarlo.
        /// </summary>
        internal static int ModulosCode128(string datos)
        {
            string texto = datos ?? string.Empty;
            int simbolos = 2; // inicio y control
            bool enC = false;
            int i = 0;
            while (i < texto.Length)
            {
                int cifras = 0;
                while (i + cifras < texto.Length && char.IsDigit(texto[i + cifras]))
                {
                    cifras++;
                }
                if (cifras >= 4)
                {
                    if (cifras % 2 == 1)
                    {
                        // la impar, suelta en B
                        if (enC)
                        {
                            simbolos++;
                            enC = false;
                        }
                        simbolos++;
                        i++;
                        cifras--;
                    }
                    if (!enC && i > 0)
                    {
                        simbolos++; // cambio a C (al principio lo hace el propio inicio)
                    }
                    enC = true;
                    simbolos += cifras / 2;
                    i += cifras;
                    continue;
                }
                if (enC)
                {
                    simbolos++; // vuelta a B
                    enC = false;
                }
                simbolos++;
                i++;
            }
            return simbolos * 11 + 13; // la parada lleva 13
        }

        /// <summary>El módulo: 2 puntos si cabe con aire (8 de margen a cada lado); si no, 1.</summary>
        public static int Estrecho(string codigo)
            => ModulosCode128(codigo) * BARRAS_ESTRECHO <= GeneradorEtiquetasHueco.ANCHO - 16 ? BARRAS_ESTRECHO : 1;

        /// <summary>La x del Code128, centrado.</summary>
        public static int BarrasX(string codigo)
            => Math.Max(0, (GeneradorEtiquetasHueco.ANCHO - ModulosCode128(codigo) * Estrecho(codigo)) / 2);

        private static int DelegacionX(string delegacion)
            => Math.Max(MARGEN_X, GeneradorEtiquetasHueco.ANCHO - MARGEN_X - (delegacion?.Length ?? 0) * ANCHO_CARACTER_PEQUENA);

        /// <summary>En EPL2: un trabajo con la cabecera una vez y cada etiqueta con sus copias (P).</summary>
        public static string Epl(IEnumerable<EtiquetaInmovilizado> etiquetas, int copias)
        {
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n");
            epl.Append("q").Append(GeneradorEtiquetasHueco.ANCHO).Append("\r\n");
            epl.Append("Q").Append(GeneradorEtiquetasHueco.ALTO).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            foreach (EtiquetaInmovilizado e in etiquetas ?? Enumerable.Empty<EtiquetaInmovilizado>())
            {
                epl.Append("N\r\n");
                epl.Append(F("A{0},{1},0,{2},1,1,N,\"{3}\"\r\n", MARGEN_X, CABECERA_Y, FUENTE_PEQUENA, TextoEpl(e.Cabecera)));
                epl.Append(F("A{0},{1},0,{2},1,1,N,\"{3}\"\r\n", MARGEN_X, CODIGO_Y, FUENTE_CODIGO_EPL, TextoEpl(e.Codigo)));
                if (!string.IsNullOrEmpty(e.Delegacion))
                {
                    epl.Append(F("A{0},{1},0,{2},1,1,N,\"{3}\"\r\n", DelegacionX(e.Delegacion), DELEGACION_Y, FUENTE_PEQUENA, TextoEpl(e.Delegacion)));
                }
                for (int i = 0; i < e.Descripcion.Count; i++)
                {
                    epl.Append(F("A{0},{1},0,{2},1,1,N,\"{3}\"\r\n", MARGEN_X, DESCRIPCION_Y + i * DESCRIPCION_INTERLINEA, FUENTE_PEQUENA, TextoEpl(e.Descripcion[i])));
                }
                int estrecho = Estrecho(e.Codigo);
                epl.Append(F("B{0},{1},0,1,{2},{2},{3},N,\"{4}\"\r\n", BarrasX(e.Codigo), BARRAS_Y, estrecho, BARRAS_ALTO, TextoEpl(e.Codigo)));
                epl.Append("P").Append(Math.Max(1, copias).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            return epl.ToString();
        }

        /// <summary>En ZPL: un ^XA…^XZ por etiqueta, con ^PQ de las copias. El Code128 es ^BC con el texto exacto «I/1823».</summary>
        public static string Zpl(IEnumerable<EtiquetaInmovilizado> etiquetas, int copias)
        {
            var zpl = new StringBuilder();
            foreach (EtiquetaInmovilizado e in etiquetas ?? Enumerable.Empty<EtiquetaInmovilizado>())
            {
                zpl.Append(ComandosZpl.Inicio());
                zpl.Append(ComandosZpl.Texto(MARGEN_X, CABECERA_Y, FUENTE_PEQUENA, e.Cabecera));
                zpl.Append(ComandosZpl.TextoEscalable(MARGEN_X, CODIGO_Y, CODIGO_ALTO, CODIGO_ANCHO, e.Codigo));
                if (!string.IsNullOrEmpty(e.Delegacion))
                {
                    zpl.Append(ComandosZpl.Texto(DelegacionX(e.Delegacion), DELEGACION_Y, FUENTE_PEQUENA, e.Delegacion));
                }
                for (int i = 0; i < e.Descripcion.Count; i++)
                {
                    zpl.Append(ComandosZpl.Texto(MARGEN_X, DESCRIPCION_Y + i * DESCRIPCION_INTERLINEA, FUENTE_PEQUENA, e.Descripcion[i]));
                }
                zpl.Append(ComandosZpl.Codigo128(BarrasX(e.Codigo), BARRAS_Y, Estrecho(e.Codigo), BARRAS_ALTO, e.Codigo));
                zpl.Append(ComandosZpl.Fin(copias));
            }
            return zpl.ToString();
        }

        public static TrabajoEtiquetas Trabajo(IReadOnlyCollection<EtiquetaInmovilizado> etiquetas, int copias)
            => new TrabajoEtiquetas(Epl(etiquetas, copias), Zpl(etiquetas, copias),
                $"Etiquetas de inmovilizado ({(etiquetas?.Count ?? 0).ToString(CultureInfo.InvariantCulture)})");

        private static string F(string formato, params object[] valores) => string.Format(CultureInfo.InvariantCulture, formato, valores);

        private static string Limpio(string texto)
            => string.Join(" ", new string((texto ?? string.Empty).Where(c => !char.IsControl(c)).ToArray())
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        /// <summary>Dentro de las comillas de EPL, la barra invertida y las comillas van escapadas.</summary>
        private static string TextoEpl(string texto) => Limpio(texto).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public interface IServicioEtiquetasInmovilizado
    {
        /// <returns>Null si no existe NINGUNO de los pedidos (404).</returns>
        /// <exception cref="ArgumentException">Sin números, demasiados o copias de más: el motivo es para el usuario.</exception>
        Task<ResultadoEtiquetasInmovilizadoDTO> Imprimir(EtiquetasInmovilizadoDTO peticion, string usuario);
    }

    /// <summary>
    /// Imprime las etiquetas de inmovilizado: lee los pedidos, decide la impresora de cada uno (<see cref="ReglaImpresoraInmovilizado"/>)
    /// y manda UN trabajo por impresora. Uno que no existe, anulado o sin impresora va en su fila con Ok false y el motivo; si una
    /// impresora falla, sus filas, sin parar a las demás.
    /// </summary>
    public class ServicioEtiquetasInmovilizado : IServicioEtiquetasInmovilizado
    {
        public const int MAXIMO_NUMEROS = 50;
        public const int MAXIMO_COPIAS = 10;
        private const string USUARIO_DEFECTO = "(defecto)";

        private readonly IRepositorioInmovilizados repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetasAlmacen impresora;

        public ServicioEtiquetasInmovilizado(IRepositorioInmovilizados repositorio, ILectorParametrosUsuario parametros, IImpresoraEtiquetasAlmacen impresora)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.parametros = parametros ?? throw new ArgumentNullException(nameof(parametros));
            this.impresora = impresora ?? throw new ArgumentNullException(nameof(impresora));
        }

        public async Task<ResultadoEtiquetasInmovilizadoDTO> Imprimir(EtiquetasInmovilizadoDTO peticion, string usuario)
        {
            List<string> pedidos = (peticion?.Numeros ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
            if (pedidos.Count == 0)
            {
                throw new ArgumentException("Diga de qué inmovilizados imprimir la etiqueta.");
            }
            if (pedidos.Count > MAXIMO_NUMEROS)
            {
                throw new ArgumentException($"Como mucho se imprimen las etiquetas de {MAXIMO_NUMEROS} inmovilizados de una vez.");
            }
            int copias = peticion.Copias <= 0 ? 1 : peticion.Copias;
            if (copias > MAXIMO_COPIAS)
            {
                throw new ArgumentException($"Como mucho se imprimen {MAXIMO_COPIAS} copias de cada etiqueta.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();

            // Cada pedido como número («1823» o «I/1823»), sin repetir y en el orden pedido
            var filas = new List<EtiquetaInmovilizadoResultadoDTO>();
            var numeros = new List<string>();
            foreach (string pedido in pedidos)
            {
                string numero = CodigoInmovilizado.Leer(pedido) ?? (pedido.All(char.IsDigit) ? CodigoInmovilizado.Normalizar(pedido) : null);
                if (numero == null)
                {
                    filas.Add(new EtiquetaInmovilizadoResultadoDTO { Numero = pedido, Ok = false, Motivo = $"«{pedido}» no es un número de inmovilizado." });
                    continue;
                }
                if (numeros.Contains(numero))
                {
                    continue;
                }
                numeros.Add(numero);
                filas.Add(new EtiquetaInmovilizadoResultadoDTO { Numero = numero, Codigo = CodigoInmovilizado.Componer(numero) });
            }

            Dictionary<string, InmovilizadoDTO> existentes = (await repositorio.Leer(empresa, numeros).ConfigureAwait(false) ?? new List<InmovilizadoDTO>())
                .Where(i => i?.Numero != null)
                .GroupBy(i => i.Numero, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            if (existentes.Count == 0)
            {
                return null;
            }

            var porImpresora = new Dictionary<string, List<(EtiquetaInmovilizadoResultadoDTO fila, EtiquetaInmovilizado etiqueta)>>(StringComparer.OrdinalIgnoreCase);
            var parametrosLeidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string impresoraUsuario = null;
            string LeerParametro(string clave)
            {
                if (!parametrosLeidos.TryGetValue(clave, out string valor))
                {
                    // Solo la fila «(defecto)»: nunca se crea ninguna al leer
                    valor = parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, clave);
                    parametrosLeidos[clave] = valor;
                }
                return valor;
            }
            string ImpresoraDelUsuario() => impresoraUsuario ?? (impresoraUsuario = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, empresa, usuario));

            foreach (EtiquetaInmovilizadoResultadoDTO fila in filas.Where(f => f.Codigo != null))
            {
                if (!existentes.TryGetValue(fila.Numero, out InmovilizadoDTO inmovilizado))
                {
                    fila.Motivo = $"No existe el inmovilizado {fila.Codigo}.";
                    continue;
                }
                fila.Descripcion = inmovilizado.Descripcion;
                fila.Delegacion = inmovilizado.Delegacion;
                if (inmovilizado.Estado < 0)
                {
                    fila.Motivo = $"El inmovilizado {fila.Codigo} está anulado: no se imprime su etiqueta.";
                    continue;
                }
                try
                {
                    fila.Impresora = ReglaImpresoraInmovilizado.Elegir(peticion.Impresora, inmovilizado.Delegacion, LeerParametro, ImpresoraDelUsuario);
                }
                catch (ArgumentException ex)
                {
                    fila.Motivo = ex.Message;
                    continue;
                }
                if (!porImpresora.TryGetValue(fila.Impresora, out var lista))
                {
                    lista = new List<(EtiquetaInmovilizadoResultadoDTO, EtiquetaInmovilizado)>();
                    porImpresora[fila.Impresora] = lista;
                }
                lista.Add((fila, GeneradorEtiquetasInmovilizado.Componer(inmovilizado)));
            }

            var resultado = new ResultadoEtiquetasInmovilizadoDTO { Etiquetas = filas };
            foreach (KeyValuePair<string, List<(EtiquetaInmovilizadoResultadoDTO fila, EtiquetaInmovilizado etiqueta)>> grupo in porImpresora)
            {
                try
                {
                    _ = impresora.Imprimir(grupo.Key, GeneradorEtiquetasInmovilizado.Trabajo(grupo.Value.Select(g => g.etiqueta).ToList(), copias));
                    foreach (var (fila, _) in grupo.Value)
                    {
                        fila.Ok = true;
                    }
                    resultado.Impresas += grupo.Value.Count * copias;
                }
                catch (ImpresionEtiquetasException ex)
                {
                    // Va en el Motivo de sus filas; las demás impresoras siguen
                    foreach (var (fila, _) in grupo.Value)
                    {
                        fila.Motivo = ex.Message;
                    }
                }
            }
            return resultado;
        }
    }
}
