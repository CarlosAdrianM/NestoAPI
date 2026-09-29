using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Verifactu
{
    /// <summary>
    /// NestoAPI#522: ventana de administración con las facturas que Verifactu todavía no da por buenas
    /// (sin registrar o incorrectas en la AEAT), su motivo y el reintento del envío.
    /// </summary>
    [TestClass]
    public class ServicioFacturasPendientesVerifactuTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 28);

        private NVEntities db;
        private DbSet<CabFacturaVta> fakeFacturas;
        private DbSet<Cliente> fakeClientes;
        private List<CabFacturaVta> reenviadas;
        private List<CabFacturaVta> subsanadas;
        private VerifactuResponse respuesta;
        private ServicioFacturasPendientesVerifactu servicio;
        private DateTime fechaInicioOriginal;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>().AsQueryable());
            reenviadas = new List<CabFacturaVta>();
            subsanadas = new List<CabFacturaVta>();
            respuesta = new VerifactuResponse { Exitoso = true, Uuid = "uuid-nuevo", Estado = "Pendiente" };
            servicio = new ServicioFacturasPendientesVerifactu(db,
                f =>
                {
                    reenviadas.Add(f);
                    if (respuesta.Exitoso)
                    {
                        f.VerifactuUUID = respuesta.Uuid;
                        f.VerifactuEstado = respuesta.Estado;
                    }
                    return Task.FromResult(respuesta);
                },
                f =>
                {
                    subsanadas.Add(f);
                    if (respuesta.Exitoso)
                    {
                        f.VerifactuUUID = respuesta.Uuid;
                        f.VerifactuEstado = respuesta.Estado;
                    }
                    return Task.FromResult(respuesta);
                },
                () => HOY);
            fechaInicioOriginal = VerifactuJobsService.FechaInicioDeclaracion;
            VerifactuJobsService.FechaInicioDeclaracion = new DateTime(2026, 7, 20);
        }

        [TestCleanup]
        public void Cleanup()
        {
            VerifactuJobsService.FechaInicioDeclaracion = fechaInicioOriginal;
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        private void ConFacturas(params CabFacturaVta[] facturas)
        {
            ConfigurarFakeDbSet(fakeFacturas, facturas.AsQueryable());
        }

        private static CabFacturaVta Factura(string numero, string serie = "NV", string uuid = null, string estado = null,
            DateTime? fecha = null, string error = null, bool? incidencia = null, string nombreFiscal = "CLIENTE DE PRUEBA SL")
        {
            return new CabFacturaVta
            {
                Empresa = "1",
                Número = numero,
                Serie = serie,
                Nº_Cliente = "30676",
                Contacto = "0",
                Fecha = fecha ?? new DateTime(2026, 9, 25),
                VerifactuUUID = uuid,
                VerifactuEstado = estado,
                VerifactuUltimoError = error,
                VerifactuIncidencia = incidencia,
                NombreFiscal = nombreFiscal
            };
        }

        #region Listar

        [TestMethod]
        public async Task Listar_SoloSinRegistrarOIncorrectas_DeSeriesQueTramitanYDesdeElArranque()
        {
            ConFacturas(
                Factura("NV2615001"),                                                   // sin enviar
                Factura("NV2615002", uuid: "u-2", estado: "Incorrecto", error: "AEAT (Incorrecto): NIF"),
                Factura("NV2615003", uuid: "u-3", estado: "Correcto"),                  // registrada
                Factura("NV2615004", uuid: "u-4", estado: "Pendiente"),                 // registrada, AEAT confirmando
                Factura("GB2601537", serie: "GB"),                                      // no tramita
                Factura("NV2500001", fecha: new DateTime(2026, 1, 15)));                // histórico

            List<FacturaPendienteVerifactuDTO> lista = await servicio.Listar();

            CollectionAssert.AreEquivalent(new[] { "NV2615001", "NV2615002" }, lista.Select(f => f.Numero).ToList());
        }

        [TestMethod]
        public async Task Listar_DescartadaAMano_NoSale()
        {
            // NestoAPI#551: CV2600484/485, del camino viejo y anteriores a la obligación
            ConFacturas(
                Factura("CV2600484", serie: "CV", nombreFiscal: null, fecha: new DateTime(2026, 7, 20),
                    estado: VerifactuJobsService.ESTADO_DESCARTADA),
                Factura("CV2600485", serie: "CV", nombreFiscal: null, fecha: new DateTime(2026, 7, 20),
                    estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES));

            List<FacturaPendienteVerifactuDTO> lista = await servicio.Listar();

            CollectionAssert.AreEquivalent(new[] { "CV2600485" }, lista.Select(f => f.Numero).ToList());
        }

        [TestMethod]
        public async Task Listar_CadaFacturaDiceSuSituacionSuMotivoYSiSePuedeReintentar()
        {
            ConFacturas(
                Factura("NV2615001"),
                Factura("NV2615002", error: "(400) El campo nif no tiene un formato válido"),
                Factura("NV2615003", incidencia: true, fecha: HOY.AddDays(-1), error: "(TIMEOUT) Timeout"),
                Factura("NV2615004", incidencia: true, fecha: HOY.AddDays(-2), error: "(TIMEOUT) Timeout"),
                Factura("CV2600484", serie: "CV", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, nombreFiscal: null,
                    error: "Factura de camino externo a la API (#348) sin datos fiscales"),
                Factura("NV2615006", uuid: "u-6", estado: "Incorrecto", error: "AEAT (Incorrecto): 4104 El NIF no está identificado"));

            Dictionary<string, FacturaPendienteVerifactuDTO> lista = (await servicio.Listar()).ToDictionary(f => f.Numero);

            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE, lista["NV2615001"].Situacion);
            Assert.IsTrue(lista["NV2615001"].PuedeReintentar);
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_RECHAZADA_VERIFACTI, lista["NV2615002"].Situacion);
            StringAssert.Contains(lista["NV2615002"].Motivo, "formato válido");
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_INCIDENCIA, lista["NV2615003"].Situacion,
                "Incidencia de ayer: Verifacti todavía admite el create");
            Assert.IsTrue(lista["NV2615003"].PuedeReintentar);
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_INCIDENCIA_ANTIGUA, lista["NV2615004"].Situacion);
            Assert.IsFalse(lista["NV2615004"].PuedeReintentar, "No hay camino en Verifacti: se revisa a mano");
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_SIN_DATOS_FISCALES, lista["CV2600484"].Situacion);
            Assert.IsFalse(lista["CV2600484"].PuedeReintentar);
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_INCORRECTA_AEAT, lista["NV2615006"].Situacion);
            Assert.AreEqual("Incorrecto", lista["NV2615006"].Estado);
            StringAssert.Contains(lista["NV2615006"].Motivo, "4104");
            Assert.IsTrue(lista["NV2615006"].PuedeReintentar);
            Assert.IsTrue(lista.Values.All(f => !string.IsNullOrWhiteSpace(f.QueHacer)), "Siempre se dice qué hacer");
        }

        [TestMethod]
        public async Task Listar_SinDatosFiscales_TomaElNombreDeLaFicha()
        {
            ConFacturas(Factura("CV2600484", serie: "CV", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, nombreFiscal: null));
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>
            {
                new Cliente { Empresa = "1  ", Nº_Cliente = "30676", Contacto = "0  ", Nombre = "PELUQUERÍA DE PRUEBA   " }
            }.AsQueryable());

            FacturaPendienteVerifactuDTO factura = (await servicio.Listar()).Single();

            Assert.AreEqual("PELUQUERÍA DE PRUEBA", factura.Nombre);
        }

        #endregion

        #region Reintentar

        [TestMethod]
        public async Task Reintentar_SinRegistrar_ReenviaComoElJobYDejaDeEstarPendiente()
        {
            ConFacturas(Factura("NV2615001", error: "(400) El campo nif no tiene un formato válido"));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615001");

            Assert.AreEqual(1, reenviadas.Count);
            Assert.AreEqual(0, subsanadas.Count);
            Assert.IsTrue(resultado.Exitoso);
            Assert.IsNull(resultado.Factura, "Ya registrada: sale de la lista");
        }

        [TestMethod]
        public async Task Reintentar_ClienteConJustificanteProvisional_AvisaDeQueLeLlegaraLaDefinitiva()
        {
            // NestoAPI#522 (parte 1): la definitiva la manda el job en su siguiente pasada
            CabFacturaVta factura = Factura("NV2615001", incidencia: true, fecha: HOY);
            factura.VerifactuEnviadaProvisional = true;
            ConFacturas(factura);

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615001");

            Assert.IsTrue(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "factura definitiva");
        }

        [TestMethod]
        public async Task Reintentar_SinJustificanteProvisional_NoHablaDeLaDefinitiva()
        {
            ConFacturas(Factura("NV2615001"));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615001");

            Assert.IsTrue(resultado.Exitoso);
            Assert.IsFalse(resultado.Mensaje.Contains("definitiva"));
        }

        [TestMethod]
        public async Task Reintentar_IncorrectaEnLaAeat_VaPorLaSubsanacion()
        {
            ConFacturas(Factura("NV2615006", uuid: "u-6", estado: "Incorrecto", error: "AEAT (Incorrecto): NIF"));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615006");

            Assert.AreEqual(0, reenviadas.Count);
            Assert.AreEqual(1, subsanadas.Count);
            Assert.IsTrue(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "subsanación");
        }

        [TestMethod]
        public async Task Reintentar_VerifactuLaVuelveARechazar_DevuelveElMotivoYLaFacturaSigueEnLaLista()
        {
            ConFacturas(Factura("NV2615001"));
            respuesta = new VerifactuResponse { Exitoso = false, CodigoError = "400", MensajeError = "El campo nombre es obligatorio" };

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615001");

            Assert.IsFalse(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "El campo nombre es obligatorio");
            Assert.IsNotNull(resultado.Factura);
        }

        [TestMethod]
        public async Task Reintentar_IncidenciaDeHaceMasDeUnDia_NoSeEnviaYDiceQueHacer()
        {
            ConFacturas(Factura("NV2615004", incidencia: true, fecha: HOY.AddDays(-2)));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615004");

            Assert.AreEqual(0, reenviadas.Count + subsanadas.Count);
            Assert.IsFalse(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "más de un día");
        }

        [TestMethod]
        public async Task Reintentar_YaRegistrada_NoReenviaNada()
        {
            ConFacturas(Factura("NV2615003", uuid: "u-3", estado: "Correcto"));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "NV2615003");

            Assert.AreEqual(0, reenviadas.Count + subsanadas.Count);
            Assert.IsTrue(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "ya está registrada");
        }

        [TestMethod]
        public async Task Reintentar_SerieQueNoTramita_NoReenvia()
        {
            ConFacturas(Factura("GB2601537", serie: "GB"));

            ResultadoReintentoVerifactuDTO resultado = await servicio.Reintentar("1", "GB2601537");

            Assert.AreEqual(0, reenviadas.Count + subsanadas.Count);
            Assert.IsFalse(resultado.Exitoso);
        }

        [TestMethod]
        public async Task Reintentar_NoExiste_DevuelveNull()
        {
            ConFacturas();

            Assert.IsNull(await servicio.Reintentar("1", "NV9999999"));
        }

        #endregion

        #region Controlador: solo Administración, Dirección e Informática

        [TestMethod]
        public async Task Controlador_SinGrupoDeAdministracion_Prohibido()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Vendedor"), new[] { "NUEVAVISION\\Almacén" })
            };

            var resultado = await controller.GetFacturasPendientes() as StatusCodeResult;
            var reintento = await controller.ReintentarFactura(new ReintentarFacturaVerifactuDTO { Empresa = "1", Numero = "NV2615001" }) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, reintento?.StatusCode);
            A.CallTo(() => fake.Reintentar(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_Administracion_VeLaListaYReintenta()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.Listar()).Returns(new List<FacturaPendienteVerifactuDTO> { new FacturaPendienteVerifactuDTO { Numero = "NV2615001" } });
            A.CallTo(() => fake.Reintentar("1", "NV2615001")).Returns(new ResultadoReintentoVerifactuDTO { Exitoso = true });
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Laura"), new[] { "NUEVAVISION\\Administración" })
            };

            var lista = await controller.GetFacturasPendientes() as OkNegotiatedContentResult<List<FacturaPendienteVerifactuDTO>>;
            var reintento = await controller.ReintentarFactura(new ReintentarFacturaVerifactuDTO { Empresa = "1", Numero = "NV2615001" })
                as OkNegotiatedContentResult<ResultadoReintentoVerifactuDTO>;

            Assert.AreEqual("NV2615001", lista.Content.Single().Numero);
            Assert.IsTrue(reintento.Content.Exitoso);
        }

        [TestMethod]
        public async Task Controlador_ReintentarFacturaQueNoExiste_NotFound()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.Reintentar(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<ResultadoReintentoVerifactuDTO>(null));
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            var resultado = await controller.ReintentarFactura(new ReintentarFacturaVerifactuDTO { Empresa = "1", Numero = "NV9999999" });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #endregion

        #region Declarar como simplificada por NIF inconseguible (#392)

        private const string ERROR_NIF_RELLENO = "Marcada como NO CENSADO (07) pero el NIF '1000000' no tiene un formato válido " +
            "de NIF: la AEAT lo rechaza. Conseguir el NIF real del cliente y corregirlo (la reabre). Excluida de los reintentos.";

        private List<Modificacion> modificaciones;

        private void ConFacturasYRectificaciones(IEnumerable<CabFacturaVta> facturas, params LinFacturaVtaRectificacion[] vinculaciones)
        {
            ConFacturas(facturas.ToArray());
            // Include(...) sobre el fake: que devuelva el mismo DbSet para que la consulta funcione
            A.CallTo(() => ((DbQuery<CabFacturaVta>)fakeFacturas).Include(A<string>._)).Returns(fakeFacturas);
            var fakeVinculaciones = A.Fake<DbSet<LinFacturaVtaRectificacion>>(o => o.Implements<IQueryable<LinFacturaVtaRectificacion>>()
                .Implements<IDbAsyncEnumerable<LinFacturaVtaRectificacion>>());
            ConfigurarFakeDbSet(fakeVinculaciones, vinculaciones.AsQueryable());
            A.CallTo(() => db.LinFacturaVtaRectificaciones).Returns(fakeVinculaciones);
            modificaciones = new List<Modificacion>();
            var fakeModificaciones = A.Fake<DbSet<Modificacion>>();
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
        }

        private static CabFacturaVta ConImporte(CabFacturaVta factura, decimal baseImponible)
        {
            factura.CifNif = "1000000";
            factura.LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { PorcentajeIVA = 21, PorcentajeRE = 0, Base_Imponible = baseImponible, ImporteIVA = baseImponible * 0.21M }
            };
            return factura;
        }

        [TestMethod]
        public async Task DeclararSimplificada_FacturaExcluidaPorNifDentroDelLimite_LaMarcaLaReabreYAudita()
        {
            CabFacturaVta factura = ConImporte(Factura("NV2615001", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, error: ERROR_NIF_RELLENO), 300M);
            ConFacturasYRectificaciones(new[] { factura });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "Cliente de paso, no da el DNI", "NUEVAVISION\\Laura");

            Assert.IsTrue(resultado.Exitoso, resultado.Mensaje);
            Assert.IsTrue(factura.VerifactuDeclararSimplificada == true);
            Assert.IsNull(factura.VerifactuEstado, "Sale de la exclusión del job: ya se puede declarar");
            Assert.IsNull(factura.VerifactuUltimoError);
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_PENDIENTE, resultado.Factura.Situacion);
            Assert.IsTrue(resultado.Factura.DeclararSimplificada);
            Assert.AreEqual(0, reenviadas.Count, "No se envía aquí: la manda el job o el botón Reintentar");
            Modificacion auditoria = modificaciones.Single();
            Assert.AreEqual("CabFacturaVta", auditoria.Tabla);
            StringAssert.Contains(auditoria.Nuevo, "Cliente de paso, no da el DNI");
            StringAssert.Contains(auditoria.Nuevo, "NUEVAVISION\\Laura");
            StringAssert.Contains(auditoria.Anterior, "1000000");
            StringAssert.Contains(auditoria.Anterior, "no tiene un formato válido");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task DeclararSimplificada_PorEncimaDelLimiteLegal_NoLaMarcaYDiceQueHayQueConseguirElNif()
        {
            // 400 € de base + IVA = 484 €: no cabe en una simplificada (límite 400 €, #325)
            CabFacturaVta factura = ConImporte(Factura("NV2615001", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, error: ERROR_NIF_RELLENO), 400M);
            ConFacturasYRectificaciones(new[] { factura });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "No da el DNI", "NUEVAVISION\\Laura");

            Assert.IsFalse(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "No hay salida sin el NIF");
            Assert.IsNull(factura.VerifactuDeclararSimplificada);
            Assert.AreEqual(VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, factura.VerifactuEstado);
            Assert.AreEqual(0, modificaciones.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task DeclararSimplificada_JustoEnElLimite_SePermite()
        {
            // 330,58 + 21 % = 400,00 €
            CabFacturaVta factura = ConImporte(Factura("NV2615001", error: ERROR_NIF_RELLENO), 330.58M);
            ConFacturasYRectificaciones(new[] { factura });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "No da el DNI", "u");

            Assert.IsTrue(resultado.Exitoso, resultado.Mensaje);
        }

        [TestMethod]
        public async Task DeclararSimplificada_SusRectificativasHeredanLaMarcaYSalenDeLaExclusion()
        {
            CabFacturaVta factura = ConImporte(Factura("NV2615001", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, error: ERROR_NIF_RELLENO), 100M);
            CabFacturaVta rectificativa = ConImporte(Factura("RV2600010", serie: "RV", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, error: ERROR_NIF_RELLENO), -100M);
            CabFacturaVta otraRectificativa = Factura("RV2600011", serie: "RV");
            ConFacturasYRectificaciones(new[] { factura, rectificativa, otraRectificativa },
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600010", NumeroLinea = 1, FacturaOriginalNumero = "NV2615001" },
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600010", NumeroLinea = 2, FacturaOriginalNumero = "NV2615001" },
                new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV2600011", NumeroLinea = 1, FacturaOriginalNumero = "NV2615999" });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "No da el DNI", "u");

            Assert.IsTrue(resultado.Exitoso, resultado.Mensaje);
            Assert.IsTrue(rectificativa.VerifactuDeclararSimplificada == true);
            Assert.IsNull(rectificativa.VerifactuEstado);
            Assert.IsNull(otraRectificativa.VerifactuDeclararSimplificada, "Rectifica otra factura: no hereda nada");
            StringAssert.Contains(resultado.Mensaje, "RV2600010");
            StringAssert.Contains(modificaciones.Single().Nuevo, "RV2600010");
        }

        [TestMethod]
        public async Task DeclararSimplificada_Rectificativa_NoSeMarcaDirectamente()
        {
            CabFacturaVta rectificativa = ConImporte(Factura("RV2600010", serie: "RV", error: ERROR_NIF_RELLENO), -100M);
            ConFacturasYRectificaciones(new[] { rectificativa });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "RV2600010", "No da el DNI", "u");

            Assert.IsFalse(resultado.Exitoso);
            StringAssert.Contains(resultado.Mensaje, "hereda");
            Assert.IsNull(rectificativa.VerifactuDeclararSimplificada);
        }

        [TestMethod]
        public async Task DeclararSimplificada_YaRegistrada_NoSePuede()
        {
            CabFacturaVta factura = ConImporte(Factura("NV2615001", uuid: "u-1", estado: "Correcto"), 100M);
            ConFacturasYRectificaciones(new[] { factura });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "No da el DNI", "u");

            Assert.IsFalse(resultado.Exitoso);
            Assert.IsNull(factura.VerifactuDeclararSimplificada);
        }

        [TestMethod]
        public async Task DeclararSimplificada_SinMotivo_NoSePuede()
        {
            CabFacturaVta factura = ConImporte(Factura("NV2615001", error: ERROR_NIF_RELLENO), 100M);
            ConFacturasYRectificaciones(new[] { factura });

            ResultadoReintentoVerifactuDTO resultado = await servicio.DeclararSimplificada("1", "NV2615001", "   ", "u");

            Assert.IsFalse(resultado.Exitoso);
            Assert.IsNull(factura.VerifactuDeclararSimplificada);
        }

        [TestMethod]
        public async Task DeclararSimplificada_NoExiste_DevuelveNull()
        {
            ConFacturasYRectificaciones(new CabFacturaVta[0]);

            Assert.IsNull(await servicio.DeclararSimplificada("1", "NV9999999", "motivo", "u"));
        }

        [TestMethod]
        public void Mapear_PuedeDeclararSimplificada_SoloSiElProblemaEsElNif()
        {
            Assert.IsTrue(servicio.Mapear(Factura("NV1", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES, error: ERROR_NIF_RELLENO), null).PuedeDeclararSimplificada);
            Assert.IsTrue(servicio.Mapear(Factura("NV2", uuid: "u", estado: "Incorrecto", error: "AEAT (Incorrecto): 4104 El NIF no está identificado"), null).PuedeDeclararSimplificada);
            Assert.IsTrue(servicio.Mapear(Factura("NV3", error: "(400) El campo nif no tiene un formato válido"), null).PuedeDeclararSimplificada);

            Assert.IsFalse(servicio.Mapear(Factura("NV4", error: "(TIMEOUT) Timeout"), null).PuedeDeclararSimplificada);
            Assert.IsFalse(servicio.Mapear(Factura("NV5", estado: VerifactuJobsService.ESTADO_SIN_DATOS_FISCALES,
                error: "Factura de camino externo a la API (#348) sin datos fiscales"), null).PuedeDeclararSimplificada);
            Assert.IsFalse(servicio.Mapear(Factura("RV6", serie: "RV", error: ERROR_NIF_RELLENO), null).PuedeDeclararSimplificada,
                "La rectificativa hereda la marca de su original");
            CabFacturaVta yaMarcada = Factura("NV7", error: ERROR_NIF_RELLENO);
            yaMarcada.VerifactuDeclararSimplificada = true;
            Assert.IsFalse(servicio.Mapear(yaMarcada, null).PuedeDeclararSimplificada);
        }

        [TestMethod]
        public async Task Controlador_DeclararSimplificada_InformaticaNoPuede_SoloAdministracionYDireccion()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Tecnico"), new[] { "NUEVAVISION\\" + NovedadesController.GRUPO_INFORMATICA })
            };

            var resultado = await controller.DeclararSimplificada(new DeclararSimplificadaVerifactuDTO { Empresa = "1", Numero = "NV2615001", Motivo = "x" }) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            A.CallTo(() => fake.DeclararSimplificada(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_DeclararSimplificada_SinMotivo_BadRequest()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Laura"), new[] { "NUEVAVISION\\Administración" })
            };

            var resultado = await controller.DeclararSimplificada(new DeclararSimplificadaVerifactuDTO { Empresa = "1", Numero = "NV2615001", Motivo = " " });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fake.DeclararSimplificada(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_DeclararSimplificada_PorEncimaDelLimite_BadRequestConElMensaje()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.DeclararSimplificada("1", "NV2615001", "No da el DNI", "NUEVAVISION\\Carlos"))
                .Returns(new ResultadoReintentoVerifactuDTO { Exitoso = false, Mensaje = "No hay salida sin el NIF" });
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            var resultado = await controller.DeclararSimplificada(new DeclararSimplificadaVerifactuDTO { Empresa = "1", Numero = "NV2615001", Motivo = "No da el DNI" })
                as BadRequestErrorMessageResult;

            Assert.AreEqual("No hay salida sin el NIF", resultado?.Message);
        }

        [TestMethod]
        public async Task Controlador_DeclararSimplificada_Administracion_OkConElUsuarioDelIdentity()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.DeclararSimplificada("1", "NV2615001", "No da el DNI", "NUEVAVISION\\Laura"))
                .Returns(new ResultadoReintentoVerifactuDTO { Exitoso = true, Mensaje = "ok" });
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Laura"), new[] { "NUEVAVISION\\Administración" })
            };

            var resultado = await controller.DeclararSimplificada(new DeclararSimplificadaVerifactuDTO { Empresa = "1", Numero = "NV2615001", Motivo = "No da el DNI" })
                as OkNegotiatedContentResult<ResultadoReintentoVerifactuDTO>;

            Assert.IsTrue(resultado.Content.Exitoso);
        }

        #endregion
    }
}
