using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#727: tests del endpoint POST api/FacturacionRutas/FinDeMes (permisos, interruptor y parámetros).
    /// </summary>
    [TestClass]
    public class FacturacionRutasControllerFinDeMesTests
    {
        private NVEntities db;
        private IServicioFacturarFinDeMes servicioFinDeMes;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioFinDeMes = A.Fake<IServicioFacturarFinDeMes>();
            A.CallTo(() => servicioFinDeMes.Facturar(A<string>._, A<DateTime>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new ResultadoFacturacionFinDeMes()));
        }

        [TestMethod]
        public void InterruptorFinDeMes_SoloTrueEnciende()
        {
            Assert.IsTrue(InterruptorFinDeMesPorApi.EstaActivo("true"));
            Assert.IsTrue(InterruptorFinDeMesPorApi.EstaActivo(" TRUE "));
            Assert.IsFalse(InterruptorFinDeMesPorApi.EstaActivo("false"));
            Assert.IsFalse(InterruptorFinDeMesPorApi.EstaActivo(null));
        }

        [TestMethod]
        public async Task FinDeMes_Almacen_Retorna403()
        {
            FacturacionRutasController controller = Controlador(Constantes.GruposSeguridad.ALMACEN);

            var resultado = await controller.FacturarFinDeMes(new FacturarFinDeMesRequestDTO());

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => servicioFinDeMes.Facturar(A<string>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FinDeMes_InterruptorApagado_Retorna400ConMensaje()
        {
            FacturacionRutasController controller = Controlador(Constantes.GruposSeguridad.ADMINISTRACION);
            controller.FinDeMesPorApi = () => false;

            var resultado = await controller.FacturarFinDeMes(new FacturarFinDeMesRequestDTO());

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(InterruptorFinDeMesPorApi.MOTIVO_APAGADO, ((BadRequestErrorMessageResult)resultado).Message);
            A.CallTo(() => servicioFinDeMes.Facturar(A<string>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(Constantes.GruposSeguridad.ADMINISTRACION)]
        [DataRow(Constantes.GruposSeguridad.DIRECCION)]
        [DataRow(Constantes.GruposSeguridad.INFORMATICA)]
        public async Task FinDeMes_SinFecha_CortaElUltimoDiaDelMesAnteriorYDevuelveElResultado(string grupo)
        {
            FacturacionRutasController controller = Controlador(grupo);

            var resultado = await controller.FacturarFinDeMes(new FacturarFinDeMesRequestDTO());

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoFacturacionFinDeMes>));
            A.CallTo(() => servicioFinDeMes.Facturar(Constantes.Empresas.EMPRESA_POR_DEFECTO, new DateTime(2026, 10, 31), null, null, "NUEVAVISION\\Test"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FinDeMes_ConFechaClienteYContacto_LosPasaAlServicio()
        {
            FacturacionRutasController controller = Controlador(Constantes.GruposSeguridad.ADMINISTRACION);

            _ = await controller.FacturarFinDeMes(new FacturarFinDeMesRequestDTO
            {
                Empresa = "3",
                FechaCorte = "2026-09-30",
                Cliente = " 27593 ",
                Contacto = "0"
            });

            A.CallTo(() => servicioFinDeMes.Facturar("3", new DateTime(2026, 9, 30), "27593", "0", "NUEVAVISION\\Test"))
                .MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow("31/10/2026", null, null)]
        [DataRow("2026-11-15", null, null)]
        [DataRow(null, null, "0")]
        public async Task FinDeMes_ParametrosNoValidos_Retorna400(string fecha, string cliente, string contacto)
        {
            FacturacionRutasController controller = Controlador(Constantes.GruposSeguridad.ADMINISTRACION);

            var resultado = await controller.FacturarFinDeMes(new FacturarFinDeMesRequestDTO { FechaCorte = fecha, Cliente = cliente, Contacto = contacto });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicioFinDeMes.Facturar(A<string>._, A<DateTime>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        private FacturacionRutasController Controlador(string grupo)
        {
            var controller = new FacturacionRutasController(db, A.Fake<IServicioPedidosParaFacturacion>(), A.Fake<IServicioAgruparPorPO>(),
                null, servicioFinDeMes)
            {
                FinDeMesPorApi = () => true,
                Hoy = () => new DateTime(2026, 11, 3)
            };
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, grupo));
            identity.AddClaim(new Claim(ClaimTypes.Name, "NUEVAVISION\\Test"));
            controller.User = new ClaimsPrincipal(identity);
            return controller;
        }
    }
}
