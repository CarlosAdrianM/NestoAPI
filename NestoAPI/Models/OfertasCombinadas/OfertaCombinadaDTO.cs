using System;
using System.Collections.Generic;

namespace NestoAPI.Models.OfertasCombinadas
{
    public class OfertaCombinadaDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public decimal ImporteMinimo { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
        public List<OfertaCombinadaDetalleDTO> Detalles { get; set; }
    }

    public class OfertaCombinadaDetalleDTO
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public string ProductoNombre { get; set; }
        public short Cantidad { get; set; }
        public decimal Precio { get; set; }
    }

    public class OfertaCombinadaCreateDTO
    {
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public decimal ImporteMinimo { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public List<OfertaCombinadaDetalleCreateDTO> Detalles { get; set; }
    }

    public class OfertaCombinadaDetalleCreateDTO
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public short Cantidad { get; set; }
        public decimal Precio { get; set; }
    }
}
