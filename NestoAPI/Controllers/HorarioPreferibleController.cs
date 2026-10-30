using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#763 (Nesto#584): el «horario preferible» de entrega de cada cliente/contacto. Se intenta respetar,
    /// pero sin compromiso (<see cref="HorarioPreferibleDTO.AVISO"/>). Más las
    /// propuestas sacadas de los comentarios de la ficha, que no cuentan hasta que una persona las acepta.
    /// </summary>
    [Authorize]
    public class HorarioPreferibleController : ApiController
    {
        private readonly ServicioHorarioPreferible servicio;

        public HorarioPreferibleController() : this(new NVEntities())
        {
        }

        internal HorarioPreferibleController(NVEntities db) : this(new ServicioHorarioPreferible(new RepositorioHorarioPreferible(db)))
        {
        }

        internal HorarioPreferibleController(ServicioHorarioPreferible servicio)
        {
            this.servicio = servicio;
        }

        private string Usuario => UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");

        /// <summary>Los clientes de la tienda online (token de api/auth/token, claim «cliente») no ven ni cambian horarios: 403.</summary>
        private IHttpActionResult SoloEmpleados()
            => Infraestructure.Notificaciones.EscanerRemotoPermisos.EsCliente(User)
                ? Content(HttpStatusCode.Forbidden, new { Message = "El horario preferible solo lo gestionan los empleados." })
                : null;

        // GET api/Clientes/1/12345/0/HorarioPreferible
        /// <summary>El horario del contacto (franjas vacías si no tiene) y, si la hay, su propuesta por revisar.</summary>
        [HttpGet]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/HorarioPreferible")]
        [ResponseType(typeof(HorarioPreferibleDTO))]
        public async Task<IHttpActionResult> GetHorarioPreferible(string empresa, string cliente, string contacto)
        {
            IHttpActionResult prohibido = SoloEmpleados();
            if (prohibido != null)
            {
                return prohibido;
            }
            return Responder(await servicio.Leer(empresa, cliente, contacto).ConfigureAwait(false));
        }

        // PUT api/Clientes/1/12345/0/HorarioPreferible   { "Franjas": [ { "DiaSemana": 1, "Desde": "10:00", "Hasta": "14:00" } ] }
        /// <summary>Sustituye el horario del contacto (sin franjas, lo quita). También vale { "Texto": "L-V 10:00-14:00, 17:00-20:00" }.</summary>
        [HttpPut]
        [Route("api/Clientes/{empresa}/{cliente}/{contacto}/HorarioPreferible")]
        [ResponseType(typeof(HorarioPreferibleDTO))]
        public async Task<IHttpActionResult> PutHorarioPreferible(string empresa, string cliente, string contacto, [FromBody] HorarioPreferibleDTO horario)
        {
            IHttpActionResult prohibido = SoloEmpleados();
            if (prohibido != null)
            {
                return prohibido;
            }
            return Responder(await servicio.Guardar(empresa, cliente, contacto, horario, Usuario).ConfigureAwait(false));
        }

        // GET api/Clientes/HorariosPreferibles/Propuestas
        /// <summary>Las propuestas por revisar, con el texto de los comentarios y lo que proponen las reglas.</summary>
        [HttpGet]
        [Route("api/Clientes/HorariosPreferibles/Propuestas")]
        [ResponseType(typeof(List<PropuestaHorarioPreferibleDTO>))]
        public async Task<IHttpActionResult> GetPropuestas()
        {
            IHttpActionResult prohibido = SoloEmpleados();
            if (prohibido != null)
            {
                return prohibido;
            }
            return Ok(await servicio.Propuestas().ConfigureAwait(false));
        }

        // POST api/Clientes/HorariosPreferibles/Propuestas/7/Aceptar   { "Texto": "L-V 10:00-14:00" } (o nada: lo propuesto)
        [HttpPost]
        [Route("api/Clientes/HorariosPreferibles/Propuestas/{id:int}/Aceptar")]
        [ResponseType(typeof(HorarioPreferibleDTO))]
        public async Task<IHttpActionResult> PostAceptarPropuesta(int id, [FromBody] HorarioPreferibleDTO horario)
        {
            IHttpActionResult prohibido = SoloEmpleados();
            if (prohibido != null)
            {
                return prohibido;
            }
            return Responder(await servicio.Aceptar(id, horario, Usuario).ConfigureAwait(false));
        }

        // POST api/Clientes/HorariosPreferibles/Propuestas/7/Descartar
        [HttpPost]
        [Route("api/Clientes/HorariosPreferibles/Propuestas/{id:int}/Descartar")]
        public async Task<IHttpActionResult> PostDescartarPropuesta(int id)
        {
            IHttpActionResult prohibido = SoloEmpleados();
            if (prohibido != null)
            {
                return prohibido;
            }
            ResultadoHorarioPreferible<bool> resultado = await servicio.Descartar(id, Usuario).ConfigureAwait(false);
            return resultado.Ok ? (IHttpActionResult)StatusCode(HttpStatusCode.NoContent) : Content(resultado.Error.Value, new { Message = resultado.Mensaje });
        }

        private IHttpActionResult Responder<T>(ResultadoHorarioPreferible<T> resultado)
            => resultado.Ok ? Ok(resultado.Contenido) : (IHttpActionResult)Content(resultado.Error.Value, new { Message = resultado.Mensaje });
    }
}
