using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Lee el recargo de combustible de una agencia de AgenciasTransporte.RecargoCombustible.
    /// El fuel es a nivel de transportista (mismo % en todas las empresas), así que se busca por
    /// Numero de agencia; el parámetro empresa se acepta por la interfaz pero no discrimina.
    /// </summary>
    public class ProveedorRecargoCombustibleEF : IProveedorRecargoCombustible
    {
        private readonly NVEntities _db;

        public ProveedorRecargoCombustibleEF(NVEntities db)
        {
            _db = db;
        }

        public decimal RecargoCombustible(string empresa, int agenciaId)
        {
            AgenciaTransporte agencia = _db.AgenciasTransportes.FirstOrDefault(a => a.Numero == agenciaId);
            return agencia == null ? 0m : agencia.RecargoCombustible;
        }
    }
}
