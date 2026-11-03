using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#588: el pedido completo (GET, POST y PUT de api/PedidosVenta) trae «tieneFechasFuturas» calculado con la MISMA
    /// regla que la lista de pedidos (<see cref="FechasFuturasPedido"/>), incluida la ruta interna con día de reparto
    /// (NestoAPI#764), para que Nesto no tenga que recalcularlo ni pedir la fecha mínima a la API.
    /// </summary>
    [TestClass]
    public class PedidosVentaFechasFuturasTests
    {
        private static readonly DateTime FUTURO = new DateTime(2030, 1, 1);
        private static readonly DateTime PASADO = new DateTime(2000, 1, 1);
        private const string EMPRESA = "1  ";

        private NVEntities db;
        private PedidosVentaController controller;
        private readonly List<CabPedidoVta> cabeceras = new List<CabPedidoVta>();
        private readonly List<LinPedidoVta> lineas = new List<LinPedidoVta>();
        private Func<bool> pickingRutasInternasAnterior;

        [TestInitialize]
        public void Preparar()
        {
            pickingRutasInternasAnterior = GestorPedidosVenta.PickingRutasInternasEncendido;
            GestorPedidosVenta.PickingRutasInternasEncendido = () => true;
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            DiasRepartoCodigosPostales.ConfigurarPara(new Dictionary<string, string>
            {
                ["28003"] = "01",
                ["28007"] = "02",
                ["28001"] = "03",
                ["28023"] = "04",
                ["28005"] = "05"
            });

            db = A.Fake<NVEntities>();
            A.CallTo(() => db.CabPedidoVtas).Returns(FakeDbSet(cabeceras));
            A.CallTo(() => db.LinPedidoVtas).Returns(FakeDbSet(lineas));
            A.CallTo(() => db.VendedoresPedidosGruposProductos).Returns(FakeDbSet(new List<VendedorPedidoGrupoProducto>()));
            A.CallTo(() => db.EnviosAgencias).Returns(FakeDbSet(new List<EnviosAgencia>()));
            controller = new PedidosVentaController(db, A.Fake<IServicioVendedores>());
        }

        [TestCleanup]
        public void Limpiar()
        {
            GestorPedidosVenta.PickingRutasInternasEncendido = pickingRutasInternasAnterior;
            RutasPropiasConfiguradas.Reiniciar();
            DiasRepartoCodigosPostales.Reiniciar();
        }

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            IQueryable<T> data = datos.AsQueryable();
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).Returns(new TestAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestAsyncEnumerator<T>(data.GetEnumerator()));
            return fakeSet;
        }

        private static LineaPedidoVentaDTO Linea(DateTime entrega, short estado = -1)
        {
            return new LineaPedidoVentaDTO { estado = estado, fechaEntrega = entrega };
        }

        /// <summary>Una cabecera de la ruta y el CP dados y sus líneas, en la BD falsa (para la lista y para el POST/PUT).</summary>
        private void PedidoEnBd(int numero, string ruta, string codigoPostal, params (short estado, DateTime entrega)[] lineasPedido)
        {
            cabeceras.Add(new CabPedidoVta
            {
                Empresa = EMPRESA,
                Número = numero,
                Nº_Cliente = "15191",
                Vendedor = "NV ",
                Ruta = ruta,
                Cliente = new Cliente { Nombre = "CLIENTE", CodPostal = codigoPostal }
            });
            foreach ((short estado, DateTime entrega) l in lineasPedido)
            {
                lineas.Add(new LinPedidoVta { Empresa = EMPRESA, Número = numero, Estado = l.estado, TipoLinea = 1, Picking = 0, Fecha_Entrega = l.entrega });
            }
        }

        /// <summary>El código postal con el día de ruta más lejano desde hoy: con el cálculo viejo (hoy o mañana) saldría «futuro».</summary>
        private static (string cp, DateTime fechaRuta) CodigoPostalConElDiaMasLejano()
        {
            return new[] { "28003", "28007", "28001", "28023", "28005" }
                .Select(cp => (cp, fechaRuta: FechasFuturasPedido.FechaEntregaFutura("16", cp)))
                .OrderByDescending(x => x.fechaRuta)
                .First();
        }

        [TestMethod]
        public void TieneFechasFuturas_LineaPendienteQueSeEntregaDespuesDeLaFechaMinima_True()
        {
            DateTime minima = new DateTime(2026, 11, 4);
            Assert.IsTrue(FechasFuturasPedido.TieneFechasFuturas(FechasFuturasPedido.UltimaEntregaPendiente(new[] { Linea(minima.AddDays(1)) }), minima));
            Assert.IsTrue(FechasFuturasPedido.TieneFechasFuturas(FechasFuturasPedido.UltimaEntregaPendiente(new[] { Linea(minima, 1), Linea(minima.AddDays(7), 1) }), minima),
                "en curso también cuenta");
        }

        [TestMethod]
        public void TieneFechasFuturas_SinLineasPendientesPosteriores_False()
        {
            DateTime minima = new DateTime(2026, 11, 4);
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(FechasFuturasPedido.UltimaEntregaPendiente(new[] { Linea(minima) }), minima), "el mismo día no es futuro");
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(FechasFuturasPedido.UltimaEntregaPendiente(new[] { Linea(minima.AddDays(7), 2), Linea(minima.AddDays(7), 4) }), minima),
                "las servidas o facturadas no cuentan");
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(FechasFuturasPedido.UltimaEntregaPendiente(new[] { Linea(minima.AddDays(7), -3) }), minima),
                "los presupuestos no cuentan");
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(new List<LineaPedidoVentaDTO>(), "FW", null), "sin líneas");
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(null, "FW", null));
        }

        [TestMethod]
        public void TieneFechasFuturas_ConLasLineasDelPedido_ComparaConLaFechaMinimaDeSuRuta()
        {
            Assert.IsTrue(FechasFuturasPedido.TieneFechasFuturas(new[] { Linea(PASADO), Linea(FUTURO) }, "FW", "28760"));
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(new[] { Linea(PASADO) }, "FW", "28760"));
            Assert.IsFalse(FechasFuturasPedido.TieneFechasFuturas(new[] { Linea(FUTURO, 4) }, "FW", "28760"));
        }

        [TestMethod]
        public async Task TieneFechasFuturas_RutaInterna_HeredaLaReglaDeLaLista()
        {
            // NestoAPI#764: un pedido de ruta interna que sale el día de su ruta NO tiene fechas futuras aunque sea pasado mañana.
            (string cp, DateTime fechaRuta) = CodigoPostalConElDiaMasLejano();
            Assert.IsTrue(fechaRuta > DateTime.Today.AddDays(1), "con el cálculo viejo (hoy o mañana) saldría «con fechas futuras»");
            PedidoEnBd(925900, "16 ", cp, ((short)-1, fechaRuta));
            PedidoEnBd(925901, "16 ", cp, ((short)-1, fechaRuta.AddDays(1)));

            List<ResumenPedidoVentaDTO> lista = await controller.GetPedidosVenta("");

            bool detalleSuDia = FechasFuturasPedido.TieneFechasFuturas(new[] { Linea(fechaRuta) }, "16 ", cp);
            bool detalleDespues = FechasFuturasPedido.TieneFechasFuturas(new[] { Linea(fechaRuta.AddDays(1)) }, "16 ", cp);
            Assert.IsFalse(detalleSuDia);
            Assert.IsTrue(detalleDespues);
            Assert.AreEqual(lista.Single(p => p.numero == 925900).tieneFechasFuturas, detalleSuDia, "el pedido completo dice lo mismo que la lista");
            Assert.AreEqual(lista.Single(p => p.numero == 925901).tieneFechasFuturas, detalleDespues, "el pedido completo dice lo mismo que la lista");
        }

        [TestMethod]
        public void FechaEntregaFutura_CodigoPostalSinDiaOAgencia_ComoSiempre()
        {
            Assert.AreEqual(FechasFuturasPedido.FechaEntregaFutura("16", null), FechasFuturasPedido.FechaEntregaFutura("16", "28760"),
                "un CP sin día no cambia nada");
            Assert.AreEqual(FechasFuturasPedido.FechaEntregaFutura("FW", null), FechasFuturasPedido.FechaEntregaFutura("FW", "28001"),
                "un pedido de agencia no tiene día de ruta aunque su CP lo tenga");
        }

        [TestMethod]
        public void TieneFechasFuturasGrabado_ConLasLineasDeLaBd()
        {
            // Lo que devuelven POST y PUT: de las líneas grabadas (las de la BD), como la lista.
            PedidoEnBd(925910, "FW ", "28760", ((short)-1, FUTURO));
            PedidoEnBd(925911, "FW ", "28760", ((short)-1, PASADO), ((short)4, FUTURO));
            PedidoEnBd(925912, "FW ", "28760");

            Assert.AreEqual(true, controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925910, ruta = "FW " }, null));
            Assert.AreEqual(false, controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925911, ruta = "FW " }, null),
                "la futura está facturada");
            Assert.AreEqual(false, controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925912, ruta = "FW " }, null),
                "sin líneas pendientes");
        }

        [TestMethod]
        public void TieneFechasFuturasGrabado_RutaInternaSuDia_False()
        {
            (string cp, DateTime fechaRuta) = CodigoPostalConElDiaMasLejano();
            PedidoEnBd(925920, "16 ", cp, ((short)-1, fechaRuta));
            PedidoEnBd(925921, "16 ", cp, ((short)1, fechaRuta.AddDays(1)));

            Assert.AreEqual(false, controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925920, ruta = "16" }, cp));
            Assert.AreEqual(true, controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925921, ruta = "16" }, cp));
        }

        [TestMethod]
        public void TieneFechasFuturasGrabado_SiFallaLaBd_NullYNoLanza()
        {
            A.CallTo(() => db.LinPedidoVtas).Throws(new InvalidOperationException("sin BD"));

            Assert.IsNull(controller.TieneFechasFuturasGrabado(new PedidoVentaDTO { empresa = EMPRESA, numero = 925930, ruta = "FW" }, null));
        }
    }
}
