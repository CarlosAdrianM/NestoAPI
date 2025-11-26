using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class PlazosPagoControllerTests
    {
        private NVEntities db;
        private PlazosPagoController controller;
        private IDbSet<ExtractoCliente> fakeExtractosCliente;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractosCliente = A.Fake<IDbSet<ExtractoCliente>>();
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractosCliente);

            controller = new PlazosPagoController(db);
        }

        #region ObtenerInfoDeuda Tests

        [TestMethod]
        public void ObtenerInfoDeuda_ClienteSinDeuda_RetornaInfoVacia()
        {
            // Arrange
            var extractos = new List<ExtractoCliente>().AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsFalse(resultado.TieneImpagados);
            Assert.IsFalse(resultado.TieneDeudaVencida);
            Assert.IsNull(resultado.ImporteImpagados);
            Assert.IsNull(resultado.ImporteDeudaVencida);
            Assert.IsNull(resultado.DiasVencimiento);
            Assert.IsNull(resultado.MotivoRestriccion);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_SoloImpagados_NoIncluyeEnCarteraVencida()
        {
            // Arrange: Cliente con UN impagado de 1000€ con fecha vencida
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 1000m,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO,
                    FechaVto = DateTime.Today.AddDays(-10) // Vencido hace 10 días
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneImpagados);
            Assert.AreEqual(1000m, resultado.ImporteImpagados);

            // NO debe contar en cartera vencida (evitar doble contabilización)
            Assert.IsFalse(resultado.TieneDeudaVencida);
            Assert.IsNull(resultado.ImporteDeudaVencida);

            Assert.AreEqual("Impagados", resultado.MotivoRestriccion);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_SoloCarteraVencida_NoIncluyeImpagados()
        {
            // Arrange: Cliente con factura vencida de 500€ (NO impagado)
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA", // NO es impagado
                    FechaVto = DateTime.Today.AddDays(-5) // Vencido hace 5 días
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsFalse(resultado.TieneImpagados);
            Assert.IsNull(resultado.ImporteImpagados);

            Assert.IsTrue(resultado.TieneDeudaVencida);
            Assert.AreEqual(500m, resultado.ImporteDeudaVencida);
            Assert.AreEqual(5, resultado.DiasVencimiento);

            Assert.AreEqual("Cartera vencida", resultado.MotivoRestriccion);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_ImpagadosYCarteraVencida_SumaPorSeparado()
        {
            // Arrange: Cliente con impagado de 1000€ + cartera vencida de 500€
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 1000m,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO,
                    FechaVto = DateTime.Today.AddDays(-10)
                },
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA", // NO impagado
                    FechaVto = DateTime.Today.AddDays(-3)
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneImpagados);
            Assert.AreEqual(1000m, resultado.ImporteImpagados);

            Assert.IsTrue(resultado.TieneDeudaVencida);
            Assert.AreEqual(500m, resultado.ImporteDeudaVencida);
            Assert.AreEqual(3, resultado.DiasVencimiento); // Más antigua de las NO impagadas

            Assert.AreEqual("Impagados y cartera vencida", resultado.MotivoRestriccion);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_VencimientoHoy_NoEsVencida()
        {
            // Arrange: Factura con vencimiento HOY (no debe contar como vencida)
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    FechaVto = DateTime.Today // Vence HOY
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsFalse(resultado.TieneDeudaVencida);
            Assert.IsNull(resultado.ImporteDeudaVencida);
            Assert.IsNull(resultado.MotivoRestriccion);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_VencimientoAyer_EsVencida()
        {
            // Arrange: Factura con vencimiento AYER (debe contar como vencida)
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    FechaVto = DateTime.Today.AddDays(-1) // Venció AYER
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneDeudaVencida);
            Assert.AreEqual(500m, resultado.ImporteDeudaVencida);
            Assert.AreEqual(1, resultado.DiasVencimiento);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_VariosImpagados_SumaTotal()
        {
            // Arrange: Cliente con 3 impagados
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente { Número = "123", ImportePdte = 500m, TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO, FechaVto = DateTime.Today.AddDays(-5) },
                new ExtractoCliente { Número = "123", ImportePdte = 300m, TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO, FechaVto = DateTime.Today.AddDays(-3) },
                new ExtractoCliente { Número = "123", ImportePdte = 200m, TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO, FechaVto = DateTime.Today.AddDays(-1) }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneImpagados);
            Assert.AreEqual(1000m, resultado.ImporteImpagados); // 500 + 300 + 200
        }

        [TestMethod]
        public void ObtenerInfoDeuda_DiasVencimiento_CalculaDesdeFacturaMasAntigua()
        {
            // Arrange: Varias facturas vencidas
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente { Número = "123", ImportePdte = 100m, TipoApunte = "FACTURA", FechaVto = DateTime.Today.AddDays(-3) },
                new ExtractoCliente { Número = "123", ImportePdte = 200m, TipoApunte = "FACTURA", FechaVto = DateTime.Today.AddDays(-15) }, // Más antigua
                new ExtractoCliente { Número = "123", ImportePdte = 150m, TipoApunte = "FACTURA", FechaVto = DateTime.Today.AddDays(-7) }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.AreEqual(15, resultado.DiasVencimiento); // Desde la más antigua (15 días)
        }

        #endregion

        #region PlazoPagoRecomendado Tests

        [TestMethod]
        public void ObtenerInfoDeuda_ClienteConImpagados_DetectaImpagados()
        {
            // Arrange: Cliente con impagado
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 1000m,
                    TipoApunte = Constantes.ExtractosCliente.TiposApunte.IMPAGADO,
                    FechaVto = DateTime.Today.AddDays(-10)
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var infoDeuda = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(infoDeuda.TieneImpagados);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_ClienteConDeudaVencida_DetectaDeudaVencida()
        {
            // Arrange: Cliente con deuda vencida (no impagado)
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    FechaVto = DateTime.Today.AddDays(-5)
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var infoDeuda = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(infoDeuda.TieneDeudaVencida);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_ClienteSinDeuda_NoTieneDeuda()
        {
            // Arrange: Cliente sin deuda
            var extractos = new List<ExtractoCliente>().AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var infoDeuda = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsFalse(infoDeuda.TieneImpagados);
            Assert.IsFalse(infoDeuda.TieneDeudaVencida);
        }

        #endregion

        #region Combinaciones Condiciones Pago Tests (Pendientes de implementar)

        // TODO: Estos tests documentan las combinaciones válidas/inválidas que deben implementarse
        // Ver issue #26 en GitHub para más detalles

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_EfectivoContado_EsValida()
        {
            // Combinación VÁLIDA: EFC + CONTADO cuando hay deuda
            // El cliente paga en efectivo antes de llevarse el pedido
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_EfectivoPrepago_EsValida()
        {
            // Combinación VÁLIDA: EFC + PRE cuando hay deuda
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_TransferenciaPrepago_EsValida()
        {
            // Combinación VÁLIDA: TRN + PRE cuando hay deuda
            // El cliente paga por transferencia antes de que salga el pedido
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_TarjetaPrepago_EsValida()
        {
            // Combinación VÁLIDA: TAR + PRE cuando hay deuda
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_TransferenciaContado_NoEsValida()
        {
            // Combinación INVÁLIDA: TRN + CONTADO cuando hay deuda
            // El cliente podría no hacer la transferencia después de recibir el pedido
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_TarjetaContado_NoEsValida()
        {
            // Combinación INVÁLIDA: TAR + CONTADO cuando hay deuda
            Assert.Fail("Pendiente de implementar");
        }

        [TestMethod]
        [Ignore("Pendiente de implementar validación de combinaciones - Issue #26")]
        public void CondicionesPago_ConDeuda_ReciboBancario_NoEsValida()
        {
            // Combinación INVÁLIDA: RCB + cualquier plazo cuando hay deuda
            // El recibo puede venir devuelto
            Assert.Fail("Pendiente de implementar");
        }

        #endregion

        #region Helper Methods

        private void ConfigurarFakeDbSet<T>(IDbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => fakeDbSet.Provider).Returns(data.Provider);
            A.CallTo(() => fakeDbSet.Expression).Returns(data.Expression);
            A.CallTo(() => fakeDbSet.ElementType).Returns(data.ElementType);
            A.CallTo(() => fakeDbSet.GetEnumerator()).Returns(data.GetEnumerator());
        }

        #endregion
    }
}
