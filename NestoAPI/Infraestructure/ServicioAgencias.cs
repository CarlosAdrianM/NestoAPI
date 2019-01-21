using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure
{
    public class ServicioAgencias : IServicioAgencias
    {
        public string LeerCodigoPostal(PedidoVentaDTO pedido)
        {
            NVEntities db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            Cliente cliente = db.Clientes.Single(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            string codigoPostal = cliente.CodPostal != null ? cliente.CodPostal.Trim() : "";

            return codigoPostal;
        }

        public async Task<RespuestaAgencia> LeerDireccionGoogleMaps(PedidoVentaDTO pedido)
        {
            // Create a New HttpClient object and dispose it when done, so the app doesn't leak resources
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    NVEntities db = new NVEntities();
                    var direccion = db.Clientes.Single(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
                    string urlGoogleMaps = "https://maps.googleapis.com/maps/api/geocode/json?address=";
                    urlGoogleMaps += ProcesarDireccion(direccion);
                    string clave = ConfigurationManager.AppSettings["GoogleMapsApiKey"];
                    urlGoogleMaps += "&key=" + clave;
                    //HttpResponseMessage response = await client.GetAsync(urlGoogleMaps);
                    //response.EnsureSuccessStatusCode();
                    //string responseBody = await response.Content.ReadAsStringAsync();
                    // Above three lines can be replaced with new helper method below
                    string responseBody = await client.GetStringAsync(urlGoogleMaps);
                    JObject respuestaJson = JsonConvert.DeserializeObject<JObject>(responseBody);
                    string direccionFormateada = respuestaJson["results"][0]["formatted_address"].ToString();
                    double longitud = double.Parse(respuestaJson["results"][0]["geometry"]["location"]["lng"].ToString());
                    double latitud = double.Parse(respuestaJson["results"][0]["geometry"]["location"]["lat"].ToString());

                    // Aquí hay que llamar a POST /b2b/orders/estimate para calcular los portes reales

                    RespuestaAgencia respuesta = new RespuestaAgencia
                    {
                        DireccionFormateada = direccionFormateada,
                        Longitud = longitud,
                        Latitud = latitud,
                        Coste = 9
                    };

                    return respuesta;
                }
                catch (HttpRequestException)
                {
                    return null;
                }
            }
        }

        private string ProcesarDireccion(Cliente cliente)
        {
            string respuesta = cliente.Dirección + "+";
            respuesta += cliente.CodPostal + "+";
            respuesta += cliente.Población + "+";
            respuesta += cliente.Provincia + "+";
            respuesta += "España";
            respuesta = respuesta.Replace(" ", "+");
            return respuesta;
        }
    }
}