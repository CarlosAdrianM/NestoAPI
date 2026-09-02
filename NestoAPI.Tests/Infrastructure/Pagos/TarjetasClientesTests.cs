using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#178: tokenización de tarjetas. La captura del token en la notificación, las
    /// reglas de una tarjeta usable y el cobro directo con tarjeta guardada.
    /// </summary>
    [TestClass]
    public class TarjetasClientesTests
    {
        private IRedsysService _redsysService;
        private ITarjetaClienteStore _tarjetaStore;
        private ILogService _logService;
        private ServicioPagos _servicio;

        [TestInitialize]
        public void Setup()
        {
            _redsysService = A.Fake<IRedsysService>();
            _tarjetaStore = A.Fake<ITarjetaClienteStore>();
            _logService = A.Fake<ILogService>();
            _servicio = new ServicioPagos(
                _redsysService,
                A.Fake<IContabilidadService>(),
                A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(),
                _logService,
                _tarjetaStore);
        }

        #region Helpers de RedsysService

        [TestMethod]
        public void ExtraerUltimosDigitos_ConNumeroEnmascarado_DevuelveLosDigitosFinales()
        {
            Assert.AreEqual("04", RedsysService.ExtraerUltimosDigitos("454881******04"));
            Assert.AreEqual("0004", RedsysService.ExtraerUltimosDigitos("454881****0004"));
            // Nunca más de 4, que es lo que cabe en la columna
            Assert.AreEqual("1234", RedsysService.ExtraerUltimosDigitos("****00051234"));
        }

        [TestMethod]
        public void ExtraerUltimosDigitos_SinDigitos_DevuelveNull()
        {
            Assert.IsNull(RedsysService.ExtraerUltimosDigitos(null));
            Assert.IsNull(RedsysService.ExtraerUltimosDigitos(""));
            Assert.IsNull(RedsysService.ExtraerUltimosDigitos("******"));
        }

        [TestMethod]
        public void ParsearCaducidadRedsys_FormatoAAMM_DevuelveUltimoDiaDelMes()
        {
            // "2712" = diciembre de 2027: la tarjeta vale hasta el 31/12/2027
            Assert.AreEqual(new DateTime(2027, 12, 31), RedsysService.ParsearCaducidadRedsys("2712"));
            Assert.AreEqual(new DateTime(2026, 2, 28), RedsysService.ParsearCaducidadRedsys("2602"));
        }

        [TestMethod]
        public void ParsearCaducidadRedsys_ValorRoto_DevuelveNull()
        {
            Assert.IsNull(RedsysService.ParsearCaducidadRedsys(null));
            Assert.IsNull(RedsysService.ParsearCaducidadRedsys("12"));
            Assert.IsNull(RedsysService.ParsearCaducidadRedsys("2713")); // mes 13
            Assert.IsNull(RedsysService.ParsearCaducidadRedsys("27AB"));
        }

        [TestMethod]
        public void NombreMarcaTarjeta_CodigosConocidos_DevuelveElNombre()
        {
            Assert.AreEqual("Visa", RedsysService.NombreMarcaTarjeta("1"));
            Assert.AreEqual("Mastercard", RedsysService.NombreMarcaTarjeta("2"));
            Assert.AreEqual("Amex", RedsysService.NombreMarcaTarjeta("8"));
            // Un código desconocido se guarda tal cual: mejor un "77" que un null
            Assert.AreEqual("77", RedsysService.NombreMarcaTarjeta("77"));
            Assert.IsNull(RedsysService.NombreMarcaTarjeta(null));
        }

        #endregion

        #region TarjetaCliente.Usable

        [TestMethod]
        public void Usable_ActivaYSinCaducar_EsCierto()
        {
            var tarjeta = new TarjetaCliente { Activa = true, FechaCaducidad = DateTime.Today.AddYears(1) };
            Assert.IsTrue(tarjeta.Usable);
        }

        [TestMethod]
        public void Usable_Caducada_EsFalso()
        {
            var tarjeta = new TarjetaCliente { Activa = true, FechaCaducidad = DateTime.Today.AddDays(-1) };
            Assert.IsFalse(tarjeta.Usable);
        }

        [TestMethod]
        public void Usable_Desactivada_EsFalso()
        {
            var tarjeta = new TarjetaCliente { Activa = false };
            Assert.IsFalse(tarjeta.Usable);
        }

        [TestMethod]
        public void Usable_ConTresFallosConsecutivos_EsFalso()
        {
            // No se martillea una tarjeta que ya no funciona; un cobro bueno resetea el contador
            var tarjeta = new TarjetaCliente { Activa = true, IntentosFallidosConsecutivos = 3 };
            Assert.IsFalse(tarjeta.Usable);
        }

        #endregion

        #region Captura del token en la notificación

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_ConToken_DaDeAltaLaTarjetaDelCliente()
        {
            var resultado = new ResultadoValidacionNotificacion
            {
                TokenTarjeta = "token123",
                CofTxnId = "cof456",
                UltimosDigitosTarjeta = "1234",
                MarcaTarjeta = "Visa",
                TipoTarjeta = "C",
                FechaCaducidadTarjeta = new DateTime(2027, 12, 31)
            };
            var pago = new PagoTPV { Empresa = "1  ", Cliente = "15191  ", Contacto = "0", NumeroOrden = "ORD1", Usuario = "app" };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago);

            A.CallTo(() => _tarjetaStore.GuardarOActualizar(A<TarjetaCliente>.That.Matches(t =>
                t.Cliente == "15191"
                && t.Empresa == "1"
                && t.TokenRedsys == "token123"
                && t.CofTxnId == "cof456"
                && t.UltimosDigitos == "1234"
                && t.MarcaTarjeta == "Visa"
                && t.FechaCaducidad == new DateTime(2027, 12, 31))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_SinToken_NoGuardaNada()
        {
            var resultado = new ResultadoValidacionNotificacion { TokenTarjeta = null };
            var pago = new PagoTPV { Cliente = "15191" };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago);

            A.CallTo(() => _tarjetaStore.GuardarOActualizar(A<TarjetaCliente>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_SinCliente_NoGuardaNada()
        {
            // Un enlace de pago sin cliente no tiene a quién asignarle la tarjeta
            var resultado = new ResultadoValidacionNotificacion { TokenTarjeta = "token123" };
            var pago = new PagoTPV { Cliente = null };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago);

            A.CallTo(() => _tarjetaStore.GuardarOActualizar(A<TarjetaCliente>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_SiElStoreRevienta_NoTiraElProcesadoDelPago()
        {
            // El cobro ya está hecho: perder el token es un log, no una excepción
            A.CallTo(() => _tarjetaStore.GuardarOActualizar(A<TarjetaCliente>._)).Throws(new Exception("BD caida"));
            var resultado = new ResultadoValidacionNotificacion { TokenTarjeta = "token123" };
            var pago = new PagoTPV { Cliente = "15191", NumeroOrden = "ORD1" };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago); // no lanza

            A.CallTo(() => _logService.LogError(A<string>._, A<Exception>._)).MustHaveHappened();
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_SinUltimosDigitos_GuardaLaTarjetaIgualYDejaRastro()
        {
            // 01/09/26: el terminal no manda Ds_Card_Number (lo activa el banco) y el alta real se
            // perdió. Los dígitos son cosmética: el token se guarda con o sin ellos.
            var resultado = new ResultadoValidacionNotificacion
            {
                TokenTarjeta = "token123",
                UltimosDigitosTarjeta = null,
                MarcaTarjeta = "Visa",
                FechaCaducidadTarjeta = new DateTime(2027, 12, 31)
            };
            var pago = new PagoTPV { Empresa = "1", Cliente = "15191", NumeroOrden = "E6E4EDC15191" };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago);

            A.CallTo(() => _tarjetaStore.GuardarOActualizar(A<TarjetaCliente>.That.Matches(t =>
                t.TokenRedsys == "token123" && t.UltimosDigitos == null && t.MarcaTarjeta == "Visa")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => _logService.LogError(
                A<string>.That.Matches(m => m.Contains("E6E4EDC15191") && m.Contains("Visa que caduca en 12/2027")),
                A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void GuardarTarjetaDeLaNotificacion_ConUltimosDigitos_NoDejaRastroDeDiagnostico()
        {
            var resultado = new ResultadoValidacionNotificacion { TokenTarjeta = "token123", UltimosDigitosTarjeta = "1234" };
            var pago = new PagoTPV { Empresa = "1", Cliente = "15191", NumeroOrden = "ORD1" };

            _servicio.GuardarTarjetaDeLaNotificacion(resultado, pago);

            A.CallTo(() => _logService.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void UltimosDigitosDe_PrefiereDsCardLast4YSiNoElNumeroEnmascarado()
        {
            Assert.AreEqual("9876", RedsysService.UltimosDigitosDe(new RespuestaRedsys { Ds_Card_Last4 = "9876", Ds_Card_Number = "454881******0004" }));
            Assert.AreEqual("0004", RedsysService.UltimosDigitosDe(new RespuestaRedsys { Ds_Card_Number = "454881******0004" }));
            Assert.IsNull(RedsysService.UltimosDigitosDe(new RespuestaRedsys { Ds_Merchant_Identifier = "token" }));
            Assert.IsNull(RedsysService.UltimosDigitosDe(null));
        }

        [TestMethod]
        public void ParaDiagnostico_DevuelveElJsonCompletoConElTokenTapado()
        {
            // Diagnóstico temporal (#445): se quiere ver TODO lo que manda el terminal (claves y
            // valores, también las no mapeadas), salvo el token, que es con lo que se cobra
            string json = "{\"Ds_Order\":\"E6E4EDC15191\",\"Ds_Merchant_Identifier\":\"726732bcff81808ce9547f939e20b16d2fced12b\",\"Ds_ExpiryDate\":\"2803\",\"Ds_Card_Typology\":\"CONSUMO\",\"Ds_Response\":\"0000\"}";

            string campos = RedsysService.ParaDiagnostico(json);

            Assert.IsFalse(campos.Contains("726732bcff81808ce9547f939e20b16d2fced12b"));
            Assert.IsTrue(campos.Contains("\"Ds_Merchant_Identifier\":\"726732******************************d12b\""), campos);
            Assert.IsTrue(campos.Contains("\"Ds_ExpiryDate\":\"2803\""));
            Assert.IsTrue(campos.Contains("\"Ds_Card_Typology\":\"CONSUMO\""), "las claves no mapeadas también se ven");
            Assert.IsTrue(campos.Contains("\"Ds_Order\":\"E6E4EDC15191\""));
            Assert.IsNull(RedsysService.ParaDiagnostico("esto no es json"));
            Assert.IsNull(RedsysService.ParaDiagnostico(null));
        }

        [TestMethod]
        public void ParametrosDeLaRespuestaREST_ConErrorCode_LanzaConElCodigoYLaRespuesta()
        {
            // 02/09/26, primer cobro real con token: Redsys respondió 200 con {"errorCode":...} y
            // sin parámetros, y aquello acababa en "El valor no puede ser nulo: value"
            Exception ex = Assert.ThrowsException<Exception>(() =>
                RedsysService.ParametrosDeLaRespuestaREST("{\"errorCode\":\"SIS0431\"}"));

            StringAssert.Contains(ex.Message, "SIS0431");
            StringAssert.Contains(ex.Message, "{\"errorCode\":\"SIS0431\"}");
        }

        [TestMethod]
        public void ParametrosDeLaRespuestaREST_SinParametros_LanzaConLaRespuesta()
        {
            Assert.ThrowsException<Exception>(() => RedsysService.ParametrosDeLaRespuestaREST("{}"));
            Assert.ThrowsException<Exception>(() => RedsysService.ParametrosDeLaRespuestaREST(""));
            Assert.ThrowsException<Exception>(() => RedsysService.ParametrosDeLaRespuestaREST("no es json"));
        }

        [TestMethod]
        public void ParametrosDeLaRespuestaREST_Correcta_DevuelveLosParametros()
        {
            string parametros = RedsysService.ParametrosDeLaRespuestaREST(
                "{\"Ds_SignatureVersion\":\"HMAC_SHA256_V1\",\"Ds_MerchantParameters\":\"eyJEc19SZXNwb25zZSI6IjAwMDAifQ==\",\"Ds_Signature\":\"abc\"}");

            Assert.AreEqual("eyJEc19SZXNwb25zZSI6IjAwMDAifQ==", parametros);
        }

        [TestMethod]
        public void LogDiagnosticoRedsys_DejaEnElmahElJsonConLaOrden()
        {
            // #445 (temporal): la respuesta al POST REST / la notificación, claves y valores
            _servicio.LogDiagnosticoRedsys("Respuesta REST al cobro con tarjeta guardada", "ORD7",
                "{\"Ds_Response\":\"0000\",\"Ds_Card_Typology\":\"CONSUMO\"}");

            A.CallTo(() => _logService.LogError(
                A<string>.That.Matches(m => m.Contains("[Redsys diag #445]") && m.Contains("ORD7")
                    && m.Contains("\"Ds_Card_Typology\":\"CONSUMO\"")),
                A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void LogDiagnosticoRedsys_NuncaRompeElCobro()
        {
            A.CallTo(() => _logService.LogError(A<string>._, A<Exception>._)).Throws(new Exception("ELMAH caido"));

            _servicio.LogDiagnosticoRedsys("Notificación recibida", "ORD7", null); // no lanza
        }

        [TestMethod]
        public void Describir_ConDigitos_MarcaAcabadaEn()
        {
            Assert.AreEqual("Visa acabada en 1234", TarjetaCliente.Describir("Visa", "1234", new DateTime(2027, 12, 31)));
            Assert.AreEqual("Tarjeta acabada en 1234", TarjetaCliente.Describir(null, "1234", null));
        }

        [TestMethod]
        public void Describir_SinDigitos_MarcaYCaducidad()
        {
            Assert.AreEqual("Visa que caduca en 12/2027", TarjetaCliente.Describir("Visa", null, new DateTime(2027, 12, 31)));
            Assert.AreEqual("Tarjeta que caduca en 12/2027", TarjetaCliente.Describir("", "", new DateTime(2027, 12, 31)));
        }

        [TestMethod]
        public void Describir_SinNada_TarjetaGuardada()
        {
            Assert.AreEqual("Tarjeta guardada", TarjetaCliente.Describir(null, null, null));
            Assert.AreEqual("Mastercard", TarjetaCliente.Describir("Mastercard", null, null));
        }

        [TestMethod]
        public void TarjetaClienteDTO_LlevaLaDescripcionCompuestaPorElServidor()
        {
            var tarjeta = new TarjetaCliente { Id = 7, MarcaTarjeta = "Visa", UltimosDigitos = null, FechaCaducidad = new DateTime(2027, 12, 31) };

            TarjetaClienteDTO dto = TarjetaClienteDTO.Desde(tarjeta);

            Assert.AreEqual("Visa que caduca en 12/2027", dto.Descripcion);
            Assert.IsNull(dto.UltimosDigitos);
        }

        #endregion

        #region IniciarPago pide tokenizar solo en pedidos de la app

        [TestMethod]
        public void IniciarPago_PedidoDeLaApp_PideTokenizarLaTarjeta()
        {
            // El objetivo del #178: que el cliente meta la tarjeta UNA vez. Cada pedido cobrado
            // sin tokenizar es un cliente al que habrá que volver a pedírsela.
            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .Returns(new ParametrosRedsysFirmados { NumeroOrden = "ORD", Ds_SignatureVersion = "V1", Ds_MerchantParameters = "p", Ds_Signature = "s" });

            var solicitud = new SolicitudPagoTPV
            {
                Importe = 50m,
                Descripcion = "Pago pedido 923001",
                Cliente = "15191",
                Pedido = 923001
            };

            try { _servicio.IniciarPago(solicitud, "usuario").Wait(); }
            catch (AggregateException) { /* BD no disponible en test: la llamada a Redsys ya se hizo */ }

            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, true, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void IniciarPago_ConTarjetaGuardada_MandaLaReferenciaYNoPideTokenizar()
        {
            // Plan B (02/09/26, SIS0883): el cliente confirma en la pasarela con su tarjeta ya
            // cargada. Va la referencia y el COF del alta; no se pide tokenizar (ya lo está).
            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .Returns(new ParametrosRedsysFirmados { NumeroOrden = "ORD", Ds_SignatureVersion = "V1", Ds_MerchantParameters = "p", Ds_Signature = "s" });

            var solicitud = new SolicitudPagoTPV
            {
                Importe = 3.84m,
                Descripcion = "Pago pedido 925300",
                Cliente = "15191",
                Pedido = 925300,
                TarjetaGuardada = new TarjetaCliente { TokenRedsys = "a26a5b03", CofTxnId = "232026245295044" }
            };

            try { _servicio.IniciarPago(solicitud, "usuario").Wait(); }
            catch (AggregateException) { /* BD no disponible en test: la llamada a Redsys ya se hizo */ }

            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, false, "a26a5b03", "232026245295044"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void TarjetaGuardadaDe_SoloSiEsDelClienteYUsable()
        {
            A.CallTo(() => _tarjetaStore.ObtenerPorId(7)).Returns(new TarjetaCliente
            {
                Id = 7, Empresa = "1", Cliente = "15191", TokenRedsys = "tok", Activa = true, FechaCaducidad = new DateTime(2030, 1, 31)
            });
            A.CallTo(() => _tarjetaStore.ObtenerPorId(8)).Returns(new TarjetaCliente
            {
                Id = 8, Empresa = "1", Cliente = "15191", TokenRedsys = "tok", Activa = false
            });

            Assert.IsNotNull(_servicio.TarjetaGuardadaDe("1", "15191", 7));
            Assert.IsNull(_servicio.TarjetaGuardadaDe("1", "99999", 7), "de otro cliente");
            Assert.IsNull(_servicio.TarjetaGuardadaDe("1", "15191", 8), "desactivada");
            Assert.IsNull(_servicio.TarjetaGuardadaDe("1", "15191", 9), "no existe");
        }

        [TestMethod]
        public void ModoCobroTarjetaGuardada_SoloEsDirectoConTrueExplicito()
        {
            // Sin la clave (o con cualquier otra cosa) manda el plan B: el más seguro hoy
            Assert.IsTrue(ModoCobroTarjetaGuardada.Leer("true"));
            Assert.IsTrue(ModoCobroTarjetaGuardada.Leer(" TRUE "));
            Assert.IsFalse(ModoCobroTarjetaGuardada.Leer("false"));
            Assert.IsFalse(ModoCobroTarjetaGuardada.Leer(null));
            Assert.IsFalse(ModoCobroTarjetaGuardada.Leer(""));
        }

        [TestMethod]
        public void IniciarPago_EnlaceDePagoNormal_NoTokeniza()
        {
            // Retrocompatibilidad (#178): los enlaces de pago de siempre no tokenizan
            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .Returns(new ParametrosRedsysFirmados { NumeroOrden = "ORD", Ds_SignatureVersion = "V1", Ds_MerchantParameters = "p", Ds_Signature = "s" });

            var solicitud = new SolicitudPagoTPV
            {
                Importe = 50m,
                Descripcion = "Pago factura NV123",
                Cliente = "15191"
            };

            try { _servicio.IniciarPago(solicitud, "usuario").Wait(); }
            catch (AggregateException) { /* BD no disponible en test */ }

            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, false, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        #endregion

        #region Alta de tarjeta sin cobro (0 EUR)

        [TestMethod]
        public void IniciarAltaTarjeta_PideCeroEurosSoloTarjetaYToken()
        {
            // El alta es una autorización de 0 EUR: tokeniza sin cobrar. Solo tarjeta (Bizum no
            // deja token) y con la tokenización pedida.
            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .Returns(new ParametrosRedsysFirmados { NumeroOrden = "ORD", Ds_SignatureVersion = "V1", Ds_MerchantParameters = "p", Ds_Signature = "s" });

            try { _servicio.IniciarAltaTarjeta(new SolicitudAltaTarjeta { Cliente = "15191" }, "15191").Wait(); }
            catch (AggregateException) { /* BD no disponible en test: la llamada a Redsys ya se hizo */ }

            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                0m, A<string>._, A<string>._, "15191", A<string>._, A<string>._, A<string>._, "C", A<string>._, true, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task IniciarAltaTarjeta_SinCliente_LanzaExcepcion()
        {
            // Una tarjeta sin cliente no tiene dueño: no hay alta que hacer
            await _servicio.IniciarAltaTarjeta(new SolicitudAltaTarjeta { Cliente = null }, "usuario");
        }

        [TestMethod]
        public void EsAltaTarjeta_DistinguePorElTipo()
        {
            Assert.IsTrue(ServicioPagos.EsAltaTarjeta(new PagoTPV { Tipo = "AltaTarjeta" }));
            Assert.IsFalse(ServicioPagos.EsAltaTarjeta(new PagoTPV { Tipo = "PedidoApp" }));
            Assert.IsFalse(ServicioPagos.EsAltaTarjeta(new PagoTPV { Tipo = "TPVVirtual" }));
            Assert.IsFalse(ServicioPagos.EsAltaTarjeta(null));
        }

        [TestMethod]
        public async Task RegenerarPagoDenegado_AltaTarjeta_NiNuevoEnlaceNiCorreo()
        {
            // Como los pedidos de la app: cancelar el alta en la pasarela es cosa de la app,
            // no del circuito de enlaces de pago
            var servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            var servicio = new ServicioPagos(_redsysService, A.Fake<IContabilidadService>(),
                A.Fake<ILectorParametrosUsuario>(), servicioCorreo, _logService, _tarjetaStore);
            var pagoDenegado = new PagoTPV { Id = 8, Tipo = "AltaTarjeta", Importe = 0m };

            await servicio.RegenerarPagoDenegado(pagoDenegado, A.Fake<NVEntities>());

            A.CallTo(() => _redsysService.CrearParametrosTPVVirtual(
                A<decimal>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<bool>._, A<string>._, A<string>._))
                .MustNotHaveHappened();
            A.CallTo(() => servicioCorreo.EnviarCorreoSMTP(A<System.Net.Mail.MailMessage>._)).MustNotHaveHappened();
        }

        #endregion

        #region CobrarConTarjetaGuardada: validaciones previas (KO = ni cargo ni pedido)

        [TestMethod]
        public async Task CobrarConTarjetaGuardada_TarjetaInexistente_NoAutorizaYNoLlamaARedsys()
        {
            A.CallTo(() => _tarjetaStore.ObtenerPorId(99)).Returns(null);

            ResultadoCobroTarjetaGuardada resultado = await _servicio.CobrarConTarjetaGuardada(
                new SolicitudCobroTarjetaGuardada { Cliente = "15191", Importe = 50m, TarjetaId = 99 }, "usuario");

            Assert.IsFalse(resultado.Autorizado);
            Assert.IsNotNull(resultado.MensajeError);
            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CobrarConTarjetaGuardada_TarjetaDeOtroCliente_NoAutoriza()
        {
            // La app manda el id, pero el dueño lo comprueba el servidor
            A.CallTo(() => _tarjetaStore.ObtenerPorId(7)).Returns(new TarjetaCliente
            {
                Id = 7,
                Empresa = "1",
                Cliente = "OTRO",
                Activa = true
            });

            ResultadoCobroTarjetaGuardada resultado = await _servicio.CobrarConTarjetaGuardada(
                new SolicitudCobroTarjetaGuardada { Empresa = "1", Cliente = "15191", Importe = 50m, TarjetaId = 7 }, "usuario");

            Assert.IsFalse(resultado.Autorizado);
            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CobrarConTarjetaGuardada_TarjetaCaducada_NoAutorizaYLoDice()
        {
            A.CallTo(() => _tarjetaStore.ObtenerPorId(7)).Returns(new TarjetaCliente
            {
                Id = 7,
                Empresa = "1",
                Cliente = "15191",
                Activa = true,
                UltimosDigitos = "1234",
                FechaCaducidad = DateTime.Today.AddMonths(-1)
            });

            ResultadoCobroTarjetaGuardada resultado = await _servicio.CobrarConTarjetaGuardada(
                new SolicitudCobroTarjetaGuardada { Empresa = "1", Cliente = "15191", Importe = 50m, TarjetaId = 7 }, "usuario");

            Assert.IsFalse(resultado.Autorizado);
            StringAssert.Contains(resultado.MensajeError, "caducada");
            A.CallTo(() => _redsysService.EnviarPeticionREST(A<ParametrosRedsysFirmados>._)).MustNotHaveHappened();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task CobrarConTarjetaGuardada_ImporteCero_LanzaExcepcion()
        {
            await _servicio.CobrarConTarjetaGuardada(
                new SolicitudCobroTarjetaGuardada { Cliente = "15191", Importe = 0m, TarjetaId = 1 }, "usuario");
        }

        #endregion
    }
}
