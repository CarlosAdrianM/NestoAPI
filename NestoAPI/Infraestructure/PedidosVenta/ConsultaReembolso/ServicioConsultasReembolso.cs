using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.ConsultaReembolso
{
    /// <summary>NestoAPI#734: lo que devuelven Consultar y Responder al controlador (que solo traduce a 200/400/404/409).</summary>
    public class ResultadoConsultaReembolso
    {
        public bool NoEncontrado { get; set; }
        /// <summary>La consulta no está abierta (nunca se hizo o ya está contestada): 409.</summary>
        public bool Conflicto { get; set; }
        public string Error { get; set; }
        public RespuestaConsultaReembolsoDTO Respuesta { get; set; }

        public static ResultadoConsultaReembolso Fallo(string error) => new ResultadoConsultaReembolso { Error = error };
        public static ResultadoConsultaReembolso EnConflicto(string error) => new ResultadoConsultaReembolso { Conflicto = true, Error = error };
        public static ResultadoConsultaReembolso SinPedido() => new ResultadoConsultaReembolso { NoEncontrado = true };
        public static ResultadoConsultaReembolso Exito(RespuestaConsultaReembolsoDTO respuesta) => new ResultadoConsultaReembolso { Respuesta = respuesta };
    }

    /// <summary>
    /// NestoAPI#734 (decisiones de Carlos, 29/10/26): un pedido contra reembolso con un enlace de pago pendiente. El almacén (Agencias
    /// de Nesto o el packing de Ariadna) no sabe si cobrar el reembolso: retiene el pedido (la misma retención de #698, modo 5, que
    /// no deja hacer envío ni albarán) y pregunta a Administración, que contesta:
    /// <list type="bullet">
    /// <item>«Sale con reembolso»: el envío lleva reembolso. Los enlaces pendientes solo se anulan si lo pide (pueden ser de otro pedido).</item>
    /// <item>«Sale sin reembolso»: con la forma de pago y plazos pactados. Nunca tarjeta o prepago si el pedido no está pagado.</item>
    /// </list>
    /// Con la respuesta se libera el pedido (si lo retuvo la consulta) y se avisa a quien preguntó. La propuesta de envío mira la
    /// última consulta para no volver a avisar.
    /// </summary>
    public class ServicioConsultasReembolso
    {
        public const int LONGITUD_MAXIMA_COMENTARIO = 500;

        private readonly IRepositorioConsultasReembolso repositorio;
        private readonly IRepositorioPedidosRetenidos retenidos;
        private readonly ServicioRetencionPedidos retencion;
        private readonly IAvisadorConsultaReembolso avisador;
        private readonly Func<DateTime> reloj;
        private readonly Action<Exception> registrar;

        public ServicioConsultasReembolso(IRepositorioConsultasReembolso repositorio, IRepositorioPedidosRetenidos retenidos,
            IAvisadorConsultaReembolso avisador = null, IRepositorioPromesaRiesgo promesas = null, Func<DateTime> reloj = null,
            Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.retenidos = retenidos ?? throw new ArgumentNullException(nameof(retenidos));
            this.avisador = avisador ?? new AvisadorConsultaReembolso();
            this.reloj = reloj ?? (() => DateTime.Now);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (consulta reembolso)"));
            // La retención de siempre (#698). Sin el aviso «NO ENTREGAR» al grupo Almacén: quien retiene es el propio almacén,
            // que tiene el pedido en la mano, y la consulta ya avisa a Administración.
            retencion = new ServicioRetencionPedidos(retenidos, promesas, this.reloj, this.registrar, (r, c, p) => Task.CompletedTask);
        }

        /// <summary>La última consulta del pedido (abierta o contestada), o null.</summary>
        public Task<ConsultaReembolsoDTO> Leer(string empresa, int numero) => repositorio.LeerUltima(Empresa(empresa), numero);

        /// <summary>Las abiertas (con sus enlaces pendientes de ahora) o, sin <paramref name="abiertas"/>, también las contestadas recientes.</summary>
        public async Task<List<ConsultaReembolsoListadoDTO>> Listado(string empresa, bool abiertas)
        {
            empresa = Empresa(empresa);
            List<ConsultaReembolsoListadoDTO> filas = await repositorio.LeerListado(empresa, abiertas).ConfigureAwait(false) ?? new List<ConsultaReembolsoListadoDTO>();
            DateTime ahora = reloj();
            foreach (ConsultaReembolsoListadoDTO fila in filas.Where(f => f.Abierta))
            {
                try
                {
                    fila.EnlacesPendientes = EnlacesPagoPedido.PendientesVivos(await repositorio.LeerEnlaces(empresa, fila.Pedido).ConfigureAwait(false), ahora);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Consulta reembolso #734] No se han podido leer los enlaces de pago del pedido {fila.Pedido}: {ex.Message}", ex));
                }
            }
            return filas;
        }

        /// <summary>
        /// El almacén retiene el pedido y pregunta. Idempotente: si ya hay una consulta abierta, la devuelve sin duplicarla
        /// (<see cref="RespuestaConsultaReembolsoDTO.YaExistia"/>). Si el pedido ya estaba retenido por otra cosa, no se toca
        /// esa retención (y al contestar tampoco se libera).
        /// </summary>
        public async Task<ResultadoConsultaReembolso> Consultar(string empresa, int numero, PedirConsultaReembolsoRequest peticion, string usuario)
        {
            empresa = Empresa(empresa);
            if (!repositorio.TablaExiste())
            {
                return ResultadoConsultaReembolso.Fallo(TextosConsultaReembolso.ERROR_SIN_TABLA);
            }
            DatosPagoPedidoConsulta pedido = await repositorio.LeerDatosPago(empresa, numero).ConfigureAwait(false);
            if (pedido == null)
            {
                return ResultadoConsultaReembolso.SinPedido();
            }
            ConsultaReembolsoDTO abierta = await repositorio.LeerAbierta(empresa, numero).ConfigureAwait(false);
            if (abierta != null)
            {
                return ResultadoConsultaReembolso.Exito(new RespuestaConsultaReembolsoDTO { Consulta = abierta, YaExistia = true });
            }

            DateTime ahora = reloj();
            List<EnlacePagoPedido> pendientes = EnlacesPagoPedido.PendientesVivos(await repositorio.LeerEnlaces(empresa, numero).ConfigureAwait(false), ahora);
            if (pendientes.Count == 0)
            {
                return ResultadoConsultaReembolso.Fallo(TextosConsultaReembolso.ERROR_SIN_ENLACES);
            }

            var consulta = new ConsultaReembolsoDTO
            {
                Empresa = empresa,
                Pedido = numero,
                ImporteEnlaces = RoundingHelper.DosDecimalesRound(pendientes.Sum(e => e.Importe)),
                NumeroEnlaces = pendientes.Count,
                Comentario = Recortar(peticion?.Comentario),
                Usuario = usuario?.Trim(),
                Fecha = ahora
            };

            // Primero la retención (como #698: sin ella el pedido podría salir mientras Administración decide).
            RetencionPedidoDTO viva = await retenidos.LeerActiva(empresa, numero).ConfigureAwait(false);
            if (viva == null)
            {
                ResultadoRetencion retenido = await retencion.Retener(empresa, numero,
                    new RetenerPedidoRequest { Motivo = TextosConsultaReembolso.MotivoRetencion(pendientes) }, usuario).ConfigureAwait(false);
                if (retenido.NoEncontrado)
                {
                    return ResultadoConsultaReembolso.SinPedido();
                }
                if (retenido.Error != null)
                {
                    return ResultadoConsultaReembolso.Fallo(retenido.Error);
                }
                consulta.RetencionId = retenido.Retencion?.Id;
            }

            try
            {
                consulta.Id = await repositorio.Insertar(consulta).ConfigureAwait(false);
            }
            catch
            {
                // Sin consulta nadie contestaría y el pedido se quedaría retenido sin saberlo: se deshace la retención.
                if (consulta.RetencionId.HasValue)
                {
                    _ = await retencion.Liberar(empresa, numero, usuario, "No se pudo guardar la consulta de reembolso (#734)").ConfigureAwait(false);
                }
                throw;
            }

            var respuesta = new RespuestaConsultaReembolsoDTO { Consulta = consulta };
            try
            {
                int avisados = await avisador.AvisarAdministracion(TextosConsultaReembolso.NotificacionConsulta(consulta, pedido, pendientes)).ConfigureAwait(false);
                if (avisados == 0)
                {
                    respuesta.Avisos.Add(TextosConsultaReembolso.AVISO_SIN_ADMINISTRACION);
                    registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no hay nadie en el grupo Administración a quien avisar."));
                }
            }
            catch (Exception ex)
            {
                respuesta.Avisos.Add(TextosConsultaReembolso.AVISO_SIN_ADMINISTRACION);
                registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se ha podido avisar a Administración: {ex.Message}", ex));
            }
            return ResultadoConsultaReembolso.Exito(respuesta);
        }

        /// <summary>Administración contesta: valida, aplica (forma de pago, enlaces), libera y avisa a quien preguntó.</summary>
        public async Task<ResultadoConsultaReembolso> Responder(string empresa, int numero, ResponderConsultaReembolsoRequest peticion, string usuario)
        {
            empresa = Empresa(empresa);
            if (!repositorio.TablaExiste())
            {
                return ResultadoConsultaReembolso.Fallo(TextosConsultaReembolso.ERROR_SIN_TABLA);
            }
            if (peticion?.ConReembolso == null)
            {
                return ResultadoConsultaReembolso.Fallo(TextosConsultaReembolso.ERROR_SIN_DECISION);
            }
            DatosPagoPedidoConsulta pedido = await repositorio.LeerDatosPago(empresa, numero).ConfigureAwait(false);
            if (pedido == null)
            {
                return ResultadoConsultaReembolso.SinPedido();
            }
            ConsultaReembolsoDTO consulta = await repositorio.LeerAbierta(empresa, numero).ConfigureAwait(false);
            if (consulta == null)
            {
                ConsultaReembolsoDTO ultima = await repositorio.LeerUltima(empresa, numero).ConfigureAwait(false);
                return ResultadoConsultaReembolso.EnConflicto(ultima == null
                    ? TextosConsultaReembolso.ERROR_SIN_CONSULTA
                    : $"La consulta ya está contestada: Administración ({TextosRetencion.NombreUsuario(ultima.UsuarioRespuesta)}) dijo el " +
                      $"{TextosConsultaReembolso.Fecha(ultima.Respondida ?? ultima.Fecha)} que {TextosConsultaReembolso.Decision(ultima)}.");
            }

            DateTime ahora = reloj();
            bool conReembolso = peticion.ConReembolso.Value;
            consulta.Respondida = ahora;
            consulta.UsuarioRespuesta = usuario?.Trim();
            consulta.ConReembolso = conReembolso;
            consulta.AnularEnlaces = peticion.AnularEnlaces == true;
            consulta.ComentarioRespuesta = Recortar(peticion.Comentario);

            // Validar todo ANTES de tocar nada.
            CambioFormaPago cambio = null;
            if (!conReembolso)
            {
                string error;
                (cambio, error) = await ValidarFormaPago(empresa, numero, pedido, peticion).ConfigureAwait(false);
                if (error != null)
                {
                    return ResultadoConsultaReembolso.Fallo(error);
                }
                consulta.FormaPagoAnterior = pedido.FormaPago?.Trim();
                consulta.PlazosPagoAnterior = pedido.PlazosPago?.Trim();
                consulta.FormaPago = cambio.FormaPago;
                consulta.PlazosPago = cambio.PlazosPago;
            }
            List<int> aAnular = new List<int>();
            if (consulta.AnularEnlaces == true)
            {
                List<EnlacePagoPedido> pendientes = EnlacesPagoPedido.PendientesVivos(await repositorio.LeerEnlaces(empresa, numero).ConfigureAwait(false), ahora);
                if (peticion.EnlacesAAnular != null && peticion.EnlacesAAnular.Count > 0)
                {
                    List<int> ajenos = peticion.EnlacesAAnular.Where(id => pendientes.All(p => p.Id != id)).Distinct().ToList();
                    if (ajenos.Any())
                    {
                        return ResultadoConsultaReembolso.Fallo(string.Format(TextosConsultaReembolso.ERROR_ENLACES_A_ANULAR, string.Join(", ", ajenos)));
                    }
                    aAnular = peticion.EnlacesAAnular.Distinct().ToList();
                }
                else
                {
                    aAnular = pendientes.Select(p => p.Id).ToList();
                }
            }

            // Primero se marca contestada (si otro de Administración contestó a la vez, gana el primero y aquí no se aplica nada).
            if (!await repositorio.Responder(consulta).ConfigureAwait(false))
            {
                return ResultadoConsultaReembolso.EnConflicto("Otra persona de Administración acaba de contestar esta consulta. Vuelva a cargarla.");
            }
            var respuesta = new RespuestaConsultaReembolsoDTO { Consulta = consulta };
            if (cambio != null)
            {
                try
                {
                    await repositorio.CambiarFormaPago(empresa, numero, cambio.FormaPago, cambio.PlazosPago, cambio.CCC, usuario,
                        $"Pedido {numero} FormaPago={pedido.FormaPago?.Trim()} PlazosPago={pedido.PlazosPago?.Trim()} CCC={pedido.CCC?.Trim() ?? "NULL"}",
                        $"FormaPago={cambio.FormaPago} PlazosPago={cambio.PlazosPago} CCC={cambio.CCC ?? "NULL"} (Administración: sale sin reembolso #734)")
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Sin el cambio, la respuesta no vale: la consulta vuelve a estar abierta y el pedido sigue retenido.
                    await repositorio.Reabrir(consulta.Id).ConfigureAwait(false);
                    throw;
                }
            }

            if (aAnular.Any())
            {
                try
                {
                    respuesta.EnlacesAnulados = await repositorio.AnularEnlaces(empresa, aAnular).ConfigureAwait(false) ?? new List<int>();
                    if (respuesta.EnlacesAnulados.Count < aAnular.Count)
                    {
                        respuesta.Avisos.Add($"No se han podido anular todos los enlaces (alguno ya no estaba pendiente): anulados {respuesta.EnlacesAnulados.Count} de {aAnular.Count}.");
                    }
                }
                catch (Exception ex)
                {
                    respuesta.Avisos.Add("No se han podido anular los enlaces de pago: anúlelos a mano.");
                    registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se han podido anular los enlaces {string.Join(", ", aAnular)}: {ex.Message}", ex));
                }
                consulta.EnlacesAnulados = respuesta.EnlacesAnulados.Any() ? string.Join(",", respuesta.EnlacesAnulados) : null;
                if (consulta.EnlacesAnulados != null)
                {
                    await GuardarAnulados(consulta).ConfigureAwait(false);
                }
            }

            respuesta.Liberado = await LiberarSiLoRetuvoLaConsulta(empresa, numero, consulta, usuario, respuesta).ConfigureAwait(false);

            if (!conReembolso)
            {
                await AvisarSiYaHayEnvioConReembolso(empresa, numero, respuesta).ConfigureAwait(false);
            }

            try
            {
                await avisador.AvisarA(consulta.Usuario,
                    TextosConsultaReembolso.NotificacionRespuesta(consulta, pedido, respuesta.EnlacesAnulados, respuesta.Liberado)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                respuesta.Avisos.Add(TextosConsultaReembolso.AVISO_SIN_RESPUESTA_AVISADA);
                registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se ha podido avisar a {consulta.Usuario}: {ex.Message}", ex));
            }
            return ResultadoConsultaReembolso.Exito(respuesta);
        }

        /// <summary>La forma de pago y los plazos nuevos para «sin reembolso», con el CCC que corresponda.</summary>
        internal class CambioFormaPago
        {
            public string FormaPago { get; set; }
            public string PlazosPago { get; set; }
            public string CCC { get; set; }
        }

        private async Task<(CambioFormaPago, string)> ValidarFormaPago(string empresa, int numero, DatosPagoPedidoConsulta pedido, ResponderConsultaReembolsoRequest peticion)
        {
            string formaPago = peticion.FormaPago?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(formaPago))
            {
                return (null, TextosConsultaReembolso.ERROR_SIN_FORMA_PAGO);
            }
            string plazosPago = string.IsNullOrWhiteSpace(peticion.PlazosPago) ? pedido.PlazosPago?.Trim() : peticion.PlazosPago.Trim().ToUpperInvariant();

            FormaPagoConsulta forma = await repositorio.LeerFormaPago(empresa, formaPago).ConfigureAwait(false);
            if (forma == null)
            {
                return (null, $"La forma de pago {formaPago} no existe.");
            }
            if (!string.IsNullOrEmpty(plazosPago) && !string.IsNullOrWhiteSpace(peticion.PlazosPago) &&
                !await repositorio.ExistenPlazosPago(empresa, plazosPago).ConfigureAwait(false))
            {
                return (null, $"Los plazos de pago {plazosPago} no existen.");
            }

            // Carlos (29/10/26): un prepago sin pagar no debe salir. Tarjeta o prepago, solo si ya está pagado entero.
            bool esPrepago = formaPago == Constantes.FormasPago.TARJETA || plazosPago == Constantes.PlazosPago.PREPAGO;
            if (esPrepago && !EstaPagado(empresa, numero))
            {
                return (null, "El pedido no está pagado: no puede salir como tarjeta o prepago. Si sale sin reembolso, elija la forma de pago " +
                    "pactada (recibo bancario, transferencia…); si el cliente va a pagar con el enlace, conteste que sale con reembolso o espere a que pague.");
            }

            string ccc = null;
            if (forma.CCCObligatorio)
            {
                ccc = string.IsNullOrWhiteSpace(pedido.CCC) ? pedido.CCCCliente?.Trim() : pedido.CCC.Trim();
                if (string.IsNullOrEmpty(ccc))
                {
                    return (null, $"El cliente no tiene cuenta bancaria (CCC) en su ficha: no se puede pasar el pedido a {forma.Descripcion?.Trim() ?? formaPago}. " +
                        "Póngasela en la ficha o elija otra forma de pago.");
                }
            }

            if (GestorPortes.EsContraReembolso(formaPago, plazosPago, ccc, pedido.PeriodoFacturacion?.Trim(), pedido.NotaEntrega))
            {
                return (null, $"Con la forma de pago {formaPago} y los plazos {plazosPago} el pedido seguiría saliendo contra reembolso. " +
                    "Elija la forma de pago pactada (recibo bancario, transferencia…).");
            }
            return (new CambioFormaPago { FormaPago = formaPago, PlazosPago = plazosPago, CCC = ccc }, null);
        }

        /// <summary>Pagado = lo pagado por adelantado (prepagos y enlaces cobrados) cubre lo que cobraría la agencia.</summary>
        private bool EstaPagado(string empresa, int numero)
        {
            try
            {
                decimal reembolso = repositorio.ImporteReembolso(empresa, numero);
                decimal prepagos = repositorio.PrepagosPendientes(empresa, numero);
                return prepagos > 0m && prepagos >= reembolso - 0.01m;
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se ha podido comprobar si está pagado: {ex.Message}", ex));
                return false;
            }
        }

        private async Task GuardarAnulados(ConsultaReembolsoDTO consulta)
        {
            try
            {
                await repositorio.ApuntarEnlacesAnulados(consulta.Id, consulta.EnlacesAnulados).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Consulta reembolso #734] Pedido {consulta.Pedido}: no se han podido guardar los enlaces anulados ({consulta.EnlacesAnulados}): {ex.Message}", ex));
            }
        }

        private async Task<bool> LiberarSiLoRetuvoLaConsulta(string empresa, int numero, ConsultaReembolsoDTO consulta, string usuario, RespuestaConsultaReembolsoDTO respuesta)
        {
            try
            {
                RetencionPedidoDTO viva = await retenidos.LeerActiva(empresa, numero).ConfigureAwait(false);
                if (viva == null)
                {
                    // La liberaron a mano mientras tanto: ya puede salir.
                    return true;
                }
                if (!consulta.RetencionId.HasValue || viva.Id != consulta.RetencionId.Value)
                {
                    // Retenido por otra cosa (antes o después de preguntar): esa retención no es de la consulta.
                    respuesta.Avisos.Add($"El pedido sigue retenido por otro motivo ({TextosRetencion.Descripcion(viva, mayusculaInicial: false)}).");
                    return false;
                }
                ResultadoRetencion liberado = await retencion.Liberar(empresa, numero, usuario, TextosConsultaReembolso.MotivoLiberacion(consulta)).ConfigureAwait(false);
                if (liberado.Error != null || liberado.NoEncontrado)
                {
                    respuesta.Avisos.Add($"No se ha podido liberar el pedido: {liberado.Error ?? "no existe"}. Libérelo desde el pedido.");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                respuesta.Avisos.Add("No se ha podido liberar el pedido: libérelo desde el pedido (botón «Liberar»).");
                registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se ha podido liberar: {ex.Message}", ex));
                return false;
            }
        }

        private async Task AvisarSiYaHayEnvioConReembolso(string empresa, int numero, RespuestaConsultaReembolsoDTO respuesta)
        {
            try
            {
                EnviosAgencia envio = EnlacesPagoPedido.EnvioEnRepartoConReembolso(await repositorio.LeerEnvios(empresa, numero).ConfigureAwait(false));
                if (envio != null)
                {
                    respuesta.Avisos.Add(TextosConsultaReembolso.AvisoEnvioYaHecho(envio));
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Consulta reembolso #734] Pedido {numero}: no se han podido leer los envíos: {ex.Message}", ex));
            }
        }

        private static string Recortar(string texto)
        {
            string limpio = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
            return limpio != null && limpio.Length > LONGITUD_MAXIMA_COMENTARIO ? limpio.Substring(0, LONGITUD_MAXIMA_COMENTARIO) : limpio;
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
