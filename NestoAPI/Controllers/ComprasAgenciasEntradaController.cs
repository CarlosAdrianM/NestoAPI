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
        /// <summary>
        /// Nombre de la agencia («GLS») o texto libre («Transportista de tal proveedor»). NestoAPI#857e: vacío = remitente de un PROVEEDOR
        /// que avisa de la salida (con Scripts/Issue857e; sin él, obligatoria).
        /// </summary>
        public string Agencia { get; set; }
        /// <summary>Dirección («avisos@ejemplo-agencia.es») o dominio («ejemplo-agencia.es»).</summary>
        public string Remitente { get; set; }
        /// <summary>
        /// NestoAPI#857e: solo cuentan sus correos que lo digan en el asunto o el cuerpo, sin distinguir mayúsculas ni tildes; varias
        /// alternativas con «|» («albarán de entrega|delivery note»). Vacío = todos.
        /// </summary>
        public string FiltroTexto { get; set; }
        /// <summary>NestoAPI#857e: leer sus PDF adjuntos (en memoria) para sacar el pedido, la agencia y la entrega.</summary>
        public bool LeerPdf { get; set; }
        /// <summary>NestoAPI#857e: no mirar nunca sus correos (boletines…): ni se guardan, ni se reenvían, ni avisan.</summary>
        public bool Ignorar { get; set; }
    }

    /// <summary>NestoAPI#857: un correo captado, con los candidatos ya leídos del JSON.</summary>
    public class CorreoSeguimientoCompraDTO
    {
        /// <summary>NestoAPI#897: los bultos que dice el correo de la agencia (DHL eCommerce), del extracto guardado; null si no lo dice.</summary>
        public int? Bultos { get; set; }
        /// <summary>NestoAPI#897: los kilos que dice el correo de la agencia; null si no lo dice.</summary>
        public decimal? Kilos { get; set; }
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
        /// <summary>Adjuntos de verdad (sin los de la firma); null = sin apuntar (sin el script Issue857b, o correo de antes).</summary>
        public int? NumAdjuntos { get; set; }
        /// <summary>Los 10 primeros (nombre, tipo y tamaño en bytes); null si no se apuntaron.</summary>
        public List<AdjuntoCorreoCompraDTO> Adjuntos { get; set; }
        /// <summary>Día de entrega que anuncia el correo; null si no lo dice (o sin el script Issue857c, o correo de antes).</summary>
        public DateTime? EntregaEstimadaDia { get; set; }
        /// <summary>Tramo horario que anuncia el correo (Desde puede faltar: «antes de las 14:00»).</summary>
        public TimeSpan? EntregaEstimadaDesde { get; set; }
        public TimeSpan? EntregaEstimadaHasta { get; set; }
        /// <summary>Desde cuándo se podría servir (final del tramo antes del corte del picking → ese día; si no, el siguiente laborable de Algete).</summary>
        public DateTime? DisponibleEstimado { get; set; }
        /// <summary>Envío nuestro de salida que llega en copia (se guarda sin asignación); null = sin apuntar.</summary>
        public bool? EsSalidaNuestra { get; set; }
        /// <summary>Fase 3 (Scripts/Issue857d): quién expide el paquete según el aviso, normalizado.</summary>
        public string RemitentePaquete { get; set; }
        /// <summary>Fase 3: el pedido al que se ha puesto la fecha de este correo (null = sin aplicar).</summary>
        public string EmpresaAplicada { get; set; }
        public int? PedidoAplicado { get; set; }
        public DateTime? FechaRecepcionAplicada { get; set; }
        public DateTime? AplicadoEl { get; set; }
        /// <summary>El usuario, o «(automático)».</summary>
        public string AplicadoPor { get; set; }
        public bool? Descartado { get; set; }
        public bool? EligioSugerido { get; set; }
        /// <summary>NestoAPI#857e: aviso de salida de un remitente marcado sin agencia (el propio proveedor); null = sin apuntar.</summary>
        public bool? AvisoProveedor { get; set; }
        /// <summary>NestoAPI#857e: el pedido de compra que se ha leído del PDF adjunto; null si ninguno.</summary>
        public int? PedidoEnAdjunto { get; set; }
        /// <summary>NestoAPI#857e: SinPdf, Leido, SinTexto, Ilegible, Grande o NoDescargado; null = no se pidió leer el PDF.</summary>
        public string AdjuntoLeido { get; set; }
        /// <summary>NestoAPI#857f: EnCamino, EnReparto, Reprogramada o Entregado según la propia agencia; null = no lo dice (o sin Issue857f).</summary>
        public string EstadoEnvio { get; set; }
        /// <summary>NestoAPI#857f: de dónde sale la entrega estimada: Agencia, Proveedor o Consulta; null = sin apuntar.</summary>
        public string OrigenEntrega { get; set; }
        /// <summary>NestoAPI#857f: cuándo se preguntó por última vez a la agencia; null = nunca.</summary>
        public DateTime? EntregaConsultadaEl { get; set; }
        /// <summary>20/11/26 (Scripts/Issue857h): los productos y unidades que dice el aviso del proveedor; null si no los dice (o sin el script).</summary>
        public List<LineaAvisoSalida> LineasAviso { get; set; }
        /// <summary>20/11/26: las líneas nuestras en el aire que casaron con el aviso al analizarlo (de uno o varios pedidos); null si ninguna.</summary>
        public List<LineaCasadaAviso> LineasCasadas { get; set; }
        /// <summary>20/11/26: las líneas a las que se puso la fecha; null = a todo el pedido (o sin aplicar).</summary>
        public List<LineaCasadaAviso> LineasAplicadas { get; set; }
        /// <summary>NestoAPI#857i: la delegación de origen que pone la agencia en el código («Vic», o la clave si no sabe el nombre); null si no lo pone.</summary>
        public string OrigenAgencia { get; set; }
        /// <summary>NestoAPI#897/#857i: por qué se ha propuesto el pedido (control de pendientes, origen de la agencia); null si no hay.</summary>
        public string Explicacion { get; set; }
    }

    /// <summary>NestoAPI#857 (fase 3): un pedido candidato de un envío por asignar, con lo que hace falta para elegir.</summary>
    public class CandidatoPorAsignarDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Motivo { get; set; }
        /// <summary>CabPedidoCmp.[Fecha Modificación] (cuando se envió).</summary>
        public DateTime? FechaEnvio { get; set; }
        public int LineasPendientes { get; set; }
        /// <summary>Si es mayor que 0, lo pendiente es un resto.</summary>
        public int LineasYaRecibidas { get; set; }
        /// <summary>El enviado más recientemente antes del primer correo del envío (como mucho uno por envío). No se elige solo.</summary>
        public bool Sugerido { get; set; }
        /// <summary>NestoAPI#897: proveedor con control de pendientes y este pedido es «de pendientes» (nunca recibe la fecha por esa regla).</summary>
        public bool DePendientes { get; set; }
        /// <summary>NestoAPI#897: por qué es «de pendientes» («ya tiene 2 líneas recibidas…», «se envió el 20/08/2026, hace más de 30 días»).</summary>
        public string MotivoDePendientes { get; set; }
    }

    /// <summary>NestoAPI#857 (fase 3): un envío por asignar (los correos que comparten código de seguimiento).</summary>
    public class EnvioPorAsignarDTO
    {
        /// <summary>El Id del primer correo del envío (para referirse a él; se asigna con cualquiera de sus correos).</summary>
        public int Clave { get; set; }
        public DateTime PrimerCorreo { get; set; }
        public List<CorreoSeguimientoCompraDTO> Correos { get; set; } = new List<CorreoSeguimientoCompraDTO>();
        public List<string> Codigos { get; set; } = new List<string>();
        public string Agencia { get; set; }
        /// <summary>La entrega estimada más tardía de sus correos.</summary>
        public DateTime? EntregaEstimadaDia { get; set; }
        /// <summary>La fecha de recepción que se pondría al asignarlo hoy.</summary>
        public DateTime FechaRecepcionPropuesta { get; set; }
        public string RemitentePaquete { get; set; }
        /// <summary>
        /// El proveedor de los candidatos si es uno solo. 27/11/26: sin candidatos, el que dice la memoria de remitentes del paquete
        /// (<see cref="ReglaRemitenteRecordadoSinCandidatos"/>), y entonces <see cref="Explicacion"/> dice por qué no hay candidatos.
        /// </summary>
        public string ProveedorPropuesto { get; set; }
        /// <summary>27/11/26: el nombre de <see cref="ProveedorPropuesto"/> cuando se sabe; null si no.</summary>
        public string NombreProveedorPropuesto { get; set; }
        /// <summary>NestoAPI#857i: la delegación de origen que pone la agencia en el código de alguno de sus correos («Vic»); null si ninguno.</summary>
        public string OrigenAgencia { get; set; }
        /// <summary>NestoAPI#897/#857i: por qué se proponen estos candidatos; null si no hay explicación.</summary>
        public string Explicacion { get; set; }
        /// <summary>25/11/26: solo con ?incluirLlegados=true: por qué ya no está pendiente de asignar (<see cref="ReglaEnvioYaNoPendiente"/>); null si lo está.</summary>
        public string MotivoYaNoPendiente { get; set; }
        public List<CandidatoPorAsignarDTO> Candidatos { get; set; } = new List<CandidatoPorAsignarDTO>();
    }

    /// <summary>NestoAPI#857 (fase 3): un pedido de compra en el aire, para elegir a mano.</summary>
    public class PedidoEnAireDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public DateTime? FechaEnvio { get; set; }
        public int LineasPendientes { get; set; }
        public int LineasYaRecibidas { get; set; }
        /// <summary>Es candidato de algún envío de la lista.</summary>
        public bool EsCandidato { get; set; }
        /// <summary>Las claves de los envíos de los que es candidato.</summary>
        public List<int> Envios { get; set; } = new List<int>();
    }

    /// <summary>NestoAPI#857 (fase 3): GET api/Compras/AgenciasEntrada/PorAsignar.</summary>
    public class PorAsignarCorreosCompraDTO
    {
        public List<EnvioPorAsignarDTO> Envios { get; set; } = new List<EnvioPorAsignarDTO>();
        public List<PedidoEnAireDTO> PedidosEnAire { get; set; } = new List<PedidoEnAireDTO>();
    }

    /// <summary>NestoAPI#857 (fase 3): respuesta de POST …/Correos/{id}/Descartar.</summary>
    public class RespuestaDescarteCorreoCompraDTO
    {
        public int CorreoId { get; set; }
        public int CorreosDelMismoEnvio { get; set; }
        public string Mensaje { get; set; }
        /// <summary>NestoAPI#857e: con ?ignorarRemitente=true, la dirección que ha quedado como «Ignorar» (null si no se ha podido o no se pidió).</summary>
        public string RemitenteIgnorado { get; set; }
    }

    public class AdjuntoCorreoCompraDTO
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public long Tamano { get; set; }
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

        public const string MENSAJE_SIN_PERMISO_HISTORICO = "Solo Dirección o Informática pueden lanzar la lectura del histórico del buzón de Compras.";

        private readonly IRepositorioCorreosSeguimiento repositorio;
        private readonly IRepositorioCorreosSinReconocer sinReconocer;
        private readonly Func<ServicioHistoricoCorreosCompras> crearHistorico;

        private readonly Func<ServicioAplicacionCorreosCompras> crearAplicacion;
        private readonly Func<ServicioCorreosSeguimientoCompras> crearNucleo;
        private const string USUARIO_ELMAH = "Sistema (correos de seguimiento de compras)";

        public ComprasAgenciasEntradaController() : this(new RepositorioCorreosSeguimiento())
        {
        }

        internal ComprasAgenciasEntradaController(IRepositorioCorreosSeguimiento repositorio, Func<ServicioHistoricoCorreosCompras> crearHistorico = null,
            Func<ServicioAplicacionCorreosCompras> crearAplicacion = null, Func<ServicioCorreosSeguimientoCompras> crearNucleo = null,
            IRepositorioCorreosSinReconocer sinReconocer = null)
        {
            this.repositorio = repositorio;
            this.sinReconocer = sinReconocer ?? new RepositorioCorreosSinReconocer();
            this.crearHistorico = crearHistorico ?? HistoricoCorreosComprasJobs.CrearServicio;
            this.crearAplicacion = crearAplicacion ?? (() => new ServicioAplicacionCorreosCompras(repositorio, registrar: ex => ElmahHelper.Log(ex, USUARIO_ELMAH)));
            this.crearNucleo = crearNucleo ?? (() =>
            {
                ConfiguracionBuzonCompras configuracion = ConfiguracionBuzonCompras.Leer(clave => System.Configuration.ConfigurationManager.AppSettings[clave]);
                ILectorBuzonCompras lector = configuracion == null ? null : new LectorBuzonComprasGraph(configuracion);
                return new ServicioCorreosSeguimientoCompras(lector, repositorio, ex => ElmahHelper.Log(ex, USUARIO_ELMAH), aplicacion: this.crearAplicacion(),
                    sinReconocer: this.sinReconocer);
            });
        }

        internal static bool PuedeVer(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.COMPRAS)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        /// <summary>El histórico (trabajo pesado contra el buzón): solo Dirección o Informática.</summary>
        internal static bool PuedeLanzarHistorico(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // POST api/Compras/AgenciasEntrada/Historico?meses=12[&ahora=true]
        /// <summary>
        /// Lee hacia atrás la bandeja de entrada del buzón de Compras (solo lectura, en sombra) con el mismo análisis que el job. No se
        /// hace en la petición: se encola en Hangfire (202). Entre las 8:00 y las 20:00 de un día laborable empieza a las 21:00, salvo
        /// <paramref name="ahora"/>. 409 si ya hay una en marcha; 503 sin claves del buzón o sin las tablas.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Historico")]
        [ResponseType(typeof(ResultadoLanzamientoHistorico))]
        public async Task<IHttpActionResult> PostHistorico(int meses = ServicioHistoricoCorreosCompras.MESES_POR_DEFECTO, bool ahora = false)
        {
            if (!PuedeLanzarHistorico(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_HISTORICO);
            }
            if (meses < 1 || meses > ServicioHistoricoCorreosCompras.MESES_MAXIMOS)
            {
                return BadRequest($"Los meses tienen que estar entre 1 y {ServicioHistoricoCorreosCompras.MESES_MAXIMOS}.");
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoLanzamientoHistorico resultado = await crearHistorico().Lanzar(meses, ahora, usuario).ConfigureAwait(false);
            switch (resultado.Codigo)
            {
                case CodigoLanzamientoHistorico.SinConfiguracion:
                case CodigoLanzamientoHistorico.SinTabla:
                    return Content(HttpStatusCode.ServiceUnavailable, resultado.Mensaje);
                case CodigoLanzamientoHistorico.YaEnMarcha:
                    return Content(HttpStatusCode.Conflict, resultado);
                default:
                    return Content(HttpStatusCode.Accepted, resultado);
            }
        }

        // GET api/Compras/AgenciasEntrada/Historico
        /// <summary>El estado y el progreso de la lectura del histórico (null en Estado si nunca se ha lanzado).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Historico")]
        [ResponseType(typeof(EstadoHistoricoCorreosCompras))]
        public async Task<IHttpActionResult> GetHistorico()
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            ServicioHistoricoCorreosCompras historico = crearHistorico();
            if (!historico.Configurado)
            {
                return Content(HttpStatusCode.ServiceUnavailable, "Faltan las claves del buzón de Compras en secretos.config.");
            }
            if (!repositorio.TablasExisten())
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_TABLA);
            }
            EstadoHistoricoCorreosCompras estado = await historico.LeerEstado().ConfigureAwait(false);
            return Ok(estado ?? new EstadoHistoricoCorreosCompras { Mensaje = "Nunca se ha lanzado." });
        }

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

        public const string MENSAJE_SIN_SCRIPT_AVISOS = "Para marcar un remitente de proveedor (sin agencia), con filtro de texto o con lectura del PDF, " +
            "hay que lanzar antes Scripts/Issue857e_ComprasCorreosRemitentesPdf.sql.";

        // POST api/Compras/AgenciasEntrada/Remitentes   { "Agencia": "GLS", "Remitente": "ejemplo-agencia.es" }
        //   NestoAPI#857e: { "Agencia": null, "Remitente": "sales@dduueett.com", "FiltroTexto": "albarán de entrega|delivery note", "LeerPdf": true }
        /// <summary>
        /// Marca una dirección o un dominio como de una agencia (si ya estaba, cambia la agencia y lo reactiva). NestoAPI#857e: sin agencia,
        /// es el remitente de un proveedor que avisa de la salida; FiltroTexto y LeerPdf, opcionales (503 si se usan sin Scripts/Issue857e).
        /// </summary>
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
            if (peticion == null)
            {
                return BadRequest("Falta el remitente.");
            }
            string agencia = peticion.Agencia?.Trim();
            if (string.IsNullOrEmpty(agencia))
            {
                agencia = null;
            }
            else
            {
                // Si es una conocida, con su nombre de siempre (así casa con HorasHastaEntrega)
                agencia = AgenciasEntradaRegistradas.PorNombre(agencia)?.Nombre ?? agencia;
                if (agencia.Length > LONGITUD_AGENCIA)
                {
                    agencia = agencia.Substring(0, LONGITUD_AGENCIA);
                }
            }
            string filtro = FiltroRemitente.Limpiar(peticion.FiltroTexto);
            if (filtro != null && filtro.Length > FiltroRemitente.LONGITUD)
            {
                return BadRequest($"El filtro de texto no puede pasar de {FiltroRemitente.LONGITUD} caracteres.");
            }
            if (filtro == null && !string.IsNullOrWhiteSpace(peticion.FiltroTexto))
            {
                return BadRequest("El filtro de texto no tiene ninguna palabra: escriba lo que tiene que decir el correo (varias alternativas, separadas por «|»).");
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
            if ((agencia == null || filtro != null || peticion.LeerPdf || peticion.Ignorar) && !repositorio.AvisosProveedorDisponibles())
            {
                return agencia == null && filtro == null && !peticion.LeerPdf && !peticion.Ignorar
                    ? (IHttpActionResult)BadRequest("Falta la agencia. " + MENSAJE_SIN_SCRIPT_AVISOS)
                    : Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT_AVISOS);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            int id = await repositorio.GuardarRemitente(agencia, remitente, usuario, filtro, peticion.LeerPdf, peticion.Ignorar).ConfigureAwait(false);
            return Ok(new RemitenteMarcado
            {
                Id = id,
                Agencia = agencia,
                Remitente = remitente,
                Activo = true,
                Usuario = usuario,
                FechaModificacion = DateTime.Now,
                FiltroTexto = filtro,
                LeerPdf = peticion.LeerPdf,
                Ignorar = peticion.Ignorar
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

        public const string MENSAJE_SIN_TABLA_SIN_RECONOCER =
            "Todavía no existe la tabla de los correos sin reconocer: hay que lanzar Scripts/Issue857j_ComprasCorreosSinReconocer.sql.";

        // GET api/Compras/AgenciasEntrada/Correos/SinReconocer?dias=30
        /// <summary>
        /// NestoAPI#857j: los correos de una agencia conocida que el lector no ha reconocido (ni código, ni entrega, ni estado), lo más reciente
        /// primero, sin los que Reanalizar ya ha reconocido. Para descubrir formatos nuevos: no tienen proveedor, pedido ni fecha. 503 sin
        /// Scripts/Issue857j.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Correos/SinReconocer")]
        [ResponseType(typeof(List<CorreoSinReconocerFila>))]
        public async Task<IHttpActionResult> GetCorreosSinReconocer(int dias = 30)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (dias < 1 || dias > DIAS_MAXIMOS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {DIAS_MAXIMOS}.");
            }
            if (!sinReconocer.Disponible())
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_TABLA_SIN_RECONOCER);
            }
            List<CorreoSinReconocerFila> filas = await sinReconocer.Leer(DateTime.Today.AddDays(-dias)).ConfigureAwait(false);
            return Ok(filas ?? new List<CorreoSinReconocerFila>());
        }

        // GET api/Compras/AgenciasEntrada/PorAsignar?dias=7[&incluirLlegados=true]
        /// <remarks>
        /// 25/11/26: los envíos que ya no están pendientes (asignados o descartados por otro de sus correos, entregados según la agencia, con el
        /// albarán ya encontrado o con todos sus candidatos ya recibidos: <see cref="ReglaEnvioYaNoPendiente"/>) no salen salvo con
        /// <c>incluirLlegados</c>, y entonces llevan MotivoYaNoPendiente.
        /// </remarks>
        /// <summary>
        /// Fase 3: los envíos (correos que comparten código de seguimiento) de los últimos días que nadie ha asignado ni descartado, con sus
        /// candidatos (fecha de envío, líneas pendientes y ya recibidas y el sugerido), y todos los pedidos de compra en el aire para elegir.
        /// 503 sin Scripts/Issue857d.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/PorAsignar")]
        [ResponseType(typeof(PorAsignarCorreosCompraDTO))]
        public async Task<IHttpActionResult> GetPorAsignar(int dias = 7, bool incluirLlegados = false)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (dias < 1 || dias > DIAS_MAXIMOS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {DIAS_MAXIMOS}.");
            }
            if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioAplicacionCorreosCompras.MENSAJE_SIN_SCRIPT);
            }
            DateTime hoy = DateTime.Today;
            List<CorreoSeguimientoFila> correos = await repositorio.LeerCorreos(hoy.AddDays(-dias - MismoEnvio.DIAS)).ConfigureAwait(false);
            List<PedidoCompraEnAire> pedidos = await repositorio.LeerPedidosEnAire(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
            // 27/11/26: la memoria de remitentes del paquete, para decir de quién es un envío sin candidatos
            List<RemitentePaqueteRecordado> memoria = await repositorio.LeerRemitentesPaquete().ConfigureAwait(false);
            return Ok(ComponerPorAsignar(correos, pedidos, hoy.AddDays(-dias), hoy, ReglaControlPendientes.LeerDias(), incluirLlegados, memoria));
        }

        /// <summary>
        /// Pura: agrupa por envío lo que tiene algún correo por asignar recibido desde <paramref name="desde"/> (los demás correos del envío
        /// solo cuentan para agrupar), con los candidatos del envío y el sugerido; y los pedidos en el aire marcando de qué envíos son candidatos.
        /// </summary>
        internal static PorAsignarCorreosCompraDTO ComponerPorAsignar(IEnumerable<CorreoSeguimientoFila> correos, IEnumerable<PedidoCompraEnAire> pedidos,
            DateTime desde, DateTime hoy, int diasPedidoDePendientes = ReglaControlPendientes.DIAS_POR_DEFECTO, bool incluirLlegados = false,
            IEnumerable<RemitentePaqueteRecordado> memoria = null)
        {
            List<PedidoCompraEnAire> enAire = (pedidos ?? Enumerable.Empty<PedidoCompraEnAire>()).Where(p => p != null).ToList();
            var resultado = new PorAsignarCorreosCompraDTO();
            foreach (List<CorreoSeguimientoFila> grupo in MismoEnvio.Agrupar(correos))
            {
                List<CorreoSeguimientoFila> porAsignar = grupo.Where(CorreosPorAsignar.Es).ToList();
                if (!porAsignar.Any(f => f.Recibido >= desde))
                {
                    continue;
                }
                // 25/11/26: el envío entero (también lo que ya llegó, se entregó o se aplicó con otro de sus correos)
                string yaNoPendiente = ReglaEnvioYaNoPendiente.Motivo(grupo, enAire);
                if (yaNoPendiente != null && !incluirLlegados)
                {
                    continue;
                }
                DecisionEnvio decision = MismoEnvio.Decidir(grupo);
                DateTime primero = grupo.Min(f => f.Recibido);
                List<PedidoCompraEnAire> delEnvio = ReglaPedidoSugerido.DelEnvio(enAire, decision.Candidatos);
                PedidoCompraEnAire sugerido = ReglaPedidoSugerido.Sugerido(delEnvio, primero);
                CorreoSeguimientoFila conPropios = grupo.FirstOrDefault(f => MismoEnvio.Propios(f).Any() && !string.IsNullOrWhiteSpace(f.RemitentePaquete));
                var envio = new EnvioPorAsignarDTO
                {
                    Clave = porAsignar.Min(f => f.Id),
                    PrimerCorreo = primero,
                    Correos = porAsignar.Select(Dto).ToList(),
                    Codigos = grupo.SelectMany(MismoEnvio.Codigos).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToList(),
                    Agencia = grupo.Select(f => f.Agencia).Where(a => !string.IsNullOrWhiteSpace(a)).GroupBy(a => a)
                        .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
                    EntregaEstimadaDia = decision.EntregaEstimadaDia,
                    FechaRecepcionPropuesta = ReglaFechaRecepcionPorCorreo.Calcular(grupo, hoy, NestoAPI.Infraestructure.JobsFestivos.EsFestivoAlgete),
                    RemitentePaquete = conPropios?.RemitentePaquete ?? grupo.Select(f => f.RemitentePaquete).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)),
                    ProveedorPropuesto = decision.ProveedorPropuesto,
                    OrigenAgencia = OrigenesAgenciaCorreo.Texto(OrigenesAgenciaCorreo.DelEnvio(grupo)),
                    Explicacion = grupo.Select(f => ReglaControlPendientes.ExplicacionDe(f)).FirstOrDefault(t => t != null),
                    MotivoYaNoPendiente = yaNoPendiente
                };
                foreach (CandidatoPedidoCompra candidato in decision.Candidatos)
                {
                    PedidoCompraEnAire pedido = delEnvio.FirstOrDefault(p => AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido) == AsignadorPedidosCompra.Clave(candidato.Empresa, candidato.Pedido));
                    // NestoAPI#897: en un proveedor con control de pendientes, cuál es «de pendientes» respecto al envío
                    string dePendientes = pedido?.ControlPendientes == true ? ReglaControlPendientes.MotivoDePendientes(pedido, primero, diasPedidoDePendientes) : null;
                    envio.Candidatos.Add(new CandidatoPorAsignarDTO
                    {
                        Empresa = candidato.Empresa,
                        Pedido = candidato.Pedido,
                        Proveedor = candidato.Proveedor,
                        NombreProveedor = pedido?.NombreProveedor,
                        Motivo = MismoEnvio.Propios(grupo.FirstOrDefault(f => MismoEnvio.Propios(f).Any(c => c.Pedido == candidato.Pedido)))
                            .FirstOrDefault(c => c.Pedido == candidato.Pedido)?.Motivo ?? candidato.Motivo,
                        FechaEnvio = pedido?.FechaEnvio,
                        LineasPendientes = pedido?.LineasPendientes ?? 0,
                        LineasYaRecibidas = pedido?.LineasYaRecibidas ?? 0,
                        Sugerido = sugerido != null && pedido == sugerido,
                        DePendientes = dePendientes != null,
                        MotivoDePendientes = dePendientes
                    });
                }
                if (envio.ProveedorPropuesto != null)
                {
                    envio.NombreProveedorPropuesto = delEnvio.Where(p => string.Equals(p.Proveedor?.Trim(), envio.ProveedorPropuesto.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.NombreProveedor).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                }
                // 27/11/26: sin candidatos, pero quien expide ya está recordado (caso «postquam» → 451 sin pedidos en el aire): de quién es y por qué
                RemitenteRecordadoEnvio recordado = ReglaRemitenteRecordadoSinCandidatos.Explicar(grupo, decision.Candidatos, memoria, enAire);
                if (recordado != null)
                {
                    envio.ProveedorPropuesto = envio.ProveedorPropuesto ?? recordado.Proveedor;
                    envio.NombreProveedorPropuesto = envio.NombreProveedorPropuesto ?? recordado.NombreProveedor;
                    envio.Explicacion = envio.Explicacion ?? recordado.Explicacion;
                }
                resultado.Envios.Add(envio);
            }
            resultado.Envios = resultado.Envios.OrderByDescending(e => e.PrimerCorreo).ToList();
            resultado.PedidosEnAire = enAire
                .OrderBy(p => p.NombreProveedor ?? p.Proveedor).ThenByDescending(p => p.FechaEnvio ?? DateTime.MinValue)
                .Select(p =>
                {
                    List<int> envios = resultado.Envios.Where(e => e.Candidatos.Any(c => AsignadorPedidosCompra.Clave(c.Empresa, c.Pedido) == AsignadorPedidosCompra.Clave(p.Empresa, p.Pedido)))
                        .Select(e => e.Clave).ToList();
                    return new PedidoEnAireDTO
                    {
                        Empresa = p.Empresa?.Trim(),
                        Pedido = p.Pedido,
                        Proveedor = p.Proveedor?.Trim(),
                        NombreProveedor = p.NombreProveedor,
                        FechaEnvio = p.FechaEnvio,
                        LineasPendientes = p.LineasPendientes,
                        LineasYaRecibidas = p.LineasYaRecibidas,
                        EsCandidato = envios.Any(),
                        Envios = envios
                    };
                }).ToList();
            return resultado;
        }

        // POST api/Compras/AgenciasEntrada/Correos/12/Asignar?reasignar=false   { "Empresa": "1", "Pedido": 912001, "RecordarRemitente": true }
        /// <summary>
        /// Fase 3: Compras dice de qué pedido es un correo. Pone su fecha (la del envío) en las líneas enviadas y sin recibir del pedido, y
        /// también a los demás correos del mismo envío sin aplicar. 27/11/26: si el pedido YA se recibió (en el plazo del envío,
        /// <see cref="ReglaPedidoYaRecibido"/>), 200 sin tocar ninguna fecha (YaRecibido, FechaAlbaran) y el envío queda asignado a él como pedido
        /// real; 400 si no existe o si no está en el aire por otra causa (nunca se envió…); 404 si no existe el correo; 409 si
        /// ya está aplicado a otro pedido (con <paramref name="reasignar"/> = true se aplica al nuevo; el anterior NO se deshace); 503 sin
        /// Scripts/Issue857d. Funciona con el interruptor apagado.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Correos/{id:int}/Asignar")]
        [ResponseType(typeof(RespuestaAsignacionCorreoCompraDTO))]
        public async Task<IHttpActionResult> PostAsignar(int id, [FromBody] PeticionAsignarCorreoCompraDTO peticion, bool reasignar = false)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoOperacionCorreoCompra resultado = await crearAplicacion().Asignar(id, peticion, reasignar, usuario).ConfigureAwait(false);
            return Resultado(resultado, resultado.Respuesta);
        }

        // POST api/Compras/AgenciasEntrada/Correos/12/Descartar[?ignorarRemitente=true]
        /// <summary>
        /// Fase 3: el correo (y los demás de su envío sin aplicar) no es de ninguna compra. 409 si ya está aplicado. NestoAPI#857e: con
        /// <paramref name="ignorarRemitente"/>, además su remitente (dirección exacta) queda como «Ignorar» (rastro en Modificaciones);
        /// RemitenteIgnorado y Mensaje lo dicen.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Correos/{id:int}/Descartar")]
        [ResponseType(typeof(RespuestaDescarteCorreoCompraDTO))]
        public async Task<IHttpActionResult> PostDescartar(int id, bool ignorarRemitente = false)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoOperacionCorreoCompra resultado = await crearAplicacion().Descartar(id, usuario, ignorarRemitente).ConfigureAwait(false);
            return Resultado(resultado, new RespuestaDescarteCorreoCompraDTO
            {
                CorreoId = id,
                CorreosDelMismoEnvio = resultado.CorreosDelMismoEnvio,
                Mensaje = resultado.Mensaje,
                RemitenteIgnorado = resultado.RemitenteIgnorado
            });
        }

        private IHttpActionResult Resultado<T>(ResultadoOperacionCorreoCompra resultado, T ok)
        {
            switch (resultado.Codigo)
            {
                case CodigoOperacionCorreoCompra.Ok:
                    return Ok(ok);
                case CodigoOperacionCorreoCompra.SinTabla:
                    return Content(HttpStatusCode.ServiceUnavailable, resultado.Mensaje);
                case CodigoOperacionCorreoCompra.NoExiste:
                    return Content(HttpStatusCode.NotFound, resultado.Mensaje);
                case CodigoOperacionCorreoCompra.Conflicto:
                    return Content(HttpStatusCode.Conflict, resultado.Mensaje);
                default:
                    return BadRequest(resultado.Mensaje);
            }
        }

        // GET api/Compras/AgenciasEntrada/RemitentesPaquete
        /// <summary>Fase 3: la memoria de remitentes del paquete (quién expide → proveedor).</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/RemitentesPaquete")]
        [ResponseType(typeof(List<RemitentePaqueteRecordado>))]
        public async Task<IHttpActionResult> GetRemitentesPaquete()
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioAplicacionCorreosCompras.MENSAJE_SIN_SCRIPT);
            }
            return Ok(await repositorio.LeerRemitentesPaquete().ConfigureAwait(false));
        }

        // POST api/Compras/AgenciasEntrada/RemitentesPaquete   { "RemitentePaquete": "iberocel - paraiso", "Empresa": "1", "Proveedor": "846" }
        /// <summary>
        /// 27/11/26: enseña a mano de qué proveedor es quien expide el paquete (sin tener que asignar un correo). Normaliza el remitente como
        /// el lector; el proveedor tiene que existir (400 si no). Crea la fila o, si ya apuntaba a otro proveedor, lo cambia (ProveedorAnterior y
        /// Mensaje lo dicen); rastro en Modificaciones. Devuelve la fila. 503 sin Scripts/Issue857d.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/RemitentesPaquete")]
        [ResponseType(typeof(RespuestaRemitentePaqueteDTO))]
        public async Task<IHttpActionResult> PostRemitentePaquete([FromBody] PeticionRemitentePaqueteDTO peticion)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioAplicacionCorreosCompras.MENSAJE_SIN_SCRIPT);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            ResultadoOperacionCorreoCompra resultado = await crearAplicacion().EnsenarRemitentePaquete(peticion, usuario).ConfigureAwait(false);
            return Resultado(resultado, resultado.RemitentePaquete);
        }

        // DELETE api/Compras/AgenciasEntrada/RemitentesPaquete/12
        /// <summary>Fase 3: olvida un remitente del paquete (rastro en Modificaciones).</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/RemitentesPaquete/{id:int}")]
        public async Task<IHttpActionResult> DeleteRemitentePaquete(int id)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten() || !repositorio.AplicacionDisponible())
            {
                return Content(HttpStatusCode.ServiceUnavailable, ServicioAplicacionCorreosCompras.MENSAJE_SIN_SCRIPT);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            return await repositorio.BorrarRemitentePaquete(id, usuario).ConfigureAwait(false) ? (IHttpActionResult)Ok() : NotFound();
        }

        public const string MENSAJE_SIN_SCRIPT_ORIGENES = "Todavía no se puede: hay que lanzar Scripts/Issue857i_ComprasOrigenesAgencia.sql.";

        // GET api/Compras/AgenciasEntrada/OrigenesAgencia
        /// <summary>
        /// NestoAPI#857i: la memoria «agencia + delegación de origen → proveedor» (aprendida de lo asignado a mano y de los criterios fuertes del
        /// job), con el nombre del origen y del proveedor, las veces y la última. 503 sin Scripts/Issue857i.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/OrigenesAgencia")]
        [ResponseType(typeof(List<OrigenAgenciaRecordado>))]
        public async Task<IHttpActionResult> GetOrigenesAgencia()
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten() || !repositorio.OrigenesAgenciaDisponibles())
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT_ORIGENES);
            }
            return Ok(await repositorio.LeerOrigenesAgencia().ConfigureAwait(false));
        }

        // DELETE api/Compras/AgenciasEntrada/OrigenesAgencia/12
        /// <summary>NestoAPI#857i: olvida una fila de la memoria de orígenes (rastro en Modificaciones). 404 si no existe; 503 sin Scripts/Issue857i.</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/OrigenesAgencia/{id:int}")]
        public async Task<IHttpActionResult> DeleteOrigenAgencia(int id)
        {
            if (!PuedeVer(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO);
            }
            if (!repositorio.TablasExisten() || !repositorio.OrigenesAgenciaDisponibles())
            {
                return Content(HttpStatusCode.ServiceUnavailable, MENSAJE_SIN_SCRIPT_ORIGENES);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);
            return await repositorio.BorrarOrigenAgencia(id, usuario).ConfigureAwait(false) ? (IHttpActionResult)Ok() : NotFound();
        }

        // POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=7
        /// <summary>
        /// Fase 3: relee el buzón de esos días (sin tocar el delta) y vuelve a analizar los correos YA guardados sin aplicar ni descartar
        /// (agencia, códigos, entrega, remitente del paquete, candidatos contra los pedidos en el aire de ahora) y rehace los enlaces por
        /// mismo envío. No inserta, no aplica fechas ni avisa. Solo Dirección o Informática; 1-30 días; 503 sin claves o sin scripts.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Compras/AgenciasEntrada/Correos/Reanalizar")]
        [ResponseType(typeof(ResultadoReanalisisCorreos))]
        public async Task<IHttpActionResult> PostReanalizar(int dias = 7)
        {
            if (!PuedeLanzarHistorico(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_PERMISO_REANALIZAR);
            }
            if (dias < 1 || dias > ServicioCorreosSeguimientoCompras.DIAS_MAXIMOS_REANALISIS)
            {
                return BadRequest($"Los días tienen que estar entre 1 y {ServicioCorreosSeguimientoCompras.DIAS_MAXIMOS_REANALISIS}.");
            }
            ResultadoReanalisisCorreos resultado = await crearNucleo().Reanalizar(dias, DateTime.Now).ConfigureAwait(false);
            switch (resultado.Estado)
            {
                case EstadoPasadaCorreosSeguimiento.SinConfiguracion:
                    return Content(HttpStatusCode.ServiceUnavailable, "Faltan las claves del buzón de Compras en secretos.config.");
                case EstadoPasadaCorreosSeguimiento.SinTabla:
                    return Content(HttpStatusCode.ServiceUnavailable, ServicioAplicacionCorreosCompras.MENSAJE_SIN_SCRIPT);
                default:
                    return Ok(resultado);
            }
        }

        public const string MENSAJE_SIN_PERMISO_REANALIZAR = "Solo Dirección o Informática pueden volver a analizar los correos del buzón de Compras.";

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
                // NestoAPI#857f: sin enlace guardado (correos de antes), el que compone su agencia a partir del código
                Url = !string.IsNullOrWhiteSpace(fila.Url) ? fila.Url : EnlaceSeguimientoAgencia.De(fila.Agencia, fila.Codigo),
                Extracto = fila.Extracto,
                ProveedorPropuesto = fila.ProveedorPropuesto,
                PedidosCandidatos = AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos),
                FechaCaptura = fila.FechaCaptura,
                EmpresaReal = fila.EmpresaReal,
                PedidoReal = fila.PedidoReal,
                FechaAlbaranReal = fila.FechaAlbaranReal,
                NumAdjuntos = fila.NumAdjuntos,
                EntregaEstimadaDia = fila.EntregaEstimadaDia,
                EntregaEstimadaDesde = fila.EntregaEstimadaDesde,
                EntregaEstimadaHasta = fila.EntregaEstimadaHasta,
                DisponibleEstimado = fila.DisponibleEstimado,
                EsSalidaNuestra = fila.EsSalidaNuestra,
                RemitentePaquete = fila.RemitentePaquete,
                EmpresaAplicada = fila.EmpresaAplicada,
                PedidoAplicado = fila.PedidoAplicado,
                FechaRecepcionAplicada = fila.FechaRecepcionAplicada,
                AplicadoEl = fila.AplicadoEl,
                AplicadoPor = fila.AplicadoPor,
                Descartado = fila.Descartado,
                EligioSugerido = fila.EligioSugerido,
                AvisoProveedor = fila.AvisoProveedor,
                PedidoEnAdjunto = fila.PedidoEnAdjunto,
                AdjuntoLeido = fila.AdjuntoLeido,
                EstadoEnvio = fila.EstadoEnvio,
                OrigenEntrega = fila.OrigenEntrega,
                EntregaConsultadaEl = fila.EntregaConsultadaEl,
                LineasAplicadas = LineasAvisoCorreo.LeerAplicadas(fila.LineasAplicadas),
                OrigenAgencia = OrigenesAgenciaCorreo.Texto(OrigenesAgenciaCorreo.De(fila)),
                Explicacion = ReglaControlPendientes.ExplicacionDe(fila)
            };
            BultosKilosEnvio bultos = BultosKilosCorreo.De(fila.Agencia, fila.Extracto);
            dto.Bultos = bultos?.Bultos;
            dto.Kilos = bultos?.Kilos;
            LineasAvisoCorreo aviso = LineasAvisoCorreo.Leer(fila.LineasAviso);
            if (aviso?.Lineas != null && aviso.Lineas.Any())
            {
                dto.LineasAviso = aviso.Lineas;
                dto.LineasCasadas = aviso.Casadas != null && aviso.Casadas.Any() ? aviso.Casadas : null;
            }
            if (!string.IsNullOrWhiteSpace(fila.Adjuntos))
            {
                try
                {
                    dto.Adjuntos = JsonConvert.DeserializeObject<List<AdjuntoCorreoCompraDTO>>(fila.Adjuntos);
                }
                catch (JsonException)
                {
                    dto.Adjuntos = null;
                }
            }
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
            if (!dto.Seguimientos.Any() && (fila.Codigo != null || dto.Url != null))
            {
                dto.Seguimientos.Add(new SeguimientoCorreo { Codigo = fila.Codigo, Url = dto.Url });
            }
            return dto;
        }
    }
}
