using System;
using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.Facturas
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
            List<VencimientoFactura> vencimientos = GenerarListaVencimientos(vtosExtracto);

            return vencimientos;
        }

        public List<VencimientoFactura> CargarVencimientosOriginales(string empresa, string cliente, string numeroFactura)
        {
            int asientoOriginal = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_FACTURA).First().Asiento;
            var vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA && e.Asiento == asientoOriginal);
            List<VencimientoFactura> vencimientos = GenerarListaVencimientos(vtosExtracto);

            return vencimientos;
        }

        public List<VendedorFactura> CargarVendedoresFactura(string empresa, string numeroFactura)
        {
            List<VendedorFactura> nombresVendedores = new List<VendedorFactura>();
            var vendedores = db.vstLinPedidoVtaComisiones.Where(l => l.Empresa == empresa && l.Nº_Factura == numeroFactura).GroupBy(l => l.Vendedor);
            foreach(var codigoVendedor in vendedores)
            {
                string vendedor = db.Vendedores.Single(v => v.Empresa == empresa && v.Número == codigoVendedor.Key).Descripción.Trim();
                nombresVendedores.Add(new VendedorFactura { Nombre = vendedor });
            }
            return nombresVendedores;
        }

        public string CuentaBancoEmpresa(string empresa)
        {
            string cuentasEmpresa = "";
            var cuentas = db.Bancos.Where(b => b.Empresa == empresa && b.DC_IBAN != null);
            foreach(var cuenta in cuentas)
            {
                cuentasEmpresa += cuenta.Pais + cuenta.DC_IBAN + cuenta.Entidad + cuenta.Sucursal + cuenta.DC + cuenta.Nº_Cuenta + Environment.NewLine;
            }
            cuentasEmpresa = cuentasEmpresa.TrimEnd(Environment.NewLine.ToCharArray());
            return cuentasEmpresa;
        }


        private static List<VencimientoFactura> GenerarListaVencimientos(IQueryable<ExtractoCliente> vtosExtracto)
        {
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach (var vto in vtosExtracto)
            {
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    CCC = vto.CCC,
                    FormaPago = vto.FormaPago,
                    Importe = vto.Importe,
                    ImportePendiente = vto.ImportePdte,
                    Vencimiento = vto.FechaVto != null ? (DateTime)vto.FechaVto : vto.Fecha,
                    Iban = vto.CCC1?.Pais + vto.CCC1?.DC_IBAN + vto.CCC1?.Entidad + vto.CCC1?.Oficina + vto.CCC1?.DC + vto.CCC1?.Nº_Cuenta
                };
                vencimientos.Add(vencimiento);
            }

            return vencimientos;
        }

        public IQueryable<FacturaCorreo> LeerFacturasDia(DateTime dia)
        {
            var facturas = from f in db.CabFacturaVtas
                           join c in db.Clientes
                           on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                           where f.Fecha == dia && c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null).Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                           select new FacturaCorreo {
                               Empresa = f.Empresa.Trim(),
                               Factura = f.Número.Trim(),
                               Correo = c.PersonasContactoClientes.FirstOrDefault(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO).CorreoElectrónico.Trim()
                           };
            return facturas;
        }
    }
}