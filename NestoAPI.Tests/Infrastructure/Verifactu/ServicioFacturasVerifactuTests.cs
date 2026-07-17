using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// Tests del envío best-effort a Verifactu tras crear la factura (issue #34).
    /// El envío nunca debe romper la facturación: si Verifacti falla, la factura
    /// sigue creada y el error queda logueado.
    /// </summary>
    [TestClass]
    public class ServicioFacturasVerifactuTests
    {
        private NVEntities db;
        private DbSet<CabFacturaVta> fakeFacturas;
        private IServicioVerifactu servicioVerifactu;
        private ILogService logService;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            A.CallTo(() => fakeFacturas.Include(A<string>.Ignored)).Returns(fakeFacturas);
            servicioVerifactu = A.Fake<IServicioVerifactu>();
            A.CallTo(() => servicioVerifactu.EstaHabilitado).Returns(true);
            logService = A.Fake<ILogService>();
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private CabFacturaVta ConfigurarFactura(string serie = "NV", string numero = "NV2600123", string uuidYaEnviado = null)
        {
            var factura = new CabFacturaVta
            {
                Empresa = "1",
                Serie = serie,
                Número = numero,
                Fecha = new DateTime(2026, 7, 17),
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                VerifactuUUID = uuidYaEnviado,
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 100.00M, ImporteIVA = 21.00M }
                }
            };
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta> { factura }.AsQueryable());
            return factura;
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_FacturaSerieNV_EnviaYPersisteLaRespuesta()
        {
            var factura = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse
                {
                    Exitoso = true,
                    Uuid = "uuid-1",
                    Huella = "huella-1",
                    QrBase64 = "qr-1",
                    Url = "https://verifactu/qr-1",
                    Estado = "Correcto"
                });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.AreEqual("uuid-1", factura.VerifactuUUID);
            Assert.AreEqual("huella-1", factura.VerifactuHuella);
            Assert.AreEqual("qr-1", factura.VerifactuQR);
            Assert.AreEqual("https://verifactu/qr-1", factura.VerifactuURL);
            Assert.AreEqual("Correcto", factura.VerifactuEstado);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_SerieNoRegistrada_NoEnvia()
        {
            _ = ConfigurarFactura(serie: "GB", numero: "GB2600123");
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "GB2600123");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_ServicioDeshabilitado_NoEnvia()
        {
            _ = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EstaHabilitado).Returns(false);
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_SerieRectificativa_NoEnviaTodavia()
        {
            // Las rectificativas (R1/R3/R4) necesitan las facturas rectificadas: issue #36
            _ = ConfigurarFactura(serie: "RV", numero: "RV2600001");
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "RV2600001");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_YaEnviada_NoReenvia()
        {
            _ = ConfigurarFactura(uuidYaEnviado: "uuid-previo");
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_RespuestaConError_NoLanzaNiPersisteYLoguea()
        {
            var factura = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse
                {
                    Exitoso = false,
                    MensajeError = "NIF incorrecto",
                    CodigoError = "400"
                });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.IsNull(factura.VerifactuUUID);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            A.CallTo(() => logService.LogError(A<string>.That.Contains("NV2600123"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_ExcepcionInesperada_NoLanzaYLoguea()
        {
            _ = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Throws(new HttpRequestExceptionParaTest("boom"));
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123"); // no debe lanzar

            A.CallTo(() => logService.LogError(A<string>.That.Contains("NV2600123"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        private class HttpRequestExceptionParaTest : Exception
        {
            public HttpRequestExceptionParaTest(string mensaje) : base(mensaje) { }
        }
    }
}
