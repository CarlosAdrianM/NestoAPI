using Hangfire;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.Abandono;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#714 (corte 1): el informe de la regla baseline de abandono (precisión por vendedor y mes) y el lanzamiento a mano
    /// de la pasada. Controlador propio (no se toca ClientesController). Solo Dirección e Informática: nada visible a los vendedores
    /// hasta el corte 4.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Clientes/RiesgoAbandono")]
    public class ClientesRiesgoAbandonoController : ApiController
    {
        private readonly IRepositorioRiesgoAbandono repositorio;
        private readonly Func<DateTime> ahora;
        private readonly Func<ModoAbandono> modo;
        /// <summary>Para los tests: qué hace POST Ejecutar en vez de encolar en Hangfire. Devuelve el id del job.</summary>
        internal Func<string> Encolar { get; set; }

        public ClientesRiesgoAbandonoController() : this(null)
        {
        }

        internal ClientesRiesgoAbandonoController(IRepositorioRiesgoAbandono repositorio, Func<DateTime> ahora = null, Func<ModoAbandono> modo = null)
        {
            this.repositorio = repositorio ?? new RepositorioRiesgoAbandonoSql();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.modo = modo ?? (() => InterruptorAbandono.Modo);
        }

        /// <summary>
        /// Precisión de la regla por vendedor y mes de la instantánea: alertas, resueltas (horizonte vencido), abandonos, bajas,
        /// compraron, desconocidos, pendientes y precisión = (abandonos + bajas) / (resueltas − desconocidos). Sin
        /// <paramref name="desde"/>, los últimos <see cref="InformeRiesgoAbandono.DIAS_POR_DEFECTO"/> días; sin <paramref name="hasta"/>,
        /// hasta hoy incluido. Solo lee.
        /// GET api/Clientes/RiesgoAbandono/Informe?desde=2026-11-01&amp;hasta=2026-11-30
        /// </summary>
        [HttpGet]
        [Route("Informe")]
        [ResponseType(typeof(InformeRiesgoAbandonoDTO))]
        public IHttpActionResult GetInforme(DateTime? desde = null, DateTime? hasta = null)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            DateTime hoy = ahora().Date;
            DateTime inicio = (desde ?? hoy.AddDays(-InformeRiesgoAbandono.DIAS_POR_DEFECTO)).Date;
            DateTime fin = (hasta?.Date ?? hoy).AddDays(1); // exclusivo
            if (fin <= inicio)
            {
                return BadRequest("La fecha «hasta» no puede ser anterior a «desde».");
            }
            if (!repositorio.TablaExiste())
            {
                return BadRequest("Falta la tabla ClientesRiesgoAbandono: hay que lanzar Scripts/Issue714_ClientesRiesgoAbandono.sql.");
            }
            List<FilaInformeAbandono> filas = repositorio.LeerInforme(inicio, fin);
            return Ok(InformeRiesgoAbandono.Calcular(filas, inicio, fin, modo()));
        }

        /// <summary>
        /// Encola ahora mismo la pasada diaria (resolver vencidas + evaluar la cartera de cada vendedor), con el interruptor que
        /// haya (Apagado no hace nada). Para no esperar a las 05:20 tras el deploy. Es ligera (segundos), pero mejor fuera de horario.
        /// POST api/Clientes/RiesgoAbandono/Ejecutar
        /// </summary>
        [HttpPost]
        [Route("Ejecutar")]
        public IHttpActionResult PostEjecutar()
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            ModoAbandono modoActual = modo();
            if (modoActual == ModoAbandono.Apagado)
            {
                return BadRequest($"El interruptor {InterruptorAbandono.CLAVE} está en Apagado: la pasada no haría nada.");
            }
            string idJob = Encolar != null
                ? Encolar()
                : BackgroundJob.Enqueue(() => RiesgoAbandonoJobsService.ResolverVencidas()) + " / " + BackgroundJob.Enqueue(() => RiesgoAbandonoJobsService.PasadaDiaria());
            return Ok(new { Modo = modoActual.ToString(), Jobs = idJob, Mensaje = "Pasada encolada; el resultado queda en ClientesRiesgoAbandono y en el informe." });
        }

        private bool EsDireccionOInformatica()
            => User != null && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
    }
}
