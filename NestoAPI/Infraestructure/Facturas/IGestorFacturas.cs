﻿using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Facturas
{
    public interface IGestorFacturas
    {
        List<DireccionFactura> DireccionesFactura(Factura factura);
        Factura LeerFactura(string empresa, string numeroFactura);
        List<Factura> LeerFacturas(List<FacturaLookup> numerosFactura);
        List<LineaFactura> LineasFactura(Factura factura);
        ByteArrayContent FacturasEnPDF(List<Factura> facturas);
        List<NotaFactura> NotasFactura(Factura factura);
        List<TotalFactura> TotalesFactura(Factura factura);
        List<VencimientoFactura> VencimientosFactura(Factura factura);
        List<VendedorFactura> VendedoresFactura(Factura factura);
    }
}