using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#898: LA REGLA de la P.O. al juntar pedidos (<see cref="ReglaPOAgrupacion"/>) y dónde se aplica: estrategias de
    /// agrupación por P.O. y de fin de mes, motor (última barrera) y «Unir pedidos». Caso de regresión: cliente 27593.
    /// </summary>
    [TestClass]
    public class ReglaPOAgrupacionTests
    {
        // ----- LA REGLA -----

        [TestMethod]
        public void Clave_SinRellenoNiMayusculas_VaciaEsSinPO()
        {
            Assert.AreEqual("PO00474329", ReglaPOAgrupacion.Clave("  po00474329   "));
            Assert.AreEqual(string.Empty, ReglaPOAgrupacion.Clave(null));
            Assert.AreEqual(string.Empty, ReglaPOAgrupacion.Clave("          "));
            Assert.IsFalse(ReglaPOAgrupacion.TienePO("   "));
            Assert.IsTrue(ReglaPOAgrupacion.TienePO("PO1"));
        }

        [TestMethod]
        public void MismaPO_ComparaSinRellenoNiMayusculas_YSinPOSoloConSinPO()
        {
            Assert.IsTrue(ReglaPOAgrupacion.MismaPO("PO00474329          ", "po00474329"));
            Assert.IsTrue(ReglaPOAgrupacion.MismaPO(null, "   "));
            Assert.IsFalse(ReglaPOAgrupacion.MismaPO("PO00474329", "PO00481139"));
            Assert.IsFalse(ReglaPOAgrupacion.MismaPO("PO00474329", null));
            Assert.IsFalse(ReglaPOAgrupacion.MismaPO("", "PO00474329"));
        }

        [TestMethod]
        public void MotivoNoSePuedenJuntar_MismaPO_Null()
        {
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenJuntar(new[]
            {
                Par(926287, "PO00474329"), Par(926280, "po00474329   "), Par(926275, "PO00474329")
            }));
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenJuntar(new[] { Par(1, null), Par(2, "  ") }));
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenJuntar(new[] { Par(1, "PO1") }));
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenJuntar(null));
        }

        [TestMethod]
        public void MotivoNoSePuedenJuntar_POsDistintas_NombraLosPedidosYSusPO()
        {
            string motivo = ReglaPOAgrupacion.MotivoNoSePuedenJuntar(new[]
            {
                Par(927198, "PO00481139"), Par(926287, "PO00474329"), Par(926280, "PO00474329")
            });

            Assert.AreEqual("No se pueden juntar en una misma factura: el pedido 926280 es de la P.O. PO00474329 y el 927198 es de la P.O. PO00481139. Cada P.O. va en su propia factura.", motivo);
        }

        [TestMethod]
        public void MotivoNoSePuedenJuntar_UnoConPOYOtroSin_NoSeJuntan()
        {
            string motivo = ReglaPOAgrupacion.MotivoNoSePuedenJuntar(new[] { Par(100, null), Par(200, "PO1") });

            StringAssert.Contains(motivo, "el pedido 100 no tiene P.O. y el 200 es de la P.O. PO1");
        }

        [TestMethod]
        public void MotivoNoSePuedenUnir_DosPedidosConPODistintas_LoDiceDeUsted()
        {
            string motivo = ReglaPOAgrupacion.MotivoNoSePuedenUnir(926287, "PO00474329", 927198, "PO00481139", ampliacionEsNueva: false);

            Assert.AreEqual("No se pueden unir: el pedido 926287 es de la P.O. PO00474329 y el 927198 es de la P.O. PO00481139. Cada P.O. va en su propia factura: póngales la misma P.O. si de verdad son del mismo pedido del cliente.", motivo);
        }

        [TestMethod]
        public void MotivoNoSePuedenUnir_PedidoExistenteSinPOConUnoConPO_NoSeUnen()
        {
            Assert.IsNotNull(ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, "PO1", 200, null, ampliacionEsNueva: false));
            Assert.IsNotNull(ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, null, 200, "PO1", ampliacionEsNueva: false));
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, "PO1 ", 200, "po1", ampliacionEsNueva: false));
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, null, 200, "", ampliacionEsNueva: false));
        }

        [TestMethod]
        public void MotivoNoSePuedenUnir_AmpliacionNuevaSinPO_SeUneComoSiempre_ConOtraPO_No()
        {
            Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, "PO1", 0, null, ampliacionEsNueva: true));
            string motivo = ReglaPOAgrupacion.MotivoNoSePuedenUnir(100, "PO1", 0, "PO2", ampliacionEsNueva: true);
            Assert.AreEqual("No se pueden unir: el pedido 100 es de la P.O. PO1 y lo que quiere añadir es de la P.O. PO2. Haga un pedido aparte para esa P.O.", motivo);
        }

        // ----- Caso de regresión 27593: tres P.O., tres facturas -----

        [TestMethod]
        public void Regresion27593_EstrategiaPO_TresPO_TresGrupos_SinMezclarNiPerderTiendas()
        {
            NVEntities db = A.Fake<NVEntities>();
            ConfigurarCabPedidos(db,
                Pedido(923793, "12", "PO00456528"),
                Pedido(923799, "14", "PO00456528          "),
                Pedido(923689, "47", "po00456528"),
                Pedido(926287, "31", "PO00474329"),
                Pedido(926280, "33", "PO00474329"),
                Pedido(926275, "35", "PO00474329 "),
                Pedido(927198, "51", "PO00481139"),
                // Fin de mes sin P.O. del mismo cliente: no entra en ninguno de los tres
                Pedido(926501, "0", null));
            var estrategia = new EstrategiaAgrupacionPO(db);

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").OrderBy(g => g.SuPedido).ToList();

            Assert.AreEqual(3, grupos.Count, "una factura por P.O.");
            CollectionAssert.AreEquivalent(new[] { 923689, 923793, 923799 }, grupos[0].Pedidos.Select(p => p.Número).ToList(),
                "la P.O. en minúsculas (Diagonal) es la misma P.O.: no se queda en otra factura");
            Assert.AreEqual("po00456528", grupos[0].SuPedido, "la del pedido de menor número, tal cual");
            CollectionAssert.AreEquivalent(new[] { 926275, 926280, 926287 }, grupos[1].Pedidos.Select(p => p.Número).ToList());
            CollectionAssert.AreEquivalent(new[] { 927198 }, grupos[2].Pedidos.Select(p => p.Número).ToList());
            foreach (GrupoPedidosPO grupo in grupos)
            {
                Assert.IsNull(ReglaPOAgrupacion.MotivoNoSePuedenJuntar(grupo.Pedidos.Select(p => Par(p.Número, p.SuPedido))));
            }
        }

        [TestMethod]
        public void Regresion27593_FinDeMes_NoMeteNingunPedidoConPO()
        {
            NVEntities db = A.Fake<NVEntities>();
            CabPedidoVta sinPO = Pedido(926501, "0", null, Linea(new DateTime(2026, 10, 15)));
            CabPedidoVta otroSinPO = Pedido(926502, "3", null, Linea(new DateTime(2026, 10, 16)));
            ConfigurarCabPedidos(db,
                Pedido(923793, "12", "PO00456528", Linea(new DateTime(2026, 10, 10))),
                Pedido(926287, "31", "PO00474329", Linea(new DateTime(2026, 10, 12))),
                Pedido(927198, "51", "PO00481139", Linea(new DateTime(2026, 10, 20))),
                sinPO, otroSinPO);
            var estrategia = new EstrategiaAgrupacionFinDeMes(db);

            List<GrupoPedidosFinDeMes> grupos = estrategia.SeleccionarGrupos("1", new DateTime(2026, 10, 31)).ToList();

            Assert.AreEqual(1, grupos.Count);
            CollectionAssert.AreEquivalent(new[] { 926501, 926502 }, grupos[0].Pedidos.Select(p => p.Número).ToList(),
                "solo los que no tienen P.O.; los de P.O. van por P.O.");
        }

        [TestMethod]
        public void Regresion27593_Motor_PedidosDeTresPO_NoMueveNada()
        {
            NVEntities db = A.Fake<NVEntities>();
            IPersistenciaMovimientoLineas persistencia = A.Fake<IPersistenciaMovimientoLineas>();
            var motor = new MotorAgrupacionPedidos(db, persistencia);
            CabPedidoVta destino = Pedido(923793, "0", "PO00456528", Linea(null));
            CabPedidoVta otra = Pedido(926287, "31", "PO00474329", Linea(null));
            CabPedidoVta tercera = Pedido(927198, "51", "PO00481139", Linea(null));

            var ex = Assert.ThrowsException<InvalidOperationException>(() => motor.Agrupar(new[] { destino, otra, tercera }, destino));

            StringAssert.Contains(ex.Message, "el pedido 923793 es de la P.O. PO00456528 y el 926287 es de la P.O. PO00474329");
            A.CallTo(persistencia).MustNotHaveHappened();
            Assert.IsFalse(destino.Agrupada);
            Assert.AreEqual(1, otra.LinPedidoVtas.Count, "nada se ha movido");
        }

        [TestMethod]
        public void Motor_MismaPOConRellenoYMinusculas_Agrupa_YConservaElContactoDeCadaLinea()
        {
            NVEntities db = A.Fake<NVEntities>();
            var motor = new MotorAgrupacionPedidos(db, A.Fake<IPersistenciaMovimientoLineas>());
            CabPedidoVta destino = Pedido(926275, "0", "PO00474329", Linea(null));
            CabPedidoVta origen = Pedido(926287, "31", "po00474329   ", Linea(null));
            origen.LinPedidoVtas.Single().Contacto = "31";

            motor.Agrupar(new[] { destino, origen }, destino);

            Assert.AreEqual(2, destino.LinPedidoVtas.Count);
            Assert.IsTrue(destino.LinPedidoVtas.Any(l => l.Contacto == "31"), "la línea sigue siendo de su tienda");
        }

        // ----- «Unir pedidos» -----

        [TestMethod]
        public void UnirPedidos_PODistintas_NestoBusinessExceptionConCodigo()
        {
            var original = new PedidoVentaDTO { empresa = "1", cliente = "27593", numero = 926287, suPedido = "PO00474329" };
            var ampliacion = new PedidoVentaDTO { empresa = "1", cliente = "27593", numero = 927198, suPedido = "PO00481139" };

            var ex = Assert.ThrowsException<NestoBusinessException>(() => GestorPedidosVenta.ComprobarPOAlUnir(original, ampliacion));

            Assert.AreEqual(ReglaPOAgrupacion.CODIGO_PO_DISTINTAS, ex.GetErrorCode());
            StringAssert.StartsWith(ex.Message, "No se pueden unir: el pedido 926287 es de la P.O. PO00474329 y el 927198 es de la P.O. PO00481139.");
        }

        [TestMethod]
        public void UnirPedidos_MismaPOOAmpliacionNuevaSinPO_NoLanza()
        {
            GestorPedidosVenta.ComprobarPOAlUnir(
                new PedidoVentaDTO { numero = 926287, suPedido = "PO00474329" },
                new PedidoVentaDTO { numero = 926280, suPedido = "po00474329 " });
            GestorPedidosVenta.ComprobarPOAlUnir(
                new PedidoVentaDTO { numero = 926287, suPedido = "PO00474329" },
                new PedidoVentaDTO { numero = 0, suPedido = null });
            GestorPedidosVenta.ComprobarPOAlUnir(
                new PedidoVentaDTO { numero = 100 },
                new PedidoVentaDTO { numero = 200 });
        }

        // ----- helpers -----

        private static KeyValuePair<int, string> Par(int numero, string po) => new KeyValuePair<int, string>(numero, po);

        private static CabPedidoVta Pedido(int numero, string contacto, string suPedido, params LinPedidoVta[] lineas)
        {
            if (lineas == null || lineas.Length == 0)
            {
                lineas = new[] { Linea(null) };
            }
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = numero,
                Nº_Cliente = "27593",
                Contacto = contacto,
                SuPedido = suPedido,
                IVA = "G21",
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                LinPedidoVtas = lineas.ToList()
            };
            int orden = numero * 10;
            foreach (LinPedidoVta l in pedido.LinPedidoVtas)
            {
                l.Empresa = "1";
                l.Número = numero;
                l.Nº_Orden = orden++;
                l.Contacto = l.Contacto ?? contacto;
                l.CabPedidoVta = pedido;
            }
            return pedido;
        }

        private static LinPedidoVta Linea(DateTime? fechaAlbaran)
        {
            return new LinPedidoVta
            {
                Estado = Constantes.EstadosLineaVenta.ALBARAN,
                Fecha_Albarán = fechaAlbaran ?? new DateTime(2026, 10, 15),
                VtoBueno = true
            };
        }

        private static void ConfigurarCabPedidos(NVEntities db, params CabPedidoVta[] pedidos)
        {
            DbSet<CabPedidoVta> set = ConfigurarFakeDbSet(pedidos.ToList());
            A.CallTo(() => db.CabPedidoVtas).Returns(set);
        }

        private static DbSet<T> ConfigurarFakeDbSet<T>(List<T> data) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            IQueryable<T> query = data.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(query.Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(query.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(query.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).Returns(query.GetEnumerator());
            return set;
        }
    }
}
