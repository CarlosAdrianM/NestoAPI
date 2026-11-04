using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Festivos;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
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
    /// <summary>NestoAPI#779: un festivo del calendario.</summary>
    public class FestivoDTO
    {
        public DateTime Fecha { get; set; }
        /// <summary>Nacional, Autonómica, Empresa, Algete, Alcobendas o Madrid.</summary>
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
        /// <summary>«Tabla» (dbo.Festivos) o «Código» (la lista de GestorFestivos, mientras la tabla no tenga ese año).</summary>
        public string Origen { get; set; }
    }

    /// <summary>NestoAPI#779: cuerpo del POST (alta, o cambio de la descripción si ya existe Fecha + Tipo).</summary>
    public class PeticionFestivoDTO
    {
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; }
        public string Descripcion { get; set; }
    }

    /// <summary>
    /// NestoAPI#779: mantenimiento del calendario de festivos (tabla <c>dbo.Festivos</c>, <c>Scripts/Issue779_Festivos.sql</c>).
    /// Leer: cualquiera autenticado. Escribir: Administración, Dirección o Informática; cada escritura invalida la caché de
    /// <see cref="CalendarioFestivos"/>.
    /// </summary>
    public class FestivosController : ApiController
    {
        public const string ORIGEN_TABLA = "Tabla";
        public const string ORIGEN_CODIGO = "Código";
        public const int LONGITUD_DESCRIPCION = 100;
        public const string MENSAJE_SIN_PERMISO = "Solo Administración, Dirección o Informática pueden cambiar los festivos.";
        public const string MENSAJE_SIN_TABLA = "Todavía no existe la tabla Festivos: hay que lanzar Scripts/Issue779_Festivos.sql.";

        private readonly IRepositorioFestivos repositorio;

        public FestivosController() : this(new RepositorioFestivos())
        {
        }

        internal FestivosController(IRepositorioFestivos repositorio)
        {
            this.repositorio = repositorio;
        }

        internal static bool PuedeCambiarFestivos(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        private static bool AnioValido(int anio) => anio >= 2000 && anio <= 2100;

        // GET api/Festivos?anio=2027
        /// <summary>
        /// Los festivos del año (por defecto, el actual), por fecha. Si la tabla no existe o no tiene ese año, los de la lista del
        /// código (Origen «Código»), que son los que está usando la API.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Festivos")]
        [ResponseType(typeof(List<FestivoDTO>))]
        public async Task<IHttpActionResult> GetFestivos(int? anio = null)
        {
            int elAnio = anio ?? DateTime.Today.Year;
            if (!AnioValido(elAnio))
            {
                return BadRequest("Año fuera de rango.");
            }
            LecturaFestivosAnio lectura = await repositorio.Leer(elAnio).ConfigureAwait(false);
            if (lectura.HayTabla && lectura.Filas.Any())
            {
                return Ok(lectura.Filas
                    .OrderBy(f => f.Fecha).ThenBy(f => f.Tipo)
                    .Select(f => new FestivoDTO
                    {
                        Fecha = f.Fecha.Date,
                        Tipo = TiposFestivo.TryParse(f.Tipo, out TipoFestivo tipo) ? TiposFestivo.Texto(tipo) : f.Tipo,
                        Descripcion = f.Descripcion,
                        Usuario = f.Usuario,
                        FechaModificacion = f.FechaModificacion,
                        Origen = ORIGEN_TABLA
                    }).ToList());
            }
            return Ok(GestorFestivos.FestivosDelCodigo(elAnio)
                .OrderBy(f => f.Fecha).ThenBy(f => TiposFestivo.Texto(f.TipoFestivo))
                .Select(f => new FestivoDTO
                {
                    Fecha = f.Fecha.Date,
                    Tipo = TiposFestivo.Texto(f.TipoFestivo),
                    Descripcion = f.Fiesta,
                    Origen = ORIGEN_CODIGO
                }).ToList());
        }

        // POST api/Festivos   { "Fecha": "2027-03-19", "Tipo": "Autonómica", "Descripcion": "San José" }
        /// <summary>Alta de un festivo (o cambio de su descripción si ya está esa fecha con ese tipo).</summary>
        [HttpPost]
        [Authorize]
        [Route("api/Festivos")]
        [ResponseType(typeof(FestivoDTO))]
        public async Task<IHttpActionResult> PostFestivo([FromBody] PeticionFestivoDTO peticion)
        {
            if (!PuedeCambiarFestivos(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (peticion == null || peticion.Fecha == default(DateTime))
            {
                return BadRequest("Falta la fecha del festivo.");
            }
            if (!AnioValido(peticion.Fecha.Year))
            {
                return BadRequest("Año fuera de rango.");
            }
            if (!TiposFestivo.TryParse(peticion.Tipo, out TipoFestivo tipo))
            {
                return BadRequest($"Tipo de festivo desconocido. Tiene que ser uno de estos: {string.Join(", ", TiposFestivo.Todos)}.");
            }
            string descripcion = peticion.Descripcion?.Trim();
            if (string.IsNullOrEmpty(descripcion))
            {
                return BadRequest("Falta la descripción del festivo.");
            }
            if (descripcion.Length > LONGITUD_DESCRIPCION)
            {
                descripcion = descripcion.Substring(0, LONGITUD_DESCRIPCION);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            var fila = new FilaTablaFestivo
            {
                Fecha = peticion.Fecha.Date,
                Tipo = TiposFestivo.Texto(tipo),
                Descripcion = descripcion
            };
            ResultadoEscrituraFestivo resultado = await repositorio.Guardar(fila, usuario).ConfigureAwait(false);
            if (resultado == ResultadoEscrituraFestivo.SinTabla)
            {
                return Content(HttpStatusCode.Conflict, MENSAJE_SIN_TABLA);
            }
            CalendarioFestivos.Invalidar();
            return Ok(new FestivoDTO
            {
                Fecha = fila.Fecha,
                Tipo = fila.Tipo,
                Descripcion = fila.Descripcion,
                Usuario = usuario,
                FechaModificacion = DateTime.Now,
                Origen = ORIGEN_TABLA
            });
        }

        // DELETE api/Festivos/2027-03-19/Autonómica
        /// <summary>Baja de un festivo (esa fecha con ese tipo).</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Festivos/{fecha:datetime}/{tipo}")]
        public async Task<IHttpActionResult> DeleteFestivo(DateTime fecha, string tipo)
        {
            if (!PuedeCambiarFestivos(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!TiposFestivo.TryParse(tipo, out TipoFestivo tipoFestivo))
            {
                return BadRequest($"Tipo de festivo desconocido. Tiene que ser uno de estos: {string.Join(", ", TiposFestivo.Todos)}.");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoEscrituraFestivo resultado = await repositorio.Borrar(fecha.Date, TiposFestivo.Texto(tipoFestivo), usuario).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoEscrituraFestivo.SinTabla:
                    return Content(HttpStatusCode.Conflict, MENSAJE_SIN_TABLA);
                case ResultadoEscrituraFestivo.NoExiste:
                    return NotFound();
                default:
                    CalendarioFestivos.Invalidar();
                    return Ok();
            }
        }
    }
}
