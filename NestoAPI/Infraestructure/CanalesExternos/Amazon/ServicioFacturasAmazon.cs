using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    public interface IServicioFacturasAmazon
    {
        /// <summary>Factura el pedido si no lo está y sube el PDF de la factura a Amazon
        /// (feed UPLOAD_VAT_INVOICE). Idempotente: resubir reemplaza la factura en Amazon.</summary>
        Task<SubirFacturaAmazonResponseDTO> FacturarYSubirAsync(string empresa, int pedido, string usuario);

        /// <summary>Estado de subida de los pedidos indicados (para el grid de Nesto).</summary>
        IReadOnlyList<FacturaSubidaAmazonDTO> ConsultarSubidas(string empresa, IReadOnlyCollection<int> pedidos);
    }

    /// <summary>
    /// NestoAPI#366: orquestación de "facturar y subir la factura a Amazon". El núcleo vive aquí
    /// (y no en Nesto) siguiendo la estrategia de migración: facturación, PDF y credenciales ya
    /// están en la API, y las llamadas SP-API solo necesitan el token LWA.
    /// </summary>
    public class ServicioFacturasAmazon : IServicioFacturasAmazon
    {
        internal const string FEED_TYPE_FACTURAS = "UPLOAD_VAT_INVOICE";
        internal const string CONTENT_TYPE_PDF = "application/pdf";

        /// <summary>
        /// Marketplaces donde aplica la subida de facturas (tiendas europeas, según la guía del
        /// Invoice Uploader). TR y AE quedan fuera a propósito: Amazon no admite el feed allí.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> MarketplacesSoportados = new Dictionary<string, string>
        {
            ["A1RKKUPIHCS9HS"] = "Amazon.es",
            ["A1PA6795UKMFR9"] = "Amazon.de",
            ["A13V1IB3VIYZZH"] = "Amazon.fr",
            ["APJ6JRA9NG5V4"] = "Amazon.it",
            ["A1F83G8C2ARO7P"] = "Amazon.co.uk",
            ["A1805IZSGTT6HS"] = "Amazon.nl",
            ["A2NODRKZP88ZB9"] = "Amazon.se",
            ["A1C3SOZRARQ6R3"] = "Amazon.pl",
            ["AMEN7PMS3EDWL"] = "Amazon.com.be",
            ["A28R8C7NBKEWEA"] = "Amazon.ie"
        };

        private static readonly Regex _regexAmazonOrderId = new Regex(@"\b\d{3}-\d{7}-\d{7}\b", RegexOptions.Compiled);

        private readonly NVEntities _db;
        private readonly IGestorFacturas _gestorFacturas;
        private readonly IAmazonFeedsGateway _gateway;
        private readonly IAlmacenFacturasAmazon _almacen;
        private readonly AlbaranesVenta.IServicioAlbaranesVenta _servicioAlbaranes;

        public ServicioFacturasAmazon(NVEntities db, IGestorFacturas gestorFacturas,
            IAmazonFeedsGateway gateway, IAlmacenFacturasAmazon almacen,
            AlbaranesVenta.IServicioAlbaranesVenta servicioAlbaranes)
        {
            _db = db;
            _gestorFacturas = gestorFacturas;
            _gateway = gateway;
            _almacen = almacen;
            _servicioAlbaranes = servicioAlbaranes;
        }

        public async Task<SubirFacturaAmazonResponseDTO> FacturarYSubirAsync(string empresa, int pedido, string usuario)
        {
            CabPedidoVta cabecera = _db.CabPedidoVtas.SingleOrDefault(p => p.Empresa == empresa && p.Número == pedido)
                ?? throw new InvalidOperationException($"No existe el pedido {pedido} de la empresa {empresa}.");

            // Los clientes ficticios de consumidor final (Amazon 32624, tienda online, público
            // final) generan factura SIMPLIFICADA (F2, sin datos del comprador): no se sube a
            // Amazon. Solo se suben facturas completas, es decir, pedidos facturados a un cliente
            // real (el comprador pidió factura y se cambió el "Cliente al que se factura").
            if (Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(cabecera.Nº_Cliente))
            {
                throw new InvalidOperationException(
                    $"El pedido {pedido} factura al cliente {cabecera.Nº_Cliente?.Trim()} (factura simplificada, " +
                    "sin datos del comprador) y no se sube a Amazon. Si el comprador pide factura, cambia el " +
                    "cliente del pedido a un cliente real y vuelve a intentarlo.");
            }

            string amazonOrderId = ExtraerAmazonOrderId(cabecera.Comentarios)
                ?? throw new InvalidOperationException(
                    $"El pedido {pedido} no tiene AmazonOrderId en los comentarios; no parece un pedido de Amazon.");

            // El marketplace real del pedido no se persiste en Nesto (DatosMarkets es una lista del
            // cliente), así que se resuelve preguntando a Amazon. De paso valida que el pedido exista.
            AmazonPedidoInfo pedidoAmazon = await _gateway.ObtenerPedidoAsync(amazonOrderId).ConfigureAwait(false);
            if (!MarketplacesSoportados.ContainsKey(pedidoAmazon.MarketplaceId ?? string.Empty))
            {
                throw new InvalidOperationException(
                    $"El marketplace {pedidoAmazon.SalesChannel ?? pedidoAmazon.MarketplaceId} del pedido {amazonOrderId} " +
                    "no admite la subida de facturas (solo tiendas europeas).");
            }

            var respuesta = new SubirFacturaAmazonResponseDTO
            {
                Empresa = empresa?.Trim(),
                Pedido = pedido,
                AmazonOrderId = amazonOrderId,
                MarketplaceId = pedidoAmazon.MarketplaceId
            };

            // Factura del pedido: la existente (líneas en estado FACTURA) o la que se crea ahora.
            string empresaFactura = empresa;
            List<string> facturas = _db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == pedido
                    && l.Estado == Constantes.EstadosLineaVenta.FACTURA && l.Nº_Factura != null)
                .Select(l => l.Nº_Factura)
                .Distinct()
                .ToList();
            if (facturas.Count > 1)
            {
                throw new InvalidOperationException(
                    $"El pedido {pedido} tiene {facturas.Count} facturas distintas; hay que subirlas a mano.");
            }
            if (facturas.Count == 1)
            {
                respuesta.NumeroFactura = facturas[0].Trim();
            }
            else
            {
                // Nesto#434: los pedidos FBA (almacén AMZ) no pasan por picking ni por rutas, así
                // que puede que nadie los haya albaraneado: sin líneas en estado ALBARÁN,
                // prdCrearFacturaVta responde "No hay líneas para facturar". Se albaranea aquí con
                // la fecha de entrega de las líneas (puede ser posterior a hoy y la fecha por
                // defecto las dejaría fuera).
                bool hayLineasEnAlbaran = _db.LinPedidoVtas.Any(l => l.Empresa == empresa && l.Número == pedido
                    && l.Estado == Constantes.EstadosLineaVenta.ALBARAN);
                if (!hayLineasEnAlbaran)
                {
                    DateTime? fechaEntrega = _db.LinPedidoVtas
                        .Where(l => l.Empresa == empresa && l.Número == pedido && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                        .Max(l => (DateTime?)l.Fecha_Entrega);
                    _ = await _servicioAlbaranes.CrearAlbaran(empresa, pedido, usuario, fechaEntrega).ConfigureAwait(false);
                }

                CrearFacturaResponseDTO creada = await _gestorFacturas.CrearFactura(empresa, pedido, usuario, usuario).ConfigureAwait(false);
                respuesta.NumeroFactura = creada.NumeroFactura;
                empresaFactura = creada.Empresa ?? empresa;
                respuesta.Avisos.AddRange(creada.Avisos ?? new List<string>());
            }

            byte[] pdf = await GenerarPdfFactura(empresaFactura, respuesta.NumeroFactura, usuario).ConfigureAwait(false);

            AmazonFeedDocumento documento = await _gateway.CrearDocumentoFeedAsync(CONTENT_TYPE_PDF).ConfigureAwait(false);
            await _gateway.SubirDocumentoAsync(documento.Url, pdf, CONTENT_TYPE_PDF).ConfigureAwait(false);
            respuesta.FeedId = await _gateway.CrearFeedAsync(FEED_TYPE_FACTURAS, pedidoAmazon.MarketplaceId,
                documento.FeedDocumentId, ConstruirFeedOptions(amazonOrderId, respuesta.NumeroFactura)).ConfigureAwait(false);
            respuesta.Estado = EstadosFacturaAmazon.ENVIADA;

            _almacen.Registrar(new AmazonFacturaSubida
            {
                Empresa = respuesta.Empresa,
                Pedido = pedido,
                NumeroFactura = respuesta.NumeroFactura,
                AmazonOrderId = amazonOrderId,
                MarketplaceId = pedidoAmazon.MarketplaceId,
                FeedId = respuesta.FeedId,
                Estado = EstadosFacturaAmazon.ENVIADA,
                Usuario = usuario
            });

            return respuesta;
        }

        public IReadOnlyList<FacturaSubidaAmazonDTO> ConsultarSubidas(string empresa, IReadOnlyCollection<int> pedidos)
        {
            List<FacturaSubidaAmazonDTO> resultado = _almacen.ObtenerVarias(empresa, pedidos)
                .Select(f => new FacturaSubidaAmazonDTO
                {
                    Pedido = f.Pedido,
                    NumeroFactura = f.NumeroFactura?.Trim(),
                    Estado = f.Estado?.Trim(),
                    FechaEnvio = f.FechaEnvio
                })
                .ToList();

            // Los pedidos de clientes de factura simplificada se devuelven como OMITIDA para que
            // el grid los pinte como "no se sube" y el lote de pendientes no los intente.
            var conEstado = new HashSet<int>(resultado.Select(r => r.Pedido));
            List<int> sinEstado = (pedidos ?? new List<int>()).Where(p => !conEstado.Contains(p)).ToList();
            if (sinEstado.Count > 0)
            {
                var clientes = _db.CabPedidoVtas
                    .Where(p => p.Empresa == empresa && sinEstado.Contains(p.Número))
                    .Select(p => new { p.Número, p.Nº_Cliente })
                    .ToList();
                resultado.AddRange(clientes
                    .Where(p => Constantes.ClientesEspeciales.EsClienteFacturaSimplificada(p.Nº_Cliente))
                    .Select(p => new FacturaSubidaAmazonDTO
                    {
                        Pedido = p.Número,
                        Estado = EstadosFacturaAmazon.OMITIDA
                    }));
            }
            return resultado;
        }

        private async Task<byte[]> GenerarPdfFactura(string empresa, string numeroFactura, string usuario)
        {
            List<Factura> facturas = _gestorFacturas.LeerFacturas(new List<FacturaLookup>
            {
                new FacturaLookup { Empresa = empresa, Factura = numeroFactura }
            });
            System.Net.Http.ByteArrayContent contenido = _gestorFacturas.FacturasEnPDF(facturas, false, usuario, false);
            return await contenido.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// feedOptions del UPLOAD_VAT_INVOICE. Solo OrderId + InvoiceNumber + DocumentType: los
        /// importes (TotalAmount/TotalVATAmount) son exclusivos de vendedores acogidos a VCS (no es
        /// nuestro caso) y si se envían deben cuadrar al céntimo con el cálculo de Amazon, así que
        /// se omiten a propósito (y de paso desaparece el problema de divisas no EUR).
        /// </summary>
        internal static IReadOnlyDictionary<string, string> ConstruirFeedOptions(string amazonOrderId, string numeroFactura)
        {
            return new Dictionary<string, string>
            {
                ["metadata:OrderId"] = amazonOrderId,
                ["metadata:InvoiceNumber"] = numeroFactura,
                ["metadata:DocumentType"] = "Invoice"
            };
        }

        /// <summary>
        /// Extrae el AmazonOrderId (formato 123-4567890-1234567) del texto indicado (normalmente los
        /// comentarios del pedido, donde puede llevar el prefijo "FBA "). Null si no hay ninguno.
        /// </summary>
        public static string ExtraerAmazonOrderId(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            Match coincidencia = _regexAmazonOrderId.Match(texto);
            return coincidencia.Success ? coincidencia.Value : null;
        }
    }
}
