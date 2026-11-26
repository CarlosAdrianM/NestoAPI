using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Rectificativas
{
    /// <summary>NestoAPI#899 y #863: LA REGLA de qué líneas de la factura se copian (o abonan) y cuántas unidades.</summary>
    [TestClass]
    public class SeleccionCopiaFacturaTests
    {
        private static LinPedidoVta L(int orden, int? albaran, byte tipo = 1, short cantidad = 1, int pedido = 900001, string producto = null)
            => new LinPedidoVta { Número = pedido, Nº_Orden = orden, Nº_Albarán = albaran, TipoLinea = tipo, Cantidad = cantidad, Producto = producto ?? "P" + orden, Nº_Factura = "NV1" };

        private static List<LinPedidoVta> Factura() => new List<LinPedidoVta>
        {
            L(1, 100, cantidad: 5), L(2, 101), L(3, 101, tipo: 0, cantidad: 0), L(4, 101, tipo: 2), L(5, 102, cantidad: -3)
        };

        [TestMethod]
        public void SinSeleccion_TodasEnteras()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", null, null);

            Assert.IsNull(r.Error);
            Assert.AreEqual(5, r.Lineas.Count);
            Assert.IsNull(r.Descripcion);
            Assert.IsFalse(r.Lineas.Any(l => l.Parcial));
        }

        [TestMethod]
        public void PorAlbaran_SinCuentasContables_ConTexto()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", new List<int> { 101, 101 }, null);

            CollectionAssert.AreEqual(new[] { 2, 3 }, r.Lineas.Select(l => l.Linea.Nº_Orden).ToArray());
            Assert.AreEqual("albarán 101", r.Descripcion);
        }

        [TestMethod]
        public void PorLinea_CantidadParcialConElSignoDeLaLinea()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", null, new List<LineaACopiarDTO>
            {
                new LineaACopiarDTO { NumeroOrden = 1, Cantidad = 2 },
                new LineaACopiarDTO { NumeroOrden = 5, Cantidad = 1 },
                new LineaACopiarDTO { NumeroOrden = 4 }
            });

            Assert.IsNull(r.Error);
            CollectionAssert.AreEqual(new short[] { 2, 1, -1 }, r.Lineas.Select(l => l.Cantidad).ToArray());
            Assert.IsTrue(r.Lineas[0].Parcial);
            Assert.IsFalse(r.Lineas[1].Parcial, "La línea de cuenta contable pedida como línea entra entera");
            Assert.AreEqual("líneas 1 (2 unidades de 5), 4, 5 (1 unidad de 3)", r.Descripcion);
        }

        [TestMethod]
        public void LineaYAlbaran_SeSumanYMandaLaCantidadDeLaLinea()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", new List<int> { 100 }, new List<LineaACopiarDTO> { new LineaACopiarDTO { NumeroOrden = 1, Cantidad = 3 }, new LineaACopiarDTO { NumeroOrden = 2 } });

            CollectionAssert.AreEqual(new short[] { 3, 1 }, r.Lineas.Select(l => l.Cantidad).ToArray());
        }

        [TestMethod]
        public void Errores_DeUstedYTodosJuntos()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", new List<int> { 999 }, new List<LineaACopiarDTO>
            {
                new LineaACopiarDTO { NumeroOrden = 1, Cantidad = 6 },
                new LineaACopiarDTO { NumeroOrden = 2, Cantidad = 0 },
                new LineaACopiarDTO { NumeroOrden = 77 },
                new LineaACopiarDTO { NumeroOrden = 4 },
                new LineaACopiarDTO { NumeroOrden = 4 }
            });

            Assert.AreEqual(0, r.Lineas.Count);
            StringAssert.Contains(r.Error, "El albarán 999 no es de la factura NV1 (sus albaranes son 100, 101, 102).");
            StringAssert.Contains(r.Error, "La línea 1 (P1) tiene 5 unidades en la factura NV1: no se pueden copiar 6 unidades.");
            StringAssert.Contains(r.Error, "La cantidad de la línea 2 (P2) tiene que ser mayor que cero.");
            StringAssert.Contains(r.Error, "La línea 77 no es de la factura NV1.");
            StringAssert.Contains(r.Error, "La línea 4 está repetida en la petición.");
        }

        [TestMethod]
        public void SoloPortesPorAlbaran_NoQuedaNada()
        {
            var factura = new List<LinPedidoVta> { L(1, 100), L(2, 200, tipo: 2) };

            var r = ReglaSeleccionCopiaFactura.Elegir(factura, "NV1", new List<int> { 200 }, null);

            StringAssert.StartsWith(r.Error, "No queda ninguna línea que copiar de la factura NV1");
        }

        [TestMethod]
        public void FacturaDeVariosPedidos_ElNumeroOrdenRepetidoPideElPedido()
        {
            var factura = new List<LinPedidoVta> { L(1, 100, pedido: 1), L(1, 101, pedido: 2), L(2, 101, pedido: 2) };

            var ambigua = ReglaSeleccionCopiaFactura.Elegir(factura, "NV1", null, new List<LineaACopiarDTO> { new LineaACopiarDTO { NumeroOrden = 1 } });
            var conPedido = ReglaSeleccionCopiaFactura.Elegir(factura, "NV1", null, new List<LineaACopiarDTO> { new LineaACopiarDTO { NumeroOrden = 1, Pedido = 2 } });

            Assert.AreEqual("La línea 1 está en los pedidos 1, 2 de la factura NV1: indique de qué pedido es.", ambigua.Error);
            Assert.IsNull(conPedido.Error);
            Assert.AreEqual(2, conPedido.Lineas.Single().Linea.Número);
            Assert.AreEqual("línea 2/1", conPedido.Descripcion);
        }

        [TestMethod]
        public void NumeroLinea_EsAliasDeNumeroOrden()
        {
            var dto = Newtonsoft.Json.JsonConvert.DeserializeObject<LineaACopiarDTO>("{\"NumeroLinea\": 7, \"Cantidad\": 2}");

            Assert.AreEqual(7, dto.NumeroOrden);
            StringAssert.DoesNotMatch(Newtonsoft.Json.JsonConvert.SerializeObject(dto), new System.Text.RegularExpressions.Regex("NumeroLinea"));
        }

        [TestMethod]
        public void HaySeleccion_SoloConAlbaranesOLineas()
        {
            Assert.IsFalse(new CopiarFacturaRequest().HaySeleccion);
            Assert.IsFalse(new CopiarFacturaRequest { Albaranes = new List<int>(), Lineas = new List<LineaACopiarDTO>() }.HaySeleccion);
            Assert.IsTrue(new CopiarFacturaRequest { Albaranes = new List<int> { 1 } }.HaySeleccion);
        }

        [TestMethod]
        public void ComentarioNoVendibles_SoloDeLasQueSeAbonan()
        {
            var r = ReglaSeleccionCopiaFactura.Elegir(Factura(), "NV1", null, new List<LineaACopiarDTO> { new LineaACopiarDTO { NumeroOrden = 1, Cantidad = 2 } });

            Assert.AreEqual("[Vuelve estropeado, no vendible: P1 (2 unidades). Hay que regularizar el stock a mano]",
                ReglaSeleccionCopiaFactura.ComentarioNoVendibles(new[] { 1, 2 }, r.Lineas));
            Assert.IsNull(ReglaSeleccionCopiaFactura.ComentarioNoVendibles(new[] { 2 }, r.Lineas));
        }

        // ---------------------------------------------------------------- no abonar dos veces, con unidades

        [TestMethod]
        public void YaAbonado_ParcialDentroDeLoQueQueda_Pasa_YSiSePasa_DiceCuantoQueda()
        {
            var anteriores = new[] { new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV1", LineaOriginal = 1, Cantidad = 3, Documento = "RV1" } };
            var dos = new ReglasAbonoCopiaFactura.LineaVendida { Factura = "NV1", Linea = 1, Producto = "P1", Cantidad = 5, CantidadQueSeAbona = 2 };
            var tres = new ReglasAbonoCopiaFactura.LineaVendida { Factura = "NV1", Linea = 1, Producto = "P1", Cantidad = 5, CantidadQueSeAbona = 3 };
            var entera = new ReglasAbonoCopiaFactura.LineaVendida { Factura = "NV1", Linea = 1, Producto = "P1", Cantidad = 5 };

            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoYaAbonado(new[] { dos }, anteriores));
            StringAssert.Contains(ReglasAbonoCopiaFactura.MotivoYaAbonado(new[] { tres }, anteriores), "P1 (3 de 5 ya abonadas: quedan 2 y se piden 3)");
            StringAssert.Contains(ReglasAbonoCopiaFactura.MotivoYaAbonado(new[] { entera }, anteriores), "P1 (3 de 5 ya abonadas)");
        }

        [TestMethod]
        public void YaCorregida_DeshacerUnaParteDeLoQueQueda_Pasa()
        {
            var anteriores = new[] { new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "RV1", LineaOriginal = 1, Cantidad = 1, Documento = "RV2" } };
            var parte = new ReglasAbonoCopiaFactura.LineaVendida { Factura = "RV1", Linea = 1, Producto = "P1", Cantidad = -3, CantidadQueSeAbona = 2 };
            var entera = new ReglasAbonoCopiaFactura.LineaVendida { Factura = "RV1", Linea = 1, Producto = "P1", Cantidad = -3 };

            Assert.IsNull(ReglasAbonoCopiaFactura.MotivoYaCorregida(new[] { parte }, anteriores));
            StringAssert.StartsWith(ReglasAbonoCopiaFactura.MotivoYaCorregida(new[] { entera }, anteriores), "La rectificativa RV1 ya está corregida");
        }

        [TestMethod]
        public void LineasParaCopiar_ConLoYaAbonadoYLoQueQueda()
        {
            var anteriores = new[] { new ReglasAbonoCopiaFactura.AbonoAnterior { FacturaOriginal = "NV1", LineaOriginal = 1, Cantidad = 2 } };

            LineasFacturaParaCopiarDTO dto = ReglaSeleccionCopiaFactura.LineasParaCopiar("1", "NV1", Factura(), anteriores, l => l.TipoLinea == 2);

            CollectionAssert.AreEqual(new[] { 100, 101, 102 }, dto.Albaranes);
            LineaFacturaParaCopiarDTO primera = dto.Lineas.First();
            Assert.AreEqual(2M, primera.YaAbonada);
            Assert.AreEqual(3M, primera.QuedanPorAbonar);
            Assert.IsTrue(dto.Lineas.Single(l => l.NumeroOrden == 4).EsCuentaContable);
            Assert.AreEqual(3M, dto.Lineas.Single(l => l.NumeroOrden == 5).QuedanPorAbonar);
        }
    }
}
