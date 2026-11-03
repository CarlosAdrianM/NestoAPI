using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#782: el código de barras que llega por la sincronización de productos (Odoo/externos) solo se graba si es
    /// válido (ValidadorGtin y que no sea la propia referencia); si no, se deja el que había y queda la traza.
    /// </summary>
    [TestClass]
    public class ProductosSyncHandlerCodigoBarrasTests
    {
        private const string EAN_VALIDO = "4006381333931";
        private const string OTRO_EAN_VALIDO = "2900000174047";

        private Action<Exception> _traza;
        private ProductosSyncHandler _handler;

        [TestInitialize]
        public void Setup()
        {
            _traza = A.Fake<Action<Exception>>();
            _handler = new ProductosSyncHandler(_traza);
        }

        private static Producto Ficha(string numero = "17404", string codBarras = OTRO_EAN_VALIDO)
        {
            return new Producto { Empresa = "1  ", Número = numero.PadRight(15), CodBarras = codBarras, Nombre = "Producto" };
        }

        private static ProductoSyncMessage Mensaje(string codigoBarras, string producto = "17404")
        {
            return new ProductoSyncMessage { Producto = producto, CodigoBarras = codigoBarras, Source = "Odoo", Usuario = "odoo" };
        }

        [TestMethod]
        public void ActualizarProducto_CodigoValido_SeGraba()
        {
            Producto ficha = Ficha();

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje(" " + EAN_VALIDO + " "));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje(EAN_VALIDO));

            Assert.AreEqual(EAN_VALIDO, ficha.CodBarras);
            A.CallTo(() => _traza.Invoke(A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ActualizarProducto_CodigoUno_NoSeGrabaYDejaTraza()
        {
            Producto ficha = Ficha();

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje("1"));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje("1"));

            Assert.AreEqual(OTRO_EAN_VALIDO, ficha.CodBarras, "Se deja el que había");
            A.CallTo(() => _traza.Invoke(A<CodigoBarrasSincronizacionRechazadoException>.That.Matches(e =>
                    e.Producto == "17404" && e.CodigoRechazado == "1" && e.Origen == "Odoo"
                    && e.Message.Contains("17404") && e.Message.Contains("'1'"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ActualizarProducto_CodigoNoValidoYElQueHabiaTampoco_NoSeToca()
        {
            Producto ficha = Ficha(codBarras: "0");

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje("1"));

            Assert.AreEqual("0", ficha.CodBarras);
        }

        [TestMethod]
        public void ActualizarProducto_DigitoControlMal_NoSeGrabaYDejaTraza()
        {
            Producto ficha = Ficha();

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje("4006381333932"));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje("4006381333932"));

            Assert.AreEqual(OTRO_EAN_VALIDO, ficha.CodBarras);
            A.CallTo(() => _traza.Invoke(A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ActualizarProducto_CodigoIgualALaReferencia_NoSeGrabaYDejaTraza()
        {
            Producto ficha = Ficha();

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje("17404"));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje("17404"));

            Assert.AreEqual(OTRO_EAN_VALIDO, ficha.CodBarras);
            A.CallTo(() => _traza.Invoke(A<Exception>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ActualizarProducto_ReferenciaConFormaDeEanValido_NoSeGraba()
        {
            // Una referencia de 13 cifras que además pasa el dígito de control: sigue sin ser su código de barras
            Producto ficha = Ficha(numero: EAN_VALIDO);

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje(EAN_VALIDO, EAN_VALIDO));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje(EAN_VALIDO, EAN_VALIDO));

            Assert.AreEqual(OTRO_EAN_VALIDO, ficha.CodBarras);
            A.CallTo(() => _traza.Invoke(A<CodigoBarrasSincronizacionRechazadoException>.That.Matches(e =>
                    e.Motivo == ProductoChangeDetector.MOTIVO_ES_LA_REFERENCIA)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Trazar_SinCodigoEnElMensaje_NoDejaTraza()
        {
            Producto ficha = Ficha();

            _handler.ActualizarProductoDesdeExterno(ficha, Mensaje(null));
            _handler.TrazarSiCodigoBarrasRechazado(ficha, Mensaje("  "));

            Assert.AreEqual(OTRO_EAN_VALIDO, ficha.CodBarras);
            A.CallTo(() => _traza.Invoke(A<Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Trazar_SiLaTrazaFalla_NoLanza()
        {
            A.CallTo(() => _traza.Invoke(A<Exception>._)).Throws(new InvalidOperationException("ELMAH caído"));

            _handler.TrazarSiCodigoBarrasRechazado(Ficha(), Mensaje("1"));
        }

        [TestMethod]
        public void Detector_CodigoNoValido_NoDetectaCambio()
        {
            var detector = new ProductoChangeDetector();

            Assert.IsFalse(detector.DetectarCambios(Ficha(), Mensaje("1")).Any());
            Assert.IsFalse(detector.DetectarCambios(Ficha(), Mensaje("4006381333932")).Any());
            Assert.IsFalse(detector.DetectarCambios(Ficha(), Mensaje("17404")).Any());
            Assert.IsFalse(detector.DetectarCambios(Ficha(codBarras: "0"), Mensaje("1")).Any());
        }

        [TestMethod]
        public void Detector_CodigoValidoDistinto_DetectaCambio()
        {
            var cambios = new ProductoChangeDetector().DetectarCambios(Ficha(), Mensaje(EAN_VALIDO));

            Assert.AreEqual(1, cambios.Count);
            StringAssert.Contains(cambios[0], EAN_VALIDO);
        }

        [TestMethod]
        public void Detector_CodigoValidoIgualConEspacios_NoDetectaCambio()
        {
            Assert.IsFalse(new ProductoChangeDetector().DetectarCambios(Ficha(), Mensaje(" " + OTRO_EAN_VALIDO + " ")).Any());
        }
    }
}
