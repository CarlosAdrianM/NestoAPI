using System.Collections.Generic;

namespace NestoAPI.Models.CodigosPostales
{
    /// <summary>
    /// #378: fila de la ventana de mantenimiento de códigos postales de Nesto
    /// (población, provincia, ruta, país, vendedor y vendedores por grupo de producto).
    /// </summary>
    public class CodigoPostalMantenimientoDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Ruta { get; set; }
        public string Vendedor { get; set; }
        public string Pais { get; set; }
        public List<VendedorGrupoProductoCodigoPostalDTO> VendedoresGrupoProducto { get; set; } = new List<VendedorGrupoProductoCodigoPostalDTO>();
    }

    public class VendedorGrupoProductoCodigoPostalDTO
    {
        public string GrupoProducto { get; set; }
        public string Vendedor { get; set; }
    }
}
