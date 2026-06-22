using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Innovatrans;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Control de tasa de DataTrans DTX: SIN espaciado por llamada (velocidad plena) y máx 50 por
    /// ventana de 5 min; solo espera al alcanzar el tope. El reloj y la espera se inyectan para no
    /// dormir de verdad: la "espera" avanza el reloj.
    /// </summary>
    [TestClass]
    public class ControlTasaDataTransTests
    {
        private DateTime _reloj;
        private List<TimeSpan> _esperas;

        private ControlTasaDataTrans CrearControl(int maxPorVentana, TimeSpan ventana)
        {
            _reloj = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _esperas = new List<TimeSpan>();
            return new ControlTasaDataTrans(
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
            ControlTasaDataTrans control = CrearControl(50, TimeSpan.FromMinutes(5));

            await control.EsperarTurnoAsync();

            Assert.AreEqual(0, _esperas.Count, "La primera llamada no debe esperar.");
        }

        [TestMethod]
        public async Task EsperarTurno_LlamadasSeguidasBajoElTope_NoEsperanNada()
        {
            // Lo importante del cambio: NO hay hueco de 2 s entre llamadas. 49 llamadas seguidas (bajo
            // el tope de 50) salen a velocidad plena, sin una sola espera.
            ControlTasaDataTrans control = CrearControl(50, TimeSpan.FromMinutes(5));

            for (int i = 0; i < 49; i++)
            {
                await control.EsperarTurnoAsync();
            }

            Assert.AreEqual(0, _esperas.Count, "Por debajo del tope no se espera nada (sin espaciado por llamada).");
        }

        [TestMethod]
        public async Task EsperarTurno_AlLlegarAlTopeDeVentana_EsperaHastaQueLaVentanaLibera()
        {
            // Tope 3 por 5 min para aislar el efecto ventana.
            ControlTasaDataTrans control = CrearControl(3, TimeSpan.FromMinutes(5));

            // 3 llamadas dentro de la ventana, separadas 1 s. Ninguna espera (estamos en el tope, no por encima).
            for (int i = 0; i < 3; i++)
            {
                await control.EsperarTurnoAsync();
                _reloj += TimeSpan.FromSeconds(1);
            }
            Assert.AreEqual(0, _esperas.Count, "Las 3 primeras (hasta el tope) no esperan.");

            // La 4ª supera el tope: debe esperar hasta que la 1ª salga de la ventana de 5 min.
            await control.EsperarTurnoAsync();

            Assert.AreEqual(1, _esperas.Count);
            Assert.IsTrue(_esperas[0] > TimeSpan.FromMinutes(4),
                $"Debía esperar casi 5 min a que liberara la ventana, esperó {_esperas[0]}.");
        }

        [TestMethod]
        public async Task EsperarTurno_TrasEsperarPorElTope_LaSiguienteVuelveAVelocidadPlena()
        {
            // Tope 2 por 5 min. Llenamos el tope, la 3ª espera; tras liberar, la 4ª no debe volver a esperar.
            ControlTasaDataTrans control = CrearControl(2, TimeSpan.FromMinutes(5));

            await control.EsperarTurnoAsync();
            await control.EsperarTurnoAsync();
            await control.EsperarTurnoAsync(); // supera el tope -> 1 espera (avanza el reloj 5 min)

            Assert.AreEqual(1, _esperas.Count);

            await control.EsperarTurnoAsync(); // ya hay hueco en la ventana -> sin nueva espera

            Assert.AreEqual(1, _esperas.Count, "Tras liberar la ventana, vuelve a velocidad plena.");
        }
    }
}
