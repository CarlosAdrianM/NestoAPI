using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloLlamadaPedido.Datos;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Reentrenamiento;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModeloContactoEntrada = ModeloLlamadaPedido.Features.ModeloContactoEntrada;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#619: reentrenamiento mensual del modelo de llamadas como job de Hangfire. La puerta de calidad (función pura)
    /// se prueba en ModeloLlamadaPedido.Tests\PuertaCalidadTests.cs, junto al núcleo; aquí, lo que añade la API: cuándo toca,
    /// la promoción atómica con el anterior guardado, qué se hace y qué se avisa en cada caso, y que se puede entrenar de verdad
    /// dentro de un proceso .NET Framework con la librería nativa de LightGBM.
    /// </summary>
    [TestClass]
    public class ReentrenamientoModeloLlamadasTests
    {
        private string carpeta;
        private string rutaDeploy;
        private AlmacenModeloLlamadas almacen;
        private IEntrenadorModeloLlamadas entrenador;
        private IEvaluacionModeloLlamadas evaluacion;
        private IServicioNotificacionesPush notificaciones;
        private List<Exception> errores;
        private Dictionary<string, string> parametros;
        private static readonly DateTime Ahora = new DateTime(2026, 11, 7, 2, 30, 0); // primer sábado de noviembre

        [TestInitialize]
        public void Inicializar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "NestoAPI619_" + Guid.NewGuid().ToString("N"));
            string deploy = Path.Combine(Path.GetTempPath(), "NestoAPI619_deploy_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(deploy);
            rutaDeploy = Path.Combine(deploy, "modelo_llamadas.zip");
            File.WriteAllText(rutaDeploy, "deploy");
            almacen = new AlmacenModeloLlamadas(carpeta, rutaDeploy);
            entrenador = A.Fake<IEntrenadorModeloLlamadas>();
            evaluacion = A.Fake<IEvaluacionModeloLlamadas>();
            notificaciones = A.Fake<IServicioNotificacionesPush>();
            errores = new List<Exception>();
            parametros = new Dictionary<string, string>();
            _ = A.CallTo(() => entrenador.Evaluar(A<OpcionesReentrenamiento>._, A<DateTime>._)).Returns(evaluacion);
            _ = A.CallTo(() => evaluacion.Comparacion).Returns("P@20 60,0 % → 62,0 %, AUC 0,740 → 0,750");
            _ = A.CallTo(() => evaluacion.Readme(A<string>._, A<string>._)).ReturnsLazily((string r, string res) => $"README {res}");
            _ = A.CallTo(() => evaluacion.Metadatos(A<DateTime>._, A<string>._)).ReturnsLazily((DateTime f, string o) =>
                new MetadatosModelo { Fecha = f, ContactosHasta = new DateTime(2026, 10, 30), Origen = o, Motivo = "pasa" });
            _ = A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).Invokes((string ruta) => File.WriteAllText(ruta, "nuevo"));
        }

        [TestCleanup]
        public void Limpiar()
        {
            foreach (string c in new[] { carpeta, Path.GetDirectoryName(rutaDeploy) })
            {
                try
                {
                    if (Directory.Exists(c))
                    {
                        Directory.Delete(c, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        private ReentrenamientoModeloLlamadasJobsService Servicio(Action asegurarNativo = null)
            => new ReentrenamientoModeloLlamadasJobsService(entrenador, almacen, notificaciones, () => Ahora,
                clave => parametros.TryGetValue(clave, out string v) ? v : null, asegurarNativo ?? (() => { }), errores.Add);

        private void Decide(bool promover, string motivo = "P@20 60,0 % → 62,0 %, AUC 0,740 → 0,750")
            => A.CallTo(() => evaluacion.Decision).Returns(new DecisionPuerta(promover, motivo));

        private void ComprobarAviso(string contiene)
            => A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Titulo == ReentrenamientoModeloLlamadasJobsService.TITULO_AVISO
                    && n.Cuerpo.Contains(contiene) && n.Datos["tipo"] == NotificacionesController.TIPO_AVISO_NESTO)))
                .MustHaveHappenedOnceExactly();

        // Cuándo toca

        [TestMethod]
        public void TocaReentrenar_SoloElPrimerSabadoDelMesDeMadrugada()
        {
            Assert.AreEqual(DayOfWeek.Saturday, new DateTime(2026, 10, 3).DayOfWeek);
            Assert.IsTrue(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 3, 2, 30, 0)));
            Assert.IsTrue(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 11, 7, 2, 30, 0)), "día 7: aún es el primero");
            Assert.IsFalse(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 10, 2, 30, 0)), "segundo sábado");
            Assert.IsFalse(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 31, 2, 30, 0)), "último sábado");
            Assert.IsFalse(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 4, 2, 30, 0)), "domingo: precios medios");
            Assert.IsFalse(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 2, 2, 30, 0)), "viernes");
            Assert.IsFalse(ReentrenamientoModeloLlamadasJobsService.TocaReentrenar(new DateTime(2026, 10, 3, 9, 0, 0)), "arrancado tarde: sería en horario");
        }

        [TestMethod]
        public void Cron_TodosLosSabadosALasDosYMedia()
        {
            Assert.AreEqual("30 2 * * 6", ReentrenamientoModeloLlamadasJobsService.CRON);
            Assert.AreEqual("entrenamiento", ReentrenamientoModeloLlamadasJobsService.COLA);
        }

        // Promoción atómica

        [TestMethod]
        public void Promover_PrimeraVez_ElDelDeployQuedaComoAnterior()
        {
            Assert.AreEqual(rutaDeploy, almacen.RutaModeloActivo, "sin modelo en la carpeta, manda el del deploy");
            string temporal = almacen.NuevaRutaTemporal();
            File.WriteAllText(temporal, "nuevo");

            almacen.Promover(temporal, new MetadatosModelo { ContactosHasta = new DateTime(2026, 10, 30) }, "readme");

            Assert.AreEqual(almacen.RutaModelo, almacen.RutaModeloActivo);
            Assert.AreEqual("nuevo", File.ReadAllText(almacen.RutaModelo));
            Assert.AreEqual("deploy", File.ReadAllText(almacen.RutaAnterior));
            Assert.AreEqual("deploy", File.ReadAllText(rutaDeploy), "el del deploy no se toca");
            Assert.IsFalse(File.Exists(temporal));
            Assert.AreEqual("readme", File.ReadAllText(almacen.RutaReadme));
            Assert.AreEqual(new DateTime(2026, 10, 30), JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(almacen.RutaMetadatos)).ContactosHasta);
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length, "no quedan temporales");
        }

        [TestMethod]
        public void Promover_ConModeloPrevio_LoCambiaYGuardaElAnteriorConSusMetadatos()
        {
            string t1 = almacen.NuevaRutaTemporal();
            File.WriteAllText(t1, "octubre");
            almacen.Promover(t1, new MetadatosModelo { ContactosHasta = new DateTime(2026, 9, 26) }, "r1");
            File.WriteAllText(almacen.RutaAnterior, "viejo que se pisa");
            DateTime antes = File.GetLastWriteTimeUtc(almacen.RutaModelo);
            System.Threading.Thread.Sleep(20);

            string t2 = almacen.NuevaRutaTemporal();
            File.WriteAllText(t2, "noviembre");
            almacen.Promover(t2, new MetadatosModelo { ContactosHasta = new DateTime(2026, 10, 30) }, "r2");

            Assert.AreEqual("noviembre", File.ReadAllText(almacen.RutaModelo));
            Assert.AreEqual("octubre", File.ReadAllText(almacen.RutaAnterior));
            Assert.IsTrue(File.GetLastWriteTimeUtc(almacen.RutaModelo) > antes, "la API recarga por la fecha del fichero");
            Assert.AreEqual(new DateTime(2026, 10, 30), almacen.LeerMetadatos().ContactosHasta);
            Assert.AreEqual(new DateTime(2026, 9, 26), JsonConvert.DeserializeObject<MetadatosModelo>(File.ReadAllText(almacen.RutaMetadatosAnterior)).ContactosHasta);
            Assert.AreEqual("r2", File.ReadAllText(almacen.RutaReadme));
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length);
        }

        [TestMethod]
        public void Promover_SinTemporal_NoTocaNada()
        {
            string t1 = almacen.NuevaRutaTemporal();
            File.WriteAllText(t1, "octubre");
            almacen.Promover(t1, new MetadatosModelo(), "r1");

            _ = Assert.ThrowsException<FileNotFoundException>(() => almacen.Promover(Path.Combine(carpeta, "no-existe.tmp"), new MetadatosModelo(), "r2"));

            Assert.AreEqual("octubre", File.ReadAllText(almacen.RutaModelo));
            Assert.AreEqual("r1", File.ReadAllText(almacen.RutaReadme));
        }

        [TestMethod]
        public void CorteModeloActivo_DeLosMetadatosOdeLaFechaDelFicheroMenosOchoDias()
        {
            File.SetLastWriteTime(rutaDeploy, new DateTime(2026, 10, 7, 18, 0, 0));
            Assert.AreEqual(new DateTime(2026, 9, 29), almacen.CorteModeloActivo(), "el del deploy no tiene metadatos: fecha − 8 días");

            string t = almacen.NuevaRutaTemporal();
            File.WriteAllText(t, "nuevo");
            almacen.Promover(t, new MetadatosModelo { ContactosHasta = new DateTime(2026, 10, 30) }, "r");
            Assert.AreEqual(new DateTime(2026, 10, 30), almacen.CorteModeloActivo());

            Assert.IsNull(new AlmacenModeloLlamadas(carpeta + "_vacia", Path.Combine(carpeta, "no-existe.zip")).CorteModeloActivo());
        }

        [TestMethod]
        public void CarpetaConfigurada_PorDefectoFueraDeLaWeb()
        {
            Assert.AreEqual(@"C:\NestoAPI\ModelosIA", AlmacenModeloLlamadas.CarpetaConfigurada(_ => null));
            Assert.AreEqual(@"D:\Modelos", AlmacenModeloLlamadas.CarpetaConfigurada(_ => @" D:\Modelos "));
        }

        // El job

        [TestMethod]
        public void Ejecutar_PasaLaPuerta_PromueveYAvisa()
        {
            Decide(true);

            EstadoReentrenamiento estado = Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.Promovido, estado);
            Assert.AreEqual("nuevo", File.ReadAllText(almacen.RutaModelo));
            Assert.AreEqual("deploy", File.ReadAllText(almacen.RutaAnterior));
            Assert.AreEqual("job mensual", almacen.LeerMetadatos().Origen);
            Assert.IsTrue(File.Exists(almacen.RutaUltimoInforme));
            ComprobarAviso("Modelo de llamadas: promovido (P@20 60,0 % → 62,0 %, AUC 0,740 → 0,750)");
            Assert.AreEqual(0, errores.Count);
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length);
        }

        [TestMethod]
        public void Ejecutar_NoPasaLaPuerta_NoTocaNadaYAvisaDelMotivo()
        {
            Decide(false, "la P@20 baja 3,0 puntos (máximo permitido: 1,0); P@20 60,0 % → 57,0 %, AUC 0,740 → 0,750");

            EstadoReentrenamiento estado = Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.NoPromovido, estado);
            Assert.IsFalse(File.Exists(almacen.RutaModelo));
            Assert.AreEqual(rutaDeploy, almacen.RutaModeloActivo);
            A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual("README no pasa", File.ReadAllText(almacen.RutaUltimoInforme));
            ComprobarAviso("Modelo de llamadas: no promovido (la P@20 baja 3,0 puntos");
            ComprobarAviso("El de producción no se toca");
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void Ejecutar_SimulacionQuePasaria_NoPromueveYLoDice()
        {
            Decide(true);

            EstadoReentrenamiento estado = Servicio().Ejecutar(simular: true, "a mano (NUEVAVISION\\Carlos)");

            Assert.AreEqual(EstadoReentrenamiento.Pasaria, estado);
            Assert.IsFalse(File.Exists(almacen.RutaModelo));
            A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).MustNotHaveHappened();
            ComprobarAviso("Modelo de llamadas: simulación, pasaría la puerta");
        }

        [TestMethod]
        public void Ejecutar_SimulacionQueNoPasa_LoDice()
        {
            Decide(false, "el AUC baja; P@20 60,0 % → 60,0 %, AUC 0,740 → 0,700");

            Assert.AreEqual(EstadoReentrenamiento.NoPromovido, Servicio().Ejecutar(simular: true, "a mano"));

            ComprobarAviso("Modelo de llamadas: simulación, no promovido (el AUC baja");
        }

        [TestMethod]
        public void Ejecutar_ErrorAlEntrenar_ElmahYAvisoDeError_SinTocarNada()
        {
            _ = A.CallTo(() => entrenador.Evaluar(A<OpcionesReentrenamiento>._, A<DateTime>._)).Throws(new TimeoutException("tiempo de espera agotado"));

            EstadoReentrenamiento estado = Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.Error, estado);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "tiempo de espera agotado");
            ComprobarAviso("Modelo de llamadas: error (tiempo de espera agotado)");
            Assert.IsFalse(File.Exists(almacen.RutaModelo));
        }

        [TestMethod]
        public void Ejecutar_FallaElModeloFinal_NoPromueveNiDejaTemporales()
        {
            Decide(true);
            _ = A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._)).Invokes((string ruta) =>
            {
                File.WriteAllText(ruta, "a medias");
                throw new InvalidOperationException("no puntúa bien en la ida y vuelta");
            });

            EstadoReentrenamiento estado = Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.Error, estado);
            Assert.IsFalse(File.Exists(almacen.RutaModelo));
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length);
            ComprobarAviso("error (no puntúa bien en la ida y vuelta)");
        }

        [TestMethod]
        public void Ejecutar_SinLibreriaNativa_ErrorSinEntrenar()
        {
            EstadoReentrenamiento estado = Servicio(() => throw new FileNotFoundException("no está lib_lightgbm.dll")).Ejecutar(false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.Error, estado);
            A.CallTo(() => entrenador.Evaluar(A<OpcionesReentrenamiento>._, A<DateTime>._)).MustNotHaveHappened();
            ComprobarAviso("lib_lightgbm.dll");
        }

        [TestMethod]
        public void Ejecutar_FallaElAviso_VaAElmahYElResultadoNoCambia()
        {
            Decide(true);
            _ = A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Throws(new Exception("SignalR caído"));

            Assert.AreEqual(EstadoReentrenamiento.Promovido, Servicio().Ejecutar(simular: false, "job mensual"));
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "promovido");
        }

        [TestMethod]
        public void Opciones_TresAnnosContraElActivoYUmbralesDelWebConfigOPorDefecto()
        {
            OpcionesReentrenamiento porDefecto = Servicio().Opciones();
            Assert.IsTrue(porDefecto.Completo);
            Assert.IsFalse(porDefecto.EvaluarAlternativas);
            Assert.AreEqual(rutaDeploy, porDefecto.ModeloActual);
            Assert.AreEqual(PuertaCalidad.MAX_BAJADA_PRECISION_POR_DEFECTO, porDefecto.MaxBajadaPrecision, 1e-9);
            Assert.AreEqual(PuertaCalidad.MAX_BAJADA_AUC_POR_DEFECTO, porDefecto.MaxBajadaAuc, 1e-9);

            parametros[ReentrenamientoModeloLlamadasJobsService.CLAVE_MAX_BAJADA_PRECISION] = "2,5";
            parametros[ReentrenamientoModeloLlamadasJobsService.CLAVE_MAX_BAJADA_AUC] = "0.005";
            OpcionesReentrenamiento configuradas = Servicio().Opciones();
            Assert.AreEqual(0.025, configuradas.MaxBajadaPrecision, 1e-9);
            Assert.AreEqual(0.005, configuradas.MaxBajadaAuc, 1e-9);

            parametros[ReentrenamientoModeloLlamadasJobsService.CLAVE_MAX_BAJADA_PRECISION] = "mucho";
            Assert.AreEqual(PuertaCalidad.MAX_BAJADA_PRECISION_POR_DEFECTO, Servicio().Opciones().MaxBajadaPrecision, 1e-9);
        }

        [TestMethod]
        public void Destinatarios_CarlosPorDefectoOLosDelWebConfig()
        {
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Carlos" }, Servicio().Destinatarios());
            parametros[ReentrenamientoModeloLlamadasJobsService.CLAVE_USUARIOS_AVISO] = "Carlos; NUEVAVISION\\Laura,carlos";
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Carlos", "NUEVAVISION\\Laura" }, Servicio().Destinatarios());
        }

        // Entrenar de verdad en .NET Framework

        [TestMethod]
        public void EntrenarEnNet48_ConLightGbmNativo_GuardaUnZipQueLaApiPuntua()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo funciona en 64 bits y el proceso de los tests es de 32");
            }
            string cargada = CargadorLightGbmNativo.Asegurar();
            StringAssert.EndsWith(cargada, "lib_lightgbm.dll");

            DateTime hoy = new DateTime(2026, 10, 9);
            DatosCrudos datos = DatosSinteticos(hoy);
            var opciones = new OpcionesReentrenamiento { Completo = false, Vendedor = "MPP", Meses = 8, EvaluarAlternativas = false };

            ProcesoReentrenamiento proceso = ProcesoReentrenamiento.Evaluar(datos, opciones, hoy);

            Assert.IsTrue(proceso.DatosSuficientes);
            Assert.IsFalse(proceso.Decision.Promover, "sin modelo actual no se promueve nunca");
            Assert.AreEqual("no hay modelo actual con el que comparar", proceso.Decision.Motivo);
            Assert.IsFalse(double.IsNaN(proceso.Nuevo.Auc));

            string ruta = Path.Combine(Path.GetTempPath(), "NestoAPI619_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                List<float> prueba = proceso.EntrenarFinalYGuardar(ruta);
                Assert.IsTrue(prueba.All(p => p >= 0 && p <= 1));

                List<float> probabilidades = ModeloContacto.Puntuar(new List<ModeloContactoEntrada>
                {
                    new ModeloContactoEntrada { ClienteId = "C0/0", TipoInteraccion = "Llamada", Mes = "10", DiaSemana = "5", GrupoSubgrupoMasVendido = "NADA",
                        Pedidos12Meses = 30, Importe12Meses = 3000, DiasDesdeUltimoPedido = 10, DiasEntrePedidos = 12, ImporteMedioPedido = 100,
                        TasaConversionCliente = 0.8f, ContactosPrevios = 20 },
                    new ModeloContactoEntrada { ClienteId = "C1/0", TipoInteraccion = "Llamada", Mes = "10", DiaSemana = "5", GrupoSubgrupoMasVendido = "NADA",
                        DiasDesdeUltimoPedido = 1000, DiasEntrePedidos = 1000, TasaConversionCliente = 0.05f, ContactosPrevios = 20, SinHistorial = 1 }
                }, ruta);
                Assert.AreEqual(2, probabilidades.Count);
                Assert.IsTrue(probabilidades[0] > probabilidades[1], $"el que compra cada 12 días ({probabilidades[0]}) antes que el que nunca compra ({probabilidades[1]})");
            }
            finally
            {
                try { File.Delete(ruta); } catch (IOException) { }
            }
        }

        /// <summary>80 clientes de MPP llamados cada 10 días durante 2 años; uno de cada tres compra cada 12 días, el resto nunca.</summary>
        private static DatosCrudos DatosSinteticos(DateTime hoy)
        {
            var datos = new DatosCrudos();
            DateTime inicio = hoy.AddMonths(-24);
            for (int i = 0; i < 80; i++)
            {
                string id = $"C{i}/0";
                bool comprador = i % 3 == 0;
                for (DateTime d = inicio.AddDays(i % 10); d < hoy; d = d.AddDays(10))
                {
                    datos.Rapports.Add(new RapportCrudo(id, d.AddHours(10 + i % 8), "T", false, "MPP"));
                }
                if (comprador)
                {
                    for (DateTime d = inicio.AddDays(i % 12); d < hoy; d = d.AddDays(12))
                    {
                        datos.Pedidos.Add(new PedidoDia(id, d, 100 + i));
                    }
                }
            }
            return datos;
        }
    }
}
