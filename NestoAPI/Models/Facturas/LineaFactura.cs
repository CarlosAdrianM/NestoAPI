using System;

namespace NestoAPI.Models.Facturas
{
    public class LineaFactura
    {
        public int Albaran { get; set; }
        public DateTime FechaAlbaran { get; set; }
        public string TextoAlbaran
        {
            get { return String.Format("Albarán {0} del {1}", Albaran.ToString(), FechaAlbaran.ToString("dd/MM/yy")); }
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
    }
}