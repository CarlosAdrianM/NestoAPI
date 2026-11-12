using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#857: cuerpo del POST de un remitente marcado.</summary>
    public class PeticionRemitenteAgenciaDTO
    {
        /// <summary>Nombre de la agencia («GLS») o texto libre («Transportista de tal proveedor»).</summary>
        public string Agencia { get; set; }
        /// <summary>Dirección («avisos@ejemplo-agencia.es») o dominio («ejemplo-agencia.es»).</summary>
        public string Remitente { get; set; }
    }

    /// <summary>NestoAPI#857: un correo captado, con los candidatos ya leídos del JSON.</summary>
    public class CorreoSeguimientoCompraDTO
    {
        public int Id { get; set; }
        public DateTime Recibido { get; set; }
        public string RemitenteDireccion { get; set; }
        public string RemitenteDominio { get; set; }
        public string Asunto { get; set; }
        public string Agencia { get; set; }
        public string Codigo { get; set; }
        public string Url { get; set; }
        public List<SeguimientoCorreo> Seguimientos { get; set; } = new List<SeguimientoCorreo>();
        public string Extracto { get; set; }
        public string ProveedorPropuesto { get; set; }
        public List<CandidatoPedidoCompra> PedidosCandidatos { get; set; } = new List<CandidatoPedidoCompra>();
        public DateTime FechaCaptura { get; set; }
        public string EmpresaReal { get; set; }
        public int? PedidoReal { get; set; }
        public DateTime? FechaAlbaranReal { get; set; }
    }

    /// <summary>
    /// NestoAPI#857 (fase 1, en sombra): lo captado de los correos de seguimiento del buzón de Compras y los remitentes marcados como
    /// de una agencia (tablas de <c>Scripts/Issue857_ComprasCorreosSeguimiento.sql</c>). Todo: Compras, Dirección o Informática.
    /// </summary>
    public class ComprasAgenciasEntradaController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Compras, Dirección o Informática pueden ver y marcar los correos de seguimiento de compras.";
        public const string MENSAJE_SIN_TABLA = "Todavía no existen las tablas: hay que lanzar Scripts/Issue857_ComprasCorreosSeguimiento.sql.";
        public const int DIAS_MAXIMOS = 365;
        public const int LONGITUD_AGENCIA = 50;

        private static readonly Regex Direccion = new Regex(@"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$", RegexOptions.Compiled);
        private static readonly Regex DominioValido = new Regex(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9\-]*[a-z0-9])?)+$", RegexOptions.Compiled);

        private readonly IRepositorioCorreosSeguimiento repositorio;

        public ComprasAgenciasEntradaController() : this(new RepositorioCorreosSeguimiento())
        {
        }

        internal ComprasAgenciasEntradaController(IRepositorioCorreosSeguimiento repositorio)
        {
            this.repositorio = repositorio;
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>«@Ejemplo.ES » → «ejemplo.es»; null si no es ni una dirección ni un dominio.</summary>
        internal static string NormalizarRemitente(string remitente)
        {
            string limpio = remitente?.Trim().TrimStart('@').TrimEnd('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > 255)
            {
                return null;
            }
            return Direccion.IsMatch(limpio) || DominioValido.IsMatch(limpio) ? limpio : null;
        }

        // GET api/Compras/AgenciasEntrada/Remitentes
        /// <summary>Los remitentes marcados (también los dados de baja, con Activo = false).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Remitentes")]
        [ResponseType(typeof(List<RemitenteMarcado>))]
        public async Task<IHttpActionResult> GetRemitentes()
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten())
            {
                return Ok(new List<RemitenteMarcado>());
            }
            return Ok(await repositorio.LeerRemitentes().ConfigureAwait(false));
        }

        // POST api/Compras/AgenciasEntrada/Remitentes   { "Agencia": "GLS", "Remitente": "ejemplo-agencia.es" }
        /// <summary>Marca una dirección o un dominio como de una agencia (si ya estaba, cambia la agencia y lo reactiva).</summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Remitentes")]
        [ResponseType(typeof(RemitenteMarcado))]
        public async Task<IHttpActionResult> PostRemitente([FromBody] PeticionRemitenteAgenciaDTO peticion)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string agencia = peticion?.Agencia?.Trim();
            if (string.IsNullOrEmpty(agencia))
            {
                return BadRequest("Falta la agencia.");
            }
            // Si es una conocida, con su nombre de siempre (así casa con HorasHastaEntrega)
            agencia = AgenciasEntradaRegistradas.PorNombre(agencia)?.Nombre ?? agencia;
            if (agencia.Length > LONGITUD_AGENCIA)
            {
                agencia = agencia.Substring(0, LONGITUD_AGENCIA);
            }
            string remitente = NormalizarRemitente(peticion.Remitente);
            if (remitente == null)
            {
                return BadRequest("El remitente tiene que ser una dirección de correo o un dominio.");
            }
            if (AsignadorPedidosCompra.EsPropio(TextoCorreo.Dominio(remitente)))
            {
                return BadRequest("No se puede marcar un remitente de Nueva Visión.");
            }
            if (!remitente.Contains("@") && AsignadorPedidosCompra.EsGratuito(remitente))
            {
                return BadRequest("No se puede marcar un dominio de correo gratuito entero: marque la dirección.");
            }
            if (!repositorio.TablasExisten())
            {
                return Content(HttpStatusCode.Conflict, MENSAJE_SIN_TABLA);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            int id = await repositorio.GuardarRemitente(agencia, remitente, usuario).ConfigureAwait(false);
            return Ok(new RemitenteMarcado
            {
                Id = id,
                Agencia = agencia,
                Remitente = remitente,
                Activo = true,
                Usuario = usuario,
                FechaModificacion = DateTime.Now
            });
        }

        // DELETE api/Compras/AgenciasEntrada/Remitentes/12
        /// <summary>Baja lógica de un remitente marcado (queda con Activo = false, con quién y cuándo).</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Remitentes/{id:int}")]
        public async Task<IHttpActionResult> DeleteRemitente(int id)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten())
            {
                return Content(HttpStatusCode.Conflict, MENSAJE_SIN_TABLA);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            return await repositorio.DesactivarRemitente(id, usuario).ConfigureAwait(false) ? (IHttpActionResult)Ok() : NotFound();
        }

        // GET api/Compras/AgenciasEntrada/Candidatos?dias=30
        /// <summary>Remitentes de lo captado en esos días que aún no están marcados: nº de correos, último asunto, dominios de los enlaces.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Candidatos")]
        [ResponseType(typeof(List<CandidatoRemitenteDTO>))]
        public async Task<IHttpActionResult> GetCandidatos(int dias = 30)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (dias < 1 || dias > DIAS_MAXIMOS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {DIAS_MAXIMOS}.");
            }
            if (!repositorio.TablasExisten())
            {
                return Ok(new List<CandidatoRemitenteDTO>());
            }
            List<CorreoSeguimientoFila> correos = await repositorio.LeerCorreos(DateTime.Today.AddDays(-dias)).ConfigureAwait(false);
            List<RemitenteMarcado> marcados = await repositorio.LeerRemitentes().ConfigureAwait(false);
            return Ok(CandidatosRemitentes.Calcular(correos, marcados));
        }

        // GET api/Compras/AgenciasEntrada/Correos?dias=7
        /// <summary>Lo captado en esos días (lo más reciente primero).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Correos")]
        [ResponseType(typeof(List<CorreoSeguimientoCompraDTO>))]
        public async Task<IHttpActionResult> GetCorreos(int dias = 7)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (dias < 1 || dias > DIAS_MAXIMOS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {DIAS_MAXIMOS}.");
            }
            if (!repositorio.TablasExisten())
            {
                return Ok(new List<CorreoSeguimientoCompraDTO>());
            }
            List<CorreoSeguimientoFila> correos = await repositorio.LeerCorreos(DateTime.Today.AddDays(-dias)).ConfigureAwait(false);
            return Ok((correos ?? new List<CorreoSeguimientoFila>()).Select(Dto).ToList());
        }

        internal static CorreoSeguimientoCompraDTO Dto(CorreoSeguimientoFila fila)
        {
            var dto = new CorreoSeguimientoCompraDTO
            {
                Id = fila.Id,
                Recibido = fila.Recibido,
                RemitenteDireccion = fila.RemitenteDireccion,
                RemitenteDominio = fila.RemitenteDominio,
                Asunto = fila.Asunto,
                Agencia = fila.Agencia,
                Codigo = fila.Codigo,
                Url = fila.Url,
                Extracto = fila.Extracto,
                ProveedorPropuesto = fila.ProveedorPropuesto,
                PedidosCandidatos = AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos),
                FechaCaptura = fila.FechaCaptura,
                EmpresaReal = fila.EmpresaReal,
                PedidoReal = fila.PedidoReal,
                FechaAlbaranReal = fila.FechaAlbaranReal
            };
            if (!string.IsNullOrWhiteSpace(fila.Seguimientos))
            {
                try
                {
                    dto.Seguimientos = JsonConvert.DeserializeObject<List<SeguimientoCorreo>>(fila.Seguimientos) ?? new List<SeguimientoCorreo>();
                }
                catch (JsonException)
                {
                    dto.Seguimientos = new List<SeguimientoCorreo>();
                }
            }
            if (!dto.Seguimientos.Any() && (fila.Codigo != null || fila.Url != null))
            {
                dto.Seguimientos.Add(new SeguimientoCorreo { Codigo = fila.Codigo, Url = fila.Url });
            }
            return dto;
        }
    }
}
