using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Todo lo que la calculadora necesita saber de un producto para replicar el SP de precios medios
    /// (Issue #547). Objeto plano, sin EF: lo construye el repositorio (corte b) o los tests.
    /// </summary>
    public sealed class HistorialPrecioMedio
    {
        /// <summary>Empresa que se procesa (1, 4 o 5).</summary>
        public string Empresa { get; set; }

        /// <summary><c>Empresas.[Iva por defecto]</c> de la empresa: 1 → 3, 4 → 4, 5 → 5.</summary>
        public string EmpresaEspejo { get; set; }

        public string Producto { get; set; }

        /// <summary><c>Productos.Ficticio</c> de la empresa E: los ficticios no se procesan.</summary>
        public bool Ficticio { get; set; }

        /// <summary>
        /// Fecha del último apunte de <c>ExtractoProducto</c> (E + espejo) con <c>Diario = '_MontarKit'</c>,
        /// <c>Texto like 'Montaje %'</c> y <c>Cantidad &gt; 0</c>. Nula si nunca se ha montado.
        /// </summary>
        public DateTime? FechaUltimoMontaje { get; set; }

        /// <summary>
        /// Fecha del último apunte de <c>ExtractoProducto</c> (E + espejo) con <c>NºProveedor</c> no nulo y
        /// <c>Cantidad &gt; 0</c> (la última recepción de compra). Nula si no hay ninguno.
        /// </summary>
        public DateTime? FechaUltimaRecepcion { get; set; }

        /// <summary>Líneas de compra del producto (<c>TipoLínea = 1</c>, E + espejo, cualquier estado).</summary>
        public IReadOnlyList<LineaCompraPrecioMedio> Compras { get; set; }

        /// <summary>
        /// Stock a una fecha: <c>sum(ExtractoProducto.Cantidad)</c> del producto en E + espejo con
        /// <c>Fecha &lt;= fecha</c> (incluye el apunte de la propia compra y todo lo del mismo instante).
        /// Sin apuntes → 0 (el SP deja el valor por defecto de la temporal).
        /// </summary>
        public Func<DateTime, int> StockHasta { get; set; }
    }
}
