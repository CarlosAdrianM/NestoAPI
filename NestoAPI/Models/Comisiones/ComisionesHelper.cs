using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public static class ComisionesHelper
    {
        public static IEtiquetaComisionAcumulada ObtenerEtiquetaAcumulada(ICollection<IEtiquetaComision> etiquetas)
        {
            // Asumimos que solo hay una etiqueta acumulada. Si mañana puede haber dos (o cero) hay que cambiar este método
            return etiquetas.OfType<IEtiquetaComisionAcumulada>().Single();
        }
    }
}
