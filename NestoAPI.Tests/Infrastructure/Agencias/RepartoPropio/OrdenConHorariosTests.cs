using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#769: la segunda pasada del orden sugerido con ventanas horarias (pura, sin red): la llegada a una parada con su
    /// horario (espera tope), la evaluación de un orden, la mejora (inserción + 2-opt/or-opt) y la matriz /table de OSRM.
    /// </summary>
    [TestClass]
    public class OrdenConHorariosTests
    {
        // Viernes 30/10/26 a las 08:00.
        private static readonly DateTime SALIDA = new DateTime(2026, 10, 30, 8, 0, 0);

        private static List<FranjaHorario> Franjas(string texto) => texto == null ? null : FormatoHorarioPreferible.Leer(texto, out _);

        /// <summary>
        /// Puntos: 0 = origen, 1 = A (L-V 10-14), 2 = B (L-V 09-13), 3 = C (sin horario), 4 = destino (= origen).
        /// Orden A, B, C: A a las 08:30 (abre a las 10: 90 min de espera, fuera). Orden C, B, A: todas dentro (A espera 10 min).
        /// </summary>
        private static MatrizTiempos Matriz()
        {
            double[,] m = new double[5, 5];
            void Tramo(int a, int b, double minutos) { m[a, b] = minutos; m[b, a] = minutos; }
            Tramo(0, 1, 30); Tramo(0, 2, 45); Tramo(0, 3, 20); Tramo(0, 4, 0);
            Tramo(1, 2, 30); Tramo(1, 3, 25); Tramo(1, 4, 30);
            Tramo(2, 3, 50); Tramo(2, 4, 45);
            Tramo(3, 4, 20);
            return new MatrizTiempos(m, null, OrdenSugeridoRepartoDTO.MOTOR_OSRM);
        }

        private static readonly List<List<FranjaHorario>> FRANJAS = new List<List<FranjaHorario>>
        {
            Franjas("L-V 10:00-14:00"), Franjas("L-V 09:00-13:00"), null
        };

        [TestMethod]
        public void Llegar_DentroSinEspera_AntesDeAbrirEsperaHastaElTope_YMasAllaFuera()
        {
            List<FranjaHorario> franjas = Franjas("L-V 10:00-14:00, 17:00-20:00");

            LlegadaParada dentro = VentanaHorario.Llegar(SALIDA.AddHours(3), franjas, OrdenConHorarios.ESPERA_TOPE_MINUTOS);
            Assert.IsFalse(dentro.Fuera);
            Assert.AreEqual(0d, dentro.MinutosEspera);

            LlegadaParada espera = VentanaHorario.Llegar(SALIDA.AddMinutes(100), franjas, OrdenConHorarios.ESPERA_TOPE_MINUTOS);
            Assert.IsFalse(espera.Fuera, "Llega a las 09:40 y abre a las 10:00: espera 20 min");
            Assert.AreEqual(20d, espera.MinutosEspera);
            Assert.AreEqual(SALIDA.Date.AddHours(10), espera.Hora);

            LlegadaParada tarde = VentanaHorario.Llegar(SALIDA.Date.AddHours(16).AddMinutes(20), franjas, OrdenConHorarios.ESPERA_TOPE_MINUTOS);
            Assert.IsTrue(tarde.Fuera, "16:20: faltan 40 min para las 17:00, más que la espera tope");
            Assert.AreEqual(0d, tarde.MinutosEspera);

            Assert.IsTrue(VentanaHorario.Llegar(SALIDA.AddMinutes(100), franjas, 0).Fuera, "Sin espera (como #763): fuera");
            Assert.IsFalse(VentanaHorario.Llegar(SALIDA, null, 30).Fuera, "Sin horario nunca está fuera");
            Assert.IsFalse(VentanaHorario.Llegar(SALIDA, null, 30).ConHorario);
            Assert.IsTrue(VentanaHorario.Llegar(SALIDA.AddHours(3), Franjas("S 10:00-13:00"), 30).Fuera, "Tiene horario, pero no los viernes");
        }

        [TestMethod]
        public void Evaluar_SumaConduccionParadaYEspera_YCuentaLasDeFuera()
        {
            EvaluacionOrden corto = OrdenConHorarios.Evaluar(Matriz(), FRANJAS, SALIDA, new[] { 0, 1, 2 });
            Assert.AreEqual(1, corto.Fuera);
            CollectionAssert.AreEqual(new[] { true, false, false }, corto.FueraPorParada);
            Assert.AreEqual(145d, corto.MinutosTotales, "30 + 5 + 30 + 5 + 50 + 5 + 20");
            Assert.AreEqual(130d, corto.MinutosConduccion);

            EvaluacionOrden alReves = OrdenConHorarios.Evaluar(Matriz(), FRANJAS, SALIDA, new[] { 2, 1, 0 });
            Assert.AreEqual(0, alReves.Fuera);
            Assert.AreEqual(SALIDA.Date.AddHours(10), alReves.Llegadas[2], "A espera de 09:50 a 10:00");
            Assert.AreEqual(155d, alReves.MinutosTotales);
            Assert.IsTrue(alReves.Coste < corto.Coste, "La penalización por llegar fuera pesa más que 10 min");
        }

        [TestMethod]
        public void Mejorar_ReordenaParaQueNingunaLlegueFuera_ConLosMinimosMinutos()
        {
            EvaluacionOrden mejor = OrdenConHorarios.Mejorar(Matriz(), FRANJAS, SALIDA, new[] { 0, 1, 2 });

            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, mejor.Orden);
            Assert.AreEqual(0, mejor.Fuera);
            Assert.AreEqual(155d, mejor.MinutosTotales);
        }

        [TestMethod]
        public void Mejorar_SiNoSePuedeArreglar_SigueFuera_YNoAlargaElRecorrido()
        {
            // A solo abre de 18 a 20: ningún orden llega; los demás no tienen horario.
            var franjas = new List<List<FranjaHorario>> { Franjas("L-V 18:00-20:00"), null, null };

            EvaluacionOrden mejor = OrdenConHorarios.Mejorar(Matriz(), franjas, SALIDA, new[] { 0, 1, 2 });

            Assert.AreEqual(1, mejor.Fuera);
            Assert.IsTrue(mejor.MinutosTotales <= OrdenConHorarios.Evaluar(Matriz(), franjas, SALIDA, new[] { 0, 1, 2 }).MinutosTotales);
        }

        [TestMethod]
        public void Mejorar_EsDeterminista_ConEmpates()
        {
            // 8 paradas en los vértices de una rejilla con tiempos iguales (muchos empates).
            int n = 8;
            double[,] m = new double[n + 2, n + 2];
            for (int i = 0; i < n + 2; i++)
            {
                for (int j = 0; j < n + 2; j++)
                {
                    m[i, j] = i == j ? 0 : 10 + ((i + j) % 3) * 5;
                }
            }
            var matriz = new MatrizTiempos(m, null, OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA);
            var franjas = Enumerable.Range(0, n).Select(i => i % 2 == 0 ? Franjas($"L-V {9 + i / 2:00}:00-{10 + i / 2:00}:00") : null).ToList();
            int[] inicial = Enumerable.Range(0, n).Reverse().ToArray();

            EvaluacionOrden uno = OrdenConHorarios.Mejorar(matriz, franjas, SALIDA, inicial);
            EvaluacionOrden dos = OrdenConHorarios.Mejorar(matriz, franjas, SALIDA, inicial);

            CollectionAssert.AreEqual(uno.Orden, dos.Orden);
            Assert.AreEqual(uno.Coste, dos.Coste);
            CollectionAssert.AreEquivalent(Enumerable.Range(0, n).ToList(), uno.Orden, "Visita todas una sola vez");
        }

        [TestMethod]
        public void Mejorar_ConElMaximoDePuntosDeTable_TardaPoco()
        {
            // 58 paradas (60 puntos con origen y destino), la mitad con franja: la PDA está esperando.
            int n = MatrizTiemposOsrm.MAXIMO_PUNTOS - 2;
            var puntos = Enumerable.Range(0, n + 2).Select(i => new CoordenadasGeo(40.3m + (i * 37 % 61) / 200m, -3.9m + (i * 23 % 59) / 150m)).ToList();
            MatrizTiempos matriz = MatrizTiempos.Heuristica(puntos);
            var franjas = Enumerable.Range(0, n).Select(i => i % 2 == 0 ? Franjas($"L-V {8 + i % 6:00}:00-{10 + i % 6:00}:00") : null).ToList();
            var reloj = System.Diagnostics.Stopwatch.StartNew();

            EvaluacionOrden mejor = OrdenConHorarios.Mejorar(matriz, franjas, SALIDA, Enumerable.Range(0, n).ToList());

            reloj.Stop();
            Assert.IsTrue(reloj.Elapsed < TimeSpan.FromSeconds(5), $"{reloj.ElapsedMilliseconds} ms");
            Assert.IsTrue(mejor.Fuera <= OrdenConHorarios.Evaluar(matriz, franjas, SALIDA, Enumerable.Range(0, n).ToList()).Fuera);
        }

        [TestMethod]
        public void MatrizHeuristica_LineaRectaPorFactorCarreteraAVelocidadMedia()
        {
            var a = new CoordenadasGeo(40.598625m, -3.521404m);
            var b = new CoordenadasGeo(40.420467m, -3.700505m);

            MatrizTiempos m = MatrizTiempos.Heuristica(new[] { a, b });

            double km = HeuristicaRutas.DistanciaKm(a, b) * HeuristicaRutas.FACTOR_CARRETERA;
            Assert.AreEqual(km / ServicioRepartoPropio.VELOCIDAD_MEDIA_KMH * 60d, m.Minutos[0, 1], 1e-9);
            Assert.AreEqual(km, m.Kilometros[1, 0], 1e-9);
            Assert.AreEqual(0d, m.Minutos[1, 1]);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, m.Motor);
        }

        [TestMethod]
        public void Osrm_Table_UrlYLeer_EnMinutosYKilometros_YMalONulosNull()
        {
            var puntos = new[] { new CoordenadasGeo(40.5m, -3.5m), new CoordenadasGeo(40.4m, -3.7m) };
            Assert.AreEqual("https://router.project-osrm.org/table/v1/driving/-3.5,40.5;-3.7,40.4?annotations=duration,distance",
                MatrizTiemposOsrm.Url(puntos));

            const string json = @"{""code"":""Ok"",""durations"":[[0,600],[660,0]],""distances"":[[0,12000],[12500,0]]}";
            MatrizTiempos m = MatrizTiemposOsrm.Leer(json, 2);
            Assert.AreEqual(10d, m.Minutos[0, 1]);
            Assert.AreEqual(11d, m.Minutos[1, 0]);
            Assert.AreEqual(12.5d, m.Kilometros[1, 0]);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_OSRM, m.Motor);

            Assert.IsNull(MatrizTiemposOsrm.Leer(@"{""code"":""NoRoute""}", 2));
            Assert.IsNull(MatrizTiemposOsrm.Leer(@"{""code"":""Ok"",""durations"":[[0,null],[660,0]]}", 2), "Un tramo sin ruta: la heurística");
            Assert.IsNull(MatrizTiemposOsrm.Leer(@"{""code"":""Ok"",""durations"":[[0,600]]}", 2), "No cuadra");
            Assert.IsNull(MatrizTiemposOsrm.Leer(@"{""code"":""Ok"",""durations"":[[0,600],[660,0]]}", 2).Kilometros, "Sin distancias: solo minutos");
        }
    }
}
