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
        public int CoberturaObjetivoDias { get; set; }
        public int? StockMaximoActual { get; set; }
        public int? StockMinimoActual { get; set; }
        public string Formula { get; set; }

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
        public int DemandaNoServida { get; set; }
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
    }
}
