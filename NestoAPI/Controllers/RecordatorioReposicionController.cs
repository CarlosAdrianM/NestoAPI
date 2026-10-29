using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#532: recordatorio de reposición calculado con las ventas de Nesto de todos los canales. Nada de lo que hay
    /// aquí escribe a clientes (eso solo lo hace el job de los jueves en modo Activo).
    /// </summary>
    [Authorize]
    [RoutePrefix("api/CorreosPostCompra/Reposicion")]
    public class RecordatorioReposicionController : ApiController
    {
        private readonly Func<ISelectorRecordatoriosReposicion> crearSelector;
        private readonly Func<IRepositorioRecordatorioReposicion> crearRepositorio;
        private readonly ILectorParametrosUsuario lector;
        private readonly IServicioCorreoElectronico servicioCorreo;
        private readonly IGeneradorTextoRecordatorioReposicion generadorTexto;

        public RecordatorioReposicionController()
            : this(null, new LectorParametrosUsuario(), new ServicioCorreoElectronico(), null,
                new GeneradorTextoRecordatorioReposicion(new ServicioOpenAI()))
        {
        }

        /// <param name="crearSelector">Null: el de la BD.</param>
        /// <param name="crearRepositorio">Solo se usa con <paramref name="crearSelector"/> (tests) y en la conversión (tests); sin él, el de la BD.</param>
        public RecordatorioReposicionController(Func<ISelectorRecordatoriosReposicion> crearSelector,
            ILectorParametrosUsuario lector, IServicioCorreoElectronico servicioCorreo,
            Func<IRepositorioRecordatorioReposicion> crearRepositorio = null,
            IGeneradorTextoRecordatorioReposicion generadorTexto = null)
        {
            this.crearSelector = crearSelector;
            this.crearRepositorio = crearRepositorio;
            this.lector = lector;
            this.servicioCorreo = servicioCorreo;
            this.generadorTexto = generadorTexto;
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
            string consumibles = RecordatorioReposicionJobsService.LeerConsumibles(lector);
            if (crearSelector != null)
            {
                return Ok(await crearSelector().Calcular(empresa, hoy, consumibles).ConfigureAwait(false));
            }
            using (NVEntities db = NuevoContexto())
            {
                return Ok(await new SelectorRecordatoriosReposicion(db, topeDias: ParametrosRecordatorioReposicion.LeerTopeDias(lector))
                    .Calcular(empresa, hoy, consumibles).ConfigureAwait(false));
            }
        }

        /// <summary>
        /// Manda ahora el resumen en modo sombra (SOLO a Informática), con el texto de esta semana, las bajas, las
        /// repeticiones y el filtro de vendedores, sin esperar al jueves y con el interruptor como esté. No escribe a ningún
        /// cliente ni registra nada.
        /// </summary>
        [HttpPost]
        [Route("EnviarSombra")]
        public async Task<IHttpActionResult> PostEnviarSombra(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string fecha = null)
        {
            if (!LeerFecha(fecha, out DateTime hoy))
            {
                return BadRequest("La fecha tiene que ir como yyyy-MM-dd");
            }
            string consumibles = RecordatorioReposicionJobsService.LeerConsumibles(lector);
            EjecucionRecordatorioReposicionDTO ejecucion;
            if (crearSelector != null)
            {
                ResultadoRecordatorioReposicionDTO resultado = await crearSelector().Calcular(empresa, hoy, consumibles).ConfigureAwait(false);
                ejecucion = await RecordatorioReposicionJobsService.Ejecutar(ModoRecordatorioReposicion.Sombra, resultado,
                    Dependencias(crearRepositorio?.Invoke(), hoy)).ConfigureAwait(false);
            }
            else
            {
                using (NVEntities db = NuevoContexto())
                {
                    ResultadoRecordatorioReposicionDTO resultado = await new SelectorRecordatoriosReposicion(db,
                            topeDias: ParametrosRecordatorioReposicion.LeerTopeDias(lector))
                        .Calcular(empresa, hoy, consumibles).ConfigureAwait(false);
                    DependenciasRecordatorioReposicion dependencias = Dependencias(new RepositorioRecordatorioReposicionSql(db), hoy);
                    dependencias.Puntuador = RecordatorioReposicionJobsService.CrearPuntuador(db);
                    ejecucion = await RecordatorioReposicionJobsService.Ejecutar(ModoRecordatorioReposicion.Sombra, resultado,
                        dependencias).ConfigureAwait(false);
                }
            }
            return Ok(new
            {
                Enviado = ejecucion.ResumenEnviado,
                Destinatarios = RecordatorioReposicionJobsService.DestinatariosSombra(),
                SeEnviarian = ejecucion.Plan.Enviar.Count,
                FueraPorVendedor = ejecucion.Plan.FueraPorVendedor.Count,
                GrupoControl = ejecucion.Resultado.GrupoControl.Count,
                Descartes = ejecucion.Resultado.Descartes.Count,
                Texto = ejecucion.Texto.Version
            });
        }

        /// <summary>NestoAPI#736: hoy (para saber qué ventanas están cerradas). Para tests.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        /// <summary>
        /// NestoAPI#736: ¿convierte el recordatorio de reposición? De cada producto recordado (Enviado) o apuntado en el grupo de
        /// control (Control) desde <paramref name="desde"/> (por defecto, hace 6 meses), si el cliente volvió a pedir ese producto
        /// en los <paramref name="dias"/> días siguientes (por defecto 30; pedidos desde el día del envío, sin presupuestos). Solo
        /// cuentan los envíos con la ventana ya cerrada. Total y desglose por antigüedad de la última compra, por intervalo y por
        /// P1 del modelo (si hay). Solo lectura. Solo Dirección e Informática.
        /// GET api/CorreosPostCompra/Reposicion/Conversion?desde=2026-10-29&amp;dias=30
        /// </summary>
        [HttpGet]
        [Route("Conversion")]
        [ResponseType(typeof(InformeConversionReposicionDTO))]
        public async Task<IHttpActionResult> GetConversion(DateTime? desde = null, int dias = InformeConversionRecordatorioReposicion.DIAS_VENTANA_POR_DEFECTO,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (User == null || !(User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) || User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA)))
            {
                return StatusCode(System.Net.HttpStatusCode.Forbidden);
            }
            if (dias < 1 || dias > InformeConversionRecordatorioReposicion.DIAS_VENTANA_MAXIMO)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {InformeConversionRecordatorioReposicion.DIAS_VENTANA_MAXIMO}.");
            }
            DateTime hoy = Hoy().Date;
            DateTime inicio = (desde ?? hoy.AddMonths(-InformeConversionRecordatorioReposicion.MESES_POR_DEFECTO)).Date;
            DateTime fin = hoy.AddDays(1); // exclusivo; las de ventana abierta se cuentan aparte (EnviosEnCurso)
            if (inicio >= fin)
            {
                return BadRequest("La fecha «desde» no puede ser posterior a hoy.");
            }
            List<FilaConversionReposicion> filas;
            if (crearRepositorio != null)
            {
                filas = await crearRepositorio().LeerConversion(empresa, inicio, fin, dias).ConfigureAwait(false);
            }
            else
            {
                using (NVEntities db = NuevoContexto())
                {
                    filas = await new RepositorioRecordatorioReposicionSql(db).LeerConversion(empresa, inicio, fin, dias).ConfigureAwait(false);
                }
            }
            return Ok(InformeConversionRecordatorioReposicion.Calcular(filas, inicio, dias, hoy));
        }

        private DependenciasRecordatorioReposicion Dependencias(IRepositorioRecordatorioReposicion repositorio, DateTime hoy)
            => new DependenciasRecordatorioReposicion
            {
                Lector = lector,
                Correo = servicioCorreo,
                RellenarProductos = RecordatorioReposicionJobsService.CrearRellenadorProductos(),
                Repositorio = repositorio,
                GeneradorTexto = generadorTexto,
                Destinatarios = RecordatorioReposicionJobsService.DestinatariosSombra(),
                Remitente = RecordatorioReposicionJobsService.Remitente(),
                Hoy = hoy
            };

        private static NVEntities NuevoContexto()
        {
            var db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            return db;
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
