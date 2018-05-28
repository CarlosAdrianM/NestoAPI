using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    
}