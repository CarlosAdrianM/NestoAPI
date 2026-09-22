using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Informes
{
    public class PedidoCompraInformeDTO
    {
        public int Id { get; set; }
        public string Proveedor { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
        public string Cif { get; set; }
        public DateTime Fecha { get; set; }
        public bool PedidoValorado { get; set; }
        /// <summary>NestoAPI#510: pronto pago del pedido (tanto por uno), el de sus líneas. Va a pie de documento.</summary>
        public decimal DescuentoPP { get; set; }
        public List<LineaPedidoCompraInformeDTO> Lineas { get; set; }
    }

    public class LineaPedidoCompraInformeDTO
    {
        public string SuReferencia { get; set; }
        public string NuestraReferencia { get; set; }
        public string Descripcion { get; set; }
        public short? Tamanno { get; set; }
        public string UnidadMedida { get; set; }
        public short? Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        /// <summary>Descuento total grabado en BD, CON el pronto pago dentro (lo recalcula el trigger).</summary>
        public decimal SumaDescuentos { get; set; }
        /// <summary>Base imponible grabada en BD, ya con el pronto pago restado.</summary>
        public decimal BaseImponible { get; set; }
        /// <summary>NestoAPI#510: Precio × Cantidad sin descuentos, para desplegar el importe sin PP.</summary>
        public decimal Bruto { get; set; }
        /// <summary>NestoAPI#510: pronto pago de la línea (tanto por uno).</summary>
        public decimal DescuentoPP { get; set; }
    }
}
