using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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
        /// <para>NestoAPI#504: contrato con Odoo (odoo-custom-addons#25): propiedad AUSENTE = no tocar
        /// el vendedor; "" = sin vendedor. Por eso null no se serializa: si viajara, Odoo lo leería
        /// como "quítale el comercial". Ver <c>GestorClientes.VendedorEmailParaPublicar</c>.</para>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
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

        // NestoAPI#623 corte 4 (odoo-custom-addons#43): origen del cliente (campaña) y consentimiento de comunicaciones
        // comerciales del lead. Solo viajan cuando se conocen (hoy, al importar un lead de Meta): propiedad AUSENTE = no tocar.

        /// <summary>Código del catálogo de orígenes (p. ej. META-HARMONIA-0926). Ausente = no tocar.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Origen { get; set; }

        /// <summary>Cuándo llegó el lead (hora de España). Ausente = no tocar.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? FechaOrigen { get; set; }

        /// <summary>Acepta recibir correos comerciales (casilla del formulario). Ausente = no se sabe / no tocar.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? AceptaComunicaciones { get; set; }

        // Embudo de Odoo con clientes del vendedor general (odoo-custom-addons#45): propiedad AUSENTE = no tocar.

        /// <summary>Vendedor de peluquería del contacto (VendedoresClienteGrupoProducto, grupo PEL; "NV" si es el general).
        /// Ausente si el contacto no tiene fila.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string VendedorPeluqueria { get; set; }

        /// <summary>Bloque del embudo (p. ej. NV-2026-11-01). SOLO en la publicación que sale de la republicación del embudo
        /// (POST api/Clientes/Sync/Republicar); una modificación posterior de la ficha viaja sin él.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string BloqueEmbudo { get; set; }

        /// <summary>Correo del responsable del embudo para este contacto (por su código postal). Solo junto a <see cref="BloqueEmbudo"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ResponsableEmbudoEmail { get; set; }

        /// <summary>"EquipoAlberto" | "Vendedor" | "TiendaOnline". Solo junto a <see cref="BloqueEmbudo"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ResponsableEmbudoTipo { get; set; }

        /// <summary>
        /// Lista de personas de contacto del cliente
        /// </summary>
        public List<PersonaContactoSyncDTO> PersonasContacto { get; set; }
    }
}
