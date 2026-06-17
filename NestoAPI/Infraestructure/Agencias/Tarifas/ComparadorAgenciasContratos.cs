using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Una opción de envío (agencia + servicio) con su coste total ya calculado (con fuel).
    /// </summary>
    public class OpcionEnvioAgencia
    {
        public int AgenciaId { get; set; }
        public byte ServicioId { get; set; }
        public string NombreServicio { get; set; }
        public decimal Coste { get; set; }
    }

    /// <summary>
    /// Devuelve el recargo de combustible (fracción, p.ej. 0.1055) de una agencia, editable
    /// mensualmente. La implementación lo lee de AgenciasTransporte.RecargoCombustible.
    /// </summary>
    public interface IProveedorRecargoCombustible
    {
        decimal RecargoCombustible(string empresa, int agenciaId);
    }

    /// <summary>
    /// Catálogo de todas las tarifas de servicio disponibles para comparar.
    /// </summary>
    public interface IRegistroTarifas
    {
        IEnumerable<ITarifaAgencia> Todas();
    }
}
