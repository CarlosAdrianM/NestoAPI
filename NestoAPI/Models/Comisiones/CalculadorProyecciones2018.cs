using System;

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
    }
}