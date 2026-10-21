using System;

namespace NestoAPI.Infraestructure.Stocks.Tendencias
{
    public static class FasesProducto
    {
        public const string NUEVO = "A";
        public const string MADURO = "B";
        public const string INTERMITENTE = "C";

        public static string Nombre(string fase)
        {
            switch (fase)
            {
                case NUEVO: return "Nuevo";
                case MADURO: return "Maduro";
                case INTERMITENTE: return "Intermitente";
                default: return fase;
            }
        }
    }

    public static class TendenciasProducto
    {
        public const string SUBE = "Sube";
        public const string ESTABLE = "Estable";
        /// <summary>Baja en 12 meses pero el contexto de 5 años no lo confirma (caso 17404): no es declive.</summary>
        public const string BAJON_PUNTUAL = "Bajón puntual";
        public const string DECLIVE = "Declive";
        public const string SIN_DATOS = "Sin datos";
    }

    public static class AccionesPropuesta
    {
        public const string AJUSTAR = "Ajustar máx/mín";
        public const string MANTENER = "Mantener";
        /// <summary>Pasar a estado 1 (sobre pedido): sin máximos ni mínimos.</summary>
        public const string ESTADO_1 = "Pasar a estado 1";
        /// <summary>Hay un pedido de compra abierto con el producto y sobra stock.</summary>
        public const string PARAR_COMPRAS = "Parar compras";
        /// <summary>Outlet (#467) o Ganavisiones.</summary>
        public const string LIQUIDAR = "Liquidar";
        public const string NADA = "Nada";

        // NestoAPI#655: las de la fórmula P (fabricación propia). Nunca Outlet ni liquidar: son los productos que hay que potenciar.
        /// <summary>Propios: el máximo y el mínimo cambian (lo que se fabrica en el siguiente lote), o el lote solo ya pasa de la vida útil.</summary>
        public const string AJUSTAR_LOTE = "Ajustar el siguiente lote";
        /// <summary>Propios: sobra stock sobre el máximo, pero se vende antes de caducar.</summary>
        public const string POTENCIAR = "Potenciar ventas";
        /// <summary>Propios: hay más stock que meses de vida útil de venta; lo que pase, caducará.</summary>
        public const string CADUCA = "Caduca antes de venderse";

        /// <summary>Las que «Aplicar» escribe como un ajuste de máximo y mínimo en ControlesStock.</summary>
        public static bool EsAjusteDeMaximos(string accion) => accion == AJUSTAR || accion == AJUSTAR_LOTE || accion == POTENCIAR || accion == CADUCA;
    }

    /// <summary>NestoAPI#655: la fórmula P (fabricación propia) se reconoce por el prefijo de <see cref="ResultadoTendencia.Formula"/>.</summary>
    public static class FormulasPropuesta
    {
        public const string PREFIJO_PROPIOS = "P:";

        public static bool EsFabricacionPropia(string formula) => formula != null && formula.StartsWith(PREFIJO_PROPIOS, StringComparison.Ordinal);
    }

    /// <summary>
    /// NestoAPI#630: indicadores, propuesta y explicación de un producto para un mes. Es lo que se guarda en PropuestasStock y lo
    /// que sale en el Excel. Las entradas van aquí también (trazabilidad obligatoria de la épica).
    /// </summary>
    public class ResultadoTendencia
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string Grupo { get; set; }
        public string Proveedor { get; set; }
        public short? EstadoProducto { get; set; }
        public string Almacen { get; set; } = "ALG";

        // Fase
        public string Fase { get; set; }
        public int? MesesDesdePrimeraVenta { get; set; }

        // Entradas
        public int Stock { get; set; }
        public decimal PrecioMedio { get; set; }
        public int Ventas12Meses { get; set; }
        public decimal VentaMedia6Meses { get; set; }
        public decimal VentaMediaPonderada { get; set; }
        /// <summary>La venta mensual con la que se calcula (media de 6 en B, velocidad en A, media de 12 en C).</summary>
        public decimal VentaMensualReferencia { get; set; }
        public decimal FactorEstacional { get; set; } = 1m;
        public bool ConEstacionalidad { get; set; }
        public int DiasEnServir { get; set; }
        public bool DiasEnServirSupuestos { get; set; }
        public int DiasSeguridad { get; set; }
        public int Multiplos { get; set; } = 1;
        /// <summary>
        /// NestoAPI#630: cuántos meses de venta (de referencia) es un múltiplo de compra; null sin múltiplo o sin venta. Solo
        /// lectura (EF, en SqlQuery, no busca columna para las propiedades sin set).
        /// </summary>
        public decimal? MesesVentaMultiplo => Multiplos > 1 && VentaMensualReferencia > 0 ? Math.Round(Multiplos / VentaMensualReferencia, 1) : (decimal?)null;
        public int CoberturaObjetivoDias { get; set; }
        public int? StockMaximoActual { get; set; }
        public int? StockMinimoActual { get; set; }
        public string Formula { get; set; }
        /// <summary>
        /// NestoAPI#655: de una familia PRP (fórmula P). Solo lectura, sacado de <see cref="Formula"/>: PropuestasStock no tiene
        /// columna para esto (y EF, en SqlQuery, no busca columna para las propiedades sin set).
        /// </summary>
        public bool FabricacionPropia => FormulasPropuesta.EsFabricacionPropia(Formula);

