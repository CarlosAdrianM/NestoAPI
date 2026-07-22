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
        private DbSet<ParametroIVA> fakeParametrosIva;
        private DbSet<VerifactuRegistro> fakeRegistros;
        private List<VerifactuRegistro> registrosInsertados;
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
            // NestoAPI#347: país por código de IVA (vacío salvo que el test lo configure)
            fakeParametrosIva = A.Fake<DbSet<ParametroIVA>>(o => o.Implements<IQueryable<ParametroIVA>>().Implements<IDbAsyncEnumerable<ParametroIVA>>());
            A.CallTo(() => db.ParametrosIVA).Returns(fakeParametrosIva);
            ConfigurarFakeDbSet(fakeParametrosIva, new List<ParametroIVA>().AsQueryable());
            // NestoAPI#347: auditoría de registros declarados
            registrosInsertados = new List<VerifactuRegistro>();
            fakeRegistros = A.Fake<DbSet<VerifactuRegistro>>();
            A.CallTo(() => db.VerifactuRegistros).Returns(fakeRegistros);
            _ = A.CallTo(() => fakeRegistros.Add(A<VerifactuRegistro>.Ignored))
                .Invokes((VerifactuRegistro r) => registrosInsertados.Add(r));
            servicioVerifactu = A.Fake<IServicioVerifactu>();
            A.CallTo(() => servicioVerifactu.EstaHabilitado).Returns(true);
            logService = A.Fake<ILogService>();
            // NestoAPI#346: estado estático del deduplicador limpio entre tests
            DeduplicadorErroresVerifactu.Reset();
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
                // El flujo real de CrearFactura envía la factura recién emitida (fecha de hoy);
                // el enrutado create/modify por antigüedad (#346) se testea aparte
                Fecha = DateTime.Today,
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
                Fecha = DateTime.Today, // recién creada: va por create (#346)
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
        public async Task EnviarFacturaAVerifactu_SimplificadaPorEncimaDelLimiteLegal_AvisaPeroEnvia()
        {
            // #325: una simplificada (F2) de más de 400€ no puede documentarse como tal. No se
            // bloquea la facturación (la factura ya está emitida), pero tiene que saltar el aviso.
            var factura = ConfigurarFactura();
            factura.Nº_Cliente = "32624"; // Amazon -> F2
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = 500.00M, ImporteIVA = 105.00M }
            };
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-f2" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => logService.LogError(A<string>.That.Contains("supera el límite legal"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("uuid-f2", factura.VerifactuUUID, "El aviso no impide registrar la factura");
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_SimplificadaDentroDelLimite_NoAvisa()
        {
            // El caso normal: los importes reales de estos clientes rondan los 33€ de media
            var factura = ConfigurarFactura();
            factura.Nº_Cliente = "32624";
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-f2" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => logService.LogError(A<string>.That.Contains("límite legal"), A<Exception>.Ignored))
                .MustNotHaveHappened();
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
                Fecha = DateTime.Today, // recién facturada: va por create (#346)
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
            // NestoAPI#346: el fallo ya SÍ persiste (último error + registro de auditoría)
            StringAssert.Contains(factura.VerifactuUltimoError, "NIF incorrecto");
            Assert.IsNotNull(factura.VerifactuUltimoIntento);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
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

        #region Enrutado create/modify por antigüedad y deduplicación de ruido (#346)

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_FacturaDeHaceDosDias_VaPorSubsanacionConRechazoPrevioX()
        {
            // #346: el create de Verifacti exige fecha actual (tolera ayer). Una factura más
            // antigua sin declarar (NIF corregido tarde, caída del proveedor) va por el camino
            // legal de la subsanación: PUT modify con rechazo_previo=X.
            var factura = ConfigurarFactura();
            factura.Fecha = DateTime.Today.AddDays(-2);
            A.CallTo(() => servicioVerifactu.ModificarFacturaAsync(A<VerifactuFacturaRequest>.Ignored, "X"))
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-subsanada" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => servicioVerifactu.ModificarFacturaAsync(A<VerifactuFacturaRequest>.Ignored, "X"))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("uuid-subsanada", factura.VerifactuUUID);
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_FacturaDeAyer_SigueYendoPorCreate()
        {
            // Tolerancia observada del create de Verifacti: acepta la fecha de ayer
            var factura = ConfigurarFactura();
            factura.Fecha = DateTime.Today.AddDays(-1);
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioVerifactu.ModificarFacturaAsync(A<VerifactuFacturaRequest>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_MismoErrorRepetido_SoloLogueaLaPrimeraVez()
        {
            // #346: el job reintenta cada hora; sin dedup, una factura atascada metía ~24 errores
            // idénticos al día en ELMAH y enmascaraba el resto
            _ = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse { Exitoso = false, MensajeError = "NIF incorrecto", CodigoError = "400" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");
            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");
            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => logService.LogError(A<string>.That.Contains("NIF incorrecto"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_ClienteConPasaporteMarcado_DeclaraConIdOtroSinNif()
        {
            // NestoAPI#339: identificación extranjera marcada → IDOtro (tipo L7 + país) y sin
            // nif: la AEAT no valida IDOtro contra el censo y el rechazo desaparece.
            var factura = ConfigurarFactura();
            factura.CifNif = "AB123456"; // el pasaporte vive en el campo NIF de la ficha
            var validacion = A.Fake<NestoAPI.Infraestructure.Clientes.IServicioValidacionNif>();
            _ = A.CallTo(() => validacion.ValidarPrincipal(A<string>.Ignored, A<string>.Ignored))
                .Returns(new NestoAPI.Infraestructure.Clientes.ResultadoValidacionNif
                {
                    Estado = NestoAPI.Infraestructure.Clientes.EstadoValidacionNif.Extranjero,
                    TipoIdentificacion = "03",
                    Pais = "MA"
                });
            VerifactuFacturaRequest enviado = null;
            _ = A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Invokes((VerifactuFacturaRequest r) => enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-pasaporte" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService,
                almacenRectificativasPendientes: null, servicioValidacionNif: validacion);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.IsNull(enviado.NifDestinatario, "Con IDOtro no puede viajar nif");
            Assert.IsNotNull(enviado.IdOtro);
            Assert.AreEqual("03", enviado.IdOtro.IdType);
            Assert.AreEqual("MA", enviado.IdOtro.CodigoPais);
            Assert.AreEqual("AB123456", enviado.IdOtro.Id);
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_PaisPersistidoExtranjero_MandaSobreLaHeuristica()
        {
            // NestoAPI#347: G21 con 21% parece nacional, pero si ParámetrosIVA dice que el código
            // de la factura es de Bélgica, la factura entera es OSS (N2/17)
            var factura = ConfigurarFactura();
            factura.IVA = "B21";
            ConfigurarFakeDbSet(fakeParametrosIva, new List<ParametroIVA>
            {
                new ParametroIVA { Empresa = "1", IVA_Producto = "G21", IVA_Cliente_Prov = "B21", Pais = "BE" }
            }.AsQueryable());
            VerifactuFacturaRequest enviado = null;
            _ = A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Invokes((VerifactuFacturaRequest r) => enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-oss" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.AreEqual("N2", enviado.DesgloseIva.Single().CalificacionOperacion);
            Assert.AreEqual("17", enviado.DesgloseIva.Single().ClaveRegimen);
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_PaisPersistidoEspanol_DesactivaLaHeuristicaDeCodigo()
        {
            // Un código raro que la lista blanca no reconoce, pero con Pais='ES' en BD → nacional
            var factura = ConfigurarFactura();
            factura.IVA = "ZZ9";
            ConfigurarFakeDbSet(fakeParametrosIva, new List<ParametroIVA>
            {
                new ParametroIVA { Empresa = "1", IVA_Producto = "G21", IVA_Cliente_Prov = "ZZ9", Pais = "ES" }
            }.AsQueryable());
            VerifactuFacturaRequest enviado = null;
            _ = A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Invokes((VerifactuFacturaRequest r) => enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-nac" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.IsNull(enviado.DesgloseIva.Single().CalificacionOperacion);
            Assert.AreEqual(21M, enviado.DesgloseIva.Single().TipoIva);
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_CadaIntentoDejaRegistroDeAuditoria()
        {
            var factura = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-1", Estado = "Pendiente" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            Assert.AreEqual(1, registrosInsertados.Count);
            var registro = registrosInsertados.Single();
            Assert.AreEqual("Alta", registro.TipoRegistro);
            Assert.IsNull(registro.RechazoPrevio);
            Assert.IsTrue(registro.Exitoso);
            Assert.AreEqual("uuid-1", registro.RespuestaUuid);
            StringAssert.Contains(registro.Payload, "2600123", "El payload serializado identifica la factura");
            Assert.IsNull(factura.VerifactuUltimoError, "El éxito limpia el último error");
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_SubsanacionFueraDePlazo_SeAuditaComoTal()
        {
            var factura = ConfigurarFactura();
            factura.Fecha = DateTime.Today.AddDays(-3);
            A.CallTo(() => servicioVerifactu.ModificarFacturaAsync(A<VerifactuFacturaRequest>.Ignored, "X"))
                .Returns(new VerifactuResponse { Exitoso = false, CodigoError = "400", MensajeError = "nombre obligatorio" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            var registro = registrosInsertados.Single();
            Assert.AreEqual("Subsanacion", registro.TipoRegistro);
            Assert.AreEqual("X", registro.RechazoPrevio);
            Assert.IsFalse(registro.Exitoso);
            StringAssert.Contains(registro.RespuestaError, "nombre obligatorio");
        }

        [TestMethod]
        public async Task EnviarFacturaAVerifactu_ErrorDistinto_VuelveALoguear()
        {
            _ = ConfigurarFactura();
            A.CallTo(() => servicioVerifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>.Ignored))
                .Returns(new VerifactuResponse { Exitoso = false, MensajeError = "NIF incorrecto", CodigoError = "400" }).Once()
                .Then.Returns(new VerifactuResponse { Exitoso = false, MensajeError = "Verifacti caido", CodigoError = "500" });
            var servicio = new ServicioFacturas(db, servicioVerifactu, logService);

            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");
            await servicio.EnviarFacturaAVerifactu("1", "NV2600123");

            A.CallTo(() => logService.LogError(A<string>.That.Contains("NIF incorrecto"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => logService.LogError(A<string>.That.Contains("Verifacti caido"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        #endregion
    }
}
