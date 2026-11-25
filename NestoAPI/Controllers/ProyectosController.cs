using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Proyectos;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.Proyectos;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#901 (primer corte): PROYECTOS (convocatorias de cursos del SEPE, eventos, ferias, campañas). Alta y ciclo de vida,
    /// reparto de apuntes ya contabilizados (cuentas 6 y 7) entre proyectos por importe y cuenta de explotación (con Excel y comparativa).
    /// Administración, Dirección e Informática; reabrir, solo Dirección. Sin las tablas (Scripts/Issue901_Proyectos.sql), 503.
    /// Errores: 400/403/404/409/503 con { Message, Codigo }.
    /// </summary>
    [Authorize]
    public class ProyectosController : ApiController
    {
        public const string MENSAJE_SIN_PERMISO = "Solo Administración, Dirección o Informática pueden ver y cambiar los proyectos y sus imputaciones.";
        public const string MENSAJE_SOLO_DIRECCION = "Solo Dirección puede reabrir un proyecto cerrado.";

        private readonly ServicioProyectos servicio;

        public ProyectosController()
        {
            servicio = new ServicioProyectos(RepositorioProyectos.DesdeConfiguracion());
        }

        internal ProyectosController(ServicioProyectos servicio)
        {
            this.servicio = servicio;
        }

        internal static bool TienePermiso(IPrincipal usuario) =>
            usuario != null && !ValidadorAccesoCliente.EsClienteTienda(usuario)
            && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        internal static bool PuedeReabrir(IPrincipal usuario) =>
            usuario != null && !ValidadorAccesoCliente.EsClienteTienda(usuario) && usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);

        private string Usuario => UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);

        private IHttpActionResult Prohibido(string mensaje = MENSAJE_SIN_PERMISO) =>
            Content(HttpStatusCode.Forbidden, new RespuestaErrorProyectoDTO { Message = mensaje, Codigo = "SIN_PERMISO" });

        private IHttpActionResult Respuesta<T>(ResultadoProyectos<T> resultado, Func<T, IHttpActionResult> ok = null)
        {
            if (resultado.Ok)
            {
                return ok != null ? ok(resultado.Valor) : Ok(resultado.Valor);
            }
            return Error(resultado.Error);
        }

        internal IHttpActionResult Error(ErrorProyecto error)
        {
            HttpStatusCode estado;
            switch (error.Tipo)
            {
                case TipoErrorProyecto.NoExiste: estado = HttpStatusCode.NotFound; break;
                case TipoErrorProyecto.Conflicto: estado = HttpStatusCode.Conflict; break;
                case TipoErrorProyecto.Prohibido: estado = HttpStatusCode.Forbidden; break;
                case TipoErrorProyecto.SinTabla: estado = HttpStatusCode.ServiceUnavailable; break;
                default: estado = HttpStatusCode.BadRequest; break;
            }
            return Content(estado, new RespuestaErrorProyectoDTO { Message = error.Mensaje, Codigo = error.Codigo });
        }

        private static DatosProyecto Datos(ProyectoDTO p) => p == null ? null : new DatosProyecto
        {
            Empresa = p.Empresa,
            Codigo = p.Codigo,
            Nombre = p.Nombre,
            Tipo = p.Tipo,
            FechaDesde = p.FechaDesde,
            FechaHasta = p.FechaHasta,
            Producto = p.Producto,
            EventoId = p.EventoId,
            PresupuestoIngresos = p.PresupuestoIngresos,
            PresupuestoGastos = p.PresupuestoGastos,
            Comentarios = p.Comentarios
        };

        // GET api/Proyectos?empresa=1&soloAbiertos=true&tipo=&filtro=
        [HttpGet]
        [Route("api/Proyectos")]
        [ResponseType(typeof(List<ProyectoDTO>))]
        public async Task<IHttpActionResult> GetProyectos(string empresa = null, bool soloAbiertos = true, string tipo = null, string filtro = null)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Listar(empresa, soloAbiertos, tipo, filtro).ConfigureAwait(false));
        }

        // GET api/Proyectos/5
        [HttpGet]
        [Route("api/Proyectos/{id:int}")]
        [ResponseType(typeof(ProyectoDTO))]
        public async Task<IHttpActionResult> GetProyecto(int id)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Leer(id).ConfigureAwait(false));
        }

        // POST api/Proyectos
        [HttpPost]
        [Route("api/Proyectos")]
        [ResponseType(typeof(ProyectoDTO))]
        public async Task<IHttpActionResult> PostProyecto([FromBody] ProyectoDTO proyecto)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            ResultadoProyectos<ProyectoDTO> resultado = await servicio.Crear(Datos(proyecto), Usuario).ConfigureAwait(false);
            return Respuesta(resultado, creado => Content(HttpStatusCode.Created, creado));
        }

        // PUT api/Proyectos/5
        [HttpPut]
        [Route("api/Proyectos/{id:int}")]
        [ResponseType(typeof(ProyectoDTO))]
        public async Task<IHttpActionResult> PutProyecto(int id, [FromBody] ProyectoDTO proyecto)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Modificar(id, Datos(proyecto), Usuario).ConfigureAwait(false));
        }

        // POST api/Proyectos/5/Cerrar
        [HttpPost]
        [Route("api/Proyectos/{id:int}/Cerrar")]
        [ResponseType(typeof(ProyectoDTO))]
        public async Task<IHttpActionResult> PostCerrar(int id)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Cerrar(id, Usuario).ConfigureAwait(false));
        }

        // POST api/Proyectos/5/Reabrir (solo Dirección)
        [HttpPost]
        [Route("api/Proyectos/{id:int}/Reabrir")]
        [ResponseType(typeof(ProyectoDTO))]
        public async Task<IHttpActionResult> PostReabrir(int id)
        {
            if (!PuedeReabrir(User))
            {
                return Prohibido(TienePermiso(User) ? MENSAJE_SOLO_DIRECCION : MENSAJE_SIN_PERMISO);
            }
            return Respuesta(await servicio.Reabrir(id, Usuario).ConfigureAwait(false));
        }

        // DELETE api/Proyectos/5 (solo abierto y sin imputaciones)
        [HttpDelete]
        [Route("api/Proyectos/{id:int}")]
        public async Task<IHttpActionResult> DeleteProyecto(int id)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Borrar(id, Usuario).ConfigureAwait(false), _ => StatusCode(HttpStatusCode.NoContent));
        }

        // GET api/Proyectos/ApuntesPorImputar?empresa=1&desde=&hasta=&cuenta=623,705&proveedor=&documento=&texto=&soloPendientes=true&pagina=0&tope=500
        [HttpGet]
        [Route("api/Proyectos/ApuntesPorImputar")]
        [ResponseType(typeof(ApuntesPorImputarDTO))]
        public async Task<IHttpActionResult> GetApuntesPorImputar(string empresa = null, DateTime? desde = null, DateTime? hasta = null, string cuenta = null,
            string proveedor = null, string documento = null, string texto = null, bool soloPendientes = true, int pagina = 0, int? tope = null)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.ApuntesPorImputar(new PeticionApuntesPorImputar
            {
                Empresa = empresa,
                Desde = desde,
                Hasta = hasta,
                Cuenta = cuenta,
                Proveedor = proveedor,
                Documento = documento,
                Texto = texto,
                SoloPendientes = soloPendientes,
                Pagina = pagina,
                Tope = tope
            }).ConfigureAwait(false));
        }

        // GET api/Proyectos/Apuntes/123456/Imputaciones?empresa=1
        [HttpGet]
        [Route("api/Proyectos/Apuntes/{numeroOrden:int}/Imputaciones")]
        [ResponseType(typeof(ImputacionesApunteDTO))]
        public async Task<IHttpActionResult> GetImputacionesApunte(int numeroOrden, string empresa = null)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.ImputacionesDeApunte(empresa, numeroOrden).ConfigureAwait(false));
        }

        // PUT api/Proyectos/Apuntes/123456/Imputaciones  { "Empresa": "1", "ImporteApunte": 120.00, "Imputaciones": [{ "ProyectoId": 1, "Importe": 60, "Comentario": "" }] }
        [HttpPut]
        [Route("api/Proyectos/Apuntes/{numeroOrden:int}/Imputaciones")]
        [ResponseType(typeof(ImputacionesApunteDTO))]
        public async Task<IHttpActionResult> PutImputacionesApunte(int numeroOrden, [FromBody] PeticionImputacionesApunteDTO peticion)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.RepartirApunte(numeroOrden, peticion, Usuario).ConfigureAwait(false));
        }

        // GET api/Proyectos/5/CuentaExplotacion
        [HttpGet]
        [Route("api/Proyectos/{id:int}/CuentaExplotacion")]
        [ResponseType(typeof(CuentaExplotacionProyectoDTO))]
        public async Task<IHttpActionResult> GetCuentaExplotacion(int id)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.CuentaExplotacion(id).ConfigureAwait(false));
        }

        // GET api/Proyectos/5/CuentaExplotacion/Excel
        [HttpGet]
        [Route("api/Proyectos/{id:int}/CuentaExplotacion/Excel")]
        public async Task<IHttpActionResult> GetCuentaExplotacionExcel(int id)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            ResultadoProyectos<CuentaExplotacionProyectoDTO> resultado = await servicio.CuentaExplotacion(id).ConfigureAwait(false);
            return Respuesta(resultado, cuenta => ResponseMessage(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new GeneradorExcelCuentaExplotacionProyecto().GenerarExcel(cuenta)
            }));
        }

        // GET api/Proyectos/Comparativa?ids=1,2,3   o   ?producto=IMPE0210[&empresa=1]
        [HttpGet]
        [Route("api/Proyectos/Comparativa")]
        [ResponseType(typeof(ComparativaProyectosDTO))]
        public async Task<IHttpActionResult> GetComparativa(string ids = null, string producto = null, string empresa = null)
        {
            if (!TienePermiso(User))
            {
                return Prohibido();
            }
            return Respuesta(await servicio.Comparativa(ids, producto, empresa).ConfigureAwait(false));
        }
    }

    /// <summary>NestoAPI#901: cuerpo de los errores de api/Proyectos.</summary>
    public class RespuestaErrorProyectoDTO
    {
        public string Message { get; set; }
        public string Codigo { get; set; }
    }
}