        // Indicadores
        /// <summary>Stock ÷ venta media de 6 meses. Null si no se ha vendido nada en 6 meses.</summary>
        public decimal? CoberturaMeses { get; set; }
        public int CoberturaAlcanzableDias { get; set; }
        public decimal? PendienteRelativa { get; set; }
        public decimal? RatioTrimestre { get; set; }
        public int? AnioPico { get; set; }
        public decimal? PorcentajeDelPico { get; set; }
        public int? AniosDesdePico { get; set; }
        public string Tendencia { get; set; }
        public decimal? Velocidad { get; set; }
        public decimal? TasaRecompra { get; set; }
        /// <summary>NestoAPI#656: pendiente vencido (−1 con fecha de entrega antes del fin del mes de referencia).</summary>
        public int DemandaNoServida { get; set; }
        /// <summary>NestoAPI#656: unidades servidas con más de <see cref="ParametrosTendencias.DiasRetraso"/> días de retraso en 12 meses.</summary>
        public int ServidoConRetraso { get; set; }
        public int PedidoCompraAbierto { get; set; }
        public int ExcesoUnidades { get; set; }
        public decimal ExcesoValorado { get; set; }

        // Propuesta
        public string Accion { get; set; }
        public int? StockMaximoPropuesto { get; set; }
        public int? StockMinimoPropuesto { get; set; }
        public string Explicacion { get; set; }

        /// <summary>Series y contexto en JSON (columna Entradas de la tabla).</summary>
        public string EntradasJson { get; set; }

        // NestoAPI#628: el tipo de exclusiva viaja en EntradasJson («tipoExclusiva»); NestoAPI#632: y cómo salen el máximo y el
        // mínimo («maximos»). Se lee una vez y se guarda en estos campos (campos y no propiedades: EF, en SqlQuery, no busca
        // columna para ellos ni para las propiedades sin set).
        private string entradasLeidas;
        private Newtonsoft.Json.Linq.JObject entradasParseadas;

        private Newtonsoft.Json.Linq.JObject Entradas
        {
            get
            {
                if (!ReferenceEquals(entradasLeidas, EntradasJson))
                {
                    entradasParseadas = Parsear(EntradasJson);
                    entradasLeidas = EntradasJson;
                }
                return entradasParseadas;
            }
        }

        /// <summary>
        /// NestoAPI#628: Familias.TipoExclusiva de la familia (NAC, MAD, NIG, EXT, PRP…), sacado de <see cref="EntradasJson"/>;
        /// null si no está. Solo lectura (PropuestasStock no tiene columna para esto).
        /// </summary>
        public string TipoExclusiva
        {
            get
            {
                string tipo = Leer<string>(Hijo(Entradas, "tipoExclusiva"));
                return string.IsNullOrWhiteSpace(tipo) ? null : tipo.Trim();
            }
        }

        /// <summary>
        /// NestoAPI#632: la venta mensual de la que salen el máximo y el mínimo (antes del factor estacional), sacada de
        /// <see cref="EntradasJson"/> («maximos.ventaBase»). Null si no hay (salidas tempranas, propios o propuestas antiguas).
        /// </summary>
        public decimal? VentaBaseMaximos => Leer<decimal?>(Hijo(Hijo(Entradas, "maximos"), "ventaBase"));

        /// <summary>
        /// NestoAPI#632: de dónde sale <see cref="VentaBaseMaximos"/> («ponderada sin estacionalidad», «6 meses sin estacionalidad
        /// (a la baja); declive: no sube», «velocidad desde el lanzamiento (nuevo)»…). Null si no hay.
        /// </summary>
        public string OrigenVentaBaseMaximos => Leer<string>(Hijo(Hijo(Entradas, "maximos"), "origenVentaBase"));

        /// <summary>
        /// NestoAPI#628: la agrupación por tipo de exclusiva (<see cref="IndicadoresStock.AgrupacionesStock"/>): los propios (fórmula
        /// P) en PRP sea cual sea su tipo; el resto, por su tipo de exclusiva (sin tipo, SIN). Solo lectura.
        /// </summary>
        public string Agrupacion => FabricacionPropia
            ? IndicadoresStock.AgrupacionesStock.PROPIOS
            : IndicadoresStock.AgrupacionesStock.Codigo(TipoExclusiva);

        private static Newtonsoft.Json.Linq.JObject Parsear(string entradas)
        {
            if (string.IsNullOrWhiteSpace(entradas))
            {
                return null;
            }
            try
            {
                return Newtonsoft.Json.Linq.JObject.Parse(entradas);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        private static Newtonsoft.Json.Linq.JToken Hijo(Newtonsoft.Json.Linq.JToken padre, string clave)
            => padre is Newtonsoft.Json.Linq.JObject objeto ? objeto[clave] : null;

        private static T Leer<T>(Newtonsoft.Json.Linq.JToken token)
        {
            if (token == null || token.Type == Newtonsoft.Json.Linq.JTokenType.Null)
            {
                return default(T);
            }
            try
            {
                return token.ToObject<T>();
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return default(T);
            }
            catch (InvalidCastException)
            {
                return default(T);
            }
            catch (ArgumentException)
            {
                return default(T);
            }
            catch (FormatException)
            {
                return default(T);
            }
        }
    }
}
