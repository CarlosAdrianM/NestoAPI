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
    /// <para><b>TNV#68: con tarjeta se cobra ANTES de crear el pedido.</b> Nació al revés (crear
    /// y luego cobrar), y el 07/09/26 el cliente 25299 canceló el pago en Redsys y el pedido
    /// 925607 se creó igualmente: un pedido fantasma que hubo que borrar a mano y que él veía en
    /// «Mis pedidos». Ahora la app cobra el carrito (<c>POST Cliente/Carrito/Pago</c>), lo
    /// autentica por EMV 3DS 2 y solo pide crear el pedido si el banco autorizó, mandando el
    /// cobro en <see cref="PedidoClienteRequest.IdPagoCarrito"/>. Si el pedido no se puede crear,
    /// el dinero se devuelve.</para>
    ///
    /// <para>Sin tarjeta (recibo, transferencia) el pedido se crea sin más, que es lo de siempre.
    /// Y el camino antiguo —crear y cobrar después— sigue vivo para las versiones de la app ya
    /// instaladas: al crearse con plazos de pago PRE, el picking retiene el pedido hasta que los
    /// prepagos cubren el total (ver PedidoPicking.RetenidoPorPrepago), así que no sale sin
    /// pagar; lo que deja son pedidos fantasma, que es justo lo que quita el orden nuevo.</para>
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Pedidos")]
    public class PedidosClienteController : ApiController
    {
        /// <summary>TNV#66: cuántos días de pedidos ve el cliente en la app si no pide otra cosa.</summary>
        internal const int DIAS_DE_PEDIDOS_POR_DEFECTO = 60;

        /// <summary>Tope para que nadie se traiga el histórico entero en una llamada.</summary>
        internal const int MAXIMO_DIAS_DE_PEDIDOS = 365;

        /// <summary>
        /// TNV#68: interruptor del orden nuevo (cobrar y luego crear). Nace ENCENDIDO, porque el
        /// orden nuevo es el correcto; existe para poder volver al antiguo desde el Web.config sin
        /// esperar a que Google apruebe una versión de la app, que es lo único que no podríamos
        /// hacer deprisa si algo saliera mal en el cobro.
        /// </summary>
        internal const string CLAVE_COBRAR_CARRITO_ANTES = "Pagos:CobrarCarritoAntesDelPedido";

        /// <summary>Encendido salvo que el Web.config diga expresamente "false".</summary>
        internal static bool CobrarCarritoAntesDelPedido =>
            !string.Equals(System.Configuration.ConfigurationManager.AppSettings[CLAVE_COBRAR_CARRITO_ANTES]?.Trim(),
                "false", StringComparison.OrdinalIgnoreCase);

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
                if (peticion.IdPagoCarrito.HasValue)
                {
                    // No debería llegar aquí (a este usuario no se le arranca el cobro del
                    // carrito), y si llega no se le crea el pedido con una tarjeta que su política
                    // no permite. El cobro, si existiera, lo recoge el job de cobros huérfanos.
                    return BadRequest("Tu pedido se paga con tu forma de pago habitual, no con tarjeta.");
                }
                PoliticaPreciosOcultos.ForzarFormaDePagoHabitual(peticion);
            }

            // TNV#68: si viene el cobro del carrito, se paga con tarjeta guardada y punto. Lo
            // decide el servidor y no la petición, para que un cuerpo mal montado no acabe
            // creando un pedido a crédito con un cobro con tarjeta ya hecho.
            if (peticion.IdPagoCarrito.HasValue)
            {
                peticion.PagarConTarjetaGuardada = true;
            }

            PedidoPreparado preparado = await PrepararPedido(peticion).ConfigureAwait(false);
            if (preparado.Error != null)
            {
                return preparado.Error;
            }

            // NestoAPI#178/#181: el pedido lo hace el CLIENTE con su tarjeta guardada, así que es
            // un CIT sobre credencial en fichero y hay que autenticarlo. Se crea el pedido y el
            // pago se confirma con la tarjeta ya cargada, sin volver a teclearla, por EMV 3DS 2
            // (frictionless cuando el emisor no exige reto). Aquí NO se cobra por MIT: hacerlo
            // sería clasificar mal la operación, saltarse la SCA y renunciar al traslado de
            // responsabilidad. El MIT que el banco activó el 07/09/26 es para la cartera de
            // aplazados y periódicos (#181), que cobrará desde el motor de remesa, no desde aquí.
            // TNV#68: cuando el cobro ya está hecho (IdPagoCarrito), la tarjeta solo sirve para
            // contarle al cliente en qué tarjeta se le ha cobrado. No se le puede rechazar el
            // pedido por ella: el dinero ya está cobrado y rechazarlo dejaría un cobro sin pedido.
            bool yaSeHaCobrado = peticion.IdPagoCarrito.HasValue;
            TarjetaCliente tarjetaParaLaPasarela = null;
            if (peticion.PagarConTarjetaGuardada)
            {
                if (!peticion.TarjetaId.HasValue)
                {
                    if (!yaSeHaCobrado)
                    {
                        return BadRequest("Falta la tarjeta con la que pagar (TarjetaId)");
                    }
                }
                else
                {
                    tarjetaParaLaPasarela = servicioPagos.TarjetaGuardadaDe(
                        preparado.Pedido.empresa, preparado.Pedido.cliente, peticion.TarjetaId.Value);
                    if (tarjetaParaLaPasarela == null && !yaSeHaCobrado)
                    {
                        return BadRequest("No encontramos esa tarjeta guardada. Elige otra forma de pago.");
                    }
                }
            }

            // TNV#68: el cobro del carrito se toma ANTES de crear nada. Aquí se decide si este
            // pedido llega a existir: si el cobro no está autorizado, no es de este cliente, ya se
            // usó o no cuadra con el importe, no se crea el pedido (que es exactamente lo que
            // faltaba el día del 925607).
            ReservaCobroCarrito cobroCarrito = null;
            if (peticion.IdPagoCarrito.HasValue)
            {
                cobroCarrito = await servicioPagos.ReservarCobroCarrito(
                    peticion.IdPagoCarrito.Value, preparado.Pedido.empresa, preparado.Pedido.cliente)
                    .ConfigureAwait(false);
                if (!cobroCarrito.Valido)
                {
                    return BadRequest(cobroCarrito.Motivo);
                }

                // El mismo cálculo con el que se cobró. Si no da lo mismo, algo ha cambiado entre
                // el pago y ahora (un precio, el stock que decide los portes) y no se crea el
                // pedido: se devuelve el dinero y que lo vuelva a intentar viendo el importe nuevo.
                decimal totalAhora = (await CalcularTotalDelCarrito(preparado).ConfigureAwait(false)).Total;
                if (totalAhora != cobroCarrito.Importe)
                {
                    _ = await DevolverCobroDelCarrito(cobroCarrito,
                        $"el pedido vale ahora {totalAhora:N2} EUR y se cobraron {cobroCarrito.Importe:N2} EUR")
                        .ConfigureAwait(false);
                    return BadRequest("El importe del pedido ha cambiado desde que lo pagaste, así que no lo hemos " +
                        "creado y te hemos devuelto el pago. Vuelve a entrar en el carrito para verlo y confirmarlo otra vez.");
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
                return await NoSeHaCreado(cobroCarrito, MotivoParaElCliente(ex)).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex)
            {
                return await NoSeHaCreado(cobroCarrito, ex.Message).ConfigureAwait(false);
            }

            if (!(resultado is CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>))
            {
                // BadRequest, Conflict... lo que haya respondido el endpoint de siempre. Si el
                // dinero ya estaba cobrado vuelve: cobrado y sin pedido no se queda nada.
                if (cobroCarrito != null)
                {
                    _ = await DevolverCobroDelCarrito(cobroCarrito, "el pedido no se ha llegado a crear")
                        .ConfigureAwait(false);
                }
                return resultado;
            }

            PedidoClienteResponse respuesta = ConstruirRespuesta(
                preparado.Pedido, preparado.FormaPago, preparado.PlazosPago, yaCobrado: cobroCarrito != null);

            if (sinPrecios)
            {
                // El pedido se ha creado con sus precios reales; a este usuario no se le cuentan
                PoliticaPreciosOcultos.OcultarImportes(respuesta);
            }

            if (cobroCarrito != null)
            {
                await AplicarCobroDelCarrito(cobroCarrito, respuesta, tarjetaParaLaPasarela).ConfigureAwait(false);
            }
            else if (respuesta.RequierePago)
            {
                await ArrancarPago(respuesta, preparado, tarjetaParaLaPasarela).ConfigureAwait(false);
            }

            return Ok(respuesta);
        }

        /// <summary>
        /// TNV#68: el pedido no se ha creado. Si se había cobrado por adelantado, el dinero vuelve
        /// antes de contestar: un cobro sin pedido no puede esperar a que alguien lo vea.
        /// </summary>
        private async Task<IHttpActionResult> NoSeHaCreado(ReservaCobroCarrito cobroCarrito, string motivo)
        {
            if (cobroCarrito == null)
            {
                return BadRequest(motivo);
            }

            bool devuelto = await DevolverCobroDelCarrito(cobroCarrito, "el pedido no se ha podido crear")
                .ConfigureAwait(false);

            return BadRequest(devuelto
                ? motivo + " No te hemos cobrado nada: el pago se ha devuelto."
                : motivo + " El pago se te devolverá; si en unos días no lo ves, llámanos.");
        }

        /// <summary>
        /// TNV#68: devuelve el cobro del carrito cuando el pedido no llega a existir. Que la
        /// devolución falle no puede tumbar la respuesta al cliente (el pedido no se ha creado
        /// igualmente), pero sí tiene que dejar rastro: eso lo hace ServicioPagos.DevolverCobro.
        /// </summary>
        private async Task<bool> DevolverCobroDelCarrito(ReservaCobroCarrito cobro, string motivo)
        {
            try
            {
                return await servicioPagos.DevolverCobro(cobro.IdPago, motivo).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Pedido app] No se ha podido devolver el cobro {cobro.NumeroOrden} " +
                    $"({cobro.Importe:N2} EUR) despues de que {motivo}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// TNV#68: el pedido ya existe y el dinero ya estaba cobrado. Solo queda enlazarlos, que
        /// es lo que apunta el Prepago y saca al pedido de la retención por prepago del picking.
        ///
        /// <para>Si esto fallara, el pedido se queda creado y retenido: no se sirve sin cobrar
        /// (que es lo que hay que proteger), pero tampoco sale, así que el aviso a ELMAH es de los
        /// que hay que mirar el mismo día.</para>
        /// </summary>
        private async Task AplicarCobroDelCarrito(ReservaCobroCarrito cobro, PedidoClienteResponse respuesta,
            TarjetaCliente tarjeta)
        {
            string diferencia = DiferenciaCobroPedido(cobro.Importe, respuesta.Total, respuesta.Numero, cobro.NumeroOrden);
            if (diferencia != null)
            {
                // No debería pasar (el importe se comprueba antes de crear), pero si pasa hay que
                // enterarse hoy: el pedido habría salido cobrado de menos o de más.
                ElmahHelper.Log(new Exception(diferencia));
            }

            try
            {
                await servicioPagos.AplicarCobroAlPedido(cobro.IdPago, respuesta.Numero).ConfigureAwait(false);
                respuesta.Pagado = true;
                respuesta.TarjetaUltimosDigitos = tarjeta?.UltimosDigitos;
                respuesta.TarjetaDescripcion = tarjeta?.Descripcion;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Pedido app] Cobrados {cobro.Importe:N2} EUR (orden {cobro.NumeroOrden}) y creado el pedido " +
                    $"{respuesta.Numero}, pero NO se ha podido apuntar el prepago: el pedido se queda retenido " +
                    $"hasta que se apunte a mano. {ex.Message}", ex));
                respuesta.Avisos.Add("Hemos cobrado tu pedido, pero el cobro tardará un poco en reflejarse. " +
                    "Si en un rato sigue como pendiente de pago, llámanos y lo miramos.");
            }
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

            // TNV#68: el total va aquí porque es el número que el carrito le enseña al cliente, y
            // tiene que ser el mismo que se le va a cobrar. La app lo calculaba por su cuenta
            // sumando un 21 % a los portes, así que a un cliente con recargo de equivalencia le
            // enseñaba uno y se le habría cobrado otro.
            TotalCarrito carrito = await CalcularTotalDelCarrito(preparado).ConfigureAwait(false);
            ResultadoPortes portes = carrito.Portes;

            return Ok(new PortesClienteResponse
            {
                BaseImponibleProductos = portes.ImporteActualPedido,
                Portes = portes.ImportePortes,
                PortesGratis = portes.PortesGratis,
                ImporteMinimoSinPortes = portes.ImporteMinimoPedidoSinPortes,
                FaltaParaPortesGratis = portes.ImporteFaltaParaPortesGratis,
                TotalConIva = carrito.Total,
                ComisionReembolso = portes.ComisionReembolso,
                // TNV#70: con la tienda elegida, el mismo cálculo dice ya qué no está allí
                ProductosSinStockEnTienda = LoQueNoHayEnLaTienda(preparado.Pedido, preparado.Tienda)
            });
        }

        /// <summary>
        /// TNV#69: las direcciones a las que este cliente puede pedir que le mandemos el pedido.
        ///
        /// <para>Hasta ahora cerraba el pedido <b>sin ver a dónde se lo íbamos a mandar</b>: se
        /// creaba siempre contra el contacto principal de su ficha y no tenía forma ni de saberlo
        /// ni de cambiarlo.</para>
        ///
        /// <para>Va aquí, acotado al canal de la app, y no se reutiliza <c>GET api/Clientes</c>:
        /// aquel devuelve la ficha entera de UN contacto —CCC, vendedor, comentarios internos—, y
        /// para elegir a dónde va el paquete solo hace falta la dirección.</para>
        /// </summary>
        // GET: api/Pedidos/Cliente/Direcciones
        [HttpGet]
        [Route("Cliente/Direcciones")]
        [ResponseType(typeof(List<DireccionEntregaClienteDTO>))]
        public async Task<IHttpActionResult> GetDireccionesCliente()
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente)
                || !ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }

            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;

            // Solo las fichas vivas: una ficha anulada es una dirección a la que ya no se manda,
            // y ofrecerla es ofrecer un pedido que luego se rechaza.
            List<DireccionEntregaClienteDTO> direcciones = await db.Clientes
                .Where(c => c.Empresa == empresa
                         && c.Nº_Cliente == cliente
                         && c.Estado >= Constantes.Clientes.Estados.VISITA_PRESENCIAL)
                .OrderByDescending(c => c.ClientePrincipal)
                .ThenBy(c => c.Nombre)
                .Select(c => new DireccionEntregaClienteDTO
                {
                    Contacto = c.Contacto,
                    Nombre = c.Nombre,
                    Direccion = c.Dirección,
                    CodigoPostal = c.CodPostal,
                    Poblacion = c.Población,
                    Provincia = c.Provincia,
                    Telefono = c.Teléfono,
                    EsPrincipal = c.ClientePrincipal
                })
                .ToListAsync()
                .ConfigureAwait(false);

            // Los char de la base vienen rellenos de espacios y la app los pinta tal cual
            foreach (DireccionEntregaClienteDTO direccion in direcciones)
            {
                direccion.Contacto = direccion.Contacto?.Trim();
                direccion.Nombre = direccion.Nombre?.Trim();
                direccion.Direccion = direccion.Direccion?.Trim();
                direccion.CodigoPostal = direccion.CodigoPostal?.Trim();
                direccion.Poblacion = direccion.Poblacion?.Trim();
                direccion.Provincia = direccion.Provincia?.Trim();
                direccion.Telefono = direccion.Telefono?.Trim();
            }

            return Ok(direcciones);
        }

        /// <summary>
        /// TNV#70: las tiendas donde el cliente puede pasar a recoger el pedido en vez de que se
        /// lo mandemos. Recogiendo no hay portes.
        ///
        /// <para>Se devuelven siempre las tres: qué hay y qué no en cada una depende del carrito,
        /// y eso lo dice <c>POST Cliente/Portes</c> con la tienda elegida
        /// (<c>ProductosSinStockEnTienda</c>), que es donde el cliente puede verlo con su pedido
        /// delante.</para>
        /// </summary>
        // GET: api/Pedidos/Cliente/Tiendas
        [HttpGet]
        [Route("Cliente/Tiendas")]
        [ResponseType(typeof(List<TiendaRecogidaDTO>))]
        public IHttpActionResult GetTiendasRecogida()
        {
            ClaimsIdentity identity = User?.Identity as ClaimsIdentity;
            string cliente = identity?.FindFirst("cliente")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(cliente)
                || !ValidadorAccesoCliente.ValidarAcceso(identity, cliente).Autorizado)
            {
                return Unauthorized();
            }

            return Ok(TiendasRecogida.Todas.Select(t => new TiendaRecogidaDTO
            {
                Almacen = t.Almacen,
                Nombre = t.Nombre,
                Direccion = t.Direccion,
                CodigoPostal = t.CodigoPostal,
                Poblacion = t.Poblacion,
                Telefono = t.Telefono,
                UrlGoogle = t.UrlGoogle
            }).ToList());
        }

        /// <summary>
        /// TNV#70: lo que el cliente ha pedido y no está en la tienda donde quiere recogerlo.
        ///
        /// <para>Se decidió avisar y que decida él, no esconderle la tienda: puede que le compense
        /// esperar. Pero tiene que verlo antes de confirmar, porque si no se planta allí a por un
        /// pedido que todavía no está.</para>
        ///
        /// <para>Disponible es lo mismo que mira el picking: el stock de ese almacén menos lo que
        /// ya está comprometido en otros pedidos.</para>
        /// </summary>
        private static List<ProductoSinStockEnTiendaDTO> LoQueNoHayEnLaTienda(
            PedidoVentaDTO pedido, TiendasRecogida.Tienda tienda)
        {
            if (tienda == null)
            {
                return new List<ProductoSinStockEnTiendaDTO>();
            }

            IGestorStocks gestorStocks = new GestorStocks();
            List<ProductoSinStockEnTiendaDTO> faltan = new List<ProductoSinStockEnTiendaDTO>();

            foreach (LineaPedidoVentaDTO linea in pedido.Lineas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO))
            {
                string producto = linea.Producto?.Trim();
                if (string.IsNullOrWhiteSpace(producto))
                {
                    continue;
                }

                int disponible = gestorStocks.Stock(producto, tienda.Almacen)
                    - gestorStocks.UnidadesPendientesEntregarAlmacen(producto, tienda.Almacen);

                if (disponible < linea.Cantidad)
                {
                    faltan.Add(new ProductoSinStockEnTiendaDTO
                    {
                        Producto = producto,
                        Texto = linea.texto,
                        Cantidad = (short)linea.Cantidad,
                        Disponible = disponible < 0 ? 0 : disponible
                    });
                }
            }

            return faltan;
        }

        /// <summary>
        /// TNV#68: cobra el carrito ANTES de que exista el pedido. Es el primer paso del orden
        /// nuevo; el segundo es <c>POST Cliente</c> con el <c>IdPagoCarrito</c> que se devuelve
        /// aquí.
        ///
        /// <para>El importe es el del pedido que se crearía —los mismos precios, los mismos portes
        /// y el mismo IVA, calculados con el mismo código—, no el que diga la app: si lo dijera
        /// ella, el cliente podría pagar 1 € por un carrito de 100. Y es ese importe exacto el que
        /// se le exige al pedido cuando se crea.</para>
        ///
        /// <para>El cobro nace sin pedido, y así se queda si el cliente cancela: no hay nada que
        /// borrar a mano. Un cobro autorizado que no llegue a ser pedido lo caza el job de cobros
        /// huérfanos.</para>
        /// </summary>
        // POST: api/Pedidos/Cliente/Carrito/Pago
        [HttpPost]
        [Route("Cliente/Carrito/Pago")]
        [ResponseType(typeof(PagoCarritoResponse))]
        public async Task<IHttpActionResult> PostPagoCarrito(PedidoClienteRequest peticion)
        {
            if (!CobrarCarritoAntesDelPedido)
            {
                // Interruptor apagado: la app se entera y se va por el camino de siempre (crear y
                // luego cobrar), sin dejar de vender mientras se arregla lo que sea.
                return BadRequest("Ahora mismo el pago se hace al confirmar el pedido.");
            }

            if (PoliticaPreciosOcultos.OcultaImportes(User?.Identity))
            {
                // NestoAPI#446: quien no ve los precios no paga con tarjeta (la pasarela enseñaría
                // el importe): su pedido va con la forma de pago habitual de su ficha.
                return BadRequest("Tu pedido se paga con tu forma de pago habitual, no con tarjeta.");
            }

            if (peticion?.TarjetaId == null)
            {
                return BadRequest("Falta la tarjeta con la que pagar (TarjetaId)");
            }
            peticion.PagarConTarjetaGuardada = true;

            PedidoPreparado preparado = await PrepararPedido(peticion).ConfigureAwait(false);
            if (preparado.Error != null)
            {
                return preparado.Error;
            }

            if (!PoliticaPagoCanal.SeCobraEnElMomento(preparado.FormaPago, preparado.PlazosPago))
            {
                // La política del canal no ha dejado el pedido en tarjeta al contado: no hay nada
                // que cobrar por adelantado.
                return BadRequest("Este pedido no se cobra con tarjeta, así que no hay nada que pagar ahora.");
            }

            TarjetaCliente tarjeta = servicioPagos.TarjetaGuardadaDe(
                preparado.Pedido.empresa, preparado.Pedido.cliente, peticion.TarjetaId.Value);
            if (tarjeta == null)
            {
                return BadRequest("No encontramos esa tarjeta guardada. Elige otra forma de pago.");
            }

            decimal total = (await CalcularTotalDelCarrito(preparado).ConfigureAwait(false)).Total;
            if (total <= 0)
            {
                return BadRequest("No hemos podido calcular el importe del pedido. Vuelve a abrir el carrito.");
            }

            RespuestaIniciarPago pago;
            try
            {
                pago = await servicioPagos.IniciarPago(new SolicitudPagoTPV
                {
                    Empresa = preparado.Pedido.empresa,
                    Cliente = preparado.Pedido.cliente,
                    Contacto = preparado.Pedido.contacto,
                    Importe = total,
                    Descripcion = "Pedido en Nueva Visión",
                    Correo = preparado.Correo,
                    // Sin pedido: todavía no existe, y solo existirá si esto se autoriza
                    EsCarritoApp = true,
                    TarjetaGuardada = tarjeta
                }, preparado.Pedido.Usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"[Pedido app] No se ha podido arrancar el cobro del carrito del cliente " +
                    $"{preparado.Pedido.cliente?.Trim()} ({total:N2} EUR): {ex.Message}", ex));
                return BadRequest("No hemos podido abrir el pago. Inténtalo de nuevo en unos minutos.");
            }

            return Ok(new PagoCarritoResponse
            {
                Pago = pago,
                IdPago = pago.IdPago,
                Importe = total,
                BaseImponible = preparado.Pedido.BaseImponible,
                Portes = PortesDelPedido(preparado.Pedido),
                TarjetaUltimosDigitos = tarjeta.UltimosDigitos,
                TarjetaDescripcion = tarjeta.Descripcion
            });
        }

        /// <summary>
        /// TNV#68: lo que va a costar el pedido que se crearía con este carrito, con IVA y portes
        /// incluidos. Es el importe que se cobra y, después, el que se le exige al pedido.
        ///
        /// <para>No es una cuenta aparte: monta las líneas de portes con el MISMO
        /// <c>GestorPortes.GestionarLineasPortes</c> que usa PostPedidoVenta al crear el pedido, y
        /// con el mismo input. Por eso lo cobrado y lo facturado coinciden al céntimo, y por eso
        /// deja el DTO ya con su línea de portes: el pedido que se cree después es exactamente el
        /// que se ha cobrado (volver a gestionarlas es idempotente, y la base de portes no cuenta
        /// las líneas de cuenta contable).</para>
        /// </summary>
        private async Task<TotalCarrito> CalcularTotalDelCarrito(PedidoPreparado preparado)
        {
            PedidoVentaDTO pedido = preparado.Pedido;

            if (pedido.ParametrosIva == null || !pedido.ParametrosIva.Any())
            {
                pedido.ParametrosIva = await LeerParametrosIva(pedido.empresa, pedido.iva).ConfigureAwait(false);
            }

            ResultadoPortes portes = CalcularPortesDelCarrito(pedido, preparado.CodigoPostal);
            _ = GestorPortes.GestionarLineasPortes(pedido.Lineas, portes, pedido.iva, pedido.ParametrosIva);

            // NestoAPI#452: sin los porcentajes, el "total" del DTO es la base pelada y se cobraría
            // el pedido sin IVA.
            RellenarPorcentajesIva(pedido);

            return new TotalCarrito { Total = pedido.Total, Portes = portes };
        }

        /// <summary>Lo que cuesta el carrito y por qué: el total que se cobra y los portes que
        /// lleva dentro. Van juntos porque salen del mismo cálculo.</summary>
        private class TotalCarrito
        {
            public decimal Total { get; set; }
            public ResultadoPortes Portes { get; set; }
        }

        /// <summary>Los parámetros de IVA del cliente, los mismos que lee PostPedidoVenta cuando el
        /// DTO no los trae.</summary>
        private async Task<List<ParametrosIvaBase>> LeerParametrosIva(string empresa, string iva)
        {
            return await db.ParametrosIVA
                .Where(p => p.Empresa == empresa && p.IVA_Cliente_Prov == iva)
                .Select(p => new ParametrosIvaBase
                {
                    CodigoIvaProducto = p.IVA_Producto.Trim(),
                    PorcentajeIvaProducto = (decimal)p.C__IVA / 100,
                    PorcentajeRecargoEquivalencia = (decimal)p.C__RE / 100
                })
                .ToListAsync()
                .ConfigureAwait(false);
        }

        /// <summary>Los portes del pedido: son líneas de cuenta contable, no de producto.</summary>
        private static decimal PortesDelPedido(PedidoVentaDTO pedido)
        {
            return pedido.Lineas
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE)
                .Sum(l => l.BaseImponible);
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

            /// <summary>TNV#70: la tienda donde lo va a recoger, o null si se lo mandamos.</summary>
            public TiendasRecogida.Tienda Tienda { get; set; }
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

            // 2 bis. TNV#70: ¿se lo mandamos o pasa a recogerlo? Solo valen las tres tiendas; un
            //    almacén cualquiera que llegue en la petición se rechaza, no se ignora en
            //    silencio, porque el cliente ha elegido algo y hay que decirle que no puede.
            TiendasRecogida.Tienda tienda = null;
            if (!string.IsNullOrWhiteSpace(peticion.TiendaRecogida))
            {
                tienda = TiendasRecogida.Buscar(peticion.TiendaRecogida);
                if (tienda == null)
                {
                    return new PedidoPreparado { Error = BadRequest("Esa tienda no existe. Elige una de las nuestras o pide que te lo enviemos.") };
                }
            }

            // 3. Su ficha: de ahí salen iva, ruta, ccc, periodo de facturación, servir junto,
            //    vendedor y el código postal con el que se calculan los portes.
            //    TNV#69: y la dirección, que ahora puede elegir. Recogiendo en tienda no hay
            //    dirección que elegir, así que se usa la principal.
            string contactoPedido = tienda != null ? null : peticion.Contacto?.Trim();
            ClienteDTO fichaCliente = await LeerFichaCliente(empresa, cliente, contactoPedido).ConfigureAwait(false);
            if (fichaCliente == null)
            {
                return new PedidoPreparado
                {
                    Error = BadRequest(string.IsNullOrWhiteSpace(contactoPedido)
                        ? $"No se encuentra la ficha del cliente {cliente}"
                        : "Esa dirección de entrega ya no está disponible. Elige otra.")
                };
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
                    peticion, fichaCliente, precios, formaPago, plazosPago, DateTime.Today, tienda),
                FormaPago = formaPago,
                PlazosPago = plazosPago,
                CodigoPostal = fichaCliente.codigoPostal?.Trim() ?? string.Empty,
                Correo = identity.FindFirst(ClaimTypes.Email)?.Value,
                Tienda = tienda
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

        /// <summary>El porcentaje de IVA y de recargo de cada línea, a partir de su código de IVA.
        /// El DTO recién construido solo trae el CÓDIGO ("G21"), y sin el porcentaje el total del
        /// pedido es la base imponible (NestoAPI#452). Internal para tests.</summary>
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


        /// <summary>
        /// La ficha sobre la que se crea el pedido. El JWT identifica al CLIENTE, no a uno de sus
        /// contactos, así que por defecto se usa el principal — que es lo que se hacía siempre.
        ///
        /// <para>TNV#69: si el cliente ha elegido una de sus direcciones, se usa ESE contacto, y
        /// con él van su código postal (los portes), su ruta y su dirección de entrega. Que el
        /// contacto sea suyo no hace falta comprobarlo aparte: los contactos cuelgan del número de
        /// cliente y el número de cliente sale del JWT, así que el de otro sencillamente no
        /// aparece. Lo que sí se exige es que esté activo, igual que el principal.</para>
        /// </summary>
        private async Task<ClienteDTO> LeerFichaCliente(string empresa, string cliente, string contacto = null)
        {
            Cliente fichaCliente = string.IsNullOrWhiteSpace(contacto)
                ? await db.Clientes
                    .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal)
                    .ConfigureAwait(false)
                : await db.Clientes
                    .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
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

        /// <param name="yaCobrado">TNV#68: el pedido se ha pagado ANTES de crearse, asi que no
        /// hay nada pendiente ni pasarela que abrir.</param>
        private static PedidoClienteResponse ConstruirRespuesta(PedidoVentaDTO pedido, string formaPago,
            string plazosPago, bool yaCobrado = false)
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
                Portes = PortesDelPedido(pedido),
                RequierePago = !yaCobrado && PoliticaPagoCanal.SeCobraEnElMomento(formaPago, plazosPago)
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
