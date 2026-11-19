using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Agencias.RepartoPropio.ModeloTiemposParada;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881 (corte 3): tiempos reales de cada parada, fotos de la previsión y el modelo en sombra.</summary>
    [TestClass]
    public class TiemposRealesRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 29, 12, 30, 0);
        private const string USUARIO = @"NUEVAVISION\Repartidor";

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private TiemposFalsos tiempos;
        private ServicioRepartoPropio servicio;
        private string carpeta;

        [TestInitialize]
        public void Preparar()
        {
            ServicioRepartoPropio.OlvidarAvisosTiempos();
            ModeloTiemposParadaJobsService.OlvidarAvisos();
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            tiempos = new TiemposFalsos();
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), null, new DependenciasParadasReparto { TiemposReales = tiempos });
            carpeta = Path.Combine(Path.GetTempPath(), "tiempos-parada-" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void Limpiar()
        {
            try
            {
                if (Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, true);
                }
            }
            catch (IOException)
            {
                // el modelo cargado puede tener el zip abierto
            }
        }

        // ================================================================ tiempos reales

        [TestMethod]
        public async Task Entregar_ApuntaElTiempoDeLaParada_ConLasHorasDeLaPdaYLaRegla()
        {
            _ = repositorio.Con(ServicioRepartoPropioTests.Envio(bultos: 2));
            var clave = Guid.NewGuid();
            var peticion = new EntregaRepartoDTO
            {
                ClaveIdempotencia = clave,
                FechaHora = AHORA,
                FechaHoraIr = AHORA.AddMinutes(-25),
                FechaHoraLlegada = AHORA.AddMinutes(-9),
                Latitud = 40.6m,
                Longitud = -3.5m
            };

            var resultado = await servicio.Entregar(251300, peticion, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            TiempoParadaReparto fila = tiempos.Tiempos.Single();
            Assert.AreEqual(clave, fila.ClaveIdempotencia);
            Assert.AreEqual("Repartidor", fila.Repartidor, "Sin dominio");
            Assert.AreEqual(AHORA.Date, fila.Dia);
            Assert.AreEqual(TiposParadaReparto.ENVIO, fila.TipoParada);
            Assert.AreEqual(TiempoParadaReparto.RESULTADO_ENTREGADA, fila.Resultado);
            Assert.AreEqual(251300, fila.EnvioId);
            Assert.AreEqual(928000, fila.Pedido);
            Assert.AreEqual("15191", fila.Cliente);
            Assert.AreEqual("28110", fila.CodigoPostal);
            Assert.AreEqual(2, fila.Bultos);
            Assert.IsNull(fila.FormaCobro, "Sin reembolso, sin cobro");
            Assert.AreEqual(AHORA.AddMinutes(-25), fila.FechaHoraIr);
            Assert.AreEqual(AHORA.AddMinutes(-9), fila.FechaHoraLlegada);
            Assert.AreEqual(AHORA, fila.FechaHoraFin);
            Assert.AreEqual(40.6m, fila.Latitud);
            Assert.AreEqual(15m, fila.MinutosPrevistos);
        }

        [TestMethod]
        public async Task Incidencia_TambienApuntaSuTiempo_YSinLaTablaNoSeApuntaNadaNiFallaLaEntrega()
        {
            _ = repositorio.Con(ServicioRepartoPropioTests.Envio());
            var resultado = await servicio.Incidencia(251300, new IncidenciaRepartoDTO
            {
                ClaveIdempotencia = Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.AUSENTE, FechaHora = AHORA
            }, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(TiempoParadaReparto.RESULTADO_INCIDENCIA, tiempos.Tiempos.Single().Resultado);

            tiempos.HayTablas = false;
            _ = repositorio.Con(ServicioRepartoPropioTests.Envio(251301));
            var sinTabla = await servicio.Entregar(251301, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, USUARIO);
            Assert.AreEqual(TipoResultadoReparto.Ok, sinTabla.Tipo, sinTabla.Mensaje);
            Assert.AreEqual(1, tiempos.Tiempos.Count);
        }

        [TestMethod]
        public async Task Entregar_SiFallaLaTablaDeTiempos_LaEntregaSaleIgual()
        {
            _ = repositorio.Con(ServicioRepartoPropioTests.Envio());
            tiempos.Fallar = true;

            var resultado = await servicio.Entregar(251300, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
        }

        [TestMethod]
        public async Task Estimacion_GuardaLaFotoDeLaPrevision()
        {
            var resultado = await servicio.Estimacion(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            (FotoEstimacionReparto foto, int minutos) = tiempos.Fotos.Single();
            Assert.AreEqual(FotoEstimacionReparto.CALCULO_ESTIMACION, foto.Calculo);
            Assert.AreEqual("Repartidor", foto.Repartidor);
            Assert.AreEqual(resultado.Contenido.FinEstimado, foto.FinEstimado);
            Assert.AreEqual(ConstantesTiemposReparto.QUIEN_MANDA_REGLA, foto.MinutosDe);
            Assert.AreEqual(10, minutos, "Una cada 10 minutos por repartidor");
        }

        [TestMethod]
        public void Constantes_QuienManda_SoloModeloSiLoDice()
        {
            Assert.IsFalse(ConstantesTiemposReparto.PorDefecto().MandaElModelo);
            Assert.IsTrue(ConstantesTiemposReparto.Desde(k => k == ConstantesTiemposReparto.CLAVE_QUIEN_MANDA ? " modelo " : null).MandaElModelo);
            Assert.IsFalse(ConstantesTiemposReparto.Desde(k => k == ConstantesTiemposReparto.CLAVE_QUIEN_MANDA ? "otra cosa" : null).MandaElModelo);
        }

        // ================================================================ rasgos (puros)

        private static TiempoParadaReparto Fila(DateTime fin, DateTime? anterior, string tipo = TiposParadaReparto.ENVIO, double minutosPrevistos = 15,
            DateTime? llegada = null, decimal latitud = 40.60m, decimal latitudAnterior = 40.60m)
            => new TiempoParadaReparto
            {
                ClaveIdempotencia = Guid.NewGuid(),
                Repartidor = "Andre",
                Dia = fin.Date,
                TipoParada = tipo,
                Resultado = TiempoParadaReparto.RESULTADO_ENTREGADA,
                CodigoPostal = "28110",
                Bultos = 2,
                FechaHoraFin = fin,
                FechaHoraAnterior = anterior,
                FechaHoraLlegada = llegada,
                Latitud = latitud,
                Longitud = -3.50m,
                LatitudAnterior = latitudAnterior,
                LongitudAnterior = -3.50m,
                MinutosPrevistos = (decimal)minutosPrevistos
            };

        [TestMethod]
        public void Ejemplo_IntervaloMenosConduccion_YDescartaPrimeraLargaYSinPosicion()
        {
            ConstantesTiemposReparto c = ConstantesTiemposReparto.PorDefecto();
            // Misma posición: sin conducción
            EjemploTiempoParada mismaCasa = ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, AHORA.AddMinutes(-12), llegada: AHORA.AddMinutes(-8)), c);
            Assert.AreEqual(12d, mismaCasa.Minutos, 1e-9);
            Assert.AreEqual(8d, mismaCasa.MinutosDesdeLlegada.Value, 1e-9);
            Assert.AreEqual(15d, mismaCasa.MinutosRegla);
            Assert.AreEqual("281", mismaCasa.Rasgos.Zona);
            Assert.AreEqual("ANDRE", mismaCasa.Rasgos.Repartidor);
            Assert.AreEqual(4f, mismaCasa.Rasgos.DiaSemana, "Jueves");

            // ~1,1 km en línea recta: 1,1 × 1,3 / 30 × 60 × 1,15 ≈ 3,3 min de conducción
            EjemploTiempoParada lejos = ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, AHORA.AddMinutes(-20), latitudAnterior: 40.59m), c);
            Assert.IsTrue(lejos.Minutos > 15 && lejos.Minutos < 18, $"{lejos.Minutos}");

            Assert.IsNull(ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, null), c), "La primera del día");
            Assert.IsNull(ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, AHORA.AddMinutes(-121)), c), "Más de 120 min (comida, avería…)");
            Assert.IsNull(ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, AHORA.AddDays(-1)), c), "La anterior es de otro día");
            TiempoParadaReparto sinPosicion = Fila(AHORA, AHORA.AddMinutes(-10));
            sinPosicion.Latitud = null;
            Assert.IsNull(ConstructorRasgosTiemposParada.Ejemplo(sinPosicion, c), "Sin posición no se puede quitar la conducción");
            Assert.AreEqual(0d, ConstructorRasgosTiemposParada.Ejemplo(Fila(AHORA, AHORA.AddMinutes(-1), latitudAnterior: 40.50m), c).Minutos,
                "Nunca negativo");
        }

        [TestMethod]
        public void Metricas_YVeredicto()
        {
            Assert.AreEqual(2.5d, MetricasTiemposParada.ErrorMedio(new[] { (10d, 12d), (10d, 7d) }));
            Assert.IsNull(MetricasTiemposParada.ErrorMedio(new (double, double)[0]));
            StringAssert.StartsWith(MetricasTiemposParada.Veredicto(40, 9.4, 6.1), "El modelo acierta mejor que la regla: 6,1 min");
            StringAssert.StartsWith(MetricasTiemposParada.Veredicto(40, 6.1, 9.4), "La regla acierta mejor que el modelo");
            StringAssert.StartsWith(MetricasTiemposParada.Veredicto(0, null, null), "Sin datos suficientes");
            Assert.IsTrue(PuertaCalidadTiemposParada.Decidir(null, false, 8).Promover);
            Assert.IsFalse(PuertaCalidadTiemposParada.Decidir(5, true, 6).Promover);
            Assert.IsTrue(PuertaCalidadTiemposParada.Decidir(5, true, 5.4).Promover);
        }

        // ================================================================ modelo

        [TestMethod]
        public void Entrenador_ConMenosDe200_NoEntrena_YDiceSinDatosSuficientes()
        {
            List<EjemploTiempoParada> pocos = Sinteticos(150);

            EvaluacionModeloTiemposParada e = new EntrenadorModeloTiemposParada().Evaluar(pocos, AHORA.Date);

            Assert.IsFalse(e.DatosSuficientes);
            StringAssert.Contains(e.Motivo, "sin datos suficientes");
            Assert.IsNull(e.ErrorMedioModelo);
            Assert.IsNotNull(e.ErrorMedioRegla, "La regla sí se mide");
        }

        /// <summary>Envíos de 8 min, reposiciones de 30 y servicios técnicos de 70: la regla (15/25/60) se equivoca; el modelo debería aprenderlo.</summary>
        private static List<EjemploTiempoParada> Sinteticos(int cuantos)
        {
            var azar = new Random(881);
            var lista = new List<EjemploTiempoParada>();
            for (int i = 0; i < cuantos; i++)
            {
                int tipo = i % 3;
                DateTime dia = AHORA.Date.AddDays(-(i * 120 / Math.Max(1, cuantos)));
                double real = tipo == 0 ? 8 : tipo == 1 ? 30 : 70;
                real += azar.NextDouble() * 4 - 2;
                double regla = tipo == 0 ? 15 : tipo == 1 ? 25 : 60;
                string tipoParada = tipo == 1 ? TiposParadaReparto.REPOSICION : tipo == 2 ? TiposParadaReparto.NOTA_ENTREGA : TiposParadaReparto.ENVIO;
                lista.Add(new EjemploTiempoParada
                {
                    Dia = dia,
                    Minutos = real,
                    MinutosRegla = regla,
                    MinutosDesdeLlegada = i % 2 == 0 ? real - 1 : (double?)null,
                    Rasgos = ConstructorRasgosTiemposParada.Rasgos(tipoParada, tipo == 2 ? "Montaje" : null, 1 + azar.Next(0, 4), null, null,
                        "28" + azar.Next(100, 999), dia.AddHours(9 + azar.Next(0, 7)), "Andre", null)
                });
            }
            return lista;
        }

        [TestMethod]
        public void EntrenamientoReal_AprendeLoQueLaReglaNoSabe_YSeGuardaYPredice()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo entrena en 64 bits");
            }
            _ = CargadorLightGbmNativo.Asegurar();

            EvaluacionModeloTiemposParada e = new EntrenadorModeloTiemposParada().Evaluar(Sinteticos(600), AHORA.Date);

            Assert.IsTrue(e.DatosSuficientes, e.Motivo);
            Assert.IsTrue(e.ErrorMedioModelo < e.ErrorMedioRegla, e.Veredicto);
            StringAssert.StartsWith(e.Veredicto, "El modelo acierta mejor que la regla");
            Assert.IsTrue(e.EjemplosConLlegada > 0);

            var almacen = new AlmacenModeloTiemposParada(carpeta);
            string temporal = almacen.NuevaRutaTemporal();
            almacen.Promover(temporal, e.EntrenarFinalYGuardar(temporal, AHORA, "test"));
            var predictor = new PredictorTiemposParada(almacen);
            double? envio = predictor.Minutos(ConstructorRasgosTiemposParada.Rasgos(TiposParadaReparto.ENVIO, null, 2, null, null, "28110", AHORA, "Andre", null));
            double? montaje = predictor.Minutos(ConstructorRasgosTiemposParada.Rasgos(TiposParadaReparto.NOTA_ENTREGA, "Montaje", 2, null, null, "28110", AHORA, "Andre", null));
            Assert.IsTrue(envio.HasValue && montaje.HasValue);
            Assert.IsTrue(envio.Value < 15 && montaje.Value > 50, $"envío {envio}, montaje {montaje}");
        }

        [TestMethod]
        public void Job_SinTablas_NoHaceNada_YConPocosDatos_ApuntaSinDatosSuficientes()
        {
            var repo = A.Fake<IRepositorioModeloTiemposParada>();
            A.CallTo(() => repo.TablaEntrenamientosExiste()).Returns(false);
            var errores = new List<Exception>();
            var job = new ModeloTiemposParadaJobsService(repo, new EntrenadorModeloTiemposParada(), new AlmacenModeloTiemposParada(carpeta),
                () => AHORA.Date.AddHours(4), errores.Add, () => { }, ConstantesTiemposReparto.PorDefecto);

            Assert.IsNull(job.Ejecutar("test"));
            Assert.AreEqual(1, errores.Count, "ELMAH una vez");
            A.CallTo(() => repo.RegistrarEntrenamiento(A<EntrenamientoModeloTiemposParada>._)).MustNotHaveHappened();

            A.CallTo(() => repo.TablaEntrenamientosExiste()).Returns(true);
            A.CallTo(() => repo.TablaTiemposExiste()).Returns(true);
            A.CallTo(() => repo.LeerTiempos(A<DateTime>._)).Returns(new List<TiempoParadaReparto>
            {
                Fila(AHORA, AHORA.AddMinutes(-12)), Fila(AHORA.AddMinutes(30), AHORA)
            });
            EntrenamientoModeloTiemposParada fila = job.Ejecutar("test");

            Assert.IsNotNull(fila);
            Assert.AreEqual(2, fila.Ejemplos);
            Assert.IsFalse(fila.ModeloActivo);
            StringAssert.StartsWith(fila.Motivo, "no se entrena: sin datos suficientes");
            StringAssert.StartsWith(fila.Veredicto, "Sin datos suficientes");
            A.CallTo(() => repo.RegistrarEntrenamiento(fila)).MustHaveHappenedOnceExactly();
            Assert.IsFalse(new AlmacenModeloTiemposParada(carpeta).HayModelo);
        }

        [TestMethod]
        public void Job_NoEntrenaEnHorario()
        {
            Assert.IsTrue(ModeloTiemposParadaJobsService.TocaEntrenar(AHORA.Date.AddHours(4).AddMinutes(25)));
            Assert.IsFalse(ModeloTiemposParadaJobsService.TocaEntrenar(AHORA.Date.AddHours(9)));
        }

        // ================================================================ endpoint

        [TestMethod]
        public void Comparacion_SinPermiso403_ConPermisoYSinTablas_LoDice()
        {
            var repo = A.Fake<IRepositorioModeloTiemposParada>();
            A.CallTo(() => repo.TablaEntrenamientosExiste()).Returns(false);
            var controlador = new TiemposRepartoController(() => repo, () => false, ConstantesTiemposReparto.PorDefecto)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN) }, "Prueba"))
            };
            Assert.AreEqual(HttpStatusCode.Forbidden, controlador.GetComparacion().ExecuteAsync(CancellationToken.None).Result.StatusCode);

            controlador.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Carlos"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.DIRECCION) }, "Prueba"));
            var ok = controlador.GetComparacion() as System.Web.Http.Results.OkNegotiatedContentResult<ComparacionTiemposParadaDTO>;
            Assert.IsNotNull(ok);
            Assert.IsFalse(ok.Content.TablasCreadas);
            Assert.AreEqual(ConstantesTiemposReparto.QUIEN_MANDA_REGLA, ok.Content.QuienManda);

            A.CallTo(() => repo.TablaEntrenamientosExiste()).Returns(true);
            A.CallTo(() => repo.TablaTiemposExiste()).Returns(true);
            A.CallTo(() => repo.Ultimos(A<int>._)).Returns(new List<EntrenamientoModeloTiemposParada>
            {
                new EntrenamientoModeloTiemposParada { Id = 2, Veredicto = "La regla acierta mejor que el modelo: 5 min…" },
                new EntrenamientoModeloTiemposParada { Id = 1 }
            });
            ok = controlador.GetComparacion() as System.Web.Http.Results.OkNegotiatedContentResult<ComparacionTiemposParadaDTO>;
            Assert.AreEqual(2, ok.Content.UltimoEntrenamiento.Id);
            Assert.AreEqual(2, ok.Content.Historia.Count);
            StringAssert.StartsWith(ok.Content.Veredicto, "La regla acierta mejor");
        }

        // ================================================================ dobles

        internal class TiemposFalsos : IRepositorioTiemposReparto
        {
            public bool HayTablas = true;
            public bool Fallar;
            public readonly List<TiempoParadaReparto> Tiempos = new List<TiempoParadaReparto>();
            public readonly List<(FotoEstimacionReparto, int)> Fotos = new List<(FotoEstimacionReparto, int)>();

            public bool TablaTiemposExiste() => HayTablas;
            public bool TablaEstimacionesExiste() => HayTablas;

            public Task<int> RegistrarTiempo(TiempoParadaReparto fila)
            {
                if (Fallar)
                {
                    throw new InvalidOperationException("se ha caído la BD");
                }
                if (Tiempos.Any(t => t.ClaveIdempotencia == fila.ClaveIdempotencia))
                {
                    return Task.FromResult(0);
                }
                Tiempos.Add(fila);
                return Task.FromResult(1);
            }

            public Task RegistrarEstimacion(FotoEstimacionReparto foto, int minutos)
            {
                Fotos.Add((foto, minutos));
                return Task.CompletedTask;
            }

            public List<TiempoParadaReparto> LeerTiempos(DateTime desde) => Tiempos.ToList();
        }
    }
}
