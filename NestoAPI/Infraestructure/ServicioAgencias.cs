﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
                    decimal portes = await CalcularPortes(longitud, latitud, direccionFormateada);

                    RespuestaAgencia respuesta = new RespuestaAgencia
                    {
                        DireccionFormateada = direccionFormateada,
                        Longitud = longitud,
                        Latitud = latitud,
                        Coste = portes
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

        private async Task<decimal> CalcularPortes(double longitud, double latitud, string direccion)
        {
            // Create a New HttpClient object and dispose it when done, so the app doesn't leak resources
            using (HttpClient client = new HttpClient())
            {
                // Call asynchronous network methods in a try/catch block to handle exceptions
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                    string urlGlovo = "https://api.glovoapp.com/b2b/orders/estimate";
                    string apiKey = "154911396777150";
                    string apiSecret = "446241744d644ad8a06d84ce5e09fcd0";//ConfigurationManager.AppSettings["GoogleMapsApiKey"];
                    Address direccionOrigen = new Address
                    {
                        lat = 40.4204877,
                        lon = -3.7005278,
                        type = "PICKUP",
                        label = "Calle Reina, 5, 28004 Madrid, España"
                    };
                    Address direccionDestino = new Address
                    {
                        lat = latitud,
                        lon = longitud,
                        type = "DELIVERY",
                        label = direccion
                    };

                    EstimateOrder estimacion = new EstimateOrder
                    {
                        scheduleTime = null,
                        description = "Portes Pedido",
                        addresses = new List<Address>
                        {
                            direccionOrigen,
                            direccionDestino
                        }
                    };

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(
                        System.Text.ASCIIEncoding.ASCII.GetBytes(
                            string.Format("{0}:{1}", apiKey, apiSecret)
                            )));
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));//ACCEPT header

                    HttpContent content = new StringContent(JsonConvert.SerializeObject(estimacion), Encoding.UTF8, "application/json");
                    //HttpContent content = new StringContent(JsonConvert.SerializeObject(estimacion));
                    //content.Headers.ContentType = new MediaTypeWithQualityHeaderValue("application/json");

                    HttpResponseMessage response = await client.PostAsync(urlGlovo, content);
                    if (response.IsSuccessStatusCode)
                    {
                        var resultByte = await response.Content.ReadAsByteArrayAsync();
                        string result = System.Text.Encoding.UTF8.GetString(resultByte);
                        JObject resultJson = JsonConvert.DeserializeObject<JObject>(result);
                        string portes = resultJson["total"]["amount"].ToString();
                        decimal portesDecimal;
                        if (decimal.TryParse(portes,out portesDecimal))
                        {
                            return portesDecimal / 100M;
                        }
                    }
                    else
                    {
                        Console.WriteLine(string.Format("The request failed with status code: {0}", response.StatusCode));

                        // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                        Console.WriteLine(response.Headers.ToString());

                        string responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine(responseContent);
                    }
                }
                catch (Exception ex)
                {
                    return decimal.MaxValue;
                }
            }

            return decimal.MaxValue;
        }

        public DateTime HoraActual()
        {
            return new DateTime();
        }
    }

    class Address
    {
        public double lat { get; set; }
        public double lon { get; set; }
        public string type { get; set; }
        public string label { get; set; }
        public string details { get; set; }
        public string contactPhone { get; set; }
        public string contactPerson { get; set; }
    }

    class EstimateOrder
    {
        public int? scheduleTime { get; set; }
        public string description { get; set; }
        public List<Address> addresses { get; set; }
    }
}