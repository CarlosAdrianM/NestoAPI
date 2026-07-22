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

        /// <summary>Slice 6: genera el fichero SEPA ISO 20022 de una remesa (único call site del
        /// SP prdCrearRemesaIso20022). Devuelve el XML completo como texto.</summary>
        Task<string> CrearFicheroRemesaAsync(int remesa, string codigo, System.DateTime fechaCobro);

        /// <summary>Slice 7: contabiliza las devoluciones de un fichero SEPA de impagados (único
        /// call site del SP prdContabilizarImpagadosSepa).</summary>
        Task ContabilizarImpagadosAsync(string fichero);

        /// <summary>Slice 8: datos para crear las tareas de Planner de un asiento de impagados
        /// (efectos con los datos del cliente, sin los apuntes de gastos).</summary>
        Task<List<TareaImpagadoDTO>> LeerTareasImpagadoAsync(string empresa, int asiento);
    }
}
