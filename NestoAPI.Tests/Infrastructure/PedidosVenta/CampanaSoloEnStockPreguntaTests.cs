using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#889 (Black Friday 2026, Carlos 24/11/26): se pregunta antes de guardar y, por defecto, lo que no hay en stock va sin el
    /// descuento de la campaña (la línea se parte).
    /// </summary>
    [TestClass]
    public class CampanaSoloEnStockPreguntaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26, 10, 0, 0);

        private static List<ProductoCampanaStock> BlackFriday() => new List<ProductoCampanaStock>
        {
            new ProductoCampanaStock { Campana = "Black Friday", Empresa = "1", Producto = "45929", FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) },
            new ProductoCampanaStock { Campana = "Black Friday", Empresa = "1", Producto = "17404", FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30) }
        };

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal descuento = 0.30M, int id = 0, int picking = 0, short estado = -1) =>
            new LineaPedidoVentaDTO
            {
                id = id,
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = 10M,
                DescuentoProducto = descuento,
                AplicarDescuento = true,
                estado = estado,
                picking = picking,
                tipoLinea = (byte)Constantes.TiposLineaVenta.PRODUCTO,
                texto = "CHAMPU LISAP",
                almacen = "ALG",
                iva = "G21",
                fechaEntrega = new DateTime(2026, 11, 27)
            };

        private static PedidoVentaDTO Pedido(string opcion, params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 0, cliente = "15191", contacto = "0", CampanaSoloEnStock = opcion };
            foreach (LineaPedidoVentaDTO l in lineas)
            {
                pedido.Lineas.Add(l);
            }
            return pedido;
        }

        private static ILectorDisponibleCampanaStock Lector(params (string producto, int stock)[] hay)
        {
            var lector = A.Fake<ILectorDisponibleCampanaStock>();
            A.CallTo(() => lector.Leer(A<string>._, A<int>._, A<IReadOnlyCollection<string>>._))
                .Returns(Task.FromResult(hay.Select(h => new DisponibleProductoCampana { Producto = h.producto, Stock = h.stock }).ToList()));
            return lector;
        }

        private static ICalculadorPrecioSinCampana Calculador(decimal descuento = 0.10M, decimal precio = 10M)
        {
            var calculador = A.Fake<ICalculadorPrecioSinCampana>();
            A.CallTo(() => calculador.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<int>._, A<bool>._))
                .Returns(new PrecioSinCampana { Precio = precio, Descuento = descuento, Pvp = 10M, AplicarDescuento = true });
            return calculador;
        }

        private static Dictionary<string, PrecioSinCampana> Sin(decimal descuento = 0.10M) =>
            new Dictionary<string, PrecioSinCampana>(StringComparer.OrdinalIgnoreCase)
            {
                { "45929", new PrecioSinCampana { Precio = 10M, Descuento = descuento, Pvp = 10M } },
                { "17404", new PrecioSinCampana { Precio = 10M, Descuento = descuento, Pvp = 10M } }
            };

        private static List<FaltaCampanaSoloEnStock> Faltas(List<LineaPedidoVentaDTO> lineas, params (string producto, int stock)[] hay) =>
            ReglaCampanaSoloEnStock.Faltas(lineas, "1", BlackFriday(), HOY,
                hay.Select(h => new DisponibleProductoCampana { Producto = h.producto, Stock = h.stock }), Sin());

        // ---------------------------------------------------------------- LA REGLA: Ajustar

        [TestMethod]
        public void Ajustar_StockParcial_PartelaLinea()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 5) };

            List<AjusteCampanaSoloEnStock> ajustes = ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 2)), Sin(), null);

            Assert.AreEqual(2, lineas.Count);
            Assert.AreEqual(2, lineas[0].Cantidad);
            Assert.AreEqual(0.30M, lineas[0].DescuentoProducto);
            Assert.AreEqual(3, lineas[1].Cantidad);
            Assert.AreEqual(0, lineas[1].id);
            Assert.AreEqual(0.10M, lineas[1].DescuentoProducto);
            Assert.AreEqual("45929", lineas[1].Producto);
            Assert.AreEqual("ALG", lineas[1].almacen);
            Assert.AreEqual("G21", lineas[1].iva);
            Assert.AreEqual(new DateTime(2026, 11, 27), lineas[1].fechaEntrega);
            Assert.AreEqual(1, ajustes.Count);
            Assert.AreEqual(2, ajustes[0].ConDescuento);
            Assert.AreEqual(3, ajustes[0].SinDescuento);
        }

        [TestMethod]
        public void Ajustar_SinStock_CambiaLaPropiaLinea()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 5) };

            ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 0)), Sin(0M), null);

            Assert.AreEqual(1, lineas.Count);
            Assert.AreEqual(5, lineas[0].Cantidad);
            Assert.AreEqual(0M, lineas[0].DescuentoProducto);
            Assert.AreEqual(10M, lineas[0].PrecioUnitario);
        }

        [TestMethod]
        public void Ajustar_VariasLineasDelMismoProducto_ReparteEnOrdenYConservaLasUnidades()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 2), Linea("45929", 3), Linea("45929", 4) };

            ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 3)), Sin(), null);

            Assert.AreEqual(9, lineas.Sum(l => l.Cantidad));
            Assert.AreEqual(3, lineas.Where(l => l.DescuentoProducto == 0.30M).Sum(l => l.Cantidad));
            Assert.AreEqual(6, lineas.Where(l => l.DescuentoProducto == 0.10M).Sum(l => l.Cantidad));
            // 2 con descuento; la segunda se parte (1 con y 2 sin); la tercera, entera sin
            CollectionAssert.AreEqual(new[] { 2, 1, 2, 4 }, lineas.Select(l => l.Cantidad).ToArray());
        }

        [TestMethod]
        public void Ajustar_LoQueNoEsDeLaCampana_NoSeToca()
        {
            var otra = Linea("99999", 7);
            var lineas = new List<LineaPedidoVentaDTO> { otra, Linea("45929", 5) };

            ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 0)), Sin(), null);

            Assert.AreEqual(7, otra.Cantidad);
            Assert.AreEqual(0.30M, otra.DescuentoProducto);
        }

        [TestMethod]
        public void Ajustar_LineaQueNoSePuedeTocar_ConsumeLoDisponibleYNoCambia()
        {
            var conPicking = Linea("45929", 2, id: 10, picking: 5);
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 3), conPicking };

            ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 2)), Sin(), l => l.picking == 0);

            Assert.AreEqual(2, conPicking.Cantidad);
            Assert.AreEqual(0.30M, conPicking.DescuentoProducto);
            Assert.AreEqual(0.10M, lineas[0].DescuentoProducto);
            Assert.AreEqual(3, lineas[0].Cantidad);
        }

        [TestMethod]
        public void Faltas_LoYaPartido_NoSeVuelveAPreguntar()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 5) };
            ReglaCampanaSoloEnStock.Ajustar(lineas, Faltas(lineas, ("45929", 2)), Sin(), null);

            Assert.AreEqual(0, Faltas(lineas, ("45929", 2)).Count);
        }

        // ---------------------------------------------------------------- Textos

        [TestMethod]
        public void Respuesta_StockParcial_MensajeDeUsted()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 5) };

            AvisoCampanaSoloEnStockRespuesta respuesta = ReglaCampanaSoloEnStock.Respuesta(Faltas(lineas, ("45929", 2)), Sin());

            Assert.IsTrue(respuesta.HayAviso);
            Assert.AreEqual("Black Friday", respuesta.Campana);
            Assert.AreEqual("Black Friday: el descuento es solo para lo que haya en stock. Del 45929 (CHAMPU LISAP) hay 2 de las 5 que pide: 3 irían sin el descuento de la campaña (al 10 % habitual).",
                respuesta.Mensaje);
            ProductoCampanaSoloEnStockDTO p = respuesta.Productos.Single();
            Assert.AreEqual(5, p.Pedidas);
            Assert.AreEqual(2, p.Disponibles);
            Assert.AreEqual(2, p.ConDescuento);
            Assert.AreEqual(3, p.SinDescuento);
            Assert.AreEqual(0.30M, p.DescuentoCampana);
            Assert.AreEqual(0.10M, p.DescuentoSinCampana);
            Assert.AreEqual(10M, p.PrecioSinCampana);
        }

        [TestMethod]
        public void Respuesta_SinStockNiDescuentoHabitual_APrecioDeTarifa()
        {
            var lineas = new List<LineaPedidoVentaDTO> { Linea("45929", 1) };

            AvisoCampanaSoloEnStockRespuesta respuesta = ReglaCampanaSoloEnStock.Respuesta(Faltas(lineas, ("45929", 0)), Sin(0M));

            StringAssert.Contains(respuesta.Mensaje, "Del 45929 (CHAMPU LISAP) hoy no hay: la que pide iría sin el descuento de la campaña (a precio de tarifa).");
        }

        // ---------------------------------------------------------------- Fichas desbloqueadas por la campaña

        private static List<FilaCampanaAplicarDto> Desbloqueo(string producto = "45929", bool? anterior = false, DateTime? activado = null) =>
            new List<FilaCampanaAplicarDto>
            {
                new FilaCampanaAplicarDto { Campana = "Black Friday ", Empresa = "1  ", Producto = producto + "   ",
                    FechaDesde = new DateTime(2026, 11, 25), FechaHasta = new DateTime(2026, 11, 30), AplicarDtoAnterior = anterior, FechaActivado = activado }
            };

        [TestMethod]
        public void FichaDesbloqueada_PorEstaCampanaVigente_Si()
        {
            Assert.IsTrue(ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(Desbloqueo(anterior: null), "Black Friday", "1", "45929", HOY));
            Assert.IsTrue(ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(Desbloqueo(activado: HOY), "Black Friday", "1", "45929", HOY));
        }

        [TestMethod]
        public void FichaDesbloqueada_OtraCampanaOtroProductoOYaTeniaAplicarDto_No()
        {
            Assert.IsFalse(ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(Desbloqueo(), "Navidad", "1", "45929", HOY));
            Assert.IsFalse(ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(Desbloqueo(), "Black Friday", "1", "17404", HOY));
            Assert.IsFalse(ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(Desbloqueo(anterior: true, activado: HOY), "Black Friday", "1", "45929", HOY));
        }

        private static ICalculadorPrecioSinCampana CalculadorQueMiraElDesbloqueo()
        {
            var calculador = A.Fake<ICalculadorPrecioSinCampana>();
            A.CallTo(() => calculador.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<int>._, A<bool>._))
                .ReturnsLazily(call => call.GetArgument<bool>(6)
                    ? new PrecioSinCampana { Precio = 10M, Descuento = 0M, Pvp = 10M, AplicarDescuento = false }
                    : new PrecioSinCampana { Precio = 10M, Descuento = 0.10M, Pvp = 10M, AplicarDescuento = true });
            return calculador;
        }

        [TestMethod]
        public async Task AlGuardar_FichaDesbloqueadaPorLaCampana_VaSinDescuento()
        {
            PedidoVentaDTO pedido = Pedido("QuitarDescuento", Linea("45929", 5));

            await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("45929", 0)), BlackFriday(), CalculadorQueMiraElDesbloqueo(),
                e => { }, () => Task.FromResult(Desbloqueo()));

            LineaPedidoVentaDTO linea = pedido.Lineas.Single();
            Assert.AreEqual(0M, linea.DescuentoProducto);
            Assert.IsFalse(linea.AplicarDescuento);
        }

        [TestMethod]
        public async Task AlGuardar_FichaNormal_SuDescuentoHabitual()
        {
            PedidoVentaDTO pedido = Pedido("QuitarDescuento", Linea("17404", 5));

            await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("17404", 0)), BlackFriday(), CalculadorQueMiraElDesbloqueo(),
                e => { }, () => Task.FromResult(Desbloqueo()));

            LineaPedidoVentaDTO linea = pedido.Lineas.Single();
            Assert.AreEqual(0.10M, linea.DescuentoProducto);
            Assert.IsTrue(linea.AplicarDescuento);
        }

        [TestMethod]
        public async Task AlGuardar_SiFallaLaLecturaDeDesbloqueos_SigueComoFichaNormal()
        {
            PedidoVentaDTO pedido = Pedido("QuitarDescuento", Linea("45929", 5));
            Exception registrada = null;

            await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("45929", 0)), BlackFriday(), CalculadorQueMiraElDesbloqueo(),
                e => registrada = e, () => throw new Exception("caída"));

            Assert.AreEqual(0.10M, pedido.Lineas.Single().DescuentoProducto);
            Assert.IsNotNull(registrada);
        }

        // ---------------------------------------------------------------- Servicio y endpoint

        [TestMethod]
        public async Task Consultar_HayStock_SinAviso()
        {
            AvisoCampanaSoloEnStockRespuesta respuesta = await ServicioCampanaSoloEnStock.Consultar(Pedido(null, Linea("45929", 5)), HOY,
                Lector(("45929", 10)), BlackFriday(), Calculador(), e => { });

            Assert.IsFalse(respuesta.HayAviso);
        }

        [TestMethod]
        public async Task Consultar_SiFallaElLector_SinAvisoYNoLanza()
        {
            var lector = A.Fake<ILectorDisponibleCampanaStock>();
            A.CallTo(() => lector.Leer(A<string>._, A<int>._, A<IReadOnlyCollection<string>>._)).Throws(new Exception("caído"));
            Exception registrada = null;

            AvisoCampanaSoloEnStockRespuesta respuesta = await ServicioCampanaSoloEnStock.Consultar(Pedido(null, Linea("45929", 5)), HOY,
                lector, BlackFriday(), Calculador(), e => registrada = e);

            Assert.IsFalse(respuesta.HayAviso);
            Assert.IsNotNull(registrada);
        }

        [TestMethod]
        public async Task Endpoint_DevuelveLaPregunta()
        {
            var controlador = new PedidosVentaCondicionesCampanaController(() => HOY, Lector(("45929", 2)), BlackFriday(), Calculador());

            var resultado = await controlador.PostAvisoCampanaSoloEnStock(Pedido(null, Linea("45929", 5))) as OkNegotiatedContentResult<AvisoCampanaSoloEnStockRespuesta>;

            Assert.IsNotNull(resultado);
            Assert.IsTrue(resultado.Content.HayAviso);
            Assert.AreEqual(3, resultado.Content.Productos.Single().SinDescuento);
        }

        [TestMethod]
        public async Task AlGuardar_SinCampo_NoTocaNadaYElAvisoDeSiempreSale()
        {
            PedidoVentaDTO pedido = Pedido(null, Linea("45929", 5));

            AvisoPedidoDTO aplicado = await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("45929", 2)), BlackFriday(), Calculador());

            Assert.IsNull(aplicado);
            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.AreEqual(5, pedido.Lineas.Single().Cantidad);
            Assert.IsFalse(OpcionesCampanaSoloEnStock.YaContestada(pedido.CampanaSoloEnStock));
        }

        [TestMethod]
        public async Task AlGuardar_MantenerDescuento_NoTocaNadaYNoSeVuelveAAvisar()
        {
            PedidoVentaDTO pedido = Pedido("MantenerDescuento", Linea("45929", 5));

            AvisoPedidoDTO aplicado = await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("45929", 2)), BlackFriday(), Calculador());

            Assert.IsNull(aplicado);
            Assert.AreEqual(5, pedido.Lineas.Single().Cantidad);
            Assert.AreEqual(0.30M, pedido.Lineas.Single().DescuentoProducto);
            Assert.IsTrue(OpcionesCampanaSoloEnStock.YaContestada(pedido.CampanaSoloEnStock));
        }

        [TestMethod]
        public async Task AlGuardar_QuitarDescuento_ParteLaLineaYDevuelveLoHecho()
        {
            PedidoVentaDTO pedido = Pedido("QuitarDescuento", Linea("45929", 5));

            AvisoPedidoDTO aplicado = await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, l => l.picking == 0, Lector(("45929", 2)), BlackFriday(), Calculador());

            Assert.IsNotNull(aplicado);
            Assert.AreEqual(ReglaCampanaSoloEnStock.TIPO_APLICADO, aplicado.Tipo);
            Assert.AreEqual("Black Friday: lo que no hay en stock va sin el descuento. Del 45929 (CHAMPU LISAP), 2 con el descuento de la campaña y 3 sin él (al 10 % habitual), en otra línea.",
                aplicado.Mensaje);
            List<LineaPedidoVentaDTO> lineas = pedido.Lineas.ToList();
            Assert.AreEqual(2, lineas.Count);
            Assert.AreEqual(5, lineas.Sum(l => l.Cantidad));
            Assert.IsTrue(lineas.All(l => l.Pedido == pedido));
            Assert.IsTrue(OpcionesCampanaSoloEnStock.YaContestada(pedido.CampanaSoloEnStock));
        }

        [TestMethod]
        public async Task AlGuardar_QuitarDescuentoConStock_NoTocaNada()
        {
            PedidoVentaDTO pedido = Pedido("QuitarDescuento", Linea("45929", 5));

            AvisoPedidoDTO aplicado = await ServicioCampanaSoloEnStock.AlGuardar(pedido, HOY, null, Lector(("45929", 5)), BlackFriday(), Calculador());

            Assert.IsNull(aplicado);
            Assert.AreEqual(1, pedido.Lineas.Count);
        }
    }

    /// <summary>NestoAPI#889: el motor «sin la campaña» y que sin pedirlo no cambia.</summary>
    [TestClass]
    public class GestorPreciosSinCampanaTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeDescuentos = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            A.CallTo(() => db.DescuentosProductoes).Returns(fakeDescuentos);
            var filas = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Producto = "44166", CantidadMínima = 1, Descuento = 0.30m, Campana = "Black Friday", Producto = new Producto { Número = "44166" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.10m, Producto = new Producto { Número = "OTRO" } }
            }.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<DescuentosProducto>)fakeDescuentos).GetAsyncEnumerator()).Returns(new TestDbAsyncEnumerator<DescuentosProducto>(filas.GetEnumerator()));
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fakeDescuentos).Provider).Returns(new TestDbAsyncQueryProvider<DescuentosProducto>(filas.Provider));
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fakeDescuentos).Expression).Returns(filas.Expression);
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fakeDescuentos).ElementType).Returns(filas.ElementType);
            A.CallTo(() => ((IQueryable<DescuentosProducto>)fakeDescuentos).GetEnumerator()).ReturnsLazily(() => filas.GetEnumerator());
        }

        private static PrecioDescuentoProducto Datos(string excluir) => new PrecioDescuentoProducto
        {
            producto = new Producto { Empresa = "1", Número = "44166", Nombre = "GEL", PVP = 100m, Familia = "Lisap", Grupo = "PEL", SubGrupo = "ACB", Aplicar_Dto = true },
            cliente = "2414",
            contacto = "0",
            cantidad = 1,
            aplicarDescuento = true,
            campanaExcluida = excluir
        };

        [TestMethod]
        public void SinPedirlo_ElMotorNoCambia_GanaLaCampana()
        {
            PrecioDescuentoProducto datos = Datos(null);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.30m, datos.descuentoCalculado);
        }

        [TestMethod]
        public void SinLaCampana_SaleElDescuentoHabitualDelCliente()
        {
            PrecioDescuentoProducto datos = Datos("Black Friday ");

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.10m, datos.descuentoCalculado);
            Assert.AreEqual(100m, datos.precioCalculado);
        }

        [TestMethod]
        public void FichaDesbloqueada_SinAplicarDescuento_TarifaSinDescuento()
        {
            PrecioDescuentoProducto datos = Datos("Black Friday");
            datos.aplicarDescuento = false;

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0m, datos.descuentoCalculado);
            Assert.AreEqual(100m, datos.precioCalculado);
        }

        [TestMethod]
        public void ExcluyendoOtraCampana_GanaLaCampana()
        {
            PrecioDescuentoProducto datos = Datos("Navidad");

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.30m, datos.descuentoCalculado);
        }
    }
}
