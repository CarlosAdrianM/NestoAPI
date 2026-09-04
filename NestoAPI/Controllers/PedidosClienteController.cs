using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Seguridad;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using System.Web.Http.Results;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#436: el carrito de TiendasNuevaVision. Un cliente final, autenticado con el JWT de
    /// la app, crea su propio pedido sin que intervenga nadie.
    ///
    /// <para><b>La regla que ordena el diseño: el cliente dice qué y cuánto; todo lo demás lo
    /// decide el servidor.</b> Y "lo decide el servidor" significa ignorar esos campos si vienen
    /// en la petición, no confiar en que la app los mande bien: un cliente que manipule la
    /// petición no puede cambiar su precio, su descuento ni sus portes. Por eso
    /// <see cref="PedidoClienteRequest"/> ni siquiera los tiene.</para>
    ///
    /// <para>Lo que distingue a este controller no es exigir un usuario determinado, sino el
    /// <b>canal</b> desde el que llega la petición: el claim <c>cliente</c> del JWT que emite
    /// <c>AuthController.CrearJWTAsync</c>. Las reglas de acceso son las que ya estaban escritas y
    /// en producción en <see cref="ValidadorAccesoCliente"/>.</para>
    ///
    /// <para>El pedido se crea ANTES de cobrar (opción B de la issue): un pedido sin cobrar es
    /// recuperable —se cancela o se persigue— y un cobro sin pedido es un problema contable.
    /// Además no hay riesgo de que salga sin pagar: al crearse con plazos de pago PRE, el picking
    /// lo retiene hasta que los prepagos cubren el total (ver PedidoPicking.RetenidoPorPrepago).</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Pedidos")]
    public class PedidosClienteController : ApiController
    {
        /// <summary>TNV#66: cuántos días de pedidos ve el cliente en la app si no pide otra cosa.</summary>
        internal const int DIAS_DE_PEDIDOS_POR_DEFECTO = 60;

        /// <summary>Tope para que nadie se traiga el histórico entero en una llamada.</summary>
        internal const int MAXIMO_DIAS_DE_PEDIDOS = 365;

        private readonly NVEntities db;
        private readonly IServicioPagos servicioPagos;

        // OJO con los constructores: los controllers se resuelven por el contenedor
        // (AddControllersAsServices), que elige el que puede construir entero. NVEntities no está
        // registrado, asi que el unico resoluble es este; el otro es para los tests.
        public PedidosClienteController()
            : this(new NVEntities(), new ServicioPagos(new RedsysService(), new ContabilidadService(), new LectorParametrosUsuario()))
        {
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
        }

        // Para poder hacer tests sobre el controlador
        public PedidosClienteController(NVEntities db, IServicioPagos servicioPagos)
        {
            this.db = db;
            this.servicioPagos = servicioPagos;
        }

        // POST: api/Pedidos/Cliente
        [HttpPost]
        [Route("Cliente")]
        [ResponseType(typeof(PedidoClienteResponse))]
        public async Task<IHttpActionResult> PostPedidoCliente(PedidoClienteRequest peticion)
        {
            // NestoAPI#446: quien hace pedidos sin ver los precios no elige cómo paga (la pasarela
            // enseñaría el importe): se ignora lo que pida y se resuelve la forma habitual.
            bool sinPrecios = PoliticaPreciosOcultos.OcultaImportes(User?.Identity);
            if (sinPrecios)
            {
                PoliticaPreciosOcultos.ForzarFormaDePagoHabitual(peticion);
            }

            PedidoPreparado preparado = await PrepararPedido(peticion).ConfigureAwait(false);
            if (preparado.Error != null)
            {
                return preparado.Error;
            }

            // NestoAPI#178: con tarjeta guardada y cobro directo (MIT), el cobro es síncrono y va
            // PRIMERO: si el banco no lo autoriza, no se crea nada y la app vuelve al carrito.
            // Mientras el terminal no permita MIT (SIS0883, 02/09/26) se va por el plan B: el
            // pedido se crea y el cliente confirma el pago en la pasarela con su tarjeta guardada,
            // sin volver a teclearla (ModoCobroTarjetaGuardada).
            TarjetaCliente tarjetaParaLaPasarela = null;
            if (peticion.PagarConTarjetaGuardada)
            {
                if (ModoCobroTarjetaGuardada.EsCobroDirecto)
                {
                    IHttpActionResult cobrado = await CrearPedidoCobrandoTarjetaGuardada(peticion, preparado).ConfigureAwait(false);
                    if (cobrado != null)
                    {
                        return cobrado;
                    }
                    // null = el terminal ha contestado SIS0883 (MIT aún no operativo): se sigue
                    // por el plan B en esta misma petición, sin que el cliente pierda el pedido.
                }
                if (!peticion.TarjetaId.HasValue)
                {
                    return BadRequest("Falta la tarjeta con la que pagar (TarjetaId)");
                }
                tarjetaParaLaPasarela = servicioPagos.TarjetaGuardadaDe(
                    preparado.Pedido.empresa, preparado.Pedido.cliente, peticion.TarjetaId.Value);
                if (tarjetaParaLaPasarela == null)
                {
                    return BadRequest("No encontramos esa tarjeta guardada. Elige otra forma de pago.");
                }
            }

            // Se crea por el camino de siempre, que es el que añade los portes, valida ofertas y
            // descuentos, manda el correo y guarda.
            PedidosVentaController controllerPedidos = CrearControllerPedidos();

            IHttpActionResult resultado;
            try
            {
                resultado = await controllerPedidos.PostPedidoVenta(preparado.Pedido).ConfigureAwait(false);
            }
            catch (PedidoValidacionException ex)
            {
                // El pedido no se ha creado. Que un pedido se quede esperando aprobación sin
                // decir nada es peor que un error: el cliente se entera de por qué.
                return BadRequest(MotivoParaElCliente(ex));
            }
            catch (NestoBusinessException ex)
            {
                return BadRequest(ex.Message);
            }

            if (!(resultado is CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>))
            {
                // BadRequest, Conflict... lo que haya respondido el endpoint de siempre
                return resultado;
            }

            PedidoClienteResponse respuesta = ConstruirRespuesta(preparado.Pedido, preparado.FormaPago, preparado.PlazosPago);

            if (sinPrecios)
            {
                // El pedido se ha creado con sus precios reales; a este usuario no se le cuentan
                PoliticaPreciosOcultos.OcultarImportes(respuesta);
            }

            if (respuesta.RequierePago)
            {
                await ArrancarPago(respuesta, preparado, tarjetaParaLaPasarela).ConfigureAwait(false);
            }

            return Ok(respuesta);
        }

        /// <summary>
        /// NestoAPI#178: el flujo cobrar-primero con la tarjeta guardada del cliente.
        ///
        /// <para>OK = se cobra, se crea el pedido y se le aplica el cobro como Prepago. KO = no
        /// se crea nada y el error le dice al cliente por qué. Y la rama fea —cobro autorizado
        /// pero el pedido no se puede crear— deshace el cobro con una devolución; si hasta la
        /// devolución falla, ELMAH y correo, porque dinero cobrado sin pedido no puede esperar
        /// al cuadre de fin de mes.</para>
        /// </summary>
        /// <summary>
        /// NestoAPI#178: con el cobro directo activado, ¿hay que caer al plan B (pasarela con la
        /// tarjeta cargada) en vez de dar el KO al cliente? Solo cuando Redsys rechaza la petición
        /// con el SIS0883 del terminal: cualquier otro rechazo o denegación del banco es un KO.
        /// </summary>
        internal static bool CaerAlPlanB(ResultadoCobroTarjetaGuardada cobro)
        {
            return cobro != null && cobro.TerminalSinMIT;
        }

        /// <summary>
        /// Cobra con la tarjeta guardada por REST y después crea el pedido. Devuelve null cuando
        /// el terminal no admite MIT (<see cref="CaerAlPlanB"/>): el que llama sigue por el plan B.
        /// </summary>
        private async Task<IHttpActionResult> CrearPedidoCobrandoTarjetaGuardada(
            PedidoClienteRequest peticion, PedidoPreparado preparado)
        {
            if (!peticion.TarjetaId.HasValue)
            {
                return BadRequest("Falta la tarjeta con la que pagar (TarjetaId)");
            }

            // NestoAPI#452: el importe tiene que ser el DEFINITIVO (con IVA y con portes) antes de
            // cobrar. Sin esto se cobraba la base imponible de los productos: 0,75 EUR en vez de
            // 0,91 EUR el 03/09/26, porque el porcentaje de IVA y la línea de portes solo se
            // rellenaban dentro de PostPedidoVenta, que va DESPUÉS del cobro.
            await CompletarImportesDelPedido(preparado).ConfigureAwait(false);
            decimal importeACobrar = preparado.Pedido.Total;

            ResultadoCobroTarjetaGuardada cobro = await servicioPagos.CobrarConTarjetaGuardada(
                new SolicitudCobroTarjetaGuardada
                {
                    Cliente = preparado.Pedido.cliente?.Trim(),
                    Contacto = preparado.Pedido.contacto?.Trim(),
                    Importe = importeACobrar,
                    Descripcion = "Pago pedido app",
                    TarjetaId = peticion.TarjetaId.Value
                }, preparado.Pedido.Usuario).ConfigureAwait(false);

            if (CaerAlPlanB(cobro))
            {
                // Comercia activó MIT el 02/09/26 "a falta del barrido nocturno": mientras el
                // terminal siga diciendo SIS0883, el cliente confirma en la pasarela con su
                // tarjeta cargada (plan B). Se apunta para saber cuándo deja de pasar.
                ElmahHelper.Log(new Exception(
                    $"[Tarjetas] El terminal sigue sin admitir MIT ({ResultadoCobroTarjetaGuardada.SIS_TERMINAL_SIN_MIT}): " +
                    $"el pedido del cliente {preparado.Pedido.cliente?.Trim()} ({preparado.Pedido.Total:N2} EUR, orden {cobro.NumeroOrden}) " +
                    "va por la pasarela con la tarjeta guardada (plan B). Si esto sigue pasando, avisar a Comercia."));
                return null;
            }

            if (!cobro.Autorizado)
            {
                // KO: sin pedido, sin cargo. La app vuelve al carrito tal cual estaba.
                return BadRequest(cobro.MensajeError ?? "El banco no ha autorizado el cobro.");
            }

            PedidosVentaController controllerPedidos = CrearControllerPedidos();
            IHttpActionResult resultado;
            string motivoFallo = null;
            try
            {
                resultado = await controllerPedidos.PostPedidoVenta(preparado.Pedido).ConfigureAwait(false);
                if (!(resultado is CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>))
                {
                    motivoFallo = "El pedido no se ha podido crear.";
                }
            }
            catch (PedidoValidacionException ex)
            {
                resultado = null;
                motivoFallo = MotivoParaElCliente(ex);
            }
            catch (NestoBusinessException ex)
            {
                resultado = null;
                motivoFallo = ex.Message;
            }

            if (motivoFallo != null)
            {
                bool devuelto = await servicioPagos.DevolverCobro(cobro.IdPago,
                    "el pedido de la app no se llegó a crear").ConfigureAwait(false);
                if (!devuelto)
                {
                    ElmahHelper.Log(new Exception(
                        $"[Pedido app] Cobro {cobro.NumeroOrden} ({preparado.Pedido.Total:N2} EUR) con tarjeta " +
                        $"guardada SIN pedido y la devolución ha fallado: anular a mano en el panel de Redsys."));
                }
                string queHaPasadoConElCobro = devuelto
                    ? "No se te ha cobrado nada: hemos anulado el cargo."
                    : "Estamos anulando el cargo; si lo ves en tu cuenta, desaparecerá en unos días.";
                return BadRequest($"{motivoFallo} {queHaPasadoConElCobro}");
            }

            PedidoClienteResponse respuesta = ConstruirRespuesta(preparado.Pedido, preparado.FormaPago, preparado.PlazosPago);
            // NestoAPI#452: el pedido ya está creado y PostPedidoVenta ha recalculado precios,
            // descuentos y portes. Si lo cobrado no coincide con lo que ha acabado valiendo, hay
            // que enterarse HOY: el cliente ha pagado de menos (o de más) y el prepago no cuadra.
            AvisarSiElCobroNoCuadra(importeACobrar, respuesta.Total, respuesta.Numero, cobro.NumeroOrden);
            respuesta.RequierePago = false;
            respuesta.Pagado = true;
            respuesta.TarjetaUltimosDigitos = cobro.UltimosDigitos;
            respuesta.TarjetaDescripcion = cobro.Descripcion;
            respuesta.Avisos.Clear();
            respuesta.Avisos.Add($"Pagado con tu tarjeta: {cobro.Descripcion}.");

            try
            {
                await servicioPagos.AplicarCobroAlPedido(cobro.IdPago, respuesta.Numero).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // El cliente ya tiene pedido y cargo correctos; lo que ha fallado es el enlace
                // interno (Documento + Prepago). Sin el prepago el pedido queda retenido en el
                // picking, así que hay que enterarse hoy.
                ElmahHelper.Log(new Exception(
                    $"[Pedido app] El pedido {respuesta.Numero} está cobrado (orden {cobro.NumeroOrden}) " +
                    $"pero no se pudo aplicar el prepago: aplicarlo a mano o el picking no lo soltará. {ex.Message}", ex));
            }

            return Ok(respuesta);
        }

        /// <summary>
        /// El controller de pedidos de siempre, cableado para que actúe como si hubiera atendido
        /// él la petición (mismo principal, misma request, misma configuración).
        ///
        /// <para>Sin using al consumirlo: comparte el DbContext de este controller y su Dispose se
        /// lo llevaría por delante. Del contexto se encarga el Dispose de aquí abajo.</para>
        ///
        /// <para>El ORDEN de asignación no es capricho: RequestContext ANTES que Request. El setter
        /// de Request exige que el contexto que viaja dentro del HttpRequestMessage coincida con el
        /// RequestContext del controller destino y, si este aún tiene el suyo por defecto, revienta
        /// con "la propiedad de contexto de solicitud debe tener un valor nulo o coincidir con
        /// ApiController.RequestContext" (fallo del 01/09/26 en producción; los tests no lo veían
        /// porque en ellos Request es null).</para>
        /// </summary>
        internal PedidosVentaController CrearControllerPedidos()
        {
            PedidosVentaController controllerPedidos = new PedidosVentaController(db)
            {
                // El principal viaja en el RequestContext: el pedido lo crea el cliente del JWT.
                RequestContext = RequestContext
            };
            if (Request != null)
            {
                controllerPedidos.Request = Request;
            }
            if (Configuration != null)
            {
                controllerPedidos.Configuration = Configuration;
            }
            return controllerPedidos;
        }

        /// <summary>
        /// NestoAPI#436: arranca el cobro con tarjeta del pedido recien creado y devuelve a la app
        /// los parametros de Redsys ya firmados.
        ///
        /// <para>El importe es el del pedido, calculado por el servidor: por eso el cobro se
        /// arranca aqui y no dejando que la app llame a <c>api/Pagos</c> por su cuenta, donde
        /// podria mandar el importe que quisiera. Cuando Redsys confirma, el cobro entra como
        /// Prepago del pedido (ServicioPagos.ProcesarNotificacion).</para>
        ///
        /// <para>Si falla, el pedido ya esta creado y se devuelve igualmente con un aviso: un
        /// pedido sin cobrar es recuperable, y el picking no lo va a servir mientras no haya
        /// prepago que cubra el total.</para>
        /// </summary>
        private async Task ArrancarPago(PedidoClienteResponse respuesta, PedidoPreparado preparado, TarjetaCliente tarjetaGuardada = null)
        {
            try
            {
                respuesta.Pago = await servicioPagos.IniciarPago(new SolicitudPagoTPV
                {
                    Empresa = respuesta.Empresa,
                    Cliente = respuesta.Cliente,
                    Contacto = respuesta.Contacto,
                    Importe = respuesta.Total,
                    Descripcion = $"Pago pedido {respuesta.Numero}",
                    // Va a Redsys (DS_MERCHANT_CUSTOMER_MAIL, que ayuda en la autenticacion y en
                    // el justificante del banco). NO se le manda ningun enlace de pago: el cobro
                    // es online y ocurre en la propia app.
                    Correo = preparado.Correo,
                    Pedido = respuesta.Numero,
                    // NestoAPI#178 (plan B): con la referencia, Redsys enseña la tarjeta guardada
                    TarjetaGuardada = tarjetaGuardada
                }, preparado.Pedido.Usuario).ConfigureAwait(false);

                if (tarjetaGuardada != null)
                {
                    respuesta.TarjetaUltimosDigitos = tarjetaGuardada.UltimosDigitos;
                    respuesta.TarjetaDescripcion = tarjetaGuardada.Descripcion;
                    respuesta.Avisos.Add($"Confirma el pago con tu tarjeta guardada ({tarjetaGuardada.Descripcion}): " +
                        "no tendrás que volver a teclearla.");
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Pedido app] El pedido {respuesta.Numero} se ha creado pero no se ha podido " +
                    $"arrancar el cobro con tarjeta: {ex.Message}", ex));
                respuesta.Avisos.Add("El pedido se ha creado, pero no hemos podido abrir la pasarela de pago. " +
                    "Inténtalo de nuevo desde tus pedidos o llámanos.");
            }
        }

        /// <summary>
        /// NestoAPI#436 (aviso del equipo de la app): lo que cuesta el envío del carrito ANTES de
        /// crear el pedido, con lo que necesita el aviso de "te faltan X € para el envío gratis".
        ///
        /// <para>Nace de que <c>POST api/PedidosVenta/CalcularPortes</c> no le sirve a la app: aquel
        /// recibe la base imponible y el código postal en la petición, y aquí ninguno de los dos los
        /// puede decir el cliente. Este calcula el envío del MISMO pedido que se crearía —mismos
        /// precios, misma ficha, mismas condiciones de pago—, así que el importe que se enseña en el
        /// carrito es exactamente el que va a pagar.</para>
        /// </summary>
        // POST: api/Pedidos/Cliente/Portes
        [HttpPost]
        [Route("Cliente/Portes")]
        [ResponseType(typeof(PortesClienteResponse))]
        public async Task<IHttpActionResult> PostPortesCliente(PedidoClienteRequest peticion)
        {
            // NestoAPI#446: "te faltan X € para el envío gratis" es un importe
            if (PoliticaPreciosOcultos.OcultaImportes(User?.Identity))
            {
                return BadRequest(PoliticaPreciosOcultos.MOTIVO_PORTES);
            }

            PedidoPreparado preparado = await PrepararPedido(peticion).ConfigureAwait(false);
            if (preparado.Error != null)
            {
                return preparado.Error;
            }

            ResultadoPortes portes = CalcularPortesDelCarrito(preparado.Pedido, preparado.CodigoPostal);

            return Ok(new PortesClienteResponse
            {
                BaseImponibleProductos = portes.ImporteActualPedido,
                Portes = portes.ImportePortes,
                PortesGratis = portes.PortesGratis,
                ImporteMinimoSinPortes = portes.ImporteMinimoPedidoSinPortes,
                FaltaParaPortesGratis = portes.ImporteFaltaParaPortesGratis
            });
        }

        /// <summary>
        /// Todo lo que resuelve el servidor antes de tocar el pedido: quién pide, su ficha, sus
        /// condiciones de pago y el precio de cada línea. Lo comparten la creación del pedido y el
        /// cálculo de portes del carrito, para que los dos vean exactamente lo mismo.
        /// </summary>
        private class PedidoPreparado
        {
            public IHttpActionResult Error { get; set; }
            public PedidoVentaDTO Pedido { get; set; }
            public string FormaPago { get; set; }
            public string PlazosPago { get; set; }
            public string CodigoPostal { get; set; }

            /// <summary>Correo del cliente (del JWT), para el aviso previo al cobro.</summary>
            public string Correo { get; set; }
        }

        private async Task<PedidoPreparado> PrepararPedido(PedidoClienteRequest peticion)
        {
            // 1. Quién pide. El cliente sale SIEMPRE del JWT: si viniera en el cuerpo se ignora,
            //    que es lo que impide pedir en nombre de otro.
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente))
            {
                // Un empleado o un vendedor no tienen claim "cliente": este endpoint es solo para
                // clientes finales. Ellos tienen POST api/PedidosVenta, que es el de siempre.
                return new PedidoPreparado { Error = Unauthorized() };
            }
            ValidadorAccesoCliente.ResultadoValidacion acceso = ValidadorAccesoCliente.ValidarAcceso(identity, cliente);
            if (!acceso.Autorizado)
            {
                return new PedidoPreparado { Error = Unauthorized() };
            }

            // 2. Qué pide
            string errorPeticion = ConstructorPedidoCliente.ValidarPeticion(peticion);
            if (errorPeticion != null)
            {
                return new PedidoPreparado { Error = BadRequest(errorPeticion) };
            }

            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;

            // 3. Su ficha: de ahí salen iva, ruta, ccc, periodo de facturación, servir junto,
            //    vendedor y el código postal con el que se calculan los portes.
            ClienteDTO fichaCliente = await LeerFichaCliente(empresa, cliente).ConfigureAwait(false);
            if (fichaCliente == null)
            {
                return new PedidoPreparado { Error = BadRequest($"No se encuentra la ficha del cliente {cliente}") };
            }
            if (fichaCliente.estado < Constantes.Clientes.Estados.VISITA_PRESENCIAL)
            {
                return new PedidoPreparado { Error = BadRequest("La ficha del cliente no está activa: no se pueden crear pedidos") };
            }

            // 4. Las condiciones de pago, con la política del canal APP (NestoAPI#435): por
            //    defecto tarjeta al contado, crédito solo si su ficha lo permite y, con deuda,
            //    solo tarjeta. La política vive en PoliticaPagoCanal, aplicada por el mismo
            //    endpoint que consulta la app para pintar las opciones.
            CondicionesPagoResponse condiciones = await LeerCondicionesPago(empresa, cliente).ConfigureAwait(false);
            // NestoAPI#178: la tarjeta guardada es tarjeta a todos los efectos de forma y plazos
            bool pagaConTarjeta = peticion.PagarConTarjeta || peticion.PagarConTarjetaGuardada;
            string formaPagoSolicitada = pagaConTarjeta ? Constantes.FormasPago.TARJETA : peticion.FormaPago;
            string plazosPagoSolicitados = pagaConTarjeta ? Constantes.PlazosPago.PREPAGO : peticion.PlazosPago;
            string formaPago = PoliticaPagoCanal.ResolverFormaPago(condiciones, formaPagoSolicitada);
            string plazosPago = PoliticaPagoCanal.ResolverPlazosPago(condiciones, plazosPagoSolicitados);

            // NestoAPI#446: sin ver los precios no hay tarjeta (la pasarela enseña el importe):
            // la forma habitual de la ficha, y si solo queda la tarjeta, el pedido no se crea.
            if (PoliticaPreciosOcultos.OcultaImportes(identity))
            {
                PoliticaPreciosOcultos.FormaYPlazos habitual = PoliticaPreciosOcultos.ResolverFormaDePagoHabitual(condiciones);
                if (habitual == null)
                {
                    return new PedidoPreparado { Error = BadRequest(PoliticaPreciosOcultos.MOTIVO_SIN_FORMA_DE_PAGO_HABITUAL) };
                }
                formaPago = habitual.FormaPago;
                plazosPago = habitual.PlazosPago;
            }

            // 5. El precio y el descuento de cada línea los calcula el servidor, exactamente igual
            //    que GET api/Productos?cliente=&contacto=&cantidad=
            Dictionary<string, ProductoPlantillaDTO> precios;
            try
            {
                precios = await CalcularPrecios(empresa, fichaCliente, peticion.Lineas).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex)
            {
                return new PedidoPreparado { Error = BadRequest(ex.Message) };
            }
            string productoSinPrecio = peticion.Lineas
                .Select(l => l.Producto.Trim())
                .FirstOrDefault(p => !precios.ContainsKey(p));
            if (productoSinPrecio != null)
            {
                return new PedidoPreparado { Error = BadRequest($"No se ha podido calcular el precio del producto {productoSinPrecio}") };
            }

            return new PedidoPreparado
            {
                Pedido = ConstructorPedidoCliente.Construir(
                    peticion, fichaCliente, precios, formaPago, plazosPago, DateTime.Today),
                FormaPago = formaPago,
                PlazosPago = plazosPago,
                CodigoPostal = fichaCliente.codigoPostal?.Trim() ?? string.Empty,
                Correo = identity.FindFirst(ClaimTypes.Email)?.Value
            };
        }

        /// <summary>
        /// Los portes del carrito, con el mismo cálculo que hace PostPedidoVenta al crear el pedido
        /// (los dos montan el input con <see cref="GestorPortes.ConstruirInput"/>). AnadirPortes va
        /// siempre a true: suprimirlos es cosa de Almacén y Compras, no de un cliente.
        /// </summary>
        private ResultadoPortes CalcularPortesDelCarrito(PedidoVentaDTO pedido, string codigoPostal)
        {
            GestorPedidosVenta gestorPedidos = new GestorPedidosVenta(new ServicioPedidosVenta());
            gestorPedidos.RellenarEstadoProducto(pedido);
            decimal baseImponibleProductos = GestorPortes.CalcularBaseImponibleProductos(
                pedido.Lineas, pedido.servirJunto, new GestorStocks());
            PedidoPortesInput input = GestorPortes.ConstruirInput(
                pedido, codigoPostal, baseImponibleProductos, anadirPortes: true);
            return GestorPortes.CalcularPortes(input);
        }

        /// <summary>
        /// NestoAPI#452: deja el pedido con sus importes DEFINITIVOS antes de cobrarlo, que es lo
        /// que <see cref="PedidosVentaController.PostPedidoVenta"/> hace al crearlo pero que en el
        /// flujo "cobrar primero" llega tarde:
        /// <list type="number">
        /// <item>el porcentaje de IVA (y el recargo) de cada línea, que en el DTO recién construido
        /// vale 0 porque solo se ha puesto el CÓDIGO de IVA;</item>
        /// <item>la línea de portes, que hasta ahora no existía hasta después del cobro.</item>
        /// </list>
        /// <para>Se puede llamar antes de PostPedidoVenta sin duplicar nada: el POST vuelve a
        /// asignar los mismos porcentajes, y <see cref="GestorPortes.GestionarLineasPortes"/> solo
        /// añade la línea de portes si no la encuentra ya.</para>
        /// </summary>
        private async Task CompletarImportesDelPedido(PedidoPreparado preparado)
        {
            PedidoVentaDTO pedido = preparado.Pedido;
            if (pedido.ParametrosIva == null || !pedido.ParametrosIva.Any())
            {
                pedido.ParametrosIva = await db.ParametrosIVA
                    .Where(p => p.Empresa == pedido.empresa && p.IVA_Cliente_Prov == pedido.iva)
                    .Select(p => new ParametrosIvaBase
                    {
                        CodigoIvaProducto = p.IVA_Producto.Trim(),
                        PorcentajeIvaProducto = (decimal)p.C__IVA / 100,
                        PorcentajeRecargoEquivalencia = (decimal)p.C__RE / 100
                    }).ToListAsync().ConfigureAwait(false);
            }
            RellenarPorcentajesIva(pedido);

            ResultadoPortes portes = CalcularPortesDelCarrito(pedido, preparado.CodigoPostal);
            _ = GestorPortes.GestionarLineasPortes(pedido.Lineas, portes, pedido.iva, pedido.ParametrosIva);
            // La línea de portes recién creada también necesita su porcentaje para sumar al total
            RellenarPorcentajesIva(pedido);
        }

        /// <summary>El porcentaje de IVA y de recargo de cada línea, a partir de su código de IVA. Internal para tests.</summary>
        internal static void RellenarPorcentajesIva(PedidoVentaDTO pedido)
        {
            if (pedido.ParametrosIva == null || !pedido.ParametrosIva.Any())
            {
                return;
            }
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas)
            {
                ParametrosIvaBase parametro = pedido.ParametrosIva
                    .FirstOrDefault(p => p.CodigoIvaProducto == linea.iva?.Trim());
                if (parametro != null)
                {
                    linea.PorcentajeIva = parametro.PorcentajeIvaProducto;
                    linea.PorcentajeRecargoEquivalencia = parametro.PorcentajeRecargoEquivalencia;
                }
            }
        }

        /// <summary>
        /// NestoAPI#452: avisa si lo que se cobró no es lo que ha acabado costando el pedido. No
        /// toca nada (el cobro ya está hecho y el pedido creado): deja el rastro para arreglarlo a
        /// mano el mismo día. Internal para tests.
        /// </summary>
        internal static string DiferenciaCobroPedido(decimal importeCobrado, decimal totalPedido, int numeroPedido, string numeroOrden)
        {
            if (importeCobrado == totalPedido)
            {
                return null;
            }
            string signo = importeCobrado < totalPedido ? "de MENOS" : "de MÁS";
            return $"[Pedido app] Se ha cobrado {signo}: orden {numeroOrden} por {importeCobrado:N2} EUR " +
                   $"y el pedido {numeroPedido} ha quedado en {totalPedido:N2} EUR " +
                   $"(diferencia {totalPedido - importeCobrado:N2} EUR). Revisar el cobro y el prepago.";
        }

        private static void AvisarSiElCobroNoCuadra(decimal importeCobrado, decimal totalPedido, int numeroPedido, string numeroOrden)
        {
            string aviso = DiferenciaCobroPedido(importeCobrado, totalPedido, numeroPedido, numeroOrden);
            if (aviso != null)
            {
                ElmahHelper.Log(new Exception(aviso));
            }
        }

        /// <summary>
        /// La ficha del contacto principal, que es sobre el que se crea el pedido: el JWT
        /// identifica al cliente, no a uno de sus contactos.
        /// </summary>
        private async Task<ClienteDTO> LeerFichaCliente(string empresa, string cliente)
        {
            Cliente fichaCliente = await db.Clientes
                .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal)
                .ConfigureAwait(false);
            if (fichaCliente == null)
            {
                return null;
            }
            return new ClienteDTO
            {
                empresa = fichaCliente.Empresa.Trim(),
                cliente = fichaCliente.Nº_Cliente.Trim(),
                contacto = fichaCliente.Contacto.Trim(),
                estado = fichaCliente.Estado,
                iva = fichaCliente.IVA,
                ccc = fichaCliente.CCC,
                codigoPostal = fichaCliente.CodPostal,
                periodoFacturacion = fichaCliente.PeriodoFacturación,
                ruta = fichaCliente.Ruta,
                servirJunto = fichaCliente.ServirJunto,
                mantenerJunto = fichaCliente.MantenerJunto,
                noComisiona = fichaCliente.NoComisiona,
                vendedor = fichaCliente.Vendedor,
                comentarioPicking = fichaCliente.ComentarioPicking
            };
        }

        private async Task<CondicionesPagoResponse> LeerCondicionesPago(string empresa, string cliente)
        {
            // Sin using, igual que arriba: comparte el DbContext de este controller.
            PlazosPagoController controllerPlazos = new PlazosPagoController(db);
            IHttpActionResult resultado = await controllerPlazos
                .GetCondicionesPago(empresa, cliente, Constantes.FormasVenta.APP)
                .ConfigureAwait(false);
            return resultado is OkNegotiatedContentResult<CondicionesPagoResponse> ok ? ok.Content : null;
        }

        private async Task<Dictionary<string, ProductoPlantillaDTO>> CalcularPrecios(
            string empresa, ClienteDTO cliente, IEnumerable<LineaPedidoClienteRequest> lineas)
        {
            Dictionary<string, ProductoPlantillaDTO> precios = new Dictionary<string, ProductoPlantillaDTO>();
            // Sin using, igual que arriba: comparte el DbContext de este controller.
            ProductosController controllerProductos = new ProductosController(db);
            foreach (LineaPedidoClienteRequest linea in lineas)
            {
                string producto = linea.Producto.Trim();
                IHttpActionResult resultado = await controllerProductos
                    .GetProducto(empresa, producto, cliente.cliente, cliente.contacto, linea.Cantidad)
                    .ConfigureAwait(false);
                if (resultado is OkNegotiatedContentResult<ProductoPlantillaDTO> ok)
                {
                    precios[producto] = ok.Content;
                }
            }
            return precios;
        }

        private static PedidoClienteResponse ConstruirRespuesta(PedidoVentaDTO pedido, string formaPago, string plazosPago)
        {
            PedidoClienteResponse respuesta = new PedidoClienteResponse
            {
                Empresa = pedido.empresa,
                Numero = pedido.numero,
                Cliente = pedido.cliente?.Trim(),
                Contacto = pedido.contacto?.Trim(),
                FormaPago = formaPago,
                PlazosPago = plazosPago,
                BaseImponible = pedido.BaseImponible,
                Total = pedido.Total,
                // Los portes los ha calculado el servidor y son una línea más de cuenta contable
                Portes = pedido.Lineas
                    .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
                    .Sum(l => l.BaseImponible),
                RequierePago = PoliticaPagoCanal.SeCobraEnElMomento(formaPago, plazosPago)
            };

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO))
            {
                respuesta.Lineas.Add(new LineaPedidoClienteResponse
                {
                    Producto = linea.Producto?.Trim(),
                    Texto = linea.texto,
                    Cantidad = (short)linea.Cantidad,
                    PrecioUnitario = linea.PrecioUnitario,
                    Descuento = linea.SumaDescuentos,
                    BaseImponible = linea.BaseImponible,
                    Total = linea.Total
                });
            }

            if (respuesta.RequierePago)
            {
                respuesta.Avisos.Add("El pedido no se prepara hasta que se recibe el pago.");
            }

            return respuesta;
        }

        /// <summary>
        /// TNV#66: los pedidos recientes del cliente que ha iniciado sesión, para que después de
        /// comprar tenga dónde comprobar que su pedido existe y por dónde va.
        ///
        /// <para>Hasta ahora, al confirmar se le vaciaba el carrito y ya: exactamente lo mismo que
        /// vería si el pedido hubiera fallado. La única señal que le dábamos era ambigua justo en
        /// el momento de más incertidumbre.</para>
        ///
        /// <para>Se devuelven también los ya servidos de los últimos días, no solo los que están
        /// en curso: el paquete de un pedido facturado ayer todavía está de camino, y es el que el
        /// cliente quiere seguir. El envío viaja en el mismo DTO que usa
        /// <c>EnviosAgencias/UltimoEnvioCliente</c> (TNV#5), con su URL de seguimiento ya montada.</para>
        /// </summary>
        /// <param name="dias">Cuántos días atrás se miran (1-365). Por defecto, dos meses.</param>
        // GET: api/Pedidos/Cliente
        [HttpGet]
        [Route("Cliente")]
        [ResponseType(typeof(List<PedidoClienteResumenDTO>))]
        public async Task<IHttpActionResult> GetPedidosCliente(int dias = DIAS_DE_PEDIDOS_POR_DEFECTO)
        {
            // El cliente sale SIEMPRE del JWT, nunca de la petición: es lo que impide ver los
            // pedidos de otro. Misma regla que el POST.
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return Unauthorized();
            }
            if (!ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }

            if (dias < 1 || dias > MAXIMO_DIAS_DE_PEDIDOS)
            {
                return BadRequest($"El número de días tiene que estar entre 1 y {MAXIMO_DIAS_DE_PEDIDOS}");
            }

            // La lectura y el resumen viven en ServicioPedidosCliente porque los comparte el job
            // que avisa por push de los cambios de estado (TNV#66): si cada uno calculara el estado
            // por su cuenta, la notificación diría una cosa y la pantalla otra.
            List<PedidoClienteResumenDTO> resumenes = await new ServicioPedidosCliente(db)
                .LeerPedidosRecientes(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, dias)
                .ConfigureAwait(false);

            if (PoliticaPreciosOcultos.OcultaImportes(identity))
            {
                // NestoAPI#446: quien hace pedidos sin ver los precios tampoco ve lo que costaron.
                foreach (PedidoClienteResumenDTO resumen in resumenes)
                {
                    resumen.Total = 0m;
                    resumen.ImportePendiente = 0m;
                }
            }

            return Ok(resumenes);
        }

        private static string MotivoParaElCliente(PedidoValidacionException ex)
        {
            List<string> motivos = ex.RespuestaValidacion?.Motivos;
            string detalle = motivos != null && motivos.Any()
                ? string.Join(". ", motivos)
                : ex.Message;
            return "El pedido no se ha podido crear y necesita que lo revisemos: " + detalle;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
