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

namespace NestoAPI.Controllers
{
    public class PedidosVentaController : ApiController
    {
        private NVEntities db = new NVEntities();
        // Carlos 04/09/15: lo pongo para desactivar el Lazy Loading
        public PedidosVentaController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        /*
        // GET: api/PedidosVenta
        public IQueryable<CabPedidoVta> GetCabPedidoVtas()
        {
            return db.CabPedidoVtas;
        }

        // GET: api/PedidosVenta/5
        [ResponseType(typeof(CabPedidoVta))]
        public async Task<IHttpActionResult> GetCabPedidoVta(string id)
        {
            CabPedidoVta cabPedidoVta = await db.CabPedidoVtas.FindAsync(id);
            if (cabPedidoVta == null)
            {
                return NotFound();
            }

            return Ok(cabPedidoVta);
        }

        // PUT: api/PedidosVenta/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutCabPedidoVta(string id, CabPedidoVta cabPedidoVta)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != cabPedidoVta.Empresa)
            {
                return BadRequest();
            }

            db.Entry(cabPedidoVta).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CabPedidoVtaExists(id))
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
        */

        // POST: api/PedidosVenta
        [ResponseType(typeof(PedidoVentaDTO))]
        public async Task<IHttpActionResult> PostPedidoVenta(PedidoVentaDTO pedido)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // El número que vamos a dar al pedido hay que leerlo de ContadoresGlobales
            if (pedido.numero == 0)
            {
                ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
                pedido.numero = contador.Pedidos + 1;
                contador.Pedidos = pedido.numero;
            }

            // Carlos 28/09/15: ajustamos el primer vencimiento a los plazos de pago y a los días de pago
            DateTime vencimientoPedido;
            System.Data.Entity.Core.Objects.ObjectParameter primerVencimiento = new System.Data.Entity.Core.Objects.ObjectParameter("FechaOut", typeof(DateTime));
            PlazoPago plazoPago = db.PlazosPago.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.plazosPago);
            vencimientoPedido = pedido.fecha.Value.AddDays(plazoPago.DíasPrimerPlazo);
            vencimientoPedido = vencimientoPedido.AddMonths(plazoPago.MesesPrimerPlazo);
            db.prdAjustarDíasPagoCliente(pedido.empresa, pedido.cliente, pedido.contacto, vencimientoPedido, primerVencimiento);

            CabPedidoVta cabecera = new CabPedidoVta {
                Empresa = pedido.empresa,
                Número = pedido.numero,
                Nº_Cliente = pedido.cliente,
                Contacto = pedido.contacto,
                Fecha = pedido.fecha,
                Forma_Pago = pedido.formaPago,
                PlazosPago = pedido.plazosPago,
                Primer_Vencimiento = (DateTime)primerVencimiento.Value,
                IVA = pedido.iva,
                Vendedor = pedido.vendedor,
                Periodo_Facturacion = pedido.periodoFacturacion,
                Ruta = pedido.ruta,
                Serie = pedido.serie,
                CCC = pedido.ccc,
                Origen = pedido.origen,
                ContactoCobro = pedido.contactoCobro,
                NoComisiona = pedido.noComisiona,
                MantenerJunto = pedido.mantenerJunto,
                ServirJunto = pedido.servirJunto,
                Usuario = pedido.usuario
            };

            db.CabPedidoVtas.Add(cabecera);

            // Calculamos las variables que se pueden corresponden a la cabecera
            decimal descuentoCliente, descuentoPP;
            ParametrosUsuarioController parametrosUsuarioCtrl = new ParametrosUsuarioController();
            DescuentosCliente dtoCliente = db.DescuentosClientes.OrderBy(d => d.ImporteMínimo).FirstOrDefault(d => d.Empresa == pedido.empresa && d.Nº_Cliente == pedido.cliente && d.Contacto == pedido.contacto);
            descuentoCliente = dtoCliente != null ?  dtoCliente.Descuento : 0;
            descuentoPP = plazoPago.DtoProntoPago;
            

            // Declaramos las variables que se van a utilizar en el bucle de insertar líneas
            LinPedidoVta linPedido;
            string tipoExclusiva;
            decimal baseImponible, bruto, importeDescuento, importeIVA, importeRE, descuentoProducto, sumaDescuentos, porcentajeRE, precio;
            byte porcentajeIVA;
            Producto producto;
            ParametroIVA parametroIva;


            
            // Bucle de insertar líneas
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido) {
                producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == pedido.empresa && p.Número == linea.producto).SingleOrDefault();
                descuentoProducto = 0;
                precio = (decimal)producto.PVP;

                bruto = linea.cantidad * linea.precio;
                if (linea.aplicarDescuento) {
                    calcularDescuentoProducto(ref precio, ref descuentoProducto, producto, pedido.cliente, pedido.contacto, linea.cantidad);
                }
                sumaDescuentos = (1 - (1 - (descuentoCliente)) * (1 - (descuentoProducto)) * (1 - (linea.descuento)) * (1 - (descuentoPP)));
                baseImponible = bruto * (1 - sumaDescuentos);
                parametroIva = db.ParametrosIVA.SingleOrDefault(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva && p.IVA_Producto == producto.IVA_Repercutido);
                porcentajeIVA = (byte)parametroIva.C__IVA; 
                porcentajeRE = (decimal)parametroIva.C__RE / 100;
                importeIVA = baseImponible * porcentajeIVA / 100;
                importeRE = baseImponible * porcentajeRE;
                importeDescuento = bruto * sumaDescuentos;

                tipoExclusiva = producto.Familia1.TipoExclusiva; 

                linPedido = new LinPedidoVta
                {
                    Estado = linea.estado,
                    TipoLinea = linea.tipoLinea,
                    Producto = linea.producto,
                    Texto = linea.texto,
                    Cantidad = linea.cantidad,
                    Fecha_Entrega = linea.fechaEntrega,
                    Precio = linea.precio,
                    PrecioTarifa = producto.PVP,
                    Coste = (decimal)producto.PrecioMedio,
                    Bruto = bruto,
                    Descuento = linea.descuento,
                    DescuentoProducto = descuentoProducto, 
                    Base_Imponible = baseImponible,
                    Aplicar_Dto = linea.aplicarDescuento,
                    VtoBueno = linea.vistoBueno,
                    Usuario = linea.usuario,
                    Almacén = linea.almacen,
                    IVA = linea.iva,
                    Total = baseImponible + importeIVA + importeRE,
                    Grupo = producto.Grupo,
                    Empresa = pedido.empresa,
                    Número = pedido.numero,
                    Nº_Cliente = pedido.cliente,
                    Contacto = pedido.contacto,
                    PorcentajeIVA = porcentajeIVA,
                    PorcentajeRE = porcentajeRE,
                    DescuentoCliente = descuentoCliente,
                    DescuentoPP = descuentoPP,
                    SumaDescuentos = sumaDescuentos,
                    ImporteDto = importeDescuento,
                    ImporteIVA = importeIVA,
                    ImporteRE = importeRE,
                    Delegación = linea.delegacion,
                    Forma_Venta = linea.formaVenta,
                    SubGrupo = producto.SubGrupo,
                    Familia = producto.Familia,
                    TipoExclusiva = tipoExclusiva,
                    Picking = 0
                };

                db.LinPedidoVtas.Add(linPedido);
            }

            // Carlos 07/10/15:
            // ahora ya tenemos el importe del pedido, hay que mirar si los plazos de pago cambian


            
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                if (CabPedidoVtaExists(cabecera.Empresa, cabecera.Número))
                {
                    return Conflict();
                }
                else
                {
                    throw e;
                }
            }

            // esto no sé si está muy bien, porque ponía empresa y lo he cambiado a número. Deberían ir los dos
            return CreatedAtRoute("DefaultApi", new { id = cabecera.Número }, cabecera);
        }

        // DELETE: api/PedidosVenta/5
        [ResponseType(typeof(CabPedidoVta))]
        public async Task<IHttpActionResult> DeleteCabPedidoVta(string id)
        {
            CabPedidoVta cabPedidoVta = await db.CabPedidoVtas.FindAsync(id);
            if (cabPedidoVta == null)
            {
                return NotFound();
            }

            db.CabPedidoVtas.Remove(cabPedidoVta);
            await db.SaveChangesAsync();

            return Ok(cabPedidoVta);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool CabPedidoVtaExists(string empresa, int id)
        {
            return db.CabPedidoVtas.Count(e => e.Empresa == empresa && e.Número == id) > 0;
        }

        // Calcula el descuento que lleva un producto determinado para un cliente determinado
        private void calcularDescuentoProducto(ref decimal precioCalculado, ref decimal descuentoCalculado, Producto producto, string cliente, string contacto, short cantidad)
        {
            DescuentosProducto dtoProducto;

            descuentoCalculado = 0;
            precioCalculado = Decimal.MaxValue;

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
            //select top 1 precio,cantidadminima from descuentosproducto where cantidadminíma<=1 and  empresa='1  ' and [Nº Producto]='29352' and [nº cliente] is null and [nºproveedor] is null order by cantidadminima desc

            // CALCULA DESCUENTOS
            //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente] is null and nºproveedor is null and familia is null
            //select * from descuentosproducto where empresa='1  ' and grupoproducto='PEL' and [nº cliente] is null and  nºproveedor is null and familia is null
            //select * from descuentosproducto where empresa='1  ' and [nº producto]='29352' and [nº cliente]='15191     ' and nºproveedor is null and familia is null
            dtoProducto = db.DescuentosProductoes.SingleOrDefault(d => d.Empresa == producto.Empresa && d.Nº_Cliente == cliente && d.Nº_Producto == producto.Número && d.CantidadMínima <= cantidad && d.NºProveedor == null && d.Familia == null);
            if (dtoProducto != null && dtoProducto.Descuento>descuentoCalculado)
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
            } else
            {
                precioCalculado = (decimal)producto.PVP;
            }
            
        }
    }
}