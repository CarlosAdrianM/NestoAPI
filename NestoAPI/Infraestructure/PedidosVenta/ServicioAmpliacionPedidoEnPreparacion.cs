using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Transactions;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    public enum AccionAmpliacion
    {
        /// <summary>Se guarda como siempre.</summary>
        Seguir,
        /// <summary>Lo añadido va en un pedido nuevo (lo crea la API en la misma operación).</summary>
        PedidoNuevo,
        /// <summary>Se guarda en el mismo pedido y, ya guardado, se pregunta a Almacén (urgente).</summary>
        PedirAlmacen,
        /// <summary>Se guarda como siempre y, ya guardado, se avisa a Almacén (el pedido hoy no sale).</summary>
        AvisarAlmacen
    }

    /// <summary>Lo que ha decidido <see cref="ServicioAmpliacionPedidoEnPreparacion.Comprobar"/> para este PUT.</summary>
    public class ComprobacionAmpliacion
    {
        public static ComprobacionAmpliacion Seguir => new ComprobacionAmpliacion { Accion = AccionAmpliacion.Seguir };

        public AccionAmpliacion Accion { get; set; }
        public EvaluacionAmpliacion Evaluacion { get; set; }
        /// <summary>El cliente y su nombre, para los avisos.</summary>
        public string NombreCliente { get; set; }
        /// <summary>Lo que el correo de la modificación pinta como «pendiente de que Almacén diga si entra en el envío de hoy».</summary>
        public HashSet<string> ProductosPendientesDeAlmacen { get; set; }
    }

    /// <summary>El desenlace de pasar lo añadido a un pedido nuevo.</summary>
    public class ResultadoSeparacion
    {
        /// <summary>Lo que contestó el guardado (o la creación) que ha fallado, tal cual. Null si ha ido bien.</summary>
        public IHttpActionResult Error { get; set; }
        public string MensajeError { get; set; }
        public RespuestaModificacionPedidoDTO Respuesta { get; set; }
        public int? PedidoNuevo { get; set; }
    }

    /// <summary>La respuesta de Almacén (POST …/AmpliacionesEnPreparacion/{id}/Respuesta).</summary>
    public class ResponderAmpliacionRequest
    {
        /// <summary>True = «Sí, lo metemos»; false = «No llega».</summary>
        public bool Entra { get; set; }
        public string Motivo { get; set; }
        /// <summary>Solo si no viene en el token (como en los demás endpoints de Nesto).</summary>
        public string Usuario { get; set; }
    }

    public class ResultadoRespuestaAmpliacion
    {
        public bool NoEncontrada { get; set; }
        /// <summary>409: ya estaba contestada (o la contestó otro a la vez).</summary>
        public string Conflicto { get; set; }
        public AmpliacionPedidoDTO Solicitud { get; set; }
        /// <summary>Lo que se le enseña a quien contesta.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>Pasar a un pedido nuevo las líneas de una solicitud (lo que hace el «No» y el vencimiento). Sustituible en los tests.</summary>
    public interface IPasoAmpliacionAPedidoNuevo
    {
        Task<ResultadoSeparacion> Pasar(AmpliacionPedidoDTO solicitud);
    }

    /// <summary>Sacar el picking de un pedido (el «Sí»). Devuelve el picking de esas líneas o el motivo por el que no lo tienen.</summary>
    public interface ISacadorPickingAmpliacion
    {
        Task<(int? Picking, string Motivo)> Sacar(string empresa, int pedido, IReadOnlyCollection<int> numerosOrden);
    }

    /// <summary>
    /// Caso 931007 (Carlos 18/11/26): añadir líneas a un pedido EN PREPARACIÓN no pasa en silencio. Todos los caminos que añaden líneas a un
    /// pedido que existe (detalle de Nesto, «Modificar con plantilla», UnirPedidos, NestoApp) acaban en PutPedidoVenta, que llama a
    /// <see cref="Comprobar"/> antes de tocar nada:
    /// <list type="bullet">
    /// <item>sin elección (NestoApp, Nesto viejo, UnirPedidos de dos pedidos que existen): 409 PEDIDO_EN_PREPARACION;</item>
    /// <item>PedidoNuevo: en una transacción, el original se guarda SIN lo añadido (el resto del PUT sí) y lo añadido se crea en un pedido
    /// nuevo por el POST de siempre (<see cref="Separar"/>);</item>
    /// <item>PedirAlmacen (solo si la agencia no lo tiene): se guarda en el mismo pedido, se apunta la solicitud y se pregunta a Almacén
    /// (urgente). «Sí» saca el picking del pedido (el envío se suma al que está en curso); «No», o <see cref="AmpliacionPedidoEnPreparacion.ESPERA_RESPUESTA_ALMACEN"/>
    /// sin respuesta, lo pasa a un pedido nuevo con el mismo núcleo.</item>
    /// </list>
    /// </summary>
    public class ServicioAmpliacionPedidoEnPreparacion
    {
        public const string USUARIO_SISTEMA = "NestoAPI (ampliación sin respuesta)";

        private readonly ILectorAmpliacionPedido lector;
        private readonly IRepositorioAmpliacionesPedido repositorio;
        private readonly IAvisosAmpliacionPedido avisos;
        private readonly Func<DateTime> ahora;
        private readonly Action<int> programarVencimiento;
        private readonly Func<IPasoAmpliacionAPedidoNuevo> paso;
        private readonly Func<ISacadorPickingAmpliacion> sacador;

        public ServicioAmpliacionPedidoEnPreparacion(ILectorAmpliacionPedido lector, IRepositorioAmpliacionesPedido repositorio,
            IAvisosAmpliacionPedido avisos, Func<DateTime> ahora = null, Action<int> programarVencimiento = null,
            Func<IPasoAmpliacionAPedidoNuevo> paso = null, Func<ISacadorPickingAmpliacion> sacador = null)
        {
            this.lector = lector;
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.avisos = avisos ?? throw new ArgumentNullException(nameof(avisos));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.programarVencimiento = programarVencimiento ?? AmpliacionesPedidoJobs.Programar;
            this.paso = paso ?? (() => new PasoAmpliacionAPedidoNuevo());
            this.sacador = sacador ?? (() => new SacadorPickingAmpliacion());
        }

        /// <summary>El de producción para los endpoints de Almacén y el job (sin lector: no comprueban PUT).</summary>
        public static ServicioAmpliacionPedidoEnPreparacion ParaRespuestas()
            => new ServicioAmpliacionPedidoEnPreparacion(null, new RepositorioAmpliacionesPedido(), new AvisosAmpliacionPedido());

        /// <summary>
        /// Antes de tocar nada en el PUT. Lanza el 409 PEDIDO_EN_PREPARACION (sin elección, o PedirAlmacen cuando no cabe), 400 si la
        /// elección no vale y 503 si se pide a Almacén sin la tabla. Sin líneas nuevas de producto no lee nada.
        /// </summary>
        public async Task<ComprobacionAmpliacion> Comprobar(PedidoVentaDTO pedido)
        {
            if (pedido == null || pedido.AmpliacionResuelta || lector == null
                || AmpliacionPedidoEnPreparacion.LineasNuevasDeProducto(pedido.Lineas).Count == 0)
            {
                return ComprobacionAmpliacion.Seguir;
            }
            string opcion = OpcionesAmpliacionEnPreparacion.Normalizar(pedido.AmpliacionEnPreparacion, out bool valida);
            if (!valida)
            {
                throw new NestoBusinessException(TextosAmpliacionPedido.ERROR_OPCION_NO_VALIDA,
                    new ErrorContext { ErrorCode = "AMPLIACION_OPCION_NO_VALIDA", Empresa = pedido.empresa?.Trim(), Pedido = pedido.numero });
            }
            string empresa = pedido.empresa?.Trim();
            List<LinPedidoVta> guardadas = await lector.LeerLineas(empresa, pedido.numero).ConfigureAwait(true);
            _ = AmpliacionPedidoEnPreparacion.PickingsEnPreparacion(guardadas, ahora().Date, out bool enPreparacion);
            if (!enPreparacion)
            {
                return ComprobacionAmpliacion.Seguir;
            }
            bool notaEntrega = pedido.notaEntrega;
            string motivoNoSale = await lector.MotivoNoSaleHoy(empresa, pedido.numero).ConfigureAwait(true);
            List<EnvioAmpliacionPedido> envios = string.IsNullOrWhiteSpace(motivoNoSale)
                ? await lector.LeerEnvios(empresa, pedido.numero).ConfigureAwait(true)
                : new List<EnvioAmpliacionPedido>();
            EvaluacionAmpliacion evaluacion = AmpliacionPedidoEnPreparacion.Evaluar(pedido.numero, guardadas, notaEntrega, pedido.Lineas, envios,
                pedido.ruta, motivoNoSale, ahora().Date);

            switch (evaluacion.Decision)
            {
                case DecisionAmpliacion.Nada:
                    return ComprobacionAmpliacion.Seguir;
                case DecisionAmpliacion.NoSaleHoy:
                    return new ComprobacionAmpliacion { Accion = AccionAmpliacion.AvisarAlmacen, Evaluacion = evaluacion };
            }
            if (opcion == null)
            {
                throw AmpliacionPedidoEnPreparacion.Excepcion(empresa, pedido.numero, evaluacion);
            }
            if (opcion == OpcionesAmpliacionEnPreparacion.PEDIDO_NUEVO)
            {
                return new ComprobacionAmpliacion { Accion = AccionAmpliacion.PedidoNuevo, Evaluacion = evaluacion };
            }
            if (!evaluacion.PuedePedirAlmacen)
            {
                throw AmpliacionPedidoEnPreparacion.Excepcion(empresa, pedido.numero, evaluacion);
            }
            if (!repositorio.TablaExiste())
            {
                throw new NestoBusinessException(TextosAmpliacionPedido.ERROR_SIN_TABLA,
                    new ErrorContext { ErrorCode = "AMPLIACION_SIN_TABLA", Empresa = empresa, Pedido = pedido.numero })
                {
                    StatusCode = HttpStatusCode.ServiceUnavailable
                };
            }
            return new ComprobacionAmpliacion { Accion = AccionAmpliacion.PedirAlmacen, Evaluacion = evaluacion };
        }

        /// <summary>
        /// EL NÚCLEO del pedido nuevo (lo usan el PUT con PedidoNuevo, el «No» de Almacén y el vencimiento): en UNA transacción, el original
        /// se guarda sin <paramref name="quitar"/> (con <paramref name="guardar"/>, el PUT) y <paramref name="nuevo"/> se crea con
        /// <paramref name="crear"/> (el POST de siempre: validación, portes, modo por defecto, correo). Si algo no se guarda, no queda nada.
        /// </summary>
        public static async Task<ResultadoSeparacion> Separar(PedidoVentaDTO original, IEnumerable<LineaPedidoVentaDTO> quitar, PedidoVentaDTO nuevo,
            Func<PedidoVentaDTO, Task<IHttpActionResult>> guardar, Func<PedidoVentaDTO, Task<IHttpActionResult>> crear)
        {
            foreach (LineaPedidoVentaDTO linea in (quitar ?? Enumerable.Empty<LineaPedidoVentaDTO>()).ToList())
            {
                _ = original.Lineas.Remove(linea);
            }
            original.AmpliacionResuelta = true;
            original.AmpliacionEnPreparacion = null;

            var opciones = new TransactionOptions { Timeout = TimeSpan.FromMinutes(5) };
            using (var transaccion = new TransactionScope(TransactionScopeOption.Required, opciones, TransactionScopeAsyncFlowOption.Enabled))
            {
                IHttpActionResult guardado = await guardar(original).ConfigureAwait(true);
                string errorGuardado = GestorPedidosVenta.MensajeDeErrorDelGuardado(guardado);
                if (errorGuardado != null)
                {
                    return new ResultadoSeparacion { Error = guardado, MensajeError = $"No se ha guardado el pedido {original.numero}: {errorGuardado}" };
                }
                IHttpActionResult creado = await crear(nuevo).ConfigureAwait(true);
                if (!(creado is CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO> conContenido) || conContenido.Content == null)
                {
                    string errorCreado = GestorPedidosVenta.MensajeDeErrorDelGuardado(creado);
                    return new ResultadoSeparacion { Error = creado, MensajeError = $"No se ha creado el pedido nuevo: {errorCreado}" };
                }
                transaccion.Complete();

                int numeroNuevo = conContenido.Content.numero;
                RespuestaModificacionPedidoDTO respuesta = (guardado as OkNegotiatedContentResult<RespuestaModificacionPedidoDTO>)?.Content
                    ?? new RespuestaModificacionPedidoDTO();
                respuesta.PedidoNuevo = numeroNuevo;
                var aviso = new AvisoPedidoDTO
                {
                    Tipo = AmpliacionPedidoEnPreparacion.TIPO_AVISO_PEDIDO_NUEVO,
                    Mensaje = AmpliacionPedidoEnPreparacion.AvisoPedidoNuevo(original, numeroNuevo, nuevo.Lineas),
                    Datos = new { PedidoNuevo = numeroNuevo, Pedido = original.numero }
                };
                respuesta.Avisos.Add(aviso);
                // UnirPedidos devuelve el DTO del original: que lleve también el número y el aviso
                original.PedidoNuevoAmpliacion = numeroNuevo;
                original.AvisosGrabacion = original.AvisosGrabacion ?? new List<AvisoPedidoDTO>();
                original.AvisosGrabacion.Add(aviso);
                return new ResultadoSeparacion { Respuesta = respuesta, PedidoNuevo = numeroNuevo };
            }
        }

        /// <summary>El PUT con PedidoNuevo: el pedido nuevo con las líneas añadidas del propio PUT (las mismas, ya sin el original).</summary>
        public static Task<ResultadoSeparacion> SepararDelPut(PedidoVentaDTO pedido, ComprobacionAmpliacion comprobacion,
            Func<PedidoVentaDTO, Task<IHttpActionResult>> guardar, Func<PedidoVentaDTO, Task<IHttpActionResult>> crear, DateTime hoy)
        {
            List<LineaPedidoVentaDTO> lineas = comprobacion.Evaluacion.LineasAMover;
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                _ = pedido.Lineas.Remove(linea);
            }
            PedidoVentaDTO nuevo = AmpliacionPedidoEnPreparacion.ConstruirPedidoNuevo(pedido, lineas, pedido.Usuario, hoy, pedido.CreadoSinPasarValidacion);
            return Separar(pedido, Enumerable.Empty<LineaPedidoVentaDTO>(), nuevo, guardar, crear);
        }

        /// <summary>
        /// Ya guardado el PUT. Con PedirAlmacen: apunta la solicitud, programa el vencimiento y pregunta a Almacén (urgente). Con AvisarAlmacen:
        /// campana normal a Almacén. Nunca lanza: devuelve el aviso para la respuesta (y el correo).
        /// </summary>
        /// <param name="creadas">Cada línea nueva del PUT con la fila que se ha guardado (ya con su Nº de orden).</param>
        public async Task<AvisoPedidoDTO> DespuesDeGuardar(PedidoVentaDTO pedido, ComprobacionAmpliacion comprobacion,
            IEnumerable<KeyValuePair<LineaPedidoVentaDTO, LinPedidoVta>> creadas, string nombreCliente)
        {
            if (pedido == null || comprobacion == null || comprobacion.Evaluacion == null)
            {
                return null;
            }
            EvaluacionAmpliacion evaluacion = comprobacion.Evaluacion;
            string productos = AmpliacionPedidoEnPreparacion.TextoProductos(evaluacion.LineasAMover);
            try
            {
                if (comprobacion.Accion == AccionAmpliacion.AvisarAlmacen)
                {
                    await avisos.AvisarAlmacen(TextosAmpliacionPedido.AnadidoAPedidoQueNoSale(pedido.empresa, pedido.numero, pedido.cliente, nombreCliente,
                        pedido.Usuario, evaluacion.Pickings, productos, evaluacion.Mensaje)).ConfigureAwait(false);
                    return null;
                }
                if (comprobacion.Accion != AccionAmpliacion.PedirAlmacen)
                {
                    return null;
                }
                var mover = new HashSet<LineaPedidoVentaDTO>(evaluacion.LineasAMover);
                List<int> numeros = (creadas ?? Enumerable.Empty<KeyValuePair<LineaPedidoVentaDTO, LinPedidoVta>>())
                    .Where(c => c.Key != null && c.Value != null && mover.Contains(c.Key))
                    .Select(c => c.Value.Nº_Orden).Where(n => n > 0).ToList();
                var solicitud = new AmpliacionPedidoDTO
                {
                    Empresa = pedido.empresa?.Trim(),
                    Pedido = pedido.numero,
                    Lineas = AmpliacionPedidoDTO.Enteros(numeros),
                    Productos = productos,
                    Pickings = AmpliacionPedidoDTO.Enteros(evaluacion.Pickings),
                    Envio = evaluacion.Envio,
                    Usuario = pedido.Usuario,
                    Fecha = ahora(),
                    Estado = EstadosAmpliacionPedido.PENDIENTE
                };
                solicitud.Id = await repositorio.Insertar(solicitud).ConfigureAwait(false);
                comprobacion.ProductosPendientesDeAlmacen = new HashSet<string>(evaluacion.LineasAMover
                    .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim()), StringComparer.OrdinalIgnoreCase);
                using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
                {
                    programarVencimiento(solicitud.Id);
                    int avisados = await avisos.Preguntar(TextosAmpliacionPedido.Pregunta(solicitud, pedido.cliente, nombreCliente)).ConfigureAwait(false);
                    return AvisoPedirAlmacen(pedido, evaluacion, solicitud.Id, avisados > 0);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] Pedido {pedido.numero}: ya guardado, no se ha podido " +
                    $"{(comprobacion.Accion == AccionAmpliacion.PedirAlmacen ? "pedir a Almacén que lo meta en el envío" : "avisar a Almacén")}: {ex.Message}", ex));
                return comprobacion.Accion == AccionAmpliacion.PedirAlmacen ? AvisoPedirAlmacen(pedido, evaluacion, null, false) : null;
            }
        }

        private static AvisoPedidoDTO AvisoPedirAlmacen(PedidoVentaDTO pedido, EvaluacionAmpliacion evaluacion, int? solicitud, bool avisado) => new AvisoPedidoDTO
        {
            Tipo = AmpliacionPedidoEnPreparacion.TIPO_AVISO_PEDIDO_A_ALMACEN,
            Mensaje = AmpliacionPedidoEnPreparacion.AvisoPedidoAAlmacen(pedido.numero, evaluacion.LineasAMover, avisado),
            Datos = new { Solicitud = solicitud, Pedido = pedido.numero, Avisado = avisado }
        };

        public Task<AmpliacionPedidoDTO> Leer(int id) => repositorio.Leer(id);

        public Task<List<AmpliacionPedidoDTO>> Listado(bool soloPendientes) => repositorio.Listado(soloPendientes);

        /// <summary>
        /// La respuesta de Almacén. «Sí»: saca el picking del pedido; si lo añadido no lo coge (sin stock, el cliente cierra…), cuenta como
        /// «No» con su motivo. «No»: lo pasa a un pedido nuevo. En los dos casos, «notificacionResuelta» y aviso a quien lo pidió.
        /// </summary>
        public async Task<ResultadoRespuestaAmpliacion> Responder(int id, bool entra, string motivo, string usuario)
        {
            if (!repositorio.TablaExiste())
            {
                throw new NestoBusinessException(TextosAmpliacionPedido.ERROR_SIN_TABLA,
                    new ErrorContext { ErrorCode = "AMPLIACION_SIN_TABLA" }) { StatusCode = HttpStatusCode.ServiceUnavailable };
            }
            AmpliacionPedidoDTO solicitud = await repositorio.Leer(id).ConfigureAwait(false);
            if (solicitud == null)
            {
                return new ResultadoRespuestaAmpliacion { NoEncontrada = true };
            }
            DateTime cuando = ahora();
            string estado = entra ? EstadosAmpliacionPedido.ENTRA : EstadosAmpliacionPedido.NO_ENTRA;
            if (solicitud.Estado != EstadosAmpliacionPedido.PENDIENTE || !await repositorio.Reclamar(id, estado, usuario, cuando).ConfigureAwait(false))
            {
                AmpliacionPedidoDTO actual = await repositorio.Leer(id).ConfigureAwait(false) ?? solicitud;
                return new ResultadoRespuestaAmpliacion { Solicitud = actual, Conflicto = YaContestada(actual) };
            }
            solicitud.Estado = estado;
            solicitud.UsuarioRespuesta = usuario;
            solicitud.Respondida = cuando;

            if (entra)
            {
                (int? picking, string motivoSinPicking) = await sacador().Sacar(solicitud.Empresa, solicitud.Pedido, solicitud.NumerosOrden()).ConfigureAwait(false);
                if (picking.HasValue)
                {
                    await repositorio.Cerrar(id, EstadosAmpliacionPedido.ENTRA, picking, null, null).ConfigureAwait(false);
                    solicitud.PickingNuevo = picking;
                    await Avisar(solicitud, EstadosAmpliacionPedido.ENTRA, picking, null, null).ConfigureAwait(false);
                    return new ResultadoRespuestaAmpliacion
                    {
                        Solicitud = solicitud,
                        Mensaje = $"Hecho: lo añadido al pedido {solicitud.Pedido} va en el picking {picking.Value}. Al hacer su envío se suma al que está en curso."
                    };
                }
                estado = EstadosAmpliacionPedido.NO_ENTRA;
                motivo = $"no se ha podido sacar el picking de lo añadido ({motivoSinPicking?.Trim().TrimEnd('.')})";
            }
            return await PasarAPedidoNuevo(solicitud, estado, motivo).ConfigureAwait(false);
        }

        /// <summary>El job: si a la hora sigue pendiente, lo añadido pasa a un pedido nuevo. Idempotente.</summary>
        public async Task<ResultadoRespuestaAmpliacion> Vencer(int id)
        {
            if (!repositorio.TablaExiste())
            {
                return null;
            }
            AmpliacionPedidoDTO solicitud = await repositorio.Leer(id).ConfigureAwait(false);
            if (solicitud == null || solicitud.Estado != EstadosAmpliacionPedido.PENDIENTE)
            {
                return null;
            }
            DateTime cuando = ahora();
            if (cuando - solicitud.Fecha < AmpliacionPedidoEnPreparacion.ESPERA_RESPUESTA_ALMACEN - TimeSpan.FromMinutes(1))
            {
                return null;
            }
            if (!await repositorio.Reclamar(id, EstadosAmpliacionPedido.SIN_RESPUESTA, USUARIO_SISTEMA, cuando).ConfigureAwait(false))
            {
                return null;
            }
            solicitud.Estado = EstadosAmpliacionPedido.SIN_RESPUESTA;
            solicitud.UsuarioRespuesta = USUARIO_SISTEMA;
            solicitud.Respondida = cuando;
            return await PasarAPedidoNuevo(solicitud, EstadosAmpliacionPedido.SIN_RESPUESTA, null).ConfigureAwait(false);
        }

        private async Task<ResultadoRespuestaAmpliacion> PasarAPedidoNuevo(AmpliacionPedidoDTO solicitud, string estado, string motivo)
        {
            ResultadoSeparacion resultado;
            try
            {
                resultado = await paso().Pasar(solicitud).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] Solicitud {solicitud.Id}, pedido {solicitud.Pedido}: no se ha podido " +
                    $"pasar lo añadido a un pedido nuevo: {ex.Message}", ex));
                resultado = new ResultadoSeparacion { MensajeError = ex.Message };
            }
            string motivoFinal = resultado.PedidoNuevo.HasValue ? motivo
                : string.IsNullOrWhiteSpace(motivo) ? resultado.MensajeError : $"{motivo.Trim().TrimEnd('.')}. {resultado.MensajeError}";
            await repositorio.Cerrar(solicitud.Id, estado, null, resultado.PedidoNuevo, motivoFinal).ConfigureAwait(false);
            solicitud.PedidoNuevo = resultado.PedidoNuevo;
            solicitud.Motivo = motivoFinal;
            await Avisar(solicitud, estado, null, resultado.PedidoNuevo, motivoFinal).ConfigureAwait(false);
            if (!resultado.PedidoNuevo.HasValue)
            {
                await Seguro(() => avisos.AvisarAlmacen(TextosAmpliacionPedido.SinMover(solicitud, motivoFinal)), solicitud.Id).ConfigureAwait(false);
            }
            return new ResultadoRespuestaAmpliacion
            {
                Solicitud = solicitud,
                Mensaje = resultado.PedidoNuevo.HasValue
                    ? $"Lo añadido al pedido {solicitud.Pedido} va en el pedido nuevo {resultado.PedidoNuevo.Value}, con sus propios portes. Ya se ha avisado a {TextosAmpliacionPedido.Nombre(solicitud.Usuario)}."
                    : $"No se ha podido pasar lo añadido a un pedido nuevo: {motivoFinal?.Trim().TrimEnd('.')}. Sigue en el pedido {solicitud.Pedido}, sin picking."
            };
        }

        private async Task Avisar(AmpliacionPedidoDTO solicitud, string estado, int? picking, int? pedidoNuevo, string motivo)
        {
            avisos.Resuelta(TextosAmpliacionPedido.Resuelta(solicitud, estado));
            await Seguro(() => avisos.AvisarA(solicitud.Usuario, TextosAmpliacionPedido.Respuesta(solicitud, estado, picking, pedidoNuevo, motivo)), solicitud.Id)
                .ConfigureAwait(false);
        }

        private static async Task Seguro(Func<Task> trabajo, int id)
        {
            try
            {
                await trabajo().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] Solicitud {id}: no se ha podido avisar: {ex.Message}", ex));
            }
        }

        internal static string YaContestada(AmpliacionPedidoDTO s)
        {
            string quien = TextosAmpliacionPedido.Nombre(s.UsuarioRespuesta);
            switch (s.Estado)
            {
                case EstadosAmpliacionPedido.ENTRA:
                    return $"Ya la ha contestado {quien}: lo añadido al pedido {s.Pedido} entra en el envío de hoy.";
                case EstadosAmpliacionPedido.NO_ENTRA:
                    return $"Ya la ha contestado {quien}: lo añadido al pedido {s.Pedido} no llega y va en un pedido nuevo.";
                case EstadosAmpliacionPedido.SIN_RESPUESTA:
                    return $"Ya no se puede contestar: nadie lo hizo a tiempo y lo añadido al pedido {s.Pedido} ha pasado a un pedido nuevo.";
                default:
                    return $"Ya está contestada ({s.Estado}).";
            }
        }
    }

    /// <summary>
    /// Caso 931007: pasar a un pedido nuevo las líneas de una solicitud, por el MISMO núcleo que el PUT (<see cref="ServicioAmpliacionPedidoEnPreparacion.Separar"/>):
    /// lee el pedido como UnirPedidos, quita esas líneas (si siguen sin picking) y crea el pedido nuevo a nombre de quien lo pidió.
    /// </summary>
    public class PasoAmpliacionAPedidoNuevo : IPasoAmpliacionAPedidoNuevo
    {
        public async Task<ResultadoSeparacion> Pasar(AmpliacionPedidoDTO solicitud)
        {
            PedidoVentaDTO original = await GestorPedidosVenta.LeerPedido(solicitud.Empresa, solicitud.Pedido).ConfigureAwait(false);
            if (original == null)
            {
                return new ResultadoSeparacion { MensajeError = $"el pedido {solicitud.Pedido} ya no existe" };
            }
            List<int> numeros = solicitud.NumerosOrden();
            List<LineaPedidoVentaDTO> lineas = original.Lineas.Where(l => numeros.Contains(l.id)).ToList();
            string motivo = MotivoNoSePuedenMover(lineas, numeros);
            if (motivo != null)
            {
                return new ResultadoSeparacion { MensajeError = motivo };
            }
            List<LineaPedidoVentaDTO> copias = lineas.Select(Clonar).ToList();
            original.LineasQuePasanAPedidoNuevo = new HashSet<int>(lineas.Select(l => l.id));
            original.Usuario = solicitud.Usuario;
            // Lo añadido ya pasó la validación al guardarlo en el pedido: el pedido nuevo no la repite (quien puede saltársela, se la salta)
            PedidoVentaDTO nuevo = AmpliacionPedidoEnPreparacion.ConstruirPedidoNuevo(original, copias, solicitud.Usuario, DateTime.Today, creadoSinPasarValidacion: true);
            using (var controller = new PedidosVentaController())
            {
                return await ServicioAmpliacionPedidoEnPreparacion.Separar(original, lineas, nuevo, controller.PutPedidoVenta, controller.PostPedidoVenta)
                    .ConfigureAwait(true);
            }
        }

        /// <summary>Null si se pueden mover; si no, por qué (de usted). Pura.</summary>
        internal static string MotivoNoSePuedenMover(IReadOnlyCollection<LineaPedidoVentaDTO> lineas, IReadOnlyCollection<int> numeros)
        {
            if (lineas == null || lineas.Count == 0)
            {
                return "lo añadido ya no está en el pedido";
            }
            if (lineas.Any(l => l.picking != 0 || l.estado < Constantes.EstadosLineaVenta.PENDIENTE || l.estado > Constantes.EstadosLineaVenta.EN_CURSO))
            {
                return "lo añadido ya tiene picking o albarán: no se ha tocado";
            }
            return null;
        }

        private static LineaPedidoVentaDTO Clonar(LineaPedidoVentaDTO linea)
            => JsonConvert.DeserializeObject<LineaPedidoVentaDTO>(JsonConvert.SerializeObject(linea));
    }

    /// <summary>Caso 931007: el «Sí» de Almacén: el picking del pedido suelto (como GET api/Picking?numeroPedido=), que coge lo que tiene sin picking.</summary>
    public class SacadorPickingAmpliacion : ISacadorPickingAmpliacion
    {
        public async Task<(int? Picking, string Motivo)> Sacar(string empresa, int pedido, IReadOnlyCollection<int> numerosOrden)
        {
            try
            {
                GestorPicking gestor = PickingJobsService.CrearGestor();
                await Task.Run(() => gestor.SacarPicking(empresa, pedido)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
            List<int> numeros = (numerosOrden ?? new List<int>()).ToList();
            using (var db = new NVEntities())
            {
                List<int?> pickings = db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Empresa == empresa && l.Número == pedido && numeros.Contains(l.Nº_Orden))
                    .Select(l => l.Picking).ToList();
                if (pickings.Count > 0 && pickings.All(p => (p ?? 0) > 0))
                {
                    return (pickings.Max(), null);
                }
            }
            return (null, "el picking no ha cogido lo añadido");
        }
    }

    /// <summary>Caso 931007: el vencimiento de la pregunta a Almacén (Hangfire, programado al crear la solicitud).</summary>
    public static class AmpliacionesPedidoJobs
    {
        public static void Programar(int id)
            => _ = Hangfire.BackgroundJob.Schedule(() => Vencer(id), AmpliacionPedidoEnPreparacion.ESPERA_RESPUESTA_ALMACEN);

        /// <summary>Punto de entrada de Hangfire. Nunca lanza.</summary>
        public static async Task Vencer(int id)
        {
            try
            {
                _ = await ServicioAmpliacionPedidoEnPreparacion.ParaRespuestas().Vencer(id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ampliación en preparación, caso 931007] Vencimiento de la solicitud {id}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>NestoAPI#775: lo usa ServicioNotificacionesUrgentes.PendientesDe.</summary>
    internal static class ComprobadorAmpliacionesProduccion
    {
        internal static IComprobadorUrgentesPendientes Crear() => new ComprobadorAmpliacionesPendientes(new RepositorioAmpliacionesPedido());
    }
}
