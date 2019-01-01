namespace NestoAPI.Models.Comisiones
{
    public interface ICalculadorProyecciones
    {
        decimal CalcularProyeccion(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno);
    }
}
