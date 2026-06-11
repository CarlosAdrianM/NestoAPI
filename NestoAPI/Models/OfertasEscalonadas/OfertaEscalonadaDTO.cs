using System;
using System.Collections.Generic;

namespace NestoAPI.Models.OfertasEscalonadas
{
    public class OfertaEscalonadaDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
        public List<OfertaEscalonadaProductoDTO> Productos { get; set; }
        public List<OfertaEscalonadaTramoDTO> Tramos { get; set; }
    }

    public class OfertaEscalonadaProductoDTO
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        public string ProductoNombre { get; set; }
        public decimal PrecioBase { get; set; }
    }

    public class OfertaEscalonadaTramoDTO
    {
        public int Id { get; set; }
        public short CantidadMinima { get; set; }
        // En tanto por uno (0.25 = 25 %).
        public decimal Descuento { get; set; }
    }

    public class OfertaEscalonadaCreateDTO
    {
        public string Empresa { get; set; }
        public string Nombre { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public List<OfertaEscalonadaProductoCreateDTO> Productos { get; set; }
        public List<OfertaEscalonadaTramoCreateDTO> Tramos { get; set; }
    }

    public class OfertaEscalonadaProductoCreateDTO
    {
        public int Id { get; set; }
        public string Producto { get; set; }
        // Null = precargar el PVP de la ficha del producto al guardar.
        public decimal? PrecioBase { get; set; }
    }

    public class OfertaEscalonadaTramoCreateDTO
    {
        public int Id { get; set; }
        public short CantidadMinima { get; set; }
        public decimal Descuento { get; set; }
    }
}
