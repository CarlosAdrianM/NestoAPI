using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Decora un <see cref="IRegistroTarifas"/> dejando solo las tarifas de agencias que de verdad
    /// existen en AgenciasTransporte (hay fila con ese Numero). Así una agencia con tarifa portada
    /// pero todavía no dada de alta (p.ej. Innovatrans antes de crearla) NO entra en la comparación.
    /// </summary>
    public class RegistroTarifasExistentes : IRegistroTarifas
    {
        private readonly IRegistroTarifas _registro;
        private readonly ISet<int> _numerosExistentes;

        public RegistroTarifasExistentes(IRegistroTarifas registro, IEnumerable<int> numerosExistentes)
        {
            _registro = registro;
            _numerosExistentes = new HashSet<int>(numerosExistentes);
        }

        public IEnumerable<ITarifaAgencia> Todas()
            => _registro.Todas().Where(t => _numerosExistentes.Contains(t.AgenciaId));
    }
}
