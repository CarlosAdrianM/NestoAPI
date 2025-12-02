using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
using System.Web.Http.Cors;
using System.Web.Http.Description;

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


        private readonly NVEntities db;
        private readonly ServicioPedidosVenta servicio = new ServicioPedidosVenta(); // inyectar para tests
        private readonly ServicioVendedores servicioVendedores = new ServicioVendedores();
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
        public async Task<List<ResumenPedidoVentaDTO>> GetPedidosVenta()
        {
            return await GetPedidosVenta("");
        }

        public async Task<List<ResumenPedidoVentaDTO>> GetPedidosVenta(string vendedor)
        {
            //IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
            //                                       join v in db.VendedoresPedidosGruposProductos

            //                                       //This is how you join by multiple values
            //                                       on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
            //                                       into jointData

            //                                       //This is how you actually turn the join into a left-join
            //                                       from jointRecord in jointData.DefaultIfEmpty()

            //                                       where (vendedor == "" || vendedor == null || c.Vendedor ==  vendedor || jointRecord.Vendedor == vendedor)
            //                                       select c;

            List<string> vendedoresLista;
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                vendedoresLista = new List<string>();
            }
            else
            {
                //// Crear una instancia del controlador VendedoresController
                //VendedoresController vendedoresController = new VendedoresController();

                //// Llamar al método GetVendedores con los parámetros deseados
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                //var resultado = await vendedoresController.GetVendedores(empresa, vendedor).ConfigureAwait(false);
                List<VendedorDTO> listaVendedores;
                listaVendedores = await servicioVendedores.VendedoresEquipo(empresa, vendedor).ConfigureAwait(false);
                //// Puedes procesar el resultado y devolver la respuesta adecuada
                //if (resultado is OkNegotiatedContentResult<List<VendedorDTO>>)
                //{
                //    listaVendedores = ((OkNegotiatedContentResult<List<VendedorDTO>>)resultado).Content;
                //}
                //else //(resultado is BadRequestErrorMessageResult)
                //{
                //    var mensajeError = ((BadRequestErrorMessageResult)resultado).Message;
                //    // Manejar el error de acuerdo a tus necesidades
                //    throw new Exception(mensajeError);
                //}
                vendedoresLista = listaVendedores.Select(v => v.vendedor).ToList();
            }

            IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
                                                       join v in db.VendedoresPedidosGruposProductos
                                                       on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
                                                       into jointData
                                                       from jointRecord in jointData.DefaultIfEmpty()
                                                       where vendedor == "" || vendedor == null || vendedoresLista.Contains(c.Vendedor) || (jointRecord != null && vendedoresLista.Contains(jointRecord.Vendedor))
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

                                                       where vendedor == "" || vendedor == null || c.Vendedor == vendedor || jointRecord.Vendedor == vendedor
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
            IServicioVendedores servicioVendedores = new ServicioVendedores();
            var vendedoresEquipo = servicioVendedores.VendedoresEquipoString(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).ConfigureAwait(false).GetAwaiter().GetResult();

            IQueryable<CabPedidoVta> pedidosVendedor = from c in db.CabPedidoVtas
                                                       join v in db.VendedoresPedidosGruposProductos

                                                       //This is how you join by multiple values
                                                       on new { empresa = c.Empresa, pedido = c.Número } equals new { empresa = v.Empresa, pedido = v.Pedido }
                                                       into jointData

                                                       //This is how you actually turn the join into a left-join
                                                       from jointRecord in jointData.DefaultIfEmpty()

                                                       where vendedor == "" || vendedor == null || vendedoresEquipo.Contains(c.Vendedor) || vendedoresEquipo.Contains(jointRecord.Vendedor)
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
            return pedido == null ? NotFound() : (IHttpActionResult)Ok(pedido);
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
                    Usuario = pedido.Usuario
                };
                _ = db.Modificaciones.Add(modificacion);
            }
            catch (Exception)
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

            // Carlos 28/11/25: Verificar si pertenece al grupo "Dirección", "Almacén" o "Tiendas" (igual que en POST)
            bool grupoPermitidoSinValidacion;
            bool grupoTiendasConAlmacenCorrecto = false;
            try
            {
                // Dirección y Almacén pueden modificar sin validación sin importar el almacén
                grupoPermitidoSinValidacion = User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                                             User.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) ||
                                             TieneParametroPermitirOmitirValidacion(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido.Usuario);

                // Tiendas puede modificar sin validación solo si todas las líneas están en su almacén
                if (!grupoPermitidoSinValidacion && User.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS))
                {
                    string usuarioParam = pedido.Usuario.Substring(pedido.Usuario.IndexOf("\\") + 1);
                    string almacenUsuario = ParametrosUsuarioController.LeerParametro(pedido.empresa, usuarioParam, "AlmacénPedidoVta");

                    if (!string.IsNullOrWhiteSpace(almacenUsuario) && pedido.Lineas.Any())
                    {
                        grupoTiendasConAlmacenCorrecto = pedido.Lineas.All(l => l.almacen == almacenUsuario);
                    }
                }
            }
            catch
            {
                grupoPermitidoSinValidacion = false;
                grupoTiendasConAlmacenCorrecto = false;
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
            if (!tienePendientes)
            {
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
            if (pedido.Lineas.Any(l => l.almacen != pedido.Lineas.First().almacen))
            {
                errorPersonalizado("No se pueden mezclar líneas de distintos almacenes");
            }


            // Carlos 17/06/22: no se pueden mezclar pedidos con presupuestos
            if (pedido.Lineas.Any(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) && !pedido.Lineas.All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO))
            {
                errorPersonalizado("No se pueden mezclar pedidos con presupuestos");
            }

            // Si todas las líneas están en -3 pero la cabecera dice que no es presupuesto, es porque queremos pasarlo a pedido
            bool aceptarPresupuesto = pedido.Lineas.All(l => l.estado == Constantes.EstadosLineaVenta.PRESUPUESTO) && !pedido.EsPresupuesto;

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
            if ((cabPedidoVta.IVA == null && pedido.iva != null) ||
                (cabPedidoVta.IVA != null && pedido.iva == null) ||
                (cabPedidoVta.IVA != null && pedido.iva != null &&
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

            cabPedidoVta.Usuario = pedido.Usuario;
            cabPedidoVta.Fecha_Modificación = DateTime.Now;

            db.Entry(cabPedidoVta).State = EntityState.Modified;

            // Carlos 02/03/17: gestionamos el vendedor por grupo de producto
            GestorComisiones.ActualizarVendedorPedidoGrupoProducto(db, cabPedidoVta, pedido);

            // Si alguno de los tres se cumple, no hace falta comprobarlo
            bool hayLineasNuevas = false;
            if (!cambiarClienteEnLineas && !cambiarContactoEnLineas && !cambiarIvaEnLineas)
            {
                hayLineasNuevas = pedido.Lineas.Where(l => l.id == 0).Any();
            }

            // Sacar diferencias entre el pedido original y el que hemos pasado:
            // - las líneas que la cantidad, o la base imponible sean diferentes hay que actualizarlas enteras
            // - las líneas que directamente no estén, hay que borrarlas
            // Carlos 23/10/25: Guardamos las cantidades y productos originales para luego poder mostrarlas en el correo
            foreach (LineaPedidoVentaDTO lineaPedido in pedido.Lineas.Where(l => l.id != 0))
            {
                var lineaOriginal = cabPedidoVta.LinPedidoVtas.SingleOrDefault(l => l.Nº_Orden == lineaPedido.id);
                if (lineaOriginal != null)
                {
                    lineaPedido.CantidadAnterior = lineaOriginal.Cantidad;
                    lineaPedido.ProductoAnterior = lineaOriginal.Producto;
                }
            }

            bool hayAlgunaLineaModificada = false;
            foreach (LinPedidoVta linea in cabPedidoVta.LinPedidoVtas.ToList())
            {
                LineaPedidoVentaDTO lineaEncontrada = pedido.Lineas.SingleOrDefault(l => l.id == linea.Nº_Orden);

                if (linea.Picking != 0 ||
                  !(
                    linea.Estado == Constantes.EstadosLineaVenta.PENDIENTE ||
                    linea.Estado == Constantes.EstadosLineaVenta.EN_CURSO ||
                    linea.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO
                    )
                )
                {
                    if (lineaEncontrada != null && lineaEncontrada.Cantidad == linea.Cantidad)
                    {
                        //lineaEncontrada.BaseImponible = linea.Base_Imponible;
                        //lineaEncontrada.Total = linea.Total;
                        continue;
                    }
                    else
                    {
                        errorPersonalizado("No se puede borrar la línea " + linea.Nº_Orden + " porque ya tiene picking o albarán");
                    }

                }

                if (lineaEncontrada == null || (lineaEncontrada.Cantidad == 0 && linea.Cantidad != 0))
                {
                    if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < this.gestor.FechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta, linea.Almacén)))
                    {
                        errorPersonalizado("No se puede borrar la línea " + linea.Nº_Orden + " porque ya tiene picking");
                    }
                    _ = db.LinPedidoVtas.Remove(linea);
                }
                else
                {
                    bool modificado = false;
                    if (linea.Producto?.Trim() != lineaEncontrada.Producto?.Trim())
                    {
                        Producto producto = db.Productos.Include(f => f.Familia1).Where(p => p.Empresa == pedido.empresa && p.Número == lineaEncontrada.Producto).SingleOrDefault();
                        if (producto.Estado < Constantes.Productos.ESTADO_NO_SOBRE_PEDIDO)
                        {
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
                    if (linea.Texto?.Trim() != lineaEncontrada.texto?.Trim())
                    {
                        linea.Texto = lineaEncontrada.texto;
                        modificado = true;
                    }
                    if (linea.Precio != lineaEncontrada.PrecioUnitario)
                    {
                        linea.Precio = lineaEncontrada.PrecioUnitario;
                        modificado = true;
                    }
                    if (linea.Cantidad != lineaEncontrada.Cantidad)
                    {
                        linea.Cantidad = (short)lineaEncontrada.Cantidad;
                        modificado = true;
                    }
                    if (linea.DescuentoProducto != lineaEncontrada.DescuentoProducto)
                    {
                        linea.DescuentoProducto = lineaEncontrada.DescuentoProducto;
                        modificado = true;
                    }
                    if (linea.Descuento != lineaEncontrada.DescuentoLinea)
                    {
                        linea.Descuento = lineaEncontrada.DescuentoLinea;
                        modificado = true;
                    }
                    // El DescuentoPP está en la cabecera
                    if (pedido.DescuentoPP != linea.DescuentoPP)
                    {
                        linea.DescuentoPP = pedido.DescuentoPP;
                        modificado = true;
                    }
                    if (linea.Aplicar_Dto != lineaEncontrada.AplicarDescuento)
                    {
                        linea.Aplicar_Dto = lineaEncontrada.AplicarDescuento;
                        modificado = true;
                    }
                    if (linea.Fecha_Entrega != lineaEncontrada.fechaEntrega)
                    {
                        linea.Fecha_Entrega = lineaEncontrada.fechaEntrega;
                        modificado = true;
                    }
                    //if (linea.Grupo != lineaEncontrada.GrupoProducto)
                    //{
                    //    linea.Grupo = lineaEncontrada.GrupoProducto;
                    //    modificado = true;
                    //}
                    //if (linea.SubGrupo != lineaEncontrada.SubgrupoProducto)
                    //{
                    //    linea.SubGrupo = lineaEncontrada.SubgrupoProducto;
                    //    modificado = true;
                    //}

                    if (modificado)
                    {
                        hayAlgunaLineaModificada = true;
                        if (linea.Picking != 0 || (algunaLineaTienePicking && DateTime.Today < this.gestor.FechaEntregaAjustada(linea.Fecha_Entrega, pedido.ruta, linea.Almacén)))
                        {
                            errorPersonalizado("No se puede modificar la línea " + linea.Nº_Orden.ToString() + " porque ya tiene picking");
                        }
                        this.gestor.CalcularImportesLinea(linea);
                    }
                    //lineaEncontrada.BaseImponible = linea.Base_Imponible;
                    //lineaEncontrada.Total = linea.Total;
                }
            }


            // Modificamos las líneas
            if (cambiarClienteEnLineas || cambiarContactoEnLineas || cambiarIvaEnLineas || hayLineasNuevas || aceptarPresupuesto)
            {
                foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
                {
                    LinPedidoVta lineaPedido;

                    if (linea.id == 0)
                    {
                        ComprobarSiSePuedenInsertarLineas(pedido, algunaLineaTienePicking, linea); //da error si no se puede
                        lineaPedido = this.gestor.CrearLineaVta(linea, pedido.empresa, pedido.numero);
                        _ = db.LinPedidoVtas.Add(lineaPedido);
                        //linea.BaseImponible = lineaPedido.Base_Imponible;
                        //linea.Total = lineaPedido.Total;
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

                        //linea.BaseImponible = lineaPedido.Base_Imponible;
                        //linea.Total = lineaPedido.Total;

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
            // Carlos 01/12/25: Movemos respuestaValidacion fuera del if para poder pasarla al correo (Issue #48)
            RespuestaValidacion respuestaValidacion = null;
            if (hayAlgunaLineaModificada || hayLineasNuevas || aceptarPresupuesto)
            {
                respuestaValidacion = GestorPrecios.EsPedidoValido(pedido);

                // Carlos 28/11/25: Permitir omitir validación si (igual que en POST):
                // - Tiene rol Dirección o Almacén (sin importar almacenes), O
                // - Tiene rol Tiendas Y todas las líneas están en su almacén
                bool puedeOmitirValidacion = grupoPermitidoSinValidacion || grupoTiendasConAlmacenCorrecto;

                if (!pedido.CreadoSinPasarValidacion || !puedeOmitirValidacion)
                {
                    if (!respuestaValidacion.ValidacionSuperada)
                    {
                        throw new PedidoValidacionException(
                            respuestaValidacion.Motivo,
                            respuestaValidacion,
                            empresa: pedido.empresa,
                            pedido: pedido.numero,
                            cliente: pedido.cliente,
                            usuario: pedido.Usuario);
                    }
                }
            }
            // Carlos 01/12/25: Si el pedido se creó sin pasar validación pero no hubo cambios en las líneas,
            // calculamos la respuesta de validación para incluirla en el correo (Issue #48)
            else if (pedido.CreadoSinPasarValidacion)
            {
                respuestaValidacion = GestorPrecios.EsPedidoValido(pedido);
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
                            Usuario = pedido.Usuario
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
                        _ = db.Prepagos.Remove(prepago);
                    }
                }
            }


            // Carlos 21/07/22: guardamos los efectos
            if (pedido.crearEfectosManualmente && pedido.Efectos.Any())
            {
                foreach (var efectoPedido in pedido.Efectos)
                {
                    if (efectoPedido.Id != 0)
                    {
                        EfectoPedidoVenta efectoCabecera = cabPedidoVta.EfectosPedidoVentas.Where(
                            p => p.Id == efectoPedido.Id
                        ).Single();
                        efectoCabecera.FechaVencimiento = efectoPedido.FechaVencimiento;
                        efectoCabecera.Importe = efectoPedido.Importe;
                        efectoCabecera.FormaPago = efectoPedido.FormaPago;
                        efectoCabecera.CCC = string.IsNullOrWhiteSpace(efectoPedido.Ccc) ? null : efectoPedido.Ccc;
                        efectoCabecera.Usuario = pedido.Usuario;
                        efectoCabecera.FechaModificacion = DateTime.Now;
                    }
                    else
                    {
                        cabPedidoVta.EfectosPedidoVentas.Add(new EfectoPedidoVenta
                        {
                            FechaVencimiento = efectoPedido.FechaVencimiento,
                            Importe = efectoPedido.Importe,
                            FormaPago = efectoPedido.FormaPago,
                            CCC = string.IsNullOrWhiteSpace(efectoPedido.Ccc) ? null : efectoPedido.Ccc,
                            Usuario = pedido.Usuario
                        });
                    }
                }
                List<EfectoPedidoVenta> efectosBorrar = new List<EfectoPedidoVenta>();
                for (var i = 0; i < cabPedidoVta.EfectosPedidoVentas.Count(); i++)
                {
                    var efectoCabecera = cabPedidoVta.EfectosPedidoVentas.ElementAt(i);
                    if (!pedido.Efectos.Any(e =>
                        e.Id == efectoCabecera.Id
                    ))
                    {
                        efectosBorrar.Add(efectoCabecera);
                    }
                }
                _ = db.EfectosPedidosVentas.RemoveRange(efectosBorrar);
            }
            else
            {
                if (cabPedidoVta.EfectosPedidoVentas.Any())
                {
                    foreach (var efecto in cabPedidoVta.EfectosPedidoVentas.ToList())
                    {
                        _ = db.EfectosPedidosVentas.Remove(efecto);
                    }
                }
            }

            // Validación: verificar que ninguna línea tenga TipoLinea NULL
            var lineasConTipoNull = cabPedidoVta.LinPedidoVtas
                .Where(l => l.TipoLinea == null
                    && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE
                    && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .ToList();
            if (lineasConTipoNull.Any())
            {
                var errores = string.Join(", ", lineasConTipoNull.Select(l => $"línea {l.Nº_Orden}"));
                throw new InvalidOperationException(
                    $"No se puede modificar el pedido {pedido.numero} porque tiene {lineasConTipoNull.Count} línea(s) con tipo de línea en blanco (NULL). " +
                    $"Esto indica un error en la creación de las líneas. Líneas afectadas: {errores}");
            }

            try
            {
                _ = await db.SaveChangesAsync();
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

            // Carlos 02/12/25: Red de seguridad - cargar ParametrosIva si no viene para que el correo muestre IVA correcto (Issue #46)
            if (pedido.iva != null && (pedido.ParametrosIva == null || !pedido.ParametrosIva.Any()))
            {
                var parametros = db.ParametrosIVA
                    .Where(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100,
                        PorcentajeRecargoEquivalencia = (decimal)p.C__RE / 100
                    });
                pedido.ParametrosIva = await parametros.ToListAsync().ConfigureAwait(false);

                // Recalcular porcentajes en las líneas para el correo
                foreach (var linea in pedido.Lineas.Where(l => !string.IsNullOrEmpty(l.iva)))
                {
                    var parametro = pedido.ParametrosIva.SingleOrDefault(p => p.CodigoIvaProducto == l.iva.Trim());
                    if (parametro != null)
                    {
                        linea.PorcentajeIva = parametro.PorcentajeIvaProducto;
                        linea.PorcentajeRecargoEquivalencia = parametro.PorcentajeRecargoEquivalencia;
                    }
                }
            }

            // Carlos 01/12/25: Pasar respuestaValidacion al GestorPresupuestos para incluirla en el correo (Issue #48)
            GestorPresupuestos gestor = new GestorPresupuestos(pedido, respuestaValidacion);
            await gestor.EnviarCorreo("Modificación");

            return StatusCode(HttpStatusCode.NoContent);
        }

        public void ComprobarSiSePuedenInsertarLineas(PedidoVentaDTO pedido, bool algunaLineaTienePicking, LineaPedidoVentaDTO linea)
        {
            if (algunaLineaTienePicking && gestor.FechaEntregaAjustada(linea.fechaEntrega, pedido.ruta, linea.almacen) <= DateTime.Today && DateTime.Now.Hour >= Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS)
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

            // Carlos 12/01/25: Verificar si pertenece al grupo "Dirección", "Almacén" o "Tiendas"
            bool grupoPermitidoSinValidacion;
            bool grupoTiendasConAlmacenCorrecto = false;
            try
            {
                // Dirección y Almacén pueden crear sin validación sin importar el almacén
                grupoPermitidoSinValidacion = User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                                             User.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN) ||
                                             TieneParametroPermitirOmitirValidacion(pedido.empresa, pedido.Usuario);

                // Tiendas puede crear sin validación solo si todas las líneas están en su almacén
                if (!grupoPermitidoSinValidacion && User.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS))
                {
                    string usuarioParam = pedido.Usuario.Substring(pedido.Usuario.IndexOf("\\") + 1);
                    string almacenUsuario = ParametrosUsuarioController.LeerParametro(pedido.empresa, usuarioParam, "AlmacénPedidoVta");

                    if (!string.IsNullOrWhiteSpace(almacenUsuario) && pedido.Lineas.Any())
                    {
                        grupoTiendasConAlmacenCorrecto = pedido.Lineas.All(l => l.almacen?.Trim() == almacenUsuario.Trim());
                    }
                }
            }
            catch
            {
                grupoPermitidoSinValidacion = false;
                grupoTiendasConAlmacenCorrecto = false;
            }

            // Carlos 11/08/23:
            if (string.IsNullOrEmpty(pedido.formaPago) || string.IsNullOrEmpty(pedido.plazosPago))
            {
                throw new Exception("El pedido tiene que tener formas y plazos de pago");
            }

            // Carlos 28/09/15: ajustamos el primer vencimiento a los plazos de pago y a los días de pago
            DateTime vencimientoPedido;
            System.Data.Entity.Core.Objects.ObjectParameter primerVencimiento = new System.Data.Entity.Core.Objects.ObjectParameter("FechaOut", typeof(DateTime));
            PlazoPago plazoPago = db.PlazosPago.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Número == pedido.plazosPago);
            Empresa empresa = db.Empresas.SingleOrDefault(e => e.Número == pedido.empresa);
            vencimientoPedido = pedido.fecha.Value.AddDays(plazoPago.DíasPrimerPlazo);
            vencimientoPedido = vencimientoPedido.AddMonths(plazoPago.MesesPrimerPlazo);
            _ = db.prdAjustarDíasPagoCliente(pedido.empresa, pedido.cliente, pedido.contacto, vencimientoPedido, primerVencimiento);

            if (pedido.ParametrosIva == null)
            {
                var parametros = db.ParametrosIVA
                    .Where(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100,
                        PorcentajeRecargoEquivalencia = (decimal)p.C__RE / 100
                    });

                pedido.ParametrosIva = await parametros.ToListAsync().ConfigureAwait(false);
            }

            // El número que vamos a dar al pedido hay que leerlo de ContadoresGlobales
            ContadorGlobal contador = db.ContadoresGlobales.SingleOrDefault();
            if (pedido.numero == 0)
            {
                contador.Pedidos++;
                pedido.numero = contador.Pedidos;
            }

            CabPedidoVta cabecera = new CabPedidoVta
            {
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
                Usuario = pedido.Usuario
            };

            _ = db.CabPedidoVtas.Add(cabecera);
            GestorComisiones.CrearVendedorPedidoGrupoProducto(db, cabecera, pedido);


            //ParametrosUsuarioController parametrosUsuarioCtrl = new ParametrosUsuarioController();
            ParametroUsuario parametroUsuario;

            // Guardamos el parámetro de pedido, para que al abrir la ventana el usuario vea el pedido
            string usuarioParametro = pedido.Usuario.Substring(pedido.Usuario.IndexOf("\\") + 1);
            if (usuarioParametro != null && (usuarioParametro.Length < 7 || usuarioParametro.Substring(0, 7) != "Cliente"))
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == pedido.empresa && p.Usuario == usuarioParametro && p.Clave == "UltNumPedidoVta");
                parametroUsuario.Valor = pedido.numero.ToString();
            }

            // Declaramos las variables que se van a utilizar en el bucle de insertar líneas
            LinPedidoVta linPedido;
            int? maxNumeroOferta = 0;

            List<LinPedidoVta> lineasPedidoInsertar = new List<LinPedidoVta>();
            // Bucle de insertar líneas
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
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
                // Carlos 02/12/25: VistoBueno siempre true para evitar bloqueos en tiendas (Issue #45)
                // Solución temporal mientras se define la lógica definitiva
                linea.vistoBueno = true;
                linPedido = this.gestor.CrearLineaVta(linea, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta, pedido.vendedor);
                //linea.BaseImponible = linPedido.Base_Imponible;
                if (linea.GrupoProducto != linPedido.Grupo)
                {
                    linea.GrupoProducto = linPedido.Grupo;
                }
                if (linea.SubgrupoProducto != linPedido.SubGrupo)
                {
                    linea.SubgrupoProducto = linPedido.SubGrupo;
                }
                if (pedido.ParametrosIva.Any())
                {
                    linea.PorcentajeIva = pedido.ParametrosIva.Single(p => p.CodigoIvaProducto == linea.iva?.Trim()).PorcentajeIvaProducto;
                    linea.PorcentajeRecargoEquivalencia = pedido.ParametrosIva.Single(p => p.CodigoIvaProducto == linea.iva?.Trim()).PorcentajeRecargoEquivalencia;
                }
                //linea.Total = linPedido.Total;
                //db.LinPedidoVtas.Add(linPedido);
                lineasPedidoInsertar.Add(linPedido);
            }



            // Carlos: 18/01/19: insertamos en la agencia si es necesario
            if (pedido.ruta == Constantes.Pedidos.RUTA_GLOVO)
            {
                IServicioAgencias servicioAgencias = new ServicioAgencias();
                string codigoPostal = servicioAgencias.LeerCodigoPostal(pedido);
                RespuestaAgencia respuestaMaps = GestorAgenciasGlovo.PortesPorCodigoPostal(codigoPostal);

                if (pedido.Lineas.Sum(b => b.BaseImponible) < GestorImportesMinimos.IMPORTE_MINIMO_URGENTE)
                {
                    LineaPedidoVentaDTO lineaPortes = new LineaPedidoVentaDTO
                    {
                        tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
                        almacen = pedido.Lineas.FirstOrDefault().almacen,
                        Producto = Constantes.Cuentas.CUENTA_PORTES_GLOVO,
                        Cantidad = 1,
                        delegacion = pedido.Lineas.FirstOrDefault().delegacion,
                        formaVenta = pedido.Lineas.FirstOrDefault().formaVenta,
                        estado = Constantes.EstadosLineaVenta.EN_CURSO,
                        texto = "Portes entrega en 2 horas",
                        PrecioUnitario = respuestaMaps.Coste,
                        iva = pedido.iva,
                        vistoBueno = true,
                        usuario = pedido.Lineas.FirstOrDefault().usuario
                    };
                    if (pedido.ParametrosIva.Any())
                    {
                        lineaPortes.PorcentajeIva = pedido.ParametrosIva.Single(p => p.CodigoIvaProducto == lineaPortes.iva?.Trim()).PorcentajeIvaProducto;
                        lineaPortes.PorcentajeRecargoEquivalencia = pedido.ParametrosIva.Single(p => p.CodigoIvaProducto == lineaPortes.iva?.Trim()).PorcentajeRecargoEquivalencia;
                    }
                    linPedido = this.gestor.CrearLineaVta(lineaPortes, pedido.numero, pedido.empresa, pedido.iva, plazoPago, pedido.cliente, pedido.contacto, pedido.ruta, pedido.vendedor);
                    //lineaPortes.BaseImponible = linPedido.Base_Imponible;
                    //lineaPortes.Total = linPedido.Total;
                    //pedido.LineasPedido.Add(lineaPortes);
                    pedido.Lineas.Add(lineaPortes);
                    lineasPedidoInsertar.Add(linPedido);
                }
            }

            _ = db.LinPedidoVtas.AddRange(lineasPedidoInsertar);

            // Actualizamos el contador de ofertas
            if ((int)maxNumeroOferta != 0)
            {
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
                    Usuario = pedido.Usuario
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
                        Usuario = pedido.Usuario
                    });
                }
            }

            // Carlos 07/10/15:
            // ahora ya tenemos el importe del pedido, hay que mirar si los plazos de pago cambian

            // Carlos 04/01/18: comprobamos que las ofertas del pedido sean todas válidas
            // Siempre calculamos la validación para incluirla en el correo, aunque no la bloqueemos
            RespuestaValidacion respuestaValidacion = GestorPrecios.EsPedidoValido(pedido);

            // Carlos 12/01/25: Permitir omitir validación si:
            // - Tiene rol Dirección o Almacén (sin importar almacenes), O
            // - Tiene rol Tiendas Y todas las líneas están en su almacén
            bool puedeOmitirValidacion = grupoPermitidoSinValidacion || grupoTiendasConAlmacenCorrecto;

            if (!pedido.CreadoSinPasarValidacion || !puedeOmitirValidacion)
            {
                if (!respuestaValidacion.ValidacionSuperada)
                {
                    // Carlos 21/11/24: Usar PedidoValidacionException en lugar de ValidationException
                    // para que GlobalExceptionFilter lo maneje correctamente y el frontend pueda
                    // detectarlo por código de error "PEDIDO_VALIDACION_FALLO"
                    throw new PedidoValidacionException(
                        respuestaValidacion.Motivo,
                        respuestaValidacion,
                        empresa: pedido.empresa,
                        pedido: pedido.numero,
                        cliente: pedido.cliente,
                        usuario: pedido.Usuario);
                }
            }

            // Validación: verificar que ninguna línea tenga TipoLinea NULL
            var lineasConTipoNull = lineasPedidoInsertar.Where(l => l.TipoLinea == null).ToList();
            if (lineasConTipoNull.Any())
            {
                var errores = string.Join(", ", lineasConTipoNull.Select(l => $"línea {l.Nº_Orden}"));
                throw new InvalidOperationException(
                    $"No se puede crear el pedido {pedido.numero} porque tiene {lineasConTipoNull.Count} línea(s) con tipo de línea en blanco (NULL). " +
                    $"Esto indica un error en la creación de las líneas. Líneas afectadas: {errores}");
            }

            try
            {
                _ = await db.SaveChangesAsync();
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
                        message = recorremosExcepcion.Message + ". " + recorremosExcepcion.InnerException.Message;
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

            GestorPresupuestos gestor = new GestorPresupuestos(pedido, respuestaValidacion);
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
            IQueryable<LinPedidoVta> lineas = db.LinPedidoVtas.Where(l => l.Empresa == empresa && l.Número == pedido && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado < Constantes.EstadosLineaVenta.FACTURA);
            await Task.Run(() => gestor.CalcularDescuentoTodasLasLineas(lineas.ToList(), descuento));

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
            return dividirLinea(db, lineaActual, cantidad);
        }

        public LinPedidoVta dividirLinea(NVEntities db, LinPedidoVta linea, short cantidad)
        {
            if (linea.Cantidad <= cantidad)
            {
                return null; // no podemos dejar una cantidad mayor de la que ya hay
            }

            LinPedidoVta lineaNueva = (LinPedidoVta)db.Entry(linea).CurrentValues.ToObject();
            lineaNueva.Cantidad -= cantidad;
            gestor.CalcularImportesLinea(lineaNueva);
            _ = db.LinPedidoVtas.Add(lineaNueva);

            linea.Cantidad = cantidad;
            gestor.CalcularImportesLinea(linea);

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

            PedidoVentaDTO pedidoUnido = parametroStringIntPedido == null || parametroStringIntPedido.PedidoAmpliacion == null
                ? await gestor.UnirPedidos(parametroStringIntInt.Empresa, parametroStringIntInt.NumeroPedidoOriginal, parametroStringIntInt.NumeroPedidoAmpliacion).ConfigureAwait(false)
                : await gestor.UnirPedidos(parametroStringIntPedido.Empresa, parametroStringIntPedido.NumeroPedidoOriginal, parametroStringIntPedido.PedidoAmpliacion).ConfigureAwait(false);
            return pedidoUnido;
        }

        [HttpGet]
        [ResponseType(typeof(DateTime))]
        [Route("api/PedidosVenta/FechaAjustada")]
        public IHttpActionResult CalcularFechaEntrega(DateTime fecha, string ruta, string almacen)
        {
            var fechaAjustada = gestor.FechaEntregaAjustada(fecha, ruta, almacen);
            return Ok(fechaAjustada);
        }

        [HttpGet]
        [ResponseType(typeof(decimal))]
        [Route("api/PedidosVenta/ImporteReembolso")]
        public IHttpActionResult ImporteReembolso(string empresa, int pedido)
        {
            var importeReembolso = gestor.ImporteReembolso(empresa, pedido);
            return Ok(importeReembolso);
        }

        /// <summary>
        /// Obtiene los documentos de impresión para un pedido ya facturado.
        /// Genera PDFs con las copias y bandeja apropiadas según el tipo de ruta.
        /// Endpoint para ser usado desde AgenciasViewModel y otros lugares donde se necesite
        /// imprimir documentos con la misma lógica que la facturación de rutas.
        /// </summary>
        /// <param name="empresa">Empresa del pedido</param>
        /// <param name="numeroPedido">Número del pedido</param>
        /// <param name="numeroFactura">Número de factura (opcional, null o "FDM" si es fin de mes)</param>
        /// <param name="numeroAlbaran">Número de albarán (opcional)</param>
        /// <returns>Documentos listos para imprimir con PDFs, copias y bandeja</returns>
        [HttpGet]
        [ResponseType(typeof(DocumentosImpresionPedidoDTO))]
        [Route("api/PedidosVenta/{empresa}/{numeroPedido}/DocumentosImpresion")]
        public async Task<IHttpActionResult> ObtenerDocumentosImpresion(
            string empresa,
            int numeroPedido,
            string numeroFactura = null,
            int? numeroAlbaran = null)
        {
            try
            {
                // Crear el gestor con todas sus dependencias
                var servicioAlbaranes = new ServicioAlbaranesVenta();
                var servicioFacturas = new ServicioFacturas();  // No recibe parámetros, crea su propio NVEntities
                var gestorFacturas = new GestorFacturas(servicioFacturas);  // Recibe IServicioFacturas, no db
                var servicioTraspaso = new ServicioTraspasoEmpresa(db);
                var servicioNotasEntrega = new ServicioNotasEntrega(db);
                var servicioExtractoRuta = new ServicioExtractoRuta(db);

                var gestorFacturacionRutas = new GestorFacturacionRutas(
                    db,
                    servicioAlbaranes,
                    servicioFacturas,
                    gestorFacturas,
                    servicioTraspaso,
                    servicioNotasEntrega,
                    servicioExtractoRuta
                );

                var documentos = await gestorFacturacionRutas.ObtenerDocumentosImpresion(
                    empresa,
                    numeroPedido,
                    numeroFactura,
                    numeroAlbaran);

                return Ok(documentos);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error en ObtenerDocumentosImpresion: {ex.Message}");
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Verifica si los comentarios de un pedido indican que se debe imprimir documento físico.
        /// Útil para auto-marcar el checkbox de impresión en AgenciasViewModel.
        /// Busca frases como "factura física", "factura en papel", "albarán físico".
        /// </summary>
        /// <param name="comentarios">Comentarios del pedido</param>
        /// <returns>True si los comentarios indican impresión física</returns>
        [HttpGet]
        [Route("api/PedidosVenta/DebeImprimirDocumento")]
        public IHttpActionResult DebeImprimirDocumento(string comentarios)
        {
            // Crear una instancia temporal del gestor para usar su método
            var servicioAlbaranes = new ServicioAlbaranesVenta();
            var servicioFacturas = new ServicioFacturas();
            var gestorFacturas = new GestorFacturas(servicioFacturas);
            var servicioTraspaso = new ServicioTraspasoEmpresa(db);
            var servicioNotasEntrega = new ServicioNotasEntrega(db);
            var servicioExtractoRuta = new ServicioExtractoRuta(db);

            var gestorFacturacionRutas = new GestorFacturacionRutas(
                db,
                servicioAlbaranes,
                servicioFacturas,
                gestorFacturas,
                servicioTraspaso,
                servicioNotasEntrega,
                servicioExtractoRuta
            );

            bool debeImprimir = gestorFacturacionRutas.DebeImprimirDocumento(comentarios);
            return Ok(debeImprimir);
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

        private bool TieneParametroPermitirOmitirValidacion(string empresa, string usuario)
        {
            string valor = ParametrosUsuarioController.LeerParametro(empresa, usuario, "PermitirCrearPedidoConErroresValidacion");

            if (!string.IsNullOrWhiteSpace(valor))
            {
                valor = valor.Trim().ToUpperInvariant();
                return valor == "1" || valor == "TRUE" || valor == "SI" || valor == "SÍ";
            }

            return false;
        }
    }
}