using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class CalculadorProyecciones2019 : ICalculadorProyecciones
    {
        const string GENERAL = "General";
        private readonly IServicioComisionesAnuales _servicioComisiones;

        public CalculadorProyecciones2019(IServicioComisionesAnuales servicioComisiones)
        {
            this._servicioComisiones = servicioComisiones;
        }

        public decimal CalcularFaltaParaSalto(decimal ventaAcumulada, decimal tramoHasta, decimal mesesDecimales, decimal proyeccion)
        {
            return tramoHasta - proyeccion;
        }

        public decimal CalcularProyeccion(ICollection<IEtiquetaComision> etiquetas, string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno)
        {
            ICollection<ResumenComisionesMes> annoActual;
            decimal ventaActual;
            int numerosMesesActual;

            if (ventaAcumulada == 0 && meses == 0)
            {
                annoActual = _servicioComisiones.LeerResumenAnno(etiquetas, vendedor, anno);
                ventaActual = annoActual.Where(v => v.Mes <= mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta);
                numerosMesesActual = annoActual.Where(v => v.Mes <= mes).Count();
            } else
            {
                ventaActual = ventaAcumulada;
                numerosMesesActual = meses;
            }
            
            var annoAnterior = _servicioComisiones.LeerResumenAnno(etiquetas, vendedor, anno - 1);
            var ventaAnterior = annoAnterior.Where(v => v.Mes > mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta);

            
            var numeroMesesAnterior = annoAnterior.Where(v => v.Mes > mes).Count();
            bool hayAgostoAnterior = annoAnterior.Where(v => v.Mes == 8).SingleOrDefault() != null;
            int mesesProyeccion = mes < 8 && !hayAgostoAnterior ? 11 : 12;


            if (numeroMesesAnterior != 12-mes)
            {    
                decimal ventaMedia = ventaActual / numerosMesesActual;
                ventaActual += ventaMedia * ((mesesProyeccion - mes) - numeroMesesAnterior);
            }

            return Math.Round(ventaActual + ventaAnterior, 2);
        }

        public bool CalcularSiBajaDeSalto(ICollection<IEtiquetaComision> etiquetas, string vendedor, int anno, int mes, int mesesAnno, ResumenComisionesMes resumen, decimal ventaAcumulada, int meses, ICollection<TramoComision> tramosAnno)
        {
            if (meses == 12)
            {
                return false;
            }
            var ventasAnnoAnterior = _servicioComisiones.LeerResumenAnno(etiquetas, vendedor, anno - 1);
            var resumenVentasMesSiguienteAnnoAnterior = ventasAnnoAnterior.Where(v => v.Mes == mes + 1).SingleOrDefault();
            decimal ventasMesSiguienteAnnoAnterior;
            if (resumenVentasMesSiguienteAnnoAnterior != null)
            {
                ventasMesSiguienteAnnoAnterior = resumenVentasMesSiguienteAnnoAnterior.Etiquetas.Where(e => e.Nombre == GENERAL).Sum(e => e.Venta);
            } else
            {
                decimal media = ventaAcumulada / (mes + 1);
                ventasMesSiguienteAnnoAnterior = ventaAcumulada + media;
            }
            var proyeccionMesSiguiente = resumen.GeneralProyeccion - ventasMesSiguienteAnnoAnterior;
            var tramoProyeccion = VendedorComisionAnual.BuscarTramoComision(tramosAnno, resumen.GeneralProyeccion);
            var tramoProyeccionMesSiguiente = VendedorComisionAnual.BuscarTramoComision(tramosAnno, proyeccionMesSiguiente);
            return (tramoProyeccion != tramoProyeccionMesSiguiente);
        }
    }
}