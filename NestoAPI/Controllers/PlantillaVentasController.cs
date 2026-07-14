using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/PlantillaVentas")]
    public class PlantillaVentasController : ApiController
    {
        private readonly NVEntities db;
        private readonly IServicioPlantillaVenta _servicio;
        private readonly ILectorParametrosUsuario _lectorParametros;

        // Carlos 06/07/15: lo pongo para desactivar el Lazy Loading
        public PlantillaVentasController(IServicioPlantillaVenta servicio, ILectorParametrosUsuario lectorParametros)
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            _servicio = servicio;
            _lectorParametros = lectorParametros;
        }

        // Issue #214: constructor para tests (permite inyectar un NVEntities fake).
        // #256: lector de parámetros opcional; sin él se usa lo que mande el cliente.
        internal PlantillaVentasController(IServicioPlantillaVenta servicio, NVEntities db, ILectorParametrosUsuario lectorParametros = null)
        {
            this.db = db;
            _servicio = servicio;
            _lectorParametros = lectorParametros;
        }

        // GET: api/PlantillaVentas
        //public IQueryable<LinPedidoVta> GetLinPedidoVtas()
        public IQueryable<LineaPlantillaVenta> GetPlantillaVentas(string empresa, string cliente)
        {
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }

            Cliente clienteCompleto = db.Clientes.Single(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal);

            IQueryable<LineaPlantillaVenta> lineasPlantilla = db.LinPedidoVtas
                .Join(db.Productos.Include(nameof(ClasificacionMasVendido)).Where(p => p.Empresa == empresa).Include(f => f.Familia).Include(sb => sb.SubGrupo), l => new { producto = l.Producto }, p => new { producto = p.Número }, (l, p) => new { p.Empresa, l.Nº_Cliente, l.TipoLinea, producto = p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, nombreFamilia = p.Familia1.Descripción, nombreSubGrupo = p.SubGruposProducto.Descripción, codigoBarras = p.CodBarras, l.Cantidad, l.Fecha_Albarán, p.Ficticio, p.IVA_Repercutido, p.PVP, aplicarDescuento = p.Aplicar_Dto || l.Nº_Cliente == Constantes.ClientesEspeciales.EL_EDEN || clienteCompleto.Estado == Constantes.Clientes.Estados.DISTRIBUIDOR, estadoLinea = l.Estado, grupo = p.Grupo, p.ClasificacionMasVendido }) // ojo, paso el estado del producto, no el de la línea
                .Where(l => (l.Empresa == empresa || l.Empresa == empresaBuscada.IVA_por_defecto) && l.Nº_Cliente == cliente && l.TipoLinea == 1 && !l.Ficticio && l.Estado >= 0 && l.estadoLinea == 4 && l.Fecha_Albarán >= DbFunctions.AddYears(DateTime.Today, -2) && l.grupo != Constantes.Productos.GRUPO_MATERIAS_PRIMAS) // ojo, es el estado del producto
                .GroupBy(g => new { g.producto, g.Nombre, g.Tamaño, g.UnidadMedida, g.nombreFamilia, g.Estado, g.nombreSubGrupo, g.codigoBarras, g.IVA_Repercutido, g.PVP, g.aplicarDescuento, g.ClasificacionMasVendido, g.grupo })
                .Select(x => new LineaPlantillaVenta
                {
                    producto = x.Key.producto.Trim(),
                    texto = x.Key.Nombre.Trim(),
                    tamanno = x.Key.Tamaño,
                    unidadMedida = x.Key.UnidadMedida,
                    familia = x.Key.nombreFamilia.Trim(),
                    estado = x.Key.Estado,
                    subGrupo = x.Key.nombreSubGrupo.Trim(),
                    grupo = x.Key.grupo.Trim(), // Issue #94: Sistema Ganavisiones
                    codigoBarras = x.Key.codigoBarras.Trim(),
                    cantidadVendida = x.Where(c => c.Cantidad > 0).Sum(c => c.Cantidad) ?? 0,
                    cantidadAbonada = -x.Where(c => c.Cantidad < 0).Sum(c => c.Cantidad) ?? 0,
                    fechaUltimaVenta = x.Max(f => f.Fecha_Albarán),
                    iva = x.Key.IVA_Repercutido,
                    precio = (decimal)x.Key.PVP,
                    aplicarDescuento = x.Key.aplicarDescuento || cliente == Constantes.ClientesEspeciales.EL_EDEN || clienteCompleto.Estado == Constantes.Clientes.ESTADO_DISTRIBUIDORES,
                    clasificacionMasVendidos = x.Key.ClasificacionMasVendido != null ? x.Key.ClasificacionMasVendido.Posicion : 0
                })
                .OrderBy(p => p.estado != 0)
                .ThenByDescending(g => g.fechaUltimaVenta)
                .ThenBy(p => p.clasificacionMasVendidos);

            return lineasPlantilla;
        }

        // GET: api/PlantillaVentasBuscarProducto
        //public IQueryable<LinPedidoVta> GetLinPedidoVtas()
        // Devuelve un listado de productos, filtrado por un concepto (para buscar productos que no ha comprado nunca)
        [HttpGet]
        public IQueryable<LineaPlantillaVenta> GetBuscarProducto(string empresa, string filtroProducto)
        {
            if (filtroProducto == null || filtroProducto.Length < 3)
            {
                throw new Exception("El filtro de productos debe tener al menos 3 caracteres de largo");
            }

            IQueryable<LineaPlantillaVenta> lineasPlantilla = db.Productos.Include(nameof(ClasificacionMasVendido))
                .Include(f => f.Familia)
                .Join(db.SubGruposProductoes, p => new { empresa = p.Empresa, grupo = p.Grupo, numero = p.SubGrupo }, s => new { empresa = s.Empresa, grupo = s.Grupo, numero = s.Número }, (p, s) => new { p.Empresa, p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, nombreFamilia = p.Familia1.Descripción, estadoFamilia = p.Familia1.Estado, nombreSubGrupo = p.SubGruposProducto.Descripción, cantidad = 0, ficticio = p.Ficticio, aplicarDescuento = p.Aplicar_Dto, precio = p.PVP, iva = p.IVA_Repercutido, grupo = p.Grupo, clasificacion = p.ClasificacionMasVendido, codigoBarras = p.CodBarras })
                .Join(db.ProveedoresProductoes, p => new { empresa = p.Empresa, producto = p.Número }, r => new { empresa = r.Empresa, producto = r.Nº_Producto }, (p, r) => new { p.Empresa, p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, p.nombreFamilia, p.estadoFamilia, p.nombreSubGrupo, cantidad = 0, p.ficticio, p.aplicarDescuento, p.precio, p.iva, r.ReferenciaProv, p.grupo, p.clasificacion, p.codigoBarras })
                .Where(p => p.Empresa == empresa && p.Estado >= 0 && !p.ficticio && p.grupo != Constantes.Productos.GRUPO_MATERIAS_PRIMAS && (
                    p.Número.Contains(filtroProducto) ||
                    p.Nombre.Contains(filtroProducto) ||
                    p.nombreFamilia.Contains(filtroProducto) ||
                    p.nombreSubGrupo.Contains(filtroProducto) ||
                    p.ReferenciaProv.Contains(filtroProducto) ||
                    p.codigoBarras.Contains(filtroProducto)
                ))
                .GroupBy(g => new { g.Número, g.Nombre, g.Tamaño, g.UnidadMedida, g.nombreFamilia, g.estadoFamilia, g.Estado, g.nombreSubGrupo, g.aplicarDescuento, g.precio, g.iva, g.clasificacion, g.codigoBarras, g.grupo })
                .OrderBy(p => p.Key.Estado != 0).ThenBy(p => p.Key.estadoFamilia != 0).ThenBy(p => p.Key.clasificacion.Posicion)
                .Select(x => new LineaPlantillaVenta
                {
                    producto = x.Key.Número.Trim(),
                    texto = x.Key.Nombre.Trim(),
                    tamanno = x.Key.Tamaño,
                    unidadMedida = x.Key.UnidadMedida,
                    familia = x.Key.nombreFamilia.Trim(),
                    estado = x.Key.Estado,
                    subGrupo = x.Key.nombreSubGrupo.Trim(),
                    grupo = x.Key.grupo.Trim(), // Issue #94: Sistema Ganavisiones
                    codigoBarras = x.Key.codigoBarras.Trim(),
                    cantidadVendida = 0,
                    cantidadAbonada = 0,
                    fechaUltimaVenta = DateTime.MinValue,
                    aplicarDescuento = x.Key.aplicarDescuento,
                    iva = x.Key.iva,
                    precio = x.Key.precio ?? 0,
                    clasificacionMasVendidos = x.Key.clasificacion != null ? x.Key.clasificacion.Posicion : 0
                });


            return lineasPlantilla;
        }

        [HttpGet]
        [Route("Buscar")]
        public async Task<IHttpActionResult> GetBuscarProductoContextual(string q, bool usarBusquedaConAND = false)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest("Debe proporcionar un filtro de búsqueda.");
            }
            var lineasPlantilla = await _servicio.BusquedaContextual(q, usarBusquedaConAND);

            return Ok(lineasPlantilla);
        }

        // Devuelve las posibles direcciones de entrega del pedido
        [HttpGet]
        public IQueryable<DireccionesEntregaClienteDTO> GetDireccionesEntrega(string empresa, string clienteDirecciones)
        {
            IQueryable<DireccionesEntregaClienteDTO> result = GetDireccionesEntrega(empresa, clienteDirecciones, 0);
            return result;
        }
        // Devuelve las posibles direcciones de entrega del pedido
        [HttpGet]
        public IQueryable<DireccionesEntregaClienteDTO> GetDireccionesEntrega(string empresa, string clienteDirecciones, decimal totalPedido)
        {
            Cliente clienteDireccionPorDefecto = db.Clientes
                .Where(c => c.Empresa == empresa && c.Estado >= 0 && c.Nº_Cliente == clienteDirecciones && c.ClientePrincipal)
                .FirstOrDefault();

            // Issue #214: si el cliente no tiene contacto principal (o tuviera varios), no desreferenciar
            // null dentro de la proyección. Capturamos el contacto por defecto como valor local: cuando es
            // null, ninguna dirección queda marcada como "por defecto" en vez de devolver 500 (NRE).
            string contactoDefecto = clienteDireccionPorDefecto?.ContactoDefecto;

            IQueryable<DireccionesEntregaClienteDTO> clientes = db.Clientes
                .Where(c => c.Empresa == empresa && c.Estado >= 0 && c.Nº_Cliente == clienteDirecciones)
                .Select(clienteEncontrado => new DireccionesEntregaClienteDTO
                {
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    direccion = clienteEncontrado.Dirección.Trim(),
                    esDireccionPorDefecto = clienteEncontrado.Contacto == contactoDefecto,
                    estado = clienteEncontrado.Estado,
                    iva = clienteEncontrado.IVA.Trim(),
                    mantenerJunto = clienteEncontrado.MantenerJunto,
                    noComisiona = clienteEncontrado.NoComisiona,
                    nombre = clienteEncontrado.Nombre.Trim(),
                    poblacion = clienteEncontrado.Población.Trim(),
                    provincia = clienteEncontrado.Provincia.Trim(),
                    servirJunto = clienteEncontrado.ServirJunto,
                    vendedor = clienteEncontrado.Vendedor.Trim(),
                    periodoFacturacion = clienteEncontrado.PeriodoFacturación.Trim(),
                    ccc = clienteEncontrado.CCC.Trim(),
                    ruta = clienteEncontrado.Ruta.Trim(),
                    formaPago = clienteEncontrado.CondPagoClientes.OrderByDescending(c => c.ImporteMínimo).FirstOrDefault(c => c.ImporteMínimo <= totalPedido).FormaPago.Trim(),
                    plazosPago = clienteEncontrado.CondPagoClientes.OrderByDescending(c => c.ImporteMínimo).FirstOrDefault(c => c.ImporteMínimo <= totalPedido).PlazosPago.Trim(),
                    tieneCorreoElectronico = clienteEncontrado.PersonasContactoClientes.Any(p => !string.IsNullOrEmpty(p.CorreoElectrónico) && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO),
                    tieneFacturacionElectronica = clienteEncontrado.PersonasContactoClientes.Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO),
                    nif = clienteEncontrado.CIF_NIF != null ? clienteEncontrado.CIF_NIF.Trim() : string.Empty,
                });



            return clientes.OrderBy(c => c.contacto);
        }

        [HttpGet]
        public IQueryable<UltimasVentasProductoClienteDTO> GetUltimasVentasProductoCliente(string empresa, string clienteUltimasVentas, string productoUltimasVentas)
        {
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }

            List<UltimasVentasProductoClienteDTO> ventas = db.LinPedidoVtas
                .Where(c => (c.Empresa == empresa || c.Empresa == empresaBuscada.IVA_por_defecto) && c.Nº_Cliente == clienteUltimasVentas && c.Producto == productoUltimasVentas && c.Fecha_Albarán != null)
                .Select(ventasEncontradas => new UltimasVentasProductoClienteDTO
                {
                    fecha = (DateTime)ventasEncontradas.Fecha_Albarán,
                    cantidad = (short)ventasEncontradas.Cantidad,
                    precioBruto = ventasEncontradas.Cantidad != 0 ? (decimal)(ventasEncontradas.Bruto / ventasEncontradas.Cantidad) : 0,
                    descuentos = ventasEncontradas.SumaDescuentos,
                    precioNeto = ventasEncontradas.Cantidad != 0 ? (decimal)(ventasEncontradas.Base_Imponible / ventasEncontradas.Cantidad) : 0

                })
                .OrderByDescending(f => f.fecha)
                .Take(10)
                .ToList();

            return ventas.AsQueryable();
        }

        [HttpGet]
        [ResponseType(typeof(StockProductoPlantillaDTO))]
        public async Task<IHttpActionResult> GetCargarStock(string empresa, string almacen, string productoStock)
        {
            /*
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }
            */

            StockProductoPlantillaDTO datosStock = new StockProductoPlantillaDTO();
            ProductoPlantillaDTO productoNuevo = new ProductoPlantillaDTO(productoStock, db);
            datosStock.stock = productoNuevo.Stock(almacen);
            datosStock.cantidadDisponible = productoNuevo.CantidadDisponible(almacen);
            datosStock.cantidadPendienteRecibir = productoNuevo.CantidadPendienteRecibir();

            GestorStocks gestorStocks = new GestorStocks(new ServicioGestorStocks());
            datosStock.StockDisponibleTodosLosAlmacenes = gestorStocks.UnidadesDisponiblesTodosLosAlmacenes(productoStock);

            // Cargamos la imagen del producto
            datosStock.urlImagen = await ProductoDTO.RutaImagen(productoStock);

            return Ok(datosStock);
        }


        [HttpGet]
        [ResponseType(typeof(PrecioProductoDTO))]
        public async Task<IHttpActionResult> GetCargarPrecio(string empresa, string cliente, string contacto, string productoPrecio, short cantidad, bool aplicarDescuento)
        {
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }

            PrecioProductoDTO datosPrecio = new PrecioProductoDTO();
            Producto producto = await db.Productos.Where(p => p.Empresa == empresa && p.Número == productoPrecio).SingleOrDefaultAsync();
            if (producto.Estado < 0)
            {
                throw new Exception("Producto nulo");
            }

            PrecioDescuentoProducto precio = new PrecioDescuentoProducto
            {
                precioCalculado = datosPrecio.precio,
                descuentoCalculado = datosPrecio.descuento,
                producto = producto,
                cliente = cliente,
                contacto = contacto,
                cantidad = cantidad,
                aplicarDescuento = aplicarDescuento
            };

            if (cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL)
            {
                var porcentajeIVA = 1.21M;
                if (producto.IVA_Repercutido == Constantes.Empresas.IVA_REDUCIDO)
                {
                    porcentajeIVA = 1.1m;
                }
                precio.precioCalculado = await ProductoDTO.LeerPrecioPublicoFinal(productoPrecio, db) / porcentajeIVA;
            }
            else
            {
                GestorPrecios.calcularDescuentoProducto(precio);
            }

            datosPrecio.precio = precio.precioCalculado;
            datosPrecio.descuento = precio.descuentoCalculado;
            datosPrecio.aplicarDescuento = precio.aplicarDescuento;

            return Ok(datosPrecio);
        }

        [HttpGet]
        [ResponseType(typeof(PrecioProductoDTO))]
        public async Task<IHttpActionResult> GetComprobarCondiciones(string empresa, string producto, bool aplicarDescuento, decimal precio, decimal descuento, short cantidad, short cantidadOferta)
        {
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }

            Producto productoEncontrado = await db.Productos.Where(p => p.Empresa == empresa && p.Número == producto).SingleOrDefaultAsync();
            if (productoEncontrado.Estado < 0)
            {
                throw new Exception("Producto nulo");
            }

            //decimal precio = datosPrecio.precio;
            //decimal descuento = datosPrecio.descuento;
            PrecioDescuentoProducto datos = new PrecioDescuentoProducto
            {
                precioCalculado = precio,
                descuentoCalculado = descuento,
                producto = productoEncontrado,
                cantidad = cantidad,
                cantidadOferta = cantidadOferta,
                aplicarDescuento = aplicarDescuento
            };

            bool condicionesAprobadas = GestorPrecios.comprobarCondiciones(datos);

            PrecioProductoDTO datosPrecio = new PrecioProductoDTO
            {
                aplicarDescuento = datos.aplicarDescuento,
                precio = datos.precioCalculado,
                descuento = datos.descuentoCalculado,
                motivo = datos.motivo
            };

            return Ok(datosPrecio);
        }

        // Devuelve los numeros de los pedidos que tiene pendientes este cliente
        [HttpGet]
        public List<int> GetPedidosPendientes(string empresa, string clientePendientes)
        {
            IQueryable<int> pedidos = db.LinPedidoVtas
                .Where(c => c.Empresa == empresa && c.Estado >= -1 && c.Estado <= 1 && c.Nº_Cliente == clientePendientes)
                .Select(l => l.Número)
                .Distinct();

            return pedidos.ToList();
        }

        [HttpPost]
        [Route("PonerStock")]
        public List<LineaPlantillaVenta> PonerStock(PonerStockParam param)
        {
            if (param == null)
            {
                return new List<LineaPlantillaVenta>();
            }
            List<LineaPlantillaVenta> resultado = PonerStockLineas(param);
            if (param.Ordenar)
            {
                // Ordenar por suma de cantidadDisponible de todos los almacenes
                return resultado.OrderByDescending(l =>
                    l.stocks != null ? l.stocks.Sum(s => s.cantidadDisponible) : l.cantidadDisponible
                ).ToList();
            }
            return resultado;
        }

        // #257: nº máximo de productos por lote en las consultas agrupadas de stock. Un IN()
        // con miles de valores genera un SQL enorme y lento en EF6; en lotes siguen siendo un
        // puñado de consultas frente a las miles del bucle antiguo.
        internal const int TAMANO_LOTE_STOCKS = 1000;

        private List<LineaPlantillaVenta> PonerStockLineas(PonerStockParam param)
        {
            if (param == null || param.Lineas == null)
            {
                return new List<LineaPlantillaVenta>();
            }

            // #256: la API es la fuente de verdad de los almacenes a consultar: manda el
            // parámetro de usuario AlmacenesPlantillaVenta (CSV) si existe; lo que envíe el
            // cliente queda como fallback (los clientes antiguos siguen mandando los tres
            // hardcodeados, pero el usuario que elija uno solo obtiene el ahorro igualmente).
            List<string> almacenesConsultar = LeerAlmacenesDelUsuario()
                ?? (param.Almacenes != null && param.Almacenes.Count > 0
                    ? param.Almacenes
                    : !string.IsNullOrEmpty(param.Almacen)
                        ? new List<string> { param.Almacen }
                        : new List<string>());

            // #257: antes se lanzaban 3 consultas por (producto × almacén) más 2 por producto
            // (para una plantilla de 200 productos y 3 almacenes, ~2.200 round-trips secuenciales
            // a SQL Server). Ahora el stock y el reservado de TODOS los productos/almacenes se
            // traen en 2 consultas agrupadas (por lotes) y el resto se resuelve en memoria con
            // los mismos valores: disponible = stock - reservado.
            List<string> productos = param.Lineas
                .Select(l => l.producto?.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            Dictionary<string, int> stocks = LeerCantidadesAgrupadas(productos, almacenesConsultar, reservado: false);
            Dictionary<string, int> reservados = LeerCantidadesAgrupadas(productos, almacenesConsultar, reservado: true);

            foreach (LineaPlantillaVenta linea in param.Lineas)
            {
                // Poblar la lista de stocks por almacen
                linea.stocks = new List<StockAlmacenDTO>();
                foreach (string almacen in almacenesConsultar)
                {
                    int stock = CantidadDe(stocks, linea.producto, almacen);
                    int reservadoAlmacen = CantidadDe(reservados, linea.producto, almacen);
                    linea.stocks.Add(new StockAlmacenDTO
                    {
                        almacen = almacen,
                        stock = stock,
                        cantidadDisponible = stock - reservadoAlmacen
                    });
                }

                // Mantener compatibilidad: los campos legacy salen del primer almacén, que ya
                // está calculado en stocks[0] (antes se recalculaba con 2 consultas más).
                if (linea.stocks.Count > 0)
                {
                    linea.cantidadDisponible = (short)linea.stocks[0].cantidadDisponible;
                }

                // Issue #286: Calcular StockDisponibleTodosLosAlmacenes como suma de todos los almacenes
                linea.StockDisponibleTodosLosAlmacenes = linea.stocks.Sum(s => s.cantidadDisponible);
            }

            return param.Lineas;
        }

        /// <summary>
        /// #256: lee la lista de almacenes elegida por el usuario (parámetro
        /// AlmacenesPlantillaVenta, CSV) o null si no hay parámetro/usuario — en ese caso se
        /// usa lo que mande el cliente. Nunca lanza: la preferencia no debe tumbar la carga.
        /// </summary>
        private List<string> LeerAlmacenesDelUsuario()
        {
            if (_lectorParametros == null)
            {
                return null;
            }
            string usuario = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string valor;
            try
            {
                valor = _lectorParametros.LeerParametro(
                    Constantes.Empresas.EMPRESA_POR_DEFECTO, usuario, Constantes.ParametrosUsuario.ALMACENES_PLANTILLA_VENTA);
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }
            List<string> almacenes = valor.Split(',')
                .Select(a => a.Trim().ToUpper())
                .Where(a => a != "")
                .Distinct()
                .ToList();
            return almacenes.Any() ? almacenes : null;
        }

        // Clave (producto, almacén) normalizada: en BD son char con relleno de espacios.
        private static string ClaveStock(string producto, string almacen)
            => (producto?.Trim() ?? "") + "|" + (almacen?.Trim() ?? "");

        private static int CantidadDe(Dictionary<string, int> cantidades, string producto, string almacen)
            => cantidades.TryGetValue(ClaveStock(producto, almacen), out int cantidad) ? cantidad : 0;

        /// <summary>
        /// #257: suma por (producto, almacén) en una consulta agrupada por lote. Con
        /// <paramref name="reservado"/> false suma el stock (ExtractosProducto); con true suma
        /// lo reservado (LinPedidoVtas en curso o pendientes). Mismos criterios que
        /// ProductoPlantillaDTO.Stock/CantidadReservada, que consultaban producto a producto.
        /// </summary>
        private Dictionary<string, int> LeerCantidadesAgrupadas(List<string> productos, List<string> almacenes, bool reservado)
        {
            Dictionary<string, int> resultado = new Dictionary<string, int>();
            if (productos.Count == 0 || almacenes.Count == 0)
            {
                return resultado;
            }

            for (int i = 0; i < productos.Count; i += TAMANO_LOTE_STOCKS)
            {
                List<string> lote = productos.Skip(i).Take(TAMANO_LOTE_STOCKS).ToList();
                if (reservado)
                {
                    var filas = db.LinPedidoVtas
                        .Where(l => (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
                            && almacenes.Contains(l.Almacén) && lote.Contains(l.Producto)
                            && (l.Estado == Constantes.EstadosLineaVenta.EN_CURSO || l.Estado == Constantes.EstadosLineaVenta.PENDIENTE))
                        .GroupBy(l => new { l.Producto, l.Almacén })
                        .Select(g => new { g.Key.Producto, g.Key.Almacén, Cantidad = g.Sum(x => (int)x.Cantidad) })
                        .ToList();
                    foreach (var fila in filas)
                    {
                        resultado[ClaveStock(fila.Producto, fila.Almacén)] = fila.Cantidad;
                    }
                }
                else
                {
                    var filas = db.ExtractosProducto
                        .Where(e => (e.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || e.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
                            && almacenes.Contains(e.Almacén) && lote.Contains(e.Número))
                        .GroupBy(e => new { e.Número, e.Almacén })
                        .Select(g => new { g.Key.Número, g.Key.Almacén, Cantidad = g.Sum(x => (int)x.Cantidad) })
                        .ToList();
                    foreach (var fila in filas)
                    {
                        resultado[ClaveStock(fila.Número, fila.Almacén)] = fila.Cantidad;
                    }
                }
            }

            return resultado;
        }

        [HttpPost]
        [Route("ProductosBonificables")]
        [ResponseType(typeof(List<LineaPlantillaVenta>))]
        public async Task<IHttpActionResult> GetCargarProductosBonificables((string, List<LineaPlantillaVenta>) parametro)
        {
            string cliente = parametro.Item1;
            List<LineaPlantillaVenta> lineas = parametro.Item2;
            HashSet<string> productosBonificables = _servicio.CargarProductosBonificables();
            HashSet<string> productosYaComprados = _servicio.CargarProductosYaComprados(cliente);
            productosYaComprados.UnionWith(lineas.Select(l => l.producto).ToHashSet());
            productosBonificables.ExceptWith(productosYaComprados);
            List<LineaPlantillaVenta> lineasBonificables = productosBonificables.Select(i => new LineaPlantillaVenta { producto = i, estado = 1 }).ToList();
            return Ok(lineasBonificables);
        }


        public class PonerStockParam
        {
            public string Almacen { get; set; }
            public List<string> Almacenes { get; set; }
            public List<LineaPlantillaVenta> Lineas { get; set; }
            public bool Ordenar { get; set; }
        }

        /*
        // GET: api/PlantillaVentas/5
        [ResponseType(typeof(LinPedidoVta))]
        public async Task<IHttpActionResult> GetLinPedidoVta(string id)
        {
            LinPedidoVta linPedidoVta = await db.LinPedidoVtas.FindAsync(id);
            if (linPedidoVta == null)
            {
                return NotFound();
            }

            return Ok(linPedidoVta);
        }

        // PUT: api/PlantillaVentas/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutLinPedidoVta(string id, LinPedidoVta linPedidoVta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != linPedidoVta.Empresa)
            {
                return BadRequest();
            }

            db.Entry(linPedidoVta).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!LinPedidoVtaExists(id))
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

        // POST: api/PlantillaVentas
        [ResponseType(typeof(LinPedidoVta))]
        public async Task<IHttpActionResult> PostLinPedidoVta(LinPedidoVta linPedidoVta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.LinPedidoVtas.Add(linPedidoVta);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (LinPedidoVtaExists(linPedidoVta.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = linPedidoVta.Empresa }, linPedidoVta);
        }

        // DELETE: api/PlantillaVentas/5
        [ResponseType(typeof(LinPedidoVta))]
        public async Task<IHttpActionResult> DeleteLinPedidoVta(string id)
        {
            LinPedidoVta linPedidoVta = await db.LinPedidoVtas.FindAsync(id);
            if (linPedidoVta == null)
            {
                return NotFound();
            }

            db.LinPedidoVtas.Remove(linPedidoVta);
            await db.SaveChangesAsync();

            return Ok(linPedidoVta);
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

        private bool LinPedidoVtaExists(int id)
        {
            return db.LinPedidoVtas.Count(e => e.Nº_Orden == id) > 0;
        }
    }
}