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
using Newtonsoft.Json;

namespace NestoAPI.Controllers
{
    public class PedidosVentaController : ApiController
    {
        private const int ESTADO_LINEA_EN_CURSO = 1;
        private const int ESTADO_LINEA_PENDIENTE = -1;
        private const int ESTADO_ENVIO_EN_CURSO = 0;

        public const int TIPO_LINEA_TEXTO = 0;
        public const int TIPO_LINEA_PRODUCTO = 1;
        public const int TIPO_LINEA_CUENTA_CONTABLE = 2;
        public const int TIPO_LINEA_INMOVILIZADO = 3;


        private NVEntities db = new NVEntities();
        // Carlos 04/09/15: lo pongo para desactivar el Lazy Loading
        public PedidosVentaController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }


        // GET: api/PedidosVenta
        public IQueryable<ResumenPedidoVentaDTO> GetPedidosVenta()
        {
            return GetPedidosVenta("");
        }

        public IQueryable<ResumenPedidoVentaDTO> GetPedidosVenta(string vendedor)
        {
            List<ResumenPedidoVentaDTO> cabeceraPedidos = db.CabPedidoVtas
                .Join(db.LinPedidoVtas, c => new {empresa = c.Empresa, numero = c.Número}, l => new {empresa = l.Empresa, numero = l.Número }, (c, l) => new { c.Vendedor, c.Empresa, c.Número, c.Nº_Cliente, c.Cliente.Nombre, c.Cliente.Dirección, c.Cliente.CodPostal, c.Cliente.Población, c.Cliente.Provincia, c.Fecha, l.TipoLinea, l.Estado, l.Picking, l.Fecha_Entrega, l.Base_Imponible, l.Total })
                .Where(c => c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
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
                    id = l.Nº_Orden,
                    almacen = l.Almacén,
                    aplicarDescuento = l.Aplicar_Dto,
                    cantidad = (l.Cantidad != null ? (short)l.Cantidad : (short)0), 
                    delegacion = l.Delegación, 
                    descuento = l.Descuento,
                    descuentoProducto = l.DescuentoProducto,
                    estado = l.Estado,
                    fechaEntrega = l.Fecha_Entrega,
                    formaVenta = l.Forma_Venta,
                    iva = l.IVA,
                    oferta = l.NºOferta,
                    picking = (l.Picking != null ? (int)l.Picking : 0),
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
            /*
             * Actualmente podemos añadir líneas o cambiar la cabecera, pero falta:
             * - Suprimir líneas
             * - Modificar líneas (cantidad, precio...)
             * */


            CabPedidoVta cabPedidoVta = db.CabPedidoVtas.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.numero);

            // Guardamos registro de los cambios
            Modificacion modificacion = new Modificacion
            {
                Tabla = "Pedidos",
                Anterior = JsonConvert.SerializeObject(cabPedidoVta),
                Nuevo = JsonConvert.SerializeObject(pedido),
                Usuario = pedido.usuario
            };
            db.Modificaciones.Add(modificacion);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (pedido.empresa != cabPedidoVta.Empresa.Trim() || pedido.numero != cabPedidoVta.Número)
            {
                return BadRequest();
            }

            // Comprobar que tiene líneas pendientes de servir, en caso contrario no se permite la edición
            bool tienePendientes = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == cabPedidoVta.Empresa && l.Número == cabPedidoVta.Número && l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO) != null;
            if (!tienePendientes) {
                throw new Exception ("No se puede modificar un pedido ya facturado");
            }

            // En una primera fase no permitimos modificar si ya está impresa la etiqueta de la agencia
            // En una segunda fase se podría ajustar para permitir modificar algunos campos, aún con la etiqueta impresa
            bool estaImpresaLaEtiqueta = db.EnviosAgencias.FirstOrDefault(e => e.Pedido == pedido.numero && e.Estado == ESTADO_ENVIO_EN_CURSO) != null;
            if (estaImpresaLaEtiqueta)
            {
                throw new Exception("No se puede modificar el pedido porque ya está preparado");
            }

            // En una primera fase no permitimos modificar si alguna línea de las pendientes tiene picking
            // En una segunda fase se podría ajustar para permitir modificar algunos campos, aún teniendo picking
            bool algunaLineaTienePicking = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == cabPedidoVta.Empresa && l.Número == cabPedidoVta.Número && l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO && l.Picking > 0) != null;
            if (algunaLineaTienePicking)
            {
                throw new Exception("No se puede modificar el pedido porque ya tiene picking");
            }

            // Son diferentes, porque el del pedido está con trim
            // Comprobar si en SQL los da por iguales y no hace update si solo cambia esto
            // Si hace update aunque no cambien, hay que poner un IF delante para comprobar
            // que cabPedidoVta.Nº_Cliente.Trim() != pedido.cliente.Trim()
            bool cambiarClienteEnLineas = false;
            if (cabPedidoVta.Nº_Cliente.Trim() != pedido.cliente.Trim())
            {
                cabPedidoVta.Nº_Cliente = pedido.cliente;
                cambiarClienteEnLineas = true;
            }

            bool cambiarContactoEnLineas = false;
            if (cabPedidoVta.Contacto.Trim() != pedido.contacto.Trim())
            {
                cabPedidoVta.Contacto = pedido.contacto;
                // hay cambiar el contacto a todas las líneas
                // ojo con la PK, que igual no puede haber diferentes contactos en un mismo pedido
                // comprobarlo con algunas facturas de FDM
                cambiarContactoEnLineas = true;
            }
            
            cabPedidoVta.Fecha = pedido.fecha;
            // La forma de pago influye en el importe del reembolso de la agencia. Si se modifica la forma de pago
            // hay que modificar el importe del reembolso
            cabPedidoVta.Forma_Pago = pedido.formaPago;
            // Ojo, que el CCC va en función de la forma de pago.
            // Mirad cómo lo hace la plantilla e intentar traer aquí la lógica (y la plantilla llame aquí)
            cabPedidoVta.CCC = pedido.ccc;
            // Comprobar si los plazos de pago son válidos
            cabPedidoVta.PlazosPago = pedido.plazosPago;
            cabPedidoVta.vtoBuenoPlazosPago = pedido.vistoBuenoPlazosPago;
            cabPedidoVta.Primer_Vencimiento = pedido.primerVencimiento;
            // Mirad en la plantilla cómo juega el contacto cobro
            cabPedidoVta.ContactoCobro = pedido.contactoCobro;

            bool cambiarIvaEnLineas = false;
            if ((cabPedidoVta.IVA == null && pedido.iva!=null) ||
                (cabPedidoVta.IVA != null && pedido.iva == null) ||
                ((cabPedidoVta.IVA != null && pedido.iva != null) &&
                cabPedidoVta.IVA.Trim() != pedido.iva.Trim()))
            {
                cabPedidoVta.IVA = pedido.iva;
                // hay que cambiarlo en todas las líneas pendientes
                // y recalcular el total
                // traer aquí la lógica de las líneas y que la plantilla llame aquí
                cambiarIvaEnLineas = true;
            }

            // Si cambia el periodo de facturación cambia el reembolso de la etiqueta
            cabPedidoVta.Periodo_Facturacion = pedido.periodoFacturacion;

            cabPedidoVta.Vendedor = pedido.vendedor;
            cabPedidoVta.Comentarios = pedido.comentarios;
            cabPedidoVta.ComentarioPicking = pedido.comentarioPicking;
            cabPedidoVta.Ruta = pedido.ruta;
            cabPedidoVta.Serie = pedido.serie;
            cabPedidoVta.Origen = pedido.origen;
            cabPedidoVta.NoComisiona = pedido.noComisiona;
            cabPedidoVta.MantenerJunto = pedido.mantenerJunto;
            cabPedidoVta.ServirJunto = pedido.servirJunto;
            
            cabPedidoVta.Usuario = pedido.usuario;
            cabPedidoVta.Fecha_Modificación = DateTime.Now;
            
            db.Entry(cabPedidoVta).State = EntityState.Modified;

            // Si alguno de los tres se cumple, no hace falta comprobarlo
            bool hayLineasNuevas = false;
            if (!cambiarClienteEnLineas && !cambiarContactoEnLineas && !cambiarIvaEnLineas)
            {
                hayLineasNuevas = pedido.LineasPedido.Where(l => l.id == 0).FirstOrDefault() != null;
            }

            // Sacar diferencias entre el pedido original y el que hemos pasado:
            // - las líneas que la cantidad, o la base imponible sean diferentes hay que actualizarlas enteras
            // - las líneas que directamente no estén, hay que borrarlas
            
            
                        
            // Modificamos las líneas
            if (cambiarClienteEnLineas || cambiarContactoEnLineas || cambiarIvaEnLineas || hayLineasNuevas) { 
                foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido)
                {
                    LinPedidoVta lineaPedido;

                    if (linea.id == 0)
                    {
                        //lineaPedido = crearLineaVta(linea, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto);
                        lineaPedido = crearLineaVta(linea, pedido.empresa, pedido.numero);
                        db.LinPedidoVtas.Add(lineaPedido);
                        break;
                    }

                    if (cambiarClienteEnLineas || cambiarContactoEnLineas || cambiarIvaEnLineas)
                    {

                        lineaPedido = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.id);

                        if (lineaPedido == null || lineaPedido.Número != pedido.numero)
                        {
                            throw new Exception("Alguien modificó o eliminó la línea mientras se actualizaba el pedido");
                        }
                        if (cambiarClienteEnLineas)
                        {
                            lineaPedido.Nº_Cliente = pedido.cliente;
                        }
                        if (cambiarContactoEnLineas)
                        {
                            lineaPedido.Contacto = pedido.contacto;
                        }
                        if (cambiarIvaEnLineas)
                        {
                            if (pedido.iva == null)
                            {
                                // lineaPedido.IVA = pedido.iva;
                                lineaPedido.ImporteIVA = 0;
                                lineaPedido.ImporteRE = 0;
                                lineaPedido.Total = lineaPedido.Base_Imponible;
                            }
                            else
                            {
                                throw new NotImplementedException("No se puede poner ese IVA al pedido");
                            }
                        }

                        db.Entry(lineaPedido).State = EntityState.Modified;
                    }
                }
            }
            
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
        [HttpPost]
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

            
            //ParametrosUsuarioController parametrosUsuarioCtrl = new ParametrosUsuarioController();
            ParametroUsuario parametroUsuario;

            // Guardamos el parámetro de pedido, para que al abrir la ventana el usuario vea el pedido
            string usuarioParametro = pedido.usuario.Substring(pedido.usuario.IndexOf("\\")+1);
            if (usuarioParametro != null && (usuarioParametro.Length<7 || usuarioParametro.Substring(0,7) != "Cliente"))
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Usuario == usuarioParametro && p.Clave == "UltNumPedidoVta");
                parametroUsuario.Valor = pedido.numero.ToString();
            }

            // Declaramos las variables que se van a utilizar en el bucle de insertar líneas
            LinPedidoVta linPedido;
            int? maxNumeroOferta = 0;
            
            // Bucle de insertar líneas
            foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido) {
                if (linea.oferta != null && linea.oferta != 0)
                {
                    if (linea.oferta > maxNumeroOferta)
                    {
                        maxNumeroOferta = linea.oferta;
                    }
                    linea.oferta += contador.Oferta;
                }
                linPedido = crearLineaVta(linea, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto);
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

        public LinPedidoVta crearLineaVta(string empresa, int numeroPedido, byte tipoLinea, string producto, short cantidad, decimal precio, string usuario)
        {
            string delegacion = calcularDelegacion(usuario, empresa, numeroPedido);
            string almacen = calcularAlmacen(usuario, empresa, numeroPedido);
            string formaVenta = calcularFormaVenta(usuario, empresa, numeroPedido);

            string texto;
            switch (tipoLinea)
            {
                case Constantes.TiposLineaVenta.CUENTA_CONTABLE:
                    texto = db.PlanCuentas.SingleOrDefault(p => p.Empresa == empresa && p.Nº_Cuenta == producto).Concepto;
                    texto = texto.Substring(0, 50);
                    break;
                case Constantes.TiposLineaVenta.PRODUCTO:
                    texto = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == producto).Nombre;
                    break;
                case Constantes.TiposLineaVenta.INMOVILIZADO:
                    texto = db.Inmovilizados.Single(p => p.Empresa == empresa && p.Número == producto).Descripción;
                    break;
                default:
                    texto = "";
                    break;
            }

            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = tipoLinea,
                estado = Constantes.EstadosLineaVenta.EN_CURSO,
                producto = producto,
                texto = texto,
                cantidad = cantidad,
                precio = precio,
                delegacion = delegacion,
                formaVenta = formaVenta,
                almacen = almacen,
                usuario = usuario == "" ? System.Environment.UserDomainName + "\\" + System.Environment.UserName : System.Environment.UserDomainName + "\\" + usuario
            };
            return crearLineaVta(linea, empresa, numeroPedido);
        }

        public LinPedidoVta crearLineaVta(LineaPedidoVentaDTO linea, string empresa, int numeroPedido)
        {
            // Si hubiese en dos empresas el mismo pedido, va a dar error
            CabPedidoVta pedido = db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroPedido);
            PlazoPago plazo = db.PlazosPago.SingleOrDefault(p => p.Empresa == pedido.Empresa && p.Número == pedido.PlazosPago);
            if (pedido.IVA != null && linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
            {
                Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == linea.producto);
                linea.iva = producto.IVA_Repercutido;
            }
            return crearLineaVta(linea, numeroPedido, pedido.Empresa, pedido.IVA, plazo, pedido.Nº_Cliente, pedido.Contacto);
        }

        public LinPedidoVta crearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto)
        {
            // CabPedidoVta pedido = db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroPedido);

            string tipoExclusiva, grupo, subGrupo, familia, ivaRepercutido;
            decimal coste, precioTarifa;
            short estadoProducto;

            // Calculamos las variables que se pueden corresponden a la cabecera
            decimal descuentoCliente, descuentoPP;
            DescuentosCliente dtoCliente = db.DescuentosClientes.OrderBy(d => d.ImporteMínimo).FirstOrDefault(d => d.Empresa == empresa && d.Nº_Cliente == cliente && d.Contacto == contacto);
            descuentoCliente = dtoCliente != null ? dtoCliente.Descuento : 0;
            descuentoPP = plazoPago.DtoProntoPago;

            switch (linea.tipoLinea)
            {
                
                case Constantes.TiposLineaVenta.PRODUCTO:
                    Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == linea.producto);
                    precioTarifa = (decimal)producto.PVP;
                    coste = (decimal)producto.PrecioMedio;
                    grupo = producto.Grupo;
                    subGrupo = producto.SubGrupo;
                    familia = producto.Familia;
                    ivaRepercutido = producto.IVA_Repercutido; // ¿Se usa? Ojo, que puede venir el IVA nulo y estar bien
                    estadoProducto = (short)producto.Estado;
                    break;
                
                default:
                    precioTarifa = 0;
                    coste = 0;
                    grupo = null;
                    subGrupo = null;
                    familia = null;
                    ivaRepercutido = db.Empresas.SingleOrDefault(e => e.Número == empresa).TipoIvaDefecto;
                    estadoProducto = 0;
                    break;
            }


            // Posiblemente este if se pueda refactorizar con el switch de arriba, pero hay que comprobarlo bien primero
            if (linea.tipoLinea == PedidosVentaController.TIPO_LINEA_PRODUCTO)
            {
                // la cláusula include es case sensitive, por lo que la familia "pure" es distinta a "Pure" y no la encuentra (es lo que da error)
                Producto producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == empresa && p.Número == linea.producto).SingleOrDefault();
                tipoExclusiva = producto.Familia1.TipoExclusiva;
            } else
            {
                tipoExclusiva = null;
            }

            // Calculamos los valores que nos falten
            if (linea.almacen==null)
            {
                linea.almacen = calcularAlmacen(linea.usuario, empresa, numeroPedido);
            }

            if (linea.formaVenta == null)
            {
                linea.formaVenta = calcularFormaVenta(linea.usuario, empresa, numeroPedido);
            }

            if (linea.delegacion == null)
            {
                linea.delegacion = calcularDelegacion(linea.usuario, empresa, numeroPedido);
            }


            LinPedidoVta lineaNueva = new LinPedidoVta
            {
                Estado = linea.estado,
                TipoLinea = linea.tipoLinea,
                Producto = linea.producto,
                Texto = linea.texto,
                Cantidad = linea.cantidad,
                Fecha_Entrega = this.fechaEntregaAjustada(linea.fechaEntrega.Date),
                Precio = linea.precio,
                PrecioTarifa = precioTarifa,
                Coste = coste,
                Descuento = linea.descuento,
                DescuentoProducto = linea.descuentoProducto,
                Aplicar_Dto = linea.aplicarDescuento,
                VtoBueno = linea.vistoBueno,
                Usuario = linea.usuario,
                Almacén = linea.almacen,
                IVA = linea.iva,
                Grupo = grupo,
                Empresa = empresa,
                Número = numeroPedido,
                Nº_Cliente = cliente,
                Contacto = contacto,
                DescuentoCliente = descuentoCliente,
                DescuentoPP = descuentoPP,
                Delegación = linea.delegacion,
                Forma_Venta = linea.formaVenta,
                SubGrupo = subGrupo,
                Familia = familia,
                TipoExclusiva = tipoExclusiva,
                Picking = 0,
                NºOferta = linea.oferta,
                BlancoParaBorrar = "NestoAPI",
                LineaParcial = linea.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO ? !esSobrePedido(linea.producto, linea.cantidad) : true,
                EstadoProducto = estadoProducto
            };
            /*
            Nota sobre LineaParcial: aunque siga llamándos el campo línea parcial, por retrocompatibidad, en realidad
            ya nada tiene que ver con que se quede una parte de la línea sin entregar, sino que ahora indica si tiene
            que salir aunque no llegue al mínimo. De este modo, todo lo que sea estado 0, por ejemplo, sale siempre, por 
            lo que la línea parcial siempre será cero (no es sobre pedido).
            */          
            
            calcularImportesLinea(lineaNueva, iva);

            return lineaNueva;

        }

        // Si pongo public, lo confunde con el método POST, porque solo llevan un parámetro
        void calcularImportesLinea(LinPedidoVta linea)
        {
            string iva = db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == linea.Empresa && c.Número == linea.Número).IVA;
            calcularImportesLinea(linea, iva);
        }

        public void calcularImportesLinea(LinPedidoVta linea, string iva)
        {
            decimal baseImponible, bruto, importeDescuento, importeIVA, importeRE, sumaDescuentos, porcentajeRE;
            byte porcentajeIVA;
            ParametroIVA parametroIva;
            
            bruto = (decimal)(linea.Cantidad * linea.Precio);
            if (linea.Aplicar_Dto)
            {
                sumaDescuentos = (1 - (1 - (linea.DescuentoCliente)) * (1 - (linea.DescuentoProducto)) * (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            else
            {
                linea.DescuentoProducto = 0;
                sumaDescuentos = linea.Descuento;
            }
            baseImponible = Math.Round(bruto * (1 - sumaDescuentos), 2);
            if (iva != null && iva.Trim() != "")
            {
                parametroIva = db.ParametrosIVA.SingleOrDefault(p => p.Empresa == linea.Empresa && p.IVA_Cliente_Prov == iva && p.IVA_Producto == linea.IVA);
                porcentajeIVA = parametroIva != null ? (byte)parametroIva.C__IVA : (byte)0;
                porcentajeRE = parametroIva != null ? (decimal)parametroIva.C__RE / 100 : (decimal)0;
                importeIVA = baseImponible * porcentajeIVA / 100;
                importeRE = baseImponible * porcentajeRE;
            }
            else
            {
                porcentajeIVA = 0;
                porcentajeRE = 0;
                importeIVA = 0;
                importeRE = 0;
            }
            importeDescuento = bruto * sumaDescuentos;

            // Ponemos los valores en la línea
            linea.Bruto = bruto;
            linea.Base_Imponible = baseImponible;
            linea.Total = baseImponible + importeIVA + importeRE;
            linea.PorcentajeIVA = porcentajeIVA;
            linea.PorcentajeRE = porcentajeRE;
            linea.SumaDescuentos = sumaDescuentos;
            linea.ImporteDto = importeDescuento;
            linea.ImporteIVA = importeIVA;
            linea.ImporteRE = importeRE;
            
        }

        private string calcularAlmacen(string usuario, string empresa, int numeroPedido)
        {
            string almacen = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido).Almacén;
            if (almacen != "")
            {
                return almacen;
            }
            ParametroUsuario parametroUsuario;

            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "AlmacénPedidoVta");
                if (parametroUsuario != null && parametroUsuario.Valor.Trim() != "")
                {
                    return parametroUsuario.Valor.Trim();
                }
            }

            return Constantes.Productos.ALMACEN_POR_DEFECTO;
        }

        private string calcularDelegacion(string usuario, string empresa, int numeroPedido)
        {
            string delegacion = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido).Delegación;
            if (delegacion != "")
            {
                return delegacion;
            }
            ParametroUsuario parametroUsuario;

            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "DelegaciónDefecto");
                if (parametroUsuario != null && parametroUsuario.Valor.Trim() != "")
                {
                    return parametroUsuario.Valor.Trim();
                }
            }

            return Constantes.Empresas.DELEGACION_POR_DEFECTO;
        }

        private string calcularFormaVenta(string usuario, string empresa, int numeroPedido)
        {
            string formaVenta = db.LinPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido).Forma_Venta;
            if (formaVenta != "")
            {
                return formaVenta;
            }
            ParametroUsuario parametroUsuario;

            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "FormaVentaDefecto");
                if (parametroUsuario != null && parametroUsuario.Valor.Trim() != "")
                {
                    return parametroUsuario.Valor.Trim();
                }
            }

            return Constantes.Empresas.FORMA_VENTA_POR_DEFECTO;
        }

        public LinPedidoVta dividirLinea(LinPedidoVta lineaActual, short cantidad)
        {
            return this.dividirLinea(db, lineaActual, cantidad);
        }

        public LinPedidoVta dividirLinea(NVEntities db, LinPedidoVta linea, short cantidad)
        {
            if (linea.Cantidad <= cantidad)
            {
                return null; // no podemos dejar una cantidad mayor de la que ya hay
            }

            LinPedidoVta lineaNueva = (LinPedidoVta)db.Entry(linea).CurrentValues.ToObject();
            lineaNueva.Cantidad -= cantidad;
            this.calcularImportesLinea(lineaNueva);
            db.LinPedidoVtas.Add(lineaNueva); 

            linea.Cantidad = cantidad;
            this.calcularImportesLinea(linea);

            return lineaNueva;
        }
        
        private bool esSobrePedido(string producto, short cantidad)
        {
            Producto productoBuscado = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto);
            if (productoBuscado.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
            {
                return false;
            }

            ProductoDTO productoNuevo = new ProductoDTO(producto, db);

            return productoNuevo.CantidadDisponible() < cantidad;
        }

        // A las 11h de la mañana se cierra la ruta y los pedidos que se metan son ya para el día siguiente
        private DateTime fechaEntregaAjustada(DateTime fecha)
        {
            DateTime fechaMinima = DateTime.Now.Hour < 11 ? DateTime.Today : DateTime.Today.AddDays(1);

            return fechaMinima < fecha ? fecha : fechaMinima;
        }
    }
}