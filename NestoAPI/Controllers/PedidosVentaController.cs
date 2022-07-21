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
using NestoAPI.Models.Picking;
using NestoAPI.Infraestructure;
using System.Web.Http.Cors;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Controllers
{
    public class PedidosVentaController : ApiController
    {
        private const int ESTADO_LINEA_EN_CURSO = Constantes.EstadosLineaVenta.EN_CURSO;
        private const int ESTADO_LINEA_PENDIENTE = Constantes.EstadosLineaVenta.PENDIENTE;
        
        public const int TIPO_LINEA_TEXTO = 0;
        public const int TIPO_LINEA_PRODUCTO = 1;
        public const int TIPO_LINEA_CUENTA_CONTABLE = 2;
        public const int TIPO_LINEA_INMOVILIZADO = 3;

        public const int NUMERO_PRESUPUESTOS_MOSTRADOS = 50;


        private NVEntities db;
        private readonly ServicioPedidosVenta servicio = new ServicioPedidosVenta(); // inyectar para tests
        private readonly GestorPedidosVenta gestor;
        // Carlos 04/09/15: lo pongo para desactivar el Lazy Loading
        public PedidosVentaController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            gestor = new GestorPedidosVenta(servicio);
    }

        // Carlos 31/05/18: para poder hacer tests sobre el controlador
        public PedidosVentaController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
            gestor = new GestorPedidosVenta(servicio);
        }


        // GET: api/PedidosVenta
        public List<ResumenPedidoVentaDTO> GetPedidosVenta()
        {
            return GetPedidosVenta("");
        }

        public List<ResumenPedidoVentaDTO> GetPedidosVenta(string vendedor)
        {
            IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
                                                   join v in db.VendedoresPedidosGruposProductos

                                                   //This is how you join by multiple values
                                                   on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
                                                   into jointData

                                                   //This is how you actually turn the join into a left-join
                                                   from jointRecord in jointData.DefaultIfEmpty()

                                                   where (vendedor == "" || vendedor == null || c.Vendedor ==  vendedor || jointRecord.Vendedor == vendedor)
                                                   select c;
            
            IQueryable<ResumenPedidoVentaDTO> cabeceraPedidos = pedidosVendedor
                .Join(db.LinPedidoVtas, c => new { empresa = c.Empresa, numero = c.Número }, l => new { empresa = l.Empresa, numero = l.Número }, (c, l) => new { c.Vendedor, c.Empresa, c.Número, c.Nº_Cliente, c.Cliente.Nombre, c.Cliente.Dirección, c.Cliente.CodPostal, c.Cliente.Población, c.Cliente.Provincia, c.Fecha, l.TipoLinea, l.Estado, l.Picking, l.Fecha_Entrega, l.Base_Imponible, l.Total, c.Ruta })
                .Where(c => c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .GroupBy(g => new { g.Empresa, g.Número, g.Nº_Cliente, g.Nombre, g.Dirección, g.CodPostal, g.Población, g.Provincia, g.Vendedor, g.Ruta })
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
                    //tieneFechasFuturas = x.FirstOrDefault(c => c.Fecha_Entrega > fechaEntregaAjustada(DateTime.Now, c.Ruta)) != null,
                    tienePendientes = x.FirstOrDefault(c => c.Estado < 0) != null,
                    tienePicking = x.FirstOrDefault(c => c.Picking != 0) != null,
                    baseImponible = x.Sum(c => c.Base_Imponible),
                    total = x.Sum(c => c.Total),
                    vendedor = x.Key.Vendedor.Trim(),
                    ruta = x.Key.Ruta.Trim()
                })
                .OrderByDescending(c => c.numero);

            List<ResumenPedidoVentaDTO> listaPedidos = cabeceraPedidos.ToList();

            foreach (ResumenPedidoVentaDTO cab in listaPedidos)
            {
                DateTime fechaEntregaFutura = gestor.FechaEntregaAjustada(DateTime.Now, cab.ruta);
                cab.tieneFechasFuturas = db.LinPedidoVtas.Any(c => c.Empresa == cab.empresa && c.Número == cab.numero && c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && c.Fecha_Entrega > fechaEntregaFutura);
                cab.ultimoSeguimiento = db.EnviosAgencias.Where(e => e.Pedido == cab.numero).OrderByDescending(e => e.Numero).FirstOrDefault()?.CodigoBarras;
            }

            return listaPedidos;
        }

        public List<ResumenPedidoVentaDTO> GetPedidosVenta(string vendedor, string cliente)
        {
            IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
                                                       join v in db.VendedoresPedidosGruposProductos

                                                       //This is how you join by multiple values
                                                       on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
                                                       into jointData

                                                       //This is how you actually turn the join into a left-join
                                                       from jointRecord in jointData.DefaultIfEmpty()

                                                       where (vendedor == "" || vendedor == null || c.Vendedor == vendedor || jointRecord.Vendedor == vendedor)
                                                       select c;

            IQueryable<ResumenPedidoVentaDTO> cabeceraPedidos = pedidosVendedor
                .Join(db.LinPedidoVtas, c => new { empresa = c.Empresa, numero = c.Número }, l => new { empresa = l.Empresa, numero = l.Número }, (c, l) => new { c.Vendedor, c.Empresa, c.Número, c.Nº_Cliente, c.Cliente.Nombre, c.Cliente.Dirección, c.Cliente.CodPostal, c.Cliente.Población, c.Cliente.Provincia, c.Fecha, l.TipoLinea, l.Estado, l.Picking, l.Fecha_Entrega, l.Base_Imponible, l.Total, c.Ruta })
                .Where(c => c.Nº_Cliente == cliente)
                .GroupBy(g => new { g.Empresa, g.Número, g.Nº_Cliente, g.Nombre, g.Dirección, g.CodPostal, g.Población, g.Provincia, g.Vendedor, g.Ruta })
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
                    //tieneFechasFuturas = x.FirstOrDefault(c => c.Fecha_Entrega > fechaEntregaAjustada(DateTime.Now, c.Ruta)) != null,
                    tienePendientes = x.FirstOrDefault(c => c.Estado < 0) != null,
                    tienePicking = x.FirstOrDefault(c => c.Picking != 0) != null,
                    baseImponible = x.Sum(c => c.Base_Imponible),
                    total = x.Sum(c => c.Total),
                    vendedor = x.Key.Vendedor.Trim(),
                    ruta = x.Key.Ruta.Trim()
                })
                .OrderByDescending(c => c.numero)
                .Take(NUMERO_PRESUPUESTOS_MOSTRADOS);

            List<ResumenPedidoVentaDTO> listaPedidos = cabeceraPedidos.ToList();

            foreach (ResumenPedidoVentaDTO cab in listaPedidos)
            {
                DateTime fechaEntregaFutura = gestor.FechaEntregaAjustada(DateTime.Now, cab.ruta);
                cab.tieneFechasFuturas = db.LinPedidoVtas.FirstOrDefault(c => c.Empresa == cab.empresa && c.Número == cab.numero && c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && c.Fecha_Entrega > fechaEntregaFutura) != null;
                cab.ultimoSeguimiento = db.EnviosAgencias.Where(e => e.Pedido == cab.numero).OrderByDescending(e => e.Numero).FirstOrDefault()?.CodigoBarras;
            }

            return listaPedidos;
        }

        public IQueryable<ResumenPedidoVentaDTO> GetPedidosVenta(string vendedor, int estado)
        {
            IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
                                                       join v in db.VendedoresPedidosGruposProductos

                                                       //This is how you join by multiple values
                                                       on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
                                                       into jointData

                                                       //This is how you actually turn the join into a left-join
                                                       from jointRecord in jointData.DefaultIfEmpty()

                                                       where (vendedor == "" || vendedor == null || c.Vendedor == vendedor || jointRecord.Vendedor == vendedor)
                                                       select c;

            IQueryable<ResumenPedidoVentaDTO> cabeceraPedidos = pedidosVendedor
                .Join(db.LinPedidoVtas, c => new { empresa = c.Empresa, numero = c.Número }, l => new { empresa = l.Empresa, numero = l.Número }, (c, l) => new { c.Vendedor, c.Empresa, c.Número, c.Nº_Cliente, c.Cliente.Nombre, c.Cliente.Dirección, c.Cliente.CodPostal, c.Cliente.Población, c.Cliente.Provincia, c.Fecha, l.TipoLinea, l.Estado, l.Picking, l.Fecha_Entrega, l.Base_Imponible, l.Total, c.Ruta })
                .Where(c => c.Estado == estado)
                .GroupBy(g => new { g.Empresa, g.Número, g.Nº_Cliente, g.Nombre, g.Dirección, g.CodPostal, g.Población, g.Provincia, g.Vendedor, g.Ruta })
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
                    //tienePendientes = x.FirstOrDefault(c => c.Estado == Constantes.EstadosLineaVenta.PENDIENTE) != null,
                    tienePresupuesto = x.FirstOrDefault(c => c.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO) != null,
                    tienePicking = x.FirstOrDefault(c => c.Picking != 0) != null,
                    baseImponible = x.Sum(c => c.Base_Imponible),
                    total = x.Sum(c => c.Total),
                    vendedor = x.Key.Vendedor.Trim(),
                    ruta = x.Key.Ruta.Trim()
                })
                .OrderByDescending(c => c.numero).Take(NUMERO_PRESUPUESTOS_MOSTRADOS);

            foreach (ResumenPedidoVentaDTO cab in cabeceraPedidos)
            {
                DateTime fechaEntregaFutura = gestor.FechaEntregaAjustada(DateTime.Now, cab.ruta);
                cab.tieneFechasFuturas = db.LinPedidoVtas.FirstOrDefault(c => c.Empresa == cab.empresa && c.Número == cab.numero && c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && c.Fecha_Entrega > fechaEntregaFutura) != null;
            }

            return cabeceraPedidos;
        }

        // GET: api/PedidosVenta/5
        [ResponseType(typeof(PedidoVentaDTO))]
        public async Task<IHttpActionResult> GetPedidoVenta(string empresa, int numero)
        {
            PedidoVentaDTO pedido = await GestorPedidosVenta.LeerPedido(empresa, numero).ConfigureAwait(false);
            if (pedido == null)
            {
                return NotFound();
            }

            return Ok(pedido);
        }

        // PUT: api/PedidosVenta/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPedidoVenta(PedidoVentaDTO pedido)
        {

            CabPedidoVta cabPedidoVta = db.CabPedidoVtas.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.numero);

            try
            {
                // Guardamos registro de los cambios
                Modificacion modificacion = new Modificacion
                {
                    Tabla = "Pedidos",
                    Anterior = JsonConvert.SerializeObject(cabPedidoVta),
                    Nuevo = JsonConvert.SerializeObject(pedido),
                    Usuario = pedido.usuario
                };
                db.Modificaciones.Add(modificacion);
            } catch (Exception ex)
            {
                
            }
            

            db.Entry(cabPedidoVta).Collection(c => c.Prepagos).Load();
            db.Entry(cabPedidoVta).Collection(c => c.EfectosPedidoVentas).Load();

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            if (pedido.empresa != cabPedidoVta.Empresa.Trim() || pedido.numero != cabPedidoVta.Número)
            {
                return BadRequest();
            }

            Cliente cliente = await db.Clientes.SingleAsync(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            if (cliente == null || string.IsNullOrWhiteSpace(cliente.CIF_NIF))
            {
                throw new ArgumentException("No se pueden modificar pedidos de clientes sin NIF");
            }
            
            // Cargamos las líneas
            //db.Entry(cabPedidoVta).Reference(l => l.LinPedidoVtas).Load();
            cabPedidoVta = db.CabPedidoVtas.Include(l => l.LinPedidoVtas).SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.numero);

            // Comprobar que tiene líneas pendientes de servir, en caso contrario no se permite la edición
            bool tienePendientes = cabPedidoVta.LinPedidoVtas.Any(l => (l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO) || l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO);
            if (!tienePendientes) {
                errorPersonalizado("No se puede modificar un pedido ya facturado");
            }

            // Carlos 15/03/18: no se puede ampliar una nota de entrega
            if (cabPedidoVta.NotaEntrega)
            {
                errorPersonalizado("No se puede ampliar una nota de entrega");
            }

            // En una primera fase no permitimos modificar si ya está impresa la etiqueta de la agencia
            // En una segunda fase se podría ajustar para permitir modificar algunos campos, aún con la etiqueta impresa
            // bool estaImpresaLaEtiqueta = db.EnviosAgencias.FirstOrDefault(e => e.Pedido == pedido.numero && e.Estado == ESTADO_ENVIO_EN_CURSO) != null;
            var etiquetaAgencia = db.EnviosAgencias.SingleOrDefault(e => e.Estado == Constantes.Agencias.ESTADO_EN_CURSO && e.Pedido == pedido.numero);

            // En una primera fase no permitimos modificar si alguna línea de las pendientes tiene picking
            // En una segunda fase se podría ajustar para permitir modificar algunos campos, aún teniendo picking
            //bool algunaLineaTienePicking = estaImpresaLaEtiqueta || cabPedidoVta.LinPedidoVtas.FirstOrDefault(l => l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO && l.Picking > 0) != null;
            bool algunaLineaTienePicking = cabPedidoVta.LinPedidoVtas.Any(l => l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO && l.Picking > 0);


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
                if (etiquetaAgencia != null)
                {
                    errorPersonalizado("No se puede cambiar el contacto " + pedido.numero + " porque ya tiene lo tiene la agencia");
                }

                cabPedidoVta.Contacto = pedido.contacto;
                // hay cambiar el contacto a todas las líneas
                // ojo con la PK, que igual no puede haber diferentes contactos en un mismo pedido
                // comprobarlo con algunas facturas de FDM
                cambiarContactoEnLineas = true;
            }

            // Carlos 17/06/22: no se pueden mezclar lineas de distintos almacenes
            if (pedido.LineasPedido.Any(l => l.almacen != pedido.LineasPedido.First().almacen))
            {
                errorPersonalizado("No se pueden mezclar líneas de distintos almacenes");
            }


            // Carlos 17/06/22: no se pueden mezclar pedidos con presupuestos
            if (pedido.LineasPedido.Any(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) && !pedido.LineasPedido.All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO))
            {
                errorPersonalizado("No se pueden mezclar pedidos con presupuestos");
            }

            // Si todas las líneas están en -3 pero la cabecera dice que no es presupuesto, es porque queremos pasarlo a pedido
            bool aceptarPresupuesto = pedido.LineasPedido.All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) && !pedido.EsPresupuesto;
            
            cabPedidoVta.Fecha = pedido.fecha;
            // La forma de pago influye en el importe del reembolso de la agencia. Si se modifica la forma de pago
            // hay que modificar el importe del reembolso
            FormaPago formaPago = db.FormasPago.Single(f => f.Empresa == pedido.empresa && f.Número == pedido.formaPago);
            cabPedidoVta.Forma_Pago = pedido.formaPago;
            // Ojo, que el CCC va en función de la forma de pago.
            // Mirad cómo lo hace la plantilla e intentar traer aquí la lógica (y la plantilla llame aquí)
            if (formaPago.CCCObligatorio && pedido.ccc == null)
            {
                pedido.ccc = cliente.CCC;
            }
            cabPedidoVta.CCC = formaPago.CCCObligatorio ? pedido.ccc : null;
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

            if (cambiarIvaEnLineas && pedido.iva == null)
            {
                cabPedidoVta.CCC = null;
                cabPedidoVta.Forma_Pago = Constantes.FormasPago.EFECTIVO;
                cabPedidoVta.PlazosPago = Constantes.PlazosPago.CONTADO;
                cabPedidoVta.Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;
            }

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

            // Carlos 02/03/17: gestionamos el vendedor por grupo de producto
            GestorComisiones.ActualizarVendedorPedidoGrupoProducto(db, cabPedidoVta, pedido);

            // Si alguno de los tres se cumple, no hace falta comprobarlo
            bool hayLineasNuevas = false;
            if (!cambiarClienteEnLineas && !cambiarContactoEnLineas && !cambiarIvaEnLineas)
            {
                hayLineasNuevas = pedido.LineasPedido.Where(l => l.id == 0).Any();
            }

            // Sacar diferencias entre el pedido original y el que hemos pasado:
            // - las líneas que la cantidad, o la base imponible sean diferentes hay que actualizarlas enteras
            // - las líneas que directamente no estén, hay que borrarlas
            bool hayAlgunaLineaModificada = false;
            foreach (LinPedidoVta linea in cabPedidoVta.LinPedidoVtas.ToList())
            {
                LineaPedidoVentaDTO lineaEncontrada = pedido.LineasPedido.SingleOrDefault(l => l.id == linea.Nº_Orden);

                if (linea.Picking != 0 || 
                  !(
                    linea.Estado == Constantes.EstadosLineaVenta.PENDIENTE || 
                    linea.Estado == Constantes.EstadosLineaVenta.EN_CURSO || 
                    linea.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO
                    )
                )
                {
                    if (lineaEncontrada != null && lineaEncontrada.cantidad == linea.Cantidad)
                    {
                        lineaEncontrada.baseImponible = linea.Base_Imponible;
                        lineaEncontrada.total = linea.Total;
                        continue;
                    } else
                    {
                        errorPersonalizado("No se puede borrar la línea " + linea.Nº_Orden + " porque ya tiene picking o albarán");
                    }
                    
                }

                if (lineaEncontrada == null || (lineaEncontrada.cantidad == 0 && linea.Cantidad != 0))
                {
                    if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < this.gestor.FechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta)))
                    {
                        errorPersonalizado("No se puede borrar la línea " + linea.Nº_Orden + " porque ya tiene picking");
                    }
                    db.LinPedidoVtas.Remove(linea);
                } else 
                {
                    bool modificado = false;
                    if (linea.Producto?.Trim() != lineaEncontrada.producto?.Trim())
                    {
                        Producto producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == pedido.empresa && p.Número == lineaEncontrada.producto).SingleOrDefault();
                        if (producto.Estado < Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO) {
                            errorPersonalizado($"El producto {producto.Número} está en un estado nulo ({producto.Estado})");
                        }
                        linea.Producto = producto.Número;
                        linea.Grupo = producto.Grupo;
                        linea.SubGrupo = producto.SubGrupo;
                        linea.Familia = producto.Familia;
                        linea.TipoExclusiva = producto.Familia1.TipoExclusiva;
                        linea.PrecioTarifa = producto.PVP;
                        linea.Coste = (decimal)producto.PrecioMedio;
                        modificado = true;
                    }
                    if(linea.Texto?.Trim() != lineaEncontrada.texto?.Trim())
                    {
                        linea.Texto = lineaEncontrada.texto;
                        modificado = true;
                    }
                    if(linea.Precio != lineaEncontrada.precio)
                    {
                        linea.Precio = lineaEncontrada.precio;
                        modificado = true;
                    }
                    if (linea.Cantidad != lineaEncontrada.cantidad)
                    {
                        linea.Cantidad = lineaEncontrada.cantidad;
                        modificado = true;
                    }
                    if (linea.DescuentoProducto != lineaEncontrada.descuentoProducto)
                    {
                        linea.DescuentoProducto = lineaEncontrada.descuentoProducto;
                        modificado = true;
                    }
                    if (linea.Descuento != lineaEncontrada.descuento)
                    {
                        linea.Descuento = lineaEncontrada.descuento;
                        modificado = true;
                    }
                    // El DescuentoPP está en la cabecera
                    if (pedido.DescuentoPP != linea.DescuentoPP) 
                    {
                        linea.DescuentoPP = pedido.DescuentoPP;
                        modificado = true;
                    }
                    if (linea.Aplicar_Dto != lineaEncontrada.aplicarDescuento)
                    {
                        linea.Aplicar_Dto = lineaEncontrada.aplicarDescuento;
                        modificado = true;
                    }
                    if (linea.Fecha_Entrega != lineaEncontrada.fechaEntrega)
                    {
                        linea.Fecha_Entrega = lineaEncontrada.fechaEntrega;
                        modificado = true;
                    }

                    if (modificado)
                    {
                        hayAlgunaLineaModificada = true;
                        if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < this.gestor.FechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta)))
                        {
                            errorPersonalizado("No se puede modificar la línea " + linea.Nº_Orden.ToString() + " porque ya tiene picking");
                        }
                        this.gestor.CalcularImportesLinea(linea);
                    }
                    lineaEncontrada.baseImponible = linea.Base_Imponible;
                    lineaEncontrada.total = linea.Total;
                }
            }
            

            // Modificamos las líneas
            if (cambiarClienteEnLineas || cambiarContactoEnLineas || cambiarIvaEnLineas || hayLineasNuevas || aceptarPresupuesto) { 
                foreach (LineaPedidoVentaDTO linea in pedido.LineasPedido)
                {
                    LinPedidoVta lineaPedido;

                    if (linea.id == 0)
                    {
                        ComprobarSiSePuedenInsertarLineas(pedido, algunaLineaTienePicking, linea); //da error si no se puede
                        lineaPedido = this.gestor.CrearLineaVta(linea, pedido.empresa, pedido.numero);
                        db.LinPedidoVtas.Add(lineaPedido);
                        linea.baseImponible = lineaPedido.Base_Imponible;
                        linea.total = lineaPedido.Total;
                        continue;
                    }

                    if (cambiarClienteEnLineas || cambiarContactoEnLineas || cambiarIvaEnLineas)
                    {

                        lineaPedido = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.id);

                        if (lineaPedido == null || lineaPedido.Número != pedido.numero)
                        {
                            errorPersonalizado("Alguien modificó o eliminó la línea mientras se actualizaba el pedido");
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
                                errorPersonalizado("No se puede poner ese IVA al pedido");
                            }
                        }

                        linea.baseImponible = lineaPedido.Base_Imponible;
                        linea.total = lineaPedido.Total;

                        db.Entry(lineaPedido).State = EntityState.Modified;
                    }

                    if (aceptarPresupuesto)
                    {
                        lineaPedido = db.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == linea.id);
                        lineaPedido.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                    }
                }
            }

            // Carlos 16/02/21: si el reembolso en la etiqueta ha variado, damos error
            if (etiquetaAgencia != null)
            {
                decimal nuevoReembolso = GestorEnviosAgencia.ImporteReembolso(cabPedidoVta);
                if (nuevoReembolso != etiquetaAgencia.Reembolso)
                {
                    string mensajeError = string.Format("No se puede modificar el pedido porque ya hay una etiqueta impresa con {0} de reembolso y el nuevo reembolso serían {1}",
                        etiquetaAgencia.Reembolso.ToString("c"), nuevoReembolso.ToString("c"));
                    errorPersonalizado(mensajeError);
                }
            }

            // Carlos 04/01/18: comprobamos que las ofertas del pedido sean todas válidas
            if (hayAlgunaLineaModificada ||  hayLineasNuevas || aceptarPresupuesto)
            {
                RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);
                if (!respuesta.ValidacionSuperada)
                {
                    throw new Exception(respuesta.Motivo);
                }
            }

            // Carlos 27/08/20: comprobamos solo un prepago. Para comprobar todos hay que poner Id en PrepagoDTO
            // Carlos 12/01/22: usamos el concepto como Id
            if (pedido.Prepagos.Where(p => p.Factura == null).Any())
            {
                foreach (var prepagoPedido in pedido.Prepagos.Where(p => p.Factura == null))
                {
                    if (cabPedidoVta.Prepagos.Where(p => p.Factura == null && p.ConceptoAdicional == prepagoPedido.ConceptoAdicional).Any())
                    {
                        Prepago prepagoCabecera = cabPedidoVta.Prepagos.Where(p => p.Factura == null && p.ConceptoAdicional == prepagoPedido.ConceptoAdicional).Single();
                        prepagoCabecera.Importe = prepagoPedido.Importe;
                        prepagoCabecera.CuentaContable = prepagoPedido.CuentaContable;
                        prepagoCabecera.ConceptoAdicional = prepagoPedido.ConceptoAdicional;
                    }
                    else
                    {
                        cabPedidoVta.Prepagos.Add(new Prepago
                        {
                            Importe = prepagoPedido.Importe,
                            CuentaContable = prepagoPedido.CuentaContable,
                            ConceptoAdicional = prepagoPedido.ConceptoAdicional,
                            Usuario = pedido.usuario
                        });
                    }

                }
            }
            else
            {
                if (cabPedidoVta.Prepagos.Where(p => p.Factura == null).Any())
                {
                    /*
                    for (int i = 0; i < cabPedidoVta.Prepagos.Where(p => p.Factura == null).Count(); i++)
                    {
                        cabPedidoVta.Prepagos.Remove(cabPedidoVta.Prepagos.ElementAt(i));
                    }
                    */
                    foreach (var prepago in cabPedidoVta.Prepagos.Where(p => p.Factura == null).ToList())
                    {
                        db.Prepagos.Remove(prepago);
                    }
                }
            }


            // Carlos 21/07/22: guardamos los efectos
            if (pedido.crearEfectosManualmente && pedido.Efectos.Any())
            {
                foreach (var efectoPedido in pedido.Efectos)
                {
                    if (cabPedidoVta.EfectosPedidoVentas.Where(
                        p => p.FechaVencimiento == efectoPedido.FechaVencimiento && 
                        p.Importe == efectoPedido.Importe &&
                        p.FormaPago?.Trim() == efectoPedido.FormaPago?.Trim() &&
                        ((p.CCC?.Trim() == efectoPedido.Ccc?.Trim()) || (p.CCC == null && string.IsNullOrWhiteSpace(efectoPedido.Ccc)))
                    ).Any())
                    {
                        EfectoPedidoVenta efectoCabecera = cabPedidoVta.EfectosPedidoVentas.Where(
                            p => p.FechaVencimiento == efectoPedido.FechaVencimiento &&
                            p.Importe == efectoPedido.Importe &&
                            p.FormaPago?.Trim() == efectoPedido.FormaPago?.Trim() &&
                            ((p.CCC?.Trim() == efectoPedido.Ccc?.Trim()) || (p.CCC == null && string.IsNullOrWhiteSpace(efectoPedido.Ccc)))
                        ).Single();
                        efectoCabecera.FechaVencimiento = efectoPedido.FechaVencimiento;
                        efectoCabecera.Importe = efectoPedido.Importe;
                        efectoCabecera.FormaPago = efectoPedido.FormaPago;
                        efectoCabecera.CCC = string.IsNullOrWhiteSpace(efectoPedido.Ccc) ? null : efectoPedido.Ccc;
                    }
                    else
                    {
                        cabPedidoVta.EfectosPedidoVentas.Add(new EfectoPedidoVenta
                        {
                            FechaVencimiento = efectoPedido.FechaVencimiento,
                            Importe = efectoPedido.Importe,
                            FormaPago = efectoPedido.FormaPago,
                            CCC = string.IsNullOrWhiteSpace(efectoPedido.Ccc) ? null : efectoPedido.Ccc,
                            Usuario = pedido.usuario
                        });
                    }
                }
                List<EfectoPedidoVenta> efectosBorrar = new List<EfectoPedidoVenta>();
                for (var i = 0; i < cabPedidoVta.EfectosPedidoVentas.Count(); i++)
                {
                    var efectoCabecera = cabPedidoVta.EfectosPedidoVentas.ElementAt(i);
                    if (!pedido.Efectos.Any(e => 
                        e.FechaVencimiento == efectoCabecera.FechaVencimiento && 
                        e.Importe == efectoCabecera.Importe && 
                        e.FormaPago?.Trim() == efectoCabecera.FormaPago?.Trim() &&
                        ((e.Ccc?.Trim() == efectoCabecera.CCC?.Trim()) || (e.Ccc == null && string.IsNullOrWhiteSpace(efectoCabecera.CCC)))
                    ))
                    {
                        efectosBorrar.Add(efectoCabecera);
                    }
                }
                db.EfectosPedidosVentas.RemoveRange(efectosBorrar);
            }
            else
            {
                if (cabPedidoVta.EfectosPedidoVentas.Any())
                {                    
                    foreach (var efecto in cabPedidoVta.EfectosPedidoVentas.ToList())
                    {
                        db.EfectosPedidosVentas.Remove(efecto);
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
            catch (Exception e)
            {
                string message = e.Message;
                Exception recorremosExcepcion = e;
                while (recorremosExcepcion.InnerException != null)
                {
                    message = recorremosExcepcion.Message + ". " + recorremosExcepcion.InnerException.Message;
                    recorremosExcepcion = recorremosExcepcion.InnerException;
                }
                throw new HttpResponseException(Request.CreateErrorResponse(HttpStatusCode.NotAcceptable, message));
            }

            GestorPresupuestos gestor = new GestorPresupuestos(pedido);
            await gestor.EnviarCorreo("Modificación");

            return StatusCode(HttpStatusCode.NoContent);
        }

        public void ComprobarSiSePuedenInsertarLineas(PedidoVentaDTO pedido, bool algunaLineaTienePicking, LineaPedidoVentaDTO linea)
        {
            if (algunaLineaTienePicking && gestor.FechaEntregaAjustada(linea.fechaEntrega, pedido.ruta) <= DateTime.Today && DateTime.Now.Hour >= Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS)
            {
                errorPersonalizado("No se pueden insertar líneas porque son más de las " + Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS.ToString() + "h. y tiene fecha de entrega " + linea.fechaEntrega.ToShortDateString());
            }
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
                Forma_Pago = pedido.iva == null && pedido.plazosPago != "PRE" ? empresa.FormaPagoEfectivo : pedido.formaPago,
                PlazosPago = pedido.iva == null && pedido.plazosPago != "PRE" ? empresa.PlazosPagoDefecto : pedido.plazosPago,
                Primer_Vencimiento = (DateTime)primerVencimiento.Value,
                IVA = pedido.iva,
                Vendedor = pedido.vendedor,
                Periodo_Facturacion = pedido.periodoFacturacion,
                Ruta = pedido.ruta,
                Serie = pedido.serie,
                CCC = pedido.iva != null ? pedido.ccc : null,
                Origen = pedido.origen != null && pedido.origen.Trim() != "" ? pedido.origen : pedido.empresa,
                ContactoCobro = pedido.contactoCobro,
                NoComisiona = pedido.noComisiona,
                MantenerJunto = pedido.mantenerJunto,
                ServirJunto = pedido.servirJunto,
                ComentarioPicking = pedido.comentarioPicking,
                Comentarios = pedido.comentarios,
                Usuario = pedido.usuario
            };
            
            db.CabPedidoVtas.Add(cabecera);
            GestorComisiones.CrearVendedorPedidoGrupoProducto(db, cabecera, pedido);


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

            List<LinPedidoVta> lineasPedidoInsertar = new List<LinPedidoVta>();
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
                if (pedido.EsPresupuesto)
                {
                    linea.estado = Constantes.EstadosLineaVenta.PRESUPUESTO;
                }
                if (linea.almacen == Constantes.Productos.ALMACEN_TIENDA)
                {
                    linea.vistoBueno = true;
                    linea.fechaEntrega = DateTime.Today;
                }
                linPedido = this.gestor.CrearLineaVta(linea, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta, pedido.vendedor);
                linea.baseImponible = linPedido.Base_Imponible;
                linea.total = linPedido.Total;
                //db.LinPedidoVtas.Add(linPedido);
                lineasPedidoInsertar.Add(linPedido);
            }



            // Carlos: 18/01/19: insertamos en la agencia si es necesario
            if (pedido.ruta == Constantes.Pedidos.RUTA_GLOVO)
            {
                ServicioAgencias servicio = new ServicioAgencias();
                RespuestaAgencia respuestaMaps = await servicio.LeerDireccionPedidoGoogleMaps(pedido);

                if (pedido.LineasPedido.Sum(b => b.baseImponible) < GestorImportesMinimos.IMPORTE_MINIMO)
                {
                    LineaPedidoVentaDTO lineaPortes = new LineaPedidoVentaDTO
                    {
                        tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                        almacen = Constantes.Productos.ALMACEN_TIENDA,
                        producto = Constantes.Cuentas.CUENTA_PORTES_GLOVO,
                        cantidad = 1,
                        delegacion = pedido.LineasPedido.FirstOrDefault().delegacion,
                        formaVenta = pedido.LineasPedido.FirstOrDefault().formaVenta,
                        estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        texto = "Portes Glovo",
                        precio = respuestaMaps.Coste,
                        iva = pedido.iva,
                        vistoBueno = true,
                        usuario = pedido.LineasPedido.FirstOrDefault().usuario
                    };
                    linPedido = this.gestor.CrearLineaVta(lineaPortes, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta, pedido.vendedor);
                    lineaPortes.baseImponible = linPedido.Base_Imponible;
                    lineaPortes.total = linPedido.Total;
                    //pedido.LineasPedido.Add(lineaPortes);
                    pedido.LineasPedido.Add(lineaPortes);
                    lineasPedidoInsertar.Add(linPedido);
                }
            }

            db.LinPedidoVtas.AddRange(lineasPedidoInsertar);

            // Actualizamos el contador de ofertas
            if ((int)maxNumeroOferta!=0) {
                contador.Oferta += (int)maxNumeroOferta;
            }

            foreach (var prepago in pedido.Prepagos)
            {
                cabecera.Prepagos.Add(new Prepago
                {
                    Importe = prepago.Importe,
                    Factura = prepago.Factura,
                    CuentaContable = prepago.CuentaContable,
                    ConceptoAdicional = prepago.ConceptoAdicional,
                    Usuario = pedido.usuario
                });
            }

            // Carlos 20/07/22: guardamos los efectos manuales
            if (pedido.crearEfectosManualmente)
            {
                foreach (var efecto in pedido.Efectos)
                {
                    cabecera.EfectosPedidoVentas.Add(new EfectoPedidoVenta
                    {
                        FechaVencimiento = efecto.FechaVencimiento,
                        Importe = efecto.Importe,
                        FormaPago = efecto.FormaPago,
                        CCC = efecto.Ccc,
                        Usuario = pedido.usuario
                    });
                }
            }
            
            // Carlos 07/10/15:
            // ahora ya tenemos el importe del pedido, hay que mirar si los plazos de pago cambian

            // Carlos 04/01/18: comprobamos que las ofertas del pedido sean todas válidas
            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);
            if (!respuesta.ValidacionSuperada)
            {
                throw new Exception(respuesta.Motivo);
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
                    string message = e.Message;
                    Exception recorremosExcepcion = e;
                    while (recorremosExcepcion.InnerException != null)
                    {
                        message = recorremosExcepcion.Message+ ". " + recorremosExcepcion.InnerException.Message;
                        recorremosExcepcion = recorremosExcepcion.InnerException;
                    }
                    throw new HttpResponseException(Request.CreateErrorResponse(HttpStatusCode.NotAcceptable, message));
                }
            }
            catch (Exception e)
            {
                string message = e.Message;
                // faltaría recorrer el InnerException
                throw new HttpResponseException(Request.CreateErrorResponse(HttpStatusCode.NotAcceptable, message));
            }

            GestorPresupuestos gestor = new GestorPresupuestos(pedido);
            await gestor.EnviarCorreo();

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

        // GET: api/PonerDescuentoTodasLasLineas
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> PonerDescuentoTodasLasLineas(string empresa, int pedido, decimal descuento)
        {
            IQueryable<LinPedidoVta> lineas = db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == pedido && l.Estado>= Constantes.EstadosLineaVenta .PENDIENTE && l.Estado < Constantes.EstadosLineaVenta.FACTURA);
            await Task.Run(() => this.gestor.CalcularDescuentoTodasLasLineas(lineas.ToList(), descuento));

            return Ok(lineas);
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
            this.gestor.CalcularImportesLinea(lineaNueva);
            db.LinPedidoVtas.Add(lineaNueva); 

            linea.Cantidad = cantidad;
            this.gestor.CalcularImportesLinea(linea);

            return lineaNueva;
        }

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/PedidosVenta/SePuedeServirPorAgencia")]
        public async Task<RespuestaAgencia> SePuedeServirPorAgencia(PedidoVentaDTO pedido)
        {
            IGestorAgencias gestor = new GestorAgenciasGlovo();
            
            return await gestor.SePuedeServirPedido(pedido, new ServicioAgencias(), new GestorStocks());
        }

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/PedidosVenta/UnirPedidos")]
        public async Task<PedidoVentaDTO> UnirPedidos(JObject parametro)
        {
            ParametroStringIntInt parametroStringIntInt = parametro.ToObject<ParametroStringIntInt>();
            ParametroStringIntPedido parametroStringIntPedido = parametro.ToObject<ParametroStringIntPedido>(); 

            if (parametroStringIntInt == null && parametroStringIntPedido == null)
            {
                throw new Exception("No se han pasado parametros");
            }

            PedidoVentaDTO pedidoUnido;
            if (parametroStringIntPedido == null || parametroStringIntPedido.PedidoAmpliacion == null)
            {
                pedidoUnido = await gestor.UnirPedidos(parametroStringIntInt.Empresa, parametroStringIntInt.NumeroPedidoOriginal, parametroStringIntInt.NumeroPedidoAmpliacion).ConfigureAwait(false);
            } else
            {
                pedidoUnido = await gestor.UnirPedidos(parametroStringIntPedido.Empresa, parametroStringIntPedido.NumeroPedidoOriginal, parametroStringIntPedido.PedidoAmpliacion).ConfigureAwait(false);
            }
            
            return pedidoUnido;
        }

        private void errorPersonalizado(string mensajePersonalizado)
        {
            var message = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(mensajePersonalizado)
            };
            var ex = new HttpResponseException(message);
            throw ex;
        }
    }
}