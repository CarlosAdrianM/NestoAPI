using Hangfire;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.FechasOrientativas;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#853: respuesta de POST api/Compras/PlazosProveedores/Recalcular.</summary>
    public class RecalculoPlazosProveedoresDTO
    {
        public string JobId { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#861: respuesta de POST api/Compras/PlazosProveedores/Orientativas/Procesar.</summary>
    public class ProcesoFechasOrientativasDTO
    {
        public string JobId { get; set; }
        public string Mensaje { get; set; }
    }

    /// <summary>
    /// NestoAPI#853 (corte 2): lo que suele tardar cada proveedor (dbo.ProveedoresPlazosEntrega, que rellena de noche el job
    /// <see cref="PlazosProveedoresJobsService.JOB_ID"/>). El GET solo lee la tabla; el recálculo se encola en Hangfire.
    /// </summary>
    public class ComprasPlazosProveedoresController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Compras, Dirección o Informática pueden ver los plazos de los proveedores.";
        public const string MENSAJE_SIN_PERMISO_RECALCULAR = "Solo Dirección o Informática pueden recalcular los plazos de los proveedores.";

        public const string MENSAJE_SIN_PERMISO_ENTRENAR = "Solo Dirección o Informática pueden entrenar el modelo de plazos.";
        public const int MESES_COMPARACION_MAXIMO = 24;

        private readonly Func<ServicioPlazosProveedores> crearServicio;
        private readonly Func<string> encolar;
        private readonly Func<IRepositorioModeloPlazos> repositorioModelo;
        private readonly IProgramadorModeloPlazos programador;
        private readonly Func<DateTime> ahoraMadrid;
        private readonly Func<bool> modeloGuardado;
        private readonly Func<bool> mandaModelo;
        private readonly Func<ServicioFechasOrientativas> crearOrientativas;
        private readonly Func<string> encolarOrientativas;

        public const string MENSAJE_SIN_PERMISO_ORIENTATIVAS = "Solo Compras, Dirección o Informática pueden ver la medida de las fechas orientativas.";
        public const string MENSAJE_SIN_PERMISO_PROCESAR_ORIENTATIVAS = "Solo Dirección o Informática pueden procesar las fechas orientativas.";

        public ComprasPlazosProveedoresController()
            : this(() => new ServicioPlazosProveedores(RepositorioPlazosProveedores.DesdeConfiguracion()),
                  () => BackgroundJob.Enqueue(() => PlazosProveedoresJobsService.Recalcular()))
        {
        }

        /// <param name="repositorioModelo">NestoAPI#853 (modelo en sombra). Null = el de producción.</param>
        internal ComprasPlazosProveedoresController(Func<ServicioPlazosProveedores> crearServicio, Func<string> encolar,
            Func<IRepositorioModeloPlazos> repositorioModelo = null, IProgramadorModeloPlazos programador = null, Func<DateTime> ahoraMadrid = null,
            Func<bool> modeloGuardado = null, Func<bool> mandaModelo = null,
            Func<ServicioFechasOrientativas> crearOrientativas = null, Func<string> encolarOrientativas = null)
        {
            this.crearServicio = crearServicio ?? throw new ArgumentNullException(nameof(crearServicio));
            this.encolar = encolar ?? (() => "0");
            this.repositorioModelo = repositorioModelo ?? (() => RepositorioModeloPlazos.DesdeConfiguracion());
            this.programador = programador ?? new ProgramadorModeloPlazosHangfire();
            this.ahoraMadrid = ahoraMadrid ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()));
            this.modeloGuardado = modeloGuardado ?? (() => AlmacenModeloPlazos.Produccion().HayModelo);
            this.mandaModelo = mandaModelo ?? (() => ServicioFechaRecepcionAlEnviar.MandaElModelo(ex => ElmahHelper.Log(ex)));
            // NestoAPI#861: el endpoint solo lee la tabla (sin motor ni plazos); el proceso lo hace el job.
            this.crearOrientativas = crearOrientativas ?? (() => new ServicioFechasOrientativas(RepositorioFechasOrientativas.DesdeConfiguracion(),
                null, null, null, () => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular())));
            this.encolarOrientativas = encolarOrientativas ?? (() => BackgroundJob.Enqueue(() => FechasOrientativasJobsService.Procesar()));
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        internal static bool PuedeRecalcular(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Compras/PlazosProveedores
        /// <summary>Las filas de la tabla (la genérica, «*», al final). 503 sin el script.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/PlazosProveedores")]
        [ResponseType(typeof(List<PlazoProveedor>))]
        public IHttpActionResult GetPlazos(string empresa = null)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            ServicioPlazosProveedores servicio = crearServicio();
            if (!servicio.TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioPlazosProveedores.MENSAJE_SIN_TABLA);
            }
            return Ok(servicio.Leer(empresa));
        }

        // POST api/Compras/PlazosProveedores/Recalcular
        /// <summary>Encola el recálculo (202). 503 sin el script.</summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Recalcular")]
        [ResponseType(typeof(RecalculoPlazosProveedoresDTO))]
        public IHttpActionResult PostRecalcular()
        {
            if (!PuedeRecalcular(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_RECALCULAR);
            }
            if (!crearServicio().TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioPlazosProveedores.MENSAJE_SIN_TABLA);
            }
            string id = encolar();
            return Content(HttpStatusCode.Accepted, new RecalculoPlazosProveedoresDTO
            {
                JobId = id,
                Mensaje = "Recálculo de los plazos de los proveedores encolado: en unos minutos estará en GET api/Compras/PlazosProveedores."
            });
        }

        // GET api/Compras/PlazosProveedores/Comparacion?meses=6
        /// <summary>
        /// NestoAPI#853: regla del P80 contra el modelo ML.NET en sombra: el último entrenamiento (métricas de validación de los dos) y lo
        /// REAL de los últimos <paramref name="meses"/> (pedidos enviados con las dos fechas y ya recibidos, vPedidosCompraEnviosError)
        /// con un veredicto. 503 sin el script.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Comparacion")]
        [ResponseType(typeof(ComparacionModeloPlazosDTO))]
        public IHttpActionResult GetComparacion(int meses = 6)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            IRepositorioModeloPlazos repositorio = repositorioModelo();
            if (!repositorio.TablaEntrenamientosExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ModeloPlazosProveedoresJobsService.MENSAJE_SIN_TABLA);
            }
            int mesesValidos = Math.Max(1, Math.Min(MESES_COMPARACION_MAXIMO, meses));
            List<EnvioRecibidoPlazos> envios = repositorio.TablaEnviosExiste()
                ? repositorio.LeerEnviosRecibidos(ahoraMadrid().Date.AddMonths(-mesesValidos))
                : new List<EnvioRecibidoPlazos>();
            return Ok(new ComparacionModeloPlazosDTO
            {
                Meses = mesesValidos,
                ModeloGuardado = modeloGuardado(),
                QuienManda = mandaModelo() ? ServicioFechaRecepcionAlEnviar.QUIEN_MANDA_MODELO : ServicioFechaRecepcionAlEnviar.QUIEN_MANDA_REGLA,
                UltimoEntrenamiento = repositorio.UltimoEntrenamiento(),
                Real = ComparacionPlazos.Comparar(envios)
            });
        }

        // POST api/Compras/PlazosProveedores/Modelo/Entrenar?ahora=false
        /// <summary>
        /// NestoAPI#853: entrena el modelo de plazos (202). De lunes a viernes de 8 a 20 (Madrid) se programa para las 21:00, salvo
        /// <paramref name="ahora"/> = true. 503 sin el script.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Modelo/Entrenar")]
        [ResponseType(typeof(LanzamientoModeloPlazosDTO))]
        public IHttpActionResult PostEntrenarModelo(bool ahora = false)
        {
            if (!PuedeRecalcular(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_ENTRENAR);
            }
            if (!repositorioModelo().TablaEntrenamientosExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ModeloPlazosProveedoresJobsService.MENSAJE_SIN_TABLA);
            }
            string usuario = User?.Identity?.Name;
            DateTime madrid = ahoraMadrid();
            if (!ahora && HorarioHistoricoCorreos.EnHorario(madrid))
            {
                DateTime cuando = HorarioHistoricoCorreos.CuandoEmpezar(madrid);
                return Content(HttpStatusCode.Accepted, new LanzamientoModeloPlazosDTO
                {
                    JobId = programador.Programar(cuando, usuario),
                    Programado = true,
                    Mensaje = $"Es horario de trabajo: el modelo de plazos se entrenará a las {cuando:HH:mm} (hora de Madrid). El resultado, en GET api/Compras/PlazosProveedores/Comparacion."
                });
            }
            return Content(HttpStatusCode.Accepted, new LanzamientoModeloPlazosDTO
            {
                JobId = programador.Encolar(usuario),
                Programado = false,
                Mensaje = "Entrenamiento del modelo de plazos encolado: en unos minutos, el resultado en GET api/Compras/PlazosProveedores/Comparacion."
            });
        }

        // GET api/Compras/PlazosProveedores/Orientativas?meses=6
        /// <summary>
        /// NestoAPI#861: cuánto acierta la fecha ORIENTATIVA de lo no pedido: por proveedor y en total, de las primeras fotos de cada
        /// episodio ya resueltas de los últimos <paramref name="meses"/> (1-24), con los errores de pedido y de llegada y el % que se queda
        /// corta, también separando «ya tocaba pedir». 503 sin el script.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Orientativas")]
        [ResponseType(typeof(ResumenFechasOrientativasDTO))]
        public IHttpActionResult GetOrientativas(int meses = 6, string empresa = null)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_ORIENTATIVAS);
            }
            ServicioFechasOrientativas servicio = crearOrientativas();
            if (!servicio.TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioFechasOrientativas.MENSAJE_SIN_TABLA);
            }
            return Ok(servicio.Resumen(empresa, meses));
        }

        // POST api/Compras/PlazosProveedores/Orientativas/Procesar
        /// <summary>NestoAPI#861: encola la pasada del job (foto de hoy y resolución de las abiertas) (202). 503 sin el script.</summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/PlazosProveedores/Orientativas/Procesar")]
        [ResponseType(typeof(ProcesoFechasOrientativasDTO))]
        public IHttpActionResult PostProcesarOrientativas()
        {
            if (!PuedeRecalcular(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_PROCESAR_ORIENTATIVAS);
            }
            if (!crearOrientativas().TablaExiste())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioFechasOrientativas.MENSAJE_SIN_TABLA);
            }
            return Content(HttpStatusCode.Accepted, new ProcesoFechasOrientativasDTO
            {
                JobId = encolarOrientativas(),
                Mensaje = "Proceso de las fechas orientativas encolado: en unos minutos estará en GET api/Compras/PlazosProveedores/Orientativas."
            });
        }
    }
}
