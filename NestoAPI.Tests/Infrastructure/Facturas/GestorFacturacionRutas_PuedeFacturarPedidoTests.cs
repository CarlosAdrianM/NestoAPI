using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>
    /// Tests del gate <see cref="GestorFacturacionRutas.PuedeFacturarPedido"/>.
    /// Cubre la lógica MantenerJunto histórica y la extensión de NestoAPI#195 Fase 1
    /// que añade la condición de "todos los hermanos del mismo PO listos" cuando
    /// el pedido tiene <c>SuPedido</c> informado.
    /// </summary>
    [TestClass]
    public class GestorFacturacionRutas_PuedeFacturarPedidoTests
    {
        private NVEntities db;
        private DbSet<CabPedidoVta> fakeCabPedidos;
        private GestorFacturacionRutas gestor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();

            fakeCabPedidos = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCabPedidos);
            ConfigurarFakeDbSet(fakeCabPedidos, new List<CabPedidoVta>().AsQueryable());

            gestor = new GestorFacturacionRutas(
                db,
                A.Fake<IServicioAlbaranesVenta>(),
                A.Fake<IServicioFacturas>(),
                A.Fake<IGestorFacturas>(),
                A.Fake<IServicioTraspasoEmpresa>(),
                A.Fake<IServicioNotasEntrega>(),
                A.Fake<IServicioExtractoRuta>());
        }

        // --- Regresión del comportamiento previo (sin PO) ---

        [TestMethod]
        public void PuedeFacturarPedido_ClienteQueExigePO_SinSuPedido_Espera_YConElSi_723()
        {
            Func<bool> antes = NestoAPI.Infraestructure.Clientes.ReglaSuPedidoObligatorio.Interruptor;
            try
            {
                NestoAPI.Infraestructure.Clientes.ReglaSuPedidoObligatorio.Interruptor = () => true;
                var consultados = new List<string>();
                gestor.ClienteExigeSuPedido = (empresa, cliente) => { consultados.Add(cliente); return cliente == "CHANEL"; };
                var sinPo = NuevoPedido("1", 100, "CHANEL", mantenerJunto: false);
                Assert.IsFalse(gestor.PuedeFacturarPedido(sinPo), "espera el P.O.");
                Assert.IsTrue(gestor.EsperaSuPedido(sinPo));
                sinPo.SuPedido = "4500123";
                Assert.IsTrue(gestor.PuedeFacturarPedido(sinPo));
                var otro = NuevoPedido("1", 101, "OTRO", mantenerJunto: false);
                Assert.IsTrue(gestor.PuedeFacturarPedido(otro));
                CollectionAssert.AreEqual(new[] { "CHANEL", "CHANEL", "OTRO" }, consultados, "con P.O. informado no se consulta la marca del cliente");
            }
            finally
            {
                NestoAPI.Infraestructure.Clientes.ReglaSuPedidoObligatorio.Interruptor = antes;
            }
        }

        [TestMethod]
        public void PuedeFacturarPedido_NoMantenerJunto_DevuelveTrue()
        {
            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: false,
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoConLineasSinAlbaran_DevuelveFalse()
        {
            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                lineas: Lineas(Constantes.EstadosLineaVenta.EN_CURSO,
                               Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoTodasConAlbaran_DevuelveTrue()
        {
            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN,
                               Constantes.EstadosLineaVenta.FACTURA));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_MantenerJuntoSinLineas_DevuelveTrue()
        {
            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true);

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_SinPO_NoSeConsultanHermanos()
        {
            // Aunque haya hermanos hipotéticos en BD, sin PO no se aplica el gate de grupo.
            var hermanoConFalta = NuevoPedido("1", 999, "CLI-1", mantenerJunto: true,
                suPedido: "PO-X",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermanoConFalta }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: null,
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        // --- NestoAPI#195: gate por PO ---

        // NestoAPI#727 (Carlos, 28/10/26): con P.O. manda el grupo del P.O., tenga o no MantenerJunto

        [TestMethod]
        public void PuedeFacturarPedido_ConPO_SinMantenerJunto_HermanoSinAlbaran_DevuelveFalse_YConTodoEnAlbaranTrue()
        {
            var hermano = NuevoPedido("1", 200, "CLI-1", mantenerJunto: false, suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.EN_CURSO));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermano }.AsQueryable());
            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: false, suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido), "el hermano del mismo P.O. aún no está en albarán");

            hermano.LinPedidoVtas = Lineas(Constantes.EstadosLineaVenta.ALBARAN).ToList();
            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPO_SinMantenerJunto_LineasPropiasSinAlbaran_DevuelveFalse_YHermanoAgrupadoNoCuenta()
        {
            var agrupado = NuevoPedido("1", 200, "CLI-1", mantenerJunto: false, suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            agrupado.Agrupada = true;
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { agrupado }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: false, suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN, Constantes.EstadosLineaVenta.EN_CURSO));
            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido), "sus propias líneas no están todas en albarán");

            pedido.LinPedidoVtas = Lineas(Constantes.EstadosLineaVenta.ALBARAN).ToList();
            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido), "el hermano ya agrupado no bloquea");
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_HermanoConLineasSinAlbaran_DevuelveFalse()
        {
            var hermano = NuevoPedido("1", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.EN_CURSO));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermano }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN,
                               Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido),
                "Debe bloquear: aunque el propio pedido esté listo, un hermano del mismo PO sigue sin albarán.");
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_TodosHermanosEnAlbaran_DevuelveTrue()
        {
            var hermano = NuevoPedido("1", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN,
                               Constantes.EstadosLineaVenta.FACTURA));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermano }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOSinMantenerJunto_BloqueaPorHermanos()
        {
            // NestoAPI#727 (Carlos, 28/10/26): con P.O. manda el grupo del P.O., tenga o no MantenerJunto el pedido:
            // antes se facturaba solo y el P.O. acababa en varias facturas (o mezclado en el fin de mes).
            var hermano = NuevoPedido("1", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermano }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: false,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_HermanoSinMantenerJunto_TambienBloquea()
        {
            // NestoAPI#727: el hermano comparte P.O.: entra en el grupo tenga o no MantenerJunto.
            var hermano = NuevoPedido("1", 200, "CLI-1", mantenerJunto: false,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermano }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_HermanoEnOtroCliente_NoSeConsideraHermano()
        {
            // Mismo PO en cliente DISTINTO no es hermano (los POs son únicos por cliente).
            var ajeno = NuevoPedido("1", 200, "CLI-2", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { ajeno }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_HermanoEnOtraEmpresa_NoSeConsideraHermano()
        {
            // El PO se interpreta dentro de la misma empresa.
            var ajeno = NuevoPedido("3", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { ajeno }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsTrue(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_VariosHermanosUnoConFalta_DevuelveFalse()
        {
            var hermanoListo = NuevoPedido("1", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));
            var hermanoConFalta = NuevoPedido("1", 300, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos,
                new[] { hermanoListo, hermanoConFalta }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido));
        }

        [TestMethod]
        public void PuedeFacturarPedido_ConPOyMantenerJunto_HermanoConPeriodoFDM_NoSeIgnoraPorElPeriodo()
        {
            // PO prevalece sobre FDM: aunque el hermano sea FDM, sigue siendo hermano.
            // Si tiene líneas sin albarán, bloquea.
            var hermanoFDM = NuevoPedido("1", 200, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                periodoFacturacion: "FDM",
                lineas: Lineas(Constantes.EstadosLineaVenta.PENDIENTE));
            ConfigurarFakeDbSet(fakeCabPedidos, new[] { hermanoFDM }.AsQueryable());

            var pedido = NuevoPedido("1", 100, "CLI-1", mantenerJunto: true,
                suPedido: "PO-1",
                periodoFacturacion: "NRM",
                lineas: Lineas(Constantes.EstadosLineaVenta.ALBARAN));

            Assert.IsFalse(gestor.PuedeFacturarPedido(pedido));
        }

        // ----- helpers -----

        private static CabPedidoVta NuevoPedido(
            string empresa,
            int numero,
            string cliente,
            bool mantenerJunto,
            string suPedido = null,
            string periodoFacturacion = "NRM",
            params LinPedidoVta[] lineas)
        {
            return new CabPedidoVta
            {
                Empresa = empresa,
                Número = numero,
                Nº_Cliente = cliente,
                MantenerJunto = mantenerJunto,
                SuPedido = suPedido,
                Periodo_Facturacion = periodoFacturacion,
                LinPedidoVtas = lineas?.ToList() ?? new List<LinPedidoVta>()
            };
        }

        private static LinPedidoVta[] Lineas(params short[] estados)
        {
            return estados.Select(e => new LinPedidoVta
            {
                Estado = e,
                VtoBueno = true
            }).ToArray();
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> set, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IQueryable<T>)set).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<T>)set).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
