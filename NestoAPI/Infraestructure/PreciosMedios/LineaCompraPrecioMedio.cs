using System;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Una línea de compra (<c>LinPedidoCmp</c>) del producto, tal como la ve el SP de precios medios:
    /// <c>TipoLínea = 1</c>, empresa E o su espejo, CUALQUIER estado (Issue #547, §1).
    /// Objeto plano, sin EF: lo rellena el repositorio (corte b) o los tests.
    /// </summary>
    public sealed class LineaCompraPrecioMedio
    {
        public string Empresa { get; set; }

        /// <summary>
        /// <c>LinPedidoCmp.NºFactura</c> (en la BD es <c>char</c>; el SP lo convierte a <c>int</c>).
        /// Nulo en las líneas sin facturar.
        /// </summary>
        public int? NumeroFactura { get; set; }

        /// <summary>
        /// <c>FechaAlbarán</c> CON HORA: los tramos y el stock se comparan con esa precisión.
        /// Nula en las líneas pendientes (el cursor del SP las ignora).
        /// </summary>
        public DateTime? FechaAlbaran { get; set; }

        public int? NumeroAlbaran { get; set; }

        /// <summary>Clave de la línea (<c>NºOrden</c>).</summary>
        public int NumeroOrden { get; set; }

        public int Cantidad { get; set; }

        public decimal BaseImponible { get; set; }

        /// <summary>
        /// <c>LinPedidoCmp.Coste</c> actual. Solo se usa como «media anterior» cuando la línea es de un
        /// albarán que NO se recalcula (p. ej. sin facturar: el SP coge su coste sembrado). El de las líneas
        /// facturadas se recalcula antes de leerse, así que su valor de entrada da igual.
        /// </summary>
        public decimal? Coste { get; set; }

        public short Estado { get; set; }
    }
}
