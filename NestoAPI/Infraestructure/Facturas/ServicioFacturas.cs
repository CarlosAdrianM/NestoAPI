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
        private readonly Verifactu.IServicioVerifactu servicioVerifactu;
        private readonly ILogService logService;

        // Compartido para no crear un HttpClient por cada instancia de ServicioFacturas
        private static readonly Lazy<Verifactu.IServicioVerifactu> servicioVerifactuPorDefecto =
            new Lazy<Verifactu.IServicioVerifactu>(() => new Verifactu.Verifacti.ServicioVerifacti());

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
        public ServicioFacturas(NVEntities dbExterno) : this(dbExterno, null)
        {
        }

        /// <summary>
        /// Constructor que además permite inyectar el servicio de Verifactu y el log (para tests).
        /// </summary>
        public ServicioFacturas(NVEntities dbExterno, Verifactu.IServicioVerifactu servicioVerifactu, ILogService logService = null,
            Rectificativas.IAlmacenRectificativasPendientes almacenRectificativasPendientes = null,
            Clientes.IServicioValidacionNif servicioValidacionNif = null,
            Clientes.NotificadorNifIncorrecto notificadorNif = null)
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
            this.servicioVerifactu = servicioVerifactu ?? servicioVerifactuPorDefecto.Value;
            this.logService = logService ?? new ElmahLogService();
            // Issue #87: vinculaciones de rectificativas facturadas a mano (tabla RectificativaPendiente)
            this.almacenRectificativasPendientes = almacenRectificativasPendientes
                ?? new Rectificativas.AlmacenRectificativasPendientes(db);
            // NestoAPI#327: validación del NIF contra la AEAT al facturar
            this.servicioValidacionNif = servicioValidacionNif ?? new Clientes.ServicioValidacionNif(db);
            this.notificadorNif = notificadorNif ?? new Clientes.NotificadorNifIncorrecto(db);
        }

        private readonly Rectificativas.IAlmacenRectificativasPendientes almacenRectificativasPendientes;
        private readonly Clientes.IServicioValidacionNif servicioValidacionNif;
        private readonly Clientes.NotificadorNifIncorrecto notificadorNif;

        // NestoAPI#328: bloqueo de facturación con NIF incorrecto. APAGADO de inicio (periodo
        // de gracia): se enciende en el Web.config cuando llegue la obligatoriedad (01/12/2026)
        // y las fichas estén limpias. Internal set para tests.
        internal static bool BloquearNifIncorrecto { get; set; } =
            string.Equals(System.Configuration.ConfigurationManager.AppSettings["Verifactu:BloquearNifIncorrecto"],
                "true", StringComparison.OrdinalIgnoreCase);

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

        public List<ImpagadoPendiente> CargarImpagadosPendientes(string empresa, string cliente, string numeroFactura)
        {
            return db.ExtractosCliente
                .Where(e => e.Empresa == empresa
                    && e.Número == cliente
                    && e.Nº_Documento == numeroFactura
                    && e.TipoApunte == Constantes.ExtractosCliente.TiposApunte.IMPAGADO
                    && e.ImportePdte != 0)
                .Select(e => new ImpagadoPendiente
                {
                    FechaVto = e.FechaVto ?? e.Fecha,
                    ImportePendiente = e.ImportePdte,
                    EsGastos = e.Concepto.Contains("Gastos")
                })
                .ToList();
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

        public async Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario, string usuarioAutenticado = null)
        {
            // Usar el db de la clase (puede ser externo o interno según el constructor)
            // Esto evita conflictos de concurrencia cuando se llama desde GestorFacturacionRutas
            string empresaOriginal = empresa;
            CabPedidoVta cabPedido = db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .Single(p => p.Empresa == empresa && p.Número == pedido);

            if (cabPedido.Periodo_Facturacion == Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES && !cabPedido.Agrupada)
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

            // PREVENTIVO (NestoAPI#276): la ruta del pedido debe existir en Rutas. Si no, el SP
            // prdCrearFacturaVta falla al insertar el apunte en ExtractoCliente (FK_ExtractoCliente_Rutas)
            // y encima deja el @@TRANCOUNT descuadrado (ROLLBACK sin BEGIN), lo que corrompe el estado de
            // la conexión. Cortamos antes con un mensaje accionable en vez del error críptico del SP.
            if (!string.IsNullOrWhiteSpace(cabPedido.Ruta)
                && !await db.Rutas.AnyAsync(r => r.Empresa == empresa && r.Número == cabPedido.Ruta))
            {
                throw new FacturacionException(
                    $"El pedido {pedido} no se puede facturar porque su ruta '{cabPedido.Ruta.Trim()}' no existe en la tabla de Rutas. " +
                    "Corrija la ruta del cliente/pedido antes de facturar.",
                    "FACTURACION_RUTA_INEXISTENTE",
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario);
            }

            // PREVENTIVO (NestoAPI#304): si la cabecera lleva CCC, debe existir en la tabla CCC
            // para (empresa, cliente, contacto) — el SP copia esos campos tal cual a CabFacturaVta
            // y si la cuenta se borró/renumeró después de crear el pedido revienta con
            // FK_CabFacturaVta_CCC ("No se ha podido crear la cabecera de factura" + ruido de
            // transacciones, familia #291/#296). Cortamos antes con un mensaje accionable.
            if (!string.IsNullOrWhiteSpace(cabPedido.CCC)
                && !await db.CCCs.AnyAsync(c => c.Empresa == empresa && c.Cliente == cabPedido.Nº_Cliente
                    && c.Contacto == cabPedido.Contacto && c.Número == cabPedido.CCC))
            {
                throw new FacturacionException(
                    $"El pedido {pedido} no se puede facturar porque su cuenta bancaria (CCC '{cabPedido.CCC.Trim()}') " +
                    $"ya no existe para el cliente {cabPedido.Nº_Cliente?.Trim()}/{cabPedido.Contacto?.Trim()}. " +
                    "Corrija la cuenta de cobro del pedido antes de facturar.",
                    "FACTURACION_CCC_INEXISTENTE",
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario);
            }

            // PREVENTIVO (NestoAPI#338): el SP rechaza el pedido entero si alguna línea viva no
            // tiene el visto bueno ("Hay lineas que no tienen el visto bueno dado"), lo que
            // bloqueaba ventas de mostrador (caso 922687: el POST fuerza vistoBueno=true desde
            // #45 pero el PUT no, y una ampliación grabó líneas a false). Criterio de Carlos:
            // al facturar, todas las líneas llevan visto bueno. Solo se tocan líneas aún sin
            // albarán (las posteriores no se pueden modificar y además ya facturaron).
            // La facturación de rutas no cambia: su selección ya exige VistoBueno=true.
            List<LinPedidoVta> lineasSinVistoBueno = cabPedido.LinPedidoVtas
                .Where(l => !l.VtoBueno
                    && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .ToList();
            if (lineasSinVistoBueno.Count > 0)
            {
                foreach (LinPedidoVta lineaSinVistoBueno in lineasSinVistoBueno)
                {
                    lineaSinVistoBueno.VtoBueno = true;
                }
                _ = await db.SaveChangesAsync();
            }

            // NestoAPI#327/#328: validar el NIF que se declarará en la factura (el del cliente
            // PRINCIPAL) contra el censo de la AEAT. Best-effort: un fallo aquí nunca impide
            // facturar durante el periodo de gracia. Con el flag de #328 encendido (a partir
            // del 01/12/2026), un NIF incorrecto BLOQUEA la factura antes de crearla.
            Clientes.ResultadoValidacionNif validacionNif = null;
            try
            {
                validacionNif = await servicioValidacionNif.ValidarPrincipal(cabPedido.Nº_Cliente, usuario);
            }
            catch (Exception exValidacionNif)
            {
                ElmahHelper.Log(new Exception(
                    $"ValidacionNif: fallo al validar el NIF del cliente {cabPedido.Nº_Cliente?.Trim()} " +
                    $"al facturar el pedido {pedido}: {exValidacionNif.Message}", exValidacionNif));
            }
            if (validacionNif?.Estado == Clientes.EstadoValidacionNif.Incorrecto && BloquearNifIncorrecto)
            {
                throw new FacturacionException(
                    $"El pedido {pedido} no se puede facturar: el NIF '{validacionNif.Nif}' del cliente " +
                    $"{cabPedido.Nº_Cliente?.Trim()} no está registrado en el censo de la AEAT " +
                    $"({validacionNif.ResultadoAeat ?? "NO IDENTIFICADO"}). Corrija el NIF en la ficha " +
                    "del cliente (se revalida automáticamente) y vuelva a facturar.",
                    "FACTURACION_NIF_INCORRECTO",
                    empresa: empresa,
                    pedido: pedido,
                    usuario: usuario);
            }

            // PREVENTIVO: Recalcular líneas ANTES de llamar al stored procedure
            // Esto evita errores de descuadre por diferencias de redondeo entre C# y SQL.
            // El recálculo se hace antes porque después del SP (incluso con rollback),
            // las líneas pueden estar temporalmente en estado 4 y el trigger no permite modificarlas.
            bool seAplicoAutoFix = await RecalcularLineasPedido(db, cabPedido, usuario, usuarioAutenticado);
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

                // Issue #87: si el pedido era una rectificativa copiada SIN facturar automáticamente,
                // sus vinculaciones esperan en RectificativaPendiente: poblarlas ahora y enviar a
                // Verifactu. Best-effort: nunca rompe la facturación.
                await VincularRectificativaPendiente(empresa, resultadoProcedimiento, pedido);

                // Verifactu (#34): enviar la factura recién creada a la AEAT vía Verifacti.
                // Best-effort: si Verifacti falla, la factura sigue creada y el error queda en ELMAH.
                await EnviarFacturaAVerifactu(empresa, resultadoProcedimiento);

                var respuestaFactura = new CrearFacturaResponseDTO
                {
                    NumeroFactura = resultadoProcedimiento,
                    Empresa = empresa,
                    NumeroPedido = pedido
                };

                // NestoAPI#327 (periodo de gracia hasta 01/12/2026): la factura SE HA creado,
                // pero el que factura tiene que enterarse de que con Verifactu obligatorio no
                // podría, y el vendedor (CC administración) recibe el correo pidiendo el NIF.
                if (validacionNif?.Estado == Clientes.EstadoValidacionNif.Incorrecto)
                {
                    respuestaFactura.Avisos.Add(
                        $"La factura {resultadoProcedimiento} se ha creado, PERO el NIF '{validacionNif.Nif}' " +
                        $"del cliente {cabPedido.Nº_Cliente?.Trim()} no está registrado en la AEAT " +
                        $"({validacionNif.ResultadoAeat ?? "NO IDENTIFICADO"}). A partir del 01/12/2026 esta " +
                        "factura NO podría emitirse y el pedido quedaría retenido. Se ha enviado un correo al " +
                        "vendedor (con copia a administración) para solicitar el NIF correcto.");
                    try
                    {
                        await notificadorNif.Enviar(Constantes.Empresas.EMPRESA_POR_DEFECTO,
                            cabPedido.Nº_Cliente, $"la factura {resultadoProcedimiento}", esFactura: true,
                            nif: validacionNif.Nif, nombre: validacionNif.Nombre,
                            resultadoAeat: validacionNif.ResultadoAeat);
                    }
                    catch (Exception exCorreoNif)
                    {
                        ElmahHelper.Log(new Exception(
                            $"ValidacionNif: no se pudo enviar el correo de NIF incorrecto del cliente " +
                            $"{cabPedido.Nº_Cliente?.Trim()} (factura {resultadoProcedimiento}): {exCorreoNif.Message}", exCorreoNif));
                    }
                }

                return respuestaFactura;
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

            // Copiar referencia de compra del cliente (PO) del pedido a la factura (congelado)
            factura.SuPedido = cabPedido.SuPedido;

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Envía la factura recién creada a Verifactu si su serie tramita (issue #34).
        /// Best-effort: NUNCA lanza. Si el envío falla, la factura queda creada con los campos
        /// Verifactu a null (así un proceso posterior puede reintentar buscando VerifactuUUID null)
        /// y el error se loguea a ELMAH.
        /// Las rectificativas (series RV/RC) NO se envían desde aquí: en el momento de CrearFactura
        /// las vinculaciones de LinFacturaVtaRectificacion aún no están guardadas (GestorCopiaPedidos
        /// las guarda después) y se enviarían sin facturas rectificadas. Se envían con
        /// <see cref="EnviarRectificativaAVerifactu"/> tras guardar las vinculaciones (issue #36).
        /// </summary>
        /// <param name="empresa">Empresa de la factura (puede ser la espejo por traspaso)</param>
        /// <param name="numeroFactura">Número de la factura recién creada</param>
        // NestoAPI#329: devuelve la respuesta del proveedor (null si no procedía enviar), para
        // que el job de reintentos pueda reaccionar al motivo (p. ej. rechazo por NIF).
        internal async Task<Verifactu.VerifactuResponse> EnviarFacturaAVerifactu(string empresa, string numeroFactura)
        {
            return await EnviarAVerifactu(empresa, numeroFactura, permitirRectificativas: false);
        }

        /// <summary>
        /// Issue #36: envía una factura RECTIFICATIVA a Verifactu, con las facturas originales
        /// identificadas desde LinFacturaVtaRectificacion. Debe llamarse DESPUÉS de guardar las
        /// vinculaciones (GestorCopiaPedidos). Mismo best-effort e idempotencia que el envío normal.
        /// </summary>
        public async Task<Verifactu.VerifactuResponse> EnviarRectificativaAVerifactu(string empresa, string numeroFactura)
        {
            return await EnviarAVerifactu(empresa, numeroFactura, permitirRectificativas: true);
        }

        /// <summary>NestoAPI#346: rechazo_previo=X — el alta inicial fue rechazada (cuadro
        /// operativo AEAT para reenviar como subsanación un registro que nunca se aceptó).</summary>
        private const string RECHAZO_PREVIO_ALTA_RECHAZADA = "X";

        /// <summary>NestoAPI#347: valor de ParámetrosIVA.Pais que marca un código nacional.</summary>
        private const string PAIS_NACIONAL = "ES";

        private static string Truncar(string texto, int maximo)
        {
            return texto != null && texto.Length > maximo ? texto.Substring(0, maximo) : texto;
        }

        private async Task<Verifactu.VerifactuResponse> EnviarAVerifactu(string empresa, string numeroFactura, bool permitirRectificativas)
        {
            try
            {
                if (!servicioVerifactu.EstaHabilitado)
                {
                    return null;
                }

                CabFacturaVta factura = await db.CabsFacturasVtas
                    .Include(f => f.LinPedidoVtas)
                    .FirstOrDefaultAsync(f => f.Empresa == empresa && f.Número == numeroFactura);
                if (factura == null)
                {
                    return null;
                }

                ISerieFacturaVerifactu serie = RegistroSeriesVerifactu.ObtenerSerie(factura.Serie);
                if (serie == null || !serie.TramitaVerifactu)
                {
                    return null;
                }

                if (serie.EsRectificativa && !permitirRectificativas)
                {
                    return null; // desde CrearFactura las vinculaciones aún no existen (#36)
                }

                if (!string.IsNullOrWhiteSpace(factura.VerifactuUUID))
                {
                    return null; // ya se envió (idempotencia)
                }

                List<Verifactu.VerifactuFacturaRectificada> facturasRectificadas = null;
                if (serie.EsRectificativa)
                {
                    facturasRectificadas = await CargarFacturasRectificadas(empresa, numeroFactura);
                    if (!facturasRectificadas.Any())
                    {
                        // Sin vinculaciones no se puede identificar qué se rectifica: no se envía
                        // (queda con VerifactuUUID null, reintentable). El alta manual de
                        // vinculaciones para rectificativas creadas fuera de CopiarFactura es #38/#87.
                        logService.LogError($"Verifactu: la rectificativa {numeroFactura} no tiene " +
                            "vinculaciones en LinFacturaVtaRectificacion; no se envía (pendiente #38/#87)");
                        return null;
                    }
                }

                // NestoAPI#347: si ParámetrosIVA tiene país para el código de IVA de la factura,
                // ese dato MANDA sobre la heurística (B21 Bélgica y G21 España comparten el 21%:
                // solo el país persistido los distingue con certeza). Sin país (NULL, pendiente de
                // confirmar en BD) el mapeador cae a la lista blanca de códigos nacionales + tipo.
                bool? esOssPorPais = null;
                string codigoIvaFactura = factura.IVA?.Trim();
                if (!string.IsNullOrEmpty(codigoIvaFactura))
                {
                    string paisIva = await db.ParametrosIVA
                        .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == codigoIvaFactura && p.Pais != null)
                        .Select(p => p.Pais)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(paisIva))
                    {
                        esOssPorPais = paisIva.Trim() != PAIS_NACIONAL;
                    }
                }

                Verifactu.VerifactuFacturaRequest request = Verifactu.MapeadorFacturaVerifactu.Mapear(factura, facturasRectificadas, esOssPorPais);

                // Issue #325: una factura simplificada (F2) por encima del límite legal no puede
                // documentarse como tal. No se bloquea la facturación (el importe ya está emitido),
                // pero tiene que saltar el aviso para revisarla.
                if (request.TipoFactura == Verifactu.MapeadorFacturaVerifactu.TIPO_FACTURA_SIMPLIFICADA &&
                    Math.Abs(request.ImporteTotal) > Verifactu.MapeadorFacturaVerifactu.LIMITE_FACTURA_SIMPLIFICADA)
                {
                    logService.LogError($"Verifactu: la factura {numeroFactura} se declara como SIMPLIFICADA (F2) " +
                        $"pero su importe ({request.ImporteTotal:C}) supera el límite legal de " +
                        $"{Verifactu.MapeadorFacturaVerifactu.LIMITE_FACTURA_SIMPLIFICADA:C}: hay que revisarla.");
                }

                // NestoAPI#346: el create de Verifacti solo admite fecha_expedicion de hoy (con
                // tolerancia observada de ayer). Una factura más antigua sin declarar (NIF
                // corregido días después, caída del proveedor...) va por el camino legal de la
                // SUBSANACIÓN (PUT modify, admite fechas pasadas): rechazo_previo=X porque el alta
                // nunca llegó a aceptarse. Pendiente confirmar con soporte de Verifacti que el
                // modify vale cuando el create ni siquiera pasó su filtro previo; si no, el error
                // quedará en ELMAH (una sola vez, deduplicado) y lo veremos en la sombra.
                bool fueraDeVentanaCreate = factura.Fecha.Date < DateTime.Today.AddDays(-1);
                Verifactu.VerifactuResponse respuesta = fueraDeVentanaCreate
                    ? await servicioVerifactu.ModificarFacturaAsync(request, RECHAZO_PREVIO_ALTA_RECHAZADA)
                    : await servicioVerifactu.EnviarFacturaAsync(request);

                // NestoAPI#346/#347: rastro persistente de cada intento — el motivo del atasco
                // queda consultable en la factura (VerifactuUltimoError/UltimoIntento) y cada
                // registro declarado queda auditado en VerifactuRegistros con su payload.
                bool exitoso = respuesta != null && respuesta.Exitoso;
                factura.VerifactuUltimoIntento = DateTime.Now;
                factura.VerifactuUltimoError = exitoso
                    ? null
                    : Truncar($"({respuesta?.CodigoError}) {respuesta?.MensajeError}", 500);
                db.VerifactuRegistros.Add(new VerifactuRegistro
                {
                    Empresa = empresa?.Trim(),
                    NumeroFactura = numeroFactura?.Trim(),
                    TipoRegistro = fueraDeVentanaCreate ? "Subsanacion" : "Alta",
                    RechazoPrevio = fueraDeVentanaCreate ? RECHAZO_PREVIO_ALTA_RECHAZADA : null,
                    Payload = Newtonsoft.Json.JsonConvert.SerializeObject(request),
                    RespuestaUuid = respuesta?.Uuid,
                    RespuestaEstado = Truncar(respuesta?.Estado, 50),
                    RespuestaError = exitoso ? null : Truncar(respuesta?.MensajeError, 500),
                    Exitoso = exitoso,
                    FechaEnvio = DateTime.Now,
                    Usuario = Truncar(UsuarioAuditoriaHelper.Resolver(
                        System.Web.HttpContext.Current?.User, "NestoAPI"), 30)
                });

                string claveRuido = $"{empresa}|{numeroFactura?.Trim()}";
                if (exitoso)
                {
                    factura.VerifactuUUID = respuesta.Uuid;
                    factura.VerifactuHuella = respuesta.Huella;
                    factura.VerifactuQR = respuesta.QrBase64;
                    factura.VerifactuURL = respuesta.Url;
                    factura.VerifactuEstado = respuesta.Estado;
                    Verifactu.DeduplicadorErroresVerifactu.Limpiar(claveRuido);
                }
                else
                {
                    // NestoAPI#346: el mismo error de la misma factura solo se loguea la primera
                    // vez (el job reintenta cada pasada y repetirlo inundaba ELMAH).
                    string mensaje = $"Verifactu: error al enviar la factura {numeroFactura} " +
                        $"({respuesta?.CodigoError}): {respuesta?.MensajeError}";
                    if (Verifactu.DeduplicadorErroresVerifactu.EsNovedad(claveRuido, mensaje))
                    {
                        logService.LogError(mensaje);
                    }
                }
                _ = await db.SaveChangesAsync();
                return respuesta;
            }
            catch (Exception ex)
            {
                logService.LogError($"Verifactu: error inesperado al enviar la factura {numeroFactura}", ex);
                return null;
            }
        }

        /// <summary>
        /// Issue #87: convierte la metadata de RectificativaPendiente (copia hecha sin facturar
        /// automáticamente) en filas de LinFacturaVtaRectificacion al facturar el pedido, y envía la
        /// rectificativa a Verifactu. Solo vincula las líneas que realmente entraron en ESTA factura
        /// (una facturación parcial dejaría el resto pendiente). Best-effort: nunca lanza.
        /// </summary>
        internal async Task VincularRectificativaPendiente(string empresa, string numeroFactura, int numeroPedido)
        {
            try
            {
                List<Models.Rectificativas.RectificativaPendienteDTO> pendientes =
                    await almacenRectificativasPendientes.LeerPendientes(empresa, numeroPedido);
                if (!pendientes.Any())
                {
                    return;
                }

                string numeroFacturaLimpio = numeroFactura?.Trim();
                List<int> numerosLinea = pendientes.Select(p => p.NumeroLinea).ToList();
                List<int> lineasFacturadas = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa && l.Número == numeroPedido &&
                        numerosLinea.Contains(l.Nº_Orden) && l.Nº_Factura.Trim() == numeroFacturaLimpio)
                    .Select(l => l.Nº_Orden)
                    .ToListAsync();
                if (!lineasFacturadas.Any())
                {
                    return;
                }

                foreach (Models.Rectificativas.RectificativaPendienteDTO pendiente in
                    pendientes.Where(p => lineasFacturadas.Contains(p.NumeroLinea)))
                {
                    _ = db.LinFacturaVtaRectificaciones.Add(new LinFacturaVtaRectificacion
                    {
                        Empresa = empresa,
                        NumeroFactura = numeroFactura,
                        NumeroLinea = pendiente.NumeroLinea,
                        FacturaOriginalNumero = pendiente.FacturaOriginalNumero,
                        FacturaOriginalLinea = pendiente.FacturaOriginalLinea,
                        CantidadRectificada = pendiente.CantidadRectificada
                    });
                }
                _ = await db.SaveChangesAsync();
                await almacenRectificativasPendientes.BorrarPendientes(empresa, numeroPedido, lineasFacturadas);

                // Con las vinculaciones ya en su sitio, la rectificativa puede declararse (#36)
                await EnviarRectificativaAVerifactu(empresa, numeroFactura);
            }
            catch (Exception ex)
            {
                logService.LogError($"Verifactu: error al vincular la rectificativa pendiente del pedido {numeroPedido} (factura {numeroFactura})", ex);
            }
        }

        /// <summary>
        /// Issue #36: facturas originales que rectifica una rectificativa, leídas de las
        /// vinculaciones por línea de LinFacturaVtaRectificacion (una entrada por factura original
        /// distinta, con su serie/número/fecha reales de CabFacturaVta).
        /// </summary>
        private async Task<List<Verifactu.VerifactuFacturaRectificada>> CargarFacturasRectificadas(string empresa, string numeroFacturaRectificativa)
        {
            string numeroRectificativa = numeroFacturaRectificativa?.Trim();
            List<string> numerosOriginales = await db.LinFacturaVtaRectificaciones
                .Where(r => r.Empresa == empresa && r.NumeroFactura.Trim() == numeroRectificativa)
                .Select(r => r.FacturaOriginalNumero)
                .Distinct()
                .ToListAsync();

            var facturasRectificadas = new List<Verifactu.VerifactuFacturaRectificada>();
            foreach (string numeroOriginal in numerosOriginales)
            {
                string numero = numeroOriginal?.Trim();
                CabFacturaVta original = await db.CabsFacturasVtas
                    .FirstOrDefaultAsync(f => f.Empresa == empresa && f.Número.Trim() == numero);
                if (original == null)
                {
                    logService.LogError($"Verifactu: no se encuentra la factura original {numero} " +
                        $"vinculada a la rectificativa {numeroFacturaRectificativa}");
                    continue;
                }
                facturasRectificadas.Add(new Verifactu.VerifactuFacturaRectificada
                {
                    Serie = original.Serie?.Trim(),
                    Numero = Verifactu.MapeadorFacturaVerifactu.NumeroSinSerie(original.Número, original.Serie),
                    FechaExpedicion = original.Fecha
                });
            }
            return facturasRectificadas;
        }

        /// <summary>
        /// Recalcula los importes de las líneas de un pedido usando SQL directo.
        /// Esto garantiza que el redondeo sea exactamente el mismo que usa el procedimiento almacenado.
        /// Issue #242/#243: Unificar redondeo entre NestoAPI y procedimientos almacenados.
        /// </summary>
        /// <param name="db">Contexto de Entity Framework</param>
        /// <param name="pedido">Pedido a recalcular</param>
        /// <returns>True si se realizaron cambios, False si no hubo cambios</returns>
        private async Task<bool> RecalcularLineasPedido(NVEntities db, CabPedidoVta pedido, string usuario = null, string usuarioAutenticado = null)
        {
            if (pedido == null)
            {
                return false;
            }

            // Capturar totales ANTES del recálculo
            decimal baseImponibleAntes = pedido.LinPedidoVtas?
                .Where(l => l.Estado >= -1 && l.Estado <= 2)
                .Sum(l => l.Base_Imponible) ?? 0;
            decimal totalAntes = pedido.LinPedidoVtas?
                .Where(l => l.Estado >= -1 && l.Estado <= 2)
                .Sum(l => l.Total) ?? 0;

            var infoRecalculo = new StringBuilder();
            infoRecalculo.AppendLine($"=== AUTO-FIX: Recálculo de líneas pedido {pedido.Empresa}/{pedido.Número} ===");
            infoRecalculo.AppendLine($"Fecha/Hora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrWhiteSpace(usuario))
            {
                infoRecalculo.AppendLine($"Usuario:    {usuario}");
            }
            infoRecalculo.AppendLine();

            // NestoAPI#171: antes del UPDATE, capturar qué líneas y qué columna van a
            // cambiar, para poder documentarlo en ELMAH. Antes el log solo mostraba
            // agregados y podía salir "Dif: 0,00" si había compensación interlínea, lo
            // que generaba confusión en diagnóstico.
            //
            // Tras analizar prdCrearFacturaVta (29/04/26): el SP NO usa ImporteDto ni
            // ImporteIVA de las líneas (los recalcula desde Bruto y BaseImponible),
            // pero SÍ lee Total para el check de cuadre (tolerancia 0,02). Por eso el
            // UPDATE conserva las cuatro columnas en el WHERE y el diagnóstico imprime
            // solo aquellas con drift real, omitiendo el ruido de "Dif 0,0000".
            var diferenciasPorLinea = await LeerDiferenciasLineasAsync(db, pedido.Empresa, pedido.Número).ConfigureAwait(false);
            if (diferenciasPorLinea.Any())
            {
                infoRecalculo.AppendLine("  DIFERENCIAS DETECTADAS POR LÍNEA:");
                foreach (var d in diferenciasPorLinea)
                {
                    var partes = new List<string>();
                    AppendDriftLog(partes, "BI",   d.BaseImponibleActual, d.BaseImponibleNueva);
                    AppendDriftLog(partes, "Dto",  d.ImporteDtoActual,    d.ImporteDtoNuevo);
                    AppendDriftLog(partes, "%IVA", d.PorcentajeIVAActual, d.PorcentajeIVANuevo);
                    AppendDriftLog(partes, "IVA",  d.ImporteIVAActual,    d.ImporteIVANuevo);
                    AppendDriftLog(partes, "Tot",  d.TotalActual,         d.TotalNuevo);
                    infoRecalculo.AppendLine($"    Nº Orden {d.NumeroOrden} [{d.Producto?.Trim()}]: " + string.Join(" | ", partes));
                }
                infoRecalculo.AppendLine();
            }

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
            //
            // NestoAPI#342 (pedido 921468): una línea puede llegar con PorcentajeIVA/PorcentajeRE
            // incoherentes con su código de IVA (ej: IVA='G21' pero PorcentajeIVA=0). La línea es
            // internamente coherente (Total = BI + BI*0), así que el auto-fix original no la tocaba,
            // pero el SP recalcula el IVA desde ParametrosIVA (código producto x código cliente) y
            // descuadra. Por eso el porcentaje canónico se toma de ParametrosIVA cuando existe la
            // combinación; si no existe (línea sin IVA, cliente exento...), se respeta el de la línea.
            // OJO unidades: [% IVA] va en porcentaje en ambos sitios, pero [% RE] va en porcentaje en
            // ParametrosIVA (5.20) y en fracción en LinPedidoVta.PorcentajeRE (0.0520) → dividir /100.
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
                        , 2) AS NuevaBI,
                        COALESCE(pi.[% IVA], l.PorcentajeIVA) AS NuevoPorcentajeIVA,
                        COALESCE(pi.[% RE] / 100.0, l.PorcentajeRE) AS NuevoPorcentajeRE
                    FROM LinPedidoVta l
                    INNER JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.Número = l.Número
                    LEFT JOIN ParametrosIVA pi ON pi.Empresa = l.Empresa
                        AND pi.[IVA Producto] = l.IVA
                        AND pi.[IVA Cliente/Prov] = c.IVA
                    WHERE l.Empresa = @empresa
                      AND l.Número = @numero
                      AND l.Estado BETWEEN -1 AND 2
                )
                UPDATE l
                SET
                    [Base Imponible] = bc.NuevaBI,
                    ImporteDto = bc.NuevoImporteDto,
                    PorcentajeIVA = bc.NuevoPorcentajeIVA,
                    PorcentajeRE = bc.NuevoPorcentajeRE,
                    ImporteIVA = bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0,
                    ImporteRE = bc.NuevaBI * bc.NuevoPorcentajeRE,
                    Total = bc.NuevaBI + (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0) + (bc.NuevaBI * bc.NuevoPorcentajeRE)
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
                      -- NestoAPI#342: porcentajes incoherentes con ParametrosIVA
                      ABS(l.PorcentajeIVA - bc.NuevoPorcentajeIVA) > 0.0001
                      OR
                      ABS(l.PorcentajeRE - bc.NuevoPorcentajeRE) > 0.0001
                      OR
                      -- Detectar diferencia en ImporteIVA
                      ABS(l.ImporteIVA - (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0)) > 0.0001
                      OR
                      -- Detectar diferencia en Total
                      ABS(l.Total - (bc.NuevaBI + (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0) + (bc.NuevaBI * bc.NuevoPorcentajeRE))) > 0.0001
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

                // Capturar totales DESPUÉS del recálculo
                decimal baseImponibleDespues = pedido.LinPedidoVtas?
                    .Where(l => l.Estado >= -1 && l.Estado <= 2)
                    .Sum(l => l.Base_Imponible) ?? 0;
                decimal totalDespues = pedido.LinPedidoVtas?
                    .Where(l => l.Estado >= -1 && l.Estado <= 2)
                    .Sum(l => l.Total) ?? 0;

                // Mostrar diferencias de importes
                infoRecalculo.AppendLine();
                infoRecalculo.AppendLine($"  IMPORTES:");
                infoRecalculo.AppendLine($"    Base Imponible: {baseImponibleAntes:N2} → {baseImponibleDespues:N2} (Dif: {baseImponibleDespues - baseImponibleAntes:+0.00;-0.00;0.00})");
                infoRecalculo.AppendLine($"    Total:          {totalAntes:N2} → {totalDespues:N2} (Dif: {totalDespues - totalAntes:+0.00;-0.00;0.00})");

                System.Diagnostics.Debug.WriteLine(infoRecalculo.ToString());

                // Registrar en ELMAH para trazabilidad
                RegistrarAutoFixEnElmah(pedido, infoRecalculo.ToString(), usuario, usuarioAutenticado);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  No se detectaron diferencias en las líneas del pedido {pedido.Número}");
            }

            return huboCambios;
        }

        // Solo añade la columna al log si el drift es relevante (>0.00005 redondeado a 4
        // decimales). Si actual y nuevo coinciden, se omite para no ensuciar el mensaje.
        private static void AppendDriftLog(List<string> partes, string etiqueta, decimal actual, decimal nuevo)
        {
            decimal diferencia = nuevo - actual;
            if (Math.Abs(diferencia) < 0.00005m)
            {
                return;
            }
            partes.Add($"{etiqueta} {actual:N4}→{nuevo:N4} (Dif {diferencia:+0.0000;-0.0000;0.0000})");
        }

        // NestoAPI#171: DTO local para capturar qué líneas y qué columnas van a cambiar
        // antes de lanzar el UPDATE, para que el log de ELMAH describa el desajuste.
        internal class LineaDiagnosticoAutoFix
        {
            public int NumeroOrden { get; set; }
            public string Producto { get; set; }
            public decimal BaseImponibleActual { get; set; }
            public decimal BaseImponibleNueva { get; set; }
            public decimal ImporteDtoActual { get; set; }
            public decimal ImporteDtoNuevo { get; set; }
            public decimal ImporteIVAActual { get; set; }
            public decimal ImporteIVANuevo { get; set; }
            public decimal TotalActual { get; set; }
            public decimal TotalNuevo { get; set; }
            public decimal PorcentajeIVAActual { get; set; }
            public decimal PorcentajeIVANuevo { get; set; }
        }

        private async Task<List<LineaDiagnosticoAutoFix>> LeerDiferenciasLineasAsync(NVEntities db, string empresa, int numero)
        {
            // Misma fórmula que el UPDATE de RecalcularLineasPedido. Se duplica a
            // propósito en modo SELECT para no interferir con la transacción: solo
            // queremos una foto de lo que va a cambiar, no modificar nada todavía.
            const string sql = @"
                ;WITH BaseCalculada AS (
                    SELECT
                        l.[Nº Orden],
                        ROUND(l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        , 2) AS NuevoImporteDto,
                        ROUND(l.Bruto, 2) - ROUND(l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        , 2) AS NuevaBI,
                        COALESCE(pi.[% IVA], l.PorcentajeIVA) AS NuevoPorcentajeIVA,
                        COALESCE(pi.[% RE] / 100.0, l.PorcentajeRE) AS NuevoPorcentajeRE
                    FROM LinPedidoVta l
                    INNER JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.Número = l.Número
                    LEFT JOIN ParametrosIVA pi ON pi.Empresa = l.Empresa
                        AND pi.[IVA Producto] = l.IVA
                        AND pi.[IVA Cliente/Prov] = c.IVA
                    WHERE l.Empresa = @empresa
                      AND l.Número = @numero
                      AND l.Estado BETWEEN -1 AND 2
                )
                SELECT
                    l.[Nº Orden] AS NumeroOrden,
                    l.Producto AS Producto,
                    CAST(l.[Base Imponible] AS decimal(18,4)) AS BaseImponibleActual,
                    CAST(bc.NuevaBI AS decimal(18,4)) AS BaseImponibleNueva,
                    CAST(l.ImporteDto AS decimal(18,4)) AS ImporteDtoActual,
                    CAST(bc.NuevoImporteDto AS decimal(18,4)) AS ImporteDtoNuevo,
                    CAST(l.ImporteIVA AS decimal(18,4)) AS ImporteIVAActual,
                    CAST(bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0 AS decimal(18,4)) AS ImporteIVANuevo,
                    CAST(l.Total AS decimal(18,4)) AS TotalActual,
                    CAST(bc.NuevaBI + (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0) + (bc.NuevaBI * bc.NuevoPorcentajeRE) AS decimal(18,4)) AS TotalNuevo,
                    CAST(l.PorcentajeIVA AS decimal(18,4)) AS PorcentajeIVAActual,
                    CAST(bc.NuevoPorcentajeIVA AS decimal(18,4)) AS PorcentajeIVANuevo
                FROM LinPedidoVta l
                INNER JOIN BaseCalculada bc ON l.[Nº Orden] = bc.[Nº Orden]
                WHERE l.Empresa = @empresa
                  AND l.Número = @numero
                  AND l.Estado BETWEEN -1 AND 2
                  AND (
                      ABS(l.[Base Imponible] - bc.NuevaBI) > 0.0001
                      OR ABS(l.ImporteDto - bc.NuevoImporteDto) > 0.0001
                      OR ABS(l.PorcentajeIVA - bc.NuevoPorcentajeIVA) > 0.0001
                      OR ABS(l.PorcentajeRE - bc.NuevoPorcentajeRE) > 0.0001
                      OR ABS(l.ImporteIVA - (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0)) > 0.0001
                      OR ABS(l.Total - (bc.NuevaBI + (bc.NuevaBI * bc.NuevoPorcentajeIVA / 100.0) + (bc.NuevaBI * bc.NuevoPorcentajeRE))) > 0.0001
                  )
                ORDER BY l.[Nº Orden]";

            return await db.Database
                .SqlQuery<LineaDiagnosticoAutoFix>(sql,
                    new SqlParameter("@empresa", empresa),
                    new SqlParameter("@numero", numero))
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Registra en ELMAH que se realizó un auto-fix de descuadre.
        /// Esto es informativo para trazabilidad, no un error crítico.
        /// </summary>
        /// <param name="usuario">Usuario que se mandó al SP (parámetro de negocio, puede venir del cuerpo del request).</param>
        /// <param name="usuarioAutenticado">Identidad autenticada del request. Si se proporciona, se usa para el campo User
        /// del registro de ELMAH (no es spoofeable). Si es null, se cae a HttpContext.Current.User?.Identity?.Name.</param>
        private void RegistrarAutoFixEnElmah(CabPedidoVta pedido, string detallesRecalculo, string usuario = null, string usuarioAutenticado = null)
        {
            try
            {
                var mensajeCompleto = new StringBuilder();
                mensajeCompleto.AppendLine($"AUTO-FIX DE DESCUADRE APLICADO - Pedido {pedido.Empresa}/{pedido.Número}");
                if (!string.IsNullOrWhiteSpace(usuario))
                {
                    mensajeCompleto.AppendLine($"Usuario: {usuario}");
                }
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
                excepcionInfo.Data["Usuario"] = usuario ?? "(desconocido)";
                excepcionInfo.Data["TipoError"] = "AUTO_FIX_DESCUADRE";
                excepcionInfo.Data["ModoRedondeo"] = RoundingHelper.UsarAwayFromZero ? "AwayFromZero" : "ToEven";

                // Construimos el Error a mano para poder forzar User con la identidad
                // autenticada cuando esté disponible. No usamos ErrorSignal.Raise porque
                // ELMAH lee User de HttpContext.Current.User y, tras los await con
                // ConfigureAwait(false), ese principal se pierde con frecuencia y el
                // registro queda anónimo aunque el JWT viniera correcto.
                var httpContext = HttpContext.Current;
                var error = httpContext != null
                    ? new Error(excepcionInfo, httpContext)
                    : new Error(excepcionInfo);
                if (!string.IsNullOrWhiteSpace(usuarioAutenticado))
                {
                    error.User = usuarioAutenticado;
                }
                // NestoAPI#182: el auto-fix se registra en plena facturación, dentro de la
                // transacción: sin Suppress, la conexión de ELMAH se alista en ella y el log se
                // pierde (o revienta) justo en el diagnóstico que más falta hace.
                ElmahHelper.SinTransaccion(() => ErrorLog.GetDefault(httpContext)?.Log(error));

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