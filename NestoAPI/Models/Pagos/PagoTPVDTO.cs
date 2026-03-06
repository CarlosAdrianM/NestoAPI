using System;

namespace NestoAPI.Models.Pagos
{
    public class PagoTPVDTO
    {
        public int Id { get; set; }
        public string NumeroOrden { get; set; }
        public string Tipo { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string Correo { get; set; }
        public string Movil { get; set; }
        public string Estado { get; set; }
        public string CodigoRespuesta { get; set; }
        public string CodigoAutorizacion { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }
        public string Usuario { get; set; }
        public int? Liquidado { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Vendedor { get; set; }
        public string FormaVenta { get; set; }
        public string Delegacion { get; set; }
        public string TipoApunteEfecto { get; set; }
    }
}
