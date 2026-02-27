using System;

namespace NestoAPI.Models.OfertasCombinadas
{
    public class OfertaPermitidaFamiliaDTO
    {
        public int NOrden { get; set; }
        public string Empresa { get; set; }
        public string Familia { get; set; }
        public string FamiliaDescripcion { get; set; }
        public short CantidadConPrecio { get; set; }
        public short CantidadRegalo { get; set; }
        public string FiltroProducto { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }

    public class OfertaPermitidaFamiliaCreateDTO
    {
        public string Empresa { get; set; }
        public string Familia { get; set; }
        public short CantidadConPrecio { get; set; }
        public short CantidadRegalo { get; set; }
        public string FiltroProducto { get; set; }
    }
}
