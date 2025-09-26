using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones.EstrategiasSobrepago
{
    public class EstrategiaSobrepagoMitadDeComision : IEstrategiaComisionSobrepago
    {
        public string Nombre => "Mitad comisión";

        public string Descripcion => "Se paga la mitad de la comisión correspondiente al tramo, hasta que el sobrepago se haya compensado completamente";

        public void AplicarEstrategia(IEtiquetaComisionAcumulada etiquetaAcumulada, ICollection<TramoComision> tramosAnno)
        {
            var tramoActual = VendedorComisionAnual.BuscarTramoComision(tramosAnno, etiquetaAcumulada.Proyeccion);

            if (tramoActual == null)
            {
                return;
            }

            decimal tipoAplicado = tramoActual.Tipo / 2;
            decimal comisionTramo = Math.Round(tipoAplicado * etiquetaAcumulada.Venta, 2, MidpointRounding.AwayFromZero);

            if (etiquetaAcumulada.Comision >= comisionTramo)
            {
                return;
            }


            etiquetaAcumulada.EstrategiaUtilizada = Nombre;
            etiquetaAcumulada.MotivoEstrategia = Descripcion;
            etiquetaAcumulada.TipoCorrespondePorTramo = tramoActual.Tipo;
            etiquetaAcumulada.TipoRealmenteAplicado = tipoAplicado;
            etiquetaAcumulada.ComisionSinEstrategia = etiquetaAcumulada.Comision;
            etiquetaAcumulada.Comision = comisionTramo;
        }
    }
}
