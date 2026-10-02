using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Petición de POST api/FacturacionRutas/FacturarPedido: facturar un solo pedido por el mismo camino
    /// que la facturación de rutas (lo usa «Facturar al imprimir etiqueta» de Agencias).
    /// </summary>
    public class FacturarPedidoRequestDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }

        /// <summary>Hasta qué fecha de entrega se facturan las líneas. Si es null, hoy (igual que las rutas).</summary>
        public DateTime? FechaEntregaDesde { get; set; }
    }
}
