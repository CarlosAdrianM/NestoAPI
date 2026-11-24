using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#889 (Black Friday 2026): aviso NO estricto al grabar un pedido con líneas de una campaña «solo en stock» que hoy no se
    /// pueden servir con lo que hay.
    /// </summary>
    [TestClass]
    public class AvisoCampanaSoloEnStockTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26, 10, 0, 0);

        private static List<ProductoCampanaStock> BlackFriday() => new List<ProductoCampanaStock>
        {
            new ProductoCampanaStock { Campana = "Black Friday ", Empresa = "1  ", Producto = "45929          ",
                FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) },
            new ProductoCampanaStock { Campana = "Black Friday", Empresa = "1", Producto = "17404",
                FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) }
        };

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal descuento = 0.20M, short estado = -1, string texto = "CHAMPU LISAP") =>
            new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                DescuentoLinea = descuento,
                estado = estado,
                tipoLinea = (byte)Constantes.TiposLineaVenta.PRODUCTO,
                texto = texto
            };

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas) => new PedidoVentaDTO
        {
            empresa = "1",
            numero = 931500,
            Lineas = lineas.ToList()
        };

        private static List<DisponibleProductoCampana> Hay(string producto, int stock, int pendiente = 0) =>
            new List<DisponibleProductoCampana> { new DisponibleProductoCampana { Producto = producto, Stock = stock, PendienteOtrosPedidos = pendiente } };

        // ---------------------------------------------------------------- LA REGLA

        [TestMethod]
        public void Avisar_SinStock_DiceHoyNoHay()
        {
            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", BlackFriday(), HOY, Hay("45929", 0));

            Assert.IsNotNull(aviso);
            Assert.AreEqual(AvisoCampanaSoloEnStock.TIPO, aviso.Tipo);
            StringAssert.StartsWith(aviso.Mensaje, "El descuento de Black Friday del 45929 (CHAMPU LISAP) es solo para lo que haya en stock: hoy no hay.");
            StringAssert.Contains(aviso.Mensaje, "decida usted");
        }

        [TestMethod]
        public void Avisar_StockQueNoLlega_DiceCuantoHayDeCuanto()
        {
            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", BlackFriday(), HOY, Hay("45929", 6, 4));

            StringAssert.StartsWith(aviso.Mensaje, "El descuento de Black Friday del 45929 (CHAMPU LISAP) es solo para lo que haya en stock: hoy hay 2 de 5.");
        }

        [TestMethod]
        public void Avisar_HayDeSobra_NoAvisa()
        {
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", BlackFriday(), HOY, Hay("45929", 5)));
        }

        [TestMethod]
        public void Avisar_LoPendienteDeOtrosSeLlevaElStock_HoyNoHay()
        {
            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 1)).Lineas, "1", BlackFriday(), HOY, Hay("45929", 3, 7));

            StringAssert.Contains(aviso.Mensaje, "hoy no hay.");
        }

        [TestMethod]
        public void Avisar_DosLineasDelMismoProducto_SumanLaCantidad()
        {
            PedidoVentaDTO pedido = Pedido(Linea("45929", 2), Linea("45929 ", 3));

            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(pedido.Lineas, "1", BlackFriday(), HOY, Hay("45929", 5)), "2 + 3 = 5: llega");
            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(pedido.Lineas, "1", BlackFriday(), HOY, Hay("45929", 4));
            StringAssert.Contains(aviso.Mensaje, "hoy hay 4 de 5.");
            Assert.AreEqual(1, ((IEnumerable<object>)aviso.Datos).Count(), "Un solo aviso por producto");
        }

        [TestMethod]
        public void Avisar_VariosProductos_UnAvisoConTodos()
        {
            PedidoVentaDTO pedido = Pedido(Linea("45929", 2), Linea("17404", 1, texto: "TINTE"), Linea("99999", 9));
            var disponibles = new List<DisponibleProductoCampana>
            {
                new DisponibleProductoCampana { Producto = "45929", Stock = 0 },
                new DisponibleProductoCampana { Producto = "17404", Stock = 0 }
            };

            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(pedido.Lineas, "1", BlackFriday(), HOY, disponibles);

            StringAssert.Contains(aviso.Mensaje, "del 45929 (CHAMPU LISAP)");
            StringAssert.Contains(aviso.Mensaje, "del 17404 (TINTE)");
            Assert.IsFalse(aviso.Mensaje.Contains("99999"), "El que no es de la campaña no avisa");
        }

        [TestMethod]
        public void Avisar_ProductoSinFilaLeida_CuentaComoQueNoHay()
        {
            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 1)).Lineas, "1", BlackFriday(), HOY, new List<DisponibleProductoCampana>());

            StringAssert.Contains(aviso.Mensaje, "hoy no hay.");
        }

        [TestMethod]
        public void Avisar_LineaSinDescuento_NoAvisa()
        {
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5, descuento: 0M)).Lineas, "1", BlackFriday(), HOY, Hay("45929", 0)));
        }

        [TestMethod]
        public void Avisar_DescuentoEnDescuentoProducto_TambienCuenta()
        {
            LineaPedidoVentaDTO linea = Linea("45929", 5, descuento: 0M);
            linea.DescuentoProducto = 0.20M;

            Assert.IsNotNull(AvisoCampanaSoloEnStock.Avisar(new List<LineaPedidoVentaDTO> { linea }, "1", BlackFriday(), HOY, Hay("45929", 0)));
        }

        [TestMethod]
        public void Avisar_LineaYaServida_NoCuenta()
        {
            PedidoVentaDTO pedido = Pedido(Linea("45929", 5, estado: Constantes.EstadosLineaVenta.ALBARAN), Linea("45929", 1));

            AvisoPedidoDTO aviso = AvisoCampanaSoloEnStock.Avisar(pedido.Lineas, "1", BlackFriday(), HOY, Hay("45929", 0));

            StringAssert.Contains(aviso.Mensaje, "hoy no hay.");
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(pedido.Lineas, "1", BlackFriday(), HOY, Hay("45929", 1)),
                "Solo cuenta la que queda por servir (1 ud.), y de esa hay");
        }

        [TestMethod]
        public void Avisar_CampanaNoVigente_NoAvisa()
        {
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", BlackFriday(), new DateTime(2026, 12, 1), Hay("45929", 0)));
            Assert.IsNotNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", BlackFriday(), new DateTime(2026, 11, 30, 23, 0, 0), Hay("45929", 0)),
                "El último día cuenta entero");
        }

        [TestMethod]
        public void Avisar_OtraEmpresa_NoAvisa()
        {
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "3", BlackFriday(), HOY, Hay("45929", 0)));
        }

        [TestMethod]
        public void Avisar_SinTabla_NoAvisa()
        {
            Assert.IsNull(AvisoCampanaSoloEnStock.Avisar(Pedido(Linea("45929", 5)).Lineas, "1", new List<ProductoCampanaStock>(), HOY, Hay("45929", 0)));
        }

        [TestMethod]
        public void Mensaje_SinNombre_SoloLaReferencia()
        {
            Assert.AreEqual("El descuento de Black Friday del 45929 es solo para lo que haya en stock: hoy hay 2 de 5.",
                AvisoCampanaSoloEnStock.Mensaje("Black Friday", "45929", null, 2, 5));
        }

        // ---------------------------------------------------------------- qué pedidos se miran

        [TestMethod]
        public void ProductosALeer_Presupuesto_Ninguno()
        {
            PedidoVentaDTO pedido = Pedido(Linea("45929", 5));
            pedido.EsPresupuesto = true;

            Assert.AreEqual(0, AvisoCampanaSoloEnStock.ProductosALeer(pedido, BlackFriday(), HOY).Count);
        }

        [TestMethod]
        public void ProductosALeer_TiendaOnline_Ninguno()
        {
            LineaPedidoVentaDTO linea = Linea("45929", 5);
            linea.formaVenta = Constantes.FormasVenta.TIENDA_ONLINE;
            PedidoVentaDTO pedido = Pedido(linea);
            pedido.comentarios = "TOTAL PEDIDO: 25,00";

            Assert.AreEqual(0, AvisoCampanaSoloEnStock.ProductosALeer(pedido, BlackFriday(), HOY).Count);
        }

        [TestMethod]
        public void ProductosALeer_SoloLosDeLaCampana_SinRepetir()
        {
            PedidoVentaDTO pedido = Pedido(Linea("45929", 2), Linea("45929", 1), Linea("99999", 1), Linea("17404", 1, descuento: 0M));

            CollectionAssert.AreEqual(new[] { "45929" }, AvisoCampanaSoloEnStock.ProductosALeer(pedido, BlackFriday(), HOY));
        }

        // ---------------------------------------------------------------- el aviso del pedido grabado

        [TestMethod]
        public async Task Calcular_SinLineasDeLaCampana_NiSiquieraLee()
        {
            ILectorDisponibleCampanaStock lector = A.Fake<ILectorDisponibleCampanaStock>();

            AvisoPedidoDTO aviso = await AvisoCampanaSoloEnStock.Calcular(Pedido(Linea("99999", 5)), HOY, lector, BlackFriday());

            Assert.IsNull(aviso);
            A.CallTo(() => lector.Leer(A<string>._, A<int>._, A<IReadOnlyCollection<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Calcular_LeeUnaVezConTodosLosProductosYSinElPropioPedido()
        {
            ILectorDisponibleCampanaStock lector = A.Fake<ILectorDisponibleCampanaStock>();
            A.CallTo(() => lector.Leer("1", 931500, A<IReadOnlyCollection<string>>._)).Returns(Task.FromResult(Hay("45929", 1)));

            AvisoPedidoDTO aviso = await AvisoCampanaSoloEnStock.Calcular(Pedido(Linea("45929", 2), Linea("17404", 1)), HOY, lector, BlackFriday());

            A.CallTo(() => lector.Leer("1", 931500, A<IReadOnlyCollection<string>>.That.Matches(p => p.Count == 2 && p.Contains("45929") && p.Contains("17404"))))
                .MustHaveHappenedOnceExactly();
            StringAssert.Contains(aviso.Mensaje, "del 45929 (CHAMPU LISAP) es solo para lo que haya en stock: hoy hay 1 de 2.");
            StringAssert.Contains(aviso.Mensaje, "del 17404 (CHAMPU LISAP) es solo para lo que haya en stock: hoy no hay.");
        }

        [TestMethod]
        public async Task Calcular_SiFallaLaLectura_SinAvisoYSeApunta()
        {
            ILectorDisponibleCampanaStock lector = A.Fake<ILectorDisponibleCampanaStock>();
            A.CallTo(() => lector.Leer(A<string>._, A<int>._, A<IReadOnlyCollection<string>>._)).Throws(new InvalidOperationException("sin BD"));
            var registrados = new List<Exception>();

            AvisoPedidoDTO aviso = await AvisoCampanaSoloEnStock.Calcular(Pedido(Linea("45929", 2)), HOY, lector, BlackFriday(), registrados.Add);

            Assert.IsNull(aviso);
            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "931500");
        }

        [TestMethod]
        public async Task Calcular_PedidoNulo_NoLanza()
        {
            Assert.IsNull(await AvisoCampanaSoloEnStock.Calcular(null, HOY, A.Fake<ILectorDisponibleCampanaStock>(), BlackFriday()));
        }

        // ---------------------------------------------------------------- la consulta y la caché

        [TestMethod]
        public void Sql_SumaLasTresSedesDeLasDosEmpresasYQuitaElPropioPedido()
        {
            string sql = LectorDisponibleCampanaStockSql.Sql(2);

            StringAssert.Contains(sql, "e.Almacén IN (@alg, @rei, @alc)");
            StringAssert.Contains(sql, "e.Empresa IN (@empresa1, @empresa3)");
            StringAssert.Contains(sql, "l.Estado >= -1 AND l.Estado <= 1");
            StringAssert.Contains(sql, "NOT (l.Empresa = @empresa AND l.Número = @pedido)");
            StringAssert.Contains(sql, "e.Número IN (@p0, @p1)");
            Assert.IsFalse(sql.Contains("{0}"));
        }

        [TestMethod]
        public void Cache_SinTablaOSinLector_NingunaCampana_YSiFallaLoApuntaUnaVez()
        {
            try
            {
                CampanasCondicionStock.Reiniciar();
                Assert.AreEqual(0, CampanasCondicionStock.Actuales.Count);

                var registrados = new List<Exception>();
                DateTime ahora = new DateTime(2026, 11, 26, 9, 0, 0);
                CampanasCondicionStock.Configurar(() => throw new InvalidOperationException("Invalid object name"), registrados.Add, () => ahora);
                Assert.AreEqual(0, CampanasCondicionStock.Actuales.Count);
                ahora = ahora.AddMinutes(11);
                Assert.AreEqual(0, CampanasCondicionStock.Actuales.Count);
                Assert.AreEqual(1, registrados.Count, "ELMAH una vez, no cada diez minutos");

                CampanasCondicionStock.ConfigurarPara(BlackFriday().ToArray());
                Assert.AreEqual(2, CampanasCondicionStock.Actuales.Count);
            }
            finally
            {
                CampanasCondicionStock.Reiniciar();
            }
        }
    }
}
