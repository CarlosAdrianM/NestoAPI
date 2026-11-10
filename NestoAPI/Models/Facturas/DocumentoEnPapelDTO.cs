using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#828: un documento en papel (la factura física, el albarán físico o la nota de entrega en papel) de un pedido que sale
    /// en el packing, al hacer la etiqueta del envío. Va en la respuesta de la etiqueta de Ariadna (EtiquetaPackingDTO), en la de
    /// POST api/FacturacionRutas/FacturarEtiqueta (Nesto) y, en Facturar rutas, en la lista de lo que quedó pendiente en el packing.
    /// </summary>
    public class DocumentoEnPapelDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>El envío de la etiqueta (para localizar la caja); null si no se sabe.</summary>
        public int? Envio { get; set; }
        /// <summary>Uno de <see cref="TiposDocumentoEnPapel"/>.</summary>
        public string Tipo { get; set; }
        /// <summary>El número de la factura («NV2617986»), del albarán o, en una nota de entrega, el del pedido.</summary>
        public string Documento { get; set; }
        /// <summary>Uno de <see cref="EstadosDocumentoEnPapel"/>.</summary>
        public string Estado { get; set; }
        /// <summary>Dónde ha salido (o dónde se iba a imprimir).</summary>
        public string Impresora { get; set; }
        /// <summary>Por qué no ha salido (con <see cref="EstadosDocumentoEnPapel.PENDIENTE"/>).</summary>
        public string Mensaje { get; set; }
        /// <summary>Ya estaba impreso en el packing de antes: no se ha vuelto a imprimir.</summary>
        public bool YaEstabaImpreso { get; set; }
        /// <summary>Se ha vuelto a imprimir sin volver a facturar (estaba pendiente).</summary>
        public bool Reimpreso { get; set; }
        public DateTime? Fecha { get; set; }

        /// <summary>«la factura NV2617986», «el albarán 123456», «la nota de entrega del pedido 929244».</summary>
        public string Descripcion => TiposDocumentoEnPapel.Descripcion(Tipo, Documento);
    }

    /// <summary>NestoAPI#828: los tipos de documento en papel.</summary>
    public static class TiposDocumentoEnPapel
    {
        public const string FACTURA = "Factura";
        public const string ALBARAN = "Albaran";
        public const string NOTA_ENTREGA = "NotaEntrega";

        public static string Descripcion(string tipo, string documento)
        {
            string numero = documento?.Trim();
            switch (tipo)
            {
                case ALBARAN:
                    return $"el albarán {numero}";
                case NOTA_ENTREGA:
                    return $"la nota de entrega del pedido {numero}";
                default:
                    return $"la factura {numero}";
            }
        }
    }

    /// <summary>NestoAPI#828: el estado del documento en papel del packing (la marca de dbo.DocumentosPapelPacking).</summary>
    public static class EstadosDocumentoEnPapel
    {
        /// <summary>Ha salido por la impresora en el packing: Facturar rutas lo cuenta como «ya impreso en el packing».</summary>
        public const string IMPRESO = "Impreso";
        /// <summary>No ha salido (impresora caída, sin impresora…): repetir la etiqueta o «Reintentar impresión» lo vuelve a imprimir.</summary>
        public const string PENDIENTE = "Pendiente";
    }

    /// <summary>NestoAPI#828: desde dónde se ha hecho la etiqueta (quién imprime el papel).</summary>
    public static class AplicacionesDocumentoEnPapel
    {
        /// <summary>El servidor imprime (Facturacion:ImprimirDocumentosEnServidor, ImpresoraDocumentos del usuario o de «(defecto)»).</summary>
        public const string ARIADNA = "Ariadna";
        /// <summary>Nesto imprime en local y lo confirma (POST api/FacturacionRutas/DocumentosEnPapel/Impresion).</summary>
        public const string NESTO = "Nesto";
    }

    /// <summary>
    /// NestoAPI#828: el cuerpo de POST api/FacturacionRutas/FacturarEtiqueta (Nesto, «Facturar al imprimir etiqueta» de Agencias):
    /// factura el pedido del envío con el núcleo de la etiqueta del packing (el mismo que F8 en Ariadna) y devuelve los documentos en
    /// papel para imprimirlos en local.
    /// </summary>
    public class FacturarEtiquetaRequestDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        /// <summary>El envío de la etiqueta; opcional.</summary>
        public int? Envio { get; set; }
        /// <summary>Nesto#586 / NestoAPI#765: la nota de entrega sin packing ya se ha confirmado.</summary>
        public bool SaltarPacking { get; set; }
    }

    /// <summary>
    /// NestoAPI#828 (S3/S6): la respuesta de POST api/Almacen/Pedidos/{pedido}/DocumentosEnPapel/Reintentar («Reintentar impresión» del
    /// packing de Ariadna): lo que se ha vuelto a imprimir sin facturar y lo que sigue pendiente.
    /// </summary>
    public class ReintentoDocumentosEnPapelDTO
    {
        public int Pedido { get; set; }
        public System.Collections.Generic.List<DocumentoEnPapelDTO> DocumentosEnPapel { get; set; } = new System.Collections.Generic.List<DocumentoEnPapelDTO>();
        /// <summary>Sigue habiendo algo sin salir.</summary>
        public bool DocumentosPendientes { get; set; }
        /// <summary>«Meta en la caja la factura NV2617986 (en «HP LaserJet Algete»).»; null si no ha salido nada.</summary>
        public string MeterEnLaCaja { get; set; }
        public System.Collections.Generic.List<string> Avisos { get; set; } = new System.Collections.Generic.List<string>();
    }

    /// <summary>
    /// NestoAPI#828 (S5): la respuesta de POST api/FacturacionRutas/FacturarEtiqueta: la de FacturarPedido (facturas, albaranes, notas
    /// y errores, con los PDF que imprime Nesto) más los documentos en papel del packing y los avisos del núcleo.
    /// </summary>
    public class FacturarEtiquetaResponseDTO : FacturarRutasResponseDTO
    {
        /// <summary>Los documentos en papel (con su marca): Nesto imprime los que no estén impresos y confirma cada uno.</summary>
        public System.Collections.Generic.List<DocumentoEnPapelDTO> DocumentosEnPapel { get; set; } = new System.Collections.Generic.List<DocumentoEnPapelDTO>();
        /// <summary>Lo que dice el núcleo: la ruta propia no se factura aquí, la nota de reparto propio, lo ya impreso, fallos.</summary>
        public System.Collections.Generic.List<string> AvisosPapel { get; set; } = new System.Collections.Generic.List<string>();

        internal static FacturarEtiquetaResponseDTO De(Infraestructure.Facturas.ResultadoDocumentosEnPapel papel)
        {
            var respuesta = new FacturarEtiquetaResponseDTO();
            FacturarRutasResponseDTO junta = papel.Respuesta;
            respuesta.PedidosProcesados = junta.PedidosProcesados;
            respuesta.Facturas.AddRange(junta.Facturas);
            respuesta.Albaranes.AddRange(junta.Albaranes);
            respuesta.NotasEntrega.AddRange(junta.NotasEntrega);
            respuesta.PedidosConErrores.AddRange(junta.PedidosConErrores);
            respuesta.ReembolsosDesfasados.AddRange(junta.ReembolsosDesfasados);
            respuesta.CartasChequeRegalo.AddRange(junta.CartasChequeRegalo);
            respuesta.TiempoTotal = junta.TiempoTotal;
            respuesta.DocumentosEnPapel.AddRange(papel.Documentos);
            respuesta.AvisosPapel.AddRange(papel.Avisos);
            return respuesta;
        }
    }

    /// <summary>NestoAPI#828: el cuerpo de POST api/FacturacionRutas/DocumentosEnPapel/Impresion (Nesto, tras imprimir en local).</summary>
    public class ImpresionDocumentoEnPapelDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int? Envio { get; set; }
        public string Tipo { get; set; }
        public string Documento { get; set; }
        /// <summary>true: ha salido por la impresora; false: no (va como pendiente, con <see cref="Mensaje"/>).</summary>
        public bool Impreso { get; set; }
        public string Impresora { get; set; }
        public string Mensaje { get; set; }
    }
}
