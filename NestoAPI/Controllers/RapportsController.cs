using Hangfire;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Net;
using System.Web.Http;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/Rapports")]
    public class RapportsController : ApiController
    {
        private readonly Func<bool, string, string> encolar;

        public RapportsController() : this(null)
        {
        }

        /// <param name="encolar">(simular, usuario) → id del job de Hangfire (sustituible en tests).</param>
        internal RapportsController(Func<bool, string, string> encolar)
        {
            this.encolar = encolar ?? ((simular, usuario) =>
                BackgroundJob.Enqueue(() => ReentrenamientoModeloLlamadasJobsService.ReentrenarManual(simular, usuario)));
        }

        /// <summary>
        /// NestoAPI#619: lanza a mano el reentrenamiento del modelo de llamadas (el mismo job que corre el primer sábado de cada
        /// mes). Lo encola en Hangfire (cola «entrenamiento») y responde 202 con el id del job; el resultado llega a la campana de
        /// Nesto de quien diga ModeloLlamadas:UsuariosAviso. Con simular=true (por defecto) entrena y evalúa pero no promueve.
        /// Ojo: entrenar lee 3 años de rapports y pedidos de la BD de producción; mejor fuera de horario. Solo Dirección e Informática.
        /// POST api/Rapports/ReentrenarModeloLlamadas?simular=false
        /// </summary>
        [HttpPost]
        [Route("ReentrenarModeloLlamadas")]
        [Authorize]
        public IHttpActionResult ReentrenarModeloLlamadas(bool simular = true)
        {
            if (User == null || !(User.IsInRoleSinDominio(GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            string jobId;
            try
            {
                jobId = encolar(simular, User.Identity?.Name);
            }
            catch (InvalidOperationException ex)
            {
                // Sin Hangfire (en DEBUG no se arranca) no hay dónde encolarlo.
                return Content(HttpStatusCode.ServiceUnavailable, new { Message = "No se puede encolar el reentrenamiento: " + ex.Message });
            }
            return Content(HttpStatusCode.Accepted, new
            {
                JobId = jobId,
                Simular = simular,
                Mensaje = simular
                    ? "Reentrenamiento encolado en modo simulación: entrena y evalúa, pero no cambia el modelo. El resultado llegará a la campana."
                    : "Reentrenamiento encolado: si el modelo nuevo pasa la puerta de calidad, sustituye al actual. El resultado llegará a la campana."
            });
        }
    }
}
