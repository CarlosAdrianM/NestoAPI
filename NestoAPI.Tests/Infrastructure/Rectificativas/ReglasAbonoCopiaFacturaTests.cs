using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rectificativas;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Rectificativas
{
    /// <summary>
    /// 16/11/26: caso real del 05/11/26. La NV2617228 era de Marcela (13465). Con «Copiar factura» invirtiendo cantidades se
    /// abonó a NATURA STYLE (30976), que no la había comprado (RV2600152), y luego otra vez entera a Marcela (RV2600153).
    /// </summary>
    [TestClass]
    public class ReglasAbonoCopiaFacturaTests
    {
        private static List<ReglasAbonoCopiaFactura.LineaVendida> NV2617228() => new List<ReglasAbonoCopiaFactura.LineaVendida>
        {
            new ReglasAbonoCopiaFactura.LineaVendida { Factura = "NV2617228", Linea = 328865100, Producto = "44724", Cantidad = 6 },
            new ReglasAbonoCopiaFactura.LineaVendida { Factura = "NV2617228", Linea = 328865300, Producto = "44725", Cantidad = 5 }
        };

        [TestMethod]
        public void AbonarAOtroClienteLaFacturaDeMarcela_NoSePuede()
        {
            string motivo = ReglasAbonoCopiaFactura.MotivoClienteDistinto(new[] { ("NV2617228", "13465") }, "30976");

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "La factura NV2617228 es del cliente 13465");
            StringAssert.Contains(motivo, "no se puede abonar al cliente 30976");
            StringAssert.Contains(motivo, "Crear abono y cargo");
        }

        [TestMethod]
        public void AbonarAlMismoCliente_SePuede_AunqueVengaConEspacios()
        {
            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoClienteDistinto(new[] { ("NV2617228", "13465     ") }, " 13465"));
        }

        [TestMethod]
        public void VariasFacturas_SiUnaEsDeOtroCliente_NoSePuede()
        {
            string motivo = ReglasAbonoCopiaFactura.MotivoClienteDistinto(
                new[] { ("NV2600001", "13465"), ("NV2600002", "30976") }, "13465");

            StringAssert.Contains(motivo, "NV2600002");
        }

        [TestMethod]
        public void SinAbonosAnteriores_SePuedeAbonarEntera()
        {
            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoYaAbonado(NV2617228(), new List<ReglasAbonoCopiaFactura.AbonoAnterior>()));
        }

        [TestMethod]
        public void YaAbonadaEnOtraRectificativa_NoSeAbonaOtraVez_YDiceDonde()
        {
            var anteriores = new List<ReglasAbonoCopiaFactura.AbonoAnterior>
            {
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV2617228 ", LineaOriginal = 328865100, Cantidad = 6, Documento = "RV2600152" },
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV2617228", LineaOriginal = 328865300, Cantidad = 5, Documento = "RV2600152" }
            };

            string motivo = ReglasAbonoCopiaFactura.MotivoYaAbonado(NV2617228(), anteriores);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "La factura NV2617228 ya está abonada en RV2600152");
            StringAssert.Contains(motivo, "44724 (6 de 6 ya abonadas)");
            StringAssert.Contains(motivo, "No se puede abonar dos veces");
        }

        [TestMethod]
        public void DevolucionParcialAnterior_TampocoDejaAbonarLaLineaEntera()
        {
            var anteriores = new List<ReglasAbonoCopiaFactura.AbonoAnterior>
            {
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV2617228", LineaOriginal = 328865100, Cantidad = 1, Documento = "el pedido 930500 (sin facturar)" }
            };

            string motivo = ReglasAbonoCopiaFactura.MotivoYaAbonado(NV2617228(), anteriores);

            StringAssert.Contains(motivo, "44724 (1 de 6 ya abonadas)");
            StringAssert.Contains(motivo, "el pedido 930500 (sin facturar)");
            Assert.IsFalse(motivo.Contains("44725"), motivo);
        }

        [TestMethod]
        public void AbonosDeOtraFacturaOOtraLinea_NoCuentan()
        {
            var anteriores = new List<ReglasAbonoCopiaFactura.AbonoAnterior>
            {
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV2600999", LineaOriginal = 328865100, Cantidad = 6, Documento = "RV2600001" },
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV2617228", LineaOriginal = 1, Cantidad = 6, Documento = "RV2600002" }
            };

            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoYaAbonado(NV2617228(), anteriores));
        }

        [TestMethod]
        public void LineasNegativasOACero_NoSeMiran()
        {
            // Deshacer un abono: las líneas de la RV son negativas y no hay nada vendido que contar
            var lineas = new List<ReglasAbonoCopiaFactura.LineaVendida>
            {
                new ReglasAbonoCopiaFactura.LineaVendida { Factura = "RV2600152", Linea = 1, Producto = "44724", Cantidad = -6 },
                new ReglasAbonoCopiaFactura.LineaVendida { Factura = "RV2600152", Linea = 2, Producto = "TEXTO", Cantidad = 0 }
            };
            var anteriores = new List<ReglasAbonoCopiaFactura.AbonoAnterior>
            {
                new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "RV2600152", LineaOriginal = 1, Cantidad = 6, Documento = "RV2600154" }
            };

            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoYaAbonado(lineas, anteriores));
        }
    }
}
