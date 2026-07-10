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
        // Issue #282: fila de FILTRO (Producto null): casa por familia y/o prefijo del nombre.
        public string Familia { get; set; }
        public string FiltroProducto { get; set; }
        public short Cantidad { get; set; }
        public decimal Precio { get; set; }
        // Líneas con el mismo GrupoAlternativa son intercambiables ("elige 1"); null = obligatoria.
        public int? GrupoAlternativa { get; set; }
        // Si true, Cantidad es un MÁXIMO: el pedido puede llevar de 0 a Cantidad sin que la oferta
        // deje de validar (extra opcional, p. ej. folletos/expositor). NestoAPI#239.
        public bool PermitirCantidadMenor { get; set; }
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
        // Issue #282: fila de FILTRO (Producto null): casa por familia y/o prefijo del nombre.
        public string Familia { get; set; }
        public string FiltroProducto { get; set; }
        public short Cantidad { get; set; }
        public decimal Precio { get; set; }
        public int? GrupoAlternativa { get; set; }
        public bool PermitirCantidadMenor { get; set; }
    }
}
