using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.PreciosMedios
{
    /// <summary>
    /// Acceso a datos del incremental de precios medios (Issue #547, corte c). Es el ÚNICO sitio de NestoAPI que
    /// escribe Productos.PrecioMedio, LinPedidoCmp.Coste y LinPedidoVta.Coste por precios medios, y solo lo usa
    /// <see cref="EscritorPreciosMedios"/>.
    /// </summary>
    public interface IRepositorioEscrituraPreciosMedios
    {
        /// <summary><c>GETDATE()</c> del servidor de BD: las «Fecha Modificación» las pone él, no el reloj del API.</summary>
        DateTime AhoraServidor();

        /// <summary>
        /// Productos (E + espejo, <c>TipoLínea = 1</c>) con alguna línea de compra modificada desde
        /// <paramref name="desde"/>, o cuya factura de compra se creó o modificó desde entonces.
        /// </summary>
        IReadOnlyList<string> ProductosConComprasModificadas(string empresa, string empresaEspejo, DateTime desde);

        /// <summary>
        /// Riesgo 2 del plan: productos con apuntes de <c>ExtractoProducto</c> (E + espejo) grabados desde
        /// <paramref name="desde"/> pero FECHADOS en o antes de alguna compra facturada suya (regularizaciones,
        /// inventarios, montajes con fecha elegida): cambian el stock «a la fecha» de esa compra y, por tanto, la media.
        /// </summary>
        IReadOnlyList<string> ProductosConMovimientosConFechaPasada(string empresa, string empresaEspejo, DateTime desde);

        /// <summary>Productos (<c>TipoLínea = 1</c>) de un pedido de compra.</summary>
        IReadOnlyList<string> ProductosDelPedidoCompra(string empresa, int pedido);

        /// <summary>Marca de la última pasada nocturna completa. Nula si no hay (o no se entiende).</summary>
        DateTime? LeerUltimaPasada();

        void GuardarUltimaPasada(DateTime marca);

        /// <summary>
        /// En UNA transacción y con un bloqueo de aplicación por (empresa, producto): lee el producto en lectura
        /// confirmada, pide el plan a <paramref name="planificar"/> (puro) y ejecuta sus sentencias. Si algo falla,
        /// deshace todo el producto.
        /// </summary>
        ResultadoEscrituraPrecioMedio RecalcularYEscribir(string empresa, string empresaEspejo, string producto,
            Func<DatosProductoPrecioMedio, PlanEscrituraPrecioMedio> planificar);
    }

    /// <summary>Qué ha pasado al recalcular un producto.</summary>
    public sealed class ResultadoEscrituraPrecioMedio
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public PlanEscrituraPrecioMedio Plan { get; set; }
        public int FilasProductos { get; set; }
        public int FilasCompras { get; set; }
        public int FilasVentas { get; set; }
        public double Milisegundos { get; set; }

        public bool HaCambiadoAlgo => FilasProductos + FilasCompras + FilasVentas > 0;
    }
}
