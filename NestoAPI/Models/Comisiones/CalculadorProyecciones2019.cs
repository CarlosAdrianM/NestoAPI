using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class CalculadorProyecciones2019 : ICalculadorProyecciones
    {
        private readonly IComisionesAnuales _comisiones;

        public CalculadorProyecciones2019(IComisionesAnuales comisiones)
        {
            _comisiones = comisiones;
        }

        public decimal CalcularFaltaParaSalto(decimal ventaAcumulada, decimal tramoHasta, decimal mesesDecimales, decimal proyeccion)
        {
            return tramoHasta - proyeccion;
        }

        public decimal CalcularProyeccion(string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno)
        {
            ICollection<ResumenComisionesMes> annoActual;
            decimal ventaActual;
            int numerosMesesActual;
            bool todoElEquipo = anno < 2025;


            if (ventaAcumulada == 0 && meses == 0)
            {
                annoActual = _comisiones.LeerResumenAnno(vendedor, anno, todoElEquipo);
                ventaActual = annoActual.Where(v => v.Mes <= mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Venta);
                numerosMesesActual = annoActual.Where(v => v.Mes <= mes).Count();
            }
            else
            {
                ventaActual = ventaAcumulada;
                numerosMesesActual = meses;
            }

            var annoAnterior = _comisiones.LeerResumenAnno(vendedor, anno - 1, todoElEquipo);
            var ventaAnterior = annoAnterior.Where(v => v.Mes > mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Venta);

            var numeroMesesAnterior = ventaAnterior != 0 ? annoAnterior.Where(v => v.Mes > mes).Count() : 0;
            bool hayAgostoAnterior = annoAnterior.Where(v => v.Mes == 8).SingleOrDefault() != null;
            int mesesProyeccion = mes < 8 && !hayAgostoAnterior ? 11 : 12;


            if (numeroMesesAnterior != 12 - mes)
            {
                decimal ventaMedia = ventaActual / numerosMesesActual;
                ventaActual += ventaMedia * (mesesProyeccion - mes - numeroMesesAnterior);
            }

            return Math.Round(ventaActual + ventaAnterior, 2);
        }

        public bool CalcularSiBajaDeSalto(string vendedor, int anno, int mes, int mesesAnno, ResumenComisionesMes resumen, decimal ventaAcumulada, int meses, ICollection<TramoComision> tramosAnno)
        {
            if (meses == 12)
            {
                return false;
            }
            var ventasAnnoAnterior = _comisiones.LeerResumenAnno(vendedor, anno - 1);
            var resumenVentasMesSiguienteAnnoAnterior = ventasAnnoAnterior.Where(v => v.Mes == mes + 1).SingleOrDefault();
            decimal ventasMesSiguienteAnnoAnterior;
            if (resumenVentasMesSiguienteAnnoAnterior != null)
            {
                ventasMesSiguienteAnnoAnterior = ComisionesHelper.ObtenerEtiquetaAcumulada(resumenVentasMesSiguienteAnnoAnterior.Etiquetas).Venta;
            }
            else
            {
                decimal media = ventaAcumulada / (mes + 1);
                ventasMesSiguienteAnnoAnterior = ventaAcumulada + media;
            }
            var proyeccionMesSiguiente = ComisionesHelper.ObtenerEtiquetaAcumulada(resumen.Etiquetas).Proyeccion - ventasMesSiguienteAnnoAnterior;
            var tramoProyeccion = VendedorComisionAnual.BuscarTramoComision(tramosAnno, ComisionesHelper.ObtenerEtiquetaAcumulada(resumen.Etiquetas).Proyeccion);
            var tramoProyeccionMesSiguiente = VendedorComisionAnual.BuscarTramoComision(tramosAnno, proyeccionMesSiguiente);
            return tramoProyeccion != tramoProyeccionMesSiguiente;
        }
    }
}