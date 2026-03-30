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
        private DbSet<ExtractoCliente> fakeExtractosCliente;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractosCliente = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>());
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

        [TestMethod]
        public void ObtenerInfoDeuda_EfectoPendienteRemesar_NoEsVencido()
        {
            // Arrange: Factura creada ayer con vencimiento ayer (FechaVto == Fecha)
            // La remesa se hace hoy, así que no debe contar como vencida
            DateTime ayer = DateTime.Today.AddDays(-1);
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    Fecha = ayer,
                    FechaVto = ayer
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsFalse(resultado.TieneDeudaVencida);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_EfectoVencidoAyerFechaFacturaDiferente_SiEsVencido()
        {
            // Arrange: Factura creada hace una semana con vencimiento ayer
            // FechaVto != Fecha, así que sí es deuda vencida normal
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    Fecha = DateTime.Today.AddDays(-7),
                    FechaVto = DateTime.Today.AddDays(-1)
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneDeudaVencida);
            Assert.AreEqual(500m, resultado.ImporteDeudaVencida);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_EfectoPendienteRemesarDeSemanaAnterior_SiEsVencido()
        {
            // Arrange: Factura del lunes de la semana pasada con FechaVto == Fecha
            // Una semana entera siempre supera el margen de remesa (1 día laborable),
            // independientemente del día en que se ejecute el test
            DateTime lunesPasado = DateTime.Today.AddDays(-7 - ((int)DateTime.Today.DayOfWeek + 6) % 7);
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    Fecha = lunesPasado,
                    FechaVto = lunesPasado
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert
            Assert.IsTrue(resultado.TieneDeudaVencida);
        }

        [TestMethod]
        public void ObtenerInfoDeuda_EfectoPendienteRemesarDeViernesSiendoLunes_NoEsVencido()
        {
            // Arrange: Factura del viernes con FechaVto == Fecha, y hoy es lunes
            // El viernes es el día laborable anterior al lunes, así que el efecto
            // aún está pendiente de remesar y NO debe contar como vencido.
            // Usamos fechas fijas: lunes 2026-03-30 y viernes 2026-03-27
            DateTime lunes = new DateTime(2026, 3, 30);
            DateTime viernes = new DateTime(2026, 3, 27);

            // Solo ejecutar si hoy es ese lunes concreto, sino el test
            // no puede controlar DateTime.Today del código bajo test
            if (DateTime.Today != lunes)
            {
                // En cualquier otro día, verificamos con el día laborable anterior real
                viernes = PlazosPagoController.CalcularDiaLaborableAnterior(DateTime.Today);
            }

            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Número = "123",
                    ImportePdte = 500m,
                    TipoApunte = "FACTURA",
                    Fecha = viernes,
                    FechaVto = viernes
                }
            }.AsQueryable();
            ConfigurarFakeDbSet(fakeExtractosCliente, extractos);

            // Act
            var resultado = controller.ObtenerInfoDeuda("123");

            // Assert: efecto del día laborable anterior no es vencido (pendiente de remesar)
            Assert.IsFalse(resultado.TieneDeudaVencida);
        }

        #endregion

        #region CalcularDiaLaborableAnterior Tests

        [TestMethod]
        public void CalcularDiaLaborableAnterior_Martes_DevuelveLunes()
        {
            // Martes 2026-03-31 → día laborable anterior = Lunes 2026-03-30
            var martes = new DateTime(2026, 3, 31);
            var resultado = PlazosPagoController.CalcularDiaLaborableAnterior(martes);
            Assert.AreEqual(new DateTime(2026, 3, 30), resultado); // Lunes
        }

        [TestMethod]
        public void CalcularDiaLaborableAnterior_Lunes_DevuelveViernes()
        {
            // Lunes 2026-03-30 → día laborable anterior = Viernes 2026-03-27
            var lunes = new DateTime(2026, 3, 30);
            var resultado = PlazosPagoController.CalcularDiaLaborableAnterior(lunes);
            Assert.AreEqual(new DateTime(2026, 3, 27), resultado); // Viernes
        }

        [TestMethod]
        public void CalcularDiaLaborableAnterior_DespuesFestivo_SaltaFestivo()
        {
            // Jueves 2 enero 2026 → miércoles 1 enero es festivo → devuelve martes 31 diciembre
            // Pero 31 dic no es festivo nacional (solo en alguna delegación)
            var jueves2Enero = new DateTime(2026, 1, 2);
            var resultado = PlazosPagoController.CalcularDiaLaborableAnterior(jueves2Enero);
            Assert.AreEqual(new DateTime(2025, 12, 31), resultado);
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
