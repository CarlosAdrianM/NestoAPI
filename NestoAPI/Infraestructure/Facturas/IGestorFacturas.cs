using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    public interface IGestorFacturas
    {
        List<DireccionFactura> DireccionesFactura(Factura factura);
        Factura LeerFactura(string empresa, string numeroFactura);
        Factura LeerPedido(string empresa, int pedido);
        List<Factura> LeerFacturas(List<FacturaLookup> numerosFactura);
        Factura LeerAlbaran(string empresa, int numeroAlbaran);
        List<Factura> LeerAlbaranes(List<FacturaLookup> numerosAlbaran);
        List<LineaFactura> LineasFactura(Factura factura);
        ByteArrayContent FacturasEnPDF(List<Factura> facturas, bool papelConMembrete = false, string usuario = null);
        List<NotaFactura> NotasFactura(Factura factura);
        List<TotalFactura> TotalesFactura(Factura factura);
        List<VencimientoFactura> VencimientosFactura(Factura factura);
        List<VendedorFactura> VendedoresFactura(Factura factura);
        Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario);
    }
}