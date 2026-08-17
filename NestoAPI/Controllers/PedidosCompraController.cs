using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Http.Description;
using Nesto.Modulos.PedidoCompra.Models;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosCompra;

namespace NestoAPI.Controllers
{
    public class PedidosCompraController : ApiController
    {
        private readonly IServicioFacturas _servicioFacturas;
        private readonly IGestorFacturas _gestor;
        private readonly IPedidosCompraService _servicio;

        private NVEntities db = new NVEntities();

        public PedidosCompraController()
        {
            _servicioFacturas = new ServicioFacturas();
            _gestor = new GestorFacturas(_servicioFacturas);
            _servicio = new PedidosCompraService();
        }

        internal PedidosCompraController(NVEntities db)
        {
            this.db = db;
            _servicioFacturas = new ServicioFacturas();
            _gestor = new GestorFacturas(_servicioFacturas);
            _servicio = new PedidosCompraService();
        }
        /*
        // GET: api/PedidosCompra
        public IQueryable<CabFacturaCmp> GetCabFacturasCmp()
        {
            return db.CabFacturasCmp;
        }
        */
        // GET: api/PedidosCompra/5
        [ResponseType(typeof(PedidoCompraDTO))]
        public async Task<IHttpActionResult> GetPedidoCompra(string empresa, int pedido)
        {
            try
            {
                PedidoCompraDTO pedidoCompra = await db.CabPedidosCmp.Where(p => p.Empresa == empresa && p.Número == pedido).Select(p => new PedidoCompraDTO
                {
                    Id = p.Número,
                    Empresa = p.Empresa.Trim(),
                    Proveedor = p.NºProveedor.Trim(),
                    Contacto = p.Contacto.Trim(),
                    Comentarios = p.Comentarios,
                    DiasEnServir = p.DíasEnServir,
                    FacturaProveedor = p.NºDocumentoProv != null ? p.NºDocumentoProv.Trim() : string.Empty,
                    Fecha = (DateTime)p.Fecha,
                    FormaPago = p.FormaPago != null ? p.FormaPago.Trim() : string.Empty,
                    CodigoIvaProveedor = p.IVA != null ? p.IVA.Trim() : null,
                    PlazosPago = p.PlazosPago != null ? p.PlazosPago.Trim() : string.Empty,
                    PrimerVencimiento = (DateTime)p.PrimerVencimiento,
                    PeriodoFacturacion = p.PeriodoFacturación
                }).SingleAsync().ConfigureAwait(false);

                if (pedidoCompra == null)
                {
                    return NotFound();
                }

                var parametros = db.ParametrosIVA
                    .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == pedidoCompra.CodigoIvaProveedor)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100
                    });

                pedidoCompra.Lineas = await db.LinPedidoCmps.Where(l => l.Empresa == empresa && l.Número == pedido).Select(l => new LineaPedidoCompraDTO
                {
                    Id = l.NºOrden,
                    Producto = l.Producto != null ? l.Producto.Trim() : string.Empty,
                    Grupo = l.Grupo,
                    Subgrupo = l.Subgrupo,
                    Texto = l.Texto != null ? l.Texto.Trim() : string.Empty,
                    TipoLinea = l.TipoLínea != null ? l.TipoLínea.Trim() : string.Empty,
                    Estado = l.Estado,
                    FechaRecepcion = (DateTime)l.FechaRecepción,
                    Cantidad = (int)l.Cantidad,
                    PrecioUnitario = l.Precio,
                    AplicarDescuento = l.AplicarDto,
                    DescuentoLinea = l.Descuento,
                    DescuentoProducto = l.DescuentoProducto,
                    DescuentoProveedor = l.DescuentoProveedor,
                    CodigoIvaProducto = l.IVA,
                    PorcentajeIva = parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault() != null ? parametros.Where(p => p.CodigoIvaProducto == l.IVA).FirstOrDefault().PorcentajeIvaProducto : 0,
                    PrecioTarifa = (decimal)(l.PrecioTarifa == null ? 0 : l.PrecioTarifa),
                    EstadoProducto = (int)(l.EstadoProducto == null ? 0 : l.EstadoProducto)
                }).ToListAsync().ConfigureAwait(false);

                pedidoCompra.ParametrosIva = await parametros.ToListAsync().ConfigureAwait(false);

