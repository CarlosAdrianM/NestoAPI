using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static System.Net.WebRequestMethods;

namespace NestoAPI.Models
{
    public class ProductoDTO
    {
        public ProductoDTO()
        {
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
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
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
                    rutaImagen = "http://" + rutaImagen;
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
                System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
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

        public static async Task<decimal> LeerPrecioPublicoFinal(string producto)
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
                        string urlProducto = productNode.Attributes["xlink:href"].Value;

                        HttpResponseMessage responseProducto = await client.GetAsync(urlProducto).ConfigureAwait(false);

                        if (responseProducto.IsSuccessStatusCode)
                        {
                            string responseContent = await responseProducto.Content.ReadAsStringAsync().ConfigureAwait(false);
                            XmlDocument xmlDocProducto = new XmlDocument();
                            xmlDocProducto.LoadXml(responseContent);

                            XmlNode priceNode = xmlDocProducto.SelectSingleNode("//price");

                            if (priceNode != null)
                            {
                                decimal price = decimal.Parse(priceNode.InnerText);
                                precioPublico = Math.Round(price/1000000, 2, MidpointRounding.AwayFromZero);
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
                    throw ex;
                }

                return precioPublico;
            }
        }
    }    
}