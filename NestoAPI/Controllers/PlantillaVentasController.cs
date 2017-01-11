using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;
using NestoAPI.Infraestructure;
using System.Web;
using System.Data.Entity.Core.Objects;

namespace NestoAPI.Controllers
{
    public class PlantillaVentasController : ApiController
    {
        private NVEntities db = new NVEntities();
        
        // Carlos 06/07/15: lo pongo para desactivar el Lazy Loading
        public PlantillaVentasController()
        {
            db.Configuration.LazyLoadingEnabled = false;
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
            
            List<LineaPlantillaVenta> lineasPlantilla = db.LinPedidoVtas
                .Join(db.Productos.Where(p => p.Empresa == empresa).Include(f => f.Familia).Include(sb => sb.SubGrupo), l => new { producto = l.Producto }, p => new { producto = p.Número }, (l, p) => new { p.Empresa, l.Nº_Cliente, l.TipoLinea, producto = p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, nombreFamilia = p.Familia1.Descripción, nombreSubGrupo = p.SubGruposProducto.Descripción, l.Cantidad, l.Fecha_Albarán, p.Ficticio, p.IVA_Repercutido, p.PVP, aplicarDescuento = p.Aplicar_Dto, estadoLinea = l.Estado}) // ojo, paso el estado del producto, no el de la línea
                .Where(l => (l.Empresa == empresa || l.Empresa == empresaBuscada.IVA_por_defecto) && l.Nº_Cliente == cliente && l.TipoLinea == 1 && !l.Ficticio && l.Estado >= 0 && l.estadoLinea == 4 && l.Fecha_Albarán >= DbFunctions.AddYears(DateTime.Today,-3)) // ojo, es el estado del producto
                .GroupBy(g => new { g.producto, g.Nombre, g.Tamaño, g.UnidadMedida, g.nombreFamilia, g.Estado, g.nombreSubGrupo, g.IVA_Repercutido, g.PVP, g.aplicarDescuento })
                .Select(x => new LineaPlantillaVenta
                {
                    producto = x.Key.producto.Trim(),
                    texto = x.Key.Nombre.Trim(),
                    tamanno = x.Key.Tamaño,
                    unidadMedida = x.Key.UnidadMedida,
                    familia = x.Key.nombreFamilia.Trim(),
                    estado = x.Key.Estado,
                    subGrupo = x.Key.nombreSubGrupo.Trim(),
                    cantidadVendida = x.Where(c => c.Cantidad>0).Sum(c => c.Cantidad) ?? 0,
                    cantidadAbonada = -x.Where(c => c.Cantidad < 0).Sum(c => c.Cantidad) ?? 0,
                    fechaUltimaVenta = x.Max(f => f.Fecha_Albarán),
                    iva = x.Key.IVA_Repercutido,
                    precio = (decimal)x.Key.PVP, 
                    aplicarDescuento = x.Key.aplicarDescuento
                })
                .OrderByDescending(g => g.fechaUltimaVenta)
                .ToList();

            
            return lineasPlantilla.AsQueryable();
        }


        // GET: api/PlantillaVentasBuscarProducto
        //public IQueryable<LinPedidoVta> GetLinPedidoVtas()
        // Devuelve un listado de productos, filtrado por un concepto (para buscar productos que no ha comprado nunca)
        [HttpGet]
        public IQueryable<LineaPlantillaVenta> GetBuscarProducto(string empresa, string filtroProducto)
        {
            if (filtroProducto.Length<3)
            {
                throw new Exception("El filtro de productos debe tener al menos 3 caracteres de largo");
            }

            List<LineaPlantillaVenta> lineasPlantilla = db.Productos
                .Include(f => f.Familia)
                .Join(db.SubGruposProductoes, p => new { empresa = p.Empresa, grupo = p.Grupo, numero = p.SubGrupo }, s => new { empresa = s.Empresa, grupo = s.Grupo, numero = s.Número }, (p, s) => new { p.Empresa, p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, nombreFamilia = p.Familia1.Descripción, nombreSubGrupo = p.SubGruposProducto.Descripción, cantidad = 0, ficticio = p.Ficticio, aplicarDescuento = p.Aplicar_Dto, precio = p.PVP, iva = p.IVA_Repercutido })
                .Join(db.ProveedoresProductoes, p => new { empresa = p.Empresa, producto = p.Número }, r => new { empresa = r.Empresa, producto = r.Nº_Producto }, (p, r) => new { p.Empresa, p.Número, p.Estado, p.Nombre, p.Tamaño, p.UnidadMedida, p.nombreFamilia, p.nombreSubGrupo , cantidad = 0, p.ficticio, p.aplicarDescuento , p.precio, p.iva , r.ReferenciaProv })
                .Where(p => p.Empresa == empresa && p.Estado >= 0 && !p.ficticio && (
                    p.Número.Contains(filtroProducto) ||
                    p.Nombre.Contains(filtroProducto) ||
                    p.nombreFamilia.Contains(filtroProducto) ||
                    p.nombreSubGrupo.Contains(filtroProducto) ||
                    p.ReferenciaProv.Contains(filtroProducto)
                ))
                .GroupBy(g => new { g.Número, g.Nombre, g.Tamaño, g.UnidadMedida, g.nombreFamilia, g.Estado, g.nombreSubGrupo, g.aplicarDescuento, g.precio, g.iva })
                .Select(x => new LineaPlantillaVenta
                {
                    producto = x.Key.Número.Trim(),
                    texto = x.Key.Nombre.Trim(),
                    tamanno = x.Key.Tamaño,
                    unidadMedida = x.Key.UnidadMedida,
                    familia = x.Key.nombreFamilia.Trim(),
                    estado = x.Key.Estado,
                    subGrupo = x.Key.nombreSubGrupo.Trim(),
                    cantidadVendida = 0,
                    cantidadAbonada = 0,
                    fechaUltimaVenta = DateTime.MinValue,
                    aplicarDescuento = x.Key.aplicarDescuento,
                    iva = x.Key.iva,
                    precio = (decimal)x.Key.precio
                })
                .ToList();


            return lineasPlantilla.AsQueryable();
        }

        // Devuelve las posibles direcciones de entrega del pedido
        [HttpGet]
        public IQueryable<DireccionesEntregaClienteDTO> GetDireccionesEntrega(string empresa, string clienteDirecciones)
        {
            Cliente clienteDireccionPorDefecto = db.Clientes
                .Where(c => (c.Empresa == empresa && c.Estado >= 0 && c.Nº_Cliente == clienteDirecciones && c.ClientePrincipal))
                .SingleOrDefault();
            
            List<DireccionesEntregaClienteDTO> clientes = db.Clientes
                .Where(c => (c.Empresa == empresa && c.Estado >= 0 && c.Nº_Cliente == clienteDirecciones))
                .Select(clienteEncontrado => new DireccionesEntregaClienteDTO
                {
                    clientePrincipal = clienteEncontrado.ClientePrincipal,
                    codigoPostal = clienteEncontrado.CodPostal.Trim(),
                    comentarioPicking = clienteEncontrado.ComentarioPicking.Trim(),
                    comentarioRuta = clienteEncontrado.ComentarioRuta.Trim(),
                    comentarios = clienteEncontrado.Comentarios,
                    contacto = clienteEncontrado.Contacto.Trim(),
                    direccion = clienteEncontrado.Dirección.Trim(),
                    esDireccionPorDefecto = clienteDireccionPorDefecto.ContactoDefecto == clienteEncontrado.Contacto,
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
                    formaPago = clienteEncontrado.CondPagoClientes.FirstOrDefault(c => c.ImporteMínimo == 0).FormaPago,
                    plazosPago = clienteEncontrado.CondPagoClientes.FirstOrDefault(c => c.ImporteMínimo == 0).PlazosPago.Trim()
                }).ToList();

            

            return clientes.AsQueryable();
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
                .Where(c => ((c.Empresa == empresa || c.Empresa == empresaBuscada.IVA_por_defecto) && c.Nº_Cliente == clienteUltimasVentas && c.Producto == productoUltimasVentas && c.Fecha_Albarán != null))
                .Select(ventasEncontradas=> new UltimasVentasProductoClienteDTO
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
        [ResponseType(typeof(StockProductoDTO))]
        public async Task<IHttpActionResult> GetCargarStock(string empresa, string almacen, string productoStock)
        {
            /*
            Empresa empresaBuscada = db.Empresas.Where(e => e.Número == empresa).SingleOrDefault();
            if (empresaBuscada.IVA_por_defecto == null)
            {
                throw new Exception("Empresa no válida");
            }
            */

            StockProductoDTO datosStock = new StockProductoDTO();
            ProductoDTO productoNuevo = new ProductoDTO(productoStock, db);
            datosStock.stock = productoNuevo.Stock();
            datosStock.cantidadDisponible = productoNuevo.CantidadDisponible();

            // Cargamos la imagen del producto
            using (var client = new HttpClient())
            {
                client.BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/imagenesPorReferencia.php");
                client.DefaultRequestHeaders.Accept.Clear();
                //client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = await client.GetAsync("?producto="+productoStock);
                if (response.IsSuccessStatusCode)
                {
                    datosStock.urlImagen = await response.Content.ReadAsStringAsync();
                    datosStock.urlImagen = "http://" + datosStock.urlImagen;
                }
            }

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

            //decimal precio = datosPrecio.precio;
            //decimal descuento = datosPrecio.descuento;
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
            
            GestorPrecios.calcularDescuentoProducto(precio);
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
        /*
        private void calcularDescuentoProducto(ref decimal precioCalculado, ref decimal descuentoCalculado, Producto producto, string cliente, string contacto, short cantidad, bool aplicarDescuento)
        {
            DescuentosProducto dtoProducto;

            descuentoCalculado = 0;
            precioCalculado = (decimal)producto.PVP;

            // AQUÍ CALCULA PRECIOS, NO DESCUENTOS
            //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     ' and contacto='0  ' and [nº producto]= '29487' and empresa='1  ' AND CANTIDADMÍNIMA<=1
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Contacto == contacto && d.Nº_Producto == producto.Número && d.CantidadMínima <= cantidad);
            if (dtoProducto != null && dtoProducto.Precio < precioCalculado)
            {
                precioCalculado = (decimal)dtoProducto.Precio;
            }
            //select precio from descuentosproducto with (nolock) where [nº cliente]='15191     '  and [nº producto]= '29487' and empresa='1  ' AND CantidadMínima<=1
            //select recargopvp from clientes with (nolock) where empresa='1  ' and [nº cliente]='15191     ' and contacto='0  '
            //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     ' and contacto='0  ' order by cantidadminima desc
            //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente]='15191     '  order by cantidadminima desc
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Nº_Producto == producto.Número && d.CantidadMínima <= cantidad);
            if (dtoProducto != null && dtoProducto.Precio < precioCalculado)
            {
                precioCalculado = (decimal)dtoProducto.Precio;
            }
            //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc
            dtoProducto = db.DescuentosProductoes.OrderByDescending(d => d.CantidadMínima).FirstOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Producto == producto.Número && d.CantidadMínima <= cantidad && d.Nº_Cliente == null && d.NºProveedor == null);
            if (dtoProducto != null && dtoProducto.Precio < precioCalculado)
            {
                precioCalculado = (decimal)dtoProducto.Precio;
            }


            // Si no tiene el aplicar descuento marcado, solo calcula precios especiales, pero no descuentos
            if (!aplicarDescuento)
            {
                return;
            }

            // CALCULA DESCUENTOS
            //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
            //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null
            //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Nº_Producto == producto.Número && d.CantidadMínima <= cantidad && d.NºProveedor == null && d.Familia == null);
            if (dtoProducto != null && dtoProducto.Descuento > descuentoCalculado)
            {
                descuentoCalculado = dtoProducto.Descuento;
            }

            //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente]='15191     ' and nºproveedor is null and familia is null

            // AGAIN AND AGAIN AND AGAIN...
            //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and grupoproducto='PEL' and cantidadmínima<=1 and familia is null and nºproveedor is null
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Familia == null && d.CantidadMínima <= cantidad && d.NºProveedor == null && d.GrupoProducto == producto.Grupo);
            if (dtoProducto != null && dtoProducto.Descuento > descuentoCalculado)
            {
                descuentoCalculado = dtoProducto.Descuento;
            }
            //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and nºproveedor is null and grupoproducto is null
            //select isnull(max(descuento),0) from descuentosproducto where [nº cliente]='15191     ' and empresa='1  ' and familia='Lisap     ' and cantidadmínima<=1 and nºproveedor is null  and grupoproducto is null
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Familia == producto.Familia && d.CantidadMínima <= cantidad && d.NºProveedor == null && d.GrupoProducto == null);
            if (dtoProducto != null && dtoProducto.Descuento > descuentoCalculado)
            {
                descuentoCalculado = dtoProducto.Descuento;
            }
            //select * from descuentosproducto where empresa='1  ' and familia='Lisap     ' and [nº cliente]='15191     ' and grupoproducto='PEL' and nºproveedor is null

            if (precioCalculado < producto.PVP * (1 - descuentoCalculado))
            {
                descuentoCalculado = 0;
            }
            else
            {
                precioCalculado = (decimal)producto.PVP;
            }

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