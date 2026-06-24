using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// Catálogo de tarifas a comparar en NestoAPI. Solo se incluyen las agencias que de verdad
    /// usamos para auto-tarificar: hoy sacamos todo por GLS y empezaremos por Innovatrans en
    /// cuanto cierre su integración, así que la comparación real es GLS vs Innovatrans.
    ///
    /// NO se incluyen a propósito (decisión Carlos, jun-2026):
    ///   - Canteras: solo Canarias y no depende del precio (siempre va por ahí). Lo resuelve el
    ///     comparador como caso fijo, no como tarifa.
    ///   - CEX y Sending: en cuarentena (parámetro AgenciasEnCuarentena).
    ///   - OnTime: fuera del factory.
    /// Si alguna se reactiva, se porta su tarifa y se añade aquí.
    ///
    /// CTT entra como AGENCIA SOMBRA (AgenciasTransporte.EsSombra = 1): SÍ se incluye aquí para que
    /// compita en el ranking y poder medir cuántos envíos ganaría, pero el comparador la excluye de
    /// MasEconomica (nunca se auto-selecciona).
    /// </summary>
    public class RegistroTarifas : IRegistroTarifas
    {
        private static readonly IReadOnlyList<ITarifaAgencia> _tarifas = new List<ITarifaAgencia>
        {
            new TarifaGLSBusinessParcel(),
            new TarifaGLSBaleares(),
            new TarifaGLSEuroBusinessParcel(),
            new TarifaInnovatransEconomy(),
            new TarifaInnovatransPortugal(),
            new TarifaInnovatransMaritimo(),
            new TarifaCTT48h()
        };

        public IEnumerable<ITarifaAgencia> Todas() => _tarifas;
    }
}
