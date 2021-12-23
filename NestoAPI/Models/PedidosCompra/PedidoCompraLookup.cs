using System;

namespace NestoAPI.Models.PedidosCompra
{
    public class PedidoCompraLookup
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public DateTime Fecha { get; set; }
        public bool TieneAlbaran { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal Total { get; set; }
        public bool TieneEnviado { get; set; }
        public bool TieneVistoBueno { get; set; }
    }
}