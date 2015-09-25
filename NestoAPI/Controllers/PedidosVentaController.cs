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

            CabPedidoVta cabecera = new CabPedidoVta {
                Empresa = pedido.empresa,
                Número = pedido.numero,
                Nº_Cliente = pedido.cliente,
                Contacto = pedido.contacto,
                Fecha = pedido.fecha,
                Forma_Pago = pedido.formaPago,
                PlazosPago = pedido.plazosPago,
                Primer_Vencimiento = pedido.primerVencimiento,
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
            PlazoPago plazoPago = db.PlazosPago.SingleOrDefault(f => f.Empresa == pedido.empresa && f.Número == pedido.plazosPago);
            descuentoPP = plazoPago.DtoProntoPago;
            

            // Declaramos las variables que se van a utilizar en el bucle de insertar líneas
            LinPedidoVta linPedido;
            string tipoExclusiva;
            decimal baseImponible, bruto, importeDescuento, importeIVA, importeRE, descuentoProducto, sumaDescuentos, porcentajeRE;
            byte porcentajeIVA;
            Producto producto;
            ParametroIVA parametroIva;
            
            // Bucle de insertar líneas
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido) {
                producto = db.Productos.Where(p => p.Empresa == pedido.empresa && p.Número == linea.producto).SingleOrDefault();
                bruto = linea.cantidad * linea.precio;
                descuentoProducto = linea.aplicarDescuento ? calcularDescuentoProducto(producto, pedido.cliente, pedido.contacto) : 0;
                sumaDescuentos = (1 - (1 - (descuentoCliente)) * (1 - (descuentoProducto)) * (1 - (linea.descuento)) * (1 - (descuentoPP)));
                baseImponible = bruto * (1 - sumaDescuentos);
                parametroIva = db.ParametrosIVA.SingleOrDefault(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva && p.IVA_Producto == producto.IVA_Repercutido);
                porcentajeIVA = (byte)parametroIva.C__IVA; 
                porcentajeRE = (decimal)parametroIva.C__RE / 100;
                importeIVA = baseImponible * porcentajeIVA / 100;
                importeRE = baseImponible * porcentajeRE;
                importeDescuento = bruto * sumaDescuentos;

                tipoExclusiva = "NIG"; // calcular

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
        private short calcularDescuentoProducto(Producto producto, string cliente, string contacto)
        {
            return 0;
        }
    }
}