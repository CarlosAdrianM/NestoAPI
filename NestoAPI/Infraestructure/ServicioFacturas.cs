using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure
{
    public class ServicioFacturas : IServicioFacturas
    {
        private readonly NVEntities db = new NVEntities();

        public CabFacturaVta CargarCabFactura(string empresa, string numeroFactura)
        {
            return db.CabFacturaVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroFactura);
        }

        public CabPedidoVta CargarCabPedido(string empresa, int numeroPedido)
        {
            return db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroPedido);
        }

        public Cliente CargarCliente(string empresa, string numeroCliente, string contacto)
        {
            return db.Clientes.Single(c => c.Empresa == empresa && c.Nº_Cliente == numeroCliente && c.Contacto == contacto);
        }

        public Cliente CargarClientePrincipal(string empresa, string numeroCliente)
        {
            return db.Clientes.Single(c => c.Empresa == empresa && c.Nº_Cliente == numeroCliente && c.ClientePrincipal);
        }

        public Empresa CargarEmpresa(string numeroEmpresa)
        {
            return db.Empresas.Single(e => e.Número == numeroEmpresa);
        }

        public Producto CargarProducto(string empresa, string numeroProducto)
        {
            return db.Productos.Single(p => p.Empresa == empresa && p.Número == numeroProducto);
        }

        public List<VencimientoFactura> CargarVencimientosExtracto(string empresa, string cliente, string numeroFactura)
        {
            var vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA);
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach(var vto in vtosExtracto)
            {
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    FormaPago = vto.FormaPago,
                    Importe = vto.Importe,
                    ImportePendiente = vto.ImportePdte,
                    Vencimiento = vto.FechaVto != null ? (DateTime)vto.FechaVto : vto.Fecha
                };
                vencimientos.Add(vencimiento);
            }

            return vencimientos;
        }

        public List<VencimientoFactura> CargarVencimientosOriginales(string empresa, string cliente, string numeroFactura)
        {
            int asientoOriginal = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_FACTURA).First().Asiento;
            var vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA && e.Asiento == asientoOriginal);
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach (var vto in vtosExtracto)
            {
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    FormaPago = vto.FormaPago,
                    Importe = vto.Importe,
                    ImportePendiente = vto.ImportePdte,
                    Vencimiento = vto.FechaVto != null ? (DateTime)vto.FechaVto : vto.Fecha
                };
                vencimientos.Add(vencimiento);
            }

            return vencimientos;
        }

        public List<string> CargarVendedoresFactura(string empresa, string numeroFactura)
        {
            List<string> nombresVendedores = new List<string>();
            var vendedores = db.vstLinPedidoVtaComisiones.Where(l => l.Empresa == empresa && l.Nº_Factura == numeroFactura).GroupBy(l => l.Vendedor);
            foreach(var codigoVendedor in vendedores)
            {
                string vendedor = db.Vendedores.Single(v => v.Empresa == empresa && v.Número == codigoVendedor.Key).Descripción.Trim();
                nombresVendedores.Add(vendedor);
            }
            return nombresVendedores;
        }
    }
}