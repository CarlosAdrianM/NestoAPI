using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>
    /// NestoAPI#343: captura de diagnóstico cuando prdCrearFacturaVta falla por «gasto sin centro de coste». Solo
    /// registra (un error informativo en ELMAH) y nunca lanza ni cambia la facturación.
    /// </summary>
    [TestClass]
    public class DiagnosticoGastoSinCentroCosteTests
    {
        // Texto real del ELMAH del 21/10/26 (pedido 929032), con el ruido de transacciones del SP.
        private const string MENSAJE_REAL =
            "Está intentando contabilizar un gasto sin centro de coste, delegación o departamento\r\n" +
            "Error al contabilizar\r\n" +
            "El recuento de transacciones después de EXECUTE indica un número no coincidente de instrucciones BEGIN y COMMIT. Recuento anterior = 1, recuento actual = 0.";

        private ILectorEstadoGastoSinCentroCoste lector;
        private ILogService log;
        private DiagnosticoGastoSinCentroCoste diagnostico;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorEstadoGastoSinCentroCoste>();
            log = A.Fake<ILogService>();
            diagnostico = new DiagnosticoGastoSinCentroCoste(lector, log);
        }

        private static ContextoFalloGastoSinCentroCoste Contexto(bool conCorreccion = true)
        {
            return new ContextoFalloGastoSinCentroCoste
            {
                Empresa = "1  ",
                Pedido = 929032,
                Usuario = "NUEVAVISION\\Alfredo",
                SerieAlEmpezar = "NV",
                HuboCorreccionSerie = conCorreccion,
                SerieAntesCorreccion = conCorreccion ? "NV" : null,
                SerieDespuesCorreccion = conCorreccion ? "RV" : null,
                HoraCorreccionSerie = conCorreccion ? new DateTime(2026, 10, 21, 12, 18, 8, 53) : (DateTime?)null,
                HoraInicioProcedimiento = new DateTime(2026, 10, 21, 12, 18, 8, 100),
                HoraFallo = new DateTime(2026, 10, 21, 12, 18, 8, 340)
            };
        }

        private static EstadoGastoSinCentroCoste EstadoConFilaAjena()
        {
            return new EstadoGastoSinCentroCoste
            {
                HoraCaptura = new DateTime(2026, 10, 21, 12, 18, 8, 400),
                PreContabilidadDiario = new List<PreContabilidad>
                {
                    new PreContabilidad { Nº_Orden = 7001, Empresa = "1  ", TipoCuenta = "1  ", Nº_Cuenta = "62400002  ", Debe = 5, Nº_Documento = "NV2617999", Concepto = "Portes", Usuario = "NUEVAVISION\\Otro", Delegación = "ALG", CentroCoste = null, Departamento = null },
                    new PreContabilidad { Nº_Orden = 7002, Empresa = "1  ", TipoCuenta = "2  ", Nº_Cuenta = "16026", Haber = 5, Nº_Documento = "NV2617999" }
                },
                LineasCuentaContable = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Nº_Orden = 329242600, Producto = "62400000", Bruto = -3, Estado = 2, Delegación = "ALG", CentroCoste = "MPP", Departamento = "COM" }
                },
                SerieCabecera = "RV ",
                Sesiones = new List<string> { "sesión 88 | NUEVAVISION\\RDS2016$ | locks escritura PreContabilidad 12" }
            };
        }

        [TestMethod]
        public void EsErrorGastoSinCentroCoste_MensajeRealDelSp_True()
        {
            Assert.IsTrue(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(new Exception(MENSAJE_REAL)));
        }

        [TestMethod]
        public void EsErrorGastoSinCentroCoste_VarianteDePrdComprobarCamposNecesarios_True()
        {
            Assert.IsTrue(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(
                new Exception("Esta intentando contabilizar un gasto sin centro de coste,delegación o departamento")));
        }

        [TestMethod]
        public void EsErrorGastoSinCentroCoste_EnUnaExcepcionInterna_True()
        {
            Exception envuelta = new Exception("Error al ejecutar el procedimiento almacenado de facturación", new Exception(MENSAJE_REAL));
            Assert.IsTrue(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(envuelta));
        }

        [TestMethod]
        public void EsErrorGastoSinCentroCoste_OtrosErroresDelSp_False()
        {
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(new Exception("Se ha producido un descuadre. Avise al Dpto. Informática")));
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(new Exception("Esta intentando contabilizar en un centro de coste con estado nulo")));
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.EsErrorGastoSinCentroCoste(null));
        }

        [TestMethod]
        public async Task RegistrarSiProcede_ConElMensaje_LeeElEstadoYRegistraUnSoloError()
        {
            Exception error = new Exception(MENSAJE_REAL);
            A.CallTo(() => lector.Leer("1  ", 929032)).Returns(Task.FromResult(EstadoConFilaAjena()));
            string registrado = null;
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).Invokes((string m, Exception e) => registrado = m);

            bool resultado = await diagnostico.RegistrarSiProcede(error, Contexto());

            Assert.IsTrue(resultado);
            A.CallTo(() => lector.Leer("1  ", 929032)).MustHaveHappenedOnceExactly();
            A.CallTo(() => log.LogError(A<string>._, error)).MustHaveHappenedOnceExactly();
            StringAssert.StartsWith(registrado, DiagnosticoGastoSinCentroCoste.PREFIJO_MENSAJE);
            StringAssert.Contains(registrado, "pedido 929032 (empresa 1)");
            StringAssert.Contains(registrado, "2 fila(s) en PreContabilidad _FRAVTA, 1 incumplen");
            StringAssert.Contains(registrado, "serie corregida NV→RV a las 21/10/26 12:18:08.053");
            StringAssert.Contains(registrado, "INCUMPLE nº orden 7001");
            StringAssert.Contains(registrado, "doc NV2617999");
            StringAssert.Contains(registrado, "cc NULL");
            StringAssert.Contains(registrado, "nº orden 329242600 | 62400000");
            StringAssert.Contains(registrado, "cc 'MPP'");
            StringAssert.Contains(registrado, "serie actual en CabPedidoVta (NOLOCK): RV");
            StringAssert.Contains(registrado, "sesión 88");
        }

        [TestMethod]
        public async Task RegistrarSiProcede_ConOtroMensaje_NiLeeNiRegistra()
        {
            bool resultado = await diagnostico.RegistrarSiProcede(new Exception("Se ha producido un descuadre. Avise al Dpto. Informática"), Contexto());

            Assert.IsFalse(resultado);
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).MustNotHaveHappened();
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RegistrarSiProcede_SinCorreccionDeSerie_LoDice()
        {
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).Returns(Task.FromResult(new EstadoGastoSinCentroCoste()));
            string registrado = null;
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).Invokes((string m, Exception e) => registrado = m);

            _ = await diagnostico.RegistrarSiProcede(new Exception(MENSAJE_REAL), Contexto(conCorreccion: false));

            StringAssert.Contains(registrado, "sin corrección de serie en esta llamada");
            StringAssert.Contains(registrado, "Corrección de serie (gate Verifactu #39): no");
            StringAssert.Contains(registrado, "(vacío:");
        }

        [TestMethod]
        public async Task RegistrarSiProcede_SiLaLecturaFalla_RegistraIgualConElMotivo()
        {
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).ThrowsAsync(new InvalidOperationException("sin conexión"));
            string registrado = null;
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).Invokes((string m, Exception e) => registrado = m);

            bool resultado = await diagnostico.RegistrarSiProcede(new Exception(MENSAJE_REAL), Contexto());

            Assert.IsTrue(resultado);
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(registrado, "La captura falló: sin conexión");
        }

        [TestMethod]
        public async Task RegistrarSiProcede_SiElLogFalla_NoLanza()
        {
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).Returns(Task.FromResult(EstadoConFilaAjena()));
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).Throws(new Exception("ELMAH caído"));

            bool resultado = await diagnostico.RegistrarSiProcede(new Exception(MENSAJE_REAL), Contexto());

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public async Task ServicioFacturas_RegistrarDiagnostico_DelegaYNuncaLanza()
        {
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).ThrowsAsync(new Exception("BD caída"));
            A.CallTo(() => log.LogError(A<string>._, A<Exception>._)).Throws(new Exception("ELMAH caído"));
            ServicioFacturas servicio = new ServicioFacturas(A.Fake<NVEntities>()) { DiagnosticoGastoSinCentroCoste = diagnostico };

            await servicio.RegistrarDiagnosticoGastoSinCentroCoste(new Exception(MENSAJE_REAL), Contexto());
            await servicio.RegistrarDiagnosticoGastoSinCentroCoste(new Exception("otro error"), Contexto());

            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void IncumpleRegla_ReplicaElChequeoDePrdContabilizar()
        {
            Assert.IsTrue(DiagnosticoGastoSinCentroCoste.IncumpleRegla(
                new PreContabilidad { TipoCuenta = "1  ", Nº_Cuenta = "62400000", Delegación = "ALG", CentroCoste = null, Departamento = "COM" }));
            Assert.IsTrue(DiagnosticoGastoSinCentroCoste.IncumpleRegla(
                new PreContabilidad { TipoCuenta = "1  ", Nº_Cuenta = "43000000", Contrapartida = "62900000", Delegación = null, CentroCoste = "LV", Departamento = "ADM" }));
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.IncumpleRegla(
                new PreContabilidad { TipoCuenta = "1  ", Nº_Cuenta = "62400000", Delegación = "ALG", CentroCoste = "MPP", Departamento = "COM" }));
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.IncumpleRegla(
                new PreContabilidad { TipoCuenta = "1  ", Nº_Cuenta = "47700021", Delegación = null, CentroCoste = null, Departamento = null }));
            Assert.IsFalse(DiagnosticoGastoSinCentroCoste.IncumpleRegla(
                new PreContabilidad { TipoCuenta = "2  ", Nº_Cuenta = "62400000", Delegación = null, CentroCoste = null, Departamento = null }));
        }
    }
}
