using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;

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
        public short Estado { get; set; }
        public string Grupo { get; set; }
        public string Subgrupo { get; set; }
        public string UrlFoto { get; set; }
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
                HttpResponseMessage response = await client.GetAsync("?producto=" + productoStock);
                string rutaImagen = "";
                if (response.IsSuccessStatusCode)
                {
                    rutaImagen = await response.Content.ReadAsStringAsync();
                    rutaImagen = "http://" + rutaImagen;
                }
                return rutaImagen;
            }
        }
    }

    
}