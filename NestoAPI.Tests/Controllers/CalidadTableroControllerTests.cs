using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Calidad.Tablero;
using NestoAPI.Tests.Infrastructure.Calidad;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#914: api/Calidad/Tablero (Mios, evolución, tablero de Dirección, umbrales y recalcular).</summary>
    [TestClass]
    public class CalidadTableroControllerTests
    {
        private static readonly DateTime JUEVES_10 = new DateTime(2026, 11, 26, 10, 0, 0);
        private static readonly DateTime JUEVES_22 = new DateTime(2026, 11, 26, 22, 0, 0);
        private RepositorioTableroEnMemoria repo;
        private IProgramadorTablero programador;
        private IdentidadTablero quien;

        [TestInitialize]
        public void Preparar()
        {
            repo = new RepositorioTableroEnMemoria();
            programador = A.Fake<IProgramadorTablero>();
            A.CallTo(() => programador.Encolar(A<int>._, A<string>._)).Returns("enc-1");
            A.CallTo(() => programador.Programar(A<DateTime>._, A<int>._, A<string>._)).Returns("prog-1");
            quien = new IdentidadTablero { Usuario = "Ines", Vendedores = new List<string> { "IA" } };
        }

        private CalidadTableroController Controller(DateTime? ahora = null, params string[] roles) =>
            new CalidadTableroController(() => repo, () => new FuentesKpiFalsas(), programador, () => ahora ?? JUEVES_10, u => Task.FromResult(quien))
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Ines", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [TestMethod]
        public async Task Mios_SinElScript_503()
        {
            repo.Existen = false;

            var r = await Controller().GetMios() as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
            StringAssert.Contains(r.Content, "Issue914t_CalidadTablero.sql");
        }

        [TestMethod]
        public async Task Mios_DelVendedor_SoloSuPedidoPerfecto()
        {
            repo.Guardar("PedidoPerfecto", TipoDuenoKpi.Vendedor, "IA", new DateTime(2026, 10, 26), 90);
            repo.Guardar("PedidoPerfecto", TipoDuenoKpi.Vendedor, "JE", new DateTime(2026, 10, 26), 10);

            var r = await Controller(null, "Vendedores").GetMios() as OkNegotiatedContentResult<List<KpiMioDTO>>;

            Assert.AreEqual(1, r.Content.Count);
            Assert.AreEqual("PedidoPerfecto", r.Content[0].Kpi);
            Assert.AreEqual("IA", r.Content[0].Dueno);
            Assert.AreEqual(90m, r.Content[0].UltimaSemana.Valor);
        }

        [TestMethod]
        public async Task Mios_SinNingunIndicador_ListaVacia200()
        {
            quien = new IdentidadTablero { Usuario = "Nadie" };

            var r = await Controller().GetMios() as OkNegotiatedContentResult<List<KpiMioDTO>>;

            Assert.AreEqual(0, r.Content.Count);
        }

        [TestMethod]
        public async Task Semanas_KpiQueNoExiste_404()
        {
            var r = await Controller().GetSemanas("Nada") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.NotFound, r.StatusCode);
        }

        [TestMethod]
        public async Task Semanas_DeOtroVendedor_403DeUsted()
        {
            var r = await Controller(null, "Vendedores").GetSemanas("PedidoPerfecto", 12, "Vendedor", "JE") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(CalidadTableroController.MENSAJE_SIN_PERMISO_OTRO, r.Content);
        }

        [TestMethod]
        public async Task Semanas_DeOtroVendedor_Direccion_Si()
        {
            repo.Guardar("PedidoPerfecto", TipoDuenoKpi.Vendedor, "JE", new DateTime(2026, 10, 26), 10);
            quien.PuedeVerTodo = true;

            var r = await Controller(null, "Dirección").GetSemanas("PedidoPerfecto", 12, "Vendedor", "JE") as OkNegotiatedContentResult<EvolucionKpiDTO>;

            Assert.AreEqual("JE", r.Content.Dueno);
            Assert.AreEqual(1, r.Content.Semanas.Count);
        }

        [TestMethod]
        public async Task Semanas_SinDueno_ElPropio()
        {
            repo.Guardar("PedidoPerfecto", TipoDuenoKpi.Vendedor, "IA", new DateTime(2026, 10, 26), 90);
            repo.Guardar("PedidoPerfecto", TipoDuenoKpi.Vendedor, "JE", new DateTime(2026, 10, 26), 10);

            var r = await Controller(null, "Vendedores").GetSemanas("pedidoperfecto") as OkNegotiatedContentResult<EvolucionKpiDTO>;

            Assert.AreEqual("IA", r.Content.Dueno);
            Assert.AreEqual(90m, r.Content.Semanas[0].Valor);
            Assert.IsNull(r.Content.EnCurso);
            Assert.IsNotNull(r.Content.SemanaActualDisponibleEl);
        }

        [TestMethod]
        public async Task Semanas_TipoQueNoEsDelKpi_400()
        {
            var r = await Controller().GetSemanas("PedidoPerfecto", 12, "Almacen") as BadRequestErrorMessageResult;

            Assert.IsNotNull(r);
        }

        [DataTestMethod]
        [DataRow("Vendedores")]
        [DataRow("Almacén")]
        [DataRow("Tiendas")]
        public void Tablero_Umbrales_Recalcular_SoloDireccionEInformatica(string rol)
        {
            CalidadTableroController c = Controller(null, rol);

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)c.GetTablero()).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)c.GetPropuesta("PedidoPerfecto")).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)c.PutUmbrales(new AprobarUmbralesDTO())).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)c.PostRecalcular()).StatusCode);
        }

        [TestMethod]
        public void Tablero_SinSemana_LaPasada()
        {
            var r = Controller(null, "Informática").GetTablero() as OkNegotiatedContentResult<TableroSemanaDTO>;

            Assert.AreEqual(new DateTime(2026, 11, 16), r.Content.Semana);
            Assert.AreEqual(CatalogoKpiTablero.Todos.Count, r.Content.Kpis.Count);
        }

        [TestMethod]
        public void Tablero_SemanaQueNoHaEmpezado_400()
        {
            Assert.IsInstanceOfType(Controller(null, "Dirección").GetTablero(new DateTime(2026, 12, 7)), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void PutUmbrales_Mal_400DeUsted()
        {
            var r = Controller(null, "Dirección").PutUmbrales(new AprobarUmbralesDTO { Kpi = "PedidoPerfecto", TipoDueno = "Vendedor", CortePoorFair = 90, CorteFairGood = 75, CorteGoodFantastic = 60 })
                as BadRequestErrorMessageResult;

            StringAssert.Contains(r.Message, "de menor a mayor");
        }

        [TestMethod]
        public void PutUmbrales_Bien_GuardaConElUsuarioDelToken()
        {
            var r = Controller(null, "Dirección").PutUmbrales(new AprobarUmbralesDTO { Kpi = "PedidoPerfecto", TipoDueno = "Vendedor", CortePoorFair = 60, CorteFairGood = 75, CorteGoodFantastic = 90 })
                as OkNegotiatedContentResult<UmbralesDTO>;

            Assert.AreEqual(ReglaNotaKpi.DESDE_SIEMPRE, r.Content.VigenteDesde);
            StringAssert.Contains(repo.Umbrales[0].Usuario, "Ines");
            Assert.AreEqual(1, repo.Rastros.Count);
        }

        [TestMethod]
        public void Recalcular_EnHorario_SeProgramaParaLaNoche()
        {
            var r = Controller(JUEVES_10, "Dirección").PostRecalcular(26) as NegotiatedContentResult<LanzamientoTableroDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, r.StatusCode);
            Assert.IsTrue(r.Content.Programado);
            A.CallTo(() => programador.Programar(new DateTime(2026, 11, 26, 21, 0, 0), 26, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Recalcular_FueraDeHorarioOAhora_SeEncola()
        {
            var deNoche = Controller(JUEVES_22, "Informática").PostRecalcular() as NegotiatedContentResult<LanzamientoTableroDTO>;
            var ahora = Controller(JUEVES_10, "Dirección").PostRecalcular(12, ahora: true) as NegotiatedContentResult<LanzamientoTableroDTO>;

            Assert.IsFalse(deNoche.Content.Programado);
            Assert.IsFalse(ahora.Content.Programado);
            A.CallTo(() => programador.Encolar(12, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Recalcular_SemanasFueraDeRango_400()
        {
            Assert.IsInstanceOfType(Controller(null, "Dirección").PostRecalcular(0), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller(null, "Dirección").PostRecalcular(105), typeof(BadRequestErrorMessageResult));
        }
    }
}
