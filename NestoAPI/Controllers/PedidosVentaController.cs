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

namespace NestoAPI.Controllers
{
    public class PedidosVentaController : ApiController
    {
        private const int ESTADO_LINEA_EN_CURSO = Constantes.EstadosLineaVenta.EN_CURSO;
        private const int ESTADO_LINEA_PENDIENTE = Constantes.EstadosLineaVenta.PENDIENTE;
        private const int ESTADO_ENVIO_EN_CURSO = 0;

        public const int TIPO_LINEA_TEXTO = 0;
        public const int TIPO_LINEA_PRODUCTO = 1;
        public const int TIPO_LINEA_CUENTA_CONTABLE = 2;
        public const int TIPO_LINEA_INMOVILIZADO = 3;

        public const int NUMERO_PRESUPUESTOS_MOSTRADOS = 50;


        private NVEntities db;
        // Carlos 04/09/15: lo pongo para desactivar el Lazy Loading
        public PedidosVentaController()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
        }

        // Carlos 31/05/18: para poder hacer tests sobre el controlador
        public PedidosVentaController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
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
                DateTime fechaEntregaFutura = fechaEntregaAjustada(DateTime.Now, cab.ruta);
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
                DateTime fechaEntregaFutura = fechaEntregaAjustada(DateTime.Now, cab.ruta);
                cab.tieneFechasFuturas = db.LinPedidoVtas.FirstOrDefault(c => c.Empresa == cab.empresa && c.Número == cab.numero && c.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && c.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && c.Fecha_Entrega > fechaEntregaFutura) != null;
            }

            return cabeceraPedidos;
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
                .OrderBy(l => l.id)
                .ToList();

            List<VendedorGrupoProductoDTO> vendedoresGrupoProductoPedido = db.VendedoresPedidosGruposProductos.Where(v => v.Empresa == empresa && v.Pedido == numero)
                .Select(v => new VendedorGrupoProductoDTO
                {
                    vendedor = v.Vendedor,
                    grupoProducto = v.GrupoProducto
                })
                .ToList();

            PedidoVentaDTO pedido;
            try
            {
                pedido = new PedidoVentaDTO
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
                    origen = (cabPedidoVta.Origen != null && cabPedidoVta.Origen.Trim() != "") ? cabPedidoVta.Origen : cabPedidoVta.Empresa,
                    contactoCobro = cabPedidoVta.ContactoCobro,
                    noComisiona = cabPedidoVta.NoComisiona,
                    vistoBuenoPlazosPago = cabPedidoVta.vtoBuenoPlazosPago,
                    mantenerJunto = cabPedidoVta.MantenerJunto,
                    servirJunto = cabPedidoVta.ServirJunto,
                    usuario = cabPedidoVta.Usuario,
                    LineasPedido = lineasPedido,
                    VendedoresGrupoProducto = vendedoresGrupoProductoPedido,
                    EsPresupuesto = lineasPedido.FirstOrDefault(c => c.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) != null,
                };
            } catch (Exception ex)
            {
                throw ex;
            }

            /*
            GestorPresupuestos gestor = new GestorPresupuestos(pedido);
            await gestor.EnviarCorreo();
            */

            return Ok(pedido);
        }

        // PUT: api/PedidosVenta/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutPedidoVenta(PedidoVentaDTO pedido)
        {

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

            Cliente cliente = await db.Clientes.SingleAsync(c => c.Empresa == pedido.empresa && c.Nº_Cliente == pedido.cliente && c.Contacto == pedido.contacto);
            if (cliente == null || string.IsNullOrWhiteSpace(cliente.CIF_NIF))
            {
                throw new ArgumentException("No se pueden modificar pedidos de clientes sin NIF");
            }
            
            // Cargamos las líneas
            //db.Entry(cabPedidoVta).Reference(l => l.LinPedidoVtas).Load();
            cabPedidoVta = db.CabPedidoVtas.Include(l => l.LinPedidoVtas).SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.numero);

            // Comprobar que tiene líneas pendientes de servir, en caso contrario no se permite la edición
            bool tienePendientes = cabPedidoVta.LinPedidoVtas.FirstOrDefault(l => (l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO) || l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO) != null;
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

            // En una primera fase no permitimos modificar si alguna línea de las pendientes tiene picking
            // En una segunda fase se podría ajustar para permitir modificar algunos campos, aún teniendo picking
            //bool algunaLineaTienePicking = estaImpresaLaEtiqueta || cabPedidoVta.LinPedidoVtas.FirstOrDefault(l => l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO && l.Picking > 0) != null;
            bool algunaLineaTienePicking = cabPedidoVta.LinPedidoVtas.FirstOrDefault(l => l.Estado >= ESTADO_LINEA_PENDIENTE && l.Estado <= ESTADO_LINEA_EN_CURSO && l.Picking > 0) != null;


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
                var etiqueta = db.EnviosAgencias.SingleOrDefault(e => e.Estado == Constantes.Agencias.ESTADO_EN_CURSO && e.Pedido == pedido.numero);
                if (etiqueta != null)
                {
                    errorPersonalizado("No se puede cambiar el contacto " + pedido.numero + " porque ya tiene lo tiene la agencia");
                }

                cabPedidoVta.Contacto = pedido.contacto;
                // hay cambiar el contacto a todas las líneas
                // ojo con la PK, que igual no puede haber diferentes contactos en un mismo pedido
                // comprobarlo con algunas facturas de FDM
                cambiarContactoEnLineas = true;
            }

            // Si todas las líneas están en -3 pero la cabecera dice que no es presupuesto, es porque queremos pasarlo a pedido
            bool aceptarPresupuesto = pedido.LineasPedido.All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) && !pedido.EsPresupuesto;
            
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
            cabPedidoVta.Periodo_Facturacion = cambiarIvaEnLineas ? 
                Constantes.Pedidos.PERIODO_FACTURACION_NORMAL : pedido.periodoFacturacion;

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
                hayLineasNuevas = pedido.LineasPedido.Where(l => l.id == 0).FirstOrDefault() != null;
            }

            // Sacar diferencias entre el pedido original y el que hemos pasado:
            // - las líneas que la cantidad, o la base imponible sean diferentes hay que actualizarlas enteras

            // - las líneas que directamente no estén, hay que borrarlas
            bool hayAlgunaLineaModificada = false;
            foreach (LinPedidoVta linea in cabPedidoVta.LinPedidoVtas.ToList())
            {
                LineaPedidoVentaDTO lineaEncontrada = pedido.LineasPedido.SingleOrDefault(l => l.id == linea.Nº_Orden);

                if (linea.Picking != 0 || !(linea.Estado == -1 || linea.Estado == 1))
                {
                    if (lineaEncontrada != null)
                    {
                        lineaEncontrada.baseImponible = linea.Base_Imponible;
                        lineaEncontrada.total = linea.Total;
                    }
                    continue;
                }

                if (lineaEncontrada == null || (lineaEncontrada.cantidad == 0 && linea.Cantidad != 0))
                {
                    if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < fechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta)))
                    {
                        errorPersonalizado("No se puede borrar la línea " + linea.Nº_Orden + " porque ya tiene picking");
                    }
                    db.LinPedidoVtas.Remove(linea);
                } else 
                {
                    bool modificado = false;
                    if (linea.Producto.Trim() != lineaEncontrada.producto.Trim())
                    {
                        Producto producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == pedido.empresa && p.Número == lineaEncontrada.producto).SingleOrDefault();
                        linea.Producto = producto.Número;
                        linea.Grupo = producto.Grupo;
                        linea.SubGrupo = producto.SubGrupo;
                        linea.Familia = producto.Familia;
                        linea.TipoExclusiva = producto.Familia1.TipoExclusiva;
                        linea.PrecioTarifa = producto.PVP;
                        linea.Coste = (decimal)producto.PrecioMedio;
                        modificado = true;
                    }
                    if(linea.Texto.Trim() != lineaEncontrada.texto.Trim())
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
                        if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < fechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta)))
                        {
                            errorPersonalizado("No se puede modificar la línea " + linea.Nº_Orden.ToString() + " porque ya tiene picking");
                        }
                        calcularImportesLinea(linea);
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
                        lineaPedido = crearLineaVta(linea, pedido.empresa, pedido.numero);
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
            
            // Carlos 04/01/18: comprobamos que las ofertas del pedido sean todas válidas
            if (hayAlgunaLineaModificada ||  hayLineasNuevas || aceptarPresupuesto)
            {
                RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);
                if (!respuesta.ValidacionSuperada)
                {
                    throw new Exception(respuesta.Motivo);
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
            catch (Exception ex)
            {
                throw ex;
            }

            GestorPresupuestos gestor = new GestorPresupuestos(pedido);
            await gestor.EnviarCorreo("Modificación");

            return StatusCode(HttpStatusCode.NoContent);
        }

        public void ComprobarSiSePuedenInsertarLineas(PedidoVentaDTO pedido, bool algunaLineaTienePicking, LineaPedidoVentaDTO linea)
        {
            if (algunaLineaTienePicking && fechaEntregaAjustada(linea.fechaEntrega, pedido.ruta) <= DateTime.Today && DateTime.Now.Hour >= Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS)
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
                linPedido = crearLineaVta(linea, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta);
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
                    linPedido = crearLineaVta(lineaPortes, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta);
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
            await Task.Run(() => this.calcularDescuentoTodasLasLineas(lineas.ToList(), descuento));

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
            } else if (pedido.IVA != null && linea.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
            {
                PlanCuenta cuenta = db.PlanCuentas.SingleOrDefault(c => c.Empresa == empresa && c.Nº_Cuenta == linea.producto);
                linea.iva = cuenta.IVA;
            }
            return crearLineaVta(linea, numeroPedido, pedido.Empresa, pedido.IVA, plazo, pedido.Nº_Cliente, pedido.Contacto, pedido.Ruta);
        }

        public LinPedidoVta crearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto)
        {
            CabPedidoVta pedido = db.CabPedidoVtas.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroPedido);
            return crearLineaVta(linea, numeroPedido, empresa, iva, plazoPago, cliente, contacto, pedido.Ruta);
        }

        public LinPedidoVta crearLineaVta(LineaPedidoVentaDTO linea, int numeroPedido, string empresa, string iva, PlazoPago plazoPago, string cliente, string contacto, string ruta)
        {
            string tipoExclusiva, grupo, subGrupo, familia, ivaRepercutido;
            decimal coste, precioTarifa;
            short estadoProducto;
            CentrosCoste centroCoste = null;

            // Calculamos las variables que se pueden corresponden a la cabecera
            decimal descuentoCliente, descuentoPP;
            DescuentosCliente dtoCliente = db.DescuentosClientes.OrderBy(d => d.ImporteMínimo).FirstOrDefault(d => d.Empresa == empresa && d.Nº_Cliente == cliente && d.Contacto == contacto);
            descuentoCliente = dtoCliente != null ? dtoCliente.Descuento : 0;
            descuentoPP = plazoPago.DtoProntoPago;

            switch (linea.tipoLinea)
            {
                
                case Constantes.TiposLineaVenta.PRODUCTO:
                    if (linea.cantidad == 0)
                    {
                        errorPersonalizado("No se pueden crear líneas de producto con cantidad 0");
                    }
                    Producto producto = db.Productos.SingleOrDefault(p => p.Empresa == empresa && p.Número == linea.producto);
                    precioTarifa = (decimal)producto.PVP;
                    coste = (decimal)producto.PrecioMedio;
                    grupo = producto.Grupo;
                    subGrupo = producto.SubGrupo;
                    familia = producto.Familia;
                    ivaRepercutido = producto.IVA_Repercutido; // ¿Se usa? Ojo, que puede venir el IVA nulo y estar bien
                    estadoProducto = (short)producto.Estado;
                    break;

                case Constantes.TiposLineaVenta.CUENTA_CONTABLE:
                    precioTarifa = 0;
                    coste = 0;
                    grupo = null;
                    subGrupo = null;
                    familia = null;
                    ivaRepercutido = db.Empresas.SingleOrDefault(e => e.Número == empresa).TipoIvaDefecto;
                    estadoProducto = 0;
                    if (linea.producto.Substring(0, 1) == "6" || linea.producto.Substring(0, 1) == "7")
                    {
                        centroCoste = calcularCentroCoste(empresa, numeroPedido);
                    }
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
            // esto habría que refactorizarlo para que solo lo lea una vez por pedido
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
                Texto = linea.texto.Length > 50 ? linea.texto.Substring(0, 50) : linea.texto, // porque 50 es la longitud del campo
                Cantidad = linea.cantidad,
                Fecha_Entrega = this.fechaEntregaAjustada(linea.fechaEntrega.Date, ruta),
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
                EstadoProducto = estadoProducto,
                CentroCoste = centroCoste != null ? centroCoste.Número : null,
                Departamento = centroCoste != null ? centroCoste.Departamento : null
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
            
            // Para que redondee a la baja
            bruto = (decimal)(linea.Cantidad * (decimal.Truncate((decimal)linea.Precio * 10000) / 10000));
            if (linea.Aplicar_Dto)
            {
                sumaDescuentos = (1 - (1 - (linea.DescuentoCliente)) * (1 - (linea.DescuentoProducto)) * (1 - (linea.Descuento)) * (1 - (linea.DescuentoPP)));
            }
            else
            {
                linea.DescuentoProducto = 0;
                sumaDescuentos = linea.Descuento;
            }
            baseImponible = Math.Round(bruto * (1 - sumaDescuentos), 2, MidpointRounding.AwayFromZero);
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
            if (almacen != null && almacen != "")
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
            if (delegacion != null && delegacion != "")
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
            if (formaVenta != null && formaVenta != "")
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

        
        private CentrosCoste calcularCentroCoste(string empresa, int numeroPedido)
        {
            CabPedidoVta cabPedidoCoste = db.CabPedidoVtas.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido);
            if (cabPedidoCoste == null)
            {
                cabPedidoCoste = db.CabPedidoVtas.Local.FirstOrDefault(l => l.Empresa == empresa && l.Número == numeroPedido);
            }
            string vendedor = cabPedidoCoste?.Vendedor;
            if (vendedor == null || vendedor == "")
            {
                throw new Exception("No se puede calcular el centro de coste del pedido " + numeroPedido.ToString() + ", porque falta el vendedor");
            }
            ParametroUsuario parametroUsuario;
            UsuarioVendedor usuarioVendedor = db.UsuarioVendedores.SingleOrDefault(u => u.Vendedor == vendedor);

            if (usuarioVendedor == null)
            {
                throw new Exception("El pedido " + numeroPedido.ToString() + " no tiene vendedor");
            }

            string usuario = usuarioVendedor.Usuario;
            string numeroCentroCoste = "";
            if (usuario != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == "CentroCosteDefecto");
                if (parametroUsuario != null && parametroUsuario.Valor.Trim() != "")
                {
                    numeroCentroCoste = parametroUsuario.Valor.Trim();
                }
            } else
            {
                throw new Exception("No se puede calcular el centro de coste del pedido " + numeroPedido.ToString() + ", porque el vendedor "+ vendedor +" no tiene un usuario asociado");
            }

            return db.CentrosCostes.SingleOrDefault(c => c.Empresa == empresa && c.Número == numeroCentroCoste);
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

        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/PedidosVenta/SePuedeServirPorAgencia")]
        public async Task<RespuestaAgencia> SePuedeServirPorAgencia(PedidoVentaDTO pedido)
        {
            IGestorAgencias gestor = new GestorAgenciasGlovo();
            
            return await gestor.SePuedeServirPedido(pedido, new ServicioAgencias(), new GestorStocks());
        }
        
        private bool esSobrePedido(string producto, short cantidad)
        {
            Producto productoBuscado = db.Productos.SingleOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Número == producto);
            if (productoBuscado.Estado == Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
            {
                return false;
            }

            ProductoPlantillaDTO productoNuevo = new ProductoPlantillaDTO(producto, db);

            return productoNuevo.CantidadDisponible() < cantidad;
        }

        // A las 11h de la mañana se cierra la ruta y los pedidos que se metan son ya para el día siguiente
        private DateTime fechaEntregaAjustada(DateTime fecha, string ruta)
        {
            DateTime fechaMinima;
            
            if (ruta != Constantes.Pedidos.RUTA_GLOVO && GestorImportesMinimos.esRutaConPortes(ruta))
            {
                fechaMinima = DateTime.Now.Hour < Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS ? DateTime.Today : DateTime.Today.AddDays(1);
            } else
            {
                fechaMinima = DateTime.Today;
            }
            
            return fechaMinima < fecha ? fecha : fechaMinima;
        }

        private void calcularDescuentoTodasLasLineas(List<LinPedidoVta> lineas, decimal descuento)
        {
            foreach (LinPedidoVta linea in lineas)
            {
                linea.Descuento = descuento;
                this.calcularImportesLinea(linea);
            }
        }

        private void copiarDatosProductoEnLinea(LinPedidoVta linea) {

        }

        private void errorPersonalizado(string mensajePersonalizado)
        {
            var message = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent(mensajePersonalizado)
            };
            throw new HttpResponseException(message);
        }
    }
}