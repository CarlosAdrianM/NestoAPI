using System;

namespace NestoAPI.Models.Facturas
{
    public class LineaFactura
    {
        public int Albaran { get; set; }
        public DateTime FechaAlbaran { get; set; }
        public string TextoAlbaran
        {
            get { 
                if (Albaran != 0)
                {
                    return String.Format("Albarán {0} del {1} (Pedido {2})", Albaran.ToString(), FechaAlbaran.ToString("dd/MM/yy"), Pedido.ToString());
                } else
                {
                    return String.Format("Pedido {0}", Pedido.ToString());
                }
                
            }
        }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public short? Tamanno { get; set; }
        public string UnidadMedida { get; set; }
        public string DescripcionCompleta
        {
            get { return String.Format("{0} {1} {2}", Descripcion, Tamanno, UnidadMedida).Trim(); }
        }
        public short? Cantidad { get; set; }
        public decimal? PrecioUnitario { get; set; }
        public decimal Descuento { get; set; }
        public decimal Importe { get; set; }
        public int Pedido { get; set; }
        public int Estado { get; set; }
        public int Picking { get; set; }
        public string UrlImagen { get; set; }

        // NestoAPI#196: contacto/dirección de entrega de la línea. Opcionales: solo se rellenan
        // en facturas multi-dirección (o mono-dirección que difiere de la cabecera) para imprimir
        // a qué dirección fue cada albarán. En facturas estándar quedan null y el PDF no cambia.
        public string Contacto { get; set; }
        public DireccionFactura DireccionEntrega { get; set; }
    }
}