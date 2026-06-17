using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Catálogo de tarifas portadas a NestoAPI. Se van añadiendo según se migran de Nesto.
    /// PENDIENTE de portar: CEX (ePaq24/Paq24/PaqEmpresa14/Baleares/CanariasMaritimo), Sending
    /// (Express/Maritimo), Canteras, OnTime, Innovatrans (Economy/Portugal/Marítimo).
    /// </summary>
    public class RegistroTarifas : IRegistroTarifas
    {
        private static readonly IReadOnlyList<ITarifaAgencia> _tarifas = new List<ITarifaAgencia>
        {
            new TarifaGLSBusinessParcel(),
            new TarifaGLSBaleares()
        };

        public IEnumerable<ITarifaAgencia> Todas() => _tarifas;
    }
}
