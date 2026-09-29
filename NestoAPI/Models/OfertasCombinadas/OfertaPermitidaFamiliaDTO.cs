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
        // NestoAPI#564: subgrupo al que se limita la regla (null = toda la familia) y si es una
        // denegación (prohíbe el N+M en vez de permitirlo).
        public string SubGrupo { get; set; }
        public bool Denegar { get; set; }
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
        // NestoAPI#564: los dos campos son opcionales para que los Nesto antiguos (que no los
        // mandan) sigan funcionando.
        // - POST: null → SubGrupo sin informar (toda la familia) y Denegar = false.
        // - PUT: null → se CONSERVA lo que ya tenga la fila (un Nesto antiguo no borra una
        //   denegación ni su subgrupo al editar la cantidad). Para quitar el subgrupo hay que
        //   mandar la cadena vacía "".
        public string SubGrupo { get; set; }
        public bool? Denegar { get; set; }
    }
}
