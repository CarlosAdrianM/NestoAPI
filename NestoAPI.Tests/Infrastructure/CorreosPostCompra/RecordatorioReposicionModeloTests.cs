using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#736: la puerta del correo de reposición con el modelo de propensión (Apagado / Sombra / Activo), el registro
    /// de la P1 y el informe de conversión Enviado frente a Control.
    /// </summary>
    [TestClass]
    public class RecordatorioReposicionModeloTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 12, 3);

        private List<Exception> errores;

        [TestInitialize]
        public void Setup()
        {
            errores = new List<Exception>();
            PuertaModeloRecordatorioReposicion.OlvidarAvisos();
        }

        private static CandidatoReposicionDTO Producto(string cliente, string id, int diasDesde = 40, int intervalo = 30)
            => new CandidatoReposicionDTO
            {
                Cliente = cliente, Producto = id, NombreProducto = id, NumeroCompras = 4, IntervaloDias = intervalo,
                DiasDesdeUltimaCompra = diasDesde, UltimaCompra = HOY.AddDays(-diasDesde), ImporteHabitual = 50m, CantidadHabitual = 1
            };

        private static RecordatorioReposicionClienteDTO Correo(string cliente, params string[] productos)
            => new RecordatorioReposicionClienteDTO
            {
                Cliente = cliente,
                Email = cliente + "@cliente.es",
                Vendedor = "NV",
                Productos = productos.Select(p => Producto(cliente, p)).ToList()
            };

        /// <summary>15191: A (0,40) y B (0,05). 15200: C (0,02). Control 15300: D (0,01) y E (0,50).</summary>
        private static PlanRecordatorioReposicionDTO Plan()
        {
            var plan = new PlanRecordatorioReposicionDTO();
            plan.Enviar.Add(Correo("15191", "A", "B"));
            plan.Enviar.Add(Correo("15200", "C"));
            plan.Control.Add(Correo("15300", "D", "E"));
            plan.FueraPorVendedor.Add(Correo("15400", "F"));
            return plan;
        }

        private static readonly Dictionary<string, float> P1 = new Dictionary<string, float>
        {
            ["A"] = 0.40f, ["B"] = 0.05f, ["C"] = 0.02f, ["D"] = 0.01f, ["E"] = 0.50f, ["F"] = 0.99f
        };

        private static IPuntuadorReposicion Puntuador(string version = "m1-20261202_2330", List<string> clientesPuntuados = null)
        {
            var puntuador = A.Fake<IPuntuadorReposicion>();
            A.CallTo(() => puntuador.ModeloDisponible()).Returns(version);
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((string e, string cliente, DateTime hoy, IReadOnlyCollection<string> productos) =>
                {
                    clientesPuntuados?.Add(cliente);
                    return Task.FromResult(productos.Where(P1.ContainsKey).ToDictionary(p => p, p => P1[p]));
                });
            return puntuador;
        }

        private Task<ResumenPuertaModeloDTO> Aplicar(PlanRecordatorioReposicionDTO plan, ResultadoRecordatorioReposicionDTO resultado,
            ModoPuertaModeloReposicion modo, IPuntuadorReposicion puntuador)
            => PuertaModeloRecordatorioReposicion.Aplicar(plan, resultado, modo, 0.15m, puntuador, "1", HOY, errores.Add);

        #region Puerta

        [TestMethod]
        public async Task Puerta_Sombra_PuntuaYApuntaPeroNoQuitaNada()
        {
            PlanRecordatorioReposicionDTO plan = Plan();
            var resultado = new ResultadoRecordatorioReposicionDTO();
            var puntuados = new List<string>();

            ResumenPuertaModeloDTO resumen = await Aplicar(plan, resultado, ModoPuertaModeloReposicion.Sombra, Puntuador(clientesPuntuados: puntuados));

            Assert.AreEqual(2, plan.Enviar.Count);
            Assert.AreEqual(2, plan.Enviar[0].Productos.Count);
            Assert.AreEqual(2, plan.Control.Single().Productos.Count);
            Assert.AreEqual(0, resultado.Descartes.Count);
            CandidatoReposicionDTO b = plan.Enviar[0].Productos.Single(p => p.Producto == "B");
            Assert.AreEqual(0.05f, b.ProbabilidadModelo);
            Assert.AreEqual(true, b.FueraPorModelo);
            Assert.IsTrue(b.SeAvisaria, "En sombra no se quita");
            Assert.AreEqual(false, plan.Enviar[0].Productos.Single(p => p.Producto == "A").FueraPorModelo);
            Assert.AreEqual(5, resumen.Puntuados);
            Assert.AreEqual(3, resumen.PorDebajo, "B, C y D");
            Assert.AreEqual("m1-20261202_2330", resumen.VersionModelo);
            CollectionAssert.AreEquivalent(new[] { "15191", "15200", "15300" }, puntuados, "Solo lo que se enviaría y el control registrado");
            Assert.IsNull(plan.FueraPorVendedor.Single().Productos.Single().ProbabilidadModelo);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Puerta_Activo_QuitaLosDePorDebajoDelUmbralConMotivo()
        {
            PlanRecordatorioReposicionDTO plan = Plan();
            var resultado = new ResultadoRecordatorioReposicionDTO();

            ResumenPuertaModeloDTO resumen = await Aplicar(plan, resultado, ModoPuertaModeloReposicion.Activo, Puntuador());

            Assert.AreEqual("A", plan.Enviar.Single().Productos.Single().Producto, "15200 se queda sin nada y no sale");
            Assert.AreEqual("E", plan.Control.Single().Productos.Single().Producto, "Al control se le aplica lo mismo");
            CollectionAssert.AreEquivalent(new[] { "B", "C", "D" }, resultado.Descartes.Select(d => d.Producto).ToList());
            Assert.AreEqual("El modelo de propensión le da P1 = 0,050: menos de 0,15.",
                resultado.Descartes.Single(d => d.Producto == "B").Motivo);
            Assert.AreEqual(3, resumen.PorDebajo);
            Assert.AreEqual(1, resumen.CorreosQuitados);
        }

        [TestMethod]
        public async Task Puerta_ProductoQueElModeloNoPuntua_PasaSiempre()
        {
            var plan = new PlanRecordatorioReposicionDTO();
            plan.Enviar.Add(Correo("15191", "SINP1"));

            ResumenPuertaModeloDTO resumen = await Aplicar(plan, new ResultadoRecordatorioReposicionDTO(), ModoPuertaModeloReposicion.Activo, Puntuador());

            Assert.AreEqual(1, plan.Enviar.Single().Productos.Count);
            Assert.IsNull(plan.Enviar.Single().Productos.Single().ProbabilidadModelo);
            Assert.AreEqual(1, resumen.SinPuntuar);
        }

        [TestMethod]
        public async Task Puerta_Apagada_NiPuntua()
        {
            PlanRecordatorioReposicionDTO plan = Plan();
            IPuntuadorReposicion puntuador = Puntuador();

            await Aplicar(plan, new ResultadoRecordatorioReposicionDTO(), ModoPuertaModeloReposicion.Apagado, puntuador);

            A.CallTo(() => puntuador.ModeloDisponible()).MustNotHaveHappened();
            Assert.IsTrue(plan.Enviar.SelectMany(c => c.Productos).All(p => p.ProbabilidadModelo == null));
        }

        [TestMethod]
        public async Task Puerta_ModeloNoDisponible_ComoHoyYUnaLineaAElmahUnaVez()
        {
            PlanRecordatorioReposicionDTO plan = Plan();
            var resultado = new ResultadoRecordatorioReposicionDTO();
            IPuntuadorReposicion puntuador = Puntuador(version: null);

            ResumenPuertaModeloDTO resumen = await Aplicar(plan, resultado, ModoPuertaModeloReposicion.Activo, puntuador);
            await Aplicar(Plan(), new ResultadoRecordatorioReposicionDTO(), ModoPuertaModeloReposicion.Activo, puntuador);

            Assert.AreEqual(2, plan.Enviar.Count);
            Assert.AreEqual(3, plan.Enviar.Sum(c => c.Productos.Count));
            Assert.AreEqual(0, resultado.Descartes.Count);
            Assert.IsTrue(plan.Enviar.SelectMany(c => c.Productos).All(p => p.ProbabilidadModelo == null && p.FueraPorModelo == null));
            Assert.IsNull(resumen.VersionModelo);
            Assert.AreEqual(PuertaModeloRecordatorioReposicion.AVISO_SIN_MODELO, resumen.Aviso);
            Assert.AreEqual(1, errores.Count, "Una sola línea a ELMAH aunque pase dos veces");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Puerta_FallaAlPuntuarUnCliente_NoApuntaNiQuitaNada()
        {
            PlanRecordatorioReposicionDTO plan = Plan();
            var resultado = new ResultadoRecordatorioReposicionDTO();
            IPuntuadorReposicion puntuador = Puntuador();
            A.CallTo(() => puntuador.Puntuar(A<string>._, "15200", A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Throws(new InvalidOperationException("zip roto"));

            ResumenPuertaModeloDTO resumen = await Aplicar(plan, resultado, ModoPuertaModeloReposicion.Activo, puntuador);

            Assert.AreEqual(3, plan.Enviar.Sum(c => c.Productos.Count));
            Assert.AreEqual(0, resultado.Descartes.Count);
            Assert.IsTrue(plan.Enviar.Concat(plan.Control).SelectMany(c => c.Productos).All(p => p.ProbabilidadModelo == null),
                "Todo o nada: el 15191, ya puntuado, tampoco se apunta");
            StringAssert.Contains(resumen.Aviso, "zip roto");
            Assert.AreEqual(1, errores.Count);
        }

        #endregion

        #region Job

        private static ILectorParametrosUsuario Lector(string modoPuerta)
        {
            var lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION)).Returns("Activo");
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_VENDEDORES)).Returns("NV");
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUERTA_MODELO)).Returns(modoPuerta);
            A.CallTo(() => lector.LeerParametro("1", "(defecto)", Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_UMBRAL_MODELO)).Returns(null);
            return lector;
        }

        private static ResultadoRecordatorioReposicionDTO Resultado()
        {
            var resultado = new ResultadoRecordatorioReposicionDTO { Fecha = HOY, HistorialDesde = HOY.AddMonths(-24), TopeDias = 240 };
            resultado.SeAvisarian.Add(Correo("15191", "A", "B"));
            resultado.GrupoControl.Add(Correo("15300", "D", "E"));
            resultado.GrupoControl[0].GrupoControl = true;
            return resultado;
        }

        private DependenciasRecordatorioReposicion Dependencias(ILectorParametrosUsuario lector, IRepositorioRecordatorioReposicion repositorio,
            List<MailMessage> enviados)
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviados.Add(m)).Returns(true);
            return new DependenciasRecordatorioReposicion
            {
                Lector = lector,
                Correo = correo,
                Calcular = (c, h) => Task.FromResult(Resultado()),
                RellenarProductos = c => Task.CompletedTask,
                Repositorio = repositorio,
                Destinatarios = "carlos@nuevavision.es",
                Remitente = "nuevavision@nuevavision.es",
                PausaEntreCorreos = TimeSpan.Zero,
                Hoy = HOY,
                Ahora = () => HOY.AddHours(9),
                Puntuador = Puntuador(),
                RegistrarError = errores.Add
            };
        }

        private static IRepositorioRecordatorioReposicion Repositorio(List<CandidatoReposicionDTO> registrados, List<FilaConversionReposicion> conversion = null)
        {
            var repositorio = A.Fake<IRepositorioRecordatorioReposicion>();
            A.CallTo(() => repositorio.ExistenTablas()).Returns(true);
            A.CallTo(() => repositorio.LeerBajas("1")).Returns(new BajasCorreosPostCompra());
            A.CallTo(() => repositorio.LeerEnviosDesde("1", A<DateTime>._)).Returns(new List<EnvioPrevioReposicion>());
            A.CallTo(() => repositorio.RegistrarEnvio("1", A<RecordatorioReposicionClienteDTO>._, A<string>._, A<DateTime>._, A<int?>._, A<string>._))
                .Invokes((string e, RecordatorioReposicionClienteDTO c, string g, DateTime f, int? t, string v) => registrados.AddRange(c.Productos))
                .Returns(Task.CompletedTask);
            A.CallTo(() => repositorio.LeerConversion("1", A<DateTime>._, A<DateTime>._, A<int>._))
                .Returns(conversion ?? new List<FilaConversionReposicion>());
            return repositorio;
        }

        [TestMethod]
        public async Task Job_PuertaEnSombra_RegistraLaP1DeTodoYNoQuitaNada()
        {
            var registrados = new List<CandidatoReposicionDTO>();
            var enviados = new List<MailMessage>();

            EjecucionRecordatorioReposicionDTO ejecucion = await RecordatorioReposicionJobsService.Procesar(
                Dependencias(Lector(null), Repositorio(registrados), enviados));

            Assert.AreEqual(ModoPuertaModeloReposicion.Sombra, ejecucion.PuertaModelo.Modo, "Sin fila: Sombra");
            Assert.AreEqual(1, enviados.Count(m => m.To.Any(t => t.Address == "15191@cliente.es")));
            CollectionAssert.AreEquivalent(new[] { "A", "B", "D", "E" }, registrados.Select(r => r.Producto).ToList(),
                "Enviado y Control, sin quitar nada");
            Assert.AreEqual(true, registrados.Single(r => r.Producto == "B").FueraPorModelo);
            Assert.AreEqual(0.40f, registrados.Single(r => r.Producto == "A").ProbabilidadModelo);
            string resumen = enviados.Single(m => m.To.Any(t => t.Address == "carlos@nuevavision.es")).Body;
            StringAssert.Contains(resumen, "Puerta del modelo de propensión (Sombra");
            StringAssert.Contains(resumen, "se habrían quitado 2");
            StringAssert.Contains(resumen, "más de 240 días");
            StringAssert.Contains(resumen, "menos de 2 veces");
        }

        [TestMethod]
        public async Task Job_PuertaActiva_NoEnviaNiRegistraLosDePorDebajo()
        {
            var registrados = new List<CandidatoReposicionDTO>();
            var enviados = new List<MailMessage>();

            EjecucionRecordatorioReposicionDTO ejecucion = await RecordatorioReposicionJobsService.Procesar(
                Dependencias(Lector("Activo"), Repositorio(registrados), enviados));

            CollectionAssert.AreEquivalent(new[] { "A", "E" }, registrados.Select(r => r.Producto).ToList());
            Assert.IsTrue(ejecucion.Resultado.Descartes.Any(d => d.Producto == "B"
                && d.Motivo.StartsWith(PuertaModeloRecordatorioReposicion.MOTIVO_MODELO_PREFIJO)));
        }

        [TestMethod]
        public async Task Job_ConEnviosDeHaceMasDe30Dias_ElResumenTraeLaConversion()
        {
            var filas = new List<FilaConversionReposicion>
            {
                Fila(HOY.AddDays(-35), "Enviado", 1),
                Fila(HOY.AddDays(-35), "Control", 0)
            };
            var enviados = new List<MailMessage>();

            EjecucionRecordatorioReposicionDTO ejecucion = await RecordatorioReposicionJobsService.Procesar(
                Dependencias(Lector(null), Repositorio(new List<CandidatoReposicionDTO>(), filas), enviados));

            Assert.AreEqual(2, ejecucion.Conversion.EnviosCerrados);
            string resumen = enviados.Single(m => m.To.Any(t => t.Address == "carlos@nuevavision.es")).Body;
            StringAssert.Contains(resumen, "¿Convierte?");
            StringAssert.Contains(resumen, "100,0 %");
        }

        [TestMethod]
        public async Task Job_SinEnviosConLaVentanaCerrada_ElResumenNoTraeConversion()
        {
            var enviados = new List<MailMessage>();

            EjecucionRecordatorioReposicionDTO ejecucion = await RecordatorioReposicionJobsService.Procesar(
                Dependencias(Lector(null), Repositorio(new List<CandidatoReposicionDTO>()), enviados));

            Assert.IsNull(ejecucion.Conversion);
            Assert.IsFalse(enviados.Single(m => m.To.Any(t => t.Address == "carlos@nuevavision.es")).Body.Contains("¿Convierte?"));
        }

        #endregion

        #region Informe de conversión

        private static FilaConversionReposicion Fila(DateTime fecha, string grupo, int lineas, int diasDesde = 40, int intervalo = 30,
            decimal? p1 = null)
            => new FilaConversionReposicion
            {
                Fecha = fecha.AddHours(9),
                Cliente = "15191",
                Producto = "A",
                Grupo = grupo,
                UltimaCompra = fecha.AddDays(-diasDesde),
                IntervaloDias = intervalo,
                ProbabilidadModelo = p1,
                Lineas = lineas
            };

        [TestMethod]
        public void Conversion_EnviadoFrenteAControl_PorTramos()
        {
            DateTime envio = HOY.AddDays(-40);
            var filas = new List<FilaConversionReposicion>
            {
                Fila(envio, "Enviado", 2, diasDesde: 40, intervalo: 30, p1: 0.40m),
                Fila(envio, "Enviado", 0, diasDesde: 40, intervalo: 30, p1: 0.02m),
                Fila(envio, "Enviado", 1, diasDesde: 200, intervalo: 150, p1: 0.20m),
                Fila(envio, "Enviado", 0, diasDesde: 250, intervalo: 150),
                Fila(envio, "Control", 1, diasDesde: 40, intervalo: 30, p1: 0.40m),
                Fila(envio, "Control", 0, diasDesde: 200, intervalo: 150, p1: 0.01m),
                Fila(HOY.AddDays(-10), "Enviado", 1)   // ventana abierta: no cuenta
            };

            InformeConversionReposicionDTO informe = InformeConversionRecordatorioReposicion.Calcular(filas, HOY.AddMonths(-6), 30, HOY);

            Assert.AreEqual(6, informe.EnviosCerrados);
            Assert.AreEqual(1, informe.EnviosEnCurso);
            Assert.AreEqual(4, informe.Total.EnviadoProductos);
            Assert.AreEqual(2, informe.Total.EnviadoRecomprados);
            Assert.AreEqual(50.0m, informe.Total.EnviadoPorcentaje);
            Assert.AreEqual(50.0m, informe.Total.ControlPorcentaje);
            Assert.AreEqual(0m, informe.Total.DiferenciaPuntos);

            CollectionAssert.AreEqual(new[] { "< 60 días", "180-239 días", "240 días o más" }, informe.PorAntiguedad.Select(t => t.Tramo).ToList());
            ConversionReposicionTramoDTO recientes = informe.PorAntiguedad.First();
            Assert.AreEqual(50.0m, recientes.EnviadoPorcentaje);
            Assert.AreEqual(100.0m, recientes.ControlPorcentaje);
            Assert.AreEqual(-50.0m, recientes.DiferenciaPuntos);
            Assert.IsNull(informe.PorAntiguedad.Last().ControlPorcentaje, "Sin control en el tramo");
            Assert.IsNull(informe.PorAntiguedad.Last().DiferenciaPuntos);

            CollectionAssert.AreEqual(new[] { "30-59 días", "120 días o más" }, informe.PorIntervalo.Select(t => t.Tramo).ToList());

            CollectionAssert.AreEqual(new[] { "< 0,05", "0,15-0,30", "0,30 o más", "Sin P1" }, informe.PorProbabilidad.Select(t => t.Tramo).ToList());
            Assert.AreEqual(100.0m, informe.PorProbabilidad.Single(t => t.Tramo == "0,30 o más").EnviadoPorcentaje);
        }

        [TestMethod]
        public void Conversion_SinP1EnNingunaFila_NoDesglosaPorP1()
        {
            var filas = new List<FilaConversionReposicion> { Fila(HOY.AddDays(-40), "Enviado", 1) };

            InformeConversionReposicionDTO informe = InformeConversionRecordatorioReposicion.Calcular(filas, HOY.AddMonths(-6), 30, HOY);

            Assert.AreEqual(0, informe.PorProbabilidad.Count);
            Assert.IsNull(informe.Total.ControlPorcentaje);
        }

        [TestMethod]
        public void Conversion_VentanaCerrada_JustoAlCumplirLosDias()
        {
            Assert.IsTrue(InformeConversionRecordatorioReposicion.VentanaCerrada(HOY.AddDays(-30).AddHours(9), 30, HOY));
            Assert.IsFalse(InformeConversionRecordatorioReposicion.VentanaCerrada(HOY.AddDays(-29), 30, HOY));
        }

        [TestMethod]
        public void SqlConversion_SinColumnasDelModelo_DevuelveNull()
        {
            string sin = RepositorioRecordatorioReposicionSql.SqlConversion(false);
            string con = RepositorioRecordatorioReposicionSql.SqlConversion(true);

            StringAssert.Contains(sin, "CAST(NULL AS decimal(9, 6)) AS ProbabilidadModelo");
            Assert.IsFalse(sin.Contains("e.ProbabilidadModelo"));
            StringAssert.Contains(con, "e.ProbabilidadModelo");
            StringAssert.Contains(con, "e.FueraPorModelo AS FueraPorModelo");
            StringAssert.Contains(con, "l.Estado >= -1");
            Assert.IsFalse(con.ToUpperInvariant().Contains("UPDATE") || con.ToUpperInvariant().Contains("INSERT"), "Solo lee");
        }

        private static RecordatorioReposicionController Controller(string grupo, IRepositorioRecordatorioReposicion repositorio)
        {
            var controller = new RecordatorioReposicionController(() => A.Fake<ISelectorRecordatoriosReposicion>(), A.Fake<ILectorParametrosUsuario>(),
                A.Fake<IServicioCorreoElectronico>(), () => repositorio)
            {
                Hoy = () => HOY,
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { grupo });
            return controller;
        }

        [TestMethod]
        public async Task GetConversion_SinSerDireccionNiInformatica_Forbidden()
        {
            IRepositorioRecordatorioReposicion repositorio = A.Fake<IRepositorioRecordatorioReposicion>();

            var resultado = await Controller("NUEVAVISION\\Vendedores", repositorio).GetConversion() as StatusCodeResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.LeerConversion(A<string>._, A<DateTime>._, A<DateTime>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetConversion_PorDefecto_SeisMesesY30Dias()
        {
            IRepositorioRecordatorioReposicion repositorio = A.Fake<IRepositorioRecordatorioReposicion>();
            A.CallTo(() => repositorio.LeerConversion(A<string>._, A<DateTime>._, A<DateTime>._, A<int>._))
                .Returns(new List<FilaConversionReposicion> { Fila(HOY.AddDays(-40), "Enviado", 1), Fila(HOY.AddDays(-40), "Control", 0) });

            var resultado = await Controller("NUEVAVISION\\Dirección", repositorio).GetConversion()
                as OkNegotiatedContentResult<InformeConversionReposicionDTO>;

            A.CallTo(() => repositorio.LeerConversion("1", HOY.AddMonths(-6), HOY.AddDays(1), 30)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(100.0m, resultado.Content.Total.EnviadoPorcentaje);
            Assert.AreEqual(0.0m, resultado.Content.Total.ControlPorcentaje);
        }

        [TestMethod]
        public async Task GetConversion_DiasFueraDeRango_BadRequest()
        {
            var resultado = await Controller("NUEVAVISION\\Informatica", A.Fake<IRepositorioRecordatorioReposicion>()).GetConversion(dias: 0);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        #endregion
    }
}
