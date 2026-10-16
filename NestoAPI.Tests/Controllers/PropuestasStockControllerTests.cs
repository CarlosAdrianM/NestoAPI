using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#630: POST api/PropuestasStock/Generar (solo Dirección) y GET api/PropuestasStock.
    /// NestoAPI#631: GET con estado, GET Resumen, GET MotivosSilencio y POST {id}/Decision (todo solo Compras y Dirección).
    /// </summary>
    [TestClass]
    public class PropuestasStockControllerTests
    {
        private List<(string mes, string usuario)> encolados;
        private List<(string mes, string usuario, DateTimeOffset cuando)> programados;
        private List<bool> regenerados;
        private IRepositorioTendencias repositorio;
        private IRepositorioDecisionesPropuestasStock decisiones;

        private PropuestasStockController Controller(DateTime ahora, bool direccion = true, string[] grupos = null)
        {
            encolados = new List<(string, string)>();
            programados = new List<(string, string, DateTimeOffset)>();
            regenerados = new List<bool>();
            repositorio = A.Fake<IRepositorioTendencias>();
            decisiones = A.Fake<IRepositorioDecisionesPropuestasStock>();
            PropuestasStockController controller = new PropuestasStockController(
                (m, u, r) => { encolados.Add((m, u)); regenerados.Add(r); return "1"; },
                (m, u, r, c) => { programados.Add((m, u, c)); regenerados.Add(r); return "2"; },
                () => repositorio,
                () => ahora,
                () => decisiones)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.User = grupos != null
                ? new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Manuel"), grupos)
                : direccion
                ? new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
                : new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Vendedor"), new[] { "NUEVAVISION\\Vendedores" });
            return controller;
        }

        private static readonly DateTime Noche = new DateTime(2026, 10, 14, 23, 0, 0);
        private static readonly DateTime Horario = new DateTime(2026, 10, 14, 11, 0, 0);

        [TestMethod]
        public void Generar_SinSerDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            IHttpActionResult resultado = controller.Generar("2026-09");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual(0, encolados.Count + programados.Count);
        }

        [TestMethod]
        public void Generar_DeNoche_EncolaYa()
        {
            IHttpActionResult resultado = Controller(Noche).Generar("2026-09");

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual(("2026-09", "NUEVAVISION\\Carlos"), encolados[0]);
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void Generar_EnHorario_SeProgramaParaLaMadrugada()
        {
            IHttpActionResult resultado = Controller(Horario).Generar();

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual(0, encolados.Count);
            Assert.AreEqual("2026-09", programados[0].mes); // sin mes: el último cerrado
            Assert.AreEqual(new DateTime(2026, 10, 15, 4, 30, 0), programados[0].cuando.DateTime);
        }

        [TestMethod]
        public void Generar_EnHorarioConAhora_EncolaYa()
        {
            _ = Controller(Horario).Generar("2026-09", ahora: true);

            Assert.AreEqual(1, encolados.Count);
        }

        [TestMethod]
        public void Generar_MesMalEscritoOFuturo_BadRequest()
        {
            Assert.IsInstanceOfType(Controller(Noche).Generar("09/2026"), typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(Controller(Noche).Generar("2026-11"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Generar_SinHangfire_503()
        {
            PropuestasStockController controller = new PropuestasStockController((m, u, r) => throw new InvalidOperationException("JobStorage no inicializado"),
                null, null, () => Noche)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { "NUEVAVISION\\Dirección" })
            };

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, controller.Generar("2026-09").ExecuteAsync(default).Result.StatusCode);
        }

        // ---------- NestoAPI#630: regenerar un mes ya calculado ----------

        [TestMethod]
        public void Generar_SinRegenerar_NoMiraLasDecisionesYEncolaElDeSiempre()
        {
            _ = Controller(Noche).Generar("2026-09");

            CollectionAssert.AreEqual(new[] { false }, regenerados);
            A.CallTo(() => repositorio.ContarDecisiones(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Generar_RegenerarEnHorario_SinDecisiones_SeProgramaParaLaMadrugada()
        {
            PropuestasStockController controller = Controller(Horario);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => repositorio.ContarDecisiones(new DateTime(2026, 9, 1))).Returns(0);

            IHttpActionResult resultado = controller.Generar("2026-09", regenerar: true);

            Assert.AreEqual(HttpStatusCode.Accepted, resultado.ExecuteAsync(default).Result.StatusCode);
            Assert.AreEqual("2026-09", programados.Single().mes);
            Assert.AreEqual(new DateTime(2026, 10, 15, 4, 30, 0), programados[0].cuando.DateTime);
            CollectionAssert.AreEqual(new[] { true }, regenerados);
        }

        [TestMethod]
        public void Generar_Regenerar_ConDecisiones_409SinEncolarNada()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => repositorio.ContarDecisiones(new DateTime(2026, 9, 1))).Returns(3);

            System.Net.Http.HttpResponseMessage respuesta = controller.Generar("2026-09", regenerar: true).ExecuteAsync(default).Result;

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content.ReadAsStringAsync().Result,
                "No se pueden regenerar las propuestas de 2026-09: ya tienen 3 decisiones tomadas y regenerarlas las borraría. No se ha borrado nada.");
            Assert.AreEqual(0, encolados.Count + programados.Count);
        }

        [TestMethod]
        public void Generar_Regenerar_SinSerDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)controller.Generar("2026-09", regenerar: true)).StatusCode);
            Assert.AreEqual(0, encolados.Count + programados.Count);
        }

        [TestMethod]
        public void Get_SinSerComprasNiDireccion_Forbidden()
        {
            // NestoAPI#631, Carlos (15/10/26): la lista solo la ven Compras y Dirección.
            PropuestasStockController controller = Controller(Noche, direccion: false);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);

            Assert.AreEqual(HttpStatusCode.Forbidden, controller.Get("2026-09").ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => decisiones.LeerPropuestas(A<DateTime?>._, A<string>._, A<int>._, A<FiltroPropuestasStock>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerPropuestas(A<DateTime?>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Get_Compras_Consulta()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuestas(new DateTime(2026, 9, 1), null, 50, A<FiltroPropuestasStock>._))
                .Returns(new List<PropuestaStockConDecision> { new PropuestaStockConDecision { Producto = "17404", Estado = EstadosPropuesta.PENDIENTE } });

            var ok = (OkNegotiatedContentResult<List<PropuestaStockConDecision>>)controller.Get("2026-09");

            Assert.AreEqual(EstadosPropuesta.PENDIENTE, ok.Content[0].Estado);
        }

        [TestMethod]
        public void Get_ConEstado_FiltraPorElEstadoNormalizado()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);

            _ = controller.Get("2026-09", 10, "pendiente");

            A.CallTo(() => decisiones.LeerPropuestas(new DateTime(2026, 9, 1), EstadosPropuesta.PENDIENTE, 10, A<FiltroPropuestasStock>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Get_EstadoDesconocido_BadRequest()
        {
            Assert.IsInstanceOfType(Controller(Noche).Get("2026-09", 10, "Borrada"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Get_ConEstadoSinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Get("2026-09", 10, "Pendiente").ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Resumen_SinSerComprasNiDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);

            Assert.AreEqual(HttpStatusCode.Forbidden, controller.Resumen("2026-09").ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => decisiones.Resumen(A<DateTime?>._, A<FiltroPropuestasStock>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void MotivosSilencio_SinSerComprasNiDireccion_Forbidden()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, Controller(Noche, direccion: false).GetMotivosSilencio().ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void MotivosSilencio_SinParametro_LosTresDeLaIssue()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.LeerParametroMotivosSilencio()).Returns(null);

            var ok = (OkNegotiatedContentResult<List<string>>)controller.GetMotivosSilencio();

            CollectionAssert.AreEqual(new[] { "Nuevo", "Estacional", "Aparato" }, ok.Content);
        }

        [TestMethod]
        public void MotivosSilencio_ConParametro_LosDelParametro()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.LeerParametroMotivosSilencio()).Returns("Nuevo, Estacional, Aparato, Fin de serie");

            var ok = (OkNegotiatedContentResult<List<string>>)controller.GetMotivosSilencio();

            CollectionAssert.AreEqual(new[] { "Nuevo", "Estacional", "Aparato", "Fin de serie" }, ok.Content);
        }

        [TestMethod]
        public void Resumen_DevuelveLosEstadosDelMes()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.Resumen(new DateTime(2026, 9, 1), A<FiltroPropuestasStock>._))
                .Returns(new List<ResumenEstadoPropuestas> { new ResumenEstadoPropuestas { Estado = EstadosPropuesta.PENDIENTE, Propuestas = 42 } });

            var ok = (OkNegotiatedContentResult<List<ResumenEstadoPropuestas>>)controller.Resumen("2026-09");

            Assert.AreEqual(42, ok.Content[0].Propuestas);
        }

        [TestMethod]
        public void Get_ConFiltros_PasaFamiliaGrupoAccionYTextoLimpios()
        {
            // Sugerencia 644 (Manuel): sin filtros la lista no es útil.
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);

            _ = controller.Get("2026-09", 10, null, " Lisap ", "COS", AccionesPropuesta.LIQUIDAR, " champú ");

            A.CallTo(() => decisiones.LeerPropuestas(new DateTime(2026, 9, 1), null, 10,
                A<FiltroPropuestasStock>.That.Matches(f => f.Familia == "Lisap" && f.Grupo == "COS" && f.Accion == AccionesPropuesta.LIQUIDAR && f.Texto == "champú")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Get_ConFiltrosSinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Get("2026-09", 10, null, "Lisap").ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => repositorio.LeerPropuestas(A<DateTime?>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Resumen_ConFiltros_CuentaSoloLoFiltrado()
        {
            // Sugerencia 644: el contador de la pantalla refleja lo filtrado.
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.Resumen(new DateTime(2026, 9, 1), A<FiltroPropuestasStock>.That.Matches(f => f.Familia == "Lisap" && f.Accion == AccionesPropuesta.LIQUIDAR)))
                .Returns(new List<ResumenEstadoPropuestas> { new ResumenEstadoPropuestas { Estado = EstadosPropuesta.PENDIENTE, Propuestas = 7 } });

            var ok = (OkNegotiatedContentResult<List<ResumenEstadoPropuestas>>)controller.Resumen("2026-09", "Lisap", null, AccionesPropuesta.LIQUIDAR);

            Assert.AreEqual(7, ok.Content[0].Propuestas);
        }

        [TestMethod]
        public void Filtros_SinSerComprasNiDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            Assert.AreEqual(HttpStatusCode.Forbidden, controller.GetFiltros("2026-09").ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => decisiones.LeerOpcionesFiltro(A<DateTime?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Filtros_DevuelveLasOpcionesDelMes()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerOpcionesFiltro(new DateTime(2026, 9, 1))).Returns(new OpcionesFiltroPropuestas
            {
                Familias = new List<string> { "Lisap" },
                Grupos = new List<string> { "COS" },
                Acciones = new List<string> { AccionesPropuesta.LIQUIDAR }
            });

            var ok = (OkNegotiatedContentResult<OpcionesFiltroPropuestas>)controller.GetFiltros("2026-09");

            CollectionAssert.AreEqual(new[] { "Lisap" }, ok.Content.Familias);
            CollectionAssert.AreEqual(new[] { AccionesPropuesta.LIQUIDAR }, ok.Content.Acciones);
        }

        [TestMethod]
        public void Filtros_MesMalEscrito_BadRequest()
        {
            Assert.IsInstanceOfType(Controller(Noche).GetFiltros("sept"), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Filtros_SinTabla_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.GetFiltros().ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Resumen_SinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Resumen().ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Decision_SinSerComprasNiDireccion_Forbidden()
        {
            PropuestasStockController controller = Controller(Noche, direccion: false);

            Assert.AreEqual(HttpStatusCode.Forbidden, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Decision_Compras_Guarda()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(new PropuestaStockConDecision
            {
                Id = 7, Mes = new DateTime(2026, 9, 1), Producto = "17404", Almacen = "ALG", Accion = AccionesPropuesta.PARAR_COMPRAS, Estado = EstadosPropuesta.PENDIENTE
            });

            IHttpActionResult resultado = controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" });

            var ok = (OkNegotiatedContentResult<ResultadoDecisionPropuestaStock>)resultado;
            Assert.AreEqual(EstadosPropuesta.POSPUESTA, ok.Content.Estado);
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>.That.Matches(r => r.Usuario == "NUEVAVISION\\Manuel" && r.Fecha == Noche)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Decision_ErrorDeNegocio_DevuelveSuCodigoYElTexto()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(null);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);

            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(new PropuestaStockConDecision
            {
                Id = 7, Mes = new DateTime(2026, 9, 1), Producto = "17404", Almacen = "ALG", Accion = AccionesPropuesta.AJUSTAR, Decision = DecisionesPropuesta.APLICAR, Estado = EstadosPropuesta.APLICADA
            });
            Assert.AreEqual(HttpStatusCode.Conflict, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Decision_SinElScriptDelCorte3_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Posponer" }).ExecuteAsync(default).Result.StatusCode);
        }

        [TestMethod]
        public void Decision_PotenciarSinElScriptDe655_404ConElNombreDelScript()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.ExistePotenciacion()).Returns(false);

            System.Net.Http.HttpResponseMessage respuesta = controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 40 })
                .ExecuteAsync(default).Result;

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content.ReadAsStringAsync().Result, "Issue655_PotenciarVentas.sql");
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Decision_Potenciar_DevuelveEnPotenciacion()
        {
            PropuestasStockController controller = Controller(Noche, grupos: new[] { "NUEVAVISION\\Compras" });
            _ = A.CallTo(() => decisiones.ExisteEsquema()).Returns(true);
            _ = A.CallTo(() => decisiones.ExistePotenciacion()).Returns(true);
            _ = A.CallTo(() => decisiones.LeerPropuesta(7)).Returns(new PropuestaStockConDecision
            {
                Id = 7, Mes = new DateTime(2026, 9, 1), Producto = "20258", Almacen = "ALG", Accion = AccionesPropuesta.POTENCIAR, Estado = EstadosPropuesta.PENDIENTE
            });

            IHttpActionResult resultado = controller.Decision(7, new DecisionPropuestaStockDTO { Decision = "Potenciar", ObjetivoVentaMensual = 40 });

            var ok = (OkNegotiatedContentResult<ResultadoDecisionPropuestaStock>)resultado;
            Assert.AreEqual(EstadosPropuesta.EN_POTENCIACION, ok.Content.Estado);
            A.CallTo(() => decisiones.GuardarDecision(A<RegistroDecisionPropuesta>.That.Matches(r => r.ObjetivoVentaMensual == 40 && r.PotenciarHasta == new DateTime(2027, 4, 14))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Get_DevuelveLasFilasDelMes()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => repositorio.LeerPropuestas(new DateTime(2026, 9, 1), 10))
                .Returns(new List<PropuestaStockGuardada> { new PropuestaStockGuardada { Producto = "17404" } });

            IHttpActionResult resultado = controller.Get("2026-09", 10);

            var ok = (OkNegotiatedContentResult<List<PropuestaStockGuardada>>)resultado;
            Assert.AreEqual("17404", ok.Content[0].Producto);
        }

        [TestMethod]
        public void Get_SinTabla_404()
        {
            PropuestasStockController controller = Controller(Noche);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            Assert.AreEqual(HttpStatusCode.NotFound, controller.Get().ExecuteAsync(default).Result.StatusCode);
        }
    }
}
