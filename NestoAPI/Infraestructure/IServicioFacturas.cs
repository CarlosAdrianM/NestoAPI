﻿using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
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
        List<string> CargarVendedoresFactura(string empresa, string numeroFactura);
    }
}
