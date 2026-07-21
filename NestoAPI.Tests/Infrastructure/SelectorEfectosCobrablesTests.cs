using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#332: selector de efectos cobrables (núcleo común + estrategia SEPA). Es el
    /// modo simulación de la remesa y donde vive el gating de entrega (#172) y la puerta de
    /// neteo — reglas que la remesa de tarjetas (#181) consumirá de aquí, no reimplementará.
    /// </summary>
    [TestClass]
    public class SelectorEfectosCobrablesTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 7, 21);

        private NVEntities db;
        private DbSet<ExtractoCliente> fakeExtractos;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<EnviosAgencia> fakeEnvios;
        private SelectorEfectosCobrables selector;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = A.Fake<DbSet<ExtractoCliente>>(o => o.Implements<IQueryable<ExtractoCliente>>().Implements<IDbAsyncEnumerable<ExtractoCliente>>());
            fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractos);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>().AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>().AsQueryable());
            selector = new SelectorEfectosCobrables(db);
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        // OJO gotcha de fakes (#313): el char() de BD lleva padding y el fake compara ordinal,
        // así que aquí los datos van SIN espacios de relleno.
        private static ExtractoCliente Efecto(int id = 1, string cliente = "15191", decimal pendiente = 250.50m,
            string ccc = "1", DateTime? fecha = null, DateTime? vencimiento = null, string documento = "NV2612000",
            string tipoApunte = "2")
        {
            return new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = id,
                Número = cliente,
                Contacto = "0",
                TipoApunte = tipoApunte,
                ImportePdte = pendiente,
                CCC = ccc,
                Fecha = fecha ?? HOY.AddDays(-5),
                FechaVto = vencimiento ?? HOY.AddDays(-1),
                Nº_Documento = documento
            };
        }

        [TestMethod]
        public async Task CandidatosSepa_CarteraVencidaConCcc_PreseleccionadaSinMotivo()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.IsTrue(candidatos.Single().Preseleccionado);
            Assert.IsNull(candidatos.Single().Motivo);
            Assert.IsFalse(candidatos.Single().ClienteConNegativos);
        }

        [TestMethod]
        public async Task CandidatosSepa_ElNucleoExcluyeLoQueNoEsCarteraVencidaPendiente()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, tipoApunte: "1"),                              // factura, no cartera
                Efecto(id: 2, pendiente: 0m),                                // nada pendiente
                Efecto(id: 3, vencimiento: HOY.AddDays(10)),                 // aún no vencido
                Efecto(id: 4, fecha: HOY),                                   // facturado hoy (margen)
                Efecto(id: 5)                                                // válido
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(5, candidatos.Single().Id);
        }

        [TestMethod]
        public async Task CandidatosSepa_EstrategiaSepa_SinCccQuedaFuera()
        {
            // La estrategia de tarjeta (#181) hará lo contrario: TAR + CCC vacío + token
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, ccc: null),
                Efecto(id: 2, ccc: ""),
                Efecto(id: 3, ccc: "1")
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(1, candidatos.Count);
            Assert.AreEqual(3, candidatos.Single().Id);
        }

        [TestMethod]
        public async Task CandidatosSepa_EnvioSinEntregar_RetenidoConMotivo()
        {
            // Gating #172: factura → línea → pedido → envío con Estado != ENTREGADO
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612001"),
                Efecto(id: 2, documento: "NV2612002")
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 922001, Estado = (short)Constantes.Agencias.ESTADO_TRAMITADO },
                new EnviosAgencia { Numero = 2, Pedido = 922002, Estado = Constantes.Agencias.ESTADO_ENTREGADO }
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            EfectoCandidatoDTO retenido = candidatos.Single(c => c.Id == 1);
            EfectoCandidatoDTO liberado = candidatos.Single(c => c.Id == 2);
            Assert.IsFalse(retenido.Preseleccionado);
            StringAssert.Contains(retenido.Motivo, "sin confirmar la entrega");
            Assert.IsTrue(liberado.Preseleccionado, "Con el envío ENTREGADO el efecto se libera");
        }

        [TestMethod]
        public async Task CandidatosSepa_FacturaSinEnvios_SeLibera()
        {
            // Mostrador/servicios: sin envíos de agencia se preserva la política actual
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() }.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922003, Nº_Factura = "NV2612000" }
            }.AsQueryable());
            // Sin envíos configurados

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.IsTrue(candidatos.Single().Preseleccionado);
        }

        [TestMethod]
        public async Task CandidatosSepa_ClienteConAbonosPendientes_MarcaLaPuertaDeNeteo()
        {
            // #332: el usuario debe pasar por la revisión (liquidar con #333) antes de remesar
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, cliente: "15191"),
                Efecto(id: 2, cliente: "30676"),
                // Abono pendiente del 15191 (no candidato por ser negativo, pero activa el flag)
                Efecto(id: 3, cliente: "15191", pendiente: -80m, tipoApunte: "1", ccc: null)
            }.AsQueryable());

            List<EfectoCandidatoDTO> candidatos = await selector.CandidatosSepa("1", HOY);

            Assert.AreEqual(2, candidatos.Count);
            Assert.IsTrue(candidatos.Single(c => c.Cliente == "15191").ClienteConNegativos);
            Assert.IsFalse(candidatos.Single(c => c.Cliente == "30676").ClienteConNegativos);
        }
    }
}
