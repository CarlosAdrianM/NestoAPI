using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega;
using NestoAPI.Infraestructure.PedidosCompra.PlazosEntrega.ModeloPlazos;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#853: modelo ML.NET de plazos en sombra (rasgos sin fuga, regla «como si», métricas, puerta, envío, job, endpoints).</summary>
    [TestClass]
    public class ModeloPlazosProveedoresTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 12);

        private static PedidoCompraHistorico P(int pedido, string proveedor, DateTime envio, params int[] diasAlbaranes) => new PedidoCompraHistorico
        {
            Empresa = "1",
            Pedido = pedido,
            Proveedor = proveedor,
            FechaEnvio = envio,
            Lineas = 3,
            Importe = 100,
            Albaranes = diasAlbaranes.Select(d => envio.AddDays(d)).ToList()
        };

        #region Rasgos

        [TestMethod]
        public void Rasgos_SinFuga_LoPosteriorAlEnvioNoInfluye()
        {
            DateTime t = HOY.AddDays(-30);
            var historia = new List<PedidoCompraHistorico>
            {
                P(1, "65", t.AddDays(-40), 4),
                P(2, "65", t.AddDays(-20), 6),
                P(3, "65", t.AddDays(-10), 2),
                P(4, "65", t.AddDays(-5), 9)   // enviado antes, pero llega DESPUÉS de t (día t+4)
            };
            RasgosPlazo sinFuturo = ConstructorRasgosPlazos.Rasgos(historia, "65", t, 0, 3, 100);

            var conFuturo = historia.Concat(new[]
            {
                P(5, "65", t.AddDays(1), 50),  // pedido futuro, muy lento: no puede influir
                P(6, "65", t, 1)               // el mismo día: tampoco (no se sabe en qué orden)
            }).ToList();
            RasgosPlazo r = ConstructorRasgosPlazos.Rasgos(conFuturo, "65", t, 0, 3, 100);

            foreach (var propiedad in typeof(RasgosPlazo).GetProperties())
            {
                Assert.AreEqual(propiedad.GetValue(sinFuturo), propiedad.GetValue(r), propiedad.Name);
            }
            Assert.AreEqual(3, r.HistCuantos, "el pedido 4 aún no había llegado el día t: su plazo no se conoce");
            Assert.AreEqual(2, r.HistUltimo, "el último recibido antes de t es el 3");
            Assert.AreEqual(1, r.EnElAire, "el 4 estaba en el aire");
            Assert.AreEqual(5, r.DiasDesdeAnterior);
        }

        [TestMethod]
        public void Construir_ElPedidoFuturoNoCambiaLosRasgosDeLosAnteriores()
        {
            var historia = Enumerable.Range(0, 10).Select(i => P(i, "65", HOY.AddDays(-200 + i * 10), 3 + i % 3)).ToList();
            List<EjemploPlazo> antes = ConstructorRasgosPlazos.Construir(historia);
            List<EjemploPlazo> despues = ConstructorRasgosPlazos.Construir(historia.Concat(new[] { P(99, "65", HOY.AddDays(-20), 80) }));

            Assert.AreEqual(antes.Count + 1, despues.Count);
            for (int i = 0; i < antes.Count; i++)
            {
                Assert.AreEqual(antes[i].Rasgos.HistP80, despues[i].Rasgos.HistP80);
                Assert.AreEqual(antes[i].Rasgos.HistMediana, despues[i].Rasgos.HistMediana);
                Assert.AreEqual(antes[i].Rasgos.EnElAire, despues[i].Rasgos.EnElAire);
                Assert.AreEqual(antes[i].Rasgos.DiasDesdeAnterior, despues[i].Rasgos.DiasDesdeAnterior);
            }
        }

        [TestMethod]
        public void Construir_SoloRecibidosConPlazoDe0a120()
        {
            var historia = new List<PedidoCompraHistorico>
            {
                P(1, "65", HOY.AddDays(-50), 3),
                P(2, "65", HOY.AddDays(-40), -2),   // modificado después del albarán
                P(3, "65", HOY.AddDays(-300), 150), // más de 120 días
                P(4, "65", HOY.AddDays(-10)),       // sin llegar
                P(5, "65", HOY.AddDays(-30), 5, 2)  // dos albaranes: cuenta el primero
            };
            List<EjemploPlazo> ejemplos = ConstructorRasgosPlazos.Construir(historia);

            CollectionAssert.AreEquivalent(new[] { 1, 5 }, ejemplos.Select(e => e.Pedido.Pedido).ToArray());
            Assert.AreEqual(2, ejemplos.Single(e => e.Pedido.Pedido == 5).Dias);
        }

        [TestMethod]
        public void Rasgos_MedianaYP80DeLosUltimos10()
        {
            int[] plazos = { 50, 50, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; // los dos primeros (más antiguos) quedan fuera
            var historia = plazos.Select((d, i) => P(i, "65", HOY.AddDays(-400 + i * 20), d)).ToList();
            RasgosPlazo r = ConstructorRasgosPlazos.Rasgos(historia, "65", HOY, 2, 4, Math.E - 1);

            Assert.AreEqual(10, r.HistCuantos);
            Assert.AreEqual(5, r.HistMediana);
            Assert.AreEqual(8, r.HistP80);
            Assert.AreEqual(10, r.HistUltimo);
            Assert.AreEqual(1, r.LogImporte, 1e-5);
            Assert.AreEqual((int)HOY.DayOfWeek, r.DiaSemana);
            Assert.AreEqual(11, r.Mes);
            Assert.AreEqual(2, r.DiasEnServir);
        }

        [TestMethod]
        public void Rasgos_SinHistoria_Centinelas()
        {
            RasgosPlazo r = ConstructorRasgosPlazos.Rasgos(null, "65", HOY, 0, 1, 10);
            Assert.AreEqual(0, r.HistCuantos);
            Assert.AreEqual(-1, r.HistMediana);
            Assert.AreEqual(-1, r.HistUltimo);
            Assert.AreEqual(-1, r.DiasDesdeAnterior);
        }

        [TestMethod]
        public void Mapa_ProveedoresConPocosPedidosVanAOtros()
        {
            var ejemplos = ConstructorRasgosPlazos.Construir(
                Enumerable.Range(0, 12).Select(i => P(i, "65", HOY.AddDays(-100 + i), 2))
                .Concat(Enumerable.Range(0, 3).Select(i => P(100 + i, "7", HOY.AddDays(-100 + i), 2))));
            MapaProveedoresPlazos mapa = MapaProveedoresPlazos.Desde(ejemplos);

            Assert.AreEqual("65", mapa.Mapear("65  "));
            Assert.AreEqual(MapaProveedoresPlazos.OTROS, mapa.Mapear("7"));
            Assert.AreEqual(MapaProveedoresPlazos.OTROS, mapa.Mapear("nuevo"));
            Assert.AreEqual((float)Math.Log(3), mapa.Fila(ejemplos[0]).Label, 1e-6);
        }

        #endregion

        #region Regla «como si»

        [TestMethod]
        public void ReglaComoSi_ConCincoRecepcionesPropias_SuP80_YSoloLoAnteriorAlEnvio()
        {
            var historia = new List<PedidoCompraHistorico>();
            int[] plazos = { 1, 2, 3, 4, 10 };
            for (int i = 0; i < plazos.Length; i++)
            {
                historia.Add(P(i, "65", HOY.AddDays(-100 + i * 10), plazos[i]));
            }
            historia.Add(P(50, "65", HOY.AddDays(-3), 60)); // llega después del envío: no cuenta
            historia.Add(P(60, "9", HOY.AddDays(-50), 30));

            var regla = new ReglaPlazosComoSi(historia);

            Assert.AreEqual(4, regla.Dias("65", HOY, 0), "P80 de 1,2,3,4,10 = 4");
        }

        [TestMethod]
        public void ReglaComoSi_PocasPropias_ElGenerico_YSinNadaNull()
        {
            var historia = new List<PedidoCompraHistorico> { P(1, "65", HOY.AddDays(-60), 2), P(2, "9", HOY.AddDays(-50), 30) };
            var regla = new ReglaPlazosComoSi(historia);

            Assert.AreEqual(30, regla.Dias("65", HOY, 0), "genérico: P80 de 2 y 30");
            Assert.IsNull(regla.Dias("65", HOY.AddDays(-100), 0));
        }

        #endregion

        #region Métricas, puerta, veredicto

        [TestMethod]
        public void Metricas_ErrorCortoYDiasDeMas()
        {
            MetricasPlazos m = MetricasPlazos.Calcular(new[] { (Real: 5, Previsto: 3), (Real: 2, Previsto: 6), (Real: 4, Previsto: 4), (Real: 1, Previsto: 3) });

            Assert.AreEqual(4, m.Pedidos);
            Assert.AreEqual(2.0, m.ErrorMedio);
            Assert.AreEqual(25.0, m.PorcentajeCorto);
            Assert.AreEqual(3.0, m.DiasDeMasMedio.Value);
        }

        [TestMethod]
        public void Puerta_SinModeloGuardado_PasaElPrimero()
        {
            var nuevo = new MetricasPlazos { Pedidos = 40, ErrorMedio = 3, PorcentajeCorto = 20 };
            Assert.IsTrue(PuertaCalidadPlazos.Decidir(null, false, nuevo).Promover);
            Assert.IsTrue(PuertaCalidadPlazos.Decidir(new MetricasPlazos { Pedidos = 40, ErrorMedio = 1, PorcentajeCorto = 1 }, false, nuevo).Promover,
                "la fila dice que había, pero el fichero no está");
        }

        [TestMethod]
        public void Puerta_SiEmpeoraNoPasa_YConLaHolguraSi()
        {
            var activo = new MetricasPlazos { Pedidos = 40, ErrorMedio = 3, PorcentajeCorto = 20 };

            Assert.IsFalse(PuertaCalidadPlazos.Decidir(activo, true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 3, PorcentajeCorto = 22 }).Promover);
            Assert.IsFalse(PuertaCalidadPlazos.Decidir(activo, true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 3.5, PorcentajeCorto = 18 }).Promover);
            Assert.IsTrue(PuertaCalidadPlazos.Decidir(activo, true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 3.2, PorcentajeCorto = 21 }).Promover);
            Assert.IsTrue(PuertaCalidadPlazos.Decidir(activo, true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 2, PorcentajeCorto = 15 }).Promover);
        }

        [TestMethod]
        public void Puerta_PocosPedidosEnValidacion_NoPasa()
        {
            Assert.IsFalse(PuertaCalidadPlazos.Decidir(null, false, new MetricasPlazos { Pedidos = 10, ErrorMedio = 1, PorcentajeCorto = 0 }).Promover);
        }

        [TestMethod]
        public void Comparacion_MenosDe50_AunNoHayDatos()
        {
            List<EnvioRecibidoPlazos> envios = Enumerable.Range(0, 49).Select(i => Envio(HOY.AddDays(-60), 5, 4, 6)).ToList();
            ComparacionRealPlazos c = ComparacionPlazos.Comparar(envios);

            Assert.AreEqual(49, c.Pedidos);
            StringAssert.StartsWith(c.Veredicto, "Aún no hay datos suficientes");
        }

        [TestMethod]
        public void Comparacion_ElModeloSeQuedaCortoMenos_LaReglaConMenosError()
        {
            // real 5; regla 4 (corto, error 1); modelo 8 (nunca corto, error 3)
            List<EnvioRecibidoPlazos> envios = Enumerable.Range(0, 60).Select(i => Envio(HOY.AddDays(-60), 5, 4, 8)).ToList();
            ComparacionRealPlazos c = ComparacionPlazos.Comparar(envios);

            Assert.AreEqual(100.0, c.Regla.PorcentajeCorto);
            Assert.AreEqual(0.0, c.Modelo.PorcentajeCorto);
            Assert.AreEqual(1.0, c.Regla.ErrorMedio);
            Assert.AreEqual(3.0, c.Modelo.ErrorMedio);
            StringAssert.Contains(c.Veredicto, "Se queda corto menos veces el modelo");
            StringAssert.Contains(c.Veredicto, "Menos error medio la regla del P80");
        }

        private static EnvioRecibidoPlazos Envio(DateTime envio, int real, int regla, int modelo) => new EnvioRecibidoPlazos
        {
            Empresa = "1",
            FechaEnvio = envio.AddHours(10),
            PrimerAlbaran = envio.AddDays(real),
            FechaRegla = envio.AddDays(regla),
            FechaModelo = envio.AddDays(modelo)
        };

        [TestMethod]
        public void DiasPrudentes_RedondeoHaciaArribaYMargen()
        {
            Assert.AreEqual(2, EntrenadorModeloPlazos.DiasPrudentes(Math.Log(3), 0), "exp(log 3) − 1 = 2: el ruido de coma flotante no sube un día");
            Assert.AreEqual(4, EntrenadorModeloPlazos.DiasPrudentes(Math.Log(1 + 2.2), Math.Log(1.5)), "3,2 × 1,5 − 1 = 3,8 → 4 (hacia arriba)");
            Assert.AreEqual(0, EntrenadorModeloPlazos.DiasPrudentes(-5, 0));
            Assert.AreEqual(EntrenadorModeloPlazos.MAX_DIAS_PREVISTOS, EntrenadorModeloPlazos.DiasPrudentes(50, 0));
        }

        [TestMethod]
        public void Margen_P80DeLosResiduos()
        {
            Assert.AreEqual(0.4, EntrenadorModeloPlazos.Margen(new[] { 0.5, 0.1, 0.4, -0.2, 0.0 }), 1e-9);
            Assert.AreEqual(0, EntrenadorModeloPlazos.Margen(new double[0]));
        }

        #endregion

        #region Al enviar el pedido

        private static PlazosProveedores ConPlazos() => new PlazosProveedores(new[]
        {
            new PlazoProveedor { Empresa = "1", Proveedor = CalculoPlazosProveedores.PROVEEDOR_GENERICO, Recepciones = 2748, DiasMediana = 4, DiasP80 = 11 },
            new PlazoProveedor { Empresa = "1", Proveedor = "65", Recepciones = 252, DiasMediana = 2, DiasP80 = 4 }
        });

        private static ServicioFechaRecepcionAlEnviar Servicio(bool interruptor, Func<ConsultaPlazoModelo, int?> modelo, bool mandaModelo,
            List<Exception> errores = null, IRepositorioPlazosProveedores repositorio = null)
        {
            ServicioFechaRecepcionAlEnviar.OlvidarComprobaciones();
            return new ServicioFechaRecepcionAlEnviar(() => repositorio ?? A.Fake<IRepositorioPlazosProveedores>(), () => interruptor,
                () => HOY.AddHours(10), ex => errores?.Add(ex), _ => ConPlazos(), modelo, () => mandaModelo);
        }

        [TestMethod]
        public void AlEnviar_ConModeloYManDaLaRegla_FechaDeLaReglaYModeloApuntado()
        {
            ConsultaPlazoModelo recibida = null;
            FechaRecepcionEstimada f = Servicio(true, c => { recibida = c; return 9; }, false).Calcular("1", "65", 0, 7, 250.5, 1234);

            Assert.AreEqual(HOY.AddDays(4), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_PLAZO_PROVEEDOR, f.Origen);
            Assert.AreEqual(HOY.AddDays(9), f.FechaModelo);
            Assert.AreEqual(7, recibida.Lineas);
            Assert.AreEqual(250.5, recibida.Importe);
            Assert.AreEqual(1234, recibida.Pedido);
            Assert.AreEqual(HOY, recibida.FechaEnvio);
        }

        [TestMethod]
        public void AlEnviar_MandaElModelo_FechaDelModelo()
        {
            FechaRecepcionEstimada f = Servicio(true, _ => 9, true).Calcular("1", "65", 2);

            Assert.AreEqual(HOY.AddDays(9), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_MODELO, f.Origen);
            Assert.AreEqual(HOY.AddDays(4), f.FechaRegla);
        }

        [TestMethod]
        public void AlEnviar_MandaElModelo_NuncaAntesDeDiasEnServir()
        {
            FechaRecepcionEstimada f = Servicio(true, _ => 1, true).Calcular("1", "65", 6);

            Assert.AreEqual(HOY.AddDays(6), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_MODELO_DIAS_EN_SERVIR, f.Origen);
            Assert.AreEqual(HOY.AddDays(1), f.FechaModelo);
        }

        [TestMethod]
        public void AlEnviar_MandaElModeloPeroSinPrediccion_LaRegla()
        {
            FechaRecepcionEstimada f = Servicio(true, _ => null, true).Calcular("1", "65", 0);

            Assert.AreEqual(HOY.AddDays(4), f.Fecha);
            Assert.AreEqual(FechaRecepcionEstimada.ORIGEN_PLAZO_PROVEEDOR, f.Origen);
            Assert.IsNull(f.FechaModelo);
        }

        [TestMethod]
        public void AlEnviar_ElModeloFalla_LaReglaSinFechaModeloYUnAviso()
        {
            var errores = new List<Exception>();
            ServicioFechaRecepcionAlEnviar servicio = Servicio(true, _ => throw new InvalidOperationException("zip roto"), true, errores);

            FechaRecepcionEstimada f = servicio.Calcular("1", "65", 0);
            _ = servicio.Calcular("1", "65", 0);

            Assert.AreEqual(HOY.AddDays(4), f.Fecha);
            Assert.IsNull(f.FechaModelo);
            Assert.AreEqual(1, errores.Count, "se avisa una vez");
        }

        [TestMethod]
        public void AlEnviar_InterruptorApagado_ComoSiempreAunqueMandeElModelo()
        {
            FechaRecepcionEstimada f = Servicio(false, _ => 9, true).Calcular("1", "65", 2);

            Assert.AreEqual(HOY.AddDays(2), f.Fecha);
            Assert.AreEqual(ServicioFechaRecepcionAlEnviar.ORIGEN_COMO_ANTES, f.Origen);
            Assert.AreEqual(HOY.AddDays(9), f.FechaModelo, "en sombra se apunta igual");
            Assert.AreEqual(HOY.AddDays(4), f.FechaRegla);
        }

        [TestMethod]
        public void AlEnviar_ElModeloTardaDemasiado_Null()
        {
            int? dias = ServicioFechaRecepcionAlEnviar.ConLimite(() => { Thread.Sleep(2000); return 5; }, TimeSpan.FromMilliseconds(50));
            Assert.IsNull(dias);
            Assert.AreEqual(5, ServicioFechaRecepcionAlEnviar.ConLimite(() => 5, TimeSpan.FromSeconds(5)));
        }

        [TestMethod]
        public void QuienManda_SoloModeloEsModelo()
        {
            Assert.IsTrue(ServicioFechaRecepcionAlEnviar.InterpretarQuienManda(" modelo "));
            Assert.IsFalse(ServicioFechaRecepcionAlEnviar.InterpretarQuienManda("Regla"));
            Assert.IsFalse(ServicioFechaRecepcionAlEnviar.InterpretarQuienManda(null));
        }

        [TestMethod]
        public void Registrar_ApuntaLaFechaDelModelo()
        {
            var repositorio = A.Fake<IRepositorioPlazosProveedores>();
            A.CallTo(() => repositorio.TablaEnviosExiste()).Returns(true);
            ServicioFechaRecepcionAlEnviar servicio = Servicio(true, _ => 9, false, repositorio: repositorio);
            FechaRecepcionEstimada f = servicio.Calcular("1", "65", 0);

            servicio.Registrar("1", 1234, "65", 0, f, "Carlos");

            A.CallTo(() => repositorio.RegistrarEnvio(A<EnvioPedidoCompra>.That.Matches(e => e.FechaModelo == HOY.AddDays(9) && e.FechaRegla == HOY.AddDays(4))))
                .MustHaveHappenedOnceExactly();
        }

        #endregion

        #region Job

        private string carpeta;

        [TestInitialize]
        public void Preparar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "ModeloPlazosTests_" + Guid.NewGuid().ToString("N"));
            ModeloPlazosProveedoresJobsService.OlvidarAvisos();
            PredictorPlazosProveedores.Olvidar();
        }

        [TestCleanup]
        public void Limpiar()
        {
            PredictorPlazosProveedores.Olvidar();
            try
            {
                if (Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, true);
                }
            }
            catch (IOException)
            {
            }
        }

        private ModeloPlazosProveedoresJobsService Job(IRepositorioModeloPlazos repositorio, IEntrenadorModeloPlazos entrenador, List<Exception> errores)
            => new ModeloPlazosProveedoresJobsService(repositorio, entrenador, new AlmacenModeloPlazos(carpeta), () => HOY.AddHours(4), errores.Add, () => { });

        [TestMethod]
        public void Job_SinTabla_NoHaceNadaYAvisaUnaVez()
        {
            var repositorio = A.Fake<IRepositorioModeloPlazos>();
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(false);
            var entrenador = A.Fake<IEntrenadorModeloPlazos>();
            var errores = new List<Exception>();

            ResultadoEntrenamientoModeloPlazos r = Job(repositorio, entrenador, errores).Ejecutar("prueba");
            _ = Job(repositorio, entrenador, errores).Ejecutar("prueba");

            Assert.IsFalse(r.Hecho);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => repositorio.LeerHistorico(A<string>._, A<DateTime>._)).MustNotHaveHappened();
            A.CallTo(() => entrenador.Evaluar(A<IReadOnlyList<PedidoCompraHistorico>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        private static IEvaluacionModeloPlazos Evaluacion(bool suficientes, MetricasPlazos modelo, Action<string> guardar = null)
        {
            var evaluacion = A.Fake<IEvaluacionModeloPlazos>();
            A.CallTo(() => evaluacion.DatosSuficientes).Returns(suficientes);
            A.CallTo(() => evaluacion.Motivo).Returns(suficientes ? "ok" : "datos insuficientes");
            A.CallTo(() => evaluacion.Modelo).Returns(modelo);
            A.CallTo(() => evaluacion.Regla).Returns(new MetricasPlazos { Pedidos = 40, ErrorMedio = 4, PorcentajeCorto = 25 });
            A.CallTo(() => evaluacion.EntrenarFinalYGuardar(A<string>._, A<DateTime>._, A<string>._)).ReturnsLazily((string ruta, DateTime f, string o) =>
            {
                File.WriteAllText(ruta, "zip");
                guardar?.Invoke(ruta);
                return new MetadatosModeloPlazos { Fecha = f, MargenLog = 0.3 };
            });
            return evaluacion;
        }

        private static IRepositorioModeloPlazos RepositorioConTabla(EntrenamientoModeloPlazos activo, List<EntrenamientoModeloPlazos> registradas)
        {
            var repositorio = A.Fake<IRepositorioModeloPlazos>();
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerHistorico("1", HOY.AddYears(-5))).Returns(new List<PedidoCompraHistorico>());
            A.CallTo(() => repositorio.UltimoActivo()).Returns(activo);
            A.CallTo(() => repositorio.RegistrarEntrenamiento(A<EntrenamientoModeloPlazos>._)).Invokes((EntrenamientoModeloPlazos f) => registradas.Add(f));
            return repositorio;
        }

        [TestMethod]
        public void Job_DatosInsuficientes_ApuntaLaFilaSinModelo()
        {
            var registradas = new List<EntrenamientoModeloPlazos>();
            var entrenador = A.Fake<IEntrenadorModeloPlazos>();
            A.CallTo(() => entrenador.Evaluar(A<IReadOnlyList<PedidoCompraHistorico>>._, HOY)).Returns(Evaluacion(false, null));

            ResultadoEntrenamientoModeloPlazos r = Job(RepositorioConTabla(null, registradas), entrenador, new List<Exception>()).Ejecutar("prueba");

            Assert.IsTrue(r.Hecho);
            Assert.IsFalse(r.Promovido);
            Assert.AreEqual(1, registradas.Count);
            Assert.IsFalse(registradas[0].ModeloActivo);
            Assert.IsFalse(new AlmacenModeloPlazos(carpeta).HayModelo);
        }

        [TestMethod]
        public void Job_PrimerModelo_SePromueveYSeApuntaActivo()
        {
            var registradas = new List<EntrenamientoModeloPlazos>();
            var entrenador = A.Fake<IEntrenadorModeloPlazos>();
            A.CallTo(() => entrenador.Evaluar(A<IReadOnlyList<PedidoCompraHistorico>>._, HOY))
                .Returns(Evaluacion(true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 3, PorcentajeCorto = 18, DiasDeMasMedio = 4 }));

            ResultadoEntrenamientoModeloPlazos r = Job(RepositorioConTabla(null, registradas), entrenador, new List<Exception>()).Ejecutar("prueba");

            Assert.IsTrue(r.Promovido);
            Assert.IsTrue(new AlmacenModeloPlazos(carpeta).HayModelo);
            Assert.AreEqual(0.3, new AlmacenModeloPlazos(carpeta).LeerDatos().MargenLog);
            EntrenamientoModeloPlazos fila = registradas.Single();
            Assert.IsTrue(fila.ModeloActivo);
            Assert.AreEqual(18.0, fila.PorcentajeCortoModelo.Value);
            Assert.AreEqual(25.0, fila.PorcentajeCortoRegla.Value);
            Assert.AreEqual(4.0, fila.ErrorMedioRegla.Value);
            Assert.AreEqual(0, Directory.GetFiles(carpeta, "*.tmp").Length, "sin temporales");
        }

        [TestMethod]
        public void Job_SiEmpeora_NoSustituyeAlGuardado()
        {
            var almacen = new AlmacenModeloPlazos(carpeta);
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(almacen.RutaModelo, "el bueno");
            File.WriteAllText(almacen.RutaDatos, "{}");
            var registradas = new List<EntrenamientoModeloPlazos>();
            var activo = new EntrenamientoModeloPlazos { PedidosComparados = 40, ErrorMedioModelo = 3, PorcentajeCortoModelo = 15, ModeloActivo = true };
            var entrenador = A.Fake<IEntrenadorModeloPlazos>();
            A.CallTo(() => entrenador.Evaluar(A<IReadOnlyList<PedidoCompraHistorico>>._, HOY))
                .Returns(Evaluacion(true, new MetricasPlazos { Pedidos = 40, ErrorMedio = 3, PorcentajeCorto = 30 }));

            ResultadoEntrenamientoModeloPlazos r = Job(RepositorioConTabla(activo, registradas), entrenador, new List<Exception>()).Ejecutar("prueba");

            Assert.IsFalse(r.Promovido);
            Assert.AreEqual("el bueno", File.ReadAllText(almacen.RutaModelo));
            Assert.IsFalse(registradas.Single().ModeloActivo);
            StringAssert.Contains(registradas.Single().Motivo, "empeora");
        }

        [TestMethod]
        public void Job_SiFallaNoLanza()
        {
            var repositorio = A.Fake<IRepositorioModeloPlazos>();
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerHistorico(A<string>._, A<DateTime>._)).Throws(new TimeoutException("BD caída"));
            var errores = new List<Exception>();

            ResultadoEntrenamientoModeloPlazos r = Job(repositorio, A.Fake<IEntrenadorModeloPlazos>(), errores).Ejecutar("prueba");

            Assert.IsFalse(r.Hecho);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Job_ProgramadoSoloDeMadrugada()
        {
            Assert.IsTrue(ModeloPlazosProveedoresJobsService.TocaEntrenar(HOY.AddHours(4).AddMinutes(10)));
            Assert.IsFalse(ModeloPlazosProveedoresJobsService.TocaEntrenar(HOY.AddHours(9)));
            Assert.AreEqual("entrenamiento", ModeloPlazosProveedoresJobsService.COLA);
        }

        #endregion

        #region Endpoints

        private sealed class ProgramadorFalso : IProgramadorModeloPlazos
        {
            public DateTime? Programado;
            public int Encolados;
            public string Encolar(string usuario) { Encolados++; return "e1"; }
            public string Programar(DateTime cuandoMadrid, string usuario) { Programado = cuandoMadrid; return "p1"; }
        }

        private static ComprasPlazosProveedoresController Controller(IRepositorioModeloPlazos repositorio, ProgramadorFalso programador, DateTime madrid,
            params string[] roles)
            => new ComprasPlazosProveedoresController(() => new ServicioPlazosProveedores(A.Fake<IRepositorioPlazosProveedores>()), () => "0",
                () => repositorio, programador, () => madrid, () => true, () => false)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        private static IRepositorioModeloPlazos RepositorioEndpoints(bool tabla = true)
        {
            var repositorio = A.Fake<IRepositorioModeloPlazos>();
            A.CallTo(() => repositorio.TablaEntrenamientosExiste()).Returns(tabla);
            A.CallTo(() => repositorio.TablaEnviosExiste()).Returns(true);
            A.CallTo(() => repositorio.UltimoEntrenamiento()).Returns(new EntrenamientoModeloPlazos { Id = 7, ErrorMedioModelo = 2, ErrorMedioRegla = 3 });
            A.CallTo(() => repositorio.LeerEnviosRecibidos(A<DateTime>._))
                .Returns(Enumerable.Range(0, 60).Select(i => Envio(HOY.AddDays(-60), 5, 4, 8)).ToList());
            return repositorio;
        }

        [TestMethod]
        public void Comparacion_SinGrupo_403()
        {
            var r = Controller(RepositorioEndpoints(), new ProgramadorFalso(), HOY.AddHours(10), "Almacén").GetComparacion() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
        }

        [TestMethod]
        public void Comparacion_Compras_UltimoEntrenamientoYLoReal()
        {
            IRepositorioModeloPlazos repositorio = RepositorioEndpoints();
            var r = Controller(repositorio, new ProgramadorFalso(), HOY.AddHours(10), "Compras").GetComparacion(6)
                as OkNegotiatedContentResult<ComparacionModeloPlazosDTO>;

            Assert.IsNotNull(r);
            Assert.AreEqual(7, r.Content.UltimoEntrenamiento.Id);
            Assert.AreEqual(60, r.Content.Real.Pedidos);
            Assert.AreEqual(ServicioFechaRecepcionAlEnviar.QUIEN_MANDA_REGLA, r.Content.QuienManda);
            Assert.IsTrue(r.Content.ModeloGuardado);
            A.CallTo(() => repositorio.LeerEnviosRecibidos(HOY.AddMonths(-6))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Comparacion_SinTabla_503()
        {
            var r = Controller(RepositorioEndpoints(false), new ProgramadorFalso(), HOY.AddHours(10), "Dirección").GetComparacion() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }

        [TestMethod]
        public void Entrenar_Compras_403()
        {
            var programador = new ProgramadorFalso();
            var r = Controller(RepositorioEndpoints(), programador, HOY.AddHours(10), "Compras").PostEntrenarModelo() as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.AreEqual(0, programador.Encolados);
            Assert.IsNull(programador.Programado);
        }

        [TestMethod]
        public void Entrenar_EnHorario_SeProgramaParaLas21()
        {
            var programador = new ProgramadorFalso();
            var r = Controller(RepositorioEndpoints(), programador, HOY.AddHours(10), "Informática").PostEntrenarModelo()
                as NegotiatedContentResult<LanzamientoModeloPlazosDTO>;

            Assert.AreEqual(HttpStatusCode.Accepted, r.StatusCode);
            Assert.IsTrue(r.Content.Programado);
            Assert.AreEqual(HOY.AddHours(21), programador.Programado);
            Assert.AreEqual(0, programador.Encolados);
        }

        [TestMethod]
        public void Entrenar_ConAhoraOFueraDeHorario_SeEncola()
        {
            var programador = new ProgramadorFalso();
            var ahora = Controller(RepositorioEndpoints(), programador, HOY.AddHours(10), "Dirección").PostEntrenarModelo(ahora: true)
                as NegotiatedContentResult<LanzamientoModeloPlazosDTO>;
            var deNoche = Controller(RepositorioEndpoints(), programador, HOY.AddHours(22), "Dirección").PostEntrenarModelo()
                as NegotiatedContentResult<LanzamientoModeloPlazosDTO>;

            Assert.IsFalse(ahora.Content.Programado);
            Assert.IsFalse(deNoche.Content.Programado);
            Assert.AreEqual(2, programador.Encolados);
            Assert.IsNull(programador.Programado);
        }

        [TestMethod]
        public void Entrenar_SinTabla_503()
        {
            var r = Controller(RepositorioEndpoints(false), new ProgramadorFalso(), HOY.AddHours(22), "Dirección").PostEntrenarModelo() as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        }

        #endregion

        #region Entrenamiento real (LightGBM, datos sintéticos)

        /// <summary>Tres proveedores durante dos años y medio: «RAPIDO» tarda 1-3 días, «MEDIO» 7-9 y «LENTO» 18-22.</summary>
        private static List<PedidoCompraHistorico> Sinteticos()
        {
            var pedidos = new List<PedidoCompraHistorico>();
            var proveedores = new[] { (Nombre: "RAPIDO", Base: 1), (Nombre: "MEDIO", Base: 7), (Nombre: "LENTO", Base: 18) };
            int numero = 1;
            var azar = new Random(853);
            foreach ((string Nombre, int Base) p in proveedores)
            {
                for (DateTime envio = HOY.AddDays(-900); envio < HOY; envio = envio.AddDays(4))
                {
                    int dias = p.Base + azar.Next(0, p.Base == 1 ? 3 : 5);
                    DateTime albaran = envio.AddDays(dias);
                    pedidos.Add(new PedidoCompraHistorico
                    {
                        Empresa = "1",
                        Pedido = numero++,
                        Proveedor = p.Nombre,
                        FechaEnvio = envio,
                        DiasEnServir = 0,
                        Lineas = 1 + azar.Next(0, 10),
                        Importe = 50 + azar.Next(0, 1000),
                        Albaranes = albaran < HOY ? new List<DateTime> { albaran } : new List<DateTime>()
                    });
                }
            }
            return pedidos;
        }

        [TestMethod]
        public void EntrenamientoReal_EntrenaPrediceYElLentoTardaMasQueElRapido()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo entrena en 64 bits");
            }
            _ = CargadorLightGbmNativo.Asegurar();
            List<PedidoCompraHistorico> historico = Sinteticos();

            IEvaluacionModeloPlazos evaluacion = new EntrenadorModeloPlazos().Evaluar(historico, HOY);

            Assert.IsTrue(evaluacion.DatosSuficientes, evaluacion.Motivo);
            Assert.IsTrue(evaluacion.Modelo.Pedidos >= PuertaCalidadPlazos.MINIMO_VALIDACION);
            Assert.IsNotNull(evaluacion.Regla);
            Assert.IsTrue(evaluacion.Modelo.ErrorMedio < 10, "en estos datos el modelo debería acertar razonablemente: " + evaluacion.Motivo);

            var almacen = new AlmacenModeloPlazos(carpeta);
            string temporal = almacen.NuevaRutaTemporal();
            MetadatosModeloPlazos datos = evaluacion.EntrenarFinalYGuardar(temporal, HOY, "test");
            almacen.Promover(temporal, datos);
            CollectionAssert.AreEquivalent(new[] { "LENTO", "MEDIO", "RAPIDO" }, datos.Proveedores);

            int? rapido = PredictorPlazosProveedores.DiasPrudentes(almacen, new ConsultaPlazoModelo { Empresa = "1", Proveedor = "RAPIDO", FechaEnvio = HOY, Lineas = 3, Importe = 300 });
            int? lento = PredictorPlazosProveedores.DiasPrudentes(almacen, new ConsultaPlazoModelo { Empresa = "1", Proveedor = "LENTO", FechaEnvio = HOY, Lineas = 3, Importe = 300 });

            Assert.IsTrue(rapido.HasValue && lento.HasValue);
            Assert.IsTrue(lento.Value > rapido.Value + 10, $"lento {lento} frente a rápido {rapido}");
            Assert.IsTrue(rapido.Value >= 1 && rapido.Value <= 8, $"rápido {rapido}");
            Assert.IsTrue(lento.Value >= 18 && lento.Value <= 35, $"lento {lento} (prudente: no por debajo de lo normal)");
        }

        #endregion
    }
}
