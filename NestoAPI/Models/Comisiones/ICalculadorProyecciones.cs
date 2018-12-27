namespace NestoAPI.Models.Comisiones
{
    public interface ICalculadorProyecciones
    {
        decimal CalcularProyeccion(string vendedor, int anno, int mes, decimal ventaAcumulada, int meses, int mesesAnno);
    }
}
