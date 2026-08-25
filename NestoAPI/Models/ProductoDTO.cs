using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace NestoAPI.Models
{
    public class ProductoDTO
    {
        public ProductoDTO()
        {
            ProductosKit = new List<ProductoKit>();
            Stocks = new List<StockProducto>();
        }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? Tamanno { get; set; }
        public string UnidadMedida { get; set; }
        public string Familia { get; set; }
        public decimal PrecioProfesional { get; set; }
        public decimal PrecioPublicoFinal { get; set; }
        public short Estado { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string UrlEnlace { get; set; }
        public string UrlFoto { get; set; }
        public bool RoturaStockProveedor { get; set; }
        public int ClasificacionMasVendidos { get; set; }
        public string CodigoBarras { get; set; }

        public ICollection<ProductoKit> ProductosKit { get; set; }
        public ICollection<StockProducto> Stocks { get; set; }

        public class StockProducto
        {
            public string Almacen { get; set; }
            public int Stock { get; set; }
            public int PendienteEntregar { get; set; }
            public int PendienteRecibir { get; set; }
            public int CantidadDisponible
            {
                get
                {
                    int cantidad = Stock - PendienteEntregar + PendienteReposicion;
                    return cantidad > 0 ? cantidad : 0;
                }
            }
            public DateTime FechaEstimadaRecepcion { get; set; }
            public int PendienteReposicion { get; set; }
        }

        public static async Task<string> RutaImagen(string productoStock)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/imagenesPorReferencia.php");
                client.DefaultRequestHeaders.Accept.Clear();
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                try
                {
                    string parametros = "?producto=" + productoStock;
                    HttpResponseMessage response = await client.GetAsync(parametros);

                    string rutaImagen = "";
                    if (response.IsSuccessStatusCode)
                    {
                        rutaImagen = await response.Content.ReadAsStringAsync();
                        rutaImagen = "https://" + rutaImagen;
                    }

                    return rutaImagen;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static async Task<string> RutaEnlace(string producto)
        {
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/enlacePorReferencia.php");
                client.DefaultRequestHeaders.Accept.Clear();
                try
                {
                    string parametros = "?producto=" + producto;
                    HttpResponseMessage response = await client.GetAsync(parametros).ConfigureAwait(false);


                    string rutaEnlace = string.Empty;
                    if (response.IsSuccessStatusCode)
                    {
                        rutaEnlace = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        rutaEnlace += "?utm_source=nuevavision&utm_campaign=nesto";
                    }

                    return rutaEnlace;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static async Task<decimal> LeerPrecioPublicoFinal(string producto, NVEntities db)
        {
            // Issue #104: Consultar primero PrestashopProductos en BBDD
            var prestashopProducto = await db.PrestashopProductos
                .FirstOrDefaultAsync(pp => pp.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && pp.Número == producto)
                .ConfigureAwait(false);

            decimal? pvpIvaIncluido = prestashopProducto?.PVP_IVA_Incluido;

            // El sentinel necesita el PVP y el IVA de la ficha; solo se leen si hace falta.
            Producto fichaProducto = pvpIvaIncluido == Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL
                ? await db.Productos
                    .FirstOrDefaultAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto)
                    .ConfigureAwait(false)
                : null;

            decimal? resuelto = ResolverPrecioPublicoFinal(
                pvpIvaIncluido, fichaProducto?.PVP ?? 0, fichaProducto?.IVA_Repercutido);
            if (resuelto.HasValue)
            {
                return resuelto.Value;
            }

            // Fallback: llamar a la API de Prestashop
            return await LeerPrecioPublicoFinalDesdePrestashop(producto).ConfigureAwait(false);
        }

        /// <summary>
        /// Decide el precio público a partir del campo <c>PVP_IVA_Incluido</c> de PrestashopProductos.
        /// Devuelve <c>null</c> cuando hay que preguntárselo a PrestaShop.
        ///
        /// Los tres modos que emite el módulo de PrestaShop (ver
        /// <see cref="Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL"/>):
        ///
        ///   · positivo → precio público con IVA, tal cual
        ///   · -1       → público = profesional: se calcula aquí como PVP + IVA
        ///   · NULL     → el público lleva el 30 % de descuento por defecto; esa regla vive SOLO en
        ///                el módulo, así que se pregunta a PrestaShop y NO se replica el 30 % aquí
        ///
        /// El -1 se calcula en local, en vez de dejarlo caer al fallback, porque
        /// <c>LeerPrecioPublicoFinalDesdePrestashop</c> devuelve 0 si la tienda no responde, y un 0
        /// acaba siendo un precio de venta de 0 € para el cliente PUBLICO_FINAL. "Público =
        /// profesional" no tiene ningún parámetro que mantener, así que calcularlo aquí no duplica
        /// ninguna regla de negocio: el IVA ya es de NestoAPI.
        ///
        /// Cualquier otro valor que no sea un precio (0, o un negativo distinto del sentinel) cae
        /// también al fallback: antes bastaba con ser distinto de 0 para viajar como precio.
        /// </summary>
        internal static decimal? ResolverPrecioPublicoFinal(decimal? pvpIvaIncluido, decimal pvp, string ivaRepercutido)
        {
            if (pvpIvaIncluido > 0)
            {
                return pvpIvaIncluido.Value;
            }

            if (pvpIvaIncluido == Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL)
            {
                return Math.Round(pvp * FactorIva(ivaRepercutido), 2, MidpointRounding.AwayFromZero);
            }

            return null;
        }

        /// <summary>Multiplicador para pasar de base imponible a precio con IVA.</summary>
        internal static decimal FactorIva(string ivaRepercutido)
        {
            return ivaRepercutido?.Trim() == Constantes.Empresas.IVA_REDUCIDO ? 1.10M : 1.21M;
        }

        private static async Task<decimal> LeerPrecioPublicoFinalDesdePrestashop(string producto)
        {
            string urlPrestashop = $"http://www.productosdeesteticaypeluqueriaprofesional.com/api/products?filter[reference]={producto}";
            decimal precioPublico = 0;
            string userName;
            try
            {
                userName = ConfigurationManager.AppSettings["PrestashopWebserviceKeyNV"];
            }
            catch
            {
                return precioPublico;
            }

            using (var handler = new HttpClientHandler { Credentials = new NetworkCredential { UserName = userName } })
            using (HttpClient client = new HttpClient(handler))
            using (HttpResponseMessage response = await client.GetAsync(urlPrestashop))
            using (HttpContent content = response.Content)
            {
                try
                {
                    string xmlResponse = await content.ReadAsStringAsync();

                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlResponse);

                    XmlNode productNode = xmlDoc.SelectSingleNode("//product");

                    if (productNode != null)
                    {
                        string urlProducto = $"{productNode.Attributes["xlink:href"].Value}?price[final_price][use_tax]=1";
                        HttpResponseMessage responseProducto = await client.GetAsync(urlProducto).ConfigureAwait(false);

                        if (responseProducto.IsSuccessStatusCode)
                        {
                            string responseContent = await responseProducto.Content.ReadAsStringAsync().ConfigureAwait(false);
                            XmlDocument xmlDocProducto = new XmlDocument();
                            xmlDocProducto.LoadXml(responseContent);

                            XmlNode priceNode = xmlDocProducto.SelectSingleNode("//final_price");
                            if (priceNode != null)
                            {
                                if (decimal.TryParse(priceNode.InnerText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                                {
                                    precioPublico = Math.Round(price, 2, MidpointRounding.AwayFromZero);
                                }
                            }
                            else
                            {
                                Console.WriteLine("No se encontró el precio en el XML");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Error al hacer la solicitud");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return 0;
                }

                return precioPublico;
            }
        }

        /// <summary>
        /// Obtiene la URL del producto en la tienda online usando la API de Prestashop.
        /// Busca el producto por su referencia y construye la URL amigable.
        /// Issue #74: Sistema de correos post-compra.
        /// </summary>
        /// <param name="producto">Referencia del producto (ej: "12345")</param>
        /// <returns>URL completa del producto en la tienda o null si no existe</returns>
        public static async Task<string> LeerUrlTiendaOnline(string producto)
        {
            if (string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }

            string urlPrestashop = $"http://www.productosdeesteticaypeluqueriaprofesional.com/api/products?filter[reference]={producto.Trim()}";
            string userName;

            try
            {
                userName = ConfigurationManager.AppSettings["PrestashopWebserviceKeyNV"];
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrEmpty(userName))
            {
                return null;
            }

            try
            {
                using (var handler = new HttpClientHandler { Credentials = new NetworkCredential { UserName = userName } })
                using (HttpClient client = new HttpClient(handler))
                {
                    // 1. Buscar el producto por referencia
                    HttpResponseMessage response = await client.GetAsync(urlPrestashop).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    string xmlResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    XmlDocument xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlResponse);

                    XmlNode productNode = xmlDoc.SelectSingleNode("//product");
                    if (productNode == null)
                    {
                        return null;
                    }

                    // 2. Obtener detalles del producto para construir la URL
                    string urlProductoApi = productNode.Attributes["xlink:href"]?.Value;
                    if (string.IsNullOrEmpty(urlProductoApi))
                    {
                        return null;
                    }

                    HttpResponseMessage responseProducto = await client.GetAsync(urlProductoApi).ConfigureAwait(false);
                    if (!responseProducto.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    string xmlProducto = await responseProducto.Content.ReadAsStringAsync().ConfigureAwait(false);
                    XmlDocument xmlDocProducto = new XmlDocument();
                    xmlDocProducto.LoadXml(xmlProducto);

                    // 3. Extraer id y link_rewrite para construir la URL amigable
                    XmlNode idNode = xmlDocProducto.SelectSingleNode("//product/id");
                    XmlNode linkRewriteNode = xmlDocProducto.SelectSingleNode("//product/link_rewrite/language");

                    if (idNode == null || linkRewriteNode == null)
                    {
                        return null;
                    }

                    string idProducto = idNode.InnerText;
                    string linkRewrite = linkRewriteNode.InnerText;

                    // 4. Construir la URL amigable con parámetros UTM
                    string urlTienda = $"https://www.productosdeesteticaypeluqueriaprofesional.com/{idProducto}-{linkRewrite}.html";
                    urlTienda += "?utm_source=nuevavision&utm_medium=email&utm_campaign=postcompra";

                    return urlTienda;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    public class ProductoKit
    {
        public string ProductoId { get; set; }
        public int Cantidad { get; set; }
    }

    public class SubgrupoProductoDTO
    {
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string Nombre { get; set; }
        public string GrupoSubgrupo => Grupo + Subgrupo;
    }
}