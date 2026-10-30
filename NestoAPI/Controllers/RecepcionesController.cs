using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#559/#553: recibir mercancía con un solo contrato para todos los tipos: COMP (lo que llega de un
    /// proveedor, documento = proveedor) y REPO (una reposición entre almacenes, documento = traspaso). Igual que
    /// Recogidas/{tipo}/{numero}. Lo usan Ariadna y Nesto (por ejemplo, las tiendas para recibir sus reposiciones).
    ///
    /// <para>Va en un controlador propio, SIN el filtro EscrituraSoloAlmacen del resto de api/Almacen: aquí quién
    /// puede terminar lo decide el tipo de recepción (compras: Almacén, Compras o Dirección; reposiciones: quien tiene el
    /// almacén de destino en AlmacénPedidoVta, como hoy). Leer y comparar basta con estar identificado.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen/Recepciones")]
    public class RecepcionesController : ApiController
    {
        private readonly IServicioRecepciones servicio;
        private readonly IFichasProductoAlmacen fichas;
        private readonly IServicioLecturasRecepcion lecturas;
        private readonly IAvisosAlmacen avisos;

        /// <param name="fichas">Familia, subgrupo, tamaño y unidad de cada producto que ve el mozo. Sin él, solo el nombre.</param>
        /// <param name="lecturas">Ariadna#51 (corte 2): lo leído en curso, en el servidor. Sin él, …/Lecturas da 404 (como sin la tabla).</param>
        /// <param name="avisos">NestoAPI#646: «ha cambiado» a los demás equipos de la recepción (SignalR). Sin él, no se avisa.</param>
        /// <param name="sinEtiqueta">NestoAPI#762: avisar de lo que llega sin etiqueta. Sin él, …/SinEtiqueta da 404.</param>
        public RecepcionesController(IServicioRecepciones servicio, IFichasProductoAlmacen fichas = null,
            IServicioLecturasRecepcion lecturas = null, IAvisosAlmacen avisos = null, IServicioSinEtiquetaRecepcion sinEtiqueta = null)
        {
            this.servicio = servicio;
            this.fichas = fichas;
            this.lecturas = lecturas;
            this.avisos = avisos;
            this.sinEtiqueta = sinEtiqueta;
        }

        private readonly IServicioSinEtiquetaRecepcion sinEtiqueta;

        // GET api/Almacen/Recepciones?almacen=ALG&empresa=1
        /// <summary>Todo lo que está por recibir en el almacén, de todos los tipos, lo más antiguo primero.</summary>
        [HttpGet]
        [Route("")]
        [ResponseType(typeof(List<RecepcionPendienteDTO>))]
        public async Task<IHttpActionResult> GetPendientes(string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            return Ok(await servicio.LeerPendientes(Empresa(empresa), Almacen(almacen)).ConfigureAwait(false));
        }

        // GET api/Almacen/Recepciones/Buscar?codigo=8436620930427&almacen=ALG&empresa=1
        /// <summary>Lo pendiente de recibir que contiene un producto leído (hoy cada producto pendiente es de un solo proveedor).</summary>
        [HttpGet]
        [Route("Buscar")]
        [ResponseType(typeof(List<RecepcionPendienteDTO>))]
        public async Task<IHttpActionResult> GetBuscar(string codigo, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                return BadRequest("Falta el código de barras o el número de producto.");
            }
            return Ok(await servicio.Buscar(Empresa(empresa), Almacen(almacen), codigo.Trim()).ConfigureAwait(false));
        }

        // GET api/Almacen/Recepciones/COMP/65?almacen=ALG&empresa=1
        /// <summary>Lo que se espera recibir, con los códigos de barras para leerlo y si quien pregunta puede terminarla.</summary>
        [HttpGet]
        [Route("{tipo}/{documento}")]
        [ResponseType(typeof(RecepcionDTO))]
        public async Task<IHttpActionResult> GetRecepcion(string tipo, string documento, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            RecepcionDTO recepcion = await servicio.LeerEsperado(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), User).ConfigureAwait(false);
            if (recepcion == null)
            {
                return NotFound();
            }
            if (fichas != null)
            {
                // Ariadna#51: también lo ya recibido, para reconocer su código al leerlo y enseñar su foto
                await fichas.Completar(Empresa(empresa), recepcion.Lineas.Concat(recepcion.YaRecibidas ?? new List<LineaYaRecibidaDTO>()))
                    .ConfigureAwait(false);
            }
            return Ok(recepcion);
        }

        // POST api/Almacen/Recepciones/COMP/65/Casar?almacen=ALG&empresa=1   [{ Producto, Cantidad }]
        /// <summary>Compara lo contado con lo esperado: de más, de menos y lo que no se esperaba. No guarda nada.</summary>
        [HttpPost]
        [Route("{tipo}/{documento}/Casar")]
        [ResponseType(typeof(ResultadoCasarRecepcionDTO))]
        public async Task<IHttpActionResult> PostCasar(string tipo, string documento, [FromBody] List<LecturaRecepcionDTO> lecturas,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            ResultadoCasarRecepcionDTO resultado = await servicio
                .Casar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), lecturas).ConfigureAwait(false);
            return resultado == null ? (IHttpActionResult)NotFound() : Ok(resultado);
        }

        // POST api/Almacen/Recepciones/COMP/65/Terminar?almacen=ALG&empresa=1   { IdRecepcion, Lecturas: [{ Producto, Cantidad }], Dispositivo }
        /// <summary>
        /// Da por recibido lo contado (todo o nada). Compras: reparte entre los pedidos del proveedor, del más
        /// antiguo al más reciente, y crea los albaranes. Reenviar el mismo IdRecepcion no recibe dos veces.
        /// 403 si quien llama no puede terminar ese tipo de recepción.
        /// <para>?ensayo=true (solo Admin o Dirección): lo mismo dentro de una transacción que se deshace SIEMPRE, con las filas
        /// implicadas antes y después y el error real si falla. No guarda nada ni avisa a nadie (Scripts/Issue559_EnsayoRecepcion_Lanzar.ps1).</para>
        /// </summary>
        [HttpPost]
        [Route("{tipo}/{documento}/Terminar")]
        [ResponseType(typeof(ResultadoTerminarRecepcionDTO))]
        public async Task<IHttpActionResult> PostTerminar(string tipo, string documento, [FromBody] TerminarRecepcionDTO terminar,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, bool ensayo = false)
        {
            try
            {
                ResultadoTerminarRecepcionDTO resultado = await servicio
                    .Terminar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), terminar, User, ensayo).ConfigureAwait(false);
                // Ariadna#51: las demás pantallas de esta recepción saben que ya está (al pedir lo leído les sale vacío o lo que queda)
                if (!ensayo && resultado != null && !resultado.YaEstabaTerminada)
                {
                    Avisar(tipo, documento, almacen, terminar?.Dispositivo);
                }
                return Ok(resultado);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Identificado pero sin permiso para este tipo: 403, no 401 (con 401 la app intentaría refrescar el token)
                return Prohibido(ex);
            }
        }

        // GET api/Almacen/Recepciones/REPO/81119/Lecturas?almacen=ALC&empresa=1&dispositivo=PDA-03
        /// <summary>
        /// Ariadna#51 (corte 2): lo leído de la recepción en curso, de todos los equipos y ya sumado, con su IdRecepcion (el que
        /// se manda al terminar). Si no hay nada en curso, la abre: IdRecepcion nuevo y listas vacías. 403 si quien llama no puede
        /// recibir eso en ese almacén. 404 si el servidor todavía no lo guarda (los clientes siguen como antes).
        /// </summary>
        [HttpGet]
        [Route("{tipo}/{documento}/Lecturas")]
        [ResponseType(typeof(LecturasRecepcionEnCursoDTO))]
        public async Task<IHttpActionResult> GetLecturas(string tipo, string documento, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string dispositivo = null)
        {
            if (lecturas == null)
            {
                return SinLecturasEnServidor();
            }
            try
            {
                LecturasRecepcionEnCursoDTO enCurso = await lecturas
                    .Leer(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), User, Dispositivo(dispositivo)).ConfigureAwait(false);
                return enCurso == null ? SinLecturasEnServidor() : Ok(enCurso);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Almacen/Recepciones/REPO/81119/Lecturas?almacen=ALC&empresa=1
        //   [{ IdCliente, Producto | CodigoBulto, Cantidad (+ al leer, − al quitar), Dispositivo, Fecha }]
        /// <summary>
        /// Ariadna#51 (corte 2): apunta lecturas en la recepción en curso (se suman a las de los demás equipos) y devuelve lo
        /// mismo que el GET, ya con todo sumado. Reenviar el mismo IdCliente no lo apunta dos veces. Avisa por SignalR a los
        /// demás equipos del grupo «recepcion:{tipo}:{documento}:{almacen}» (no al que lo manda).
        /// </summary>
        [HttpPost]
        [Route("{tipo}/{documento}/Lecturas")]
        [ResponseType(typeof(LecturasRecepcionEnCursoDTO))]
        public async Task<IHttpActionResult> PostLecturas(string tipo, string documento, [FromBody] List<LecturaRecepcionEnCursoDTO> nuevas,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (lecturas == null)
            {
                return SinLecturasEnServidor();
            }
            // Equipos[].Dispositivo es el nombre del equipo que manda la cabecera X-Dispositivo (el mismo que excluye el aviso)
            string cabecera = Cabecera(AvisosAlmacen.CABECERA_DISPOSITIVO);
            if (cabecera != null)
            {
                foreach (LecturaRecepcionEnCursoDTO nueva in (nuevas ?? new List<LecturaRecepcionEnCursoDTO>()).Where(l => l != null))
                {
                    nueva.Dispositivo = cabecera;
                }
            }
            try
            {
                LecturasRecepcionEnCursoDTO enCurso = await lecturas
                    .Anotar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), nuevas, User).ConfigureAwait(false);
                if (enCurso == null)
                {
                    return SinLecturasEnServidor();
                }
                Avisar(tipo, documento, almacen, nuevas?.Select(l => l?.Dispositivo).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)));
                return Ok(enCurso);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // DELETE api/Almacen/Recepciones/REPO/81119/Lecturas?almacen=ALC&empresa=1&dispositivo=PDA-03
        /// <summary>
        /// Ariadna#51 (corte 2): «Descartar lo leído». Lo en curso queda como descartado (no se borra) y se abre la recepción con
        /// un IdRecepcion nuevo; devuelve lo mismo que el GET (listas vacías). Avisa por SignalR a los demás equipos.
        /// </summary>
        [HttpDelete]
        [Route("{tipo}/{documento}/Lecturas")]
        [ResponseType(typeof(LecturasRecepcionEnCursoDTO))]
        public async Task<IHttpActionResult> DeleteLecturas(string tipo, string documento, string almacen = Constantes.Almacenes.ALGETE,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string dispositivo = null)
        {
            if (lecturas == null)
            {
                return SinLecturasEnServidor();
            }
            try
            {
                LecturasRecepcionEnCursoDTO enCurso = await lecturas
                    .Descartar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), User, Dispositivo(dispositivo)).ConfigureAwait(false);
                if (enCurso == null)
                {
                    return SinLecturasEnServidor();
                }
                Avisar(tipo, documento, almacen, Dispositivo(dispositivo));
                return Ok(enCurso);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Prohibido(ex);
            }
        }

        // POST api/Almacen/Recepciones/REPO/81198/SinEtiqueta?almacen=REI&empresa=1   { Producto, Motivo, Comentario, Unidades, Dispositivo, ClaveIdempotencia }
        // ClaveIdempotencia (opcional, revisión 30/10): si ya se procesó, 200 con la respuesta de la primera vez y sin volver a avisar.
        /// <summary>
        /// NestoAPI#762: ha llegado una unidad sin etiqueta (se recibe solo escaneando, RecepcionDTO.SoloEscaneando). Avisa al
        /// origen y a Compras con el motivo (NoLleva, NoSeLee u Otro con Comentario). NO cuenta la unidad: la cuenta la app con
        /// sus lecturas tras el 200. 400 con el motivo para el usuario (no es REPO, motivo no válido, el producto no viene…);
        /// 404 si la reposición no está pendiente en ese almacén.
        /// </summary>
        [HttpPost]
        [Route("{tipo}/{documento}/SinEtiqueta")]
        [ResponseType(typeof(ResultadoSinEtiquetaRecepcionDTO))]
        public async Task<IHttpActionResult> PostSinEtiqueta(string tipo, string documento, [FromBody] SinEtiquetaRecepcionDTO peticion,
            string almacen = Constantes.Almacenes.ALGETE, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            if (sinEtiqueta == null)
            {
                return NotFound();
            }
            try
            {
                return Ok(await sinEtiqueta.Apuntar(tipo, Empresa(empresa), Almacen(almacen), documento?.Trim(), peticion,
                    UsuarioAuditoriaHelper.Resolver(User, null)).ConfigureAwait(false));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (KeyNotFoundException ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                // Otro reintento con la misma ClaveIdempotencia lleva más de 30 s procesándose
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Conflict, ex.Message));
            }
        }

        private IHttpActionResult SinLecturasEnServidor()
        {
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.NotFound, ServicioLecturasRecepcion.MENSAJE_SIN_TABLA));
        }

        private IHttpActionResult Prohibido(UnauthorizedAccessException ex)
        {
            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Forbidden, ex.Message));
        }

        /// <summary>
        /// NestoAPI#646 / Ariadna#51: a los demás equipos de la recepción, «ha cambiado». Al que lo hace no: su conexión llega en la
        /// cabecera X-Conexion-SignalR y, si no, se excluyen las de su dispositivo. Nunca rompe la petición.
        /// </summary>
        private void Avisar(string tipo, string documento, string almacen, string dispositivoDeLaPeticion)
        {
            if (avisos == null)
            {
                return;
            }
            try
            {
                string dispositivo = Cabecera(AvisosAlmacen.CABECERA_DISPOSITIVO) ?? dispositivoDeLaPeticion?.Trim();
                avisos.HaCambiado(AvisosAlmacen.GrupoRecepcion(tipo, documento, Almacen(almacen)),
                    UsuarioAuditoriaHelper.ParaAuditoria(UsuarioAuditoriaHelper.Resolver(User, null)),
                    Cabecera(AvisosAlmacen.CABECERA_CONEXION), string.IsNullOrWhiteSpace(dispositivo) ? null : dispositivo);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(ex);
            }
        }

        /// <summary>El dispositivo de la petición: el parámetro o, si no, la cabecera X-Dispositivo.</summary>
        private string Dispositivo(string dispositivo)
        {
            return string.IsNullOrWhiteSpace(dispositivo) ? Cabecera(AvisosAlmacen.CABECERA_DISPOSITIVO) : dispositivo.Trim();
        }

        private string Cabecera(string nombre)
        {
            if (Request?.Headers == null || !Request.Headers.TryGetValues(nombre, out IEnumerable<string> valores))
            {
                return null;
            }
            string valor = valores?.FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(valor) ? null : valor;
        }

        private static string Almacen(string almacen)
        {
            return string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }
    }
}
