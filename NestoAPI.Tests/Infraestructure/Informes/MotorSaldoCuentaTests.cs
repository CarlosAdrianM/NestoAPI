using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes.SaldoCuenta555;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    [TestClass]
    public class MotorSaldoCuentaTests
    {
        private static readonly DateTime FechaCorte = new DateTime(2026, 3, 31);

        private static ApunteCuentaDto Apunte(
            long numeroOrden,
            DateTime fecha,
            string concepto,
            decimal debe = 0,
            decimal haber = 0,
            string numeroDocumento = null)
        {
            return new ApunteCuentaDto
            {
                NumeroOrden = numeroOrden,
                Fecha = fecha,
                Concepto = concepto,
                Debe = debe,
                Haber = haber,
                NumeroDocumento = numeroDocumento,
                Diario = "_test",
                TipoApunte = 3
            };
        }

        [TestMethod]
        public void PrepagoYLiqPagoMismoOrderId_SaldanAcero_CerradoNoAparece()
        {
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 20), "Prepago Amazon.es FBA 407-3383405-7214713", debe: 27.90M),
                Apunte(2, new DateTime(2026, 3, 26), "Liq. Pago Pedido 407-3383405-7214713", haber: 27.90M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count);
        }

        [TestMethod]
        public void PrepagoSinLiqPago_QuedaAbiertoPorFifo()
        {
            // Solo DEBE en un OrderId → Pasada 1 lo libera. FIFO no tiene HABER que consumir.
            // Queda abierto como Fifo con saldo = 27.90.
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 20), "Prepago Amazon.es FBA 407-3383405-7214713", debe: 27.90M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(27.90M, res.SaldoTotal);
            Assert.AreEqual(1, res.GruposAbiertos.Count);
            Assert.AreEqual(TipoClaveGrupo.Fifo, res.GruposAbiertos[0].TipoClave);
            Assert.AreEqual(27.90M, res.GruposAbiertos[0].Saldo);
            Assert.AreEqual(11, res.GruposAbiertos[0].DiasAntiguedad);
        }

        [TestMethod]
        public void PrepagoLiqPagoYReembolsoMismoOrderIdSaldanAcero_Cerrado()
        {
            // OrderId con DEBE 30 + HABER 20 + HABER 10 = 0 → cerrado por Pasada 1.
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 1), "Prepago Amazon.es FBA 405-0887571-2817165", debe: 30M),
                Apunte(2, new DateTime(2026, 3, 26), "Liq. Pago Pedido 405-0887571-2817165", haber: 20M),
                Apunte(3, new DateTime(2026, 3, 31), "Reembolso Amazon.es FBA 405-0887571-2817165", haber: 10M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count);
        }

        [TestMethod]
        public void DosApuntesManuales_SinOrderIdSinDoc_FifoCancela()
        {
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 10), "Pago Amazon manual", debe: 50M),
                Apunte(2, new DateTime(2026, 3, 15), "Compensación pago Amazon", haber: 50M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count, "FIFO debe cancelar ambos apuntes");
        }

        [TestMethod]
        public void DebeHuerfano_SinMatch_QuedaAbiertoPorFifo()
        {
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 10), "Ajuste manual sin referencia", debe: 100M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(100M, res.SaldoTotal);
            Assert.AreEqual(1, res.GruposAbiertos.Count);
            Assert.AreEqual(TipoClaveGrupo.Fifo, res.GruposAbiertos[0].TipoClave);
            Assert.AreEqual(100M, res.GruposAbiertos[0].Saldo);
        }

        [TestMethod]
        public void InvarianteSumaSaldosGrupos_IgualSaldoTotal()
        {
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 1), "Prepago Amazon.es FBA 111-1111111-1111111", debe: 10M),
                Apunte(2, new DateTime(2026, 3, 2), "Prepago Amazon.es FBA 222-2222222-2222222", debe: 20M),
                Apunte(3, new DateTime(2026, 3, 3), "Liq. Pago Pedido 222-2222222-2222222", haber: 20M),
                Apunte(4, new DateTime(2026, 3, 4), "Ajuste sin referencia", debe: 5M),
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(15M, res.SaldoTotal, "10 abierto + 5 huérfano");
            Assert.AreEqual(
                res.SaldoTotal,
                res.GruposAbiertos.Sum(g => g.Saldo),
                "Invariante: suma saldos grupos = saldoTotal");
        }

        [TestMethod]
        public void Antiguedad_SeCalculaDesdePrimerApunte()
        {
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 2, 15), "Prepago Amazon.es FBA 333-3333333-3333333", debe: 50M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(1, res.GruposAbiertos.Count);
            Assert.AreEqual(44, res.GruposAbiertos[0].DiasAntiguedad);
            Assert.AreEqual(new DateTime(2026, 2, 15), res.GruposAbiertos[0].FechaPrimerApunte);
        }

        [TestMethod]
        public void CuentaComisiones_DebesPorOrderIdYFacturaHaberGorda_FifoCancela()
        {
            // Escenario 55500062: muchas comisiones al DEBE con OrderId (sin HABER por OrderId),
            // más una factura N/Pago al HABER sin OrderId por el total.
            // Pasada 1 libera todos los DEBE (solo tienen DEBE por OrderId).
            // Pasada 2 no cuadra (NºDoc distintos entre comisiones y factura).
            // Pasada 3 FIFO los cancela.
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 26), "Liq. Comisiones 407-6729914-0493959", debe: 7.86M, numeroDocumento: "AMZ260326"),
                Apunte(2, new DateTime(2026, 3, 26), "Liq. Comisiones 405-4661448-1519555", debe: 4.47M, numeroDocumento: "AMZ260326"),
                Apunte(3, new DateTime(2026, 3, 26), "Liq. Comisiones 404-5713995-0469927", debe: 4.47M, numeroDocumento: "AMZ260326"),
                Apunte(4, new DateTime(2026, 3, 31), "N/Pago S/Fra.ES-AEU-2026-196640 - 87045", haber: 16.80M, numeroDocumento: "87045")
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500062", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count, "FIFO debe cancelar los 3 DEBE contra el HABER factura");
        }

        [TestMethod]
        public void DebeNegativo_ActuaComoHaber()
        {
            // Amazon devuelve comisión por un refund → se contabiliza Debe=-16.96
            // Debe comportarse como HABER 16.96 y cancelar un DEBE de 16.96.
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 10), "Liq. Comisiones 111-1111111-1111111", debe: 16.96M),
                Apunte(2, new DateTime(2026, 3, 20), "Liq. Comisiones 222-2222222-2222222 (refund)", debe: -16.96M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500062", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count, "FIFO empareja DEBE 16.96 con ImporteNeto -16.96");
        }

        [TestMethod]
        public void OrderIdConDebeYHaberNoSaldan_QuedaAbiertoPorOrderId()
        {
            // En la cuenta de Pago, un OrderId con DEBE 27.90 y HABER 20.00 queda abierto
            // con saldo 7.90 → TipoClave = AmazonOrderId (información precisa).
            var apuntes = new[]
            {
                Apunte(1, new DateTime(2026, 3, 10), "Prepago Amazon.es FBA 444-4444444-4444444", debe: 27.90M),
                Apunte(2, new DateTime(2026, 3, 26), "Liq. Pago Pedido 444-4444444-4444444", haber: 20.00M)
            };

            var res = MotorSaldoCuenta.Calcular(apuntes, "1", "55500047", FechaCorte);

            Assert.AreEqual(7.90M, res.SaldoTotal);
            Assert.AreEqual(1, res.GruposAbiertos.Count);
            Assert.AreEqual(TipoClaveGrupo.AmazonOrderId, res.GruposAbiertos[0].TipoClave);
            Assert.AreEqual("444-4444444-4444444", res.GruposAbiertos[0].Clave);
            Assert.AreEqual(7.90M, res.GruposAbiertos[0].Saldo);
            Assert.AreEqual(2, res.GruposAbiertos[0].Apuntes.Count);
        }

        [TestMethod]
        public void SinApuntes_ResultadoVacio()
        {
            var res = MotorSaldoCuenta.Calcular(
                Enumerable.Empty<ApunteCuentaDto>(), "1", "55500047", FechaCorte);

            Assert.AreEqual(0M, res.SaldoTotal);
            Assert.AreEqual(0, res.GruposAbiertos.Count);
        }
    }
}
