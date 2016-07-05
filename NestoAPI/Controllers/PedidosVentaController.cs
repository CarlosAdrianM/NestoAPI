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
        private const int ESTADO_LINEA_EN_CURSO = 1;
        private const int ESTADO_LINEA_PENDIENTE = -1;

        private NVEntities db = new NVEntities();
        // Carlos 04/09/15: lo pongo para desactivar el Lazy Loading
        public PedidosVentaController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        
        // GET: api/PedidosVenta
        
        public IQueryable<ResumenPedidoVentaDTO> GetPedidosVenta(string vendedor)
        {
            List<ResumenPedidoVentaDTO> cabeceraPedidos = db.CabPedidoVtas
                .Join(db.LinPedidoVtas, c => new {empresa = c.Empresa, numero = c.Número}, l => new {empresa = l.Empresa, numero = l.Número }, (c, l) => new { c.Vendedor, c.Empresa, c.Número, c.Nº_Cliente, c.Cliente.Nombre, c.Cliente.Dirección, c.Cliente.CodPostal, c.Cliente.Población, c.Cliente.Provincia, c.Fecha, l.TipoLinea, l.Estado, l.Picking, l.Fecha_Entrega, l.Base_Imponible, l.Total })
                .Where(c => c.Estado >= -1 && c.Estado <= 1)
                .GroupBy(g => new { g.Empresa, g.Número, g.Nº_Cliente, g.Nombre, g.Dirección, g.CodPostal, g.Población, g.Provincia, g.Vendedor})
                .Select(x => new ResumenPedidoVentaDTO
                {
                    empresa = x.Key.Empresa.Trim(),
                    numero = x.Key.Número,
                    cliente = x.Key.Nº_Cliente.Trim(),
                    nombre = x.Key.Nombre.Trim(),
                    direccion = x.Key.Dirección.Trim(),
                    codPostal = x.Key.CodPostal.Trim(),
                    poblacion = x.Key.Población.Trim(),
                    provincia = x.Key.Provincia.Trim(),
                    fecha = x.Min(c => c.Fecha_Entrega),
                    tieneProductos = x.FirstOrDefault(c => c.TipoLinea == 1) != null,
                    tieneFechasFuturas = x.FirstOrDefault(c => c.Fecha_Entrega > DateTime.Now) != null,
                    tienePendientes = x.FirstOrDefault(c => c.Estado < 0) != null,
                    tienePicking = x.FirstOrDefault(c => c.Picking != 0) != null,
                    baseImponible = x.Sum(c => c.Base_Imponible),
                    total = x.Sum(c => c.Total),
                    vendedor = x.Key.Vendedor.Trim()
                })
                .OrderByDescending(c => c.numero)
                .ToList();

            if (vendedor != null && vendedor.Trim() != "")
            {
                cabeceraPedidos = cabeceraPedidos.Where(c => c.vendedor == vendedor).ToList();
            }

            return cabeceraPedidos.AsQueryable();
        }
        

        // GET: api/PedidosVenta/5
        [ResponseType(typeof(PedidoVentaDTO))]
        public async Task<IHttpActionResult> GetPedidoVenta(string empresa, int numero)
        {
            CabPedidoVta cabPedidoVta = await db.CabPedidoVtas.SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == numero);
            if (cabPedidoVta == null)
            {
                return NotFound();
            }

            List<LineaPedidoVentaDTO> lineasPedido = db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == numero && l.Estado > -99)
                .Select(l => new LineaPedidoVentaDTO
                {
                    almacen = l.Almacén,
                    aplicarDescuento = l.Aplicar_Dto,
                    cantidad = (l.Cantidad != null ? (short)l.Cantidad : (short)0), 
                    delegacion = l.Delegación, 
                    descuento = l.Descuento,
                    estado = l.Estado,
                    fechaEntrega = l.Fecha_Entrega,
                    formaVenta = l.Forma_Venta,
                    iva = l.IVA,
                    oferta = l.NºOferta,
                    precio = (l.Precio != null ? (decimal)l.Precio : 0),
                    producto = l.Producto.Trim(),
                    texto = l.Texto.Trim(),
                    tipoLinea = l.TipoLinea,
                    usuario = l.Usuario,
                    vistoBueno = l.VtoBueno,
                    baseImponible = l.Base_Imponible,
                    importeIva = l.ImporteIVA,
                    total = l.Total
                })
                .ToList();

            PedidoVentaDTO pedido = new PedidoVentaDTO
            {
                empresa = cabPedidoVta.Empresa.Trim(),
                numero = cabPedidoVta.Número,
                cliente = cabPedidoVta.Nº_Cliente.Trim(),
                contacto = cabPedidoVta.Contacto.Trim(),
                fecha = cabPedidoVta.Fecha,
                formaPago = cabPedidoVta.Forma_Pago,
                plazosPago = cabPedidoVta.PlazosPago.Trim(),
                primerVencimiento = cabPedidoVta.Primer_Vencimiento,
                iva = cabPedidoVta.IVA,
                vendedor = cabPedidoVta.Vendedor,
                comentarios = cabPedidoVta.Comentarios,
                comentarioPicking = cabPedidoVta.ComentarioPicking,
                periodoFacturacion = cabPedidoVta.Periodo_Facturacion,
                ruta = cabPedidoVta.Ruta,
                serie = cabPedidoVta.Serie,
                ccc = cabPedidoVta.CCC,
                origen = cabPedidoVta.Origen,
                contactoCobro = cabPedidoVta.ContactoCobro,
                noComisiona = cabPedidoVta.NoComisiona,
                vistoBuenoPlazosPago = cabPedidoVta.vtoBuenoPlazosPago,
                mantenerJunto = cabPedidoVta.MantenerJunto,
                servirJunto = cabPedidoVta.ServirJunto,
                usuario = cabPedidoVta.Usuario,
                LineasPedido = lineasPedido,
            };
            
            return Ok(pedido);
        }

        
        // PUT: api/PedidosVenta/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPedidoVenta(PedidoVentaDTO pedido)
        {
            CabPedidoVta cabPedidoVta = db.CabPedidoVtas.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.numero);


            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pedido.empresa != cabPedidoVta.Empresa.Trim() || pedido.numero != cabPedidoVta.Número)
            {
                return BadRequest();
            }

            // Comprobar que tiene líneas pendientes de servir, en caso contrario no se permite la edición
            bool tienePendientes = db.LinPedidoVtas.Where(l => l.Empresa == cabPedidoVta.Empresa && l.Número == cabPedidoVta.Número && l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO).SingleOrDefault() != null;
            if (!tienePendientes) {
                throw new Exception ("No se puede modificar un pedido ya facturado");
            }

            cabPedidoVta.Nº_Cliente = pedido.cliente;
            cabPedidoVta.Contacto = pedido.contacto;
            cabPedidoVta.Fecha = pedido.fecha;
            cabPedidoVta.Forma_Pago = pedido.formaPago;
            cabPedidoVta.PlazosPago = pedido.plazosPago;
            cabPedidoVta.Primer_Vencimiento = pedido.primerVencimiento;
            cabPedidoVta.IVA = pedido.iva;
            cabPedidoVta.Vendedor = pedido.vendedor;
            cabPedidoVta.Comentarios = pedido.comentarios;
            cabPedidoVta.ComentarioPicking = pedido.comentarioPicking;
            cabPedidoVta.Periodo_Facturacion = pedido.periodoFacturacion;
            cabPedidoVta.Ruta = pedido.ruta;
            cabPedidoVta.Serie = pedido.serie;
            cabPedidoVta.CCC = pedido.ccc;
            cabPedidoVta.Origen = pedido.origen;
            cabPedidoVta.ContactoCobro = pedido.contactoCobro;
            cabPedidoVta.NoComisiona = pedido.noComisiona;
            cabPedidoVta.vtoBuenoPlazosPago = pedido.vistoBuenoPlazosPago;
            cabPedidoVta.MantenerJunto = pedido.mantenerJunto;
            cabPedidoVta.ServirJunto = pedido.servirJunto;
            cabPedidoVta.Usuario = pedido.usuario;
            cabPedidoVta.Fecha_Modificación = DateTime.Now;


            db.Entry(cabPedidoVta).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CabPedidoVtaExists(pedido.empresa, pedido.numero))
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
        

        // POST: api/PedidosVenta
        [ResponseType(typeof(PedidoVentaDTO))]
        public async Task<IHttpActionResult> PostPedidoVenta(PedidoVentaDTO pedido)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Carlos 28/09/15: ajustamos el primer vencimiento a los plazos de pago y a los días de pago
            DateTime vencimientoPedido;
            System.Data.Entity.Core.Objects.ObjectParameter primerVencimiento = new System.Data.Entity.Core.Objects.ObjectParameter("FechaOut", typeof(DateTime));
            PlazoPago plazoPago = db.PlazosPago.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.plazosPago);
            Empresa empresa = db.Empresas.SingleOrDefault(e => e.Número == pedido.empresa);
            vencimientoPedido = pedido.fecha.Value.AddDays(plazoPago.DíasPrimerPlazo);
            vencimientoPedido = vencimientoPedido.AddMonths(plazoPago.MesesPrimerPlazo);
            db.prdAjustarDíasPagoCliente(pedido.empresa, pedido.cliente, pedido.contacto, vencimientoPedido, primerVencimiento);

            // El número que vamos a dar al pedido hay que leerlo de ContadoresGlobales
            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            if (pedido.numero == 0)
            {
                contador.Pedidos++;
                pedido.numero = contador.Pedidos;
            }

            CabPedidoVta cabecera = new CabPedidoVta {
                Empresa = pedido.empresa,
                Número = pedido.numero,
                Nº_Cliente = pedido.cliente,
                Contacto = pedido.contacto,
                Fecha = pedido.fecha,
                Forma_Pago = pedido.iva != null ? pedido.formaPago : empresa.FormaPagoEfectivo,
                PlazosPago = pedido.iva != null ? pedido.plazosPago : empresa.PlazosPagoDefecto,
                Primer_Vencimiento = (DateTime)primerVencimiento.Value,
                IVA = pedido.iva,
                Vendedor = pedido.vendedor,
                Periodo_Facturacion = pedido.periodoFacturacion,
                Ruta = pedido.ruta,
                Serie = pedido.serie,
                CCC = pedido.iva != null ? pedido.ccc : null,
                Origen = pedido.origen,
                ContactoCobro = pedido.contactoCobro,
                NoComisiona = pedido.noComisiona,
                MantenerJunto = pedido.mantenerJunto,
                ServirJunto = pedido.servirJunto,
                ComentarioPicking = pedido.comentarioPicking,
                Comentarios = pedido.comentarios,
                Usuario = pedido.usuario
            };

            db.CabPedidoVtas.Add(cabecera);

            // Calculamos las variables que se pueden corresponden a la cabecera
            decimal descuentoCliente, descuentoPP;
            //ParametrosUsuarioController parametrosUsuarioCtrl = new ParametrosUsuarioController();
            ParametroUsuario parametroUsuario;
            DescuentosCliente dtoCliente = db.DescuentosClientes.OrderBy(d => d.ImporteMínimo).FirstOrDefault(d => d.Empresa == pedido.empresa && d.Nº_Cliente == pedido.cliente && d.Contacto == pedido.contacto);
            descuentoCliente = dtoCliente != null ?  dtoCliente.Descuento : 0;
            descuentoPP = plazoPago.DtoProntoPago;

            // Guardamos el parámetro de pedido, para que al abrir la ventana el usuario vea el pedido
            string usuarioParametro = pedido.usuario.Substring(pedido.usuario.IndexOf("\\")+1);
            if (usuarioParametro != null && (usuarioParametro.Length<7 || usuarioParametro.Substring(0,7) != "Cliente"))
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Usuario == usuarioParametro && p.Clave == "UltNumPedidoVta");
                parametroUsuario.Valor = pedido.numero.ToString();
            }

            // Declaramos las variables que se van a utilizar en el bucle de insertar líneas
            LinPedidoVta linPedido;
            string tipoExclusiva;
            decimal baseImponible, bruto, importeDescuento, importeIVA, importeRE, sumaDescuentos, porcentajeRE;
            byte porcentajeIVA;
            Producto producto;
            ParametroIVA parametroIva;
            int? maxNumeroOferta = 0;


            
            // Bucle de insertar líneas
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido) {
                producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == pedido.empresa && p.Número == linea.producto).SingleOrDefault();

                //descuentoProducto = linea.descuentoProducto; // si queremos separar descuento producto de descuento de linea, hay que crear el campo linea.descuentoProducto

                /*                
                // No permitimos poner descuento y además precio especial
                if (linea.precio != producto.PVP || linea.descuento > 0) {
                    linea.aplicarDescuento = false;
                }
                
                // Solo calculamos los descuentos si no lleva otra oferta o precio especial aplicado
                if ((linea.oferta == null || linea.oferta == 0) && linea.precio >= producto.PVP && linea.descuento == 0)
                {
                    precio = linea.precio; //para poder pasar el precio por referencia
                    calcularDescuentoProducto(ref precio, ref descuentoProducto, producto, pedido.cliente, pedido.contacto, linea.cantidad, linea.aplicarDescuento);
                    linea.precio = precio;
                }
                */

                bruto = linea.cantidad * linea.precio;
                if (linea.aplicarDescuento) {
                    sumaDescuentos = (1 - (1 - (descuentoCliente)) * (1 - (linea.descuentoProducto)) * (1 - (linea.descuento)) * (1 - (descuentoPP))); 
                } else {
                    linea.descuentoProducto = 0;
                    sumaDescuentos = linea.descuento;
                }
                baseImponible = Math.Round(bruto * (1 - sumaDescuentos), 2);
                if (pedido.iva != null)
                {
                    parametroIva = db.ParametrosIVA.SingleOrDefault(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva && p.IVA_Producto == producto.IVA_Repercutido);
                    porcentajeIVA = (byte)parametroIva.C__IVA;
                    porcentajeRE = (decimal)parametroIva.C__RE / 100;
                    importeIVA = baseImponible * porcentajeIVA / 100;
                    importeRE = baseImponible * porcentajeRE;
                } else
                {
                    porcentajeIVA = 0;
                    porcentajeRE = 0;
                    importeIVA = 0;
                    importeRE = 0;
                }
                importeDescuento = bruto * sumaDescuentos;

                tipoExclusiva = producto.Familia1.TipoExclusiva;

                if (linea.oferta != null && linea.oferta != 0)
                {
                    if (linea.oferta > maxNumeroOferta)
                    {
                        maxNumeroOferta = linea.oferta;
                    }
                    linea.oferta += contador.Oferta;
                }

                linPedido = new LinPedidoVta
                {
                    Estado = linea.estado,
                    TipoLinea = linea.tipoLinea,
                    Producto = linea.producto,
                    Texto = linea.texto,
                    Cantidad = linea.cantidad,
                    Fecha_Entrega = linea.fechaEntrega.Date,
                    Precio = linea.precio,
                    PrecioTarifa = producto.PVP,
                    Coste = (decimal)producto.PrecioMedio,
                    Bruto = bruto,
                    Descuento = linea.descuento,
                    DescuentoProducto = linea.descuentoProducto, 
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
                    Picking = 0,
                    NºOferta = linea.oferta,
                    BlancoParaBorrar = "NestoAPI"
                };

                db.LinPedidoVtas.Add(linPedido);
            }

            // Actualizamos el contador de ofertas
            if ((int)maxNumeroOferta!=0) {
                contador.Oferta += (int)maxNumeroOferta;
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
                    string message = e.Message;
                    Exception recorremosExcepcion = e;
                    while (recorremosExcepcion.InnerException != null)
                    {
                        message = recorremosExcepcion.Message+ "\n" + recorremosExcepcion.InnerException.Message;
                        recorremosExcepcion = recorremosExcepcion.InnerException;
                    }
                    throw new HttpResponseException(Request.CreateErrorResponse(HttpStatusCode.NotAcceptable, message));
                }
            }

            // esto no sé si está muy bien, porque ponía empresa y lo he cambiado a número. Deberían ir los dos
            return CreatedAtRoute("DefaultApi", new { id = pedido.numero }, pedido);
        }

        /*
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
        */

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

        /*
        // Calcula el descuento que lleva un producto determinado para un cliente determinado
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
        */
    }
}