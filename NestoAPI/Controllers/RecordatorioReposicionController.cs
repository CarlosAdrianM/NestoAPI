using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#532 (fase 1, cálculo en seco): recordatorio de reposición calculado con las ventas de
    /// Nesto de todos los canales. Nada de lo que hay aquí escribe a clientes.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/CorreosPostCompra/Reposicion")]
    public class RecordatorioReposicionController : ApiController
    {
        private readonly Func<ISelectorRecordatoriosReposicion> crearSelector;
        private readonly ILectorParametrosUsuario lector;
        private readonly IServicioCorreoElectronico servicioCorreo;

        public RecordatorioReposicionController()
            : this(null, new LectorParametrosUsuario(), new ServicioCorreoElectronico())
        {
        }

        public RecordatorioReposicionController(Func<ISelectorRecordatoriosReposicion> crearSelector,
            ILectorParametrosUsuario lector, IServicioCorreoElectronico servicioCorreo)
        {
            this.crearSelector = crearSelector;
            this.lector = lector;
            this.servicioCorreo = servicioCorreo;
        }

        /// <summary>
        /// Lista, sin enviar nada, a qué clientes se les recordaría reponer qué productos (y quién se queda
        /// fuera y por qué). Funciona con el interruptor apagado. <paramref name="fecha"/> (yyyy-MM-dd) sirve
        /// para ver qué habría salido otro día; por defecto, hoy.
        /// </summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ResultadoRecordatorioReposicionDTO))]
        public async Task<IHttpActionResult> GetReposicion(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string fecha = null)
        {
            if (!LeerFecha(fecha, out DateTime hoy))
            {
                return BadRequest("La fecha tiene que ir como yyyy-MM-dd");
            }
            ResultadoRecordatorioReposicionDTO resultado = await Calcular(empresa, hoy).ConfigureAwait(false);
            return Ok(resultado);
        }

        /// <summary>
        /// Manda ahora el correo sombra (SOLO al equipo interno de CorreosPostCompra:EmailsTest), sin esperar
        /// al jueves y aunque el interruptor esté apagado. No escribe a ningún cliente.
        /// </summary>
        [HttpPost]
        [Route("EnviarSombra")]
        public async Task<IHttpActionResult> PostEnviarSombra(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string fecha = null)
        {
            if (!LeerFecha(fecha, out DateTime hoy))
            {
                return BadRequest("La fecha tiene que ir como yyyy-MM-dd");
            }
            ResultadoRecordatorioReposicionDTO resultado = await Calcular(empresa, hoy).ConfigureAwait(false);
            string destinatarios = RecordatorioReposicionJobsService.DestinatariosSombra();
            bool enviado = await RecordatorioReposicionJobsService.EnviarSombra(resultado, servicioCorreo,
                RecordatorioReposicionJobsService.RellenarEnlacesTienda, destinatarios).ConfigureAwait(false);
            return Ok(new
            {
                Enviado = enviado,
                Destinatarios = destinatarios,
                resultado.Correos,
                resultado.Productos,
                GrupoControl = resultado.GrupoControl.Count,
                Descartes = resultado.Descartes.Count
            });
        }

        private async Task<ResultadoRecordatorioReposicionDTO> Calcular(string empresa, DateTime hoy)
        {
            string consumibles = RecordatorioReposicionJobsService.LeerConsumibles(lector);
            if (crearSelector != null)
            {
                return await crearSelector().Calcular(empresa, hoy, consumibles).ConfigureAwait(false);
            }
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return await new SelectorRecordatoriosReposicion(db).Calcular(empresa, hoy, consumibles).ConfigureAwait(false);
            }
        }

        internal static bool LeerFecha(string fecha, out DateTime hoy)
        {
            if (string.IsNullOrWhiteSpace(fecha))
            {
                hoy = DateTime.Today;
                return true;
            }
            return DateTime.TryParseExact(fecha.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out hoy);
        }
    }
}
