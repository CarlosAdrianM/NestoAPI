using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Mail;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
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
            var vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA).OrderBy(e => e.Nº_Orden);
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
                Iban iban = new Iban(cuenta.Pais + cuenta.DC_IBAN + cuenta.Entidad + cuenta.Sucursal + cuenta.DC + cuenta.Nº_Cuenta);
                cuentasEmpresa +=  iban.Formateado + Environment.NewLine;
            }
            cuentasEmpresa = cuentasEmpresa.TrimEnd(Environment.NewLine.ToCharArray());
            return cuentasEmpresa;
        }


        private static List<VencimientoFactura> GenerarListaVencimientos(IQueryable<ExtractoCliente> vtosExtracto)
        {
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach (var vto in vtosExtracto)
            {
                Iban iban = new Iban(Iban.ComponerIban(vto.CCC1));
                VencimientoFactura vencimiento = new VencimientoFactura
                {
                    CCC = vto.CCC,
                    FormaPago = vto.FormaPago,
                    Importe = vto.Importe,
                    ImportePendiente = vto.ImportePdte,
                    Vencimiento = vto.FechaVto != null ? (DateTime)vto.FechaVto : vto.Fecha,
                    Iban = iban.Enmascarado
                };
                vencimientos.Add(vencimiento);
            }

            return vencimientos;
        }

        public string ComponerIban(string empresa, string cliente, string contacto, string ccc)
        {
            NVEntities db = new NVEntities();
            return Iban.ComponerIban(db.CCCs.FirstOrDefault(c => c.Empresa == empresa && c.Cliente == cliente && c.Contacto == contacto && c.Número == ccc));
        }

        public IEnumerable<FacturaCorreo> LeerFacturasDia(DateTime dia)
        {
            var facturas = (from f in db.CabFacturaVtas
                           join c in db.Clientes
                           on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                           where f.Fecha == dia && c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null).Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                           select new { Empresa = c.Empresa.Trim(), Factura = f.Número.Trim(), Correos = c.PersonasContactoClientes.Where(p => p.CorreoElectrónico != null) })
                           .ToList()
                           .Select (f => new FacturaCorreo {
                               Empresa = f.Empresa,
                               Factura = f.Factura,
                               Correo = string.Join(", ", f.Correos.Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO).Select(c => c.CorreoElectrónico.Trim())) 
                           })
                           .OrderBy(p => p.Correo);

            List<FacturaCorreo> facturasSinDeudaVencida = new List<FacturaCorreo>();

            foreach (var fra in facturas)
            {
                var estadoDVD = db.ExtractosCliente.Where(e => e.Empresa == fra.Empresa && e.Nº_Documento == fra.Factura && e.ImportePdte != 0 && e.Estado == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA);
                if (!estadoDVD.Any())
                {
                    facturasSinDeudaVencida.Add(fra);
                }
            }

            return facturasSinDeudaVencida;
        }

        public List<VendedorFactura> CargarVendedoresPedido(string empresa, int pedido)
        {
            List<VendedorFactura> nombresVendedores = new List<VendedorFactura>();
            List<string> codigosVendedores = new List<string>();
            var pedidoCompleto = db.CabPedidoVtas.Single(p => p.Empresa == empresa && p.Número == pedido);
            var vendedorPeluqueria = db.VendedoresPedidosGruposProductos.SingleOrDefault(v => v.Empresa == empresa && v.Pedido == pedido);
            var vendedorEstetica = pedidoCompleto.Vendedor;

            var hayPeluqueria = pedidoCompleto.LinPedidoVtas.Any(l => l.Grupo == Constantes.Productos.GRUPO_PELUQUERIA);
            var hayNoPeluqueria = pedidoCompleto.LinPedidoVtas.Any(l => l.Grupo != Constantes.Productos.GRUPO_PELUQUERIA);
            if (hayPeluqueria && vendedorPeluqueria != null)
            {
                codigosVendedores.Add(vendedorPeluqueria.Vendedor);
            }
            if (hayNoPeluqueria)
            {
                codigosVendedores.Add(vendedorEstetica);
            }
            
            foreach (var codigoVendedor in codigosVendedores)
            {
                string vendedor = db.Vendedores.Single(v => v.Empresa == empresa && v.Número == codigoVendedor).Descripción.Trim();
                nombresVendedores.Add(new VendedorFactura { Nombre = vendedor });
            }
            return nombresVendedores;
        }

        public PlazoPago CargarPlazosPago(string empresa, string plazosPago)
        {
            return db.PlazosPago.Single(p => p.Empresa == empresa && p.Número == plazosPago);
        }

        public List<ClienteCorreoFactura> LeerClientesCorreo(DateTime firstDayOfQuarter, DateTime lastDayOfQuarter)
        {
            var clientes = (from f in db.CabFacturaVtas
                            join c in db.Clientes
                            on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                            where f.Fecha >= firstDayOfQuarter && f.Fecha <= lastDayOfQuarter &&
                                  c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null)
                                                            .Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO ||
                                                                      p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURAS_TRIMESTRE_POR_CORREO)
                            select new { Cliente = c.Nº_Cliente, Contacto = f.Contacto, Correos = c.PersonasContactoClientes.Where(p => p.CorreoElectrónico != null) })
                            .ToList()
                            .Select(f => new ClienteCorreoFactura
                            {
                                Cliente = f.Cliente,
                                Contacto = f.Contacto,
                                Correo = string.Join(", ", f.Correos
                                    // Primero intentamos obtener el correo del cargo trimestral
                                    .Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURAS_TRIMESTRE_POR_CORREO)
                                    .Select(c => c.CorreoElectrónico.Trim())
                                    // Si no hay correos del cargo trimestral, usamos el correo del cargo regular
                                    .DefaultIfEmpty(f.Correos.FirstOrDefault(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)?.CorreoElectrónico?.Trim())
                                )
                            })
                            .OrderBy(p => p.Correo);


            IEnumerable<ClienteCorreoFactura> filteredList = clientes.Distinct();

            return filteredList.ToList();
        }

        public IEnumerable<FacturaCorreo> LeerFacturasCliente(string cliente, string contacto, DateTime firstDayOfQuarter, DateTime lastDayOfQuarter)
        {
            var facturas = (from f in db.CabFacturaVtas
                            join c in db.Clientes
                            on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                            where c.Nº_Cliente == cliente && c.Contacto == contacto && f.Fecha >= firstDayOfQuarter && f.Fecha <= lastDayOfQuarter && c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null).Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
                            select new { Empresa = c.Empresa.Trim(), Factura = f.Número.Trim(), Correos = c.PersonasContactoClientes.Where(p => p.CorreoElectrónico != null) })
                           .ToList()
                           .Select(f => new FacturaCorreo
                           {
                               Empresa = f.Empresa,
                               Factura = f.Factura,
                               Correo = string.Join(", ", f.Correos.Where(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO).Select(c => c.CorreoElectrónico.Trim()))
                           })
                           .OrderBy(p => p.Correo);

            List<FacturaCorreo> facturasSinDeudaVencida = new List<FacturaCorreo>();

            foreach (var fra in facturas)
            {
                var estadoDVD = db.ExtractosCliente.Where(e => e.Empresa == fra.Empresa && e.Nº_Documento == fra.Factura && e.ImportePdte != 0 && e.Estado == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA);
                if (!estadoDVD.Any())
                {
                    facturasSinDeudaVencida.Add(fra);
                }
            }

            return facturasSinDeudaVencida;
        }

        public bool EnviarCorreoSMTP(MailMessage mail)
        {
            using (SmtpClient client = new SmtpClient())
            {
                client.Port = 587;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                string contrasenna = ConfigurationManager.AppSettings["office365password"];
                client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
                client.Host = "smtp.office365.com";
                client.TargetName = "STARTTLS/smtp.office365.com"; // Añadir esta línea para especificar el nombre del objetivo para STARTTLS
                // Configurar TLS 1.2 explícitamente
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                try
                {
                    client.Send(mail);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}