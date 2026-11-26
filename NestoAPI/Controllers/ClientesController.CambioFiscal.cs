using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Clientes.CambioFiscal;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#876 (corte nuevo, Carlos 26/11/26): un cliente que cambia de forma fiscal. Ver qué se copiaría de su ficha
    /// anterior, copiarlo por bloques y pasar sus pedidos pendientes al nuevo. LA REGLA: <see cref="ReglaCopiaCambioFiscal"/>.
    /// </summary>
    public partial class ClientesController
    {
        /// <summary>Sustituible en los tests.</summary>
        internal IRepositorioCambioFiscal RepositorioCambioFiscal
        {
            get => repositorioCambioFiscal ?? (repositorioCambioFiscal = new RepositorioCambioFiscalSql(db.Database.Connection.ConnectionString));
            set => repositorioCambioFiscal = value;
        }
        private IRepositorioCambioFiscal repositorioCambioFiscal;

        /// <summary>(empresa, número, petición) → el CambiarCliente de PedidosVenta. Sustituible en los tests.</summary>
        internal Func<string, int, CambiarClientePedidoRequest, Task<ResultadoCambioClientePedido>> CambiarClienteDePedido { get; set; }

        /// <summary>Solo lectura: por bloque, lo que tiene cada cliente y lo que cambiaría; y los pedidos pendientes del anterior.</summary>
        [HttpGet]
        [Route("api/Clientes/CambioFiscal/Previa")]
        [ResponseType(typeof(CopiaCambioFiscalDTO))]
        public async Task<IHttpActionResult> GetPreviaCambioFiscal(string empresa, string clienteAnterior, string clienteNuevo)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string emp = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            ResultadoCambioFiscal<CopiaCambioFiscalDTO> r = await new ServicioCambioFiscal(RepositorioCambioFiscal)
                .Previa(emp, clienteAnterior, clienteNuevo, v => MotivoSinPermisoCambioFiscal(emp, clienteAnterior, v),
                    () => LeerPedidosPendientesCambioFiscal(emp, clienteAnterior?.Trim())).ConfigureAwait(true);
            return Traducir(r);
        }

        /// <summary>
        /// Copia del anterior al nuevo los bloques pedidos (por defecto todos) en UNA transacción, con rastro en Modificaciones
        /// (Tabla ClienteCambioFiscal) y publicando el cliente a Odoo. Con <paramref name="ensayo"/> (por defecto) no escribe nada.
        /// </summary>
        [HttpPost]
        [Route("api/Clientes/CambioFiscal/Copiar")]
        [ResponseType(typeof(CopiaCambioFiscalDTO))]
        public async Task<IHttpActionResult> PostCopiarCambioFiscal([FromBody] CopiaCambioFiscalRequest peticion, bool ensayo = true)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return Traducir(await CopiarCambioFiscal(peticion, ensayo).ConfigureAwait(true));
        }

        /// <summary>
        /// Pasa los pedidos pendientes del anterior al nuevo, uno a uno, por el CambiarCliente de siempre (recalcula; con picking,
        /// solo si no cambia nada). Con <paramref name="ensayo"/> (por defecto) dice qué cambiaría en cada uno sin tocar nada.
        /// </summary>
        [HttpPost]
        [Route("api/Clientes/CambioFiscal/PasarPedidos")]
        [ResponseType(typeof(PasarPedidosCambioFiscalDTO))]
        public async Task<IHttpActionResult> PostPasarPedidosCambioFiscal([FromBody] PasarPedidosCambioFiscalRequest peticion, bool ensayo = true)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string emp = string.IsNullOrWhiteSpace(peticion?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string usuario = UsuarioAuditoriaHelper.Resolver(User, peticion?.Usuario);
            Func<string, int, CambiarClientePedidoRequest, Task<ResultadoCambioClientePedido>> cambiar = CambiarClienteDePedido ?? CambiarClienteDePedidoPorPedidosVenta;
            ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO> r = await new ServicioCambioFiscal(RepositorioCambioFiscal)
                .PasarPedidos(peticion, ensayo, usuario, v => MotivoSinPermisoCambioFiscal(emp, peticion?.ClienteAnterior, v),
                    async numero =>
                    {
                        var cab = await db.CabPedidoVtas.Where(c => c.Empresa == emp && c.Número == numero)
                            .Select(c => new { c.Nº_Cliente, c.Contacto }).FirstOrDefaultAsync().ConfigureAwait(true);
                        return cab == null ? null : Tuple.Create(cab.Nº_Cliente?.Trim(), cab.Contacto?.Trim());
                    },
                    (numero, p) => cambiar(emp, numero, p)).ConfigureAwait(true);
            return Traducir(r);
        }

        /// <summary>El núcleo de Copiar (también lo usa el alta con <see cref="ClienteCrear.CopiarDelAnterior"/>).</summary>
        internal Task<ResultadoCambioFiscal<CopiaCambioFiscalDTO>> CopiarCambioFiscal(CopiaCambioFiscalRequest peticion, bool ensayo)
        {
            string emp = string.IsNullOrWhiteSpace(peticion?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string usuario = UsuarioAuditoriaHelper.Resolver(User, peticion?.Usuario);
            return new ServicioCambioFiscal(RepositorioCambioFiscal)
                .Copiar(peticion, ensayo, usuario, v => MotivoSinPermisoCambioFiscal(emp, peticion?.ClienteAnterior, v), PublicarClienteTrasCopia);
        }

        /// <summary>
        /// NestoAPI#876: en el alta, <see cref="ClienteCrear.ClienteAnterior"/> solo con origen CAMBIO-FISCAL, de un cliente que existe,
        /// y <see cref="ClienteCrear.CopiarDelAnterior"/> solo con él y con bloques que existen. Null = vale (o no viene).
        /// </summary>
        internal async Task<string> ValidarClienteAnteriorAlta(ClienteCrear clienteCrear)
        {
            string anterior = clienteCrear?.ClienteAnterior?.Trim();
            if (string.IsNullOrWhiteSpace(anterior))
            {
                return clienteCrear?.CopiarDelAnterior != null
                    ? "Para copiar las condiciones del cliente anterior, diga cuál es (ClienteAnterior)."
                    : null;
            }
            if (!string.Equals(clienteCrear.Origen?.Trim(), ReglaCopiaCambioFiscal.ORIGEN_CAMBIO_FISCAL, StringComparison.OrdinalIgnoreCase))
            {
                return "El cliente anterior solo se indica en un alta por cambio de forma fiscal (origen «Cambio de forma fiscal»).";
            }
            if (clienteCrear.EsContacto)
            {
                return "El cliente anterior se indica al dar de alta un cliente nuevo, no un contacto.";
            }
            _ = BloquesCambioFiscal.Normalizar(clienteCrear.CopiarDelAnterior, out List<string> desconocidos);
            if (desconocidos.Any())
            {
                return $"No existe el bloque {string.Join(", ", desconocidos)}. Los bloques son: {string.Join(", ", BloquesCambioFiscal.TODOS)}.";
            }
            string emp = string.IsNullOrWhiteSpace(clienteCrear.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : clienteCrear.Empresa.Trim();
            bool existe = await db.Clientes.AnyAsync(c => c.Empresa == emp && c.Nº_Cliente == anterior).ConfigureAwait(true);
            return existe ? null : $"No existe el cliente anterior {anterior}.";
        }

        /// <summary>
        /// NestoAPI#876: tras el alta con <see cref="ClienteCrear.ClienteAnterior"/>, el enlace en dbo.ClientesAnteriores y, con
        /// <see cref="ClienteCrear.CopiarDelAnterior"/>, la copia de sus condiciones (el mismo núcleo que Copiar). Nunca rompe el alta:
        /// lo que falle va a <see cref="Cliente.AvisoCopiaClienteAnterior"/> y a ELMAH.
        /// </summary>
        internal async Task TrasAltaCambioFiscal(Cliente cliente, ClienteCrear clienteCrear)
        {
            string anterior = clienteCrear?.ClienteAnterior?.Trim();
            if (cliente == null || string.IsNullOrWhiteSpace(anterior))
            {
                return;
            }
            string emp = cliente.Empresa?.Trim();
            string nuevo = cliente.Nº_Cliente?.Trim();
            try
            {
                if (!await RepositorioCambioFiscal.TablaExiste().ConfigureAwait(true))
                {
                    cliente.AvisoCopiaClienteAnterior = clienteCrear.CopiarDelAnterior != null
                        ? "El cliente se ha creado, pero no se han copiado las condiciones del anterior: " + ServicioCambioFiscal.MENSAJE_SIN_TABLA
                        : null;
                    return;
                }
                string usuario = UsuarioAuditoriaHelper.Resolver(User, clienteCrear.Usuario);
                await RepositorioCambioFiscal.CrearEnlace(emp, nuevo, anterior, usuario).ConfigureAwait(true);
                if (clienteCrear.CopiarDelAnterior == null)
                {
                    return;
                }
                ResultadoCambioFiscal<CopiaCambioFiscalDTO> copia = await CopiarCambioFiscal(new CopiaCambioFiscalRequest
                {
                    Empresa = emp,
                    ClienteAnterior = anterior,
                    ClienteNuevo = nuevo,
                    Bloques = clienteCrear.CopiarDelAnterior,
                    Usuario = clienteCrear.Usuario
                }, ensayo: false).ConfigureAwait(true);
                if (copia.Estado != 200)
                {
                    cliente.AvisoCopiaClienteAnterior = $"El cliente se ha creado, pero no se han copiado las condiciones del {anterior}: {copia.Motivo}";
                    return;
                }
                cliente.CopiadoDelAnterior = copia.Datos.Bloques.SelectMany(b => b.Cambios).ToList();
                List<string> avisos = copia.Datos.Bloques.SelectMany(b => b.Avisos).Concat(copia.Datos.Avisos).ToList();
                if (avisos.Any())
                {
                    cliente.AvisoCopiaClienteAnterior = string.Join(" ", avisos);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cambio de forma fiscal #876] Alta {emp}/{nuevo} (anterior {anterior}): {ex.Message}", ex));
                cliente.AvisoCopiaClienteAnterior = $"El cliente se ha creado, pero no se han podido copiar las condiciones del {anterior}: {ex.Message}";
            }
        }

        /// <summary>Administración, Dirección, Informática y el vendedor del cliente anterior o su jefe de equipo.</summary>
        private async Task<string> MotivoSinPermisoCambioFiscal(string empresa, string clienteAnterior, string vendedorAnterior)
        {
            bool esOficina = ValidadorCambioClienteComercial.EsUsuarioDeOficina(User);
            string vendedorUsuario = esOficina ? null : ValidadorCambioClienteComercial.VendedorDelUsuario(User, new ServicioUsuarioVendedor());
            List<string> equipo = null;
            if (!esOficina && !string.IsNullOrWhiteSpace(vendedorUsuario) && servicioVendedores != null)
            {
                equipo = await servicioVendedores.VendedoresEquipoString(empresa, vendedorUsuario).ConfigureAwait(true);
            }
            return ReglaCopiaCambioFiscal.MotivoSinPermiso(esOficina, vendedorUsuario, equipo, clienteAnterior, vendedorAnterior);
        }

        /// <summary>Como cualquier modificación de ficha: cada contacto del cliente a Odoo.</summary>
        private async Task PublicarClienteTrasCopia(string empresa, string cliente)
        {
            List<Cliente> fichas = await db.Clientes.Include(c => c.PersonasContactoClientes)
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente).ToListAsync().ConfigureAwait(true);
            foreach (Cliente ficha in fichas)
            {
                await _gestorClientes.PublicarClienteSincronizar(ficha).ConfigureAwait(true);
            }
        }

        /// <summary>Los pedidos con alguna línea pendiente (-1..1, con cantidad, sin albarán) del cliente anterior.</summary>
        private async Task<List<PedidoPendienteCambioFiscalDTO>> LeerPedidosPendientesCambioFiscal(string empresa, string cliente)
        {
            short pendiente = Constantes.EstadosLineaVenta.PENDIENTE;
            short enCurso = Constantes.EstadosLineaVenta.EN_CURSO;
            List<int> numeros = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Nº_Cliente == cliente && l.Estado >= pendiente && l.Estado <= enCurso && l.Cantidad != 0 && l.Nº_Albarán == null)
                .Select(l => l.Número).Distinct().ToListAsync().ConfigureAwait(true);
            if (!numeros.Any())
            {
                return new List<PedidoPendienteCambioFiscalDTO>();
            }
            List<CabPedidoVta> cabeceras = await db.CabPedidoVtas.Include(c => c.LinPedidoVtas)
                .Where(c => c.Empresa == empresa && numeros.Contains(c.Número)).ToListAsync().ConfigureAwait(true);
            var envios = await db.EnviosAgencias.Where(e => e.Empresa == empresa && numeros.Contains(e.Pedido ?? 0))
                .Select(e => new { e.Pedido, e.Numero }).ToListAsync().ConfigureAwait(true);
            var efectos = await db.EfectosPedidosVentas.Where(e => e.Empresa == empresa && numeros.Contains(e.Pedido))
                .GroupBy(e => e.Pedido).Select(g => new { Pedido = g.Key, Cuantos = g.Count() }).ToListAsync().ConfigureAwait(true);
            List<string> documentos = numeros.Select(n => n.ToString()).ToList();
            var tarjetas = await db.PagosTPV.Where(p => p.Empresa == empresa && documentos.Contains(p.Documento)
                    && (p.Estado == Constantes.EstadosPagoTPV.PENDIENTE || p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO))
                .Select(p => p.Documento).ToListAsync().ConfigureAwait(true);
            return cabeceras.OrderBy(c => c.Número).Select(c => ReglaCopiaCambioFiscal.PedidoPendiente(c, c.LinPedidoVtas.ToList(),
                    efectos.Where(e => e.Pedido == c.Número).Sum(e => e.Cuantos),
                    envios.Where(e => e.Pedido == c.Número).Select(e => e.Numero).ToList(),
                    tarjetas.Count(d => d?.Trim() == c.Número.ToString())))
                .ToList();
        }

        /// <summary>El CambiarCliente de PedidosVenta, con un controller (y un contexto) nuevo para cada pedido.</summary>
        private async Task<ResultadoCambioClientePedido> CambiarClienteDePedidoPorPedidosVenta(string empresa, int numero, CambiarClientePedidoRequest peticion)
        {
            using (PedidosVentaController controller = new PedidosVentaController { RequestContext = RequestContext })
            {
                if (Request != null)
                {
                    controller.Request = Request;
                }
                if (Configuration != null)
                {
                    controller.Configuration = Configuration;
                }
                return await controller.CambiarClienteDelPedido(empresa, numero, peticion).ConfigureAwait(true);
            }
        }

        private bool EsClienteDeLaTienda() => (User?.Identity as ClaimsIdentity)?.FindFirst("cliente") != null;

        private IHttpActionResult Traducir<T>(ResultadoCambioFiscal<T> r)
        {
            switch (r.Estado)
            {
                case 200: return Ok(r.Datos);
                case 404: return Content(HttpStatusCode.NotFound, new { Message = r.Motivo });
                case 403: return Content(HttpStatusCode.Forbidden, new { Message = r.Motivo });
                case 409: return Content(HttpStatusCode.Conflict, new { Message = r.Motivo, r.Codigo });
                case 503: return Content(HttpStatusCode.ServiceUnavailable, new { Message = r.Motivo });
                default: return BadRequest(r.Motivo);
            }
        }
    }
}
