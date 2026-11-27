using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Carlos 27/11/26 (NestoAPI#876): enlazar dos fichas de cliente equivalentes (la misma persona dada de alta dos veces, o el mismo
    /// cliente con otra forma fiscal) sin script de sa, verlas juntas y deshacer el enlace. LA REGLA: <see cref="ReglaEnlaceEquivalentes"/>.
    /// </summary>
    public partial class ClientesController
    {
        /// <summary>Sustituible en los tests.</summary>
        internal IRepositorioEquivalenciasClientes RepositorioEquivalenciasClientes
        {
            get => repositorioEquivalenciasClientes ?? (repositorioEquivalenciasClientes = new RepositorioEquivalenciasClientesSql(db.Database.Connection.ConnectionString));
            set => repositorioEquivalenciasClientes = value;
        }
        private IRepositorioEquivalenciasClientes repositorioEquivalenciasClientes;

        /// <summary>
        /// Enlaza <c>Cliente</c> (la que queda) con <c>ClienteAnterior</c> (la otra) por «Duplicado» o «CambioFiscal». Si ya eran
        /// equivalentes, 200 sin escribir nada; si el cliente ya tenía anterior, encadena (A → B y se pide A → C: B → C). Permisos de
        /// #876: Administración, Dirección, Informática y el vendedor de la otra ficha o su jefe de equipo.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/Equivalentes")]
        [ResponseType(typeof(ResultadoEnlaceEquivalenteDTO))]
        public async Task<IHttpActionResult> PostClientesEquivalentes([FromBody] PeticionEnlaceEquivalenteDTO peticion)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string emp = EmpresaODefecto(peticion?.Empresa);
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return TraducirEquivalencias(await new ServicioEquivalenciasClientes(RepositorioEquivalenciasClientes)
                .Enlazar(peticion, usuario, v => MotivoSinPermisoEquivalentes(emp, peticion?.ClienteAnterior, v)).ConfigureAwait(true));
        }

        /// <summary>El grupo entero de fichas equivalentes del cliente (en los dos sentidos), con nombre, estado, NIF y el enlace de cada una.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/Equivalentes")]
        [ResponseType(typeof(ClientesEquivalentesDTO))]
        public async Task<IHttpActionResult> GetClientesEquivalentes(string empresa, string cliente)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            return TraducirEquivalencias(await new ServicioEquivalenciasClientes(RepositorioEquivalenciasClientes)
                .Leer(empresa, cliente).ConfigureAwait(true));
        }

        /// <summary>Deshace el enlace <paramref name="cliente"/> → <paramref name="clienteAnterior"/> (solo ese), con rastro. Mismos permisos que el POST.</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/Equivalentes/{clienteAnterior}")]
        [ResponseType(typeof(ResultadoEnlaceEquivalenteDTO))]
        public async Task<IHttpActionResult> DeleteClientesEquivalentes(string empresa, string cliente, string clienteAnterior)
        {
            if (EsClienteDeLaTienda())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string emp = EmpresaODefecto(empresa);
            string usuario = UsuarioAuditoriaHelper.Resolver(User, null);
            return TraducirEquivalencias(await new ServicioEquivalenciasClientes(RepositorioEquivalenciasClientes)
                .Deshacer(emp, cliente, clienteAnterior, usuario, v => MotivoSinPermisoEquivalentes(emp, clienteAnterior, v)).ConfigureAwait(true));
        }

        /// <summary>Los grupos de #876 (<see cref="MotivoSinPermisoCambioFiscal"/>), con su propio mensaje.</summary>
        private async Task<string> MotivoSinPermisoEquivalentes(string empresa, string clienteAnterior, string vendedorAnterior)
        {
            string motivo = await MotivoSinPermisoCambioFiscal(empresa, clienteAnterior, vendedorAnterior).ConfigureAwait(true);
            return motivo == null ? null
                : $"Solo Administración, Dirección, Informática o el vendedor del cliente {clienteAnterior?.Trim()} (o su jefe de equipo) pueden enlazar " +
                  "sus fichas equivalentes o deshacer el enlace.";
        }

        private IHttpActionResult TraducirEquivalencias<T>(ResultadoEquivalencias<T> r)
        {
            switch (r.Estado)
            {
                case 200: return Ok(r.Datos);
                case 403: return Content(HttpStatusCode.Forbidden, new { Message = r.Motivo });
                case 404: return Content(HttpStatusCode.NotFound, new { Message = r.Motivo });
                case 409: return Content(HttpStatusCode.Conflict, new { Message = r.Motivo, r.Codigo });
                case 503: return Content(HttpStatusCode.ServiceUnavailable, new { Message = r.Motivo });
                default: return Content(HttpStatusCode.BadRequest, new { Message = r.Motivo });
            }
        }
    }
}
