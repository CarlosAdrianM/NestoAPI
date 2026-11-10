namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Clase base abstracta para todos los documentos creados durante la facturación de rutas.
    /// Contiene propiedades comunes a todos los tipos de documentos.
    /// </summary>
    public abstract class DocumentoCreadoDTO
    {
        /// <summary>
        /// Empresa a la que pertenece el documento
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Número del pedido original
        /// </summary>
        public int NumeroPedido { get; set; }

        /// <summary>
        /// Código del cliente
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Contacto del cliente
        /// </summary>
        public string Contacto { get; set; }

        /// <summary>
        /// Nombre del cliente
        /// </summary>
        public string NombreCliente { get; set; }

        /// <summary>
        /// NestoAPI#828 (S4): en Facturar rutas, el envío del pedido (el último) de los documentos que salen en papel, para localizar
        /// la caja. Null si no tiene envío o no se ha mirado.
        /// </summary>
        public int? Envio { get; set; }
    }
}
