using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881 (corte 5): los km por mapa de cada jornada: sede → paradas con posición por la hora real → sede, con el motor del fin
    /// de ruta (aquí falso: sin llamadas a OSRM) y la línea recta de respaldo; el job solo recalcula lo que falta o ha cambiado; el
    /// informe los enseña por jornada y en los totales.
    /// </summary>
    [TestClass]
    public class KmJornadasRepartoTests
    {
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 19);
        private static readonly DateTime HOY = new DateTime(2026, 11, 26, 3, 50, 0);

        private static PosicionParadaReparto Parada(long id, int hora, decimal? lat, decimal? lon, string repartidor = "Miguel", DateTime? dia = null)
            => new PosicionParadaReparto { Id = id, Repartidor = repartidor, Dia = dia ?? JUEVES, FechaHoraFin = (dia ?? JUEVES).AddHours(hora), Latitud = lat, Longitud = lon };

        /// <summary>Un motor falso: 10 km cada tramo (como si fuera OSRM).</summary>
        private static IMatrizTiempos MotorDiezKm(List<int> llamadas = null)
        {
            IMatrizTiempos motor = A.Fake<IMatrizTiempos>();
            A.CallTo(() => motor.Calcular(A<IReadOnlyList<CoordenadasGeo>>._)).ReturnsLazily((IReadOnlyList<CoordenadasGeo> puntos) =>
            {
                llamadas?.Add(puntos.Count);
                int n = puntos.Count;
                var minutos = new double[n, n];
                var km = new double[n, n];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        km[i, j] = i == j ? 0 : 10;
                    }
                }
                return Task.FromResult(new MatrizTiempos(minutos, km, OrdenSugeridoRepartoDTO.MOTOR_OSRM));
            });
            return motor;
        }

        [TestMethod]
        public void Recorrido_SedeParadasPorLaHoraRealSede_SinPosicionYMismoSitioFuera()
        {
            var paradas = new List<PosicionParadaReparto>
            {
                Parada(3, 12, 40.50m, -3.60m),
                Parada(1, 9, 40.40m, -3.70m),
                Parada(2, 10, null, null),
                Parada(4, 13, 40.500001m, -3.600001m), // el mismo portal: una vez
                Parada(5, 14, 0m, 0m)
            };

            List<CoordenadasGeo> puntos = CalculoKmJornada.Recorrido(paradas, SedesReparto.ALGETE, out int sinPosicion, out int conPosicion);

            Assert.AreEqual(4, puntos.Count, "sede, 9:00, 12:00 (y 13:00 en el mismo sitio), sede");
            Assert.AreEqual(SedesReparto.ALGETE.Latitud, puntos[0].Latitud);
            Assert.AreEqual(40.40m, puntos[1].Latitud, "por la hora real, no por el Id");
            Assert.AreEqual(SedesReparto.ALGETE.Latitud, puntos[3].Latitud);
            Assert.AreEqual(2, sinPosicion);
            Assert.AreEqual(3, conPosicion);
        }

        [TestMethod]
        public void Bloques_SolapadosEnUno_CadaTramoEnAlguno()
        {
            List<(int Desde, int Hasta)> bloques = CalculoKmJornada.Bloques(130, 60);

            CollectionAssert.AreEqual(new[] { (0, 59), (59, 118), (118, 129) }, bloques.ToArray());
            Assert.AreEqual(0, CalculoKmJornada.Bloques(1, 60).Count);
        }

        [TestMethod]
        public async Task Calcular_ConElMapa_SumaLosTramos()
        {
            var servicio = new ServicioKmJornadasReparto(A.Fake<IRepositorioKmJornadas>(), MotorDiezKm(), () => HOY);

            KmJornadaReparto km = await servicio.Calcular("Miguel", JUEVES, new[] { Parada(1, 9, 40.40m, -3.70m), Parada(2, 10, 40.50m, -3.60m) });

            Assert.AreEqual(30m, km.Km, "sede → 1 → 2 → sede: 3 tramos");
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_OSRM, km.Motor);
            Assert.IsFalse(km.Aproximada);
            Assert.AreEqual(2, km.Paradas);
        }

        [TestMethod]
        public async Task Calcular_MasDeSesentaPuntos_VariasPeticionesSinTramosRepetidos()
        {
            var llamadas = new List<int>();
            var servicio = new ServicioKmJornadasReparto(A.Fake<IRepositorioKmJornadas>(), MotorDiezKm(llamadas), () => HOY);
            List<PosicionParadaReparto> paradas = Enumerable.Range(1, 70).Select(i => Parada(i, 8, 40m + i * 0.01m, -3.5m)).ToList();

            KmJornadaReparto km = await servicio.Calcular("Miguel", JUEVES, paradas);

            Assert.AreEqual(71 * 10m, km.Km, "72 puntos, 71 tramos");
            Assert.IsTrue(llamadas.All(n => n <= MatrizTiemposOsrm.MAXIMO_PUNTOS));
            Assert.AreEqual(2, llamadas.Count);
        }

        [TestMethod]
        public async Task Calcular_SinMapa_LineaRectaYAproximada()
        {
            IMatrizTiempos motor = A.Fake<IMatrizTiempos>();
            A.CallTo(() => motor.Calcular(A<IReadOnlyList<CoordenadasGeo>>._)).Returns(Task.FromResult<MatrizTiempos>(null));
            var servicio = new ServicioKmJornadasReparto(A.Fake<IRepositorioKmJornadas>(), motor, () => HOY);
            CoordenadasGeo punto = new CoordenadasGeo(40.40m, -3.70m);

            KmJornadaReparto km = await servicio.Calcular("Miguel", JUEVES, new[] { Parada(1, 9, punto.Latitud, punto.Longitud) });

            double esperado = 2 * HeuristicaRutas.DistanciaKm(SedesReparto.ALGETE, punto) * HeuristicaRutas.FACTOR_CARRETERA;
            Assert.AreEqual(Math.Round((decimal)esperado, 1), km.Km);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, km.Motor);
            Assert.IsTrue(km.Aproximada);
        }

        [TestMethod]
        public async Task CalcularPendientes_SoloLoQueFaltaOHaCambiado_SinHoy()
        {
            IRepositorioKmJornadas repositorio = A.Fake<IRepositorioKmJornadas>();
            A.CallTo(() => repositorio.Disponible()).Returns(true);
            DateTime viernes = JUEVES.AddDays(1);
            A.CallTo(() => repositorio.LeerParadas(A<DateTime>._, A<DateTime>._)).Returns(Task.FromResult(new List<PosicionParadaReparto>
            {
                Parada(1, 9, 40.40m, -3.70m),
                Parada(2, 10, 40.50m, -3.60m, repartidor: @"NUEVAVISION\Pedro"),
                Parada(3, 9, 40.40m, -3.70m, dia: viernes)
            }));
            A.CallTo(() => repositorio.Leer(A<DateTime>._, A<DateTime>._)).Returns(Task.FromResult(new List<KmJornadaReparto>
            {
                new KmJornadaReparto { Repartidor = "Miguel", Dia = JUEVES, Paradas = 1, Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM },            // ya está
                new KmJornadaReparto { Repartidor = "Miguel", Dia = viernes, Paradas = 1, Motor = OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA }      // sin mapa: otra vez
            }));
            var servicio = new ServicioKmJornadasReparto(repositorio, MotorDiezKm(), () => HOY);

            int guardadas = await servicio.CalcularPendientes();

            Assert.AreEqual(2, guardadas, "Pedro el jueves (no tenía) y Miguel el viernes (se calculó sin mapa)");
            A.CallTo(() => repositorio.LeerParadas(new DateTime(2026, 11, 19), new DateTime(2026, 11, 25))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Guardar(A<KmJornadaReparto>.That.Matches(k => k.Repartidor == "Pedro" && k.Dia == JUEVES))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Guardar(A<KmJornadaReparto>.That.Matches(k => k.Repartidor == "Miguel" && k.Dia == JUEVES))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CalcularPendientes_SinTabla_Nada()
        {
            IRepositorioKmJornadas repositorio = A.Fake<IRepositorioKmJornadas>();
            A.CallTo(() => repositorio.Disponible()).Returns(false);

            Assert.AreEqual(0, await new ServicioKmJornadasReparto(repositorio, MotorDiezKm(), () => HOY).CalcularPendientes());
            A.CallTo(() => repositorio.LeerParadas(A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Informe_KmPorJornadaYEnLosTotales()
        {
            var datos = new DatosInformeReparto
            {
                Tiempos = new List<TiempoParadaReparto>
                {
                    new TiempoParadaReparto { Repartidor = "Miguel", Dia = JUEVES, Resultado = TiempoParadaReparto.RESULTADO_ENTREGADA, FechaHoraFin = JUEVES.AddHours(10) },
                    new TiempoParadaReparto { Repartidor = "Miguel", Dia = JUEVES.AddDays(1), Resultado = TiempoParadaReparto.RESULTADO_ENTREGADA, FechaHoraFin = JUEVES.AddDays(1).AddHours(10) }
                },
                Km = new List<KmJornadaReparto>
                {
                    new KmJornadaReparto { Repartidor = "Miguel", Dia = JUEVES, Km = 84.3m, Aproximada = true, ParadasSinPosicion = 1 },
                    new KmJornadaReparto { Repartidor = "Pedro", Dia = JUEVES, Km = 50m } // sin jornada en el informe: no aparece
                }
            };

            InformeRepartoDTO informe = CalculoInformeReparto.Calcular(JUEVES, JUEVES.AddDays(1), datos, null, HOY);

            JornadaInformeRepartoDTO jueves = informe.Repartidores.Single().Dias.First();
            Assert.AreEqual(84.3m, jueves.Km);
            Assert.AreEqual(true, jueves.KmAproximados);
            Assert.AreEqual(1, jueves.ParadasSinPosicion);
            Assert.IsNull(informe.Repartidores.Single().Dias.Last().Km, "el viernes aún sin km");
            Assert.AreEqual(84.3m, informe.Totales.Km);
            Assert.AreEqual(1, informe.Totales.JornadasConKm);
            Assert.AreEqual(84.3m, informe.Totales.MediaKm);
            Assert.AreEqual(1, informe.Repartidores.Count, "Pedro no tiene paradas en el informe");
        }

        [TestMethod]
        public void Informe_SinTablaDeKm_KmNullComoAntes()
        {
            var datos = new DatosInformeReparto
            {
                Tiempos = new List<TiempoParadaReparto>
                {
                    new TiempoParadaReparto { Repartidor = "Miguel", Dia = JUEVES, Resultado = TiempoParadaReparto.RESULTADO_ENTREGADA, FechaHoraFin = JUEVES.AddHours(10) }
                }
            };

            InformeRepartoDTO informe = CalculoInformeReparto.Calcular(JUEVES, JUEVES, datos, null, HOY);

            Assert.IsNull(informe.Repartidores.Single().Dias.Single().Km);
            Assert.IsNull(informe.Totales.Km);
            Assert.AreEqual(0, informe.Totales.JornadasConKm);
        }
    }
}
