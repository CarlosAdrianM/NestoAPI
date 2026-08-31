using System;

namespace NestoAPI.Models.Agencias
{
    /// <summary>
    /// Nesto#340 (slice A3): una fila del historial de cambios de un envío, tal y como la pinta la
    /// pestaña de Agencias. Sustituye a la entidad EF <c>EnviosHistoria</c>, que hasta ahora Nesto
    /// leía con su propio DbContext.
    ///
    /// Es de solo lectura: las ESCRITURAS del historial siguen en el ViewModel de Nesto, dentro de
    /// las transacciones de contabilización de reembolsos (los <c>prd*</c>), y se migrarán con
    /// ellas. Este DTO no las cubre.
    /// </summary>
    public class EnvioHistoriaDTO
    {
        public int Numero { get; set; }
        public int NumeroEnvio { get; set; }

        /// <summary>Qué campo del envío se cambió: "Reembolso", "Estado", "Retorno"...</summary>
        public string Campo { get; set; }

        public string ValorAnterior { get; set; }
        public string Observaciones { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaModificacion { get; set; }
    }
}
