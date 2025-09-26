using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public class EstrategiaSobrepagoDescuentoCompleto : IEstrategiaComisionSobrepago
    {
        public string Nombre => "Descuento completo";

        public string Descripcion => "No paga nada de comisión hasta que el sobrepago se haya compensado completamente";

        public void AplicarEstrategia(IEtiquetaComisionAcumulada etiquetaAcumulada, ICollection<TramoComision> tramosAnno)
        {
            if (etiquetaAcumulada.Comision >= 0)
            {
                return;
            }

            etiquetaAcumulada.EstrategiaUtilizada = Nombre;
            etiquetaAcumulada.MotivoEstrategia = Descripcion;
            etiquetaAcumulada.TipoCorrespondePorTramo = etiquetaAcumulada.Tipo;
            etiquetaAcumulada.TipoRealmenteAplicado = 0;
            etiquetaAcumulada.ComisionSinEstrategia = etiquetaAcumulada.Comision;
            etiquetaAcumulada.Comision = 0;
        }
    }
}