                pedidoCompra.CorreoRecepcionPedidos = (await db.PersonasContactoProveedores.FirstOrDefaultAsync(
                    p => p.Empresa == pedidoCompra.Empresa && 
                    p.NºProveedor == pedidoCompra.Proveedor && 
                    p.Contacto == pedidoCompra.Contacto && 
                    p.Cargo == Constantes.Proveedores.PersonasContacto.RECEPCION_PEDIDOS
                ).ConfigureAwait(false))?.CorreoElectrónico?.Trim();

                return Ok(pedidoCompra);
            } catch (Exception ex)
            {
                throw ex;
            }
            
        }

        [ResponseType(typeof(List<PedidoCompraLookup>))]
        public async Task<IHttpActionResult> GetPedidosCompra()
        {
            var pedidos = db.CabPedidosCmp.Include((p) => p.LinPedidoCmps).Include((p)=> p.Proveedore)
                .Where(p => p.LinPedidoCmps.Where(l => l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado < Constantes.EstadosLineaVenta.FACTURA).Any())
                .Select(r => new PedidoCompraLookup
                {
                    Empresa = r.Empresa.Trim(),
                    Pedido = r.Número,
                    Proveedor = r.NºProveedor.Trim(),
                    Contacto = r.Contacto.Trim(),
                    Fecha = (DateTime)r.Fecha,
                    Nombre = r.Proveedore.Nombre != null ? r.Proveedore.Nombre.Trim() : string.Empty,
                    Direccion = r.Proveedore.Dirección != null ? r.Proveedore.Dirección.Trim() : string.Empty,
                    CodigoPostal = r.Proveedore.CodPostal != null ? r.Proveedore.CodPostal.Trim() : string.Empty,
                    Poblacion = r.Proveedore.Población != null ? r.Proveedore.Población.Trim() : string.Empty,
                    Provincia = r.Proveedore.Provincia != null ? r.Proveedore.Provincia.Trim() : string.Empty,
                    TieneEnviado = r.LinPedidoCmps.Where(l => l.Enviado).Any(),
                    TieneAlbaran = r.LinPedidoCmps.Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN).Any(),
                    TieneVistoBueno = r.LinPedidoCmps.Where(l => l.VistoBueno).Any(),
                    BaseImponible = r.LinPedidoCmps.Sum(l => l.BaseImponible),
                    Total = r.LinPedidoCmps.Sum(l => l.Total)
                })
                .OrderByDescending(p => p.Pedido);

            return Ok(await pedidos.ToListAsync().ConfigureAwait(false));
        }

        [ResponseType(typeof(List<PedidoCompraDTO>))]
        public async Task<IHttpActionResult> GetPedidosCompraAutomaticos(string empresa)
        {
            List<PedidoCompraDTO> lista;
            List<LineaPedidoCompraDTO> listaLineas;
            try
            {
                db.Database.Connection.Open(); // para que no cierre la sesión y siga existiendo la tabla temporal

                await db.Database.ExecuteSqlCommandAsync("prdCrearPedidoCmpAuto @Empresa, @Proveedor, @ProveedoresAInsertar",
                    new SqlParameter("Empresa", empresa),
                    new SqlParameter("Proveedor", string.Empty),
                    new SqlParameter("ProveedoresAInsertar", string.Empty)
                ).ConfigureAwait(false);
                
                string consulta = "select Empresa, Número as Id, rtrim(NºProveedor) as Proveedor, Contacto, Fecha, FormaPago, PlazosPago, PrimerVencimiento, DiasEnServir, IVA as CodigoIvaProveedor, Nombre, Direccion, PeriodoFacturacion from ##CabeceraAuto";
                lista = await db.Database.SqlQuery<PedidoCompraDTO>(consulta).ToListAsync().ConfigureAwait(false);

                string consultaLineas = "select Número as Id, rtrim(TipoLinea) TipoLinea, rtrim(Producto) Producto, FechaRecepcion, Texto, Cantidad, Cantidad as CantidadBruta, Precio as PrecioUnitario, StockMaximo, PendienteEntregar, PendienteRecibir, Stock, Multiplos, Iva as CodigoIvaProducto, Grupo, Subgrupo, AplicarDto as AplicarDescuentos, PrecioTarifa, EstadoProducto from ##LineasAuto";
                listaLineas = await db.Database.SqlQuery<LineaPedidoCompraDTO>(consultaLineas).ToListAsync().ConfigureAwait(false);

                db.Database.Connection.Close();
            } 
            catch (Exception ex)
            {
                throw ex;
            }
            

            foreach (var pedido in lista)
            {
                pedido.ParametrosIva = await db.ParametrosIVA
                    .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == pedido.CodigoIvaProveedor)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100
                    }).ToListAsync().ConfigureAwait(false);
                pedido.Lineas = listaLineas.Where(l => l.Id == pedido.Id).ToList();
                /*
                foreach (var linea in pedido.Lineas.Where(l => l.TipoLinea == Constantes.TiposLineaCompra.PRODUCTO && pedido.ParametrosIva.Where(p => p.CodigoIvaProducto == l.CodigoIvaProducto).Any()))
                {
                    linea.PorcentajeIva = pedido.ParametrosIva.Single(p => p.CodigoIvaProducto == linea.CodigoIvaProducto).PorcentajeIvaProducto;
                }
                */
                pedido.Id = 0;
            }

            return Ok(lista);
        }

        [ResponseType(typeof(LineaPedidoCompraDTO))]
        public async Task<IHttpActionResult> GetProductoCompra(string empresa, string producto, string proveedor, string ivaCabecera)
        {
            try
            {
                var productos = db.Productos.Include(p => p.ProveedoresProductoes)
                .Where(p =>
                    p.Empresa == empresa &&
                    p.Número == producto &&
                    p.Estado >= Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO &&
                    p.ProveedoresProductoes.Any(v => v.Nº_Proveedor == proveedor)
                );
                LineaPedidoCompraDTO lineaProducto;
                var lista = DatosProductosProcesados(productos, empresa, proveedor, DateTime.Now).ToList();
                if (lista.Any())
                {
                    lineaProducto = lista.Single();
                }
                else
                {
                    lineaProducto = await db.Productos.
                    Where(p => p.Empresa == empresa && p.Número == producto).
                    Select(p => new LineaPedidoCompraDTO
                    {
                        Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        TipoLinea = Constantes.TiposLineaCompra.PRODUCTO,
                        Texto = p.Nombre != null ? p.Nombre.Trim() : string.Empty,
                        CodigoIvaProducto = p.IVA_Soportado
                    }).FirstOrDefaultAsync().ConfigureAwait(false);
                }
                
                if (lineaProducto != null && !string.IsNullOrWhiteSpace(lineaProducto.CodigoIvaProducto) && lineaProducto.PorcentajeIva == 0)
                {
                    var parametroIVA = await db.ParametrosIVA.SingleAsync(
                        p => p.Empresa == empresa && p.IVA_Cliente_Prov == ivaCabecera && p.IVA_Producto == lineaProducto.CodigoIvaProducto
                        ).ConfigureAwait(false);
                    lineaProducto.PorcentajeIva = (decimal)parametroIVA.C__IVA / 100;
                }                
                
                return Ok(lineaProducto);
            } 
            catch (Exception ex)
            {
                throw ex;
            }            
        }

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/PedidosCompra/AmpliarPedidoAlStockMaximo")]
        [ResponseType(typeof(PedidoCompraDTO))]
        public async Task<IHttpActionResult> AmpliarPedidoAlStockMaximo(PedidoCompraDTO pedido)
        {
            if (pedido == null)
            {
                return null;
            }


            try
            {
                var productos = db.Productos.Include(p => p.ProveedoresProductoes)
                .Where(p =>
                    p.Empresa == pedido.Empresa &&
                    p.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO &&
                    p.ProveedoresProductoes.Any(v => v.Nº_Proveedor == pedido.Proveedor)
                );
                IEnumerable<LineaPedidoCompraDTO> productosInsertar = DatosProductosProcesados(productos, pedido.Empresa, pedido.Proveedor, pedido.Lineas.Any() ? pedido.Lineas.FirstOrDefault().FechaRecepcion : pedido.Fecha);
                productosInsertar = productosInsertar.Where(p => !pedido.Lineas.Select(l => l.Producto).Contains(p.Producto));
                var pedidoAmpliado = productosInsertar.Where(p => p.Cantidad > 0 || p.StockMaximo > 0);
                pedido.Lineas = pedido.Lineas.Concat(pedidoAmpliado).ToList();
            }
            catch (Exception ex)
            {
                throw ex;
            }
            

            return Ok(pedido);
        }

        /// <summary>
        /// NestoAPI#367: los datos de compra de cada producto (necesidad hasta el stock máximo,
        /// pendientes, descuentos y ofertas) se calculan con UNA consulta agregada sencilla por
        /// tabla y se componen en memoria. La versión anterior (GroupJoin + subconsultas
        /// correladas por producto + colecciones anidadas) generaba un único SQL de ~69K
        /// caracteres que tardaba 8-30+ segundos y agotaba el timeout; esta forma baja de 100 ms.
        /// </summary>
        private IEnumerable<LineaPedidoCompraDTO> DatosProductosProcesados(IQueryable<Producto> productos, string empresa, string proveedor, DateTime fechaRecepcion)
        {
            var datosProductos = productos
                .Select(p => new { p.Número, p.Grupo, p.SubGrupo, p.IVA_Soportado, p.Nombre, p.PVP, p.Estado })
                .ToList();
            if (!datosProductos.Any())
            {
                return new List<LineaPedidoCompraDTO>();
            }
            List<string> numeros = datosProductos.Select(p => p.Número).ToList();

            Dictionary<string, ControlStock> controles = db.ControlesStocks
                .Where(c => c.Empresa == empresa && c.Almacén == Constantes.Almacenes.ALGETE && numeros.Contains(c.Número))
                .ToList()
                .GroupBy(c => c.Número)
                .ToDictionary(g => g.Key, g => g.First());

            Dictionary<string, int> stocks = db.ExtractosProducto
                .Where(e => e.Almacén == Constantes.Almacenes.ALGETE && numeros.Contains(e.Número))
                .GroupBy(e => e.Número)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(e => (int)e.Cantidad) })
                .ToDictionary(x => x.Producto, x => x.Cantidad);

            Dictionary<string, int> pendientesEntregar = db.LinPedidoVtas
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO &&
                    l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO &&
                    numeros.Contains(l.Producto))
                .GroupBy(l => l.Producto)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(l => (int)l.Cantidad) })
                .ToDictionary(x => x.Producto, x => x.Cantidad);

            Dictionary<string, int> pendientesRecibir = db.LinPedidoCmps
                .Where(l => l.TipoLínea == Constantes.TiposLineaCompra.PRODUCTO &&
                    l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO &&
                    numeros.Contains(l.Producto))
                .GroupBy(l => l.Producto)
                .Select(g => new { Producto = g.Key, Cantidad = g.Sum(l => (int)l.Cantidad) })
                .ToDictionary(x => x.Producto, x => x.Cantidad);

            Dictionary<string, List<DescuentosProducto>> descuentos = db.DescuentosProductoes
                .Where(d => d.Empresa == empresa && d.NºProveedor == proveedor && numeros.Contains(d.Nº_Producto))
                .ToList()
                .GroupBy(d => d.Nº_Producto)
                .ToDictionary(g => g.Key, g => g.ToList());

            Dictionary<string, List<OfertaProveedor>> ofertas = db.OfertasProveedores
                .Where(o => o.Empresa == empresa && o.NºProveedor == proveedor &&
                    o.CantidadOferta != 0 && o.CantidadRegalo != 0 && numeros.Contains(o.Producto))
                .ToList()
                .GroupBy(o => o.Producto)
                .ToDictionary(g => g.Key, g => g.ToList());

            var productosInsertar = new List<LineaPedidoCompraDTO>();
            foreach (var prod in datosProductos)
            {
                _ = controles.TryGetValue(prod.Número, out ControlStock control);
                int stockMaximo = control?.StockMáximo ?? 0;
                int stock = stocks.TryGetValue(prod.Número, out int s) ? s : 0;
                int pendienteEntregar = pendientesEntregar.TryGetValue(prod.Número, out int pe) ? pe : 0;
                int pendienteRecibir = pendientesRecibir.TryGetValue(prod.Número, out int pr) ? pr : 0;
                int cantidadBruta = stockMaximo - stock + pendienteEntregar - pendienteRecibir;
                int cantidadNecesaria = cantidadBruta > 0 ? cantidadBruta : 0;
                // Sin control de stock el múltiplo es 1; un múltiplo 0 en la tabla también se
                // trata como 1 (antes petaba con división por cero al calcular el módulo).
                int multiplos = control == null || control.Múltiplos == 0 ? 1 : control.Múltiplos;

                productosInsertar.Add(new LineaPedidoCompraDTO
                {
                    Id = -1, // si ponemos id = 0 piensa que viene del datagrid y da error
                    Producto = prod.Número.Trim(),
                    Grupo = prod.Grupo,
                    Subgrupo = prod.SubGrupo,
                    // Descuentos debe asignarse ANTES que Cantidad: el setter de Cantidad aplica
                    // el descuento por cantidad (precio y descuento del tramo alcanzado).
                    Descuentos = (descuentos.TryGetValue(prod.Número, out List<DescuentosProducto> dtos) ? dtos : new List<DescuentosProducto>())
                        .Select(d => new DescuentoCantidadCompra
                        {
                            CantidadMinima = d.CantidadMínima,
                            Descuento = d.Descuento,
                            Precio = (decimal)(d.Precio ?? prod.PVP ?? 0)
                        }).ToList(),
                    Ofertas = (ofertas.TryGetValue(prod.Número, out List<OfertaProveedor> ofs) ? ofs : new List<OfertaProveedor>())
                        .Select(o => new OfertaCompra
                        {
                            CantidadCobrada = o.CantidadOferta,
                            CantidadRegalo = o.CantidadRegalo
                        }).ToList(),
                    Cantidad = cantidadNecesaria % multiplos == 0 ? cantidadNecesaria : (int)(Math.Ceiling((double)cantidadNecesaria / multiplos) * multiplos),
                    CantidadBruta = cantidadBruta,
                    Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    FechaRecepcion = fechaRecepcion,
                    CodigoIvaProducto = prod.IVA_Soportado?.Trim(),
                    TipoLinea = Constantes.TiposLineaCompra.PRODUCTO,
                    Texto = prod.Nombre?.Trim(),
                    Stock = stock,
                    StockMaximo = stockMaximo,
                    PendienteEntregar = pendienteEntregar,
                    PendienteRecibir = pendienteRecibir,
                    Multiplos = multiplos,
                    PrecioTarifa = prod.PVP ?? 0,
                    EstadoProducto = prod.Estado ?? 0
                });
            }

            return productosInsertar;
        }

        
        // PUT: api/PedidosCompra/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPedidoCompra(PedidoCompraDTO pedido)
        {
            if (!ModelState.IsValid || pedido == null)
            {
                return BadRequest(ModelState);
            }

            CabPedidoCmp cabPedidoCmp = db.CabPedidosCmp.Include(c => c.LinPedidoCmps).Single(c => c.Empresa == pedido.Empresa && c.Número == pedido.Id);

            if (string.IsNullOrEmpty(cabPedidoCmp.PathPedido) && !string.IsNullOrEmpty(pedido.PathPedido))
            {
                cabPedidoCmp.PathPedido = pedido.PathPedido;
                foreach (var linea in cabPedidoCmp.LinPedidoCmps)
                {
                    linea.Enviado = true;
                    linea.FechaRecepción = DateTime.Today.AddDays(cabPedidoCmp.DíasEnServir);
                }
            }

            db.Entry(cabPedidoCmp).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CabPedidoCmpExists(pedido.Empresa, pedido.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }
        


        // POST: api/PedidosCompra
        [ResponseType(typeof(int))]
        public async Task<IHttpActionResult> PostPedidoCompra(PedidoCompraDTO pedido)
        {
            if (!ModelState.IsValid || pedido == null)
            {
                return BadRequest(ModelState);
            }

            var cabecera = await _servicio.CrearPedido(pedido, db);

            return Ok(cabecera.Número);
            //return CreatedAtRoute("DefaultApi", new { empresa = cabFacturaCmp.Empresa, id = cabFacturaCmp.Número }, cabFacturaCmp);
        }

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/PedidosCompra/CrearAlbaranYFactura")]
        [ResponseType(typeof(CrearFacturaCmpResponse))]
        public async Task<IHttpActionResult> CrearAlbaranYFactura(CrearFacturaCmpRequest request)
        {
            if (request == null)
            {
                return null;
            }

            // NestoAPI#384: reintento ante deadlock de la operación COMPLETA (transacción y
            // contexto NUEVOS en cada intento, patrón #273: la víctima 1205 se revierte entera).
            // Caso real 17/08/26: los gastos de remesa deadlockeaban a las y cuarto contra el
            // job de Verifactu y el usuario veía el 500 con la mitad contabilizada.
            CrearFacturaCmpResponse resultado = await Infraestructure.Contabilidad.ContabilidadService
                .ReintentarSiDeadlock(() => CrearAlbaranYFacturaUnaVez(request));
            return Ok(resultado);
        }

        private async Task<CrearFacturaCmpResponse> CrearAlbaranYFacturaUnaVez(CrearFacturaCmpRequest request)
        {
            using (var db = new NVEntities())
            {
                // NestoAPI#384: guarda de idempotencia — si la factura del proveedor ya está
                // contabilizada (el reintento del usuario tras un error a mitad de lote), se
                // devuelve la existente y NO se crea nada. Así "volver a dar al botón" solo
                // crea las facturas que falten (caso real: 88130 duplicada como 88131).
                CrearFacturaCmpResponse existente = await _servicio.BuscarFacturaExistente(
                    request.Pedido?.Proveedor, request.Pedido?.FacturaProveedor, db);
                if (existente != null)
                {
                    return existente;
                }

                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        CabPedidoCmp pedido = await _servicio.CrearPedido(request.Pedido, db);
                        var fechaFactura = (DateTime)pedido.Fecha;
                        CrearFacturaCmpResponse respuesta = await _servicio.CrearAlbaranYFactura(pedido.Número, fechaFactura, db, pedido.Usuario);
                        if (request.CrearPago)
                        {
                            respuesta.Exito = false;
                            respuesta.AsientoPago = await _servicio.CrearPagoFactura(request, respuesta, db);
                            if (respuesta.AsientoPago > 0)
                            {
                                respuesta.Exito = true;
                            };
                        }

                        if (respuesta.Exito)
                        {
                            transaction.Commit();
                        }
                        else
                        {
                            // #291: si el SP ya abortó por dentro, la transacción está zombi y
                            // el Rollback normal lanzaría, pisando la respuesta de negocio.
                            transaction.RollbackSeguro();
                        }

                        return respuesta;
                    }
                    catch (Exception ex)
                    {
                        // #291: rollback seguro para que viaje SIEMPRE la excepción original
                        // (el incidente de la #287 salió como 'failed on Rollback/connection
                        // null' y ocultó el 'Invalid object name' real).
                        transaction.RollbackSeguro();
                        // #384: los deadlock viajan tal cual para que ReintentarSiDeadlock los
                        // reconozca (1205 en la cadena de InnerException) y reintente.
                        throw new Exception("No se ha podido crear la factura de compra", ex);
                    }
                }
            }
        }

        /*
        // DELETE: api/PedidosCompra/5
        [ResponseType(typeof(CabFacturaCmp))]
        public async Task<IHttpActionResult> DeleteCabFacturaCmp(string id)
        {
            CabFacturaCmp cabFacturaCmp = await db.CabFacturasCmp.FindAsync(id);
            if (cabFacturaCmp == null)
            {
                return NotFound();
            }

            db.CabFacturasCmp.Remove(cabFacturaCmp);
            await db.SaveChangesAsync();

            return Ok(cabFacturaCmp);
        }
        */

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool CabPedidoCmpExists(string empresa, int id)
        {
            return db.CabPedidosCmp.Any(e => e.Empresa == empresa && e.Número == id);
        }

        [HttpGet]
        [Route("api/PedidosCompra/FacturasContabilizadasProveedor")]
        [ResponseType(typeof(List<Models.PedidosCompra.FacturaContabilizadaProveedorDTO>))]
        public async Task<IHttpActionResult> GetFacturasContabilizadasProveedor(string proveedor, DateTime desde, DateTime hasta)
        {
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            var facturas = await db.CabFacturasCmp
                .Where(f => f.Empresa == empresa
                    && f.NºProveedor == proveedor
                    && f.Fecha >= desde && f.Fecha <= hasta
                    && f.NºDocumentoProv != null)
                .ToListAsync();
            var resultado = facturas
                .Where(f => f.NºDocumentoProv != null && f.NºDocumentoProv.Trim().Length > 0)
                .GroupBy(f => f.NºDocumentoProv.Trim())
                .Select(g => new Models.PedidosCompra.FacturaContabilizadaProveedorDTO
                {
                    NumeroDocumentoProv = g.Key,
                    NumeroFactura = int.TryParse(g.First().Número?.Trim(), out int n) ? n : 0
                })
                .ToList();
            return Ok(resultado);
        }
    }
}