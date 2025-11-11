using System.Collections.Generic;

namespace NestoAPI.Models.Sincronizacion
{
    /// <summary>
    /// Mensaje de sincronización recibido/enviado a través de Google Pub/Sub
    /// Estructura PLANA que coincide con lo que emite GestorClientes.PublicarClienteSincronizar
    /// Topic: sincronizacion-tablas
    /// </summary>
    public class ExternalSyncMessageDTO
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
        public int Estado { get; set; }

        /// <summary>
        /// Lista de personas de contacto del cliente
        /// </summary>
        public List<PersonaContactoSyncDTO> PersonasContacto { get; set; }

        /// <summary>
        /// Tabla afectada: "Clientes", "Productos", etc.
        /// </summary>
        public string Tabla { get; set; }

        /// <summary>
        /// Sistema origen: "Nesto", "Odoo", "Prestashop", etc.
        /// </summary>
        public string Source { get; set; }
    }

    /// <summary>
    /// Datos de persona de contacto para sincronización
    /// </summary>
    public class PersonaContactoSyncDTO
    {
        /// <summary>
        /// Número (ID) de la persona de contacto
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Nombre de la persona de contacto
        /// </summary>
        public string Nombre { get; set; }

        /// <summary>
        /// Correo electrónico
        /// </summary>
        public string CorreoElectronico { get; set; }

        /// <summary>
        /// Teléfonos
        /// </summary>
        public string Telefonos { get; set; }

        /// <summary>
        /// Cargo de la persona (código numérico)
        /// </summary>
        public int? Cargo { get; set; }
    }
}
