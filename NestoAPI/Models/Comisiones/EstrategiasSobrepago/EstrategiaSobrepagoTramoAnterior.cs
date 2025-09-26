using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class EstrategiaSobrepagoTramoAnterior : IEstrategiaComisionSobrepago
    {
        public string Nombre => "Tramo anterior";

        public string Descripcion => "Se paga la comisión al tramo inmediatamente inferior al correspondiente hasta que el sobrepago se haya compensado completamente";

        public void AplicarEstrategia(IEtiquetaComisionAcumulada etiquetaAcumulada, ICollection<TramoComision> tramosAnno)
        {
            if (etiquetaAcumulada.Comision >= 0)
            {
                return;
            }

            // Buscar tramo anterior al actual
            var tramoActual = VendedorComisionAnual.BuscarTramoComision(tramosAnno, etiquetaAcumulada.Proyeccion);
            var tramoAnterior = BuscarTramoAnterior(tramosAnno, tramoActual);

            if (tramoAnterior == null)
            {
                return;
            }

            etiquetaAcumulada.EstrategiaUtilizada = Nombre;
            etiquetaAcumulada.MotivoEstrategia = Descripcion;
            etiquetaAcumulada.TipoCorrespondePorTramo = etiquetaAcumulada.Tipo;
            etiquetaAcumulada.TipoRealmenteAplicado = tramoAnterior.Tipo;
            etiquetaAcumulada.ComisionSinEstrategia = etiquetaAcumulada.Comision;
            etiquetaAcumulada.Tipo = tramoAnterior.Tipo;
            etiquetaAcumulada.Comision = Math.Round(tramoActual.Tipo * etiquetaAcumulada.Venta, 2, MidpointRounding.AwayFromZero);
        }

        private TramoComision BuscarTramoAnterior(ICollection<TramoComision> tramos, TramoComision tramoActual)
        {
            return tramos
                .Where(t => t.Hasta < tramoActual.Desde)
                .OrderByDescending(t => t.Hasta)
                .FirstOrDefault();
        }
    }
}
