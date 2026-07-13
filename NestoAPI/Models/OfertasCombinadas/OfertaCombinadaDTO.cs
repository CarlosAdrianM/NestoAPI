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
        // Issue #290: 2+1 combinable entre referencias de precios distintos. La unidad a base 0
        // debe ser la de menor tarifa del conjunto y las pagadas cubren su tarifa (suelo dinámico).
        public bool RegalarMenorImporte { get; set; }
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
        // Issue #289: la fila de filtro tambien puede casar por Grupo y/o Subgrupo del producto
        // (todos los criterios informados en AND). En blanco = igual que antes.
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
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
        // Issue #290: ver OfertaCombinadaDTO.RegalarMenorImporte. Por defecto TRUE (decisión de
        // Carlos 13/07/26): las ofertas nuevas nacen con la regla activada salvo que se desmarque
        // (p. ej. promos que regalan a propósito un artículo más caro que lo comprado).
        public bool RegalarMenorImporte { get; set; } = true;
        public List<OfertaCombinadaDetalleCreateDTO> Detalles { get; set; }
    }

    public class OfertaCombinadaDetalleCreateDTO
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        // Issue #282: fila de FILTRO (Producto null): casa por familia y/o prefijo del nombre.
        public string Familia { get; set; }
        public string FiltroProducto { get; set; }
        // Issue #289: filtro por Grupo y/o Subgrupo del producto (AND con familia/prefijo).
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public short Cantidad { get; set; }
        public decimal Precio { get; set; }
        public int? GrupoAlternativa { get; set; }
        public bool PermitirCantidadMenor { get; set; }
    }
}
