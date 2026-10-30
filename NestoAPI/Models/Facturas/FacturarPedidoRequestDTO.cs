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

        /// <summary>
        /// NestoAPI#765: una nota de ENTREGA sin picking o sin envío no se procesa sin más (la contestación lleva un error con
        /// <see cref="PedidoConErrorDTO.Codigo"/> «NOTA_SIN_PACKING»). Con true, quien la procesa ya ha confirmado que se salta el
        /// packing: se procesa, y si no tiene picking, con sus líneas en curso hasta la fecha de entrega.
        /// </summary>
        public bool SaltarPacking { get; set; }
    }
}
