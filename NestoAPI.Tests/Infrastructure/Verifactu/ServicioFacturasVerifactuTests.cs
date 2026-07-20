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
        private DbSet<LinFacturaVtaRectificacion> fakeRectificaciones;
        private IServicioVerifactu servicioVerifactu;
        private ILogService logService;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            A.CallTo(() => fakeFacturas.Include(A<string>.Ignored)).Returns(fakeFacturas);
            // Issue #36: vinculaciones de rectificativas (vacías salvo que el test las configure)
            fakeRectificaciones = A.Fake<DbSet<LinFacturaVtaRectificacion>>(o => o.Implements<IQueryable<LinFacturaVtaRectificacion>>().Implements<IDbAsyncEnumerable<LinFacturaVtaRectificacion>>());
            A.CallTo(() => db.LinFacturaVtaRectificaciones).Returns(fakeRectificaciones);
            ConfigurarFakeDbSet(fakeRectificaciones, new List<LinFacturaVtaRectificacion>().AsQueryable());
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
        public async Task EnviarFacturaAVerifactu_SerieRectificativa_NoEnviaDesdeCrearFactura()
        {
            // Issue #36: en CrearFactura las vinculaciones aún no están guardadas (GestorCopiaPedidos
            // las guarda después y llama a EnviarRectificativaAVerifactu): desde aquí NO se envía.
            _ = ConfigurarFactura(serie: "RV", numero: "RV2600001");
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "RV2600001");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarRectificativaAVerifactu_ConVinculaciones_EnviaConLasFacturasOriginales()
        {
            // Issue #36: rectificativa por diferencias (importes en negativo) con las facturas
            // originales identificadas desde LinFacturaVtaRectificacion.
            var rectificativa = new CabFacturaVta
            {
                Empresa = "1",
                Serie = "RV",
                Número = "RV2600001",
                Fecha = new DateTime(2026, 7, 20),
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = -100.00M, ImporteIVA = -21.00M }
                }
            };
            var original = new CabFacturaVta
            {
                Empresa = "1",
                Serie = "NV",
                Número = "NV2600123 ",
                Fecha = new DateTime(2026, 6, 1),
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta> { rectificativa, original }.AsQueryable());
            // Dos líneas vinculadas a la MISMA factura original → una sola factura rectificada (Distinct)
            ConfigurarFakeDbSet(fakeRectificaciones, new List<LinFacturaVtaRectificacion>
            {
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600001", NumeroLinea = 1, FacturaOriginalNumero = "NV2600123", FacturaOriginalLinea = 5, CantidadRectificada = 1 },
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600001", NumeroLinea = 2, FacturaOriginalNumero = "NV2600123", FacturaOriginalLinea = 6, CantidadRectificada = 2 }
            }.AsQueryable());
            VerifactuFacturaRequest enviado = null;
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Invokes((VerifactuFacturaRequest r) => enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-rect", Estado = "Correcto" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarRectificativaAVerifactu("1", "RV2600001");

            Assert.IsNotNull(enviado, "Debe enviar la rectificativa a Verifactu");
            Assert.AreEqual("R1", enviado.TipoFactura);
            Assert.AreEqual("I", enviado.TipoRectificacion);
            Assert.AreEqual(-121.00M, enviado.ImporteTotal);
            Assert.AreEqual(1, enviado.FacturasRectificadas.Count);
            Assert.AreEqual("NV", enviado.FacturasRectificadas[0].Serie);
            Assert.AreEqual("2600123", enviado.FacturasRectificadas[0].Numero);
            Assert.AreEqual(new DateTime(2026, 6, 1), enviado.FacturasRectificadas[0].FechaExpedicion);
            Assert.AreEqual("uuid-rect", rectificativa.VerifactuUUID);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task VincularRectificativaPendiente_ConPendientes_VinculaBorraYEnvia()
        {
            // Issue #87: rectificativa copiada SIN facturar automáticamente; al facturar a mano,
            // las pendientes se convierten en LinFacturaVtaRectificacion, se borran y se envía.
            var rectificativa = new CabFacturaVta
            {
                Empresa = "1",
                Serie = "RV",
                Número = "RV2600001",
                Fecha = new DateTime(2026, 7, 20),
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = -100.00M, ImporteIVA = -21.00M }
                }
            };
            var original = new CabFacturaVta { Empresa = "1", Serie = "NV", Número = "NV2600123", Fecha = new DateTime(2026, 6, 1), LinPedidoVtas = new List<LinPedidoVta>() };
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta> { rectificativa, original }.AsQueryable());
            // Las vinculaciones "ya guardadas" que verá EnviarRectificativaAVerifactu tras el SaveChanges
            ConfigurarFakeDbSet(fakeRectificaciones, new List<LinFacturaVtaRectificacion>
            {
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600001", NumeroLinea = 10, FacturaOriginalNumero = "NV2600123", FacturaOriginalLinea = 5, CantidadRectificada = 2 }
            }.AsQueryable());
            var fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 900001, Nº_Orden = 10, Nº_Factura = "RV2600001 " }
            }.AsQueryable());
            var almacen = A.Fake<NestoAPI.Infraestructure.Rectificativas.IAlmacenRectificativasPendientes>();
            _ = A.CallTo(() => almacen.LeerPendientes("1", 900001)).Returns(new List<NestoAPI.Models.Rectificativas.RectificativaPendienteDTO>
            {
                new NestoAPI.Models.Rectificativas.RectificativaPendienteDTO { NumeroLinea = 10, FacturaOriginalNumero = "NV2600123", FacturaOriginalLinea = 5, CantidadRectificada = 2 }
            });
            var vinculadas = new List<LinFacturaVtaRectificacion>();
            _ = A.CallTo(() => fakeRectificaciones.Add(A<LinFacturaVtaRectificacion>.Ignored))
                .Invokes((LinFacturaVtaRectificacion fila) => vinculadas.Add(fila));
            VerifactuFacturaRequest enviado = null;
            _ = A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Invokes((VerifactuFacturaRequest r) => enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-manual" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService, almacen);

            await servicio.VincularRectificativaPendiente("1", "RV2600001", 900001);

            Assert.AreEqual(1, vinculadas.Count, "Debe crear la vinculación en LinFacturaVtaRectificacion");
            Assert.AreEqual("NV2600123", vinculadas[0].FacturaOriginalNumero);
            Assert.AreEqual(10, vinculadas[0].NumeroLinea);
            A.CallTo(() => almacen.BorrarPendientes("1", 900001, A<List<int>>.That.Matches(l => l.Count == 1 && l[0] == 10)))
                .MustHaveHappenedOnceExactly();
            Assert.IsNotNull(enviado, "Con las vinculaciones en su sitio, la rectificativa debe enviarse a Verifactu");
            Assert.AreEqual("R1", enviado.TipoFactura);
        }

        [TestMethod]
        public async Task VincularRectificativaPendiente_SinPendientes_NoTocaNada()
        {
            // El caso masivo (facturas normales, rectificativas del flujo automático): ni consulta
            // líneas ni envía nada.
            _ = ConfigurarFactura();
            var almacen = A.Fake<NestoAPI.Infraestructure.Rectificativas.IAlmacenRectificativasPendientes>();
            _ = A.CallTo(() => almacen.LeerPendientes(A<string>.Ignored, A<int>.Ignored))
                .Returns(new List<NestoAPI.Models.Rectificativas.RectificativaPendienteDTO>());
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService, almacen);

            await servicio.VincularRectificativaPendiente("1", "NV2600123", 900001);

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task VincularRectificativaPendiente_SiElAlmacenFalla_NoRompeLaFacturacion()
        {
            // Best-effort: un fallo aquí (tabla sin desplegar, BD...) jamás puede tumbar CrearFactura
            _ = ConfigurarFactura();
            var almacen = A.Fake<NestoAPI.Infraestructure.Rectificativas.IAlmacenRectificativasPendientes>();
            _ = A.CallTo(() => almacen.LeerPendientes(A<string>.Ignored, A<int>.Ignored)).Throws(new Exception("tabla no existe"));
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService, almacen);

            await servicio.VincularRectificativaPendiente("1", "NV2600123", 900001); // no debe lanzar

            A.CallTo(() => logService.LogError(A<string>.That.Contains("900001"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarRectificativaAVerifactu_SinVinculaciones_NoEnviaYLoguea()
        {
            // Rectificativa creada fuera de CopiarFactura (alta manual, #38/#87): sin vinculaciones
            // no se puede identificar qué rectifica → no se envía (queda reintentable) y se loguea.
            _ = ConfigurarFactura(serie: "RV", numero: "RV2600001");
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarRectificativaAVerifactu("1", "RV2600001");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => logService.LogError(A<string>.That.Contains("RV2600001"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
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
