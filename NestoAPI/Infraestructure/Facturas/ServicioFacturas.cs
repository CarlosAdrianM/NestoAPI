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
        private readonly NVEntities db = new NVEntities();

        public CabFacturaVta CargarCabFactura(string empresa, string numeroFactura)
        {
            return db.CabsFacturasVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroFactura);
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
            using (NVEntities db = new NVEntities())
            {
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

                if (string.IsNullOrEmpty(cabPedido.IVA))
                {
                    throw new FacturacionException(
                        $"El pedido {pedido} no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
                        "FACTURACION_IVA_FALTANTE",
                        empresa: empresa,
                        pedido: pedido,
                        usuario: usuario);
                }

                // Intentar crear la factura con auto-retry si hay descuadre
                const int maxIntentos = 2;
                Exception ultimaExcepcion = null;
                bool seAplicoAutoFix = false;

                for (int intento = 1; intento <= maxIntentos; intento++)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  → Ejecutando prdCrearFacturaVta para pedido {pedido} (intento {intento}/{maxIntentos})");

                        // Crear nuevos parámetros en cada intento (no se pueden reutilizar)
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

                        // Si se aplicó auto-fix, registrar el éxito
                        if (seAplicoAutoFix)
                        {
                            System.Diagnostics.Debug.WriteLine($"  ✓ AUTO-FIX EXITOSO: Factura {resultadoProcedimiento} creada tras recalcular líneas");
                        }

                        // Retornar DTO con empresa final (puede ser diferente a la original)
                        return new CrearFacturaResponseDTO
                        {
                            NumeroFactura = resultadoProcedimiento,
                            Empresa = empresa,
                            NumeroPedido = pedido
                        };
                    }
                    catch (Exception ex)
                    {
                        ultimaExcepcion = ex;
                        string mensajeError = ex.Message + (ex.InnerException?.Message ?? "");

                        System.Diagnostics.Debug.WriteLine($"  ✗ ERROR al crear factura (intento {intento}): {mensajeError}");

                        // Verificar si es un error de descuadre y si podemos reintentar
                        if (EsErrorDescuadre(mensajeError) && intento < maxIntentos)
                        {
                            System.Diagnostics.Debug.WriteLine($"  → Detectado error de descuadre. Intentando auto-fix...");

                            // Intentar recalcular las líneas del pedido
                            bool huboCambios = await RecalcularLineasPedido(db, cabPedido);

                            if (huboCambios)
                            {
                                seAplicoAutoFix = true;
                                System.Diagnostics.Debug.WriteLine($"  → Auto-fix aplicado. Reintentando creación de factura...");
                                // El bucle continuará con el siguiente intento
                            }
                            else
                            {
                                // No hubo cambios que hacer, el descuadre tiene otra causa
                                System.Diagnostics.Debug.WriteLine($"  → No se detectaron diferencias de redondeo. El descuadre tiene otra causa.");
                                break; // Salir del bucle, no tiene sentido reintentar
                            }
                        }
                        else
                        {
                            // No es descuadre o ya no hay más reintentos
                            break;
                        }
                    }
                }

                // Si llegamos aquí, todos los intentos fallaron
                if (ultimaExcepcion is SqlException sqlEx)
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
                        .WithData("SeIntentoAutoFix", seAplicoAutoFix);
                }
                else
                {
                    throw new FacturacionException(
                        $"Error inesperado al crear la factura del pedido {pedido}: {ultimaExcepcion?.Message}",
                        "FACTURACION_ERROR_INESPERADO",
                        ultimaExcepcion,
                        empresa: empresa,
                        pedido: pedido,
                        usuario: usuario)
                        .WithData("SeIntentoAutoFix", seAplicoAutoFix);
                }
            }
        }

        /// <summary>
        /// Detecta si el mensaje de error indica un problema de descuadre.
        /// </summary>
        private bool EsErrorDescuadre(string mensajeError)
        {
            if (string.IsNullOrEmpty(mensajeError))
            {
                return false;
            }

            var mensajeLower = mensajeError.ToLower();
            return mensajeLower.Contains("descuadre") ||
                   mensajeLower.Contains("cuadre") ||
                   (mensajeLower.Contains("diferencia") && mensajeLower.Contains("total"));
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

            // Ejecutar UPDATE SQL directo - misma fórmula que el procedimiento almacenado
            // Solo actualiza líneas con diferencia > 0.001 para evitar cambios innecesarios
            string sql = @"
                UPDATE l
                SET
                    [Base Imponible] = ROUND(l.Bruto - (l.Bruto *
                        CASE WHEN l.[Aplicar Dto] = 1
                            THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                            ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                        END
                    ), 2),
                    ImporteIVA = ROUND(
                        ROUND(l.Bruto - (l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        ), 2) * l.PorcentajeIVA / 100
                    , 2),
                    ImporteRE = ROUND(
                        ROUND(l.Bruto - (l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        ), 2) * l.PorcentajeRE
                    , 2),
                    Total = ROUND(
                        ROUND(l.Bruto - (l.Bruto *
                            CASE WHEN l.[Aplicar Dto] = 1
                                THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                                ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                            END
                        ), 2) * (1 + l.PorcentajeIVA/100.0 + l.PorcentajeRE)
                    , 2)
                FROM LinPedidoVta l
                WHERE l.Empresa = @empresa
                  AND l.Número = @numero
                  AND l.Estado BETWEEN -1 AND 1
                  AND ABS(l.[Base Imponible] - ROUND(l.Bruto - (l.Bruto *
                        CASE WHEN l.[Aplicar Dto] = 1
                            THEN 1 - (1-l.DescuentoCliente)*(1-l.DescuentoProducto)*(1-l.Descuento)*(1-l.DescuentoPP)
                            ELSE 1 - (1-l.Descuento)*(1-l.DescuentoPP)
                        END
                    ), 2)) > 0.001";

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