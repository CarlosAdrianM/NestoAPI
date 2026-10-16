using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Pagos.Aplazame;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Pagos.Aplazame
{
    /// <summary>
    /// NestoAPI#663: Aplázame entra en el flujo del carrito de TNV#68 como otro proveedor de cobro. Aquí, lo que es suyo:
    /// solo vale un cobro aprobado (Autorizado) de este cliente, durante 2 horas desde que se confirmó, el pedido va TRN /
    /// PRE con su Prepago en la 57200013 (como los de PrestaShop pagados con Aplázame), y lo que se queda sin pedido se
    /// cancela.
    /// </summary>
    [TestClass]
    public class CobroCarritoAplazameTests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "15191";
        private static readonly DateTime CONFIRMADO = new DateTime(2026, 10, 16, 11, 0, 0);

        private static PagoTPV CobroAplazame(string estado = Constantes.EstadosPagoTPV.AUTORIZADO, string documento = null)
        {
            return new PagoTPV
            {
                Id = 900,
                NumeroOrden = "APP-900",
                Tipo = Constantes.TiposPagoTPV.CARRITO_APLAZAME,
                Empresa = EMPRESA,
                Cliente = CLIENTE,
                Importe = 345.67M,
                Estado = estado,
                Documento = documento,
                FechaCreacion = CONFIRMADO.AddMinutes(-25),
                FechaConfirmacion = CONFIRMADO
            };
        }

        #region Reservar el cobro para crear el pedido

        [TestMethod]
        public void ComprobarCobroCarrito_AplazameConfirmado_Sirve()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(CobroAplazame(), EMPRESA, CLIENTE, CONFIRMADO.AddMinutes(1));

            Assert.IsTrue(reserva.Valido);
            Assert.AreEqual(345.67M, reserva.Importe, "es el importe que se le exige al pedido");
            Assert.AreEqual("APP-900", reserva.NumeroOrden);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_AplazamePendiente_NoCreaPedido()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                CobroAplazame(Constantes.EstadosPagoTPV.PENDIENTE), EMPRESA, CLIENTE, CONFIRMADO);

            Assert.IsFalse(reserva.Valido);
            Assert.IsFalse(reserva.HayQueDevolver);
            StringAssert.Contains(reserva.Motivo, "Aplázame no ha aprobado el pago");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_AplazameRechazado_NoCreaPedido()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                CobroAplazame(Constantes.EstadosPagoTPV.DENEGADO), EMPRESA, CLIENTE, CONFIRMADO);

            Assert.IsFalse(reserva.Valido);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_AplazameCancelado_NoCreaPedido()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(
                CobroAplazame(Constantes.EstadosPagoTPV.CANCELADO), EMPRESA, CLIENTE, CONFIRMADO);

            Assert.IsFalse(reserva.Valido);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_AplazameDeOtroCliente_NoCreaPedido()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(CobroAplazame(), EMPRESA, "25299", CONFIRMADO);

            Assert.IsFalse(reserva.Valido);
            StringAssert.Contains(reserva.Motivo, "No encontramos el pago");
        }

        [TestMethod]
        public void ComprobarCobroCarrito_AplazameYaUsado_NoCreaOtroPedido()
        {
            ReservaCobroCarrito reserva = ServicioPagos.ComprobarCobroCarrito(CobroAplazame(documento: "925700"), EMPRESA, CLIENTE, CONFIRMADO);

            Assert.IsFalse(reserva.Valido);
        }

        [TestMethod]
        public void ComprobarCobroCarrito_Aplazame_ValeDosHorasDesdeLaConfirmacionNoDesdeQueSeCreo()
        {
            // Redsys caduca a los 30 minutos de crearse; Aplázame cuenta desde que se aprobó (la validación de identidad
            // puede tardar), así que a los 90 minutos de crearse todavía vale
            Assert.IsTrue(ServicioPagos.ComprobarCobroCarrito(CobroAplazame(), EMPRESA, CLIENTE, CONFIRMADO.AddMinutes(90)).Valido);
            Assert.IsTrue(ServicioPagos.ComprobarCobroCarrito(CobroAplazame(), EMPRESA, CLIENTE, CONFIRMADO.AddMinutes(120)).Valido);

            ReservaCobroCarrito caducado = ServicioPagos.ComprobarCobroCarrito(CobroAplazame(), EMPRESA, CLIENTE, CONFIRMADO.AddMinutes(121));
            Assert.IsFalse(caducado.Valido);
            Assert.IsTrue(caducado.HayQueDevolver, "se cancela en el acto");
            StringAssert.Contains(caducado.Motivo, "no se le cobrará nada");
        }

        #endregion

        #region El pedido: TRN / PRE y Prepago en la 57200013

        [TestMethod]
        public void Proveedor_Aplazame_EsComoLosPedidosDePrestashopPagadosConAplazame()
        {
            // CanalExternoPedidosPrestashop: FORMA_PAGO_APLAZAME → FormaPago TRN, PlazosPago PRE, CuentaPrepago 57200013
            Assert.AreEqual("TRN", ProveedorCobroCarrito.Aplazame.FormaPago);
            Assert.AreEqual("PRE", ProveedorCobroCarrito.Aplazame.PlazosPago);
            Assert.AreEqual("57200013", ProveedorCobroCarrito.Aplazame.CuentaPrepago);
            Assert.IsTrue(PoliticaPagoCanal.SeCobraEnElMomento(ProveedorCobroCarrito.Aplazame.FormaPago, ProveedorCobroCarrito.Aplazame.PlazosPago),
                "PRE: el picking lo retiene hasta que el prepago cubra el total");
        }

        [TestMethod]
        public void ConstruirPrepago_CobroAplazame_57200013ConSuMid()
        {
            PagoTPV pago = CobroAplazame(documento: "925700");
            pago.Usuario = "cliente@ejemplo.com";

            Prepago prepago = ServicioPagos.ConstruirPrepago(pago, 925700, EMPRESA);

            Assert.AreEqual("57200013", prepago.CuentaContable);
            Assert.AreEqual(345.67M, prepago.Importe);
            Assert.AreEqual(925700, prepago.Pedido);
            Assert.AreEqual("Aplazame app pedido 925700 APP-900", prepago.ConceptoAdicional);
            Assert.IsTrue(prepago.ConceptoAdicional.Length <= 50);
        }

        [TestMethod]
        public void ConstruirPrepago_CobroConTarjeta_SigueIgual()
        {
            PagoTPV pago = CobroAplazame(documento: "925700");
            pago.Tipo = Constantes.TiposPagoTPV.CARRITO_APP;
            pago.NumeroOrden = "0336B1C25299";

            Prepago prepago = ServicioPagos.ConstruirPrepago(pago, 925700, EMPRESA);

            Assert.AreEqual(Constantes.Prepagos.CUENTA_REDSYS, prepago.CuentaContable);
            Assert.AreEqual(ServicioPagos.ConceptoPrepagoPedido(925700, "0336B1C25299"), prepago.ConceptoAdicional);
        }

        [TestMethod]
        public void EsCobroDeCarrito_YNoEsEnlaceDePago()
        {
            Assert.IsTrue(ServicioPagos.EsCobroDeCarrito(CobroAplazame()));
            Assert.IsTrue(ServicioPagos.EsCobroDeLaTienda(CobroAplazame()), "no contabiliza contra el extracto");
            Assert.IsTrue(ServicioPagos.EsPagoDePedido(CobroAplazame(documento: "925700")));
            Assert.IsFalse(ServicioPagos.EsPagoDePedido(CobroAplazame(documento: ServicioPagos.MARCA_COBRO_RESERVADO)));
        }

        #endregion

        #region Huérfanos

        [TestMethod]
        public void Huerfano_AplazameConfirmadoSinPedidoALasDosHoras_SeCancela()
        {
            Assert.AreEqual(CobrosCarritoJobsService.Decision.Nada, CobrosCarritoJobsService.Decidir(CobroAplazame(), CONFIRMADO.AddMinutes(60)),
                "a la hora todavía no: con Redsys sí lo habría devuelto");
            Assert.AreEqual(CobrosCarritoJobsService.Decision.Deshacer, CobrosCarritoJobsService.Decidir(CobroAplazame(), CONFIRMADO.AddMinutes(121)));
            Assert.AreEqual(CobrosCarritoJobsService.Decision.Avisar,
                CobrosCarritoJobsService.Decidir(CobroAplazame(documento: ServicioPagos.MARCA_COBRO_RESERVADO), CONFIRMADO.AddMinutes(121)));
            Assert.AreEqual(CobrosCarritoJobsService.Decision.Nada,
                CobrosCarritoJobsService.Decidir(CobroAplazame(documento: "925700"), CONFIRMADO.AddDays(1)));
        }

        [TestMethod]
        public async Task Huerfano_ElJobLoDeshacePorElMismoCaminoQueRedsys()
        {
            // DevolverCobro es el punto único: para un cobro de Aplázame cancela en Aplázame (ver ServicioPagos.DevolverCobro)
            IServicioPagos servicioPagos = A.Fake<IServicioPagos>();
            A.CallTo(() => servicioPagos.DevolverCobro(A<int>._, A<string>._)).Returns(true);
            PagoTPV huerfano = CobroAplazame();
            PagoTPV aTiempo = CobroAplazame();
            aTiempo.Id = 901;
            aTiempo.FechaConfirmacion = CONFIRMADO.AddMinutes(100);

            await CobrosCarritoJobsService.RevisarCobrosHuerfanos(servicioPagos,
                _ => Task.FromResult(new List<PagoTPV> { huerfano, aTiempo }), CONFIRMADO.AddMinutes(130));

            A.CallTo(() => servicioPagos.DevolverCobro(900, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioPagos.DevolverCobro(901, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Huerfano_RedsysSigueConSusTreintaMinutos()
        {
            PagoTPV redsys = CobroAplazame();
            redsys.Tipo = Constantes.TiposPagoTPV.CARRITO_APP;
            redsys.FechaCreacion = CONFIRMADO;

            Assert.AreEqual(CobrosCarritoJobsService.Decision.Nada, CobrosCarritoJobsService.Decidir(redsys, CONFIRMADO.AddMinutes(30)));
            Assert.AreEqual(CobrosCarritoJobsService.Decision.Deshacer, CobrosCarritoJobsService.Decidir(redsys, CONFIRMADO.AddMinutes(31)));
        }

        #endregion

        #region Disponible

        private static ConfiguracionAplazame Config(ModoAplazame modo, decimal? minimo = null, decimal? maximo = null, params string[] prueba)
        {
            return new ConfiguracionAplazame
            {
                Modo = modo,
                ClavePrivada = "privada",
                ClavePublica = "publica",
                ImporteMinimo = minimo,
                ImporteMaximo = maximo,
                ClientesPrueba = ConfiguracionAplazame.InterpretarClientes(string.Join(",", prueba))
            };
        }

        [TestMethod]
        public void Disponible_Apagado_No()
        {
            DisponibilidadAplazameDTO d = Config(ModoAplazame.Apagado).Disponibilidad(CLIENTE, 300, true, false);
            Assert.IsFalse(d.Disponible);
            Assert.IsNotNull(d.Motivo);
        }

        [TestMethod]
        public void Disponible_Sandbox_SoloParaLosClientesDePrueba()
        {
            ConfiguracionAplazame config = Config(ModoAplazame.Sandbox, prueba: new[] { "15191", " 25299 " });

            DisponibilidadAplazameDTO deprueba = config.Disponibilidad("25299", 300, true, false);
            Assert.IsTrue(deprueba.Disponible);
            Assert.IsTrue(deprueba.Sandbox);
            Assert.IsNull(deprueba.Motivo);
            Assert.IsFalse(config.Disponibilidad("10000", 300, true, false).Disponible);
        }

        [TestMethod]
        public void Disponible_Activo_ParaTodos()
        {
            DisponibilidadAplazameDTO d = Config(ModoAplazame.Activo).Disponibilidad("10000", 300, true, false);
            Assert.IsTrue(d.Disponible);
            Assert.IsFalse(d.Sandbox);
        }

        [TestMethod]
        public void Disponible_SinCobrarElCarritoAntes_No()
        {
            Assert.IsFalse(Config(ModoAplazame.Activo).Disponibilidad(CLIENTE, 300, cobrarCarritoAntes: false, ocultaPrecios: false).Disponible);
        }

        [TestMethod]
        public void Disponible_SinVerLosPrecios_No()
        {
            Assert.IsFalse(Config(ModoAplazame.Activo).Disponibilidad(CLIENTE, 300, cobrarCarritoAntes: true, ocultaPrecios: true).Disponible);
        }

        [TestMethod]
        public void Disponible_FueraDeLosImportes_No()
        {
            ConfiguracionAplazame config = Config(ModoAplazame.Activo, minimo: 60, maximo: 3000);

            Assert.IsFalse(config.Disponibilidad(CLIENTE, 59.99M, true, false).Disponible);
            Assert.IsTrue(config.Disponibilidad(CLIENTE, 60M, true, false).Disponible);
            Assert.IsTrue(config.Disponibilidad(CLIENTE, 3000M, true, false).Disponible);
            Assert.IsFalse(config.Disponibilidad(CLIENTE, 3000.01M, true, false).Disponible);
        }

        [TestMethod]
        public void Disponible_SinClaves_No()
        {
            ConfiguracionAplazame config = Config(ModoAplazame.Activo);
            config.ClavePrivada = null;
            Assert.IsFalse(config.Disponibilidad(CLIENTE, 300, true, false).Disponible);
        }

        [TestMethod]
        public void Interruptor_SinFilaOValorDesconocido_Apagado()
        {
            Assert.AreEqual(ModoAplazame.Apagado, ConfiguracionAplazame.InterpretarModo(null));
            Assert.AreEqual(ModoAplazame.Apagado, ConfiguracionAplazame.InterpretarModo("1"));
            Assert.AreEqual(ModoAplazame.Sandbox, ConfiguracionAplazame.InterpretarModo(" sandbox "));
            Assert.AreEqual(ModoAplazame.Activo, ConfiguracionAplazame.InterpretarModo("Activo"));
            Assert.AreEqual(150.5M, ConfiguracionAplazame.InterpretarImporte("150,50"));
            Assert.IsNull(ConfiguracionAplazame.InterpretarImporte("mucho"));
        }

        [TestMethod]
        public void Leer_FalloAlLeerElParametro_Apagado()
        {
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("BD caída"));

            Assert.AreEqual(ModoAplazame.Apagado, ConfiguracionAplazame.Leer(lector).Modo);
        }

        #endregion
    }
}
