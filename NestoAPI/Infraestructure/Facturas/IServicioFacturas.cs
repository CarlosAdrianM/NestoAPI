using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosCompra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    public interface IServicioFacturas
    {
        Cliente CargarCliente(string empresa, string numeroCliente, string contacto);
        Cliente CargarClientePrincipal(string empresa, string numeroCliente);
        CabFacturaVta CargarCabFactura(string empresa, string numeroFactura);
        CabPedidoVta CargarCabPedido(string empresa, int numeroPedido);
        Empresa CargarEmpresa(string numeroEmpresa);
        Producto CargarProducto(string empresa, string numeroProducto);
        List<VencimientoFactura> CargarVencimientosExtracto(string empresa, string cliente, string numeroFactura);
        List<VencimientoFactura> CargarVencimientosOriginales(string empresa, string cliente, string numeroFactura);
        List<VendedorFactura> CargarVendedoresFactura(string empresa, string numeroFactura);
        string CuentaBancoEmpresa(string empresa);
        IEnumerable<FacturaCorreo> LeerFacturasDia(DateTime dia);
        List<VendedorFactura> CargarVendedoresPedido(string empresa, int pedido);
        PlazoPago CargarPlazosPago(string empresa, string plazosPago);
        string ComponerIban(string empresa, string cliente, string contacto, string ccc);
        List<ClienteCorreoFactura> LeerClientesCorreo(DateTime firstDayOfQuarter, DateTime lastDayOfQuarter);
        IEnumerable<FacturaCorreo> LeerFacturasCliente(string cliente, string contacto, DateTime firstDayOfQuarter, DateTime lastDayOfQuarter);
        bool EnviarCorreoSMTP(MailMessage mail);
        List<EfectoPedidoVenta> CargarEfectosPedido(string empresa, int pedido);
    }
}
