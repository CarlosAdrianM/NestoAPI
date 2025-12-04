using Elmah;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Facturas
{
    public class ServicioFacturas : IServicioFacturas
    {
        private readonly NVEntities db;
        private readonly bool dbEsExterno;

        /// <summary>
        /// Constructor por defecto. Crea su propio NVEntities interno.
        /// </summary>
        public ServicioFacturas() : this(null)
        {
        }

        /// <summary>
        /// Constructor que permite inyectar un NVEntities externo.
        /// Esto es necesario para evitar conflictos de concurrencia cuando se usa
        /// desde GestorFacturacionRutas, que ya tiene su propio contexto.
        /// </summary>
        /// <param name="dbExterno">NVEntities externo. Si es null, se crea uno interno.</param>
        public ServicioFacturas(NVEntities dbExterno)
        {
            if (dbExterno != null)
            {
                db = dbExterno;
                dbEsExterno = true;
            }
            else
            {
                db = new NVEntities();
                dbEsExterno = false;
            }
        }

        public CabFacturaVta CargarCabFactura(string empresa, string numeroFactura)
        {
            // Usar AsNoTracking para forzar lectura fresca desde BD
            // Esto es necesario porque el SP prdCrearFacturaVta actualiza LinPedidoVta.Nº_Factura
            // y EF podría tener datos obsoletos en su caché del contexto
            return db.CabsFacturasVtas
                .AsNoTracking()
                .Include(c => c.LinPedidoVtas)
                .SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroFactura);
        }

        public CabPedidoVta CargarCabPedido(string empresa, int numeroPedido)
        {
            return db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroPedido);
        }

        public CabPedidoVta CargarCabPedidoPorAlbaran(string empresa, int numeroAlbaran)
        {
            return db.CabPedidoVtas.FirstOrDefault(c => c.Empresa == empresa && c.LinPedidoVtas.Any(l => l.Nº_Albarán == numeroAlbaran));
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
            IOrderedQueryable<ExtractoCliente> vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA).OrderBy(e => e.Nº_Orden);
            List<VencimientoFactura> vencimientos = GenerarListaVencimientos(vtosExtracto);

            return vencimientos;
        }

        public List<VencimientoFactura> CargarVencimientosOriginales(string empresa, string cliente, string numeroFactura)
        {
            int asientoOriginal = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_FACTURA).First().Asiento;
            IQueryable<ExtractoCliente> vtosExtracto = db.ExtractosCliente.Where(e => e.Empresa == empresa && e.Número == cliente && e.Nº_Documento == numeroFactura && e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA && e.Asiento == asientoOriginal);
            List<VencimientoFactura> vencimientos = GenerarListaVencimientos(vtosExtracto);

            return vencimientos;
        }

        public List<VendedorFactura> CargarVendedoresFactura(string empresa, string numeroFactura)
        {
            List<VendedorFactura> nombresVendedores = new List<VendedorFactura>();
            IQueryable<IGrouping<string, vstLinPedidoVtaComisione>> vendedores = db.vstLinPedidoVtaComisiones.Where(l => l.Empresa == empresa && l.Nº_Factura == numeroFactura).GroupBy(l => l.Vendedor);
            foreach (IGrouping<string, vstLinPedidoVtaComisione> codigoVendedor in vendedores)
            {
                var vendedorEntity = db.Vendedores.FirstOrDefault(v => v.Empresa == empresa && v.Número == codigoVendedor.Key);
                if (vendedorEntity != null && !string.IsNullOrWhiteSpace(vendedorEntity.Descripción))
                {
                    string vendedor = vendedorEntity.Descripción.Trim();
                    nombresVendedores.Add(new VendedorFactura { Nombre = vendedor });
                }
                else
                {
                    // Si el vendedor no existe o no tiene descripción, agregar un placeholder
                    System.Diagnostics.Debug.WriteLine($"⚠ ADVERTENCIA: Vendedor {codigoVendedor.Key} no encontrado o sin descripción para factura {numeroFactura}");
                    nombresVendedores.Add(new VendedorFactura { Nombre = $"Vendedor {codigoVendedor.Key}" });
                }
            }
            return nombresVendedores;
        }

        public string CuentaBancoEmpresa(string empresa)
        {
            string cuentasEmpresa = "";
            IQueryable<Banco> cuentas = db.Bancos.Where(b => b.Empresa == empresa && b.DC_IBAN != null);
            foreach (Banco cuenta in cuentas)
            {
                Iban iban = new Iban(cuenta.Pais + cuenta.DC_IBAN + cuenta.Entidad + cuenta.Sucursal + cuenta.DC + cuenta.Nº_Cuenta);
                cuentasEmpresa += iban.Formateado + Environment.NewLine;
            }
            cuentasEmpresa = cuentasEmpresa.TrimEnd(Environment.NewLine.ToCharArray());
            return cuentasEmpresa;
        }


        private static List<VencimientoFactura> GenerarListaVencimientos(IQueryable<ExtractoCliente> vtosExtracto)
        {
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>();
            foreach (ExtractoCliente vto in vtosExtracto)
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
            IOrderedEnumerable<FacturaCorreo> facturas = (from f in db.CabsFacturasVtas
                                                          join c in db.Clientes
                                                          on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                                                          where f.Fecha == dia && c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null).Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
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

            foreach (FacturaCorreo fra in facturas)
            {
                IQueryable<ExtractoCliente> estadoDVD = db.ExtractosCliente.Where(e => e.Empresa == fra.Empresa && e.Nº_Documento == fra.Factura && e.ImportePdte != 0 && e.Estado == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA);
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
            CabPedidoVta pedidoCompleto = db.CabPedidoVtas.Single(p => p.Empresa == empresa && p.Número == pedido);
            VendedorPedidoGrupoProducto vendedorPeluqueria = db.VendedoresPedidosGruposProductos.SingleOrDefault(v => v.Empresa == empresa && v.Pedido == pedido);
            string vendedorEstetica = pedidoCompleto.Vendedor;

            bool hayPeluqueria = pedidoCompleto.LinPedidoVtas.Any(l => l.Grupo == Constantes.Productos.GRUPO_PELUQUERIA);
            bool hayNoPeluqueria = pedidoCompleto.LinPedidoVtas.Any(l => l.Grupo != Constantes.Productos.GRUPO_PELUQUERIA);
            if (hayPeluqueria && vendedorPeluqueria != null)
            {
                codigosVendedores.Add(vendedorPeluqueria.Vendedor);
            }
            if (hayNoPeluqueria)
            {
                codigosVendedores.Add(vendedorEstetica);
            }

            foreach (string codigoVendedor in codigosVendedores)
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
            IOrderedEnumerable<ClienteCorreoFactura> clientes = (from f in db.CabsFacturasVtas
                                                                 join c in db.Clientes
                                                                 on new { f.Empresa, f.Nº_Cliente, f.Contacto } equals new { c.Empresa, c.Nº_Cliente, c.Contacto }
                                                                 where f.Fecha >= firstDayOfQuarter && f.Fecha <= lastDayOfQuarter &&
                                                                       c.PersonasContactoClientes.Where(c => c.CorreoElectrónico != null)
                                                                                                 .Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO ||
                                                                                                           p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURAS_TRIMESTRE_POR_CORREO)
                                                                 select new { Cliente = c.Nº_Cliente, f.Contacto, Correos = c.PersonasContactoClientes.Where(p => p.CorreoElectrónico != null) })
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
            IOrderedEnumerable<FacturaCorreo> facturas = (from f in db.CabsFacturasVtas
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

            foreach (FacturaCorreo fra in facturas)
            {
                IQueryable<ExtractoCliente> estadoDVD = db.ExtractosCliente.Where(e => e.Empresa == fra.Empresa && e.Nº_Documento == fra.Factura && e.ImportePdte != 0 && e.Estado == Constantes.ExtractosCliente.Estados.DEUDA_VENCIDA);
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

        public List<EfectoPedidoVenta> CargarEfectosPedido(string empresa, int pedido)
        {
            List<EfectoPedidoVenta> efectos = db.EfectosPedidosVentas.Where(e => e.Empresa == empresa && e.Pedido == pedido).OrderBy(e => e.FechaVencimiento).ToList();
            return efectos;
        }

        public async Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario)
        {
            // Usar el db de la clase (puede ser externo o interno según el constructor)
            // Esto evita conflictos de concurrencia cuando se llama desde GestorFacturacionRutas
            string empresaOriginal = empresa;
            CabPedidoVta cabPedido = db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .Single(p => p.Empresa == empresa && p.Número == pedido);

            if (cabPedido.Periodo_Facturacion == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES)
            {
                // Caso especial: cliente de fin de mes
                return new CrearFacturaResponseDTO
                {
                    NumeroFactura = cabPedido.Periodo_Facturacion,
                    Empresa = empresa,
                    NumeroPedido = pedido
                };
            }

            // Verificar si hay que traspasar a empresa espejo
            // NOTA: Solo traspasar si NO estamos usando db externo (para evitar doble traspaso
            // cuando se llama desde GestorFacturacionRutas que ya maneja el traspaso)
            if (!dbEsExterno)
            {
                var servicioTraspaso = new Infraestructure.Traspasos.ServicioTraspasoEmpresa(db);
                if (servicioTraspaso.HayQueTraspasar(cabPedido))
                {
                    await servicioTraspaso.TraspasarPedidoAEmpresa(
                        cabPedido,
                        Constantes.Empresas.EMPRESA_POR_DEFECTO,
                        Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO,
                        usuario);

                    // Actualizar empresa para el stored procedure
                    empresa = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO;

                    // IMPORTANTE: Después del traspaso, el objeto cabPedido queda Detached
                    // Debemos recargar el pedido desde la BD para tener los datos actualizados
                    // (especialmente el campo IVA que se actualiza durante el traspaso)
                    cabPedido = db.CabPedidoVtas
                        .Include(p => p.LinPedidoVtas)
                        .Single(p => p.Empresa == empresa && p.Número == pedido);
                }
            }

            if (string.IsNullOrEmpty(cabPedido.IVA))
            {
                throw new FacturacionException(
                    $"El pedido {pedido} no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
                    "FACTURACION_IVA_FALTANTE",
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario);
            }

            // PREVENTIVO: Recalcular líneas ANTES de llamar al stored procedure
            // Esto evita errores de descuadre por diferencias de redondeo entre C# y SQL.
            // El recálculo se hace antes porque después del SP (incluso con rollback),
            // las líneas pueden estar temporalmente en estado 4 y el trigger no permite modificarlas.
            bool seAplicoAutoFix = await RecalcularLineasPedido(db, cabPedido);
            if (seAplicoAutoFix)
            {
                System.Diagnostics.Debug.WriteLine($"  → AUTO-FIX PREVENTIVO aplicado para pedido {pedido}");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"  → Ejecutando prdCrearFacturaVta para pedido {pedido}");

                SqlParameter empresaParam = new SqlParameter("@Empresa", System.Data.SqlDbType.Char) { Value = empresa };
                SqlParameter pedidoParam = new SqlParameter("@Pedido", System.Data.SqlDbType.Int) { Value = pedido };
                SqlParameter fechaEntregaParam = new SqlParameter("@Fecha", System.Data.SqlDbType.DateTime) { Value = DateTime.Today };
                SqlParameter usuarioParam = new SqlParameter("@Usuario", System.Data.SqlDbType.Char) { Value = usuario };
                SqlParameter numFactura = new SqlParameter("@NumFactura", SqlDbType.Char, 10) { Direction = ParameterDirection.Output };

                // Ejecutar el procedimiento almacenado
                _ = await db.Database.ExecuteSqlCommandAsync(
                    "EXEC prdCrearFacturaVta @Empresa, @Pedido, @Fecha, @NumFactura OUTPUT, @Usuario",
                    empresaParam, pedidoParam, fechaEntregaParam, numFactura, usuarioParam
                );

                // Obtener el valor de retorno del parámetro
                string resultadoProcedimiento = numFactura.Value.ToString().Trim();

                if (seAplicoAutoFix)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✓ AUTO-FIX PREVENTIVO EXITOSO: Factura {resultadoProcedimiento} creada tras recalcular líneas");
                }

                // Persistir datos fiscales del cliente principal en la factura (Verifactu)
                await PersistirDatosFiscalesFactura(empresa, resultadoProcedimiento, cabPedido);

                return new CrearFacturaResponseDTO
                {
                    NumeroFactura = resultadoProcedimiento,
                    Empresa = empresa,
                    NumeroPedido = pedido
                };
            }
            catch (SqlException sqlEx)
            {
                throw new FacturacionException(
                    $"Error al ejecutar el procedimiento almacenado de facturación: {sqlEx.Message}",
                    "FACTURACION_STORED_PROCEDURE_ERROR",
                    sqlEx,
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario)
                    .WithData("SqlErrorNumber", sqlEx.Number)
                    .WithData("StoredProcedure", "prdCrearFacturaVta")
                    .WithData("SeAplicoAutoFixPreventivo", seAplicoAutoFix);
            }
            catch (Exception ex)
            {
                throw new FacturacionException(
                    $"Error inesperado al crear la factura del pedido {pedido}: {ex.Message}",
                    "FACTURACION_ERROR_INESPERADO",
                    ex,
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario)
                    .WithData("SeAplicoAutoFixPreventivo", seAplicoAutoFix);
            }
        }

        /// <summary>
        /// Persiste los datos fiscales del cliente principal en la factura.
        /// Esto es necesario para Verifactu: los datos fiscales deben quedar grabados
        /// en el momento de la facturación, independientemente de cambios posteriores en el cliente.
        /// </summary>
        /// <param name="empresaFactura">Empresa de la factura (puede ser empresa espejo por traspaso)</param>
        /// <param name="numeroFactura">Número de la factura recién creada</param>
        /// <param name="cabPedido">Cabecera del pedido con TipoRectificativa</param>
        private async Task PersistirDatosFiscalesFactura(string empresaFactura, string numeroFactura, CabPedidoVta cabPedido)
        {
            // IMPORTANTE: Detach el pedido y sus líneas antes de guardar datos fiscales
            // El SP prdCrearFacturaVta modificó las líneas en la BD pero EF tiene datos obsoletos en memoria.
            // Si no hacemos detach, SaveChanges intentará guardar las líneas con datos incorrectos.
            if (cabPedido != null)
            {
                if (cabPedido.LinPedidoVtas != null)
                {
                    foreach (var linea in cabPedido.LinPedidoVtas.ToList())
                    {
                        var entry = db.Entry(linea);
                        if (entry.State != EntityState.Detached)
                        {
                            entry.State = EntityState.Detached;
                        }
                    }
                }
                var cabEntry = db.Entry(cabPedido);
                if (cabEntry.State != EntityState.Detached)
                {
                    cabEntry.State = EntityState.Detached;
                }
            }

            // Obtener la factura recién creada
            var factura = await db.CabsFacturasVtas
                .FirstOrDefaultAsync(f => f.Empresa == empresaFactura && f.Número == numeroFactura);

            if (factura == null)
            {
                throw new FacturacionException(
                    $"No se encontró la factura {numeroFactura} recién creada para guardar los datos fiscales",
                    "FACTURACION_FACTURA_NO_ENCONTRADA",
                    empresa: empresaFactura,
                    pedido: cabPedido.Número,
                    usuario: cabPedido.Usuario);
            }

            // Buscar el cliente principal (datos fiscales)
            // IMPORTANTE: Los clientes siempre están en EMPRESA_POR_DEFECTO ('1'),
            // incluso cuando la factura se crea en empresa espejo ('3') por traspaso.
            // Por eso buscamos el cliente en la empresa por defecto, no en la empresa de la factura.
            var clientesPrincipales = await db.Clientes
                .Where(c => c.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && c.Nº_Cliente == factura.Nº_Cliente
                    && c.ClientePrincipal)
                .ToListAsync();

            if (clientesPrincipales.Count == 0)
            {
                throw new FacturacionException(
                    $"No se encontró el cliente principal para el cliente {factura.Nº_Cliente}. " +
                    "Debe existir un contacto con ClientePrincipal = true para poder facturar.",
                    "FACTURACION_CLIENTE_PRINCIPAL_NO_ENCONTRADO",
                    empresa: empresaFactura,
                    pedido: cabPedido.Número,
                    usuario: cabPedido.Usuario);
            }

            if (clientesPrincipales.Count > 1)
            {
                var contactos = string.Join(", ", clientesPrincipales.Select(c => c.Contacto?.Trim()));
                throw new FacturacionException(
                    $"El cliente {factura.Nº_Cliente} tiene {clientesPrincipales.Count} contactos marcados como ClientePrincipal. " +
                    $"Solo debe haber uno. Contactos: {contactos}",
                    "FACTURACION_MULTIPLES_CLIENTES_PRINCIPALES",
                    empresa: empresaFactura,
                    pedido: cabPedido.Número,
                    usuario: cabPedido.Usuario);
            }

            var clientePrincipal = clientesPrincipales.Single();

            // Guardar datos fiscales en la factura
            factura.NombreFiscal = clientePrincipal.Nombre?.Trim();
            factura.CifNif = clientePrincipal.CIF_NIF?.Trim();
            factura.DireccionFiscal = clientePrincipal.Dirección?.Trim();
            factura.CodPostalFiscal = clientePrincipal.CodPostal?.Trim();
            factura.PoblacionFiscal = clientePrincipal.Población?.Trim();
            factura.ProvinciaFiscal = clientePrincipal.Provincia?.Trim();

            // Copiar tipo rectificativa del pedido
            factura.TipoRectificativa = cabPedido.TipoRectificativa;

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Recalcula los importes de las líneas de un pedido usando SQL directo.
        /// Esto garantiza que el redondeo sea exactamente el mismo que usa el procedimiento almacenado.
        /// Issue #242/#243: Unificar redondeo entre NestoAPI y procedimientos almacenados.
        /// </summary>
        /// <param name="db">Contexto de Entity Framework</param>
        /// <param name="pedido">Pedido a recalcular</param>
        /// <returns>True si se realizaron cambios, False si no hubo cambios</returns>
        private async Task<bool> RecalcularLineasPedido(NVEntities db, CabPedidoVta pedido)
        {
            if (pedido == null)
            {
                return false;
            }

            var infoRecalculo = new StringBuilder();
            infoRecalculo.AppendLine($"=== AUTO-FIX: Recálculo de líneas pedido {pedido.Empresa}/{pedido.Número} ===");
            infoRecalculo.AppendLine($"Fecha/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            infoRecalculo.AppendLine();

            // Ejecutar UPDATE SQL directo - misma fórmula que C# en GestorPedidosVenta.CalcularImportesLinea
            // IMPORTANTE: Solo Base Imponible se redondea a 2 decimales.
            // ImporteIVA, ImporteRE y Total NO se redondean (se guardan con más precisión).
            // El redondeo final a 2 decimales se hace al sumar en la factura, no línea a línea.
            //
            // Carlos 01/12/25: El SP prdCrearFacturaVta calcula los descuentos redondeando cada uno por separado:
            //   @ImpDtoProducto = sum(round(Bruto*DescuentoProducto,2))
            //   @ImpDtoCliente = sum(round(bruto*(1-(1-descuentoProducto)*(1-descuentoCliente)),2)) - @ImpDtoProducto
            //   etc.
            //
            // Carlos 02/12/25: IMPORTANTE - NO podemos modificar Bruto porque existe la restricción:
            //   CK_LinPedidoVta_5: ([bruto]=[precio]*[cantidad] OR [tipolinea]<>(1))
            //
            // Carlos 02/12/25: CLAVE PARA EL ASIENTO CONTABLE
            // El SP construye el asiento usando:
            //   - HABER Ventas (700): SUM(ROUND(Bruto, 2))
            //   - DEBE Descuentos (665): SUM(ROUND(Bruto * Dto, 2))
            //   - La diferencia (Ventas - Descuentos) debe ser igual a SUM(BaseImponible)
            //
            // Por tanto, BaseImponible debe calcularse como:
            //   BaseImponible = ROUND(Bruto, 2) - ROUND(Bruto * SumaDescuentos, 2)
            // Y NO como:
            //   BaseImponible = Bruto - ROUND(Bruto * SumaDescuentos, 2)  <-- INCORRECTO, causa descuadre
            //
            // La diferencia (ej: 67.4325 vs 67.43) se acumula en múltiples líneas y descuadra el asiento.
            //
            // Fórmulas CORRECTAS (coherentes con el asiento contable del SP):
            //   importeDto = ROUND(Bruto * sumaDescuentos, 2)
            //   baseImponible = ROUND(Bruto, 2) - importeDto   <-- Usar ROUND(Bruto, 2)!
            //   importeIVA = baseImponible * PorcentajeIVA / 100
            //   importeRE = baseImponible * PorcentajeRE
            //   total = baseImponible + importeIVA + importeRE
            // Estados de línea: -1=Pendiente, 1=En curso, 2=Albarán (las que se facturan)
            // No incluimos estado 4 (Facturado) porque el trigger no permite modificarlas
            string sql = @"
                ;WITH BaseCalculada AS (
                    SELECT
                        l.[Nº Orden],
                        l.Bruto,
                        ROUND(l.Bruto, 2) AS BrutoRedondeado,
                        ROUND(l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        , 2) AS NuevoImporteDto,
                        -- CLAVE: Usar ROUND(Bruto, 2) para que cuadre con el asiento contable del SP
                        ROUND(l.Bruto, 2) - ROUND(l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        , 2) AS NuevaBI
                    FROM LinPedidoVta l
                    WHERE l.Empresa = @empresa
                      AND l.Número = @numero
                      AND l.Estado BETWEEN -1 AND 2
                )
                UPDATE l
                SET
                    [Base Imponible] = bc.NuevaBI,
                    ImporteDto = bc.NuevoImporteDto,
                    ImporteIVA = bc.NuevaBI * l.PorcentajeIVA / 100.0,
                    ImporteRE = bc.NuevaBI * l.PorcentajeRE,
                    Total = bc.NuevaBI + (bc.NuevaBI * l.PorcentajeIVA / 100.0) + (bc.NuevaBI * l.PorcentajeRE)
                FROM LinPedidoVta l
                INNER JOIN BaseCalculada bc ON l.[Nº Orden] = bc.[Nº Orden]
                WHERE l.Empresa = @empresa
                  AND l.Número = @numero
                  AND l.Estado BETWEEN -1 AND 2
                  AND (
                      -- Detectar diferencia en Base Imponible o ImporteDto (coherencia con SP)
                      -- Tolerancia 0.0001 para detectar diferencias pequeñas que acumuladas causan descuadres
                      ABS(l.[Base Imponible] - bc.NuevaBI) > 0.0001
                      OR
                      ABS(l.ImporteDto - bc.NuevoImporteDto) > 0.0001
                      OR
                      -- Detectar diferencia en ImporteIVA
                      ABS(l.ImporteIVA - (bc.NuevaBI * l.PorcentajeIVA / 100.0)) > 0.0001
                      OR
                      -- Detectar diferencia en Total
                      ABS(l.Total - (bc.NuevaBI + (bc.NuevaBI * l.PorcentajeIVA / 100.0) + (bc.NuevaBI * l.PorcentajeRE))) > 0.0001
                  )";

            var empresaParam = new SqlParameter("@empresa", pedido.Empresa);
            var numeroParam = new SqlParameter("@numero", pedido.Número);

            int filasAfectadas = await db.Database.ExecuteSqlCommandAsync(sql, empresaParam, numeroParam);

            bool huboCambios = filasAfectadas > 0;

            if (huboCambios)
            {
                infoRecalculo.AppendLine($"  Líneas actualizadas: {filasAfectadas}");
                infoRecalculo.AppendLine($"  ✓ Cambios guardados correctamente via SQL directo");

                // Recargar las líneas desde la BD para que EF tenga los valores actualizados
                infoRecalculo.AppendLine($"  Recargando líneas desde BD...");
                if (pedido.LinPedidoVtas != null)
                {
                    foreach (var linea in pedido.LinPedidoVtas)
                    {
                        await db.Entry(linea).ReloadAsync();
                    }
                }
                infoRecalculo.AppendLine($"  ✓ Líneas recargadas correctamente");

                System.Diagnostics.Debug.WriteLine(infoRecalculo.ToString());

                // Registrar en ELMAH para trazabilidad
                RegistrarAutoFixEnElmah(pedido, infoRecalculo.ToString());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  No se detectaron diferencias en las líneas del pedido {pedido.Número}");
            }

            return huboCambios;
        }

        /// <summary>
        /// Registra en ELMAH que se realizó un auto-fix de descuadre.
        /// Esto es informativo para trazabilidad, no un error crítico.
        /// </summary>
        private void RegistrarAutoFixEnElmah(CabPedidoVta pedido, string detallesRecalculo)
        {
            try
            {
                var mensajeCompleto = new StringBuilder();
                mensajeCompleto.AppendLine($"AUTO-FIX DE DESCUADRE APLICADO - Pedido {pedido.Empresa}/{pedido.Número}");
                mensajeCompleto.AppendLine();
                mensajeCompleto.AppendLine("Se detectó un error de descuadre durante la facturación.");
                mensajeCompleto.AppendLine("Se recalcularon automáticamente las líneas del pedido y se reintentó la factura.");
                mensajeCompleto.AppendLine("El usuario NO vio ningún error.");
                mensajeCompleto.AppendLine();
                mensajeCompleto.AppendLine(detallesRecalculo);

                var excepcionInfo = new System.ApplicationException(mensajeCompleto.ToString());
                excepcionInfo.Data["Pedido"] = pedido.Número;
                excepcionInfo.Data["Empresa"] = pedido.Empresa;
                excepcionInfo.Data["Cliente"] = pedido.Nº_Cliente;
                excepcionInfo.Data["TipoError"] = "AUTO_FIX_DESCUADRE";
                excepcionInfo.Data["ModoRedondeo"] = RoundingHelper.UsarAwayFromZero ? "AwayFromZero" : "ToEven";

                var httpContext = HttpContext.Current;
                if (httpContext != null)
                {
                    ErrorSignal.FromContext(httpContext).Raise(excepcionInfo, httpContext);
                }
                else
                {
                    ErrorLog.GetDefault(null)?.Log(new Error(excepcionInfo));
                }

                System.Diagnostics.Debug.WriteLine($"[ELMAH] Registrado auto-fix para pedido {pedido.Número}");
            }
            catch (Exception ex)
            {
                // Si falla el registro en ELMAH, no interrumpir el flujo
                System.Diagnostics.Debug.WriteLine($"[ELMAH] Error al registrar auto-fix: {ex.Message}");
            }
        }
    }
}