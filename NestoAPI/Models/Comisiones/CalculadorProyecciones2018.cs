using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class CalculadorProyecciones2018 : ICalculadorProyecciones
    {
        public decimal CalcularFaltaParaSalto(decimal ventaAcumulada, decimal tramoHasta, decimal mesesDecimales, decimal proyeccion)
        {
            return Math.Round((tramoHasta / mesesDecimales) - ventaAcumulada, 2);
        }

        public decimal CalcularProyeccion(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno)
        {
            return (ventaAcumulada / meses) * mesesAnno;
        }

        public bool CalcularSiBajaDeSalto(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, int mesesAnno, ResumenComisionesMes resumen, decimal ventaAcumulada, int meses, ICollection<TramoComision> tramosAnno)
        {
            if (meses == 12)
            {
                return false;
            }
            var tramosMes = servicio.LeerTramosComisionMes(vendedor);
            var tramoMaximoMes = tramosMes.LastOrDefault() != null ? tramosMes.LastOrDefault().Hasta : 0;
            var proyeccionMesSiguiente = ((ventaAcumulada + tramoMaximoMes) / (meses + 1)) * mesesAnno;
            var tramoProyeccion = VendedorComisionAnual.BuscarTramoComision(tramosAnno, resumen.GeneralProyeccion);
            var tramoProyeccionMesSiguiente = VendedorComisionAnual.BuscarTramoComision(tramosAnno, proyeccionMesSiguiente);
            return (tramoProyeccion != tramoProyeccionMesSiguiente);
        }
    }
}