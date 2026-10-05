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
        // forzarQuestPdf: los clientes de TiendasNuevaVision no tienen ParametrosUsuario y la
        // tienda online usa siempre QuestPDF (TiendasNuevaVision#15)
        ByteArrayContent FacturasEnPDF(List<Factura> facturas, bool papelConMembrete = false, string usuario = null, bool mostrarImagenes = false, bool forzarQuestPdf = false);
        List<NotaFactura> NotasFactura(Factura factura);
        List<TotalFactura> TotalesFactura(Factura factura);
        List<VencimientoFactura> VencimientosFactura(Factura factura);
        List<VendedorFactura> VendedoresFactura(Factura factura);
        Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario, string usuarioAutenticado = null);
        /// <summary>Nesto#259: las facturas marcadas, en un solo correo, a los correos que escribe el usuario.</summary>
        Task<ResultadoEnvioFacturasCorreoDTO> EnviarFacturasACorreo(string empresa, IEnumerable<string> facturas, IEnumerable<string> correos, string usuario);
    }
}