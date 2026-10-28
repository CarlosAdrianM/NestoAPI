using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: tests de <see cref="EstrategiaAgrupacionFinDeMes"/> y del filtro de fecha del motor.
    /// </summary>
    [TestClass]
    public class EstrategiaAgrupacionFinDeMesTests
    {
        private static readonly DateTime CORTE = new DateTime(2026, 10, 31);
        private static readonly DateTime EN_OCTUBRE = new DateTime(2026, 10, 15);
        private static readonly DateTime ULTIMO_DIA_TARDE = new DateTime(2026, 10, 31, 18, 30, 0);
        private static readonly DateTime EN_NOVIEMBRE = new DateTime(2026, 11, 2);

        private NVEntities db;
        private EstrategiaAgrupacionFinDeMes estrategia;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            estrategia = new EstrategiaAgrupacionFinDeMes(db);
            ConfigurarClientes();
        }

        [TestMethod]
        public void SeleccionarGrupos_VariosContactosDelMismoCliente_UnSoloGrupoPorCliente()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "31", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-1", "51", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, ULTIMO_DIA_TARDE)),
                Pedido(300, "CLI-1", "0", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(400, "CLI-2", "0", Linea(4, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            List<GrupoPedidosFinDeMes> grupos = estrategia.SeleccionarGrupos("1", CORTE).ToList();

            Assert.AreEqual(2, grupos.Count);
            GrupoPedidosFinDeMes cli1 = grupos.Single(g => g.Cliente == "CLI-1");
            CollectionAssert.AreEqual(new[] { 100, 200, 300 }, cli1.Pedidos.Select(p => p.Número).ToArray(),
                "todos los contactos del cliente en el mismo grupo (el albarán del último día por la tarde también entra)");
            Assert.AreEqual(1, grupos.Single(g => g.Cliente == "CLI-2").Pedidos.Count);
        }

        [TestMethod]
        public void SeleccionarGrupos_PedidosConPO_NoEntranNunca()
        {
            CabPedidoVta conPO = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            conPO.SuPedido = "PO00474329";
            ConfigurarCabPedidos(conPO,
                Pedido(200, "CLI-1", "5", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            List<GrupoPedidosFinDeMes> grupos = estrategia.SeleccionarGrupos("1", CORTE).ToList();

            Assert.AreEqual(1, grupos.Count);
            CollectionAssert.AreEqual(new[] { 200 }, grupos[0].Pedidos.Select(p => p.Número).ToArray(), "el del P.O. lo factura la agrupación por P.O.");
        }

        [TestMethod]
        public void SeleccionarGrupos_SoloAlbaranesPosterioresAlCorte_NoEntra()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE)),
                Pedido(200, "CLI-2", "0", Linea(2, Constantes.EstadosLineaVenta.PENDIENTE, null)));

            Assert.AreEqual(0, estrategia.SeleccionarGrupos("1", CORTE).Count());
        }

        [TestMethod]
        public void SeleccionarGrupos_YaAgrupadosOPeriodoNormalOFacturados_NoEntran()
        {
            CabPedidoVta agrupado = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            agrupado.Agrupada = true;
            CabPedidoVta normal = Pedido(200, "CLI-1", "0", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            normal.Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_NORMAL;
            CabPedidoVta facturado = Pedido(300, "CLI-1", "0", Linea(3, Constantes.EstadosLineaVenta.FACTURA, EN_OCTUBRE));
            ConfigurarCabPedidos(agrupado, normal, facturado);

            Assert.AreEqual(0, estrategia.SeleccionarGrupos("1", CORTE).Count());
        }

        [TestMethod]
        public void SeleccionarGrupos_ConClienteYContacto_SoloEse()
        {
            ConfigurarCabPedidos(
                Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-1", "5", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(300, "CLI-2", "0", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            List<GrupoPedidosFinDeMes> soloCliente = estrategia.SeleccionarGrupos("1", CORTE, "CLI-1").ToList();
            List<GrupoPedidosFinDeMes> soloContacto = estrategia.SeleccionarGrupos("1", CORTE, "CLI-1", "5").ToList();

            Assert.AreEqual(1, soloCliente.Count);
            Assert.AreEqual(2, soloCliente[0].Pedidos.Count);
            Assert.AreEqual(1, soloContacto.Count);
            Assert.AreEqual(200, soloContacto[0].Pedidos.Single().Número);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void SeleccionarGrupos_ContactoSinCliente_Lanza()
        {
            ConfigurarCabPedidos();
            _ = estrategia.SeleccionarGrupos("1", CORTE, null, "5").ToList();
        }

        [TestMethod]
        public void ElegirDestino_ElDelContactoPrincipal()
        {
            ConfigurarClientes(("CLI-1", "0"));
            GrupoPedidosFinDeMes grupo = Grupo(
                Pedido(100, "CLI-1", "31", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(200, "CLI-1", "0", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            CabPedidoVta destino = estrategia.ElegirDestino(grupo, CORTE);

            Assert.AreEqual(200, destino.Número);
            Assert.AreEqual("0", destino.Contacto);
        }

        [TestMethod]
        public void ElegirDestino_SinPedidoDelPrincipal_ElMasAntiguoConElContactoPrincipal()
        {
            ConfigurarClientes(("CLI-1", "0"));
            GrupoPedidosFinDeMes grupo = Grupo(
                Pedido(200, "CLI-1", "51", Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)),
                Pedido(100, "CLI-1", "31", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            CabPedidoVta destino = estrategia.ElegirDestino(grupo, CORTE);

            Assert.AreEqual(100, destino.Número);
            Assert.AreEqual("0", destino.Contacto, "el deudor de la factura es el cliente principal");
        }

        [TestMethod]
        public void ElegirDestino_ElPrincipalTieneAlbaranesDeNoviembre_EligeOtro()
        {
            ConfigurarClientes(("CLI-1", "0"));
            GrupoPedidosFinDeMes grupo = Grupo(
                Pedido(100, "CLI-1", "0",
                    Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                    Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE)),
                Pedido(200, "CLI-1", "5", Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE)));

            CabPedidoVta destino = estrategia.ElegirDestino(grupo, CORTE);

            Assert.AreEqual(200, destino.Número, "la factura del destino se llevaría el albarán de noviembre");
            Assert.AreEqual("0", destino.Contacto);
        }

        [TestMethod]
        public void ElegirDestino_TodosConAlbaranesPosteriores_Null()
        {
            ConfigurarClientes(("CLI-1", "0"));
            GrupoPedidosFinDeMes grupo = Grupo(
                Pedido(100, "CLI-1", "0",
                    Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                    Linea(2, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE)));

            Assert.IsNull(estrategia.ElegirDestino(grupo, CORTE));
        }

        [TestMethod]
        public void ElegirDestino_PendientesEnElDestino_NoImpiden()
        {
            ConfigurarClientes(("CLI-1", "0"));
            GrupoPedidosFinDeMes grupo = Grupo(
                Pedido(100, "CLI-1", "0",
                    Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE),
                    Linea(2, Constantes.EstadosLineaVenta.PENDIENTE, null)));

            Assert.AreEqual(100, estrategia.ElegirDestino(grupo, CORTE).Número);
        }

        [TestMethod]
        public void Motor_ConFechaTope_SoloMueveLosAlbaranesHastaElCorte()
        {
            ConfigurarDbSet(s => A.CallTo(() => db.PedidosEspeciales).Returns(s), new List<PedidosEspeciale>());
            ConfigurarDbSet(s => A.CallTo(() => db.Ubicaciones).Returns(s), new List<Ubicacion>());
            var motor = new MotorAgrupacionPedidos(db);
            CabPedidoVta destino = Pedido(100, "CLI-1", "0", Linea(1, Constantes.EstadosLineaVenta.ALBARAN, EN_OCTUBRE));
            CabPedidoVta origen = Pedido(200, "CLI-1", "5",
                Linea(2, Constantes.EstadosLineaVenta.ALBARAN, ULTIMO_DIA_TARDE),
                Linea(3, Constantes.EstadosLineaVenta.ALBARAN, EN_NOVIEMBRE),
                Linea(4, Constantes.EstadosLineaVenta.PENDIENTE, null));

            _ = motor.Agrupar(new[] { destino, origen }, destino, CORTE);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, destino.LinPedidoVtas.Select(l => l.Nº_Orden).ToArray());
            CollectionAssert.AreEquivalent(new[] { 3, 4 }, origen.LinPedidoVtas.Select(l => l.Nº_Orden).ToArray());
            Assert.IsTrue(destino.Agrupada);
        }

        // ----- helpers -----

        private GrupoPedidosFinDeMes Grupo(params CabPedidoVta[] pedidos)
        {
            return new GrupoPedidosFinDeMes { Empresa = "1", Cliente = "CLI-1", Pedidos = pedidos.ToList() };
        }

        private void ConfigurarCabPedidos(params CabPedidoVta[] pedidos)
        {
            ConfigurarDbSet(s => A.CallTo(() => db.CabPedidoVtas).Returns(s), pedidos.ToList());
        }

        private void ConfigurarClientes(params (string cliente, string contacto)[] principales)
        {
            List<Cliente> clientes = principales
                .Select(p => new Cliente { Empresa = "1", Nº_Cliente = p.cliente, Contacto = p.contacto, ClientePrincipal = true })
                .ToList();
            ConfigurarDbSet(s => A.CallTo(() => db.Clientes).Returns(s), clientes);
        }

        internal static CabPedidoVta Pedido(int numero, string cliente, string contacto, params LinPedidoVta[] lineas)
        {
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = numero,
                Nº_Cliente = cliente,
                Contacto = contacto,
                Periodo_Facturacion = Constantes.Pedidos.PERIODO_FACTURACION_FIN_DE_MES,
                IVA = "G21",
                Agrupada = false,
                LinPedidoVtas = lineas.ToList()
            };
            foreach (LinPedidoVta l in pedido.LinPedidoVtas)
            {
                l.Empresa = "1";
                l.Número = numero;
                l.Contacto = contacto;
                l.CabPedidoVta = pedido;
            }
            return pedido;
        }

        internal static LinPedidoVta Linea(int orden, int estado, DateTime? fechaAlbaran)
        {
            return new LinPedidoVta { Nº_Orden = orden, Estado = (short)estado, Fecha_Albarán = fechaAlbaran, VtoBueno = true };
        }

        internal static void ConfigurarDbSet<T>(Action<DbSet<T>> asignar, List<T> data) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>());
            IQueryable<T> query = data.AsQueryable();
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(query.Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(query.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(query.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => set.Add(A<T>._)).ReturnsLazily((T e) => { data.Add(e); return e; });
            asignar(set);
        }
    }
}
