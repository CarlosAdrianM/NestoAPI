using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// DTO con información de un tipo de ruta para mostrar en UI
    /// </summary>
    public class TipoRutaInfoDTO
    {
        /// <summary>
        /// Identificador único del tipo de ruta (ej: "PROPIA", "AGENCIA")
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Nombre amigable para mostrar en la interfaz (ej: "Ruta propia")
        /// </summary>
        public string NombreParaMostrar { get; set; }

        /// <summary>
        /// Descripción del tipo de ruta
        /// </summary>
        public string Descripcion { get; set; }

        /// <summary>
        /// Lista de códigos de ruta que pertenecen a este tipo (ej: ["16", "AT"])
        /// </summary>
        public List<string> RutasContenidas { get; set; }
    }
}
