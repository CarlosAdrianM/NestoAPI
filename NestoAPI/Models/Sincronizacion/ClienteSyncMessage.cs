using System.Collections.Generic;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Mensaje de sincronización específico para Clientes
    /// Contiene solo los campos relevantes para la entidad Cliente
    /// </summary>
    public class ClienteSyncMessage : SyncMessageBase
    {
        /// <summary>
        /// NIF/CIF del cliente
        /// </summary>
        public string Nif { get; set; }

        /// <summary>
        /// Nº_Cliente en Nesto
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Contacto en Nesto
        /// </summary>
        public string Contacto { get; set; }

        /// <summary>
        /// Indica si es el cliente principal
        /// </summary>
        public bool ClientePrincipal { get; set; }

        /// <summary>
        /// Nombre del cliente
        /// </summary>
        public string Nombre { get; set; }

        /// <summary>
        /// Dirección del cliente
        /// </summary>
        public string Direccion { get; set; }

        /// <summary>
        /// Código postal
        /// </summary>
        public string CodigoPostal { get; set; }

        /// <summary>
        /// Población
        /// </summary>
        public string Poblacion { get; set; }

        /// <summary>
        /// Provincia
        /// </summary>
        public string Provincia { get; set; }

        /// <summary>
        /// Teléfono
        /// </summary>
        public string Telefono { get; set; }

        /// <summary>
        /// Comentarios
        /// </summary>
        public string Comentarios { get; set; }

        /// <summary>
        /// Vendedor asignado
        /// </summary>
        public string Vendedor { get; set; }

        /// <summary>
        /// Estado del cliente
        /// </summary>
        public short? Estado { get; set; }

        /// <summary>
        /// Lista de personas de contacto del cliente
        /// </summary>
        public List<PersonaContactoSyncDTO> PersonasContacto { get; set; }
    }
}
