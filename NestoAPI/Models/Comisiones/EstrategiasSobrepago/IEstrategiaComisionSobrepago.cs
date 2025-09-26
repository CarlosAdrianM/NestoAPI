using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface IEstrategiaComisionSobrepago
    {
        string Nombre { get; }
        string Descripcion { get; }

        void AplicarEstrategia(
            IEtiquetaComisionAcumulada etiquetaAcumulada,
            ICollection<TramoComision> tramosAnno);
    }
}
