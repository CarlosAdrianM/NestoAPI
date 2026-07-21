using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// DTO de respuesta para la creación de facturas
    /// Incluye el número de factura y la empresa donde se facturó
    /// (que puede ser diferente a la empresa original si hubo traspaso)
    /// </summary>
    public class CrearFacturaResponseDTO
    {
        /// <summary>
        /// Número de la factura creada
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Empresa donde se facturó el pedido
        /// Puede ser diferente a la empresa original si hubo traspaso a empresa espejo
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Número del pedido facturado
        /// </summary>
        public int NumeroPedido { get; set; }

        /// <summary>
        /// NestoAPI#327: avisos operativos de la facturación (p. ej. NIF no registrado en la
        /// AEAT durante el periodo de gracia hasta el 01/12/2026). La factura SE HA creado;
        /// el cliente debe mostrarlos a quien factura.
        /// </summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }
}
