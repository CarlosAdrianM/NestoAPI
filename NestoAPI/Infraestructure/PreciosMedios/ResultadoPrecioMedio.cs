using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Lo que el SP de precios medios dejaría escrito para un producto (Issue #547): la media final
    /// (<c>Productos.PrecioMedio</c>), el coste de cada línea de compra (<c>LinPedidoCmp.Coste</c>) y los
    /// tramos de <c>##global</c> con los que reescribe <c>LinPedidoVta.Coste</c>. Más los avisos de los
    /// casos en los que el SP no es determinista o hace algo sospechoso.
    /// </summary>
    public sealed class ResultadoPrecioMedio
    {
        public ResultadoPrecioMedio()
        {
            Tramos = new List<TramoPrecioMedio>();
            CostesPorLinea = new Dictionary<int, decimal>();
            Avisos = new List<AvisoPrecioMedio>();
        }

        public string Empresa { get; set; }
        public string Producto { get; set; }

        /// <summary>False si el SP no toca el producto (ver <see cref="Motivo"/>): se queda la media que tenga.</summary>
        public bool Procesado { get; set; }

        public MotivoPrecioMedioNoProcesado Motivo { get; set; }

        /// <summary>
        /// Nuevo <c>Productos.PrecioMedio</c> (solo de la empresa E, nunca de la espejo). Nulo si no se procesa
        /// o si ninguna línea llegó a calcularse (el SP no escribiría nada).
        /// </summary>
        public decimal? PrecioMedioFinal { get; set; }

        /// <summary>Tramos en el orden en que el SP los inserta en <c>##global</c>.</summary>
        public List<TramoPrecioMedio> Tramos { get; }

        /// <summary><c>NºOrden</c> → nuevo <c>LinPedidoCmp.Coste</c>. Solo las líneas que el SP reescribe.</summary>
        public Dictionary<int, decimal> CostesPorLinea { get; }

        public List<AvisoPrecioMedio> Avisos { get; }
    }

    /// <summary>
    /// Un tramo de <c>##global</c>: las ventas con albarán en [FechaDesde, FechaHasta) llevan este coste.
    /// </summary>
    public sealed class TramoPrecioMedio
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public decimal PrecioMedio { get; set; }

        /// <summary>Factura de compra que generó el tramo (diagnóstico).</summary>
        public int NumeroFactura { get; set; }

        /// <summary>Fecha de albarán de la compra que generó el tramo (diagnóstico).</summary>
        public DateTime FechaAlbaran { get; set; }
    }

    public sealed class AvisoPrecioMedio
    {
        public AvisoPrecioMedio(TipoAvisoPrecioMedio tipo, string detalle)
        {
            Tipo = tipo;
            Detalle = detalle;
        }

        public TipoAvisoPrecioMedio Tipo { get; }
        public string Detalle { get; }

        public override string ToString()
        {
            return Tipo + ": " + Detalle;
        }
    }

    public enum MotivoPrecioMedioNoProcesado
    {
        /// <summary>Sí se procesa.</summary>
        Ninguno,

        /// <summary>No tiene líneas de compra con fecha de albarán (incluye los kits sin compras).</summary>
        SinCompras,

        /// <summary>En la primera fecha de albarán ninguna línea está facturada: el SP lo trata como sin compras.</summary>
        PrimeraCompraSinFacturar,

        /// <summary><c>Productos.Ficticio = 1</c>.</summary>
        Ficticio,

        /// <summary>El último montaje de kit es posterior a la última recepción de compra.</summary>
        ExcluidoPorMontaje,

        /// <summary>
        /// Código de más de 10 caracteres: <c>prdLLamarActualizarPrecioMedioProducto</c> recibe <c>@Producto char(10)</c>,
        /// lo trunca y no encuentra sus compras.
        /// </summary>
        CodigoDemasiadoLargo
    }

    public enum TipoAvisoPrecioMedio
    {
        /// <summary>
        /// Dos facturas distintas con la misma fecha y hora de albarán: el SP las procesa en orden indeterminado
        /// (la última pisa a la otra); aquí se desempata por nº de factura.
        /// </summary>
        EmpateFacturasMismaFecha,

        /// <summary>
        /// Varios albaranes distintos en la fecha del «albarán anterior»: el SP coge uno cualquiera
        /// (<c>top 1 ... order by FechaAlbarán desc</c> sin desempate).
        /// </summary>
        EmpateAlbaranAnterior,

        /// <summary>
        /// El «albarán anterior» tiene líneas con costes distintos (misma factura con dos fechas): el SP coge
        /// uno cualquiera; aquí, el de la línea más reciente (lo que hizo en el caso real 34248).
        /// </summary>
        AlbaranAnteriorConVariosCostes,

        /// <summary>
        /// La media anterior sale de un albarán sin facturar (coste sembrado, no calculado): el SP lo usa igual.
        /// </summary>
        AlbaranAnteriorSinFacturar,

        /// <summary>La media resultante es negativa (efecto del <c>abs(stock - cantidad)</c> del SP).</summary>
        MediaNegativa,

        /// <summary>Código de producto de más de 10 caracteres (ver <see cref="MotivoPrecioMedioNoProcesado.CodigoDemasiadoLargo"/>).</summary>
        CodigoDemasiadoLargo
    }
}
