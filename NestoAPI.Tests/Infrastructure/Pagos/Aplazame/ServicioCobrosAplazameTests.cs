using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: el cobro con Aplázame del carrito. Lo que importa por encima de todo: que nada pase a «Autorizado»
    /// (y por tanto que ningún pedido nazca pagado) sin que Aplázame lo haya aprobado por el importe exacto, que no se
    /// capture dos veces, y que lo que no llega a ser pedido se cancele.
    /// </summary>
    [TestClass]
    public class ServicioCobrosAplazameTests
    {
        private const string CLAVE = "clave-privada-de-prueba";
        private const string CLIENTE = "15191";

        private RepositorioCobrosAplazameEnMemoria repositorio;
        private IClienteApiAplazame api;
        private IServicioCorreoElectronico correo;
        private IServicioNotificacionesPush push;
        private List<int> avisosProgramados;
        private List<Exception> errores;
        private ServicioCobrosAplazame servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCobrosAplazameEnMemoria();
            api = A.Fake<IClienteApiAplazame>();
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            push = A.Fake<IServicioNotificacionesPush>();
            avisosProgramados = new List<int>();
            errores = new List<Exception>();
            servicio = new ServicioCobrosAplazame(repositorio, api, () => new ConfiguracionAplazame
            {
                Modo = ModoAplazame.Activo,
                ClavePrivada = CLAVE,
                ClavePublica = "clave-publica-de-prueba"
            }, correo)
            {
                RegistrarError = e => errores.Add(e),
                ProgramarAvisoAprobacion = id => avisosProgramados.Add(id),
                CrearNotificaciones = () => push,
                Ahora = () => new DateTime(2026, 10, 16, 12, 0, 0)
            };
        }

        private PagoTPV Cobro(int id = 7, string estado = Constantes.EstadosPagoTPV.PENDIENTE, decimal importe = 203.86M,
            string documento = null, bool sandbox = false)
        {
            return repositorio.Anadir(new PagoTPV
            {
                Id = id,
                NumeroOrden = "APP-" + id,
                Tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME,
                Empresa = "1",
                Cliente = CLIENTE,
                Importe = importe,
                Estado = estado,
                Documento = documento,
                Sandbox = sandbox,
                FechaCreacion = new DateTime(2026, 10, 16, 11, 50, 0)
            });
        }

        private static string Notificacion(string mid, string status, string motivo, long importe, bool sandbox = false)
        {
            return "{\"id\":\"8606a585a5a56e51856e7f6d84a131b8\",\"status\":\"" + status + "\"," +
                (motivo == null ? "" : "\"status_reason\":\"" + motivo + "\",") +
                "\"sandbox\":" + (sandbox ? "true" : "false") + ",\"mid\":\"" + mid + "\",\"total_amount\":" + importe + "}";
        }

        #region Notificación

        [TestMethod]
        public async Task Notificacion_BearerIncorrecto_403YNoCambiaNada()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer otra-clave",
                Notificacion("APP-7", "pending", "confirmation_required", 20386));

            Assert.AreEqual(403, respuesta.CodigoHttp);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repositorio.Pagos[7].Estado);
            Assert.IsFalse(avisosProgramados.Any());
        }

        [TestMethod]
        public async Task Notificacion_SinCabecera_403()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion(null,
                Notificacion("APP-7", "pending", "confirmation_required", 20386));

            Assert.AreEqual(403, respuesta.CodigoHttp);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public void AutorizacionValida_SoloLaClaveExacta()
        {
            Assert.IsTrue(ServicioCobrosAplazame.AutorizacionValida("Bearer " + CLAVE, CLAVE));
            Assert.IsFalse(ServicioCobrosAplazame.AutorizacionValida("Bearer " + CLAVE + "x", CLAVE));
            Assert.IsFalse(ServicioCobrosAplazame.AutorizacionValida(CLAVE, CLAVE));
            Assert.IsFalse(ServicioCobrosAplazame.AutorizacionValida("Bearer ", CLAVE));
            Assert.IsFalse(ServicioCobrosAplazame.AutorizacionValida("Bearer " + CLAVE, null));
        }

        [TestMethod]
        public async Task Notificacion_MidDesconocido_404()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-99", "pending", "confirmation_required", 20386));

            Assert.AreEqual(404, respuesta.CodigoHttp);
        }

        [TestMethod]
        public async Task Notificacion_ImporteDistinto_KoYNoSeConfirma()
        {
            // Como confirm.php del módulo de PrestaShop: un céntimo de diferencia es «fraude», nunca un pedido
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "pending", "confirmation_required", 20385));

            Assert.AreEqual(200, respuesta.CodigoHttp);
            Assert.AreEqual("ko", respuesta.Status);
            Assert.AreNotEqual(Constantes.EstadosPagoTPV.AUTORIZADO, repositorio.Pagos[7].Estado);
            Assert.IsFalse(avisosProgramados.Any());
            Assert.IsTrue(errores.Any());
        }

        [TestMethod]
        public async Task Notificacion_Confirmacion_PasaAConfirmadoYContestaOk()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "pending", "confirmation_required", 20386));

            Assert.AreEqual(200, respuesta.CodigoHttp);
            Assert.AreEqual("ok", respuesta.Status);
            Assert.AreEqual(Constantes.EstadosPagoTPV.AUTORIZADO, repositorio.Pagos[7].Estado);
            Assert.AreEqual(new DateTime(2026, 10, 16, 12, 0, 0), repositorio.Pagos[7].FechaConfirmacion);
            CollectionAssert.AreEqual(new[] { 7 }, avisosProgramados);
        }

        [TestMethod]
        public async Task Notificacion_Repetida_EsIdempotente()
        {
            Cobro();
            string cuerpo = Notificacion("APP-7", "pending", "confirmation_required", 20386);

            RespuestaNotificacionAplazame primera = await servicio.ProcesarNotificacion("Bearer " + CLAVE, cuerpo);
            RespuestaNotificacionAplazame segunda = await servicio.ProcesarNotificacion("Bearer " + CLAVE, cuerpo);
            RespuestaNotificacionAplazame final = await servicio.ProcesarNotificacion("Bearer " + CLAVE, Notificacion("APP-7", "ok", null, 20386));
            RespuestaNotificacionAplazame finalOtraVez = await servicio.ProcesarNotificacion("Bearer " + CLAVE, Notificacion("APP-7", "ok", null, 20386));

            Assert.AreEqual("ok", primera.Status);
            Assert.AreEqual("ok", segunda.Status);
            Assert.AreEqual("ok", final.Status);
            Assert.AreEqual("ok", finalOtraVez.Status);
            Assert.AreEqual(Constantes.EstadosPagoTPV.AUTORIZADO, repositorio.Pagos[7].Estado);
            Assert.AreEqual(1, avisosProgramados.Count, "la push se programa una sola vez");
        }

        [TestMethod]
        public async Task Notificacion_Ko_PasaARechazado()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "ko", "ko_generic", 20386));

            Assert.AreEqual("ok", respuesta.Status);
            Assert.AreEqual(Constantes.EstadosPagoTPV.DENEGADO, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Notificacion_ValidacionDeIdentidadPendiente_OkSinCambiarNada()
        {
            Cobro();

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "pending", "challenge_required", 20386));

            Assert.AreEqual("ok", respuesta.Status);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Notificacion_ConfirmacionDeUnCobroCancelado_Ko()
        {
            // Lo cancelamos (caducó o el pedido no salió): si después llega la aprobación, no se confirma
            Cobro(estado: Constantes.EstadosPagoTPV.CANCELADO);

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "pending", "confirmation_required", 20386));

            Assert.AreEqual("ko", respuesta.Status);
            Assert.AreEqual(Constantes.EstadosPagoTPV.CANCELADO, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Notificacion_SandboxDistintoDelCobro_NoSeConfirma()
        {
            Cobro(sandbox: false);

            RespuestaNotificacionAplazame respuesta = await servicio.ProcesarNotificacion("Bearer " + CLAVE,
                Notificacion("APP-7", "pending", "confirmation_required", 20386, sandbox: true));

            Assert.AreEqual(400, respuesta.CodigoHttp);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Notificacion_KoDespuesDeCrearElPedido_AvisaAInformatica()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: "925700");

            _ = await servicio.ProcesarNotificacion("Bearer " + CLAVE, Notificacion("APP-7", "ko", "merchant_failed_to_confirm", 20386));

            Assert.AreEqual(Constantes.EstadosPagoTPV.DENEGADO, repositorio.Pagos[7].Estado);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.Subject.Contains("925700")))).MustHaveHappened();
        }

        #endregion

        #region Estado para la app

        [TestMethod]
        public async Task Estado_DeOtroCliente_Null()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO);

            Assert.IsNull(await servicio.Estado(7, "1", "99999"));
            Assert.AreEqual("Confirmado", (await servicio.Estado(7, "1", CLIENTE)).Estado);
        }

        [TestMethod]
        public void EstadoParaLaApp_TraduceLosEstadosDelContrato()
        {
            Assert.AreEqual("Pendiente", ServicioCobrosAplazame.EstadoParaLaApp(Constantes.EstadosPagoTPV.PENDIENTE));
            Assert.AreEqual("Confirmado", ServicioCobrosAplazame.EstadoParaLaApp(Constantes.EstadosPagoTPV.AUTORIZADO));
            Assert.AreEqual("Rechazado", ServicioCobrosAplazame.EstadoParaLaApp(Constantes.EstadosPagoTPV.DENEGADO));
            Assert.AreEqual("Cancelado", ServicioCobrosAplazame.EstadoParaLaApp(Constantes.EstadosPagoTPV.CANCELADO));
        }

        #endregion

        #region Abrir el cobro

        [TestMethod]
        public async Task IniciarCobro_CreaElCobroConSuMidYElCheckout()
        {
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._)).Returns("chk-123");

            CobroAplazameIniciadoDTO cobro = await servicio.IniciarCobro(Solicitud(121.00M));

            PagoTPV pago = repositorio.Pagos[cobro.IdPago];
            Assert.AreEqual("APP-" + cobro.IdPago, pago.NumeroOrden);
            Assert.AreEqual(Constantes.TiposPagoTPV.CARRITO_APLAZAME, pago.Tipo);
            Assert.AreEqual(Constantes.EstadosPagoTPV.PENDIENTE, pago.Estado);
            Assert.AreEqual("chk-123", pago.ReferenciaExterna);
            Assert.AreEqual(121.00M, pago.Importe);
            Assert.AreEqual("chk-123", cobro.CheckoutId);
            Assert.AreEqual(121.00M, cobro.Importe);
            Assert.AreEqual("https://api.nuevavision.es/aplazame/checkout.html?id=chk-123&pk=clave-publica-de-prueba&sandbox=false", cobro.UrlCheckout);
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>.That.Matches(c =>
                c.Order.Id == "APP-" + cobro.IdPago && c.Order.TotalAmount == 12100
                && c.Merchant.NotificationUrl == "https://api.nuevavision.es/api/Aplazame/Notificacion"
                && c.Merchant.SuccessUrl == "nestotiendas://aplazame/ok"
                && c.Merchant.DismissUrl == "nestotiendas://aplazame/dismiss"
                && c.Customer.Email == "cliente@ejemplo.com"), false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task IniciarCobro_SiAplazameNoCreaElCheckout_ElCobroQuedaCancelado()
        {
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._)).Throws(new InvalidOperationException("caído"));

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.IniciarCobro(Solicitud(121.00M)));

            Assert.AreEqual(Constantes.EstadosPagoTPV.CANCELADO, repositorio.Pagos.Values.Single().Estado);
        }

        private static SolicitudCobroAplazame Solicitud(decimal importe)
        {
            return new SolicitudCobroAplazame
            {
                Empresa = "1",
                Cliente = CLIENTE,
                Contacto = "0",
                Correo = "cliente@ejemplo.com",
                Usuario = "cliente@ejemplo.com",
                Importe = importe,
                Lineas = new List<LineaPedidoVentaDTO>
                {
                    new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "17404", texto = "CHAMPU", Cantidad = 1, PrecioUnitario = 100M, PorcentajeIva = 0.21M }
                },
                Facturacion = new DireccionCheckout { Nombre = "PELUQUERIA PRUEBA", Direccion = "C/ Mayor 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid" },
                Envio = new DireccionCheckout { Nombre = "PELUQUERIA PRUEBA", Direccion = "C/ Mayor 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid" }
            };
        }

        #endregion

        #region Cancelar

        [TestMethod]
        public async Task Cancelar_PedidoQueNoSeHaCreado_CancelaEnAplazame()
        {
            // El pedido falló con el cobro ya reservado: se cancela en Aplázame y el cobro queda Cancelado
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: "RESERVADO", sandbox: true);

            bool cancelado = await servicio.Cancelar(7, "el pedido no se ha podido crear");

            Assert.IsTrue(cancelado);
            A.CallTo(() => api.Cancelar("APP-7", true)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(Constantes.EstadosPagoTPV.CANCELADO, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Cancelar_SiAplazameFalla_NoLoDaPorCancelado()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO);
            A.CallTo(() => api.Cancelar(A<string>._, A<bool>._)).Throws(new InvalidOperationException("caído"));

            bool cancelado = await servicio.Cancelar(7, "prueba");

            Assert.IsFalse(cancelado);
            Assert.AreEqual(Constantes.EstadosPagoTPV.AUTORIZADO, repositorio.Pagos[7].Estado);
            Assert.IsTrue(errores.Any(e => e.Message.Contains("panel de Aplázame")));
        }

        [TestMethod]
        public async Task Cancelar_CobroQueYaPagaUnPedido_NoSeCancela()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: "925700");

            bool cancelado = await servicio.Cancelar(7, "prueba");

            Assert.IsFalse(cancelado);
            A.CallTo(() => api.Cancelar(A<string>._, A<bool>._)).MustNotHaveHappened();
            Assert.AreEqual(Constantes.EstadosPagoTPV.AUTORIZADO, repositorio.Pagos[7].Estado);
        }

        [TestMethod]
        public async Task Cancelar_CobroSinAprobar_SeCierraSinLlamarAAplazame()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.PENDIENTE);

            bool cancelado = await servicio.Cancelar(7, "prueba");

            Assert.IsTrue(cancelado);
            A.CallTo(() => api.Cancelar(A<string>._, A<bool>._)).MustNotHaveHappened();
            Assert.AreEqual(Constantes.EstadosPagoTPV.CANCELADO, repositorio.Pagos[7].Estado);
        }

        #endregion

        #region Captura

        private PagoTPV CobroConPedido(int pedido, DateTime confirmado, decimal importe = 121.00M)
        {
            PagoTPV pago = Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: pedido.ToString(), importe: importe);
            pago.FechaConfirmacion = confirmado;
            return pago;
        }

        [TestMethod]
        public async Task Captura_SoloCuandoTodoEstaEnAlbaranOFactura()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 15));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.ALBARAN, Total = 100M },
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Total = 21M }
            };

            ResultadoCapturasAplazame resultado = await servicio.CapturarServidos(new DateTime(2026, 10, 16, 12, 0, 0));

            Assert.IsFalse(resultado.Capturados.Any());
            A.CallTo(() => api.Capturar(A<string>._, A<long>._, A<bool>._)).MustNotHaveHappened();
            Assert.IsNull(repositorio.Pagos[7].FechaCaptura);
        }

        [TestMethod]
        public async Task Captura_TodoServido_CapturaLoServido()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 15));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.ALBARAN, Total = 100M },
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.FACTURA, Total = 21M }
            };
            A.CallTo(() => api.ConsultarCapturas("APP-7", false)).Returns(new CapturasAplazame { PendienteDeCapturar = 12100 });
            DateTime ahora = new DateTime(2026, 10, 16, 12, 0, 0);

            ResultadoCapturasAplazame resultado = await servicio.CapturarServidos(ahora);

            CollectionAssert.AreEqual(new[] { 7 }, resultado.Capturados);
            A.CallTo(() => api.Capturar("APP-7", 12100, false)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(ahora, repositorio.Pagos[7].FechaCaptura);
            Assert.AreEqual(121.00M, repositorio.Pagos[7].ImporteCapturado);
        }

        [TestMethod]
        public async Task Captura_YaCapturadoEnAplazame_NoSeCapturaDosVeces()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 15));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.FACTURA, Total = 121M }
            };
            A.CallTo(() => api.ConsultarCapturas("APP-7", false)).Returns(new CapturasAplazame
            {
                PendienteDeCapturar = 0,
                Capturas = new List<CapturaAplazame> { new CapturaAplazame { Id = "c1", Importe = 12100 } }
            });

            ResultadoCapturasAplazame resultado = await servicio.CapturarServidos(new DateTime(2026, 10, 16, 12, 0, 0));

            A.CallTo(() => api.Capturar(A<string>._, A<long>._, A<bool>._)).MustNotHaveHappened();
            CollectionAssert.AreEqual(new[] { 7 }, resultado.YaCapturados);
            Assert.AreEqual(121.00M, repositorio.Pagos[7].ImporteCapturado);

            // Y en la pasada siguiente ya no aparece
            ResultadoCapturasAplazame otra = await servicio.CapturarServidos(new DateTime(2026, 10, 16, 13, 0, 0));
            Assert.IsFalse(otra.Capturados.Any() || otra.YaCapturados.Any());
        }

        [TestMethod]
        public async Task Captura_ServidoPorMenos_CapturaLoServidoYDejaConstancia()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 15));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.ALBARAN, Total = 60.50M }
            };
            A.CallTo(() => api.ConsultarCapturas("APP-7", false)).Returns(new CapturasAplazame { PendienteDeCapturar = 12100 });

            _ = await servicio.CapturarServidos(new DateTime(2026, 10, 16, 12, 0, 0));

            A.CallTo(() => api.Capturar("APP-7", 6050, false)).MustHaveHappenedOnceExactly();
            Assert.IsTrue(errores.Any(e => e.Message.Contains("60,50") || e.Message.Contains("60.50")));
        }

        [TestMethod]
        public async Task Captura_ServidoPorMas_NuncaMasDeLoAutorizado()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 15));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.ALBARAN, Total = 150M }
            };
            A.CallTo(() => api.ConsultarCapturas("APP-7", false)).Returns(new CapturasAplazame { PendienteDeCapturar = 12100 });

            _ = await servicio.CapturarServidos(new DateTime(2026, 10, 16, 12, 0, 0));

            A.CallTo(() => api.Capturar("APP-7", 12100, false)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Captura_A_Los12DiasSinServir_AvisaAInformaticaUnaVez()
        {
            CobroConPedido(925700, new DateTime(2026, 10, 1, 10, 0, 0));
            repositorio.Lineas[925700] = new List<LineaPedidoServida>
            {
                new LineaPedidoServida { Estado = Constantes.EstadosLineaVenta.PENDIENTE, Total = 121M }
            };

            ResultadoCapturasAplazame antes = await servicio.CapturarServidos(new DateTime(2026, 10, 13, 9, 0, 0));
            ResultadoCapturasAplazame alDia12 = await servicio.CapturarServidos(new DateTime(2026, 10, 13, 10, 0, 0));
            ResultadoCapturasAplazame despues = await servicio.CapturarServidos(new DateTime(2026, 10, 13, 11, 0, 0));

            Assert.IsFalse(antes.Avisados.Any());
            CollectionAssert.AreEqual(new[] { 7 }, alDia12.Avisados);
            Assert.IsFalse(despues.Avisados.Any(), "un solo aviso");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m =>
                m.To.Any(t => t.Address == Constantes.Correos.INFORMATICA) && m.Subject.Contains("925700")))).MustHaveHappenedOnceExactly();
        }

        #endregion

        #region Push

        [TestMethod]
        public async Task AvisoAprobacion_SinPedido_MandaLaPushDeCarrito()
        {
            Cobro(estado: Constantes.EstadosPagoTPV.AUTORIZADO);

            await servicio.AvisarAprobacionSiSigueSinPedido(7);

            A.CallTo(() => push.EnviarACliente("1", CLIENTE, A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == "carrito" && n.Datos["tipo"] == "carrito"
                && n.Cuerpo == "Aplázame ha aprobado su pago. Abra el carrito para terminar el pedido"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AvisoAprobacion_ConPedidoOReservado_NoMandaNada()
        {
            Cobro(id: 7, estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: "925700");
            Cobro(id: 8, estado: Constantes.EstadosPagoTPV.AUTORIZADO, documento: "RESERVADO");
            Cobro(id: 9, estado: Constantes.EstadosPagoTPV.CANCELADO);

            await servicio.AvisarAprobacionSiSigueSinPedido(7);
            await servicio.AvisarAprobacionSiSigueSinPedido(8);
            await servicio.AvisarAprobacionSiSigueSinPedido(9);

            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        #endregion
    }
}
