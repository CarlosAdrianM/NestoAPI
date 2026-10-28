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
    /// NestoAPI#195 (Fase 2): tests de <see cref="EstrategiaAgrupacionPO"/>.
    /// </summary>
    [TestClass]
    public class EstrategiaAgrupacionPOTests
    {
        private NVEntities db;
        private EstrategiaAgrupacionPO estrategia;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            estrategia = new EstrategiaAgrupacionPO(db);
        }

        [TestMethod]
        public void SeleccionarGrupos_PedidosCompletosEnAlbaran_LosIncluye()
        {
            ConfigurarCabPedidos(
                NuevoPedido("1", 100, "CLI-1", "0", "PO-1",
                    Linea(Constantes.EstadosLineaVenta.ALBARAN)),
                NuevoPedido("1", 200, "CLI-1", "5", "PO-1",
                    Linea(Constantes.EstadosLineaVenta.ALBARAN)));

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").ToList();

            Assert.AreEqual(1, grupos.Count);
            Assert.AreEqual("PO-1", grupos[0].SuPedido);
            Assert.AreEqual(2, grupos[0].Pedidos.Count);
        }

        [TestMethod]
        public void SeleccionarGrupos_AlgunPedidoSinAlbaran_NoIncluyeElGrupo()
        {
            ConfigurarCabPedidos(
                NuevoPedido("1", 100, "CLI-1", "0", "PO-1",
                    Linea(Constantes.EstadosLineaVenta.ALBARAN)),
                // Este hermano tiene una línea aún pendiente: el grupo NO está listo.
                NuevoPedido("1", 200, "CLI-1", "5", "PO-1",
                    Linea(Constantes.EstadosLineaVenta.PENDIENTE)));

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").ToList();

            Assert.AreEqual(0, grupos.Count);
        }

        // NestoAPI#727 (Carlos, 28/10/26): el P.O. es la unidad de facturación, tenga o no MantenerJunto el pedido

        [TestMethod]
        public void SeleccionarGrupos_SinMantenerJunto_ConPO_TambienSeAgrupa()
        {
            CabPedidoVta a = NuevoPedido("1", 100, "CLI-1", "31", "PO00474329", Linea(Constantes.EstadosLineaVenta.ALBARAN));
            CabPedidoVta b = NuevoPedido("1", 200, "CLI-1", "51", "PO00474329", Linea(Constantes.EstadosLineaVenta.ALBARAN));
            a.MantenerJunto = false; b.MantenerJunto = false;
            a.Periodo_Facturacion = "FDM"; b.Periodo_Facturacion = "FDM";
            ConfigurarCabPedidos(a, b);

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").ToList();

            Assert.AreEqual(1, grupos.Count, "contactos distintos y fin de mes: da igual, manda el P.O.");
            Assert.AreEqual(2, grupos[0].Pedidos.Count);
        }

        [TestMethod]
        public void SeleccionarGrupos_PedidoYaFacturadoDelTodo_NoEsCandidato_YDosPOsSonDosGrupos()
        {
            CabPedidoVta facturado = NuevoPedido("1", 100, "CLI-1", "0", "PO-VIEJO", Linea(Constantes.EstadosLineaVenta.FACTURA));
            facturado.MantenerJunto = false;
            ConfigurarCabPedidos(facturado,
                NuevoPedido("1", 200, "CLI-1", "0", "PO-A", Linea(Constantes.EstadosLineaVenta.ALBARAN)),
                NuevoPedido("1", 300, "CLI-1", "0", "PO-B", Linea(Constantes.EstadosLineaVenta.ALBARAN)));

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").OrderBy(g => g.SuPedido).ToList();

            Assert.AreEqual(2, grupos.Count, "PO-A y PO-B, nunca juntos; el ya facturado no vuelve");
            Assert.AreEqual("PO-A", grupos[0].SuPedido);
            Assert.AreEqual("PO-B", grupos[1].SuPedido);
        }

        [TestMethod]
        public void SeleccionarGrupos_PedidoAgrupado_NoSeVuelveAIncluir()
        {
            CabPedidoVta yaAgrupado = NuevoPedido("1", 100, "CLI-1", "0", "PO-1",
                Linea(Constantes.EstadosLineaVenta.ALBARAN));
            yaAgrupado.Agrupada = true;
            ConfigurarCabPedidos(yaAgrupado);

            List<GrupoPedidosPO> grupos = estrategia.SeleccionarGrupos("1").ToList();

            Assert.AreEqual(0, grupos.Count);
        }

        [TestMethod]
        public void ElegirDestino_TomaPedidoConContactoClientePrincipal()
        {
            ConfigurarClientePrincipal("1", "CLI-1", contactoPrincipal: "5");

            var grupo = new GrupoPedidosPO
            {
                Empresa = "1",
                Cliente = "CLI-1",
                SuPedido = "PO-1",
                Pedidos = new List<CabPedidoVta>
                {
                    NuevoPedido("1", 100, "CLI-1", "0", "PO-1", Linea(Constantes.EstadosLineaVenta.ALBARAN)),
                    NuevoPedido("1", 200, "CLI-1", "5", "PO-1", Linea(Constantes.EstadosLineaVenta.ALBARAN))
                }
            };

            CabPedidoVta destino = estrategia.ElegirDestino(grupo);

            // El contacto principal es el "5" → debe elegir el pedido 200 sin tocar su contacto.
            Assert.AreEqual(200, destino.Número);
            Assert.AreEqual("5", destino.Contacto);
        }

        [TestMethod]
        public void ElegirDestino_SinPrincipalEnGrupo_TomaMasAntiguoYAjustaContacto()
        {
            // El contacto principal del cliente ("9") no está en ningún pedido del grupo.
            ConfigurarClientePrincipal("1", "CLI-1", contactoPrincipal: "9");

            var grupo = new GrupoPedidosPO
            {
                Empresa = "1",
                Cliente = "CLI-1",
                SuPedido = "PO-1",
                Pedidos = new List<CabPedidoVta>
                {
                    NuevoPedido("1", 200, "CLI-1", "5", "PO-1", Linea(Constantes.EstadosLineaVenta.ALBARAN)),
                    NuevoPedido("1", 100, "CLI-1", "0", "PO-1", Linea(Constantes.EstadosLineaVenta.ALBARAN))
                }
            };

            CabPedidoVta destino = estrategia.ElegirDestino(grupo);

            // Toma el más antiguo (menor número = 100) y le pone el contacto principal "9".
            Assert.AreEqual(100, destino.Número);
            Assert.AreEqual("9", destino.Contacto);
        }

        // ----- helpers -----

        private void ConfigurarCabPedidos(params CabPedidoVta[] pedidos)
        {
            DbSet<CabPedidoVta> set = ConfigurarFakeDbSet(pedidos.ToList());
            A.CallTo(() => db.CabPedidoVtas).Returns(set);
        }

        private void ConfigurarClientePrincipal(string empresa, string cliente, string contactoPrincipal)
        {
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = empresa, Nº_Cliente = cliente, Contacto = contactoPrincipal, ClientePrincipal = true },
                new Cliente { Empresa = empresa, Nº_Cliente = cliente, Contacto = "0", ClientePrincipal = false }
            };
            DbSet<Cliente> set = ConfigurarFakeDbSet(clientes);
            A.CallTo(() => db.Clientes).Returns(set);
        }

        private static CabPedidoVta NuevoPedido(string empresa, int numero, string cliente,
            string contacto, string suPedido, params LinPedidoVta[] lineas)
        {
            return new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = cliente,
                Contacto = contacto,
                SuPedido = suPedido,
                MantenerJunto = true,
                LinPedidoVtas = lineas.ToList()
            };
        }

        private static LinPedidoVta Linea(short estado)
        {
            return new LinPedidoVta { Estado = estado, VtoBueno = true };
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
