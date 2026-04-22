using System;

namespace NestoAPI.Models.Comisiones
{
    // Proyección limpia de la vista vstLinPedidoVtaComisiones para los listados
    // agrupados (por grupo/dirección, por familia, por fecha) del panel de
    // Comisión Anual.
    public class VentaVendedorComisionDTO
    {
        public int NumeroOrden { get; set; }
        public string Empresa { get; set; }
        public string NumeroCliente { get; set; }
        public string Contacto { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodPostal { get; set; }
        public string Poblacion { get; set; }
        public int Numero { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public string Familia { get; set; }
        public DateTime FechaEntrega { get; set; }
        public short? Cantidad { get; set; }
        public short Estado { get; set; }
        public int? Picking { get; set; }
        public decimal BaseImponible { get; set; }
        public DateTime? FechaAlbaran { get; set; }
        public int? NumeroAlbaran { get; set; }
        public DateTime? FechaFactura { get; set; }
        public string NumeroFactura { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public short? EstadoFamilia { get; set; }
        public decimal? PrecioTarifa { get; set; }
    }
}
