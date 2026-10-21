using Hangfire;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#670 (corte 1 de la épica #669): sugerencias de productos para la plantilla de venta (Nesto y NestoApp), por
    /// reglas, y el registro de lo que se enseña, lo que contesta el vendedor y lo que entra en el pedido.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/SugerenciasProductos")]
    public class SugerenciasProductosController : ApiController
    {
        private readonly Func<IServicioSugerenciasProductos> crearServicio;
        private readonly Func<bool, string, DateTime, string> programarEntrenamiento;
        private readonly Func<DateTime> ahora;

        public SugerenciasProductosController() : this(null)
        {
        }

        public SugerenciasProductosController(Func<IServicioSugerenciasProductos> crearServicio) : this(crearServicio, null, null)
        {
        }

        /// <param name="programarEntrenamiento">(forzar, usuario, cuándo) → id del job de Hangfire (sustituible en tests).</param>
        /// <param name="ahora">Reloj (sustituible en tests).</param>
        internal SugerenciasProductosController(Func<IServicioSugerenciasProductos> crearServicio,
            Func<bool, string, DateTime, string> programarEntrenamiento, Func<DateTime> ahora)
        {
            this.crearServicio = crearServicio;
            this.ahora = ahora ?? (() => DateTime.Now);
            this.programarEntrenamiento = programarEntrenamiento ?? ProgramarEnHangfire;
        }

        /// <summary>
        /// Las sugerencias para el carrito de la plantilla (3, nunca más de 4). Los clientes la llaman cuando cambia el carrito, con
        /// un retardo. Si la sesión ya tiene sugerencias que siguen valiendo, se mantienen en su hueco. Sin las tablas del script,
        /// la lista va vacía.
        /// </summary>
        [HttpPost]
        [Route("Calcular")]
        [ResponseType(typeof(SugerenciasProductosDTO))]
        public async Task<IHttpActionResult> PostCalcular([FromBody] CalcularSugerenciasDTO peticion)
        {
            try
            {
                SugerenciasProductosDTO resultado = await Usar(s => s.Calcular(peticion, User?.Identity?.Name)).ConfigureAwait(false);
                return Ok(resultado);
            }
            catch (SugerenciasProductosException ex)
            {
                return ex.NoEncontrada ? (IHttpActionResult)NotFound() : BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Lo que contesta el vendedor: «Anadido» (ofrecida y aceptada), «NoLeInteresa» (ofrecida y rechazada) o
        /// «DescartadoVendedor» (Nesto#537: el vendedor no la ofrece porque sabe que no la quiere o no se la puede vender; se
        /// registra aparte del rechazo del cliente). Con las dos últimas no se le vuelve a sugerir a este cliente en 60 días y
        /// devuelve la sustituta (en el mismo hueco), si la hay.
        /// </summary>
        [HttpPost]
        [Route("{idSugerencia:int}/Respuesta")]
        [ResponseType(typeof(ResultadoRespuestaSugerenciaDTO))]
        public async Task<IHttpActionResult> PostRespuesta(int idSugerencia, [FromBody] RespuestaSugerenciaDTO respuesta)
        {
            try
            {
                ResultadoRespuestaSugerenciaDTO resultado = await Usar(s => s.Responder(idSugerencia, respuesta)).ConfigureAwait(false);
                return Ok(resultado);
            }
            catch (SugerenciasProductosException ex)
            {
                return ex.NoEncontrada ? Content(System.Net.HttpStatusCode.NotFound, ex.Message) : (IHttpActionResult)BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// NestoAPI#671 (corte 2d): entrena el modelo de sugerencias en el servidor (el mismo job que corre el segundo sábado de
        /// cada mes), en la cola «entrenamiento» de Hangfire. Deja zip, json, README e informe frente a reglas-1 en
        /// ModeloSugerencias:Carpeta y avisa en la campana de Nesto. Desde el corte 2c, Calcular usa el último que pasa la puerta
        /// (<see cref="PredictorSugerenciasProductos"/>). Solo Dirección e Informática.
        /// <para>Sin <paramref name="cuando"/>, en la próxima franja permitida (de lunes a viernes de 5:00 a 21:00, hoy a las
        /// 23:30; si no, ya). Con <paramref name="cuando"/>, a esa hora, que no puede caer de lunes a viernes de 7:00 a 21:00
        /// (400). Con <paramref name="forzar"/>=true se salta la guarda horaria (y sin <paramref name="cuando"/>, se lanza ya).</para>
        /// POST api/SugerenciasProductos/ReentrenarModelo?cuando=2026-10-20T23:30
        /// </summary>
        [HttpPost]
        [Route("ReentrenarModelo")]
        public IHttpActionResult PostReentrenarModelo(DateTime? cuando = null, bool forzar = false)
        {
            if (User == null || !(User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            DateTime momento = ahora();
            DateTime programada;
            if (cuando.HasValue)
            {
                programada = cuando.Value;
                if (!forzar && ReentrenamientoModeloSugerenciasJobsService.EnHorario(programada))
                {
                    return BadRequest($"No se puede programar el entrenamiento el {programada.ToString("dddd dd/MM/yyyy 'a las' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("es-ES"))}: " +
                        "lee 36 meses de LinPedidoVta y no se lanza de lunes a viernes de 7:00 a 21:00. Elija otra hora o no indique ninguna (se programa para las 23:30).");
                }
                if (programada < momento.AddMinutes(-5))
                {
                    return BadRequest($"La hora indicada ({programada:dd/MM/yyyy HH:mm}) ya ha pasado.");
                }
            }
            else
            {
                programada = forzar ? momento : ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(momento);
            }
            if (programada < momento)
            {
                programada = momento;
            }

            string jobId;
            try
            {
                jobId = programarEntrenamiento(forzar, User.Identity?.Name, programada);
            }
            catch (InvalidOperationException ex)
            {
                // Sin Hangfire (en DEBUG no se arranca) no hay dónde encolarlo.
                return Content(HttpStatusCode.ServiceUnavailable, new { Message = "No se puede programar el entrenamiento: " + ex.Message });
            }
            return Content(HttpStatusCode.Accepted, new
            {
                JobId = jobId,
                Programada = programada,
                Forzar = forzar,
                Mensaje = $"Entrenamiento del modelo de sugerencias programado para el {programada:dd/MM/yyyy HH:mm}. Si pasa la puerta, " +
                    "las sugerencias lo usarán en cuanto termine; el resultado (métricas y dónde está el informe) llegará a la campana."
            });
        }

        /// <summary>Ya (o en el próximo minuto): Enqueue; más tarde: Schedule. Los dos en la cola «entrenamiento» (atributo Queue del job).</summary>
        private string ProgramarEnHangfire(bool forzar, string usuario, DateTime programada)
            => programada <= ahora().AddMinutes(1)
                ? BackgroundJob.Enqueue(() => ReentrenamientoModeloSugerenciasJobsService.ReentrenarManual(forzar, usuario))
                : BackgroundJob.Schedule(() => ReentrenamientoModeloSugerenciasJobsService.ReentrenarManual(forzar, usuario), new DateTimeOffset(programada));

        private async Task<T> Usar<T>(Func<IServicioSugerenciasProductos, Task<T>> accion)
        {
            if (crearServicio != null)
            {
                return await accion(crearServicio()).ConfigureAwait(false);
            }
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                // Nesto#537: las fichas llevan la foto de la tienda (la caché de las fotos es común a todas las peticiones)
                return await accion(new ServicioSugerenciasProductos(new RepositorioSugerenciasProductosSql(db),
                    fotos: new Infraestructure.PreparacionAlmacen.FotosProductoAlmacen(),
                    // NestoAPI#671 (corte 2c): el modelo (caché del zip común a todas las peticiones; sin él, reglas-1)
                    predictor: PredictorSugerenciasProductos.Instancia)).ConfigureAwait(false);
            }
        }
    }
}
