using System;
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
        /// Vendedor asignado (código)
        /// </summary>
        public string Vendedor { get; set; }

        /// <summary>
        /// Email del vendedor asignado
        /// </summary>
        public string VendedorEmail { get; set; }

        /// <summary>
        /// Estado del cliente
        /// </summary>
        public short? Estado { get; set; }

        // NestoAPI#498: fechas de compras de TODO el cliente (iguales en todos sus contactos),
        // calculadas en CalculoFechasComprasCliente. Solo viajan de Nesto a Odoo
        // (odoo-custom-addons#8): Odoo no las devuelve y Nesto no las lee al recibir.

        /// <summary>
        /// Fecha (de cabecera) del primer pedido con alguna línea en presupuesto; null si no hay
        /// </summary>
        public DateTime? FechaPrimerPresupuesto { get; set; }

        /// <summary>
        /// Fecha (de cabecera) del primer pedido real (línea en estado -1 o superior; la nota de
        /// entrega no cuenta); null si no hay
        /// </summary>
        public DateTime? FechaPrimerPedido { get; set; }

        /// <summary>
        /// Fecha (de cabecera) del último pedido real; null si no hay
        /// </summary>
        public DateTime? FechaUltimoPedido { get; set; }

        /// <summary>
        /// Lista de personas de contacto del cliente
        /// </summary>
        public List<PersonaContactoSyncDTO> PersonasContacto { get; set; }
    }
}
