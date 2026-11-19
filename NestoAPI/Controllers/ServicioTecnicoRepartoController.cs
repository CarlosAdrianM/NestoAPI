using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#881 (corte 2): el «servicio técnico» de las notas de entrega y de recogida. Los tipos (combo que empieza vacío y se amplía
    /// escribiendo uno nuevo) y el de cada nota. Lo usan los que crean notas (<see cref="PermisosNotasEntrega"/>: Almacén, Administración,
    /// Dirección e Informática).
    /// </summary>
    [Authorize]
    public class ServicioTecnicoRepartoController : ApiController
    {
        private readonly NVEntities db;
        private readonly Func<ServicioServicioTecnicoReparto> crearServicio;

        public ServicioTecnicoRepartoController() : this(new NVEntities())
        {
        }

        internal ServicioTecnicoRepartoController(NVEntities db, Func<ServicioServicioTecnicoReparto> crearServicio = null)
        {
            this.db = db;
            this.crearServicio = crearServicio ?? (() => ServicioServicioTecnicoReparto.DeLaBaseDeDatos(db));
        }

        // GET api/RepartoPropio/TiposServicioTecnico?todos=false
        /// <summary>Los tipos (activos, por nombre; con <paramref name="todos"/>, también los inactivos). Sin el script, la lista vacía.</summary>
        [HttpGet]
        [Route("api/RepartoPropio/TiposServicioTecnico")]
        [ResponseType(typeof(List<TipoServicioTecnicoDTO>))]
        public async Task<IHttpActionResult> GetTipos(bool todos = false)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await crearServicio().Tipos(todos));
        }

        // POST api/RepartoPropio/TiposServicioTecnico { Nombre, MinutosEstimados? }
        /// <summary>Crea un tipo (el combo de Nesto, al escribir uno nuevo). Si el nombre ya existe, devuelve el que hay. 400 nombre vacío o largo; 503 sin el script.</summary>
        [HttpPost]
        [Route("api/RepartoPropio/TiposServicioTecnico")]
        [ResponseType(typeof(TipoServicioTecnicoDTO))]
        public async Task<IHttpActionResult> PostTipo([FromBody] CrearTipoServicioTecnicoDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await crearServicio().CrearTipo(peticion, UsuarioAuditoriaHelper.Resolver(User, "NestoAPI")));
        }

        // GET api/NotasEntrega/1/930123/ServicioTecnico
        /// <summary>El servicio técnico de la nota (todo a null si no lleva o sin el script).</summary>
        [HttpGet]
        [Route("api/NotasEntrega/{empresa}/{pedido:int}/ServicioTecnico")]
        [ResponseType(typeof(ServicioTecnicoNotaDTO))]
        public async Task<IHttpActionResult> GetServicioTecnicoNota(string empresa, int pedido)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            return Responder(await crearServicio().Leer(empresa, pedido));
        }

        // PUT api/NotasEntrega/1/930123/ServicioTecnico { TipoServicioTecnicoId? | TipoServicioTecnicoNombre?, MinutosServicio? }
        /// <summary>
        /// Lo pone, lo cambia o (sin tipo) lo quita. Por nombre, si el tipo no existe se crea. 400 si no es una nota de entrega o de recogida,
        /// el tipo no existe o los minutos no valen; 404 sin el pedido; 409 si la nota ya está entregada; 503 sin el script.
        /// </summary>
        [HttpPut]
        [Route("api/NotasEntrega/{empresa}/{pedido:int}/ServicioTecnico")]
        [ResponseType(typeof(ServicioTecnicoNotaDTO))]
        public async Task<IHttpActionResult> PutServicioTecnicoNota(string empresa, int pedido, [FromBody] PeticionServicioTecnicoNotaDTO peticion)
        {
            if (!PermisosNotasEntrega.Puede(User))
            {
                return Prohibido();
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            ResultadoServicioTecnico<ServicioTecnicoNotaDTO> resultado = await crearServicio().Poner(empresa, pedido, peticion, usuario);
            if (resultado.Bien)
            {
                // NestoAPI#881: cambian los minutos de la parada: las PDA del reparto recalculan el fin de ruta. Nunca rompe la respuesta.
                try
                {
                    AvisarReparto?.Invoke(usuario);
                }
                catch
                {
                    // el aviso es un extra
                }
            }
            return Responder(resultado);
        }

        /// <summary>NestoAPI#881: «repartoCambiado» (Motivo «Parada») por el punto único agrupado. Sustituible en los tests. Nunca lanza.</summary>
        internal Action<string> AvisarReparto { get; set; } = AvisoParadasReparto.Avisar;

        private IHttpActionResult Responder<T>(ResultadoServicioTecnico<T> resultado)
            => resultado.Bien ? Ok(resultado.Contenido) : (IHttpActionResult)Content(resultado.Estado, new { Message = resultado.Mensaje });

        private IHttpActionResult Prohibido()
            => Content(HttpStatusCode.Forbidden, new { Message = "Su usuario no tiene permiso para las notas de entrega." });

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
