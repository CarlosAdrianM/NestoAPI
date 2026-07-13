using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// Issue #284: una línea de CLIENTE con FormaPago inexistente reventaba la FK de
    /// ExtractoCliente DENTRO de prdContabilizar (error SQL críptico + trancount descuadrado,
    /// caso ELMAH 13/07/26). La validación previa del gestor lo corta con un 400 claro
    /// SIN llegar al SP.
    /// </summary>
    [TestClass]
    public class GestorContabilidadFormasPagoTests
    {
        private IContabilidadService _servicio;
        private GestorContabilidad _gestor;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IContabilidadService>();
            _gestor = new GestorContabilidad(_servicio);
            A.CallTo(() => _servicio.LeerFormasPago(A<string>.Ignored))
                .Returns(Task.FromResult(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "EFC", "RCB", "TRN" }));
        }

        private static PreContabilidad Linea(string tipoCuenta, string formaPago, string cuenta = "43000123")
        {
            return new PreContabilidad
            {
                Empresa = "1  ",
                TipoCuenta = tipoCuenta,
                Nº_Cuenta = cuenta,
                Nº_Documento = "F123",
                FormaPago = formaPago,
                Diario = "_Caja"
            };
        }

        [TestMethod]
        public async Task CrearLineasDiarioYContabilizar_LineaClienteConFormaPagoInexistente_NoLlegaAlSP()
        {
            var lineas = new List<PreContabilidad>
            {
                Linea(Constantes.Contabilidad.TiposCuenta.CLIENTE, "XXX")
            };

            try
            {
                _ = await _gestor.CrearLineasDiarioYContabilizar(lineas);
                Assert.Fail("Debe lanzar ArgumentException por la forma de pago inexistente");
            }
            catch (ArgumentException ex)
            {
                StringAssert.Contains(ex.Message, "XXX", "El mensaje debe decir qué forma de pago es inválida");
                StringAssert.Contains(ex.Message, "43000123", "El mensaje debe identificar la línea");
            }

            A.CallTo(() => _servicio.CrearLineasYContabilizarDiario(A<List<PreContabilidad>>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearLineasDiarioYContabilizar_LineaClienteSinFormaPago_NoLlegaAlSP()
        {
            var lineas = new List<PreContabilidad>
            {
                Linea(Constantes.Contabilidad.TiposCuenta.CLIENTE, "   ")
            };

            try
            {
                _ = await _gestor.CrearLineasDiarioYContabilizar(lineas);
                Assert.Fail("Debe lanzar ArgumentException por la forma de pago vacía");
            }
            catch (ArgumentException)
            {
            }

            A.CallTo(() => _servicio.CrearLineasYContabilizarDiario(A<List<PreContabilidad>>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearLineasDiarioYContabilizar_LineaClienteConFormaPagoValida_Contabiliza()
        {
            var lineas = new List<PreContabilidad>
            {
                Linea(Constantes.Contabilidad.TiposCuenta.CLIENTE, "EFC")
            };
            A.CallTo(() => _servicio.CrearLineasYContabilizarDiario(lineas)).Returns(Task.FromResult(555));

            int asiento = await _gestor.CrearLineasDiarioYContabilizar(lineas);

            Assert.AreEqual(555, asiento);
        }

        [TestMethod]
        public async Task CrearLineasDiarioYContabilizar_LineaNoClienteSinFormaPago_Contabiliza()
        {
            // Las líneas de cuenta contable o proveedor no van a ExtractoCliente: su FormaPago
            // vacía no debe bloquear (el gasto sin forma de pago es el caso normal).
            var lineas = new List<PreContabilidad>
            {
                Linea(Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE, null, cuenta: "62400003")
            };
            A.CallTo(() => _servicio.CrearLineasYContabilizarDiario(lineas)).Returns(Task.FromResult(556));

            int asiento = await _gestor.CrearLineasDiarioYContabilizar(lineas);

            Assert.AreEqual(556, asiento);
            A.CallTo(() => _servicio.LeerFormasPago(A<string>.Ignored)).MustNotHaveHappened();
        }
    }
}
