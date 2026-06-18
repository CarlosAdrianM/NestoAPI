using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Control de tasa de DataTrans DTX: ~2 s entre llamadas y máx 50 por ventana de 5 min.
    /// El reloj y la espera se inyectan para no dormir de verdad: la "espera" avanza el reloj.
    /// </summary>
    [TestClass]
    public class ControlTasaDataTransTests
    {
        private DateTime _reloj;
        private List<TimeSpan> _esperas;

        private ControlTasaDataTrans CrearControl(TimeSpan intervaloMinimo, int maxPorVentana, TimeSpan ventana)
        {
            _reloj = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _esperas = new List<TimeSpan>();
            return new ControlTasaDataTrans(
                intervaloMinimo: intervaloMinimo,
                maxPorVentana: maxPorVentana,
                ventana: ventana,
                ahora: () => _reloj,
                esperar: t =>
                {
                    _esperas.Add(t);
                    _reloj += t; // simular el paso del tiempo durante la espera
                    return Task.CompletedTask;
                });
        }

        [TestMethod]
        public async Task EsperarTurno_PrimeraLlamada_NoEspera()
        {
            ControlTasaDataTrans control = CrearControl(TimeSpan.FromSeconds(2), 50, TimeSpan.FromMinutes(5));

            await control.EsperarTurnoAsync();

            Assert.AreEqual(0, _esperas.Count, "La primera llamada no debe esperar.");
        }

        [TestMethod]
        public async Task EsperarTurno_DosLlamadasSeguidas_RespetaElIntervaloMinimo()
        {
            ControlTasaDataTrans control = CrearControl(TimeSpan.FromSeconds(2), 50, TimeSpan.FromMinutes(5));

            await control.EsperarTurnoAsync();
            await control.EsperarTurnoAsync();

            Assert.AreEqual(1, _esperas.Count);
            Assert.AreEqual(TimeSpan.FromSeconds(2), _esperas[0]);
        }

        [TestMethod]
        public async Task EsperarTurno_SiYaPasoElIntervalo_NoEspera()
        {
            ControlTasaDataTrans control = CrearControl(TimeSpan.FromSeconds(2), 50, TimeSpan.FromMinutes(5));

            await control.EsperarTurnoAsync();
            _reloj += TimeSpan.FromSeconds(3); // pasan 3 s entre llamadas (> intervalo)
            await control.EsperarTurnoAsync();

            Assert.AreEqual(0, _esperas.Count, "Si ya pasó el intervalo mínimo, no hay que esperar.");
        }

        [TestMethod]
        public async Task EsperarTurno_AlLlegarAlTopeDeVentana_EsperaHastaQueLaVentanaLibera()
        {
            // Aislamos el efecto ventana: intervalo mínimo 0, tope 3 por 5 min.
            ControlTasaDataTrans control = CrearControl(TimeSpan.Zero, 3, TimeSpan.FromMinutes(5));

            // 3 llamadas dentro de la ventana, separadas 1 s (no hay espera por intervalo).
            for (int i = 0; i < 3; i++)
            {
                await control.EsperarTurnoAsync();
                _reloj += TimeSpan.FromSeconds(1);
            }
            Assert.AreEqual(0, _esperas.Count, "Las 3 primeras (bajo el tope) no esperan.");

            // La 4ª supera el tope: debe esperar hasta que la 1ª salga de la ventana de 5 min.
            await control.EsperarTurnoAsync();

            Assert.AreEqual(1, _esperas.Count);
            Assert.IsTrue(_esperas[0] > TimeSpan.FromMinutes(4),
                $"Debía esperar casi 5 min a que liberara la ventana, esperó {_esperas[0]}.");
        }
    }
}
