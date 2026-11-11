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
    /// 11/11/26 (CV2600692/693, cursos de 500 € que la AEAT rechaza por NIF/nombre): «No declarar» una factura antes de la entrada en
    /// vigor de Verifactu. Datos inventados.
    /// </summary>
    [TestClass]
    public class NoDeclararVerifactuTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);
        private const string ERROR_AEAT = "(BadRequest) El NIF/NOMBRE (00000000T/CLIENTE INVENTADO) del destinatario no se encuentra registrado en la Agencia Tributaria.";
        private const string MOTIVO = "El cliente no aparece en el censo de la AEAT y no contesta";

        private NVEntities db;
        private DbSet<CabFacturaVta> fakeFacturas;
        private List<Modificacion> modificaciones;
        private List<CabFacturaVta> reenviadas;
        private DateTime hoy;
        private DateTime fechaInicioOriginal;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeFacturas = A.Fake<DbSet<CabFacturaVta>>(o => o.Implements<IQueryable<CabFacturaVta>>().Implements<IDbAsyncEnumerable<CabFacturaVta>>());
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            var fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());
            Configurar(fakeClientes, new List<Cliente>().AsQueryable());
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            modificaciones = new List<Modificacion>();
            var fakeModificaciones = A.Fake<DbSet<Modificacion>>();
            A.CallTo(() => fakeModificaciones.Add(A<Modificacion>._)).ReturnsLazily((Modificacion m) => { modificaciones.Add(m); return m; });
            A.CallTo(() => db.Modificaciones).Returns(fakeModificaciones);
            reenviadas = new List<CabFacturaVta>();
            hoy = HOY;
            fechaInicioOriginal = VerifactuJobsService.FechaInicioDeclaracion;
            VerifactuJobsService.FechaInicioDeclaracion = new DateTime(2026, 7, 20);
        }

        [TestCleanup]
        public void Cleanup()
        {
            VerifactuJobsService.FechaInicioDeclaracion = fechaInicioOriginal;
        }

        private ServicioFacturasPendientesVerifactu Servicio() => new ServicioFacturasPendientesVerifactu(db,
            f => { reenviadas.Add(f); return Task.FromResult(new VerifactuResponse { Exitoso = false, CodigoError = "400", MensajeError = "sigue mal" }); },
            f => Task.FromResult(new VerifactuResponse { Exitoso = false }),
            () => hoy);

        private static void Configurar<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        private void ConFacturas(params CabFacturaVta[] facturas) => Configurar(fakeFacturas, facturas.AsQueryable());

        private static CabFacturaVta Factura(string numero = "CV2600692", string error = ERROR_AEAT, string uuid = null, string estado = null, DateTime? fecha = null,
            string serie = "CV") => new CabFacturaVta
            {
                Empresa = "1",
                Número = numero,
                Serie = serie,
                Nº_Cliente = "99901",
                Contacto = "0",
                Fecha = fecha ?? new DateTime(2026, 10, 29),
                VerifactuUUID = uuid,
                VerifactuEstado = estado,
                VerifactuUltimoError = error,
                NombreFiscal = "CLIENTE INVENTADO"
            };

        [TestMethod]
        public async Task SinRegistroConErrorYAnteriorAlVigor_LaAparta_YDejaRastro()
        {
            CabFacturaVta factura = Factura();
            ConFacturas(factura);

            ResultadoReintentoVerifactuDTO resultado = await Servicio().NoDeclarar("1", "CV2600692", MOTIVO, "NUEVAVISION\\Laura");

            Assert.IsTrue(resultado.Exitoso, resultado.Mensaje);
            Assert.AreEqual(VerifactuJobsService.ESTADO_DESCARTADA, factura.VerifactuEstado);
            Assert.AreEqual(ERROR_AEAT, factura.VerifactuUltimoError, "el error se conserva");
            Assert.IsTrue(resultado.Factura.NoDeclarada);
            Assert.IsFalse(resultado.Factura.PuedeNoDeclararse);
            Modificacion rastro = modificaciones.Single();
            StringAssert.Contains(rastro.Nuevo, MOTIVO);
            StringAssert.Contains(rastro.Nuevo, "NUEVAVISION\\Laura");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task Apartada_NoSaleEnLaLista_SoloConIncluirNoDeclaradas()
        {
            ConFacturas(Factura(estado: VerifactuJobsService.ESTADO_DESCARTADA), Factura("CV2600693"));

            List<FacturaPendienteVerifactuDTO> normal = await Servicio().Listar();
            List<FacturaPendienteVerifactuDTO> todas = await Servicio().Listar(true);

            CollectionAssert.AreEqual(new[] { "CV2600693" }, normal.Select(f => f.Numero).ToArray());
            Assert.AreEqual(2, todas.Count);
            FacturaPendienteVerifactuDTO apartada = todas.Single(f => f.Numero == "CV2600692");
            Assert.AreEqual(ServicioFacturasPendientesVerifactu.SITUACION_NO_DECLARADA, apartada.Situacion);
            Assert.IsTrue(apartada.PuedeReintentar);
            Assert.IsTrue(todas.Single(f => f.Numero == "CV2600693").PuedeNoDeclararse);
        }

        [TestMethod]
        public async Task Reintentar_SobreUnaApartada_LaVuelveAAbrir()
        {
            CabFacturaVta factura = Factura(estado: VerifactuJobsService.ESTADO_DESCARTADA);
            ConFacturas(factura);

            await Servicio().Reintentar("1", "CV2600692");

            Assert.IsNull(factura.VerifactuEstado, "vuelve al ciclo del job");
            Assert.AreEqual(1, reenviadas.Count);
            StringAssert.Contains(modificaciones.Single().Nuevo, "Se vuelve a abrir");
        }

        [TestMethod]
        public void Reglas_CadaUnaConSuMotivo()
        {
            Assert.IsNull(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(), MOTIVO, HOY));
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(), "corto", HOY), "motivo");
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(), null, HOY), "motivo");
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(uuid: "u", estado: "Incorrecto"), MOTIVO, HOY), "registro");
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(error: null), MOTIVO, HOY), "ningún error");
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(estado: VerifactuJobsService.ESTADO_DESCARTADA), MOTIVO, HOY), "ya está apartada");
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(serie: "XX"), MOTIVO, HOY), "no se declara a Verifactu");
        }

        [TestMethod]
        public void DesdeLaEntradaEnVigor_NadaSePuedeApartar()
        {
            Assert.AreEqual("Desde el 01/01/2027 todas las facturas tienen que declararse.",
                ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(), MOTIVO, new DateTime(2027, 1, 1)));
            StringAssert.Contains(ServicioFacturasPendientesVerifactu.MotivoParaNoPoderApartar(Factura(fecha: new DateTime(2027, 1, 1)), MOTIVO, HOY),
                "todas las facturas tienen que declararse");
        }

        [TestMethod]
        public async Task DesdeLaEntradaEnVigor_PuedeNoDeclararseFalseYElPostNoAparta()
        {
            hoy = new DateTime(2027, 1, 2);
            CabFacturaVta factura = Factura();
            ConFacturas(factura);

            Assert.IsFalse((await Servicio().Listar()).Single().PuedeNoDeclararse);
            ResultadoReintentoVerifactuDTO resultado = await Servicio().NoDeclarar("1", "CV2600692", MOTIVO, "NUEVAVISION\\Laura");
            Assert.IsFalse(resultado.Exitoso);
            Assert.IsNull(factura.VerifactuEstado);
        }

        // ---------- Controlador ----------

        [TestMethod]
        public async Task Controlador_Informatica_403()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\" + NovedadesController.GRUPO_INFORMATICA })
            };

            var resultado = await controller.NoDeclarar(new NoDeclararVerifactuDTO { Empresa = "1", Numero = "CV2600692", Motivo = MOTIVO }) as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado?.StatusCode);
            A.CallTo(() => fake.NoDeclarar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_NoSePuede_400ConElMensaje()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.NoDeclarar("1", "CV2600692", MOTIVO, "NUEVAVISION\\Laura"))
                .Returns(new ResultadoReintentoVerifactuDTO { Exitoso = false, Mensaje = "Desde el 01/01/2027 todas las facturas tienen que declararse." });
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Laura"), new[] { "NUEVAVISION\\Administración" })
            };

            var resultado = await controller.NoDeclarar(new NoDeclararVerifactuDTO { Empresa = "1", Numero = "CV2600692", Motivo = MOTIVO }) as BadRequestErrorMessageResult;

            Assert.AreEqual("Desde el 01/01/2027 todas las facturas tienen que declararse.", resultado?.Message);
        }

        [TestMethod]
        public async Task Controlador_Direccion_Ok()
        {
            var fake = A.Fake<IServicioFacturasPendientesVerifactu>();
            A.CallTo(() => fake.NoDeclarar("1", "CV2600692", MOTIVO, "NUEVAVISION\\Carlos"))
                .Returns(new ResultadoReintentoVerifactuDTO { Exitoso = true, Mensaje = "ok" });
            var controller = new VerifactuController(fake)
            {
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            var resultado = await controller.NoDeclarar(new NoDeclararVerifactuDTO { Empresa = "1", Numero = "CV2600692", Motivo = MOTIVO })
                as OkNegotiatedContentResult<ResultadoReintentoVerifactuDTO>;

            Assert.IsTrue(resultado.Content.Exitoso);
        }
    }
}
