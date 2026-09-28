using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Sugerencia 396 de Novedades: datos de transferencia de un pedido prepago (IBAN y beneficiario
    /// del mismo sitio que el aviso de facturas vencidas, concepto con cliente y pedido, e importe).
    /// </summary>
    [TestClass]
    public class ServicioDatosTransferenciaPedidoTests
    {
        private const string IBAN = "ES91 2100 0418 4502 0005 1332";
        private const string TITULAR = "NUEVA VISION, S.A.";

        private NVEntities db;
        private DbSet<CabPedidoVta> fakeCabeceras;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<Banco> fakeBancos;
        private DbSet<Empresa> fakeEmpresas;
        private ILectorDatosPagoEmpresa lector;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeCabeceras = FakeDbSet<CabPedidoVta>();
            fakeLineas = FakeDbSet<LinPedidoVta>();
            fakeBancos = FakeDbSet<Banco>();
            fakeEmpresas = FakeDbSet<Empresa>();
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCabeceras);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.Bancos).Returns(fakeBancos);
            A.CallTo(() => db.Empresas).Returns(fakeEmpresas);
            ConfigurarFakeDbSet(fakeCabeceras, new List<CabPedidoVta>());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>());
            ConfigurarFakeDbSet(fakeBancos, new List<Banco>());
            ConfigurarFakeDbSet(fakeEmpresas, new List<Empresa>());

            lector = A.Fake<ILectorDatosPagoEmpresa>();
            A.CallTo(() => lector.Leer(A<string>._)).Returns(new DatosPagoAviso { Iban = IBAN, Titular = TITULAR });
        }

        [TestMethod]
        public async Task Leer_PedidoQueNoExiste_DevuelveNull()
        {
            var servicio = new ServicioDatosTransferenciaPedido(db, lector);

            Assert.IsNull(await servicio.Leer("1", 927160));
        }

        [TestMethod]
        public async Task Leer_PedidoPrepago_DevuelveIbanTitularConceptoEImporteDelPedido()
        {
            ConfigurarPedido("29606     ", 100.10M, 21.02M, 999M);
            var servicio = new ServicioDatosTransferenciaPedido(db, lector);

            DatosTransferenciaPedidoDTO datos = await servicio.Leer("1", 927160);

            Assert.AreEqual("29606", datos.Cliente);
            Assert.AreEqual(927160, datos.Pedido);
            Assert.AreEqual(IBAN, datos.Iban);
            Assert.AreEqual(TITULAR, datos.Titular);
            Assert.AreEqual("Cliente 29606 - Pedido 927160", datos.Concepto);
            Assert.AreEqual(121.12M, datos.Importe, "Las líneas con estado -99 no cuentan, como en el pedido de Nesto");
            A.CallTo(() => lector.Leer("1")).MustHaveHappened();
        }

        [TestMethod]
        public async Task Leer_TextoParaCopiar_UnaLineaPorDato()
        {
            ConfigurarPedido("29606", 1234.5M);
            var servicio = new ServicioDatosTransferenciaPedido(db, lector);

            DatosTransferenciaPedidoDTO datos = await servicio.Leer("1", 927160);

            Assert.AreEqual(
                "IBAN: " + IBAN + Environment.NewLine +
                "Beneficiario: " + TITULAR + Environment.NewLine +
                "Concepto: Cliente 29606 - Pedido 927160" + Environment.NewLine +
                "Importe: 1.234,50 €",
                datos.Texto);
        }

        [TestMethod]
        public void Texto_SinImporteNiTitular_NoSacaEsasLineas()
        {
            string texto = ServicioDatosTransferenciaPedido.Texto(new DatosTransferenciaPedidoDTO
            {
                Iban = IBAN,
                Concepto = ServicioDatosTransferenciaPedido.Concepto("29606", 927160),
                Importe = 0
            });

            Assert.AreEqual("IBAN: " + IBAN + Environment.NewLine + "Concepto: Cliente 29606 - Pedido 927160", texto);
        }

        [TestMethod]
        public async Task Leer_SinCuentaBancaria_IbanNull()
        {
            ConfigurarPedido("29606", 10M);
            A.CallTo(() => lector.Leer(A<string>._)).Returns(new DatosPagoAviso { Iban = "  ", Titular = TITULAR });
            var servicio = new ServicioDatosTransferenciaPedido(db, lector);

            DatosTransferenciaPedidoDTO datos = await servicio.Leer("1", 927160);

            Assert.IsNull(datos.Iban);
            Assert.IsFalse(datos.Texto.Contains("IBAN"));
        }

        [TestMethod]
        public void LectorDatosPagoEmpresa_LeeElIbanDeBancosYElTitularDeEmpresas()
        {
            ConfigurarFakeDbSet(fakeBancos, new List<Banco>
            {
                new Banco { Empresa = "1", Número = "1", Pais = "ES", DC_IBAN = "91", Entidad = "2100", Sucursal = "0418", DC = "45", Nº_Cuenta = "0200051332" }
            });
            ConfigurarFakeDbSet(fakeEmpresas, new List<Empresa>
            {
                new Empresa { Número = "1", Nombre = TITULAR + "     " }
            });

            DatosPagoAviso datos = new LectorDatosPagoEmpresa(db).Leer("1");

            Assert.AreEqual(IBAN, datos.Iban);
            Assert.AreEqual(TITULAR, datos.Titular);
        }

        private void ConfigurarPedido(string cliente, params decimal[] totalesLineas)
        {
            ConfigurarFakeDbSet(fakeCabeceras, new List<CabPedidoVta>
            {
                new CabPedidoVta { Empresa = "1", Número = 927160, Nº_Cliente = cliente, Forma_Pago = "TRN", PlazosPago = "PRE" }
            });
            List<LinPedidoVta> lineas = new List<LinPedidoVta>();
            for (int i = 0; i < totalesLineas.Length; i++)
            {
                lineas.Add(new LinPedidoVta
                {
                    Empresa = "1",
                    Número = 927160,
                    Nº_Orden = i + 1,
                    Total = totalesLineas[i],
                    // La tercera línea, si la hay, está anulada (-99) y no cuenta
                    Estado = (short)(i == 2 ? -99 : -1)
                });
            }
            lineas.Add(new LinPedidoVta { Empresa = "1", Número = 111111, Nº_Orden = 99, Total = 5000M, Estado = -1 }); // otro pedido
            ConfigurarFakeDbSet(fakeLineas, lineas);
        }

        private static DbSet<T> FakeDbSet<T>() where T : class
            => A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
