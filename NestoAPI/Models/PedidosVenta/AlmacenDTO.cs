namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// DTO para representar un almacén en la lista de selección.
    /// Carlos 09/12/25: Issue #253/#52
    /// </summary>
    public class AlmacenDTO
    {
        public string Codigo { get; set; }
        public string Nombre { get; set; }

        /// <summary>
        /// Indica si el almacén es ficticio (no físico).
        /// Carlos 09/12/25: Para aplicar estilo diferente en el selector.
        /// </summary>
        public bool EsFicticio { get; set; }

        /// <summary>
        /// Indica si el almacén permite stock negativo.
        /// Carlos 09/12/25: Para aplicar estilo diferente en el selector.
        /// </summary>
        public bool PermiteNegativo { get; set; }

        /// <summary>
        /// Indica si es un almacén destacado (no ficticio y no permite negativo).
        /// Estos son los almacenes principales que se muestran con estilo destacado.
        /// </summary>
        public bool EsDestacado => !EsFicticio && !PermiteNegativo;
    }
}
