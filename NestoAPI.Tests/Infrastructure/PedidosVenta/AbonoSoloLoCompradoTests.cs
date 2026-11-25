using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>NestoAPI#891 (Carlos 24/11/26, RV2600189): no abonar lo que el cliente no ha comprado. La regla y su sombra.</summary>
    [TestClass]
    public class AbonoSoloLoCompradoTests
    {
        private const string CUPON = "CUPONEVENTO";
        private const string CHEQUE = "CHEQUE50_OCT26";

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, short estado = Constantes.EstadosLineaVenta.EN_CURSO,
            byte tipo = Constantes.TiposLineaVenta.PRODUCTO)
            => new LineaPedidoVentaDTO { Producto = producto, Cantidad = cantidad, estado = estado, tipoLinea = tipo };

        private static bool EsDescuento(string producto) => producto == CUPON || producto == CHEQUE;

        private static List<FaltaAbonoProducto> Evaluar(string producto, int abona, int facturadas, int abonadas, int sinFacturar = 0)
            => ReglaAbonoSoloLoComprado.Evaluar(new[] { new LineaAbonoProducto { Producto = producto, Unidades = abona } },
                new[] { new CompradoParaAbono { Producto = producto, Facturadas = facturadas, Abonadas = abonadas, AbonadasSinFacturar = sinFacturar } });

        [TestMethod]
        public void Caso41752_FacturadasTresYAbonadasTres_NoSePuedeAbonarOtra()
        {
            List<FaltaAbonoProducto> faltas = Evaluar("41752", 1, 3, 3);

            Assert.AreEqual(1, faltas.Count);
            Assert.AreEqual("Del 41752 este cliente tiene facturadas 3 y ya abonadas 3: no se puede abonar 1 unidad.", faltas[0].Texto);
            Assert.AreEqual("No se puede abonar lo que el cliente no ha comprado. " + faltas[0].Texto, ReglaAbonoSoloLoComprado.Mensaje(faltas));
        }

        [TestMethod]
        public void LoQueCabeJusto_SePuede()
        {
            Assert.AreEqual(0, Evaluar("41752", 2, 3, 1).Count);
        }

        [TestMethod]
        public void NuncaComprado_NoSePuede()
        {
            List<FaltaAbonoProducto> faltas = ReglaAbonoSoloLoComprado.Evaluar(new[] { new LineaAbonoProducto { Producto = "41752", Unidades = 2 } },
                new List<CompradoParaAbono>());

            Assert.AreEqual("Del 41752 este cliente no tiene ninguna facturada: no se puede abonar 2 unidades.", faltas.Single().Texto);
        }

        [TestMethod]
        public void EnParte_DiceCuantasCaben()
        {
            Assert.AreEqual("Del 41752 este cliente tiene facturadas 5 y ya abonadas 3: solo se pueden abonar 2 de 4.", Evaluar("41752", 4, 5, 3).Single().Texto);
        }

        [TestMethod]
        public void AbonosEnOtrosPedidosSinFacturar_CuentanComoYaAbonados()
        {
            List<FaltaAbonoProducto> faltas = Evaluar("41752", 1, 3, 1, sinFacturar: 2);

            Assert.AreEqual("Del 41752 este cliente tiene facturadas 3 y ya abonadas 1 (2 más en otros pedidos sin facturar): no se puede abonar 1 unidad.",
                faltas.Single().Texto);
        }

        [TestMethod]
        public void LineasAbono_SoloProductosNegativosSinFacturarNiPresupuesto_SumadosYSinCuponNiCheque()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                Linea("41752", -1),
                Linea("41752 ", -2, Constantes.EstadosLineaVenta.PENDIENTE),
                Linea("CREMA", 3),
                Linea(CUPON, -1),
                Linea(CHEQUE, -1),
                Linea("62400000", -1, tipo: Constantes.TiposLineaVenta.CUENTA_CONTABLE),
                Linea("SERUM", -1, Constantes.EstadosLineaVenta.FACTURA),
                Linea("TONICO", -1, Constantes.EstadosLineaVenta.PRESUPUESTO),
                Linea("MASCARILLA", -1, Constantes.EstadosLineaVenta.ALBARAN)
            };

            List<LineaAbonoProducto> abonos = ReglaAbonoSoloLoComprado.LineasAbono(lineas, EsDescuento);

            CollectionAssert.AreEquivalent(new[] { "41752", "MASCARILLA" }, abonos.Select(a => a.Producto).ToList());
            Assert.AreEqual(3, abonos.Single(a => a.Producto == "41752").Unidades);
        }

        [TestMethod]
        public void Sombra_PedidoSinAbonos_NoLeeNiApuntaNada()
        {
            var lector = A.Fake<ILectorCompradoParaAbono>();
            int apuntes = 0;
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 930900, cliente = "15191", Lineas = new List<LineaPedidoVentaDTO> { Linea("CREMA", 2), Linea(CUPON, -1) } };

            List<FaltaAbonoProducto> faltas = SombraAbonoSoloLoComprado.Apuntar(pedido, EsDescuento, lector, e => apuntes++);

            Assert.AreEqual(0, faltas.Count);
            Assert.AreEqual(0, apuntes);
            A.CallTo(() => lector.Leer(A<IEnumerable<string>>._, A<string>._, A<int>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Sombra_AbonoDeLoNoComprado_ApuntaUnaVezEnEmpresasUnoYTresSinTocarElPedido()
        {
            var lector = A.Fake<ILectorCompradoParaAbono>();
            A.CallTo(() => lector.Leer(A<IEnumerable<string>>._, "15191", 930900, A<IEnumerable<string>>._))
                .Returns(new List<CompradoParaAbono> { new CompradoParaAbono { Producto = "41752", Facturadas = 3, Abonadas = 3 } });
            var apuntes = new List<Exception>();
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 930900, cliente = "15191", contacto = "0", Usuario = "NUEVAVISION\\Carlos",
                Lineas = new List<LineaPedidoVentaDTO> { Linea("41752", -1) } };

            List<FaltaAbonoProducto> faltas = SombraAbonoSoloLoComprado.Apuntar(pedido, EsDescuento, lector, apuntes.Add);

            Assert.AreEqual(1, faltas.Count);
            Assert.AreEqual(1, apuntes.Count);
            StringAssert.Contains(apuntes[0].Message, "#891, sombra");
            StringAssert.Contains(apuntes[0].Message, "Del 41752 este cliente tiene facturadas 3 y ya abonadas 3");
            A.CallTo(() => lector.Leer(A<IEnumerable<string>>.That.Matches(e => e.SequenceEqual(new[] { "1", "3" })), "15191", 930900,
                A<IEnumerable<string>>.That.Matches(p => p.SequenceEqual(new[] { "41752" })))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(-1, pedido.Lineas.Single().Cantidad, "La sombra no cambia nada del pedido");
        }

        [TestMethod]
        public void Sombra_TodoComprado_NoApunta()
        {
            var lector = A.Fake<ILectorCompradoParaAbono>();
            A.CallTo(() => lector.Leer(A<IEnumerable<string>>._, A<string>._, A<int>._, A<IEnumerable<string>>._))
                .Returns(new List<CompradoParaAbono> { new CompradoParaAbono { Producto = "41752", Facturadas = 3 } });
            int apuntes = 0;
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 930900, cliente = "15191", Lineas = new List<LineaPedidoVentaDTO> { Linea("41752", -1) } };

            Assert.AreEqual(0, SombraAbonoSoloLoComprado.Apuntar(pedido, EsDescuento, lector, e => apuntes++).Count);
            Assert.AreEqual(0, apuntes);
        }

        [TestMethod]
        public void Sombra_SiFallaLaLectura_NoLanzaYLoApunta()
        {
            var lector = A.Fake<ILectorCompradoParaAbono>();
            A.CallTo(() => lector.Leer(A<IEnumerable<string>>._, A<string>._, A<int>._, A<IEnumerable<string>>._)).Throws(new InvalidOperationException("sin BD"));
            var apuntes = new List<Exception>();
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 930900, cliente = "15191", Lineas = new List<LineaPedidoVentaDTO> { Linea("41752", -1) } };

            List<FaltaAbonoProducto> faltas = SombraAbonoSoloLoComprado.Apuntar(pedido, EsDescuento, lector, apuntes.Add);

            Assert.AreEqual(0, faltas.Count);
            Assert.AreEqual(1, apuntes.Count);
            StringAssert.Contains(apuntes[0].Message, "No se ha podido comprobar");
        }

        [TestMethod]
        public void Sql_LeeSoloDeFacturaYLosAbonosDeOtrosPedidos()
        {
            StringAssert.Contains(LectorCompradoParaAbonoSql.SQL, "l.Estado = 4 AND l.Cantidad > 0");
            StringAssert.Contains(LectorCompradoParaAbonoSql.SQL, "l.Número <> @pedido");
            Assert.IsFalse(LectorCompradoParaAbonoSql.SQL.Contains("Fecha"), "Sin límite de tiempo hacia atrás");
        }
    }
}
