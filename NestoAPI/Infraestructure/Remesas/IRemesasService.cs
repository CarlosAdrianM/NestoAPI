using NestoAPI.Models.Remesas;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Remesas
{
    /// <summary>
    /// Lecturas del módulo de Remesas de cobros (Nesto#340, Fase 1C.14: sustituye los accesos EF
    /// del RemesasViewModel del cliente).
    /// </summary>
    public interface IRemesasService
    {
        /// <param name="top">Número máximo de remesas (null = todas, botón "Ver Todas").</param>
        Task<List<RemesaDTO>> LeerRemesasAsync(string empresa, int? top);

        /// <summary>Slice 3: efectos incluidos en una remesa (apuntes de pago del extracto).</summary>
        Task<List<MovimientoRemesaDTO>> LeerMovimientosAsync(string empresa, int remesa);

        /// <summary>Slice 4: asientos de impagados agrupados (grid izquierdo de la pestaña Impagados).</summary>
        /// <param name="top">Número máximo de asientos (mismo criterio que las remesas).</param>
        Task<List<ImpagadoRemesaDTO>> LeerImpagadosAsync(string empresa, int? top);

        /// <summary>Slice 5: movimientos de un asiento de impagados (grid derecho).</summary>
        Task<List<MovimientoRemesaDTO>> LeerMovimientosImpagadoAsync(string empresa, int asiento);

        /// <summary>NestoAPI#332 (modo simulación): efectos candidatos a remesa SEPA, con
        /// preselección, motivo de retención (gating #172) y puerta de neteo.</summary>
        Task<List<EfectoCandidatoDTO>> LeerEfectosCandidatosSepaAsync(string empresa);

        /// <summary>NestoAPI#332 (slices 2-3): crea la remesa (numeración + alta + líneas de
        /// PreContabilidad + contabilización por el único call site). Revalida server-side.</summary>
        Task<CrearRemesaResponse> CrearRemesaAsync(CrearRemesaRequest peticion, string usuario);
    }
}
