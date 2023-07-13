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
using System.Web.Http;
using System.Web.Http.Cors;
using System.Web.Http.Description;
using Nesto.Modulos.PedidoCompra.Models;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosCompra;

namespace NestoAPI.Controllers
{
    public class PedidosCompraController : ApiController
    {
        private readonly IServicioFacturas servicio;
        private readonly IGestorFacturas gestor;

        private NVEntities db = new NVEntities();

        public PedidosCompraController()
        {
            servicio = new ServicioFacturas();
            gestor = new GestorFacturas(servicio);
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

        private IEnumerable<LineaPedidoCompraDTO> DatosProductosProcesados(IQueryable<Producto> productos, string empresa, string proveedor, DateTime fechaRecepcion)
        {
            var controles = db.ControlesStocks.Where(c =>
                productos.Select(p => p.Número).Contains(c.Número) &&
                c.Empresa == empresa && c.Almacén == Constantes.Almacenes.ALGETE
            );
            var stock = db.ExtractosProducto.Where(e =>
                e.Almacén == Constantes.Almacenes.ALGETE &&
                productos.Select(p => p.Número).Contains(e.Número)
            );
            var pendientesRecibir = db.LinPedidoCmps.Where(l =>
                l.TipoLínea == Constantes.TiposLineaCompra.PRODUCTO &&
                l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO &&
                productos.Select(p => p.Número).Contains(l.Producto)
            );
            var pendientesEntregar = db.LinPedidoVtas.Where(l =>
                l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO &&
                l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO &&
                productos.Select(p => p.Número).Contains(l.Producto)
            );
            var descuentosProducto = db.DescuentosProductoes.Where(d =>
                productos.Select(p => p.Número).Contains(d.Nº_Producto) &&
                d.Empresa == empresa &&
                d.NºProveedor == proveedor
            );
            var ofertas = db.OfertasProveedores.Where(d =>
                productos.Select(p => p.Número).Contains(d.Producto) &&
                d.CantidadOferta != 0 && d.CantidadRegalo != 0 &&
                d.Empresa == empresa &&
                d.NºProveedor == proveedor
            );

            var prequery = productos
                .GroupJoin(controles, prod => prod.Número, ctrl => ctrl.Número, (prod, ctrl) => new { prod = prod, ctrl = ctrl })
                .SelectMany(x => x.ctrl.DefaultIfEmpty(), 
                (x, prod) =>
                new
                {
                    EsNulo = x.ctrl.FirstOrDefault() == null,
                    StockMaximo = x.ctrl.FirstOrDefault() != null ? x.ctrl.FirstOrDefault().StockMáximo : 0,
                    Stock = stock.Where(p => p.Número == x.prod.Número).Select(e => (int)e.Cantidad).DefaultIfEmpty().Sum(),
                    PendienteEntregar = pendientesEntregar.Where(p => p.Producto == x.prod.Número).Select(p => (int)p.Cantidad).DefaultIfEmpty().Sum(),
                    PendienteRecibir = pendientesRecibir.Where(p => p.Producto == x.prod.Número).Select(p => (int)p.Cantidad).DefaultIfEmpty().Sum(),
                    Producto = x.prod.Número.Trim(),
                    Grupo = x.prod.Grupo,
                    Subgrupo = x.prod.SubGrupo,
                    Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    CodigoIvaProducto = x.prod.IVA_Soportado.Trim(),
                    TipoLinea = Constantes.TiposLineaCompra.PRODUCTO,
                    Texto = x.prod.Nombre.Trim(),
                    Multiplos = x.ctrl.FirstOrDefault() != null ? x.ctrl.FirstOrDefault().Múltiplos : 1,
                    PrecioTarifa = (decimal)x.prod.PVP,
                    EstadoProducto = (int)x.prod.Estado,
                    Descuentos = descuentosProducto.Where(d => d.Nº_Producto == x.prod.Número).Select(d => new DescuentoCantidadCompra
                    {
                        CantidadMinima = d.CantidadMínima,
                        Descuento = d.Descuento,
                        Precio = d.Precio == null ? (decimal)x.prod.PVP : (decimal)d.Precio
                    }),
                    Ofertas = ofertas.Where(d => d.Producto == x.prod.Número).Select(d => new OfertaCompra
                    {
                        CantidadCobrada = d.CantidadOferta,
                        CantidadRegalo = d.CantidadRegalo
                    })
                })
                //.Where(l => !l.EsNulo)
                .Select(l => new
                {
                    Cantidad = l.StockMaximo - l.Stock + l.PendienteEntregar - l.PendienteRecibir > 0 ? l.StockMaximo - l.Stock + l.PendienteEntregar - l.PendienteRecibir : 0,
                    CantidadBruta = l.StockMaximo - l.Stock + l.PendienteEntregar - l.PendienteRecibir,
                    Producto = l.Producto,
                    Grupo = l.Grupo,
                    Subgrupo = l.Subgrupo,
                    Estado = l.Estado,
                    CodigoIvaProducto = l.CodigoIvaProducto,
                    TipoLinea = l.TipoLinea,
                    Texto = l.Texto,
                    Multiplos = l.Multiplos,
                    StockMaximo = l.StockMaximo,
                    Stock = l.Stock,
                    PendienteEntregar = l.PendienteEntregar,
                    PendienteRecibir = l.PendienteRecibir,
                    PrecioTarifa = l.PrecioTarifa,
                    EstadoProducto = l.EstadoProducto,
                    Descuentos = l.Descuentos,
                    Ofertas = l.Ofertas
                });

            var query = prequery.AsEnumerable();

            var productosInsertar = query
                .Select(x => new LineaPedidoCompraDTO
                {
                    Id = -1, // si ponemos id = 0 piensa que viene del datagrid y da error
                    Producto = x.Producto,
                    Grupo = x.Grupo,
                    Subgrupo = x.Subgrupo,
                    Descuentos = x.Descuentos.ToList(),
                    Ofertas = x.Ofertas.ToList(),
                    Cantidad = (int)(x.Cantidad % x.Multiplos == 0 ? x.Cantidad : Math.Ceiling((double)x.Cantidad / x.Multiplos) * x.Multiplos), // Multiplos
                    CantidadBruta = x.CantidadBruta,
                    Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    FechaRecepcion = fechaRecepcion,
                    CodigoIvaProducto = x.CodigoIvaProducto,
                    TipoLinea = Constantes.TiposLineaCompra.PRODUCTO,
                    Texto = x.Texto,
                    Stock = x.Stock,
                    StockMaximo = x.StockMaximo,
                    PendienteEntregar = x.PendienteEntregar,
                    PendienteRecibir = x.PendienteRecibir,
                    Multiplos = x.Multiplos != 0 ? x.Multiplos : 1,
                    PrecioTarifa = x.PrecioTarifa,
                    EstadoProducto = x.EstadoProducto
                }
            );
            /*
            var p2 = productos.ToList();
            var c1 = controles.ToList();
            var d1 = descuentosProducto.ToList();
            var o1 = ofertas.ToList();
            var p1 = prequery.ToList();
            var q1 = query.ToList();
            var i1 = productosInsertar.ToList();
            */
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

            // El número que vamos a dar al pedido hay que leerlo de ContadoresGlobales
            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            if (pedido.Id == 0)
            {
                contador.PedidosCmp++;
                pedido.Id = contador.PedidosCmp;
            }

            CabPedidoCmp cabecera = pedido.ToCabPedidoCmp();

            db.CabPedidosCmp.Add(cabecera);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (CabPedidoCmpExists(cabecera.Empresa, cabecera.Número))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Ok(cabecera.Número);
            //return CreatedAtRoute("DefaultApi", new { empresa = cabFacturaCmp.Empresa, id = cabFacturaCmp.Número }, cabFacturaCmp);
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
    }
}