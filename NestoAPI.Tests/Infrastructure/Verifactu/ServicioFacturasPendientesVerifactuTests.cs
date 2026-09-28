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
    }
}
