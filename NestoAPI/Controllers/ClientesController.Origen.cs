using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#623: una fila de GET api/Clientes/Origenes.</summary>
    public class OrigenClienteDTO
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Campana { get; set; }
        public bool Activo { get; set; }
        public int HorasParaLlamar { get; set; }
    }

    /// <summary>NestoAPI#623: el origen de un cliente (GET y respuesta del PUT de api/Clientes/{empresa}/{cliente}/Origen).</summary>
    public class OrigenDeClienteDTO
    {
        /// <summary>Código del catálogo (META-HARMONIA-0926); null si se ha quitado (respuesta del PUT).</summary>
        public string Origen { get; set; }
        public string Descripcion { get; set; }
        public DateTime? FechaOrigen { get; set; }
        public string UsuarioOrigen { get; set; }
    }

    /// <summary>NestoAPI#623: cuerpo del PUT. Origen null o vacío = quitarlo.</summary>
    public class PeticionOrigenClienteDTO
    {
        public string Origen { get; set; }
    }

    public partial class ClientesController
    {
        public const string MENSAJE_ORIGEN_SIN_PERMISO = "Solo Dirección, Administración, Vendedores, Tiendas o Informática pueden cambiar el origen de un cliente.";

        /// <summary>NestoAPI#623: las tablas OrigenesCliente y ClientesOrigen (sustituible en los tests).</summary>
        internal IRepositorioOrigenesCliente RepositorioOrigenes { get; set; } = new RepositorioOrigenesClienteSql();
        /// <summary>NestoAPI#623: dónde se traza un origen que no se ha podido guardar tras el alta (ELMAH; sustituible en los tests).</summary>
        internal Action<Exception> RegistrarErrorOrigen { get; set; } = ex => ElmahHelper.Log(ex);
        /// <summary>NestoAPI#623: reloj (sustituible en los tests).</summary>
        internal Func<DateTime> RelojOrigenes { get; set; } = () => DateTime.Now;

        internal static bool PuedeCambiarOrigen(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.VENDEDORES)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Clientes/Origenes?todos=false
        /// <summary>NestoAPI#623: el catálogo de orígenes de cliente (solo los activos salvo todos=true), por descripción.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/Origenes")]
        [ResponseType(typeof(List<OrigenClienteDTO>))]
        public async Task<IHttpActionResult> GetOrigenes(bool todos = false)
        {
            List<OrigenCliente> catalogo = await RepositorioOrigenes.LeerCatalogo().ConfigureAwait(false);
            return Ok(ReglasLeadsCampana.Filtrar(catalogo, todos, RelojOrigenes())
                .Select(o => new OrigenClienteDTO
                {
                    Codigo = o.Codigo?.Trim(),
                    Descripcion = o.Descripcion,
                    Campana = o.Campana,
                    Activo = o.Activo,
                    HorasParaLlamar = o.HorasParaLlamar
                })
                .ToList());
        }

        // GET api/Clientes/1/12345/Origen
        /// <summary>NestoAPI#623: el origen del cliente (200) o 204 si no tiene (ni se comprueba que el cliente exista).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/Origen")]
        [ResponseType(typeof(OrigenDeClienteDTO))]
        public async Task<IHttpActionResult> GetOrigenCliente(string empresa, string cliente)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            OrigenAsignadoCliente origen = await RepositorioOrigenes.LeerDeCliente(EmpresaODefecto(empresa), cliente.Trim()).ConfigureAwait(false);
            if (origen == null || string.IsNullOrWhiteSpace(origen.Origen))
            {
                return StatusCode(HttpStatusCode.NoContent);
            }
            return Ok(new OrigenDeClienteDTO
            {
                Origen = origen.Origen.Trim(),
                Descripcion = origen.Descripcion,
                FechaOrigen = origen.FechaOrigen,
                UsuarioOrigen = origen.UsuarioOrigen?.Trim()
            });
        }

        // PUT api/Clientes/1/12345/Origen   { "Origen": "META-HARMONIA-0926" }  (null o vacío lo quita)
        /// <summary>
        /// NestoAPI#623: pone o quita el origen del cliente (todos sus contactos). 400 si el código no existe o no está activo;
        /// 403 si no es de Dirección, Administración, Vendedores, Tiendas o Informática. Si ya tenía ese origen, no cambia nada.
        /// </summary>
        [HttpPut]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/Origen")]
        [ResponseType(typeof(OrigenDeClienteDTO))]
        public async Task<IHttpActionResult> PutOrigenCliente(string empresa, string cliente, [FromBody] PeticionOrigenClienteDTO peticion)
        {
            if (!PuedeCambiarOrigen(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_ORIGEN_SIN_PERMISO);
            }
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Falta el cliente.");
            }
            string empresaLimpia = EmpresaODefecto(empresa);
            string clienteLimpio = cliente.Trim();
            string codigo = string.IsNullOrWhiteSpace(peticion?.Origen) ? null : peticion.Origen.Trim().ToUpperInvariant();
            string error = await ValidarOrigen(codigo).ConfigureAwait(false);
            if (error != null)
            {
                return BadRequest(error);
            }
            await RepositorioOrigenes.Guardar(empresaLimpia, clienteLimpio, codigo, User?.Identity?.Name).ConfigureAwait(false);
            if (codigo == null)
            {
                return Ok(new OrigenDeClienteDTO());
            }
            OrigenAsignadoCliente guardado = await RepositorioOrigenes.LeerDeCliente(empresaLimpia, clienteLimpio).ConfigureAwait(false);
            return Ok(new OrigenDeClienteDTO
            {
                Origen = codigo,
                Descripcion = guardado?.Descripcion,
                FechaOrigen = guardado?.FechaOrigen,
                UsuarioOrigen = guardado?.UsuarioOrigen?.Trim()
            });
        }

        /// <summary>NestoAPI#623: null si el código vale (o es null: quitar); si no, el mensaje del 400.</summary>
        internal async Task<string> ValidarOrigen(string codigo)
        {
            if (codigo == null)
            {
                return null;
            }
            List<OrigenCliente> catalogo = await RepositorioOrigenes.LeerCatalogo().ConfigureAwait(false);
            OrigenCliente origen = catalogo?.FirstOrDefault(o => string.Equals(o.Codigo?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
            if (origen == null)
            {
                return $"No existe el origen de cliente «{codigo}».";
            }
            return ReglasLeadsCampana.EstaActivo(origen, RelojOrigenes())
                ? null
                : $"El origen de cliente «{codigo}» no está activo.";
        }

        /// <summary>NestoAPI#623: como <see cref="ValidarOrigen"/>, pero sin quejarse si es el origen que ya tiene el cliente.</summary>
        internal async Task<string> ValidarOrigenAlModificar(string empresa, string cliente, string origen)
        {
            if (string.IsNullOrWhiteSpace(origen))
            {
                return null;
            }
            string codigo = origen.Trim().ToUpperInvariant();
            OrigenAsignadoCliente actual = string.IsNullOrWhiteSpace(cliente) ? null : await LeerOrigenSeguro(empresa, cliente).ConfigureAwait(false);
            return string.Equals(actual?.Origen?.Trim(), codigo, StringComparison.OrdinalIgnoreCase) ? null : await ValidarOrigen(codigo).ConfigureAwait(false);
        }

        /// <summary>
        /// NestoAPI#623: el campo Origen del alta (POST) o la modificación (PUT) de ClienteCrear. Null o vacío = no se toca (al crear,
        /// sin origen; al modificar, se deja el que tenga: Nesto no siempre lo conoce). Para QUITARLO, PUT api/Clientes/{empresa}/{cliente}/Origen
        /// con Origen null. Se graba DESPUÉS de guardar el cliente (otra tabla, fuera del EDMX): si falla, el cliente queda
        /// guardado igual y el error va a ELMAH. Al modificar, solo si cambia.
        /// </summary>
        internal async Task GuardarOrigenTrasGuardarCliente(string empresa, string cliente, string origen, bool esAlta)
        {
            if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(cliente))
            {
                return;
            }
            string codigo = origen.Trim().ToUpperInvariant();
            try
            {
                string empresaLimpia = EmpresaODefecto(empresa);
                if (!esAlta)
                {
                    OrigenAsignadoCliente actual = await RepositorioOrigenes.LeerDeCliente(empresaLimpia, cliente.Trim()).ConfigureAwait(false);
                    if (string.Equals(actual?.Origen?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                await RepositorioOrigenes.Guardar(empresaLimpia, cliente.Trim(), codigo, User?.Identity?.Name).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarErrorOrigen?.Invoke(new Exception(
                    $"NestoAPI#623: el cliente {cliente?.Trim()} se ha guardado, pero no su origen {codigo ?? "(quitar)"}: {ex.Message}", ex));
            }
        }

        /// <summary>NestoAPI#623: el origen para el DTO de GET api/Clientes. Nunca lanza: null si no tiene o no se puede leer.</summary>
        internal async Task<OrigenAsignadoCliente> LeerOrigenSeguro(string empresa, string cliente)
        {
            try
            {
                return await RepositorioOrigenes.LeerDeCliente(EmpresaODefecto(empresa), cliente?.Trim()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
