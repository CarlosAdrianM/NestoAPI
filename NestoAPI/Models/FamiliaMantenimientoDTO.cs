namespace NestoAPI.Models
{
    /// <summary>
    /// NestoAPI#406: una familia en la pantalla de mantenimiento. Lo único editable es
    /// <see cref="PublicoIgualQueProfesional"/>; el resto viaja para identificarla.
    /// </summary>
    public class FamiliaMantenimientoDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public short Estado { get; set; }

        /// <summary>
        /// Esta familia se vende al público al MISMO precio que al profesional (sin el descuento
        /// del 30 %). Marcarla o desmarcarla cambia el precio de la web de todos sus productos.
        /// </summary>
        public bool PublicoIgualQueProfesional { get; set; }
    }
}
