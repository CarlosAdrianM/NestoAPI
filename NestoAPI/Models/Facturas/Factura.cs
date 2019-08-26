using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Facturas
{
    public class Factura
    {
        public string Cliente { get; set; }
        public string Comentarios { get; set; }
        public string DatosRegistrales { get; set; }
        public string Delegacion { get; set; }
        public DateTime Fecha { get; set; }
        public decimal ImporteTotal { get; set; }
        public string LogoURL { get; set; }
        public string Nif { get; set; }
        public string NumeroFactura { get; set; }
        public string Ruta { get; set; }
        public List<DireccionFactura> Direcciones { get; set; }
        public List<LineaFactura> Lineas { get; set; }
        public List<string> NotasAlPie { get; set; }
        public List<TotalFactura> Totales { get; set; }
        public List<VencimientoFactura> Vencimientos { get; set; }
        public List<string> Vendedores { get; set; }
        
    }
}