using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class CalculadorProyecciones2019 : ICalculadorProyecciones
    {
        const string GENERAL = "General";

        public decimal CalcularFaltaParaSalto(decimal ventaAcumulada, decimal tramoHasta, decimal mesesDecimales, decimal proyeccion)
        {
            return tramoHasta - proyeccion;
        }

        public decimal CalcularProyeccion(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno)
        {
            ICollection<ResumenComisionesMes> annoActual;
            decimal ventaActual;
            int numerosMesesActual;

            if (ventaAcumulada == 0 && meses == 0)
            {
                annoActual = servicio.LeerResumenAnno(vendedor, anno);
                ventaActual = annoActual.Where(v => v.Mes <= mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta);
                numerosMesesActual = annoActual.Where(v => v.Mes <= mes).Count();
            } else
            {
                ventaActual = ventaAcumulada;
                numerosMesesActual = meses;
            }
            
            var annoAnterior = servicio.LeerResumenAnno(vendedor, anno - 1);
            var ventaAnterior = annoAnterior.Where(v => v.Mes > mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta);

            
            var numeroMesesAnterior = annoAnterior.Where(v => v.Mes > mes).Count();
            int mesesProyeccion = mes < 8 ? 11 : 12;


            if (numeroMesesAnterior != 12-mes)
            {    
                decimal ventaMedia = ventaActual / numerosMesesActual;
                if (numeroMesesAnterior > 0)
                {
                    ventaActual += ventaMedia * ((12 - mes) - numeroMesesAnterior);
                } else
                {
                    ventaActual += ventaMedia * (mesesProyeccion - mes);
                }
                
            }

            return Math.Round(ventaActual + ventaAnterior, 2);
        }
    }
}