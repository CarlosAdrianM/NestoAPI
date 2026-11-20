using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.PedidosVenta.Autorizaciones
{
    /// <summary>Lo que devuelve cada operación: el código HTTP y su cuerpo (el controlador solo lo traduce).</summary>
    public class ResultadoAutorizacionPedido
    {
        public HttpStatusCode Codigo { get; set; } = HttpStatusCode.OK;
        /// <summary>El mensaje de error (de usted) si no es 2xx.</summary>
        public string Mensaje { get; set; }
        /// <summary>Solo con 409 YA_SOLICITADA: la que ya hay.</summary>
        public int? IdExistente { get; set; }
        public object Cuerpo { get; set; }
        /// <summary>Al autorizar, si el alta del pedido no ha ido bien: la respuesta tal cual del POST de PedidosVenta.</summary>
        public IHttpActionResult RespuestaAlta { get; set; }

        public static ResultadoAutorizacionPedido Error(HttpStatusCode codigo, string mensaje) => new ResultadoAutorizacionPedido { Codigo = codigo, Mensaje = mensaje };
        public static ResultadoAutorizacionPedido Bien(object cuerpo, HttpStatusCode codigo = HttpStatusCode.OK) => new ResultadoAutorizacionPedido { Codigo = codigo, Cuerpo = cuerpo };
    }

    /// <summary>
    /// Novedades 1258 (Carlos 20/11/26): el vendedor pide desde el propio aviso de «no pasa la validación» que le autoricen el pedido; a
    /// quien puede autorizarlo (LA REGLA: <see cref="ReglaOmitirValidacionPedido"/>, la del «¿Desea crearlo de todos modos?») le llega por
    /// la campana con el pedido listo, y al autorizarlo se crea por el MISMO POST de PedidosVenta, a nombre del solicitante.
    /// </summary>
    /// <remarks>ConfigureAwait(true): el alta del pedido (PostPedidoVenta) usa HttpContext.Current.</remarks>
    public class ServicioAutorizacionesPedido
    {
        private readonly IRepositorioAutorizacionesPedido repositorio;
        private readonly IDestinatariosAutorizacionPedido destinatarios;
        private readonly IAvisosAutorizacionPedido avisos;
        private readonly Func<PedidoVentaDTO, IPrincipal, RespuestaValidacion> validar;
        private readonly Func<string, string, string, string> leerParametro;
        private readonly Func<IPrincipal, string, string> usuarioDe;
        private readonly Func<DateTime> ahora;

        public ServicioAutorizacionesPedido(IRepositorioAutorizacionesPedido repositorio, IDestinatariosAutorizacionPedido destinatarios,
            IAvisosAutorizacionPedido avisos, Func<PedidoVentaDTO, IPrincipal, RespuestaValidacion> validar = null,
            Func<string, string, string, string> leerParametro = null, Func<IPrincipal, string, string> usuarioDe = null, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.destinatarios = destinatarios ?? throw new ArgumentNullException(nameof(destinatarios));
            this.avisos = avisos ?? throw new ArgumentNullException(nameof(avisos));
            this.validar = validar ?? ((p, u) => GestorPrecios.EsPedidoValido(p, VigenciaPedidoTienda.PuedeValidarConDiaDelPedido(u)));
            this.leerParametro = leerParametro ?? ParametrosUsuarioController.LeerParametro;
            this.usuarioDe = usuarioDe ?? UsuarioAuditoriaHelper.Resolver;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public static ServicioAutorizacionesPedido DeProduccion()
            => new ServicioAutorizacionesPedido(new RepositorioAutorizacionesPedido(), new DestinatariosAutorizacionPedido(), new AvisosAutorizacionPedido());

        public bool TablaExiste() => repositorio.TablaExiste();

        // ----- POST api/PedidosVenta/Autorizaciones -----

        public async Task<ResultadoAutorizacionPedido> Solicitar(IPrincipal principal, SolicitarAutorizacionPedidoRequest peticion)
        {
            PedidoVentaDTO pedido = peticion?.Pedido;
            if (pedido == null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_SIN_PEDIDO);
            }
            if (string.IsNullOrWhiteSpace(pedido.cliente))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_SIN_CLIENTE);
            }
            if (pedido.Lineas == null || !pedido.Lineas.Any())
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_SIN_LINEAS);
            }
            string motivoComentario = ReglasAutorizacionPedido.MotivoTextoNoValido(peticion.Comentario, "El comentario");
            if (motivoComentario != null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, motivoComentario);
            }
            if (string.IsNullOrWhiteSpace(pedido.empresa))
            {
                pedido.empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            }
            string solicitante = usuarioDe(principal, pedido.Usuario);
            pedido.Usuario = solicitante;
            pedido.VincularLineas();

            RespuestaValidacion validacion = validar(pedido, principal);
            if (validacion == null || validacion.ValidacionSuperada)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_NO_NECESITA);
            }

            string huella = ReglasAutorizacionPedido.Huella(pedido);
            DateTime cuando = ahora();
            int? identica = await repositorio.PendienteIdentica(solicitante, huella, cuando - ReglasAutorizacionPedido.VENTANA_IDENTICA).ConfigureAwait(true);
            if (identica.HasValue)
            {
                return new ResultadoAutorizacionPedido
                {
                    Codigo = HttpStatusCode.Conflict,
                    Mensaje = TextosAutorizacionPedido.ERROR_YA_SOLICITADA,
                    IdExistente = identica
                };
            }

            MotivosAutorizacionPedido motivos = MotivosAutorizacionPedido.De(validacion);
            var fila = new FilaAutorizacionPedido
            {
                Empresa = pedido.empresa.Trim(),
                Cliente = pedido.cliente.Trim(),
                Contacto = pedido.contacto?.Trim(),
                Solicitante = solicitante,
                FechaSolicitud = cuando,
                PedidoJson = SerializarPedido(pedido),
                Motivos = motivos.AJson(),
                Comentario = string.IsNullOrWhiteSpace(peticion.Comentario) ? null : peticion.Comentario.Trim(),
                Estado = EstadosAutorizacionPedido.PENDIENTE,
                Huella = huella,
                PermisoNecesario = PermisoNecesario(validacion),
                Importe = ImporteSeguro(pedido),
                NumeroLineas = pedido.Lineas.Count,
                Vendedor = pedido.vendedor?.Trim(),
                Almacenes = ReglasAutorizacionPedido.Almacenes(pedido)
            };
            fila.Id = await repositorio.Insertar(fila).ConfigureAwait(true);

            List<string> quienes = await destinatarios.Leer(fila.Empresa, fila.ListaAlmacenes(), fila.PermisoNecesario, solicitante).ConfigureAwait(true)
                ?? new List<string>();
            try
            {
                FilaAutorizacionPedido conNombre = await repositorio.Leer(fila.Id).ConfigureAwait(true);
                fila.NombreCliente = conNombre?.NombreCliente;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] Solicitud {fila.Id}: no se ha podido leer el nombre del cliente: {ex.Message}", ex));
            }
            await avisos.Pedida(quienes, TextosAutorizacionPedido.Pedida(fila, motivos.Motivos)).ConfigureAwait(true);

            return ResultadoAutorizacionPedido.Bien(new SolicitudAutorizacionCreadaDTO
            {
                Id = fila.Id,
                Estado = EstadosAutorizacionPedido.PENDIENTE,
                Motivos = motivos.Motivos,
                Errores = motivos.Errores,
                Destinatarios = quienes.Select(ReglaOmitirValidacionPedido.SinDominio).ToList()
            }, HttpStatusCode.Created);
        }

        // ----- GET -----

        public async Task<List<AutorizacionPedidoDTO>> Listado(IPrincipal principal, bool pendientes, bool mias)
        {
            string usuario = usuarioDe(principal, null);
            List<FilaAutorizacionPedido> filas = await repositorio.Listado(ahora().Date.AddDays(-ReglasAutorizacionPedido.DIAS_LISTADO), pendientes)
                .ConfigureAwait(true) ?? new List<FilaAutorizacionPedido>();
            Func<string, string, string, string> lector = Memorizado(leerParametro);
            return filas
                .Where(f => ReglasAutorizacionPedido.EsDe(f.Solicitante, usuario) || (!mias && PuedeAutorizar(principal, usuario, f, lector)))
                .Select(f => ADto(f, conPedido: false))
                .ToList();
        }

        public async Task<ResultadoAutorizacionPedido> Leer(IPrincipal principal, int id)
        {
            FilaAutorizacionPedido fila = await repositorio.Leer(id).ConfigureAwait(true);
            if (fila == null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.NotFound, TextosAutorizacionPedido.ERROR_NO_ENCONTRADA);
            }
            string usuario = usuarioDe(principal, null);
            if (!ReglasAutorizacionPedido.EsDe(fila.Solicitante, usuario) && !PuedeAutorizar(principal, usuario, fila, leerParametro))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Forbidden, TextosAutorizacionPedido.ERROR_SIN_PERMISO_VER);
            }
            return ResultadoAutorizacionPedido.Bien(ADto(fila, conPedido: true));
        }

        // ----- POST …/{id}/Autorizar -----

        /// <param name="crearPedido">El POST de PedidosVenta (el pedido, quien lo autoriza): el MISMO camino que «crearlo de todos modos».</param>
        public async Task<ResultadoAutorizacionPedido> Autorizar(IPrincipal principal, int id, AutorizarPedidoRequest peticion,
            Func<PedidoVentaDTO, string, Task<IHttpActionResult>> crearPedido)
        {
            string motivoComentario = ReglasAutorizacionPedido.MotivoTextoNoValido(peticion?.Comentario, "El comentario");
            if (motivoComentario != null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, motivoComentario);
            }
            FilaAutorizacionPedido fila = await repositorio.Leer(id).ConfigureAwait(true);
            if (fila == null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.NotFound, TextosAutorizacionPedido.ERROR_NO_ENCONTRADA);
            }
            string autorizador = usuarioDe(principal, null);
            if (!PuedeAutorizar(principal, autorizador, fila, leerParametro))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Forbidden, TextosAutorizacionPedido.ERROR_SIN_PERMISO_AUTORIZAR);
            }
            string noPendiente = ReglasAutorizacionPedido.MotivoNoPendiente(fila);
            if (noPendiente != null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Conflict, noPendiente);
            }

            using (IDisposable bloqueo = repositorio.BloquearParaAutorizar(id))
            {
                if (bloqueo == null)
                {
                    return ResultadoAutorizacionPedido.Error(HttpStatusCode.Conflict, TextosAutorizacionPedido.ERROR_SE_ESTA_AUTORIZANDO);
                }
                // Con el bloqueo cogido, otra vez: puede haberla resuelto otro mientras tanto
                fila = await repositorio.Leer(id).ConfigureAwait(true);
                noPendiente = fila == null ? TextosAutorizacionPedido.ERROR_NO_ENCONTRADA : ReglasAutorizacionPedido.MotivoNoPendiente(fila);
                if (noPendiente != null)
                {
                    return ResultadoAutorizacionPedido.Error(HttpStatusCode.Conflict, noPendiente);
                }
                PedidoVentaDTO pedido = PedidoParaCrear(fila);
                if (pedido == null)
                {
                    return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_PEDIDO_ILEGIBLE);
                }

                IHttpActionResult alta = await crearPedido(pedido, autorizador).ConfigureAwait(true);
                int? numero = NumeroCreado(alta);
                if (!numero.HasValue)
                {
                    // Stock, cliente, fecha lejana… lo que diga el POST de siempre; la solicitud sigue pendiente
                    return new ResultadoAutorizacionPedido { Codigo = HttpStatusCode.BadRequest, RespuestaAlta = alta };
                }

                string comentario = string.IsNullOrWhiteSpace(peticion?.Comentario) ? null : peticion.Comentario.Trim();
                DateTime cuando = ahora();
                try
                {
                    if (!await repositorio.Resolver(id, EstadosAutorizacionPedido.AUTORIZADA, autorizador, cuando, comentario, numero).ConfigureAwait(true))
                    {
                        ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] Solicitud {id}: el pedido {numero} se ha creado pero la " +
                            "solicitud ya no estaba pendiente al marcarla autorizada."));
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] Solicitud {id}: el pedido {numero} se ha creado pero no se ha " +
                        $"podido marcar autorizada: {ex.Message}", ex));
                }
                await repositorio.Rastro(RepositorioAutorizacionesPedido.TABLA_RASTRO,
                    JsonConvert.SerializeObject(new { Solicitud = id, fila.Empresa, fila.Cliente, fila.Contacto, fila.Solicitante, Motivos = MotivosAutorizacionPedido.DeJson(fila.Motivos).Motivos }),
                    JsonConvert.SerializeObject(new { Pedido = numero, Autorizo = autorizador, Comentario = comentario }),
                    autorizador).ConfigureAwait(true);

                fila.Estado = EstadosAutorizacionPedido.AUTORIZADA;
                fila.UsuarioResolucion = autorizador;
                fila.FechaResolucion = cuando;
                fila.MotivoResolucion = comentario;
                fila.PedidoCreado = numero;
                await AvisarResuelta(fila, autorizador).ConfigureAwait(true);
                return ResultadoAutorizacionPedido.Bien(new ResolucionAutorizacionPedidoDTO { Id = id, Estado = EstadosAutorizacionPedido.AUTORIZADA, Pedido = numero });
            }
        }

        // ----- POST …/{id}/Denegar -----

        public async Task<ResultadoAutorizacionPedido> Denegar(IPrincipal principal, int id, DenegarPedidoRequest peticion)
        {
            string motivo = peticion?.Motivo?.Trim();
            if (string.IsNullOrEmpty(motivo))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, TextosAutorizacionPedido.ERROR_SIN_MOTIVO);
            }
            string largo = ReglasAutorizacionPedido.MotivoTextoNoValido(motivo, "El motivo");
            if (largo != null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.BadRequest, largo);
            }
            FilaAutorizacionPedido fila = await repositorio.Leer(id).ConfigureAwait(true);
            if (fila == null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.NotFound, TextosAutorizacionPedido.ERROR_NO_ENCONTRADA);
            }
            string usuario = usuarioDe(principal, null);
            if (!PuedeAutorizar(principal, usuario, fila, leerParametro))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Forbidden, TextosAutorizacionPedido.ERROR_SIN_PERMISO_AUTORIZAR);
            }
            return await Cerrar(fila, EstadosAutorizacionPedido.DENEGADA, usuario, motivo, avisarAlSolicitante: true).ConfigureAwait(true);
        }

        // ----- DELETE …/{id} -----

        public async Task<ResultadoAutorizacionPedido> Anular(IPrincipal principal, int id)
        {
            FilaAutorizacionPedido fila = await repositorio.Leer(id).ConfigureAwait(true);
            if (fila == null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.NotFound, TextosAutorizacionPedido.ERROR_NO_ENCONTRADA);
            }
            string usuario = usuarioDe(principal, null);
            if (!ReglasAutorizacionPedido.EsDe(fila.Solicitante, usuario))
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Forbidden, TextosAutorizacionPedido.ERROR_SIN_PERMISO_ANULAR);
            }
            return await Cerrar(fila, EstadosAutorizacionPedido.ANULADA, usuario, null, avisarAlSolicitante: false).ConfigureAwait(true);
        }

        private async Task<ResultadoAutorizacionPedido> Cerrar(FilaAutorizacionPedido fila, string estado, string usuario, string motivo, bool avisarAlSolicitante)
        {
            string noPendiente = ReglasAutorizacionPedido.MotivoNoPendiente(fila);
            if (noPendiente != null)
            {
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Conflict, noPendiente);
            }
            DateTime cuando = ahora();
            if (!await repositorio.Resolver(fila.Id, estado, usuario, cuando, motivo, null).ConfigureAwait(true))
            {
                FilaAutorizacionPedido releida = await repositorio.Leer(fila.Id).ConfigureAwait(true);
                return ResultadoAutorizacionPedido.Error(HttpStatusCode.Conflict,
                    ReglasAutorizacionPedido.MotivoNoPendiente(releida) ?? "Esta solicitud ya no está pendiente.");
            }
            fila.Estado = estado;
            fila.UsuarioResolucion = usuario;
            fila.FechaResolucion = cuando;
            fila.MotivoResolucion = motivo;
            if (avisarAlSolicitante)
            {
                await AvisarResuelta(fila, usuario).ConfigureAwait(true);
            }
            else
            {
                await QuitarDeLosDemas(fila, usuario).ConfigureAwait(true);
            }
            return ResultadoAutorizacionPedido.Bien(new ResolucionAutorizacionPedidoDTO { Id = fila.Id, Estado = estado });
        }

        private async Task AvisarResuelta(FilaAutorizacionPedido fila, string quien)
        {
            try
            {
                await avisos.Resuelta(fila.Solicitante, fila.Empresa, fila.Vendedor, TextosAutorizacionPedido.Resuelta(fila)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] Solicitud {fila.Id}: aviso a quien la pidió: {ex.Message}", ex));
            }
            await QuitarDeLosDemas(fila, quien).ConfigureAwait(true);
        }

        private async Task QuitarDeLosDemas(FilaAutorizacionPedido fila, string quien)
        {
            try
            {
                List<string> todos = await destinatarios.Leer(fila.Empresa, fila.ListaAlmacenes(), fila.PermisoNecesario, fila.Solicitante).ConfigureAwait(true)
                    ?? new List<string>();
                avisos.QuitarDeLosDemas(todos.Where(u => !ReglasAutorizacionPedido.EsDe(u, quien)).ToList(), TextosAutorizacionPedido.NotificacionResuelta(fila));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Autorizaciones de pedidos, Novedades 1258] Solicitud {fila.Id}: notificacionResuelta: {ex.Message}", ex));
            }
        }

        // ----- piezas -----

        /// <summary>LA REGLA del pedido de la solicitud para quien pregunta (con sus parámetros).</summary>
        internal static bool PuedeAutorizar(IPrincipal principal, string usuario, FilaAutorizacionPedido fila, Func<string, string, string, string> lector)
            => ReglaOmitirValidacionPedido.PuedeOmitir(principal, usuario, fila.Empresa, fila.ListaAlmacenes(), fila.PermisoNecesario, lector);

        /// <summary>El pedido tal como se guardó, listo para el POST: a nombre del solicitante y «de todos modos». Null si el JSON no se lee.</summary>
        internal static PedidoVentaDTO PedidoParaCrear(FilaAutorizacionPedido fila)
        {
            PedidoVentaDTO pedido;
            try
            {
                pedido = string.IsNullOrWhiteSpace(fila?.PedidoJson) ? null : JsonConvert.DeserializeObject<PedidoVentaDTO>(fila.PedidoJson);
            }
            catch (JsonException)
            {
                return null;
            }
            if (pedido == null)
            {
                return null;
            }
            pedido.numero = 0;
            pedido.Usuario = fila.Solicitante;
            pedido.CreadoSinPasarValidacion = true;
            pedido.VincularLineas();
            return pedido;
        }

        /// <summary>El número del pedido creado si el POST ha ido bien (201 con el pedido); si no, null.</summary>
        internal static int? NumeroCreado(IHttpActionResult resultado)
        {
            PedidoVentaDTO pedido = (resultado as CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>)?.Content
                ?? (resultado as CreatedNegotiatedContentResult<PedidoVentaDTO>)?.Content
                ?? (resultado as OkNegotiatedContentResult<PedidoVentaDTO>)?.Content;
            return pedido != null && pedido.numero > 0 ? pedido.numero : (int?)null;
        }

        private static string PermisoNecesario(RespuestaValidacion validacion)
        {
            string permiso = validacion?.PermisoNecesario;
            if (string.IsNullOrWhiteSpace(permiso))
            {
                permiso = validacion?.Errores?.Select(e => e?.PermisoNecesario).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            }
            return string.IsNullOrWhiteSpace(permiso) ? null : permiso.Trim();
        }

        private static decimal ImporteSeguro(PedidoVentaDTO pedido)
        {
            try
            {
                return pedido.BaseImponible;
            }
            catch
            {
                return 0;
            }
        }

        private static Func<string, string, string, string> Memorizado(Func<string, string, string, string> lector)
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return (empresa, usuario, clave) =>
            {
                string k = string.Join("|", empresa, usuario, clave);
                if (!cache.TryGetValue(k, out string valor))
                {
                    valor = lector(empresa, usuario, clave);
                    cache[k] = valor;
                }
                return valor;
            };
        }

        internal static AutorizacionPedidoDTO ADto(FilaAutorizacionPedido f, bool conPedido)
        {
            MotivosAutorizacionPedido motivos = MotivosAutorizacionPedido.DeJson(f.Motivos);
            PedidoVentaDTO pedido = null;
            if (conPedido && !string.IsNullOrWhiteSpace(f.PedidoJson))
            {
                try
                {
                    pedido = JsonConvert.DeserializeObject<PedidoVentaDTO>(f.PedidoJson);
                }
                catch (JsonException)
                {
                    pedido = null;
                }
            }
            return new AutorizacionPedidoDTO
            {
                Id = f.Id,
                Estado = f.Estado,
                Empresa = f.Empresa,
                Cliente = f.Cliente,
                Contacto = f.Contacto,
                NombreCliente = f.NombreCliente,
                Solicitante = f.Solicitante,
                NombreSolicitante = ReglaOmitirValidacionPedido.SinDominio(f.Solicitante),
                FechaSolicitud = f.FechaSolicitud,
                Importe = f.Importe,
                NumeroLineas = f.NumeroLineas,
                Motivos = motivos.Motivos,
                Errores = motivos.Errores,
                Comentario = f.Comentario,
                UsuarioResolucion = f.UsuarioResolucion,
                FechaResolucion = f.FechaResolucion,
                MotivoResolucion = f.MotivoResolucion,
                PedidoCreado = f.PedidoCreado,
                Pedido = pedido
            };
        }

        /// <summary>
        /// El pedido tal como llegó. «RecogeEnTienda» se lee calculado de la ruta pero al escribir null es «no tocar» (NestoAPI#571): se
        /// guarda lo que mandó el cliente, no lo calculado, para que el POST de la autorización haga lo mismo que habría hecho el suyo.
        /// </summary>
        internal static string SerializarPedido(PedidoVentaDTO pedido)
        {
            Newtonsoft.Json.Linq.JObject o = Newtonsoft.Json.Linq.JObject.FromObject(pedido);
            o[nameof(PedidoVentaDTO.RecogeEnTienda)] = pedido.RecogeEnTiendaRecibido.HasValue
                ? new Newtonsoft.Json.Linq.JValue(pedido.RecogeEnTiendaRecibido.Value)
                : Newtonsoft.Json.Linq.JValue.CreateNull();
            return o.ToString(Formatting.None);
        }
    }
}
