using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Evaluacion;
using ModeloSugerenciasProductos.Features;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#671 (corte 2d): entrenamiento mensual del modelo de sugerencias de productos como job de Hangfire. El núcleo
    /// (instantáneas, métricas, puerta) se prueba en ModeloSugerenciasProductos.Tests; aquí, lo que añade la API: cuándo toca,
    /// la guarda horaria, qué se guarda y qué se avisa en cada caso, el endpoint y que se puede entrenar de verdad dentro de un
    /// proceso .NET Framework con la librería nativa de LightGBM.
    /// </summary>
    [TestClass]
    public class ReentrenamientoModeloSugerenciasTests
    {
        private string carpeta;
        private AlmacenModeloSugerencias almacen;
        private IEntrenadorModeloSugerencias entrenador;
        private IEvaluacionModeloSugerencias evaluacion;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> errores;
        private Dictionary<string, string> parametros;
        private int memoriaLiberada;
        private DateTime ahora;
        private static readonly DateTime SegundoSabado = new DateTime(2026, 11, 14, 2, 30, 0);

        [TestInitialize]
        public void Inicializar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "NestoAPI671d_" + Guid.NewGuid().ToString("N"));
            almacen = new AlmacenModeloSugerencias(carpeta);
            entrenador = A.Fake<IEntrenadorModeloSugerencias>();
            evaluacion = A.Fake<IEvaluacionModeloSugerencias>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            errores = new List<Exception>();
            parametros = new Dictionary<string, string>();
            memoriaLiberada = 0;
            ahora = SegundoSabado;
            _ = A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._)).Returns(evaluacion);
            _ = A.CallTo(() => evaluacion.DatosSuficientes).Returns(true);
            _ = A.CallTo(() => evaluacion.Modelo).Returns(new ResultadoRanking { Modelo = "m", P3 = 0.153, R3 = 0.21, Ndcg3 = 0.18, AlMenos1 = 0.35 });
            _ = A.CallTo(() => evaluacion.Reglas).Returns(new ResultadoRanking { Modelo = "reglas-1", P3 = 0.121, Ndcg3 = 0.15 });
            _ = A.CallTo(() => evaluacion.InformeMetricas()).Returns("INFORME");
            _ = A.CallTo(() => evaluacion.Readme(A<string>._, A<string>._)).ReturnsLazily((string r, string c) => "README " + Path.GetFileName(r));
            _ = A.CallTo(() => evaluacion.Metadatos(A<DateTime>._, A<string>._, A<string>._)).ReturnsLazily((DateTime f, string o, string r) =>
                new MetadatosModelo { Fecha = f, Origen = o, Fichero = Path.GetFileName(r), PasaPuerta = true });
            _ = A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).Invokes((string ruta) => File.WriteAllText(ruta, "zip"));
        }

        [TestCleanup]
        public void Limpiar()
        {
            try
            {
                if (Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        private ReentrenamientoModeloSugerenciasJobsService Servicio(Action asegurarNativo = null, IEntrenadorModeloSugerencias otro = null)
            => new ReentrenamientoModeloSugerenciasJobsService(otro ?? entrenador, almacen, notificaciones, () => ahora,
                clave => parametros.TryGetValue(clave, out string v) ? v : null, asegurarNativo ?? (() => { }), errores.Add,
                () => memoriaLiberada++);

        private void Decide(bool pasa, string motivo = "P@3 12,1 % → 15,3 % (+3,2 puntos), NDCG@3 no peor")
            => A.CallTo(() => evaluacion.Decision).Returns(new DecisionPuerta(pasa, motivo));

        private void ComprobarAviso(params string[] contiene)
            => A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == ReentrenamientoModeloSugerenciasJobsService.TITULO_AVISO
                    && contiene.All(c => n.Cuerpo.Contains(c)) && n.Datos["tipo"] == NotificacionesController.TIPO_AVISO_NESTO)))
                .MustHaveHappenedOnceExactly();

        // Cuándo toca

        [TestMethod]
        public void TocaEntrenar_SoloElSegundoSabadoDelMesDeMadrugada()
        {
            Assert.AreEqual(DayOfWeek.Saturday, new DateTime(2026, 10, 10).DayOfWeek);
            Assert.IsTrue(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 10, 10, 2, 30, 0)), "día 10: segundo sábado");
            Assert.IsTrue(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 11, 14, 2, 30, 0)), "día 14: aún es el segundo");
            Assert.IsTrue(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 8, 8, 2, 30, 0)), "día 8: ya es el segundo");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 10, 3, 2, 30, 0)), "primer sábado: el de llamadas");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 11, 7, 2, 30, 0)), "día 7: aún es el primero");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 10, 17, 2, 30, 0)), "tercer sábado");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 10, 11, 2, 30, 0)), "domingo");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.TocaEntrenar(new DateTime(2026, 10, 10, 9, 0, 0)), "arrancado tarde");
        }

        [TestMethod]
        public void Cron_TodosLosSabadosALasDosYMediaEnLaColaDeEntrenamiento()
        {
            Assert.AreEqual("30 2 * * 6", ReentrenamientoModeloSugerenciasJobsService.CRON);
            Assert.AreEqual("entrenamiento", ReentrenamientoModeloSugerenciasJobsService.COLA);
            Assert.AreEqual("entrenar-modelo-sugerencias", ReentrenamientoModeloSugerenciasJobsService.ID_JOB);
            foreach (string metodo in new[] { nameof(ReentrenamientoModeloSugerenciasJobsService.EntrenarProgramado), nameof(ReentrenamientoModeloSugerenciasJobsService.ReentrenarManual) })
            {
                System.Reflection.MethodInfo info = typeof(ReentrenamientoModeloSugerenciasJobsService).GetMethod(metodo);
                // Por nombre: el proyecto de tests no referencia Hangfire.
                object reintentos = info.GetCustomAttributes(false).Single(a => a.GetType().Name == "AutomaticRetryAttribute");
                object cola = info.GetCustomAttributes(false).Single(a => a.GetType().Name == "QueueAttribute");
                Assert.AreEqual(0, reintentos.GetType().GetProperty("Attempts").GetValue(reintentos), metodo + ": sin reintentos");
                Assert.AreEqual("entrenamiento", cola.GetType().GetProperty("Queue").GetValue(cola), metodo);
            }
        }

        // Guarda horaria

        [TestMethod]
        public void EnHorario_DeLunesAViernesDeSieteANueve()
        {
            Assert.IsTrue(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 20, 7, 0, 0)), "martes 7:00");
            Assert.IsTrue(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 23, 20, 59, 0)), "viernes 20:59");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 20, 6, 59, 0)), "martes 6:59");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 20, 21, 0, 0)), "martes 21:00");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 24, 12, 0, 0)), "sábado");
            Assert.IsFalse(ReentrenamientoModeloSugerenciasJobsService.EnHorario(new DateTime(2026, 10, 25, 12, 0, 0)), "domingo");
        }

        [TestMethod]
        public void Ejecutar_EnHorarioSinForzar_NoEntrenaLoApuntaYAvisa()
        {
            ahora = new DateTime(2026, 10, 20, 10, 15, 0); // martes

            EstadoEntrenamientoSugerencias estado = Servicio().Ejecutar(forzar: false, "a mano (Carlos)");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.EnHorario, estado);
            A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._)).MustNotHaveHappened();
            ComprobarAviso("no se ha entrenado", "10:15", "forzar=true");
            Assert.IsFalse(Directory.Exists(carpeta), "no se escribe nada");
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void Ejecutar_EnHorarioForzado_Entrena()
        {
            ahora = new DateTime(2026, 10, 20, 10, 15, 0);
            Decide(false);

            EstadoEntrenamientoSugerencias estado = Servicio().Ejecutar(forzar: true, "a mano (Carlos)");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.NoPasaria, estado);
            A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ProximaFranja_LaborableEsperaALasOnceYMedia()
        {
            Assert.AreEqual(new DateTime(2026, 10, 20, 23, 30, 0), ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(new DateTime(2026, 10, 20, 10, 15, 0)), "martes en horario");
            Assert.AreEqual(new DateTime(2026, 10, 20, 23, 30, 0), ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(new DateTime(2026, 10, 20, 6, 0, 0)), "martes 6:00: no daría tiempo");
            Assert.AreEqual(new DateTime(2026, 10, 20, 22, 0, 0), ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(new DateTime(2026, 10, 20, 22, 0, 0)), "martes 22:00: ya");
            Assert.AreEqual(new DateTime(2026, 10, 20, 1, 0, 0), ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(new DateTime(2026, 10, 20, 1, 0, 0)), "martes 1:00: ya");
            Assert.AreEqual(new DateTime(2026, 10, 24, 12, 0, 0), ReentrenamientoModeloSugerenciasJobsService.ProximaFranja(new DateTime(2026, 10, 24, 12, 0, 0)), "sábado: ya");
        }

        // Qué hace el job

        [TestMethod]
        public void Ejecutar_Pasaria_EntrenaConLasOpcionesPorDefectoGuardaTodoConFechaYAvisaSinActivar()
        {
            Decide(true);

            EstadoEntrenamientoSugerencias estado = Servicio().Ejecutar(forzar: false, "job mensual");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.Pasaria, estado);
            A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>.That.Matches(o => o.Meses == 36 && o.MuestraClientes == 0 && o.Instantaneas == 18),
                SegundoSabado.Date, A<Action<string>>._)).MustHaveHappenedOnceExactly();
            string sufijo = "20261114_0230";
            Assert.AreEqual("zip", File.ReadAllText(almacen.RutaModelo(sufijo)));
            Assert.AreEqual("INFORME", File.ReadAllText(almacen.RutaInforme(sufijo)));
            Assert.AreEqual("README modelo_sugerencias_productos_20261114_0230.zip", File.ReadAllText(almacen.RutaReadme(sufijo)));
            MetadatosModelo meta = JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(almacen.RutaMetadatos(sufijo)));
            Assert.AreEqual("job mensual", meta.Origen);
            Assert.IsTrue(File.Exists(almacen.RutaLog(sufijo)));
            Assert.AreEqual(5, Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories).Length, "zip, json, README, informe y log; nada más (nada «activo»)");
            ComprobarAviso("PASARÍA", "Las sugerencias ya lo usan", "P@3 15,3 %", "reglas-1 12,1 %", "NDCG@3 0,180", almacen.RutaInforme(sufijo));
            Assert.AreEqual(0, errores.Count);
            Assert.AreEqual(1, memoriaLiberada);
        }

        [TestMethod]
        public void Ejecutar_NoPasaria_GuardaIgualYLoDice()
        {
            Decide(false, "P@3 12,1 % → 12,5 % (+0,4 puntos; hacen falta 2)");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.NoPasaria, Servicio().Ejecutar(false, "job mensual"));

            Assert.IsTrue(File.Exists(almacen.RutaModelo("20261114_0230")), "se guarda para poder mirarlo");
            ComprobarAviso("NO pasaría", "hacen falta 2");
        }

        [TestMethod]
        public void Ejecutar_DatosInsuficientes_SoloInformeYAviso()
        {
            _ = A.CallTo(() => evaluacion.DatosSuficientes).Returns(false);
            Decide(false, "no hay datos suficientes para entrenar y validar");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.DatosInsuficientes, Servicio().Ejecutar(false, "job mensual"));

            A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).MustNotHaveHappened();
            Assert.IsTrue(File.Exists(almacen.RutaInforme("20261114_0230")));
            ComprobarAviso("no hay datos suficientes");
        }

        [TestMethod]
        public void Ejecutar_FallaElEntrenamiento_VaAElmahAvisaYNoLanza()
        {
            _ = A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._))
                .Throws(new TimeoutException("Tiempo de espera agotado"));

            EstadoEntrenamientoSugerencias estado = Servicio().Ejecutar(false, "job mensual");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.Error, estado);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "Tiempo de espera agotado");
            ComprobarAviso("error", "Tiempo de espera agotado", "ELMAH");
            StringAssert.Contains(File.ReadAllText(almacen.RutaLog("20261114_0230")), "TimeoutException");
            Assert.AreEqual(1, memoriaLiberada, "la memoria se libera también si falla");
        }

        [TestMethod]
        public void Ejecutar_SinLibreriaNativa_ErrorSinLeerLaBd()
        {
            EstadoEntrenamientoSugerencias estado = Servicio(() => throw new FileNotFoundException("no está lib_lightgbm.dll")).Ejecutar(false, "job mensual");

            Assert.AreEqual(EstadoEntrenamientoSugerencias.Error, estado);
            A.CallTo(() => entrenador.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._)).MustNotHaveHappened();
            ComprobarAviso("lib_lightgbm.dll");
        }

        [TestMethod]
        public void Ejecutar_FallaElAviso_VaAElmahYElResultadoNoCambia()
        {
            Decide(true);
            _ = A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Throws(new Exception("SignalR caído"));

            Assert.AreEqual(EstadoEntrenamientoSugerencias.Pasaria, Servicio().Ejecutar(false, "job mensual"));
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "PASARÍA");
        }

        [TestMethod]
        public void Destinatarios_YCarpeta_DelWebConfigOPorDefecto()
        {
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Carlos" }, Servicio().Destinatarios());
            parametros[ReentrenamientoModeloSugerenciasJobsService.CLAVE_USUARIOS_AVISO] = "Carlos; NUEVAVISION\\Laura";
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Carlos", "NUEVAVISION\\Laura" }, Servicio().Destinatarios());

            Assert.AreEqual(@"C:\NestoAPI\ModelosIA\Sugerencias", AlmacenModeloSugerencias.CarpetaConfigurada(_ => null));
            Assert.AreEqual(@"D:\Modelos", AlmacenModeloSugerencias.CarpetaConfigurada(c => c == "ModeloSugerencias:Carpeta" ? @" D:\Modelos " : null));
        }

        // Endpoint

        private List<(bool forzar, string usuario, DateTime cuando)> programados;

        private SugerenciasProductosController Controller(DateTime ahoraController, string grupo = "NUEVAVISION\\Dirección", Func<bool, string, DateTime, string> programar = null)
        {
            programados = new List<(bool, string, DateTime)>();
            var controller = new SugerenciasProductosController(null, programar ?? ((f, u, c) =>
            {
                programados.Add((f, u, c));
                return "77";
            }), () => ahoraController)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            if (grupo != null)
            {
                controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { grupo });
            }
            return controller;
        }

        private static HttpStatusCode Codigo(IHttpActionResult resultado) => resultado.ExecuteAsync(default).Result.StatusCode;

        [TestMethod]
        public void ReentrenarModelo_SinSerDireccionNiInformatica_Forbidden()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0), "NUEVAVISION\\Vendedores");

            IHttpActionResult resultado = controller.PostReentrenarModelo();

            Assert.IsInstanceOfType(resultado, typeof(StatusCodeResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((StatusCodeResult)resultado).StatusCode);
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void ReentrenarModelo_SinUsuario_Forbidden()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0), grupo: null);

            Assert.AreEqual(HttpStatusCode.Forbidden, Codigo(controller.PostReentrenarModelo()));
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void ReentrenarModelo_CuandoEnHorario_400ConElMotivo()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0));

            IHttpActionResult resultado = controller.PostReentrenarModelo(new DateTime(2026, 10, 21, 12, 0, 0));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "de lunes a viernes de 7:00 a 21:00");
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void ReentrenarModelo_CuandoEnElPasado_400()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 22, 0, 0));

            Assert.IsInstanceOfType(controller.PostReentrenarModelo(new DateTime(2026, 10, 19, 23, 0, 0)), typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public void ReentrenarModelo_SinCuandoEnHorario_SeProgramaParaLasOnceYMediaY202()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0), "NUEVAVISION\\Informatica");

            IHttpActionResult resultado = controller.PostReentrenarModelo();

            Assert.AreEqual(HttpStatusCode.Accepted, Codigo(resultado));
            Assert.AreEqual(1, programados.Count);
            Assert.AreEqual((false, "NUEVAVISION\\Carlos", new DateTime(2026, 10, 20, 23, 30, 0)), programados[0]);
            string cuerpo = resultado.ExecuteAsync(default).Result.Content.ReadAsStringAsync().Result;
            StringAssert.Contains(cuerpo, "\"JobId\":\"77\"");
            StringAssert.Contains(cuerpo, "2026-10-20T23:30:00");
        }

        [TestMethod]
        public void ReentrenarModelo_ConCuandoFueraDeHorario_SeProgramaAEsaHora()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0));

            Assert.AreEqual(HttpStatusCode.Accepted, Codigo(controller.PostReentrenarModelo(new DateTime(2026, 10, 24, 3, 0, 0))));
            Assert.AreEqual((false, "NUEVAVISION\\Carlos", new DateTime(2026, 10, 24, 3, 0, 0)), programados[0]);
        }

        [TestMethod]
        public void ReentrenarModelo_SinCuandoFueraDeHorario_Ya()
        {
            DateTime sabado = new DateTime(2026, 10, 24, 12, 0, 0);
            SugerenciasProductosController controller = Controller(sabado);

            Assert.AreEqual(HttpStatusCode.Accepted, Codigo(controller.PostReentrenarModelo()));
            Assert.AreEqual(sabado, programados[0].cuando);
        }

        [TestMethod]
        public void ReentrenarModelo_Forzar_YaYConElJobForzado()
        {
            DateTime martes = new DateTime(2026, 10, 20, 10, 0, 0);
            SugerenciasProductosController controller = Controller(martes);

            Assert.AreEqual(HttpStatusCode.Accepted, Codigo(controller.PostReentrenarModelo(forzar: true)));
            Assert.AreEqual((true, "NUEVAVISION\\Carlos", martes), programados[0]);
        }

        [TestMethod]
        public void ReentrenarModelo_SinHangfire_503()
        {
            SugerenciasProductosController controller = Controller(new DateTime(2026, 10, 20, 10, 0, 0),
                programar: (f, u, c) => throw new InvalidOperationException("JobStorage.Current property value has not been initialized"));

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, Codigo(controller.PostReentrenarModelo()));
        }

        // Entrenar de verdad en .NET Framework

        [TestMethod]
        public void EntrenarEnNet48_ConLightGbmNativo_ElJobGuardaUnZipQueSeCarga()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo funciona en 64 bits y el proceso de los tests es de 32");
            }
            string cargada = CargadorLightGbmNativo.Asegurar();
            StringAssert.EndsWith(cargada, "lib_lightgbm.dll");

            var real = A.Fake<IEntrenadorModeloSugerencias>();
            _ = A.CallTo(() => real.Evaluar(A<OpcionesEntrenamiento>._, A<DateTime>._, A<Action<string>>._))
                .ReturnsLazily((OpcionesEntrenamiento op, DateTime hoy, Action<string> log) =>
                {
                    // Las opciones son las de producción; aquí, más ligeras para que el test tarde segundos.
                    Assert.AreEqual(36, op.Meses);
                    var ligeras = new OpcionesEntrenamiento { Iteraciones = 40, MinimoEjemplosPorHoja = 10, EvaluarLambdaRank = false };
                    VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, ligeras);
                    return new EntrenadorModeloSugerencias.Evaluacion(ProcesoEntrenamiento.Evaluar(DatosSinteticos(v.DatosDesde, v.DatosHasta), ligeras, hoy, log));
                });

            EstadoEntrenamientoSugerencias estado = Servicio(asegurarNativo: () => CargadorLightGbmNativo.Asegurar(), otro: real).Ejecutar(false, "test");

            Assert.IsTrue(estado == EstadoEntrenamientoSugerencias.Pasaria || estado == EstadoEntrenamientoSugerencias.NoPasaria,
                $"{estado}: {string.Join(" | ", errores.Select(e => e.ToString()))}");
            string sufijo = "20261114_0230";
            string zip = almacen.RutaModelo(sufijo);
            Assert.IsTrue(File.Exists(zip));
            // EntrenarFinalYGuardar ya lo ha cargado y puntuado (ida y vuelta) con ModeloSugerenciaEntrada; si no, habría lanzado.
            Assert.IsTrue(new FileInfo(zip).Length > 1000, "el zip tiene un modelo de verdad");
            MetadatosModelo meta = JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(almacen.RutaMetadatos(sufijo)));
            Assert.AreEqual(Path.GetFileName(zip), meta.Fichero);
            Assert.IsTrue(meta.P3.HasValue);
            StringAssert.Contains(File.ReadAllText(almacen.RutaInforme(sufijo)), "| reglas-1 |");
            StringAssert.Contains(File.ReadAllText(almacen.RutaReadme(sufijo)), "entrenar-modelo-sugerencias");
        }

        /// <summary>Lo mismo que DatosSinteticos de la consola: 60 clientes de 4 tipos que compran sobre todo su subgrupo, con pares.</summary>
        internal static DatosCrudos DatosSinteticos(DateTime desde, DateTime hasta, int clientes = 60, int productos = 48)
        {
            var datos = new DatosCrudos();
            for (int i = 0; i < productos; i++)
            {
                string p = $"P{i:00}";
                datos.Fichas[p] = new FichaProducto { Producto = p, Nombre = "Producto " + i, Grupo = "COS", SubGrupo = $"S{i % 4}", Familia = $"F{i % 6}" };
            }
            var azar = new Random(671);
            for (int c = 0; c < clientes; c++)
            {
                string cliente = $"C{c:000}";
                List<int> favoritos = Enumerable.Range(0, productos).Where(i => i % 4 == c % 4).ToList();
                int ritmo = 15 + (c % 5) * 7;
                DateTime dia = desde.AddDays(azar.Next(ritmo));
                while (dia < hasta)
                {
                    var cesta = new HashSet<string>();
                    int n = 2 + azar.Next(4);
                    for (int k = 0; k < n; k++)
                    {
                        int i = azar.NextDouble() < 0.8 ? favoritos[azar.Next(favoritos.Count)] : azar.Next(productos);
                        _ = cesta.Add($"P{i:00}");
                        if (i + 4 < productos && azar.NextDouble() < 0.6)
                        {
                            _ = cesta.Add($"P{i + 4:00}");
                        }
                    }
                    foreach (string p in cesta.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        datos.Compras.Add(new Compra { Cliente = cliente, Producto = p, Dia = dia, BaseImponible = 10 + azar.Next(90), Cantidad = 1 + azar.Next(3) });
                    }
                    dia = dia.AddDays(Math.Max(1, ritmo + azar.Next(-5, 6)));
                }
            }
            return datos;
        }
    }
}
