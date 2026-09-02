using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Infraestructure.Pagos
{
    public class ServicioPagos : IServicioPagos
    {
        private readonly IRedsysService _redsysService;
        private readonly IContabilidadService _contabilidadService;
        private readonly ILectorParametrosUsuario _lectorParametros;
        private readonly IServicioCorreoElectronico _servicioCorreo;
        private readonly ILogService _logService;
        private readonly ITarjetaClienteStore _tarjetaStore;

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros)
            : this(redsysService, contabilidadService, lectorParametros, new ServicioCorreoElectronico(), new ElmahLogService())
        {
        }

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros, IServicioCorreoElectronico servicioCorreo)
            : this(redsysService, contabilidadService, lectorParametros, servicioCorreo, new ElmahLogService())
        {
        }

        public ServicioPagos(IRedsysService redsysService, IContabilidadService contabilidadService, ILectorParametrosUsuario lectorParametros, IServicioCorreoElectronico servicioCorreo, ILogService logService, ITarjetaClienteStore tarjetaStore = null)
        {
            _redsysService = redsysService;
            _contabilidadService = contabilidadService;
            _lectorParametros = lectorParametros;
            _servicioCorreo = servicioCorreo;
            _logService = logService;
            _tarjetaStore = tarjetaStore ?? new TarjetaClienteStore();
        }

        public async Task<RespuestaIniciarPago> IniciarPago(SolicitudPagoTPV solicitud, string usuario)
        {
            if (solicitud.Importe <= 0)
            {
                throw new ArgumentException("El importe debe ser mayor que cero");
            }

            // #295: el concepto acaba en el extracto del cliente ("Pago TPV {Descripcion}").
            // Normalizamos mayúsculas y, si el enlace NO liquida efectos concretos, exigimos un
            // concepto real (con el genérico no hay forma de saber qué pagó el cliente).
            solicitud.Descripcion = FormateadorConcepto.Normalizar(solicitud.Descripcion);
            List<EfectoAPagar> efectos = NormalizarEfectos(solicitud);
            if (!efectos.Any() && FormateadorConcepto.EsGenericoOVacio(solicitud.Descripcion))
            {
                throw new ArgumentException(
                    "Indique un concepto que identifique el pago (por ejemplo 'Pago pedido 123456' o " +
                    "'Pago señal curso quiromasaje') o seleccione los efectos que paga el cliente.");
            }

            string urlBase = "https://api.nuevavision.es";
            string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
            string urlOk = solicitud.UrlOk ?? urlBase + "/pago/ok.html";
            string urlKo = solicitud.UrlKo ?? urlBase + "/pago/ko.html";

            // NestoAPI#178: en los cobros de pedidos de la app se pide a Redsys que tokenice la
            // tarjeta. El objetivo es que el cliente la meta UNA vez: cada pedido cobrado sin
            // tokenizar es un cliente al que habrá que volver a pedírsela.
            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosTPVVirtual(
                solicitud.Importe,
                solicitud.Descripcion,
                solicitud.Correo,
                solicitud.Cliente,
                urlNotificacion,
                urlOk,
                urlKo,
                solicitud.MetodoPago,
                // Con tarjeta guardada no se pide tokenizar (ya lo está): se manda la referencia
                solicitarToken: solicitud.Pedido.HasValue && solicitud.TarjetaGuardada == null,
                tokenTarjeta: solicitud.TarjetaGuardada?.TokenRedsys,
                cofTxnId: solicitud.TarjetaGuardada?.CofTxnId);

            using (NVEntities db = new NVEntities())
            {
                var pago = new PagoTPV
                {
                    NumeroOrden = parametros.NumeroOrden,
                    // NestoAPI#436: el cobro de un pedido de la app no se contabiliza como el
                    // enlace de pago; se distingue por el Tipo y lleva el pedido en Documento.
                    Tipo = solicitud.Pedido.HasValue
                        ? Constantes.TiposPagoTPV.PEDIDO_APP
                        : Constantes.TiposPagoTPV.TPV_VIRTUAL,
                    Empresa = solicitud.Empresa ?? Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = solicitud.Cliente,
                    Contacto = solicitud.Contacto,
                    Importe = solicitud.Importe,
                    Descripcion = solicitud.Descripcion,
                    Correo = solicitud.Correo,
                    // Campos legacy se mantienen para compatibilidad
                    ExtractoClienteId = solicitud.ExtractoClienteId,
                    Documento = solicitud.Pedido.HasValue
                        ? solicitud.Pedido.Value.ToString()
                        : solicitud.Documento,
                    Efecto = solicitud.Efecto,
                    Vendedor = solicitud.Vendedor,
                    FormaVenta = solicitud.FormaVenta,
                    Delegacion = solicitud.Delegacion,
                    TipoApunte = solicitud.TipoApunte,
                    Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                    FechaCreacion = DateTime.Now,
                    Usuario = usuario,
                    TokenAcceso = Guid.NewGuid(),
                    MetodoPago = solicitud.MetodoPago
                };

                db.PagosTPV.Add(pago);
                await db.SaveChangesAsync().ConfigureAwait(false);

                foreach (var efecto in efectos)
                {
                    var pagoEfecto = new PagoTPV_Efecto
                    {
                        IdPago = pago.Id,
                        ExtractoClienteId = efecto.ExtractoClienteId,
                        Importe = efecto.Importe,
                        Documento = efecto.Documento,
                        Efecto = efecto.Efecto,
                        Contacto = efecto.Contacto,
                        Vendedor = efecto.Vendedor,
                        FormaVenta = efecto.FormaVenta,
                        Delegacion = efecto.Delegacion,
                        TipoApunte = efecto.TipoApunte
                    };
                    db.PagosTPV_Efectos.Add(pagoEfecto);
                }

                await db.SaveChangesAsync().ConfigureAwait(false);

                string urlPaginaPago = $"https://api.nuevavision.es/pago/{pago.TokenAcceso}";

                // Issue #139: Correo pre-cobro al cliente.
                // NestoAPI#436: en la app NO. Alli esto no es un enlace de pago que se manda para
                // que lo abra cuando quiera: es un cobro online, el cliente esta delante y la
                // pasarela se abre en el momento. Mandarle un correo con un enlace de pago
                // ademas del cobro que acaba de hacer solo confunde (y se pagaria dos veces).
                if (!solicitud.Pedido.HasValue)
                {
                    EnviarCorreoPreCobro(pago, efectos, urlPaginaPago);
                }

                return new RespuestaIniciarPago
                {
                    IdPago = pago.Id,
                    UrlRedsys = _redsysService.UrlFormularioRedsys,
                    Ds_SignatureVersion = parametros.Ds_SignatureVersion,
                    Ds_MerchantParameters = parametros.Ds_MerchantParameters,
                    Ds_Signature = parametros.Ds_Signature,
                    TokenAcceso = pago.TokenAcceso,
                    UrlPaginaPago = urlPaginaPago
                };
            }
        }

        public async Task<bool> ProcesarNotificacion(NotificacionRedsys notificacion)
        {
            ResultadoValidacionNotificacion resultado = _redsysService.ValidarNotificacion(notificacion);

            if (!resultado.FirmaValida)
            {
                string mensajeFirma = $"[ProcesarNotificacion] Firma inválida. Orden: {resultado.NumeroOrden}, Error: {resultado.MensajeError}";
                _logService.LogError(mensajeFirma);
                EnviarCorreoAlertaPago("Firma invalida en notificacion Redsys", mensajeFirma, resultado);
                return false;
            }

            // #445 (TEMPORAL): qué manda exactamente nuestro terminal en cada notificación
            LogDiagnosticoRedsys("Notificación recibida", resultado.NumeroOrden, resultado.NotificacionDecodificada);

            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .FirstOrDefaultAsync(p => p.NumeroOrden == resultado.NumeroOrden)
                    .ConfigureAwait(false);

                if (pago == null)
                {
                    string mensajeNoEncontrado = $"[ProcesarNotificacion] Pago no encontrado. " +
                        $"Orden: {resultado.NumeroOrden}, " +
                        $"Codigo respuesta: {resultado.CodigoRespuesta}, " +
                        $"Codigo autorizacion: {resultado.CodigoAutorizacion}, " +
                        $"Pago autorizado: {resultado.PagoAutorizado}";
                    _logService.LogError(mensajeNoEncontrado);
                    EnviarCorreoAlertaPago(
                        "Pago Redsys recibido pero NO encontrado en base de datos",
                        mensajeNoEncontrado,
                        resultado);
                    return false;
                }

                pago.CodigoRespuesta = resultado.CodigoRespuesta;
                pago.CodigoAutorizacion = resultado.CodigoAutorizacion;
                pago.FechaActualizacion = DateTime.Now;

                if (resultado.PagoAutorizado)
                {
                    pago.Estado = Constantes.EstadosPagoTPV.AUTORIZADO;
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    // NestoAPI#178: si Redsys ha tokenizado la tarjeta, se guarda para poder
                    // cobrar al cliente en el futuro sin que vuelva a meterla. Que esto falle no
                    // puede tirar el procesado del pago: el cobro ya está hecho.
                    GuardarTarjetaDeLaNotificacion(resultado, pago);

                    // Issue #143: Contabilizar con resiliencia - si falla, el correo debe enviarse igualmente
                    string errorContabilizacion = null;
                    try
                    {
                        // NestoAPI#436: el cobro de un pedido de la app NO va por el circuito del
                        // enlace de pago (que apunta un cobro contra el extracto del cliente): entra
                        // como Prepago del pedido, igual que hace CanalesExternos con PrestaShop, y
                        // se aplica al facturarlo. Contabilizarlo por los dos sitios seria contarlo
                        // dos veces.
                        if (EsAltaTarjeta(pago))
                        {
                            // NestoAPI#178: 0 EUR solo para tokenizar. El token ya está guardado
                            // (GuardarTarjetaDeLaNotificacion); no hay dinero que apuntar.
                        }
                        else if (EsPagoDePedido(pago))
                        {
                            await AnadirPrepagoAlPedido(pago, db).ConfigureAwait(false);
                        }
                        else
                        {
                            await ContabilizarCobro(pago).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorContabilizacion = ObtenerMensajeCompletoExcepcion(ex);
                        _logService.LogError($"[ProcesarNotificacion] Error al contabilizar cobro. Orden: {pago.NumeroOrden}, Error: {errorContabilizacion}", ex);
                    }

                    // Issue #139/#142/#143: Correo post-cobro a administración (siempre, incluso si falló la contabilización)
                    // NestoAPI#436: en los pedidos de la app, solo si algo ha fallado. Son cobros
                    // online de una tienda: avisar a administracion de cada compra seria ruido, y
                    // el ruido acaba en que nadie mira el correo que si importa.
                    // NestoAPI#178: el alta de tarjeta (0 EUR) tampoco avisa: no hay cobro.
                    if ((!EsPagoDePedido(pago) && !EsAltaTarjeta(pago)) || errorContabilizacion != null)
                    {
                        EnviarCorreoPostCobro(pago, errorContabilizacion);
                    }
                }
                else
                {
                    pago.Estado = Constantes.EstadosPagoTPV.DENEGADO;
                    await db.SaveChangesAsync().ConfigureAwait(false);

                    // Issue #156: Regenerar enlace de pago si no se ha superado el límite de reintentos
                    try
                    {
                        await RegenerarPagoDenegado(pago, db).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logService.LogError($"[ProcesarNotificacion] Error al regenerar pago denegado. Orden: {pago.NumeroOrden}, Error: {ex.Message}", ex);
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// #445 (TEMPORAL, 02/09/26): deja en ELMAH un JSON de Redsys completo (claves y valores,
        /// token tapado) para poder analizar qué manda nuestro terminal. Nunca tira el flujo.
        /// </summary>
        internal void LogDiagnosticoRedsys(string que, string numeroOrden, string json)
        {
            try
            {
                _logService.LogError($"[Redsys diag #445] {que}. Orden: {numeroOrden}. {json ?? "(sin JSON)"}");
            }
            catch
            {
                // El diagnóstico nunca puede romper un cobro
            }
        }

        /// <summary>
        /// NestoAPI#178: da de alta (o refresca) la tarjeta guardada del cliente con el token que
        /// viene en la notificación de un pago autorizado. Sin cliente no hay a quién asignarla y
        /// no se guarda nada.
        /// </summary>
        internal void GuardarTarjetaDeLaNotificacion(ResultadoValidacionNotificacion resultado, PagoTPV pago)
        {
            if (!resultado.TieneTokenTarjeta || string.IsNullOrWhiteSpace(pago.Cliente))
            {
                return;
            }

            // Los últimos dígitos NO son obligatorios (01/09/26: el terminal no tiene activado el
            // envío de datos de tarjeta y el alta se perdió por un NOT NULL). Se deja rastro; la
            // notificación completa ya la loguea ProcesarNotificacion (#445).
            if (string.IsNullOrWhiteSpace(resultado.UltimosDigitosTarjeta))
            {
                _logService.LogError($"[Tarjetas] La notificación de la orden {pago.NumeroOrden} trae token pero " +
                    $"no el número de tarjeta (el terminal no manda datos de tarjeta). La tarjeta se guarda " +
                    $"igualmente como '{TarjetaCliente.Describir(resultado.MarcaTarjeta, null, resultado.FechaCaducidadTarjeta)}'.");
            }

            try
            {
                _tarjetaStore.GuardarOActualizar(new TarjetaCliente
                {
                    Empresa = pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = pago.Cliente?.Trim(),
                    Contacto = pago.Contacto?.Trim(),
                    TokenRedsys = resultado.TokenTarjeta,
                    CofTxnId = resultado.CofTxnId,
                    UltimosDigitos = resultado.UltimosDigitosTarjeta,
                    TipoTarjeta = resultado.TipoTarjeta,
                    MarcaTarjeta = resultado.MarcaTarjeta,
                    FechaCaducidad = resultado.FechaCaducidadTarjeta,
                    UsuarioCreacion = pago.Usuario
                });
            }
            catch (Exception ex)
            {
                _logService.LogError($"[Tarjetas] No se pudo guardar el token de la tarjeta del " +
                    $"cliente {pago.Cliente?.Trim()} (orden {pago.NumeroOrden}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// NestoAPI#178: arranca el alta de una tarjeta SIN cobro — una autorización de 0 EUR en
        /// la pasarela, con autenticación fuerte del cliente y tokenización. Es el pago inicial
        /// (CIT, COF_INI=S) que ampara los cobros con token posteriores; con esto el PRIMER
        /// pedido del cliente ya puede ir por el flujo cobrar-primero, sin el hueco de "pedido
        /// creado y pasarela KO".
        ///
        /// <para>Requiere que el terminal tenga habilitadas las operaciones de importe 0; si el
        /// banco no las activa, el plan B es una preautorización de 1 EUR anulada.</para>
        /// </summary>
        public async Task<RespuestaIniciarPago> IniciarAltaTarjeta(SolicitudAltaTarjeta solicitud, string usuario)
        {
            if (string.IsNullOrWhiteSpace(solicitud?.Cliente))
            {
                throw new ArgumentException("El alta de tarjeta necesita un cliente");
            }

            string urlBase = "https://api.nuevavision.es";

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosTPVVirtual(
                0m,
                "Alta de tarjeta",
                solicitud.Correo,
                solicitud.Cliente,
                urlBase + "/api/Pagos/NotificacionRedsys",
                solicitud.UrlOk ?? urlBase + "/pago/ok.html",
                solicitud.UrlKo ?? urlBase + "/pago/ko.html",
                metodoPago: "C", // solo tarjeta: Bizum no deja token
                solicitarToken: true);

            using (NVEntities db = new NVEntities())
            {
                var pago = new PagoTPV
                {
                    NumeroOrden = parametros.NumeroOrden,
                    Tipo = Constantes.TiposPagoTPV.ALTA_TARJETA,
                    Empresa = solicitud.Empresa ?? Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = solicitud.Cliente,
                    Contacto = solicitud.Contacto,
                    Importe = 0m,
                    Descripcion = "Alta de tarjeta",
                    Correo = solicitud.Correo,
                    Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                    FechaCreacion = DateTime.Now,
                    Usuario = usuario,
                    TokenAcceso = Guid.NewGuid()
                };
                db.PagosTPV.Add(pago);
                await db.SaveChangesAsync().ConfigureAwait(false);

                return new RespuestaIniciarPago
                {
                    IdPago = pago.Id,
                    UrlRedsys = _redsysService.UrlFormularioRedsys,
                    Ds_SignatureVersion = parametros.Ds_SignatureVersion,
                    Ds_MerchantParameters = parametros.Ds_MerchantParameters,
                    Ds_Signature = parametros.Ds_Signature,
                    TokenAcceso = pago.TokenAcceso
                };
            }
        }

        /// <summary>
        /// NestoAPI#178: es una autorización de 0 EUR solo para tokenizar? No mueve dinero: ni
        /// contabiliza, ni prepago, ni correos — solo la fila de TarjetasClientes.
        /// </summary>
        internal static bool EsAltaTarjeta(PagoTPV pago)
        {
            return pago != null
                && string.Equals(pago.Tipo?.Trim(), Constantes.TiposPagoTPV.ALTA_TARJETA, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// NestoAPI#178: la tarjeta guardada con ese id SI es de ese cliente y se puede usar;
        /// null en cualquier otro caso (misma respuesta si no existe que si es de otro).
        /// </summary>
        public TarjetaCliente TarjetaGuardadaDe(string empresa, string cliente, int tarjetaId)
        {
            TarjetaCliente tarjeta = _tarjetaStore.ObtenerPorId(tarjetaId);
            if (tarjeta == null
                || !tarjeta.Usable
                || !string.Equals(tarjeta.Cliente?.Trim(), cliente?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(tarjeta.Empresa?.Trim(), (empresa ?? Empresas.EMPRESA_POR_DEFECTO).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return tarjeta;
        }

        /// <summary>
        /// NestoAPI#178/#181: cobro directo y síncrono con una tarjeta guardada. Al contrario que
        /// el flujo de pasarela (crear pedido -> cobrar -> esperar la notificación), aquí el
        /// resultado se sabe en el momento, y eso permite el flujo que quiere la app: cobrar
        /// PRIMERO y crear el pedido solo si el cobro se autoriza (KO = no se crea nada).
        ///
        /// <para>El PagoTPV se crea sin Documento (el pedido aún no existe); cuando el pedido se
        /// crea, <see cref="AplicarCobroAlPedido"/> lo enlaza y apunta el Prepago.</para>
        /// </summary>
        public async Task<ResultadoCobroTarjetaGuardada> CobrarConTarjetaGuardada(SolicitudCobroTarjetaGuardada solicitud, string usuario)
        {
            if (solicitud == null || solicitud.Importe <= 0)
            {
                throw new ArgumentException("El importe debe ser mayor que cero");
            }

            TarjetaCliente tarjeta = _tarjetaStore.ObtenerPorId(solicitud.TarjetaId);
            if (tarjeta == null
                || !string.Equals(tarjeta.Cliente?.Trim(), solicitud.Cliente?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(tarjeta.Empresa?.Trim(), solicitud.Empresa?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // La misma respuesta si no existe que si es de otro: no se filtra qué ids hay
                return new ResultadoCobroTarjetaGuardada
                {
                    Autorizado = false,
                    MensajeError = "No encontramos esa tarjeta guardada. Elige otra forma de pago."
                };
            }
            if (!tarjeta.Usable)
            {
                string motivo = tarjeta.Caducada
                    ? $"La tarjeta ({tarjeta.Descripcion}) está caducada."
                    : $"La tarjeta ({tarjeta.Descripcion}) no se puede usar ahora mismo.";
                return new ResultadoCobroTarjetaGuardada
                {
                    Autorizado = false,
                    UltimosDigitos = tarjeta.UltimosDigitos,
                    Descripcion = tarjeta.Descripcion,
                    MensajeError = motivo + " Paga con otra tarjeta para volver a activarla."
                };
            }

            solicitud.Descripcion = FormateadorConcepto.Normalizar(solicitud.Descripcion);

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosCobroConToken(
                solicitud.Importe,
                solicitud.Descripcion,
                solicitud.Cliente,
                tarjeta.TokenRedsys,
                tarjeta.CofTxnId);

            int idPago;
            using (NVEntities db = new NVEntities())
            {
                var pago = new PagoTPV
                {
                    NumeroOrden = parametros.NumeroOrden,
                    Tipo = Constantes.TiposPagoTPV.PEDIDO_APP,
                    Empresa = solicitud.Empresa ?? Empresas.EMPRESA_POR_DEFECTO,
                    Cliente = solicitud.Cliente,
                    Contacto = solicitud.Contacto,
                    Importe = solicitud.Importe,
                    Descripcion = solicitud.Descripcion,
                    Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                    FechaCreacion = DateTime.Now,
                    Usuario = usuario,
                    TokenAcceso = Guid.NewGuid()
                };
                db.PagosTPV.Add(pago);
                await db.SaveChangesAsync().ConfigureAwait(false);
                idPago = pago.Id;
            }

            RespuestaRedsys respuesta;
            try
            {
                respuesta = await _redsysService.EnviarPeticionREST(parametros).ConfigureAwait(false);
                // #445 (TEMPORAL): la respuesta completa del POST REST de cobro con token
                LogDiagnosticoRedsys("Respuesta REST al cobro con tarjeta guardada", parametros.NumeroOrden,
                    RedsysService.ParaDiagnostico(respuesta?.JsonCrudo));
            }
            catch (Exception ex)
            {
                await ActualizarEstadoCobro(idPago, Constantes.EstadosPagoTPV.DENEGADO, null, null).ConfigureAwait(false);
                _logService.LogError($"[Tarjetas] Error al cobrar con tarjeta guardada. Orden: {parametros.NumeroOrden}, Error: {ex.Message}", ex);
                return new ResultadoCobroTarjetaGuardada
                {
                    Autorizado = false,
                    IdPago = idPago,
                    NumeroOrden = parametros.NumeroOrden,
                    UltimosDigitos = tarjeta.UltimosDigitos,
                    Descripcion = tarjeta.Descripcion,
                    // Redsys rechazó la petición sin procesarla (SIS0xxx): el código va aparte
                    // para que el que cobra pueda caer al plan B si es el SIS0883 del terminal.
                    CodigoErrorRedsys = (ex as RedsysRestException)?.ErrorCode,
                    MensajeError = "No hemos podido conectar con el banco. Inténtalo de nuevo en unos minutos."
                };
            }

            bool autorizado = int.TryParse(respuesta?.Ds_Response, out int codigo) && codigo >= 0 && codigo <= 99;

            await ActualizarEstadoCobro(idPago,
                autorizado ? Constantes.EstadosPagoTPV.AUTORIZADO : Constantes.EstadosPagoTPV.DENEGADO,
                respuesta?.Ds_Response,
                respuesta?.Ds_AuthorisationCode).ConfigureAwait(false);

            try
            {
                _tarjetaStore.RegistrarUso(tarjeta.Id, autorizado);
            }
            catch (Exception ex)
            {
                _logService.LogError($"[Tarjetas] No se pudo registrar el uso de la tarjeta {tarjeta.Id}: {ex.Message}", ex);
            }

            return new ResultadoCobroTarjetaGuardada
            {
                Autorizado = autorizado,
                IdPago = idPago,
                NumeroOrden = parametros.NumeroOrden,
                CodigoRespuesta = respuesta?.Ds_Response,
                UltimosDigitos = tarjeta.UltimosDigitos,
                Descripcion = tarjeta.Descripcion,
                MensajeError = autorizado
                    ? null
                    : $"El banco no ha autorizado el cobro en la tarjeta ({tarjeta.Descripcion}). " +
                      "Puedes intentarlo con otra tarjeta."
            };
        }

        /// <summary>
        /// NestoAPI#178: enlaza un cobro con tarjeta guardada con el pedido recién creado y apunta
        /// el Prepago (el mismo circuito que los cobros de pasarela de la app, NestoAPI#436).
        /// </summary>
        public async Task AplicarCobroAlPedido(int idPago, int pedido)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV.FirstOrDefaultAsync(p => p.Id == idPago).ConfigureAwait(false);
                if (pago == null)
                {
                    throw new InvalidOperationException($"No existe el pago {idPago} para aplicarlo al pedido {pedido}");
                }
                pago.Documento = pedido.ToString();
                pago.FechaActualizacion = DateTime.Now;
                await db.SaveChangesAsync().ConfigureAwait(false);
                await AnadirPrepagoAlPedido(pago, db).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// NestoAPI#178: devuelve un cobro hecho con tarjeta guardada. Es la red de seguridad del
        /// flujo cobrar-primero: si el pedido no se llega a crear, el dinero vuelve. Devuelve true
        /// si Redsys acepta la devolución.
        /// </summary>
        public async Task<bool> DevolverCobro(int idPago, string motivo)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV.FirstOrDefaultAsync(p => p.Id == idPago).ConfigureAwait(false);
                if (pago == null)
                {
                    return false;
                }

                ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosDevolucion(pago.Importe, pago.NumeroOrden);
                bool devuelto;
                try
                {
                    RespuestaRedsys respuesta = await _redsysService.EnviarPeticionREST(parametros).ConfigureAwait(false);
                    // #445 (TEMPORAL): la respuesta completa del POST REST de devolución
                    LogDiagnosticoRedsys("Respuesta REST a la devolución", pago.NumeroOrden,
                        RedsysService.ParaDiagnostico(respuesta?.JsonCrudo));
                    // 0900 = devolución aceptada
                    devuelto = string.Equals(respuesta?.Ds_Response?.Trim(), "0900", StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    _logService.LogError($"[Tarjetas] Error al devolver el cobro {pago.NumeroOrden}: {ex.Message}", ex);
                    devuelto = false;
                }

                if (devuelto)
                {
                    pago.Estado = Constantes.EstadosPagoTPV.DEVUELTO;
                    pago.FechaActualizacion = DateTime.Now;
                    await db.SaveChangesAsync().ConfigureAwait(false);
                }

                _logService.LogError($"[Tarjetas] Cobro {pago.NumeroOrden} ({pago.Importe:N2} EUR, cliente " +
                    $"{pago.Cliente?.Trim()}): {motivo}. Devolución {(devuelto ? "ACEPTADA" : "FALLIDA: revisar en el panel de Redsys")}.");

                return devuelto;
            }
        }

        private async Task ActualizarEstadoCobro(int idPago, string estado, string codigoRespuesta, string codigoAutorizacion)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV.FirstOrDefaultAsync(p => p.Id == idPago).ConfigureAwait(false);
                if (pago == null)
                {
                    return;
                }
                pago.Estado = estado;
                pago.CodigoRespuesta = codigoRespuesta;
                pago.CodigoAutorizacion = codigoAutorizacion;
                pago.FechaActualizacion = DateTime.Now;
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// NestoAPI#436: es este cobro el de un pedido que ha creado un cliente desde la app? El
        /// numero de pedido viaja en Documento, asi que no hizo falta columna nueva.
        /// </summary>
        internal static bool EsPagoDePedido(PagoTPV pago)
        {
            return pago != null
                && string.Equals(pago.Tipo?.Trim(), Constantes.TiposPagoTPV.PEDIDO_APP, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pago.Documento?.Trim(), out int numero)
                && numero > 0;
        }

        /// <summary>
        /// NestoAPI#436: concepto del prepago. Al contabilizar se le antepone "Prepago", así que
        /// dice QUÉ es (tarjeta de la app) y de QUÉ pedido, y detrás el numero de orden de Redsys,
        /// que es unico por cobro y es lo que hace la insercion idempotente: Redsys puede mandar
        /// la misma notificacion mas de una vez y el pedido no puede acabar con el cobro por
        /// duplicado. El campo son 50 caracteres.
        /// </summary>
        internal static string ConceptoPrepagoPedido(int numeroPedido, string numeroOrden)
        {
            string concepto = $"Tarjeta app pedido {numeroPedido} {numeroOrden?.Trim()}";
            return concepto.Length > 50 ? concepto.Substring(0, 50) : concepto;
        }

        /// <summary>
        /// NestoAPI#436: apunta el cobro como Prepago del pedido. A partir de ahi el pedido deja de
        /// estar retenido en el picking (PedidoPicking.RetenidoPorPrepago) y, al facturarlo, el
        /// prepago se aplica contra la cuenta de Redsys.
        /// </summary>
        private async Task AnadirPrepagoAlPedido(PagoTPV pago, NVEntities db)
        {
            int numeroPedido = int.Parse(pago.Documento.Trim());
            string empresa = pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO;
            string concepto = ConceptoPrepagoPedido(numeroPedido, pago.NumeroOrden);

            CabPedidoVta pedido = await db.CabPedidoVtas
                .FirstOrDefaultAsync(cab => cab.Empresa == empresa && cab.Número == numeroPedido)
                .ConfigureAwait(false);

            if (pedido == null)
            {
                // Dinero cobrado y ningun pedido al que aplicarlo: hay que enterarse hoy, no al
                // cuadrar el mes. La excepcion la recoge ProcesarNotificacion, que igualmente
                // manda el correo post-cobro con el error.
                string mensaje = $"[Prepago pedido app] Cobrado {pago.Importe:N2} EUR del pedido " +
                    $"{numeroPedido} (orden {pago.NumeroOrden}, cliente {pago.Cliente?.Trim()}), " +
                    "pero ese pedido NO existe. El prepago no se ha creado.";
                _logService.LogError(mensaje);
                throw new InvalidOperationException(mensaje);
            }

            bool yaEstaba = await db.Prepagos
                .AnyAsync(pre => pre.Empresa == empresa && pre.Pedido == numeroPedido && pre.ConceptoAdicional == concepto)
                .ConfigureAwait(false);
            if (yaEstaba)
            {
                // Notificacion repetida de Redsys: el cobro no se duplica.
                return;
            }

            _ = db.Prepagos.Add(new Prepago
            {
                Empresa = empresa,
                Pedido = numeroPedido,
                Importe = pago.Importe,
                CuentaContable = Constantes.Prepagos.CUENTA_REDSYS,
                ConceptoAdicional = concepto,
                Usuario = pago.Usuario
            });
            _ = await db.SaveChangesAsync().ConfigureAwait(false);

            // El prepago tiene que cubrir el total para que el pedido salga en el picking. Si no
            // llega (un pago parcial, o el pedido ha cambiado despues de cobrarlo), mejor saberlo
            // ahora que cuando el cliente pregunte por que no le ha llegado.
            decimal totalPedido = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == numeroPedido)
                .Select(l => l.Total)
                .DefaultIfEmpty(0)
                .SumAsync()
                .ConfigureAwait(false);
            decimal prepagos = await db.Prepagos
                .Where(pre => pre.Empresa == empresa && pre.Pedido == numeroPedido)
                .Select(pre => pre.Importe)
                .DefaultIfEmpty(0)
                .SumAsync()
                .ConfigureAwait(false);
            if (prepagos < Math.Round(totalPedido, 2, MidpointRounding.AwayFromZero))
            {
                _logService.LogError($"[Prepago pedido app] El pedido {numeroPedido} sigue retenido en " +
                    $"el picking: cobrado {prepagos:N2} EUR de {totalPedido:N2} EUR (orden {pago.NumeroOrden}).");
            }
        }

        private async Task ContabilizarCobro(PagoTPV pago)
        {
            if (string.IsNullOrWhiteSpace(pago.Cliente))
            {
                return;
            }

            string cuentaBanco = _lectorParametros.LeerParametro(
                pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO,
                "(defecto)",
                Parametros.Claves.CuentaBancoTarjeta);

            if (string.IsNullOrWhiteSpace(cuentaBanco))
            {
                return;
            }

            string empresa = pago.Empresa?.Trim() ?? Empresas.EMPRESA_POR_DEFECTO;
            string concepto = $"Pago TPV {pago.Descripcion}";
            if (concepto.Length > 50)
            {
                concepto = concepto.Substring(0, 50);
            }

            List<PreContabilidad> lineas = ConstruirLineasCobro(pago, cuentaBanco, empresa, concepto);

            // Si hay Liquidado, copiar Nº_Documento, Vendedor y Ruta del movimiento original
            var lineasConLiquidado = lineas.Where(l => l.Liquidado.HasValue).ToList();
            if (lineasConLiquidado.Any())
            {
                var numerosOrden = lineasConLiquidado.Select(l => l.Liquidado.Value).Distinct().ToList();
                using (NVEntities db = new NVEntities())
                {
                    var movimientosOriginales = await db.ExtractosCliente
                        .Where(e => e.Empresa == empresa && numerosOrden.Contains(e.Nº_Orden))
                        .ToListAsync()
                        .ConfigureAwait(false);

                    foreach (var linea in lineasConLiquidado)
                    {
                        var original = movimientosOriginales.FirstOrDefault(e => e.Nº_Orden == linea.Liquidado.Value);
                        if (original != null)
                        {
                            linea.Nº_Documento = original.Nº_Documento?.Trim();
                            linea.Vendedor = original.Vendedor?.Trim();
                            linea.Ruta = original.Ruta?.Trim();
                        }
                    }
                }

                // Si hay líneas con Liquidado, usar el Nº_Documento del movimiento original también en el banco (como hace Cajas)
                var lineaConDocumentoOriginal = lineasConLiquidado.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Nº_Documento));
                var lineaBanco = lineas.FirstOrDefault(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
                if (lineaConDocumentoOriginal != null && lineaBanco != null)
                {
                    lineaBanco.Nº_Documento = lineaConDocumentoOriginal.Nº_Documento;
                }
            }

            await _contabilidadService.CrearLineasYContabilizarDiario(lineas).ConfigureAwait(false);
        }

        /// <summary>
        /// Construye las líneas de PreContabilidad de un cobro TPV: una línea HABER por cada efecto
        /// liquidado (o una línea legacy si no hay efectos) y la línea DEBE del banco por el importe cobrado.
        /// No accede a base de datos para que sea testeable de forma aislada.
        /// </summary>
        internal static List<PreContabilidad> ConstruirLineasCobro(PagoTPV pago, string cuentaBanco, string empresa, string concepto)
        {
            var lineas = new List<PreContabilidad>();

            if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
            {
                // Pagos multiples: una linea HABER por cada efecto
                foreach (var efecto in pago.PagosTPV_Efectos)
                {
                    string docEfecto = efecto.Documento?.Trim();
                    if (string.IsNullOrWhiteSpace(docEfecto))
                    {
                        docEfecto = pago.NumeroOrden?.Length > 10
                            ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                            : pago.NumeroOrden;
                    }

                    lineas.Add(new PreContabilidad
                    {
                        Empresa = empresa,
                        Nº_Cuenta = pago.Cliente,
                        Contacto = efecto.Contacto?.Trim() ?? pago.Contacto ?? "0",
                        TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                        TipoApunte = TiposExtractoCliente.PAGO,
                        Haber = efecto.Importe,
                        Concepto = concepto,
                        Nº_Documento = docEfecto,
                        Efecto = efecto.Efecto?.Trim(),
                        Diario = "_CobrosTPV",
                        Fecha = DateTime.Today,
                        FechaVto = DateTime.Today,
                        Asiento = 1,
                        Asiento_Automático = true,
                        Delegación = efecto.Delegacion?.Trim() ?? "ALG",
                        FormaVenta = efecto.FormaVenta?.Trim() ?? Constantes.FormasVenta.TIENDA_ONLINE,
                        FormaPago = Constantes.FormasPago.TARJETA,
                        Vendedor = efecto.Vendedor?.Trim(),
                        Liquidado = efecto.ExtractoClienteId,
                        Origen = Empresas.EMPRESA_POR_DEFECTO,
                        Usuario = "NestoAPI",
                        Fecha_Modificación = DateTime.Now
                    });
                }
            }
            else
            {
                // Pago individual legacy (sin tabla de efectos)
                string delegacion = pago.Delegacion?.Trim() ?? "ALG";
                string formaVenta = pago.FormaVenta?.Trim() ?? Constantes.FormasVenta.TIENDA_ONLINE;
                string documento = pago.Documento?.Trim();
                if (string.IsNullOrWhiteSpace(documento))
                {
                    documento = pago.NumeroOrden?.Length > 10
                        ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                        : pago.NumeroOrden;
                }

                lineas.Add(new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = pago.Cliente,
                    Contacto = pago.Contacto ?? "0",
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    TipoApunte = TiposExtractoCliente.PAGO,
                    Haber = pago.Importe,
                    Concepto = concepto,
                    Nº_Documento = documento,
                    Efecto = pago.Efecto,
                    Diario = "_CobrosTPV",
                    Fecha = DateTime.Today,
                    FechaVto = DateTime.Today,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Delegación = delegacion,
                    FormaVenta = formaVenta,
                    FormaPago = Constantes.FormasPago.TARJETA,
                    Vendedor = pago.Vendedor,
                    Liquidado = pago.ExtractoClienteId,
                    Origen = Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = "NestoAPI",
                    Fecha_Modificación = DateTime.Now
                });
            }

            string docBanco = pago.NumeroOrden?.Length > 10
                ? pago.NumeroOrden.Substring(pago.NumeroOrden.Length - 10)
                : pago.NumeroOrden;

            // Si el importe cobrado no coincide con la suma de los efectos liquidados
            // (p.ej. se reclama 40,65 € contra un movimiento de -150 €, o un cobro parcial),
            // se añade una línea de cliente con el resto para que el asiento cuadre:
            //  - diferencia > 0 -> queda un saldo a favor del cliente (HABER pendiente)
            //  - diferencia < 0 -> queda deuda pendiente del cliente (DEBE pendiente)
            // En el caso normal (importe == suma de efectos) la diferencia es 0 y no se añade nada.
            decimal sumaHaberEfectos = lineas.Sum(l => l.Haber);
            decimal diferencia = pago.Importe - sumaHaberEfectos;
            if (diferencia != 0)
            {
                PreContabilidad plantilla = lineas.First();
                lineas.Add(new PreContabilidad
                {
                    Empresa = empresa,
                    Nº_Cuenta = pago.Cliente,
                    Contacto = plantilla.Contacto,
                    TipoCuenta = Constantes.Contabilidad.TiposCuenta.CLIENTE,
                    TipoApunte = TiposExtractoCliente.PAGO,
                    Haber = diferencia > 0 ? diferencia : 0,
                    Debe = diferencia < 0 ? -diferencia : 0,
                    Concepto = concepto,
                    Nº_Documento = docBanco,
                    Diario = "_CobrosTPV",
                    Fecha = DateTime.Today,
                    FechaVto = DateTime.Today,
                    Asiento = 1,
                    Asiento_Automático = true,
                    Delegación = plantilla.Delegación,
                    FormaVenta = plantilla.FormaVenta,
                    FormaPago = Constantes.FormasPago.TARJETA,
                    Vendedor = plantilla.Vendedor,
                    Liquidado = null,
                    Origen = Empresas.EMPRESA_POR_DEFECTO,
                    Usuario = "NestoAPI",
                    Fecha_Modificación = DateTime.Now
                });
            }

            // Linea banco (DEBE) - siempre una sola linea por el total cobrado
            lineas.Insert(0, new PreContabilidad
            {
                Empresa = empresa,
                Nº_Cuenta = cuentaBanco,
                TipoCuenta = Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                TipoApunte = TiposExtractoCliente.PAGO,
                Debe = pago.Importe,
                Concepto = concepto,
                Nº_Documento = docBanco,
                Diario = "_CobrosTPV",
                Fecha = DateTime.Today,
                FechaVto = DateTime.Today,
                Asiento = 1,
                Asiento_Automático = true,
                Delegación = "ALG",
                FormaVenta = Constantes.FormasVenta.TIENDA_ONLINE,
                FormaPago = Constantes.FormasPago.TARJETA,
                Origen = Empresas.EMPRESA_POR_DEFECTO,
                Usuario = "NestoAPI",
                Fecha_Modificación = DateTime.Now
            });

            return lineas;
        }

        internal static List<EfectoAPagar> NormalizarEfectos(SolicitudPagoTPV solicitud)
        {
            if (solicitud.Efectos != null && solicitud.Efectos.Any())
            {
                return solicitud.Efectos;
            }

            // Compatibilidad: si no hay Efectos pero hay ExtractoClienteId, crear uno
            if (solicitud.ExtractoClienteId.HasValue)
            {
                return new List<EfectoAPagar>
                {
                    new EfectoAPagar
                    {
                        ExtractoClienteId = solicitud.ExtractoClienteId.Value,
                        Importe = solicitud.Importe,
                        Documento = solicitud.Documento,
                        Efecto = solicitud.Efecto,
                        Contacto = solicitud.Contacto,
                        Vendedor = solicitud.Vendedor,
                        FormaVenta = solicitud.FormaVenta,
                        Delegacion = solicitud.Delegacion,
                        TipoApunte = solicitud.TipoApunte
                    }
                };
            }

            return new List<EfectoAPagar>();
        }

        public async Task<PagoTPVDTO> ConsultarPago(int idPago)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == idPago)
                    .ConfigureAwait(false);

                return pago != null ? MapearADTO(pago) : null;
            }
        }

        public async Task<PagoTPVDTO> ConsultarAuditoria(string numeroOrden)
        {
            using (NVEntities db = new NVEntities())
            {
                PagoTPV pago = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.NumeroOrden == numeroOrden)
                    .ConfigureAwait(false);

                return pago != null ? MapearADTO(pago) : null;
            }
        }

        public async Task<List<PagoTPVDTO>> ListarPorCliente(string empresa, string cliente, int limite = 20)
        {
            using (NVEntities db = new NVEntities())
            {
                string empresaPadded = empresa.PadRight(3);
                var pagos = await db.PagosTPV
                    .Include(p => p.PagosTPV_Efectos)
                    .AsNoTracking()
                    .Where(p => p.Empresa == empresaPadded && p.Cliente == cliente)
                    .OrderByDescending(p => p.FechaCreacion)
                    .Take(limite)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return pagos.Select(p => MapearADTO(p)).ToList();
            }
        }

        internal static PagoTPVDTO MapearADTO(PagoTPV pago)
        {
            var dto = new PagoTPVDTO
            {
                Id = pago.Id,
                NumeroOrden = pago.NumeroOrden,
                Tipo = pago.Tipo,
                Empresa = pago.Empresa?.Trim(),
                Cliente = pago.Cliente?.Trim(),
                Contacto = pago.Contacto?.Trim(),
                Importe = pago.Importe,
                Descripcion = pago.Descripcion,
                Correo = pago.Correo,
                Movil = pago.Movil,
                Estado = pago.Estado,
                CodigoRespuesta = pago.CodigoRespuesta,
                CodigoAutorizacion = pago.CodigoAutorizacion,
                FechaCreacion = pago.FechaCreacion,
                FechaActualizacion = pago.FechaActualizacion,
                Usuario = pago.Usuario,
                ExtractoClienteId = pago.ExtractoClienteId,
                Documento = pago.Documento?.Trim(),
                Efecto = pago.Efecto?.Trim(),
                Vendedor = pago.Vendedor?.Trim(),
                FormaVenta = pago.FormaVenta?.Trim(),
                Delegacion = pago.Delegacion?.Trim(),
                TipoApunte = pago.TipoApunte?.Trim(),
                PagoOriginalId = pago.PagoOriginalId
            };

            if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
            {
                dto.Efectos = pago.PagosTPV_Efectos.Select(e => new EfectoTPVDTO
                {
                    Id = e.Id,
                    ExtractoClienteId = e.ExtractoClienteId,
                    Importe = e.Importe,
                    Documento = e.Documento?.Trim(),
                    Efecto = e.Efecto?.Trim(),
                    Contacto = e.Contacto?.Trim(),
                    Vendedor = e.Vendedor?.Trim(),
                    FormaVenta = e.FormaVenta?.Trim(),
                    Delegacion = e.Delegacion?.Trim(),
                    TipoApunte = e.TipoApunte?.Trim()
                }).ToList();
            }

            return dto;
        }

        private const string URL_LOGO = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";
        private const string URL_GOOGLE_PLAY_NESTOTIENDAS = "https://play.google.com/store/apps/details?id=com.nuevavision.nestotiendas";
        private const string URL_BADGE_GOOGLE_PLAY = "https://upload.wikimedia.org/wikipedia/commons/7/78/Google_Play_Store_badge_EN.svg";

        /// <summary>
        /// Envía correo al cliente con el enlace de pago generado.
        /// Issue #139: Correo pre-cobro. Si el correo es null, no envía.
        /// </summary>
        internal void EnviarCorreoPreCobro(PagoTPV pago, List<EfectoAPagar> efectos, string urlPaginaPago)
        {
            if (string.IsNullOrWhiteSpace(pago.Correo))
            {
                return;
            }

            try
            {
                string filasEfectos = "";
                bool alternar = false;
                if (efectos != null && efectos.Any())
                {
                    foreach (var e in efectos)
                    {
                        string bgColor = alternar ? "background-color:#faf5f7;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e8ec'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e8ec;text-align:right;white-space:nowrap'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                string seccionEfectos = efectos != null && efectos.Any()
                    ? $@"<table style='border-collapse:collapse;width:100%;margin:20px 0'>
                        <tr style='background:#f8f4f6'>
                            <th style='padding:10px;text-align:left;border-bottom:2px solid #d4a5b5;color:#6b3a5d'>Documento</th>
                            <th style='padding:10px;text-align:right;border-bottom:2px solid #d4a5b5;color:#6b3a5d'>Importe</th>
                        </tr>
                        {filasEfectos}
                        <tr style='background:#f8f4f6'>
                            <td style='padding:10px;font-weight:bold;color:#6b3a5d'>Total</td>
                            <td style='padding:10px;font-weight:bold;text-align:right;color:#6b3a5d'>{pago.Importe:N2} &euro;</td>
                        </tr>
                    </table>"
                    : $"<p style='font-size:24px;font-weight:bold;color:#6b3a5d;text-align:center;margin:20px 0'>{pago.Importe:N2} &euro;</p>";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f8f4f6;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f8f4f6;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08)'>
                <!-- Cabecera con logo -->
                <tr>
                    <td style='background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%);padding:30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:180px;height:auto' />
                    </td>
                </tr>
                <!-- Contenido -->
                <tr>
                    <td style='padding:30px 35px'>
                        <h1 style='color:#6b3a5d;font-size:22px;margin:0 0 15px 0'>Enlace de pago</h1>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 5px 0'>
                            Estimado cliente,
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Le hemos preparado un enlace de pago seguro para que pueda realizar su abono de forma r&aacute;pida y c&oacute;moda.
                        </p>
                        {seccionEfectos}
                        <table role='presentation' cellpadding='0' cellspacing='0' border='0' align='center' style='margin:25px auto'>
                            <tr>
                                <td bgcolor='#6b3a5d' style='border-radius:8px;background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%)'>
                                    <a href='{urlPaginaPago}' style='display:inline-block;padding:14px 40px;color:#ffffff;text-decoration:none;font-size:16px;font-weight:bold;letter-spacing:0.5px;border-radius:8px'>
                                        Realizar pago seguro
                                    </a>
                                </td>
                            </tr>
                        </table>
                        <p style='color:#999;font-size:12px;text-align:center;margin:15px 0 0 0'>
                            El pago se realiza a trav&eacute;s de la pasarela segura Redsys, con la m&aacute;xima protecci&oacute;n para sus datos.
                        </p>
                    </td>
                </tr>
                <!-- Pie -->
                <tr>
                    <td style='background:#f8f4f6;padding:20px 35px;border-top:1px solid #f0e8ec'>
                        <p style='color:#888;font-size:12px;margin:0 0 8px 0;text-align:center'>
                            &iquest;Lo sab&iacute;a? Puede pagar todas sus facturas de forma r&aacute;pida y c&oacute;moda desde nuestra app.
                        </p>
                        <p style='text-align:center;margin:0 0 16px 0'>
                            <a href='{URL_GOOGLE_PLAY_NESTOTIENDAS}' target='_blank' style='text-decoration:none'>
                                <img src='{URL_BADGE_GOOGLE_PLAY}' alt='Disponible en Google Play' style='width:130px;height:auto;display:inline-block;border:0' />
                            </a>
                        </p>
                        <p style='color:#999;font-size:12px;margin:0;text-align:center'>
                            &iquest;Tiene alguna duda? Contacte con nosotros en
                            <a href='mailto:administracion@nuevavision.es' style='color:#8b5a6b'>administracion@nuevavision.es</a>
                        </p>
                        <p style='color:#ccc;font-size:11px;margin:8px 0 0 0;text-align:center'>
                            Nueva Visi&oacute;n &middot; Distribuci&oacute;n de productos de est&eacute;tica y peluquer&iacute;a profesional
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "Nueva Visión");
                    mail.To.Add(pago.Correo);
                    mail.Subject = $"Enlace de pago - {pago.Descripcion ?? "Nueva Visión"}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPreCobro] Error enviando correo a {pago.Correo}: {ex.Message}");
            }
        }

        /// <summary>
        /// Envía correo a administración con los detalles del cobro realizado.
        /// Issue #139: Correo post-cobro.
        /// </summary>
        internal void EnviarCorreoPostCobro(PagoTPV pago, string errorContabilizacion = null)
        {
            try
            {
                string filasEfectos = "";
                if (pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any())
                {
                    bool alternar = false;
                    foreach (var e in pago.PagosTPV_Efectos)
                    {
                        string bgColor = alternar ? "background-color:#f9f9f9;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Efecto?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee'>{e.Contacto?.Trim()}</td>" +
                            $"<td style='padding:8px;border-bottom:1px solid #eee;text-align:right'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                // Issue #143: Alerta de error de contabilización
                string seccionError = !string.IsNullOrEmpty(errorContabilizacion)
                    ? $@"<div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                        <strong style='color:#c0392b'>ERROR: No se ha podido contabilizar el cobro</strong>
                        <p style='color:#721c24;margin:8px 0 0 0;font-size:13px'>{HttpUtility.HtmlEncode(errorContabilizacion)}</p>
                    </div>"
                    : "";

                string seccionEfectos = pago.PagosTPV_Efectos != null && pago.PagosTPV_Efectos.Any()
                    ? $@"<h3 style='color:#333;font-size:14px;margin:20px 0 10px 0'>Efectos cobrados</h3>
                    <table style='border-collapse:collapse;width:100%'>
                        <tr style='background:#f5f5f5'>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Documento</th>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Efecto</th>
                            <th style='padding:8px;text-align:left;border-bottom:2px solid #ddd;font-size:12px'>Contacto</th>
                            <th style='padding:8px;text-align:right;border-bottom:2px solid #ddd;font-size:12px'>Importe</th>
                        </tr>
                        {filasEfectos}
                    </table>"
                    : "";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <!-- Cabecera -->
                <tr>
                    <td style='background:#27ae60;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>Cobro NestoPago realizado</h1>
                    </td>
                </tr>
                <!-- Datos del cobro -->
                <tr>
                    <td style='padding:25px 30px'>
                        {seccionError}
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:140px'>Cliente</td><td style='padding:6px 0;font-weight:bold'>{pago.Cliente?.Trim()}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Concepto</td><td style='padding:6px 0'>{HttpUtility.HtmlEncode(pago.Descripcion?.Trim())}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Importe</td><td style='padding:6px 0;font-weight:bold;color:#27ae60;font-size:18px'>{pago.Importe:N2} &euro;</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>N&ordm; Orden</td><td style='padding:6px 0'>{pago.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Autorizaci&oacute;n</td><td style='padding:6px 0'>{pago.CodigoAutorizacion}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Fecha</td><td style='padding:6px 0'>{pago.FechaActualizacion:g}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Correo cliente</td><td style='padding:6px 0'>{pago.Correo}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Usuario</td><td style='padding:6px 0'>{pago.Usuario}</td></tr>
                        </table>
                        {seccionEfectos}
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);

                    // Issue #142: CC al creador del enlace de pago
                    string correoCreador = ObtenerCorreoUsuario(pago.Usuario);
                    if (!string.IsNullOrEmpty(correoCreador))
                    {
                        try
                        {
                            mail.CC.Add(correoCreador);
                        }
                        catch
                        {
                            // Si el correo no es válido, ignorar
                        }
                    }

                    string prefijoAsunto = !string.IsNullOrEmpty(errorContabilizacion) ? "ERROR " : "";
                    mail.Subject = $"{prefijoAsunto}Cobro NestoPago: {pago.Importe:C} - Cliente {pago.Cliente?.Trim()}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPostCobro] Error enviando correo post-cobro: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene el correo electrónico de un usuario.
        /// Si el usuario ya es un email, lo devuelve directamente.
        /// Si es un usuario de Windows (DOMINIO\Usuario), lee el parámetro Parametros.Claves.CorreoDefecto.
        /// </summary>
        internal string ObtenerCorreoUsuario(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }

            // Si ya parece un email, devolverlo directamente
            if (usuario.Contains("@"))
            {
                return usuario.Trim();
            }

            // Extraer nombre de usuario sin dominio (NUEVAVISION\Lidia → Lidia)
            string nombreUsuario = usuario.Contains("\\")
                ? usuario.Substring(usuario.IndexOf('\\') + 1)
                : usuario;

            try
            {
                return _lectorParametros.LeerParametro(
                    Empresas.EMPRESA_POR_DEFECTO, nombreUsuario, Parametros.Claves.CorreoDefecto);
            }
            catch
            {
                return null;
            }
        }

        internal void EnviarCorreoAlertaPago(string titulo, string detalle, ResultadoValidacionNotificacion resultado)
        {
            try
            {
                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <tr>
                    <td style='background:#c0392b;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>ALERTA NestoPago</h1>
                    </td>
                </tr>
                <tr>
                    <td style='padding:25px 30px'>
                        <div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                            <strong style='color:#c0392b'>{HttpUtility.HtmlEncode(titulo)}</strong>
                        </div>
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:160px'>N&ordm; Orden Redsys</td><td style='padding:6px 0;font-weight:bold'>{resultado?.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Pago autorizado</td><td style='padding:6px 0;font-weight:bold'>{(resultado?.PagoAutorizado == true ? "SI" : "NO")}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo respuesta</td><td style='padding:6px 0'>{resultado?.CodigoRespuesta}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo autorizaci&oacute;n</td><td style='padding:6px 0'>{resultado?.CodigoAutorizacion}</td></tr>
                        </table>
                        <p style='color:#555;font-size:13px;margin:15px 0 0 0;padding:10px;background:#f9f9f9;border-radius:4px;word-break:break-all'>
                            {HttpUtility.HtmlEncode(detalle)}
                        </p>
                        <p style='color:#999;font-size:12px;margin:15px 0 0 0'>
                            Este correo se ha generado autom&aacute;ticamente porque se ha recibido una notificaci&oacute;n de Redsys
                            que no se ha podido procesar correctamente. Es necesario investigar y actuar manualmente.
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);
                    mail.Subject = $"ALERTA NestoPago: {titulo} - Orden {resultado?.NumeroOrden}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoAlertaPago] Error enviando correo de alerta: {ex.Message}. Alerta original: {detalle}");
            }
        }

        internal const int LIMITE_REINTENTOS_PAGO = 3;

        internal async Task RegenerarPagoDenegado(PagoTPV pagoDenegado, NVEntities db)
        {
            // El cobro de un pedido de la app (#436) no es un enlace de pago: el pago se cancela
            // o deniega DENTRO de la app y es la app quien ofrece reintentarlo. Generar aquí un
            // enlace y mandar el correo de "Pago no procesado" confunde (detectado por Carlos el
            // 01/09/26 con el primer pedido real: canceló en la pasarela y le llegó el correo del
            // circuito de enlaces). El pedido queda retenido por prepago, que es el estado seguro.
            if (string.Equals(pagoDenegado.Tipo?.Trim(), Constantes.TiposPagoTPV.PEDIDO_APP, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // NestoAPI#178: el alta de tarjeta tampoco es un enlace de pago: si el cliente
            // cancela o el banco deniega, la app lo enseña y se puede volver a intentar desde
            // alli. Ni enlace nuevo ni correo.
            if (EsAltaTarjeta(pagoDenegado))
            {
                return;
            }

            // Buscar el pago raíz de la cadena de reintentos
            int pagoRaizId = pagoDenegado.PagoOriginalId ?? pagoDenegado.Id;

            // Contar reintentos previos vinculados al pago raíz
            int reintentosPrevios = await db.PagosTPV
                .CountAsync(p => p.PagoOriginalId == pagoRaizId)
                .ConfigureAwait(false);

            if (reintentosPrevios >= LIMITE_REINTENTOS_PAGO)
            {
                EnviarCorreoLimiteReintentos(pagoDenegado);
                return;
            }

            // Crear nuevos parámetros Redsys
            string urlBase = "https://api.nuevavision.es";
            string urlNotificacion = urlBase + "/api/Pagos/NotificacionRedsys";
            string urlOk = urlBase + "/pago/ok.html";
            string urlKo = urlBase + "/pago/ko.html";

            ParametrosRedsysFirmados parametros = _redsysService.CrearParametrosTPVVirtual(
                pagoDenegado.Importe,
                pagoDenegado.Descripcion,
                pagoDenegado.Correo,
                pagoDenegado.Cliente,
                urlNotificacion,
                urlOk,
                urlKo,
                pagoDenegado.MetodoPago);

            // Crear nuevo PagoTPV con los mismos datos
            var nuevoPago = new PagoTPV
            {
                NumeroOrden = parametros.NumeroOrden,
                Tipo = pagoDenegado.Tipo,
                Empresa = pagoDenegado.Empresa,
                Cliente = pagoDenegado.Cliente,
                Contacto = pagoDenegado.Contacto,
                Importe = pagoDenegado.Importe,
                Descripcion = pagoDenegado.Descripcion,
                Correo = pagoDenegado.Correo,
                Movil = pagoDenegado.Movil,
                ExtractoClienteId = pagoDenegado.ExtractoClienteId,
                Documento = pagoDenegado.Documento,
                Efecto = pagoDenegado.Efecto,
                Vendedor = pagoDenegado.Vendedor,
                FormaVenta = pagoDenegado.FormaVenta,
                Delegacion = pagoDenegado.Delegacion,
                TipoApunte = pagoDenegado.TipoApunte,
                Estado = Constantes.EstadosPagoTPV.PENDIENTE,
                FechaCreacion = DateTime.Now,
                Usuario = pagoDenegado.Usuario,
                TokenAcceso = Guid.NewGuid(),
                PagoOriginalId = pagoRaizId,
                MetodoPago = pagoDenegado.MetodoPago
            };

            db.PagosTPV.Add(nuevoPago);
            await db.SaveChangesAsync().ConfigureAwait(false);

            // Duplicar efectos
            if (pagoDenegado.PagosTPV_Efectos != null)
            {
                foreach (var efecto in pagoDenegado.PagosTPV_Efectos)
                {
                    db.PagosTPV_Efectos.Add(new PagoTPV_Efecto
                    {
                        IdPago = nuevoPago.Id,
                        ExtractoClienteId = efecto.ExtractoClienteId,
                        Importe = efecto.Importe,
                        Documento = efecto.Documento,
                        Efecto = efecto.Efecto,
                        Contacto = efecto.Contacto,
                        Vendedor = efecto.Vendedor,
                        FormaVenta = efecto.FormaVenta,
                        Delegacion = efecto.Delegacion,
                        TipoApunte = efecto.TipoApunte
                    });
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
            }

            string urlPaginaPago = $"https://api.nuevavision.es/pago/{nuevoPago.TokenAcceso}";
            EnviarCorreoPagoDenegado(pagoDenegado, urlPaginaPago);
        }

        internal void EnviarCorreoPagoDenegado(PagoTPV pagoDenegado, string urlNuevoPago)
        {
            try
            {
                string filasEfectos = "";
                if (pagoDenegado.PagosTPV_Efectos != null && pagoDenegado.PagosTPV_Efectos.Any())
                {
                    bool alternar = false;
                    foreach (var e in pagoDenegado.PagosTPV_Efectos)
                    {
                        string bgColor = alternar ? "background-color:#fef5f5;" : "";
                        filasEfectos +=
                            $"<tr style='{bgColor}'>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e0e0'>{e.Documento?.Trim()}</td>" +
                            $"<td style='padding:10px;border-bottom:1px solid #f0e0e0;text-align:right;white-space:nowrap'>{e.Importe:N2} &euro;</td></tr>";
                        alternar = !alternar;
                    }
                }

                string seccionEfectos = pagoDenegado.PagosTPV_Efectos != null && pagoDenegado.PagosTPV_Efectos.Any()
                    ? $@"<table style='border-collapse:collapse;width:100%;margin:20px 0'>
                        <tr style='background:#fef5f5'>
                            <th style='padding:10px;text-align:left;border-bottom:2px solid #e8b4b4;color:#8b3a3a'>Documento</th>
                            <th style='padding:10px;text-align:right;border-bottom:2px solid #e8b4b4;color:#8b3a3a'>Importe</th>
                        </tr>
                        {filasEfectos}
                        <tr style='background:#fef5f5'>
                            <td style='padding:10px;font-weight:bold;color:#8b3a3a'>Total</td>
                            <td style='padding:10px;font-weight:bold;text-align:right;color:#8b3a3a'>{pagoDenegado.Importe:N2} &euro;</td>
                        </tr>
                    </table>"
                    : $"<p style='font-size:24px;font-weight:bold;color:#8b3a3a;text-align:center;margin:20px 0'>{pagoDenegado.Importe:N2} &euro;</p>";

                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#fef5f5;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#fef5f5;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08)'>
                <!-- Cabecera con logo -->
                <tr>
                    <td style='background:linear-gradient(135deg,#c0392b 0%,#8b3a3a 100%);padding:30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:180px;height:auto' />
                    </td>
                </tr>
                <!-- Contenido -->
                <tr>
                    <td style='padding:30px 35px'>
                        <h1 style='color:#8b3a3a;font-size:22px;margin:0 0 15px 0'>Pago no procesado</h1>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 5px 0'>
                            Estimado cliente,
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Le informamos de que su intento de pago no ha podido ser procesado. No se ha realizado ning&uacute;n cargo en su tarjeta.
                        </p>
                        <p style='color:#555;font-size:15px;line-height:1.6;margin:0 0 20px 0'>
                            Hemos generado autom&aacute;ticamente un nuevo enlace de pago para que pueda reintentar la operaci&oacute;n cuando lo desee.
                        </p>
                        {seccionEfectos}
                        <table role='presentation' cellpadding='0' cellspacing='0' border='0' align='center' style='margin:25px auto'>
                            <tr>
                                <td bgcolor='#6b3a5d' style='border-radius:8px;background:linear-gradient(135deg,#8b5a6b 0%,#6b3a5d 100%)'>
                                    <a href='{urlNuevoPago}' style='display:inline-block;padding:14px 40px;color:#ffffff;text-decoration:none;font-size:16px;font-weight:bold;letter-spacing:0.5px;border-radius:8px'>
                                        Reintentar pago seguro
                                    </a>
                                </td>
                            </tr>
                        </table>
                        <p style='color:#999;font-size:12px;text-align:center;margin:15px 0 0 0'>
                            El pago se realiza a trav&eacute;s de la pasarela segura Redsys, con la m&aacute;xima protecci&oacute;n para sus datos.
                        </p>
                    </td>
                </tr>
                <!-- Pie -->
                <tr>
                    <td style='background:#fef5f5;padding:20px 35px;border-top:1px solid #f0e0e0'>
                        <p style='color:#888;font-size:12px;margin:0 0 8px 0;text-align:center'>
                            &iquest;Lo sab&iacute;a? Puede pagar todas sus facturas de forma r&aacute;pida y c&oacute;moda desde nuestra app.
                        </p>
                        <p style='text-align:center;margin:0 0 16px 0'>
                            <a href='{URL_GOOGLE_PLAY_NESTOTIENDAS}' target='_blank' style='text-decoration:none'>
                                <img src='{URL_BADGE_GOOGLE_PLAY}' alt='Disponible en Google Play' style='width:130px;height:auto;display:inline-block;border:0' />
                            </a>
                        </p>
                        <p style='color:#999;font-size:12px;margin:0;text-align:center'>
                            &iquest;Tiene alguna duda? Contacte con nosotros en
                            <a href='mailto:administracion@nuevavision.es' style='color:#8b5a6b'>administracion@nuevavision.es</a>
                        </p>
                        <p style='color:#ccc;font-size:11px;margin:8px 0 0 0;text-align:center'>
                            Nueva Visi&oacute;n &middot; Distribuci&oacute;n de productos de est&eacute;tica y peluquer&iacute;a profesional
                        </p>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                // Enviar al cliente
                if (!string.IsNullOrWhiteSpace(pagoDenegado.Correo))
                {
                    using (var mail = new MailMessage())
                    {
                        mail.From = new MailAddress(Correos.CORREO_ADMON, "Nueva Visión");
                        mail.To.Add(pagoDenegado.Correo);
                        mail.CC.Add(Correos.CORREO_ADMON);
                        mail.Subject = $"Pago no procesado - {pagoDenegado.Descripcion ?? "Nueva Visión"} - Nuevo enlace disponible";
                        mail.Body = html;
                        mail.IsBodyHtml = true;
                        _servicioCorreo.EnviarCorreoSMTP(mail);
                    }
                }
                else
                {
                    // Sin correo de cliente, avisar solo a administración
                    EnviarCorreoLimiteReintentos(pagoDenegado);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoPagoDenegado] Error enviando correo: {ex.Message}");
            }
        }

        internal void EnviarCorreoLimiteReintentos(PagoTPV pago)
        {
            try
            {
                string html = $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='margin:0;padding:0;background-color:#f4f4f4;font-family:-apple-system,BlinkMacSystemFont,""Segoe UI"",Roboto,Arial,sans-serif'>
    <table width='100%' cellpadding='0' cellspacing='0' style='background-color:#f4f4f4;padding:20px 0'>
        <tr><td align='center'>
            <table width='600' cellpadding='0' cellspacing='0' style='background:white;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.06)'>
                <tr>
                    <td style='background:#c0392b;padding:20px 30px;text-align:center'>
                        <img src='{URL_LOGO}' alt='Nueva Visi&oacute;n' style='max-width:120px;height:auto;margin-bottom:8px' />
                        <h1 style='color:white;font-size:18px;margin:0'>L&iacute;mite de reintentos NestoPago</h1>
                    </td>
                </tr>
                <tr>
                    <td style='padding:25px 30px'>
                        <div style='background:#fdecea;border:1px solid #f5c6cb;border-radius:6px;padding:15px;margin:0 0 15px 0'>
                            <strong style='color:#c0392b'>Se ha superado el l&iacute;mite de {LIMITE_REINTENTOS_PAGO} reintentos autom&aacute;ticos</strong>
                            <p style='color:#721c24;margin:8px 0 0 0;font-size:13px'>
                                El cliente ha agotado los intentos autom&aacute;ticos de pago. Es necesario intervenci&oacute;n manual para generar un nuevo enlace.
                            </p>
                        </div>
                        <table style='width:100%;font-size:14px'>
                            <tr><td style='padding:6px 0;color:#888;width:140px'>Cliente</td><td style='padding:6px 0;font-weight:bold'>{pago.Cliente?.Trim()}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Importe</td><td style='padding:6px 0;font-weight:bold;color:#c0392b;font-size:18px'>{pago.Importe:N2} &euro;</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>N&ordm; Orden</td><td style='padding:6px 0'>{pago.NumeroOrden}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>C&oacute;digo respuesta</td><td style='padding:6px 0'>{pago.CodigoRespuesta}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Correo cliente</td><td style='padding:6px 0'>{pago.Correo}</td></tr>
                            <tr><td style='padding:6px 0;color:#888'>Usuario</td><td style='padding:6px 0'>{pago.Usuario}</td></tr>
                        </table>
                    </td>
                </tr>
            </table>
        </td></tr>
    </table>
</body>
</html>";

                using (var mail = new MailMessage())
                {
                    mail.From = new MailAddress(Correos.CORREO_ADMON, "NestoPago");
                    mail.To.Add(Correos.CORREO_ADMON);
                    mail.Subject = $"LIMITE REINTENTOS NestoPago: {pago.Importe:C} - Cliente {pago.Cliente?.Trim()}";
                    mail.Body = html;
                    mail.IsBodyHtml = true;
                    _servicioCorreo.EnviarCorreoSMTP(mail);
                }
            }
            catch (Exception ex)
            {
                _logService.LogError($"[EnviarCorreoLimiteReintentos] Error enviando correo: {ex.Message}");
            }
        }

        private static string ObtenerMensajeCompletoExcepcion(Exception ex)
        {
            var mensajes = new System.Text.StringBuilder();
            var actual = ex;
            while (actual != null)
            {
                if (mensajes.Length > 0) mensajes.Append(" → ");
                mensajes.Append(actual.Message);
                actual = actual.InnerException;
            }
            return mensajes.ToString();
        }
    }
}
