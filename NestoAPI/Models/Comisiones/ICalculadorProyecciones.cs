using System.Collections.Generic;

namespace NestoAPI.Models.Comisiones
{
    public interface ICalculadorProyecciones
    {
        decimal CalcularProyeccion(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno);
        decimal CalcularFaltaParaSalto(decimal ventaAcumulada, decimal tramoHasta, decimal mesesDecimales, decimal proyeccion);
        bool CalcularSiBajaDeSalto(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, int mesesAnno, ResumenComisionesMes resumen, decimal ventaAcumulada, int meses, ICollection<TramoComision> tramosAnno);
    }
}
