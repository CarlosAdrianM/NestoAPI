using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Infraestructure.Pagos.PagoAPlazos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos.DatosPagoAPlazos;

namespace NestoAPI.Tests.Infrastructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: Aplázame como implementación de <see cref="IProveedorPagoAPlazos"/>, sin red (el
    /// <see cref="IClienteApiAplazame"/> es falso): cómo traduce el contrato neutro a su checkout, su notificación, su
    /// captura y su cancelación. Y el núcleo entero con Aplázame de proveedor, de punta a punta.
    /// </summary>
    [TestClass]
    public class ProveedorPagoAPlazosAplazameTests
    {
        private const string CLAVE = "clave-privada-de-prueba";

        private IClienteApiAplazame api;
        private ConfiguracionAplazame configuracion;
        private ProveedorPagoAPlazosAplazame proveedor;

        [TestInitialize]
        public void Preparar()
        {
            api = A.Fake<IClienteApiAplazame>();
            configuracion = new ConfiguracionAplazame
            {
                // El interruptor de la APP, apagado: este canal no lo mira
                Modo = ModoAplazame.Apagado,
                ClavePrivada = CLAVE,
                ClavePublica = "clave-publica-de-prueba",
                ImporteMinimo = 100M,
                ImporteMaximo = 2500M
            };
            proveedor = new ProveedorPagoAPlazosAplazame(api, () => configuracion);
        }

        private static PeticionSolicitudProveedor Peticion(string canal = CanalesPagoAPlazos.APP, bool pruebas = false)
        {
            PedidoPagoAPlazos pedido = Pedido();
            return new PeticionSolicitudProveedor
            {
                Referencia = "PAP-7",
                Pruebas = pruebas,
                Canal = canal,
                Empresa = "1",
                Pedido = PEDIDO,
                Cliente = CLIENTE,
                Correo = "cliente@example.com",
                Importe = 302.50M,
                Lineas = pedido.Lineas,
                Facturacion = pedido.Facturacion,
                Envio = pedido.Envio,
                UrlNotificacion = "https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Aplazame"
            };
        }

        private static ReferenciaSolicitudProveedor Referencia(bool pruebas = false)
            => new ReferenciaSolicitudProveedor { Referencia = "PAP-7", ReferenciaExterna = "chk_123", Pruebas = pruebas, Importe = 302.50M };

        private static string Cuerpo(string status, string motivo = null, long? importe = 30250, string mid = "PAP-7", bool? sandbox = null)
        {
            List<string> campos = new List<string>();
            if (mid != null) campos.Add($"\"mid\":\"{mid}\"");
            campos.Add($"\"status\":\"{status}\"");
            if (motivo != null) campos.Add($"\"status_reason\":\"{motivo}\"");
            if (importe.HasValue) campos.Add($"\"total_amount\":{importe}");
            if (sandbox.HasValue) campos.Add($"\"sandbox\":{(sandbox.Value ? "true" : "false")}");
            return "{" + string.Join(",", campos) + "}";
        }

        #region Lo que dice de sí mismo

        [TestMethod]
        public void PagoAPlazosAplazame_CodigoYNombre()
        {
            Assert.AreEqual("Aplazame", proveedor.Codigo);
            Assert.AreEqual("Aplázame", proveedor.Nombre);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_Condiciones_LimitesDeLaCuentaPlazoDe15DiasYLaCuentaDeSiempre()
        {
            CondicionesProveedorPagoAPlazos condiciones = proveedor.Condiciones();

            Assert.IsTrue(condiciones.Operativo, "Con claves es operativo aunque el interruptor de la app esté apagado");
            Assert.AreEqual(100M, condiciones.ImporteMinimo);
            Assert.AreEqual(2500M, condiciones.ImporteMaximo);
            Assert.AreEqual(15, condiciones.DiasPlazoCaptura);
            Assert.AreEqual(Constantes.Prepagos.CUENTA_APLAZAME, condiciones.CuentaPrepago);
            Assert.AreEqual(ProveedorCobroCarrito.Aplazame.PrefijoConceptoPrepago, condiciones.PrefijoConceptoPrepago);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_SinClaves_NoEsOperativo()
        {
            configuracion.ClavePrivada = null;
            Assert.IsFalse(proveedor.Condiciones().Operativo);
        }

        #endregion

        #region Crear la solicitud (el checkout)

        [TestMethod]
        public async Task PagoAPlazosAplazame_CrearSolicitud_ElCheckoutLlevaNuestraReferenciaElImporteExactoYLaNotificacionNeutra()
        {
            CheckoutAplazame enviado = null;
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, false))
                .Invokes((CheckoutAplazame c, bool s) => enviado = c).Returns("chk_123");

            string externa = await proveedor.CrearSolicitud(Peticion());

            Assert.AreEqual("chk_123", externa);
            Assert.AreEqual("PAP-7", enviado.Order.Id);
            Assert.AreEqual(30250, enviado.Order.TotalAmount);
            Assert.AreEqual("https://api.nuevavision.es/api/PagoAPlazos/Notificacion/Aplazame", enviado.Merchant.NotificationUrl);
            Assert.AreEqual("cliente@example.com", enviado.Customer.Email);
            Assert.AreEqual(CLIENTE, enviado.Customer.Id);
            Assert.AreEqual(1, enviado.Order.Articles.Count);
            long suma = ConstructorCheckoutAplazame.Sumar(enviado.Order.Articles, enviado.Shipping.Price, enviado.Shipping.TaxRate);
            Assert.AreEqual(enviado.Order.TotalAmount, suma - enviado.Order.Discount, "Artículos + envío − descuento = total, al céntimo");
        }

        [TestMethod]
        public async Task PagoAPlazosAplazame_CrearSolicitud_PorLaApp_VuelveALaApp_YPorEnlace_ALaPaginaWeb()
        {
            List<CheckoutAplazame> enviados = new List<CheckoutAplazame>();
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._))
                .Invokes((CheckoutAplazame c, bool s) => enviados.Add(c)).Returns("chk_123");

            _ = await proveedor.CrearSolicitud(Peticion(CanalesPagoAPlazos.APP));
            _ = await proveedor.CrearSolicitud(Peticion(CanalesPagoAPlazos.ENLACE));

            Assert.AreEqual("nestotiendas://aplazame/ok", enviados[0].Merchant.SuccessUrl);
            Assert.AreEqual("nestotiendas://aplazame/dismiss", enviados[0].Merchant.DismissUrl);
            Assert.AreEqual("https://api.nuevavision.es/aplazame/checkout.html?volver=web&resultado=ok", enviados[1].Merchant.SuccessUrl);
            Assert.AreEqual("https://api.nuevavision.es/aplazame/checkout.html?volver=web&resultado=ko", enviados[1].Merchant.KoUrl);
        }

        [TestMethod]
        public async Task PagoAPlazosAplazame_CrearSolicitud_EnPruebas_VaAlSandbox()
        {
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._)).Returns("chk_123");

            _ = await proveedor.CrearSolicitud(Peticion(pruebas: true));

            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, true)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PagoAPlazosAplazame_CrearSolicitud_ConPrepagosPrevios_ElTotalEsLoPendienteYElRestoVaDeDescuento()
        {
            CheckoutAplazame enviado = null;
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._))
                .Invokes((CheckoutAplazame c, bool s) => enviado = c).Returns("chk_123");
            PeticionSolicitudProveedor peticion = Peticion();
            peticion.Importe = 202.50M; // el pedido es de 302,50 y ya tiene 100 pagados

            _ = await proveedor.CrearSolicitud(peticion);

            Assert.AreEqual(20250, enviado.Order.TotalAmount);
            Assert.AreEqual(10000, enviado.Order.Discount);
        }

        [TestMethod]
        public async Task PagoAPlazosAplazame_CrearSolicitud_SinCorreoOSinImporte_NoLlamaAAplazame()
        {
            PeticionSolicitudProveedor sinCorreo = Peticion();
            sinCorreo.Correo = " ";
            PeticionSolicitudProveedor sinImporte = Peticion();
            sinImporte.Importe = 0;

            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => proveedor.CrearSolicitud(sinCorreo));
            _ = await Assert.ThrowsExceptionAsync<ArgumentException>(() => proveedor.CrearSolicitud(sinImporte));
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void PagoAPlazosAplazame_UrlPago_LaMismaPaginaDeLaApp_YConVolverWebParaElEnlace()
        {
            string app = proveedor.UrlPago(Referencia(), CanalesPagoAPlazos.APP);
            string enlace = proveedor.UrlPago(Referencia(pruebas: true), CanalesPagoAPlazos.ENLACE);

            Assert.AreEqual("https://api.nuevavision.es/aplazame/checkout.html?id=chk_123&pk=clave-publica-de-prueba&sandbox=false", app);
            Assert.AreEqual("https://api.nuevavision.es/aplazame/checkout.html?id=chk_123&pk=clave-publica-de-prueba&sandbox=true&volver=web", enlace);
            Assert.IsFalse(app.Contains(CLAVE) || enlace.Contains(CLAVE), "La clave privada nunca sale");
        }

        #endregion

        #region Notificación

        [TestMethod]
        public void PagoAPlazosAplazame_Notificacion_SinNuestraClave_NoAutorizada()
        {
            string cuerpo = Cuerpo("pending", "confirmation_required");

            Assert.AreEqual(TipoNotificacionPagoAPlazos.NoAutorizada, proveedor.InterpretarNotificacion("Bearer otra-clave", cuerpo).Tipo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.NoAutorizada, proveedor.InterpretarNotificacion(null, cuerpo).Tipo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.NoAutorizada, proveedor.InterpretarNotificacion(CLAVE, cuerpo).Tipo);

            configuracion.ClavePrivada = null;
            Assert.AreEqual(TipoNotificacionPagoAPlazos.NoAutorizada, proveedor.InterpretarNotificacion("Bearer ", cuerpo).Tipo);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_Notificacion_IlegibleOSinMid()
        {
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Ilegible, proveedor.InterpretarNotificacion("Bearer " + CLAVE, "esto no es json").Tipo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Ilegible, proveedor.InterpretarNotificacion("Bearer " + CLAVE, null).Tipo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Ilegible, proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ok", mid: null)).Tipo);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_Notificacion_PideConfirmacion_ConElImporteEnEurosYLaReferencia()
        {
            NotificacionPagoAPlazos notificacion = proveedor.InterpretarNotificacion("Bearer " + CLAVE,
                Cuerpo("pending", "confirmation_required", 30250, sandbox: true));

            Assert.AreEqual(TipoNotificacionPagoAPlazos.PideConfirmacion, notificacion.Tipo);
            Assert.AreEqual("PAP-7", notificacion.Referencia);
            Assert.AreEqual(302.50M, notificacion.Importe);
            Assert.AreEqual(true, notificacion.Pruebas);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_Notificacion_FinalesEIntermedias()
        {
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Aprobada, proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ok")).Tipo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Rechazada, proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ko", "denied")).Tipo);
            Assert.AreEqual("denied", proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ko", "denied")).Motivo);
            Assert.AreEqual(TipoNotificacionPagoAPlazos.Intermedia,
                proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("pending", "challenge_required")).Tipo);
            Assert.IsNull(proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ok", importe: null)).Importe);
            Assert.IsNull(proveedor.InterpretarNotificacion("Bearer " + CLAVE, Cuerpo("ok")).Pruebas);
        }

        [TestMethod]
        public void PagoAPlazosAplazame_Respuestas_ComoLasEsperaAplazame()
        {
            RespuestaNotificacionProveedor aceptar = proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.Aceptar);
            RespuestaNotificacionProveedor rechazar = proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.Rechazar);

            Assert.AreEqual(200, aceptar.CodigoHttp);
            Assert.AreEqual("{\"status\":\"ok\"}", aceptar.Cuerpo);
            Assert.AreEqual(200, rechazar.CodigoHttp);
            Assert.AreEqual("{\"status\":\"ko\"}", rechazar.Cuerpo);
            Assert.AreEqual(403, proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.NoAutorizada).CodigoHttp);
            Assert.AreEqual(400, proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.Ilegible).CodigoHttp);
            Assert.AreEqual(404, proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.Desconocida).CodigoHttp);
            Assert.AreEqual(500, proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.Reintentar).CodigoHttp);
            Assert.IsNull(proveedor.ResponderNotificacion(DecisionNotificacionPagoAPlazos.NoAutorizada).Cuerpo);
        }

        #endregion

        #region Captura, cancelación y reembolso

        [TestMethod]
        public async Task PagoAPlazosAplazame_ConsultarCaptura_DeCentimosAEuros()
        {
            A.CallTo(() => api.ConsultarCapturas("PAP-7", false)).Returns(new CapturasAplazame
            {
                PendienteDeCapturar = 10250,
                Capturas = new List<CapturaAplazame>
                {
                    new CapturaAplazame { Importe = 15000, Fecha = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero) },
                    new CapturaAplazame { Importe = 5000, Fecha = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero) }
                }
            });

            EstadoCapturaProveedor estado = await proveedor.ConsultarCaptura(Referencia());

            Assert.AreEqual(200M, estado.Capturado);
            Assert.AreEqual(102.50M, estado.PendienteDeCapturar);
            Assert.AreEqual(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero).LocalDateTime, estado.FechaUltimaCaptura);
        }

        [TestMethod]
        public async Task PagoAPlazosAplazame_CapturarCancelarYReembolsar_PorElMidYEnCentimos()
        {
            await proveedor.Capturar(Referencia(), 302.50M);
            await proveedor.Cancelar(Referencia(pruebas: true));
            await proveedor.Reembolsar(Referencia(), 50.05M);

            A.CallTo(() => api.Capturar("PAP-7", 30250, false)).MustHaveHappenedOnceExactly();
            A.CallTo(() => api.Cancelar("PAP-7", true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => api.Reembolsar("PAP-7", 5005, false)).MustHaveHappenedOnceExactly();
        }

        #endregion

        #region El núcleo con Aplázame, de punta a punta

        [TestMethod]
        public async Task PagoAPlazosAplazame_DePuntaAPunta_SolicitarAprobarYCapturar()
        {
            RepositorioPagoAPlazosEnMemoria repositorio = new RepositorioPagoAPlazosEnMemoria();
            PedidosPagoAPlazosEnMemoria pedidos = new PedidosPagoAPlazosEnMemoria();
            pedidos.Pedidos[PEDIDO] = Pedido(tieneApp: false);
            AvisosPagoAPlazosEspia avisos = new AvisosPagoAPlazosEspia();
            DateTime ahora = AHORA;
            A.CallTo(() => api.CrearCheckout(A<CheckoutAplazame>._, false)).Returns("chk_123");
            A.CallTo(() => api.ConsultarCapturas(A<string>._, false)).Returns(new CapturasAplazame { PendienteDeCapturar = 30250 });
            ServicioPagoAPlazos servicio = new ServicioPagoAPlazos(repositorio, pedidos, new RegistroProveedoresPagoAPlazos(proveedor),
                () => new ConfiguracionPagoAPlazos { Modo = ModoPagoAPlazos.Activo, Proveedor = "Aplazame" }, avisos)
            {
                RegistrarError = e => { },
                Ahora = () => ahora
            };

            // 1. Quien pasa el pedido pide el pago a plazos: el cliente no tiene la app, así que sale el enlace
            SolicitudPagoAPlazosDTO solicitud = (await servicio.Solicitar("1", PEDIDO, null, "NUEVAVISION\\Paloma")).Contenido;
            Assert.AreEqual("Enlace", solicitud.Canal);
            Assert.AreEqual("Aplázame", solicitud.NombreProveedor);
            StringAssert.Contains(solicitud.UrlPago, "checkout.html?id=chk_123");
            StringAssert.EndsWith(solicitud.UrlPago, "&volver=web");
            string mid = repositorio.Filas.Single().Referencia;

            // 2. Aplázame aprueba y pide confirmar: el pedido recibe su prepago en la cuenta de Aplázame con la fecha del cobro
            RespuestaNotificacionProveedor respuesta = await servicio.ProcesarNotificacion("aplazame", "Bearer " + CLAVE,
                Cuerpo("pending", "confirmation_required", 30250, mid));
            Assert.AreEqual(200, respuesta.CodigoHttp);
            Assert.AreEqual("{\"status\":\"ok\"}", respuesta.Cuerpo);
            PrepagoEnMemoria prepago = pedidos.Prepagos.Single();
            Assert.AreEqual(Constantes.Prepagos.CUENTA_APLAZAME, prepago.Cuenta);
            Assert.AreEqual(ProveedorCobroCarrito.Aplazame.ConceptoPrepago(PEDIDO, mid), prepago.Concepto, "El mismo concepto que #663");
            Assert.AreEqual(AHORA.Date, prepago.FechaCobro);
            Assert.AreEqual(AHORA.AddDays(15), repositorio.Filas.Single().FechaLimiteCaptura);

            // 3. Con otra clave no se toca nada
            Assert.AreEqual(403, (await servicio.ProcesarNotificacion("Aplazame", "Bearer otra", Cuerpo("ko", mid: mid))).CodigoHttp);
            Assert.AreEqual(EstadosPagoAPlazos.APROBADO, repositorio.Filas.Single().Estado);

            // 4. El pedido sale: el job confirma el cobro
            ahora = AHORA.AddDays(2);
            pedidos.Pedidos[PEDIDO] = Pedido(estado: Constantes.EstadosLineaVenta.ALBARAN);
            ResultadoPlazosPagoAPlazos job = await servicio.ProcesarPlazos(ahora);
            Assert.AreEqual(1, job.Capturadas.Count);
            A.CallTo(() => api.Capturar(mid, 30250, false)).MustHaveHappenedOnceExactly();
            Assert.AreEqual("Capturado", (await servicio.Estado("1", PEDIDO)).Contenido.Estado);
        }

        #endregion
    }
}
