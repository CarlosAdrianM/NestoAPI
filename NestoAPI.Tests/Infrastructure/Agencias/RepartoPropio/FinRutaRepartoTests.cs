using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#881 (corte 1): las reglas puras del fin previsto de ruta.</summary>
    [TestClass]
    public class FinRutaRepartoTests
    {
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 19, 9, 0, 0);
        private static readonly ConstantesTiemposReparto C = ConstantesTiemposReparto.PorDefecto();

        // ---------------------------------------------------------------- minutos por parada

        [TestMethod]
        public void MinutosParada_PorTipo_LasDecisionesDeCarlos()
        {
            Assert.AreEqual(15d, MinutosParadaReparto.DeTipo(TiposParadaReparto.ENVIO, C));
            Assert.AreEqual(15d, MinutosParadaReparto.DeTipo(TiposParadaReparto.NOTA_ENTREGA, C));
            Assert.AreEqual(10d, MinutosParadaReparto.DeTipo(TiposParadaReparto.NOTA_COBRO, C));
            Assert.AreEqual(15d, MinutosParadaReparto.DeTipo(TiposParadaReparto.NOTA_RECOGIDA, C));
            Assert.AreEqual(25d, MinutosParadaReparto.DeTipo(TiposParadaReparto.REPOSICION, C));
            Assert.AreEqual(15d, MinutosParadaReparto.DeTipo(null, C), "Sin tipo, un envío");
        }

        [TestMethod]
        public void MinutosParada_ServicioTecnico_LosSuyos_OSinEllos60()
        {
            Assert.AreEqual(90d, MinutosParadaReparto.De(new ParadaParaTiempo { TipoParada = TiposParadaReparto.NOTA_ENTREGA, ConServicioTecnico = true, MinutosServicio = 90 }, C));
            Assert.AreEqual(60d, MinutosParadaReparto.De(new ParadaParaTiempo { TipoParada = TiposParadaReparto.NOTA_RECOGIDA, ConServicioTecnico = true }, C));
            Assert.AreEqual(15d, MinutosParadaReparto.De(new ParadaParaTiempo { TipoParada = TiposParadaReparto.NOTA_ENTREGA, MinutosServicio = 90 }, C),
                "Sin servicio técnico, los minutos no cuentan");
        }

        [TestMethod]
        public void MinutosParada_MismaDireccion_CuentaUnaVez_LaMayor_EnLaPrimera()
        {
            var paradas = new List<ParadaParaTiempo>
            {
                new ParadaParaTiempo { TipoParada = TiposParadaReparto.NOTA_COBRO, Empresa = "1", Cliente = "1001", Contacto = "0" },
                new ParadaParaTiempo { TipoParada = TiposParadaReparto.ENVIO, Empresa = "1", Cliente = "2002", Contacto = "0", Latitud = 40.5m, Longitud = -3.6m },
                new ParadaParaTiempo { TipoParada = TiposParadaReparto.NOTA_ENTREGA, Empresa = "1", Cliente = "1001 ", Contacto = "0", ConServicioTecnico = true, MinutosServicio = 45 },
                new ParadaParaTiempo { TipoParada = TiposParadaReparto.REPOSICION, Latitud = 40.50001m, Longitud = -3.60001m }
            };

            List<double> minutos = MinutosParadaReparto.Recorrido(paradas, C);

            CollectionAssert.AreEqual(new[] { 45d, 25d, 0d, 0d }, minutos,
                "1001/0 dos veces: 45 en la primera; la reposición en las mismas coordenadas que 2002: 25 en la primera");
        }

        [TestMethod]
        public void Constantes_SeCambianPorParametro_YLoQueNoSeEntiendeSeIgnora()
        {
            var valores = new Dictionary<string, string>
            {
                [ConstantesTiemposReparto.CLAVE_ENVIO] = "12",
                [ConstantesTiemposReparto.CLAVE_COLCHON] = "20 %",
                [ConstantesTiemposReparto.CLAVE_CIERRE_VIERNES] = "14:30",
                [ConstantesTiemposReparto.CLAVE_REPOSICION] = "mucho",
                [ConstantesTiemposReparto.CLAVE_CIERRE_LUNES_JUEVES] = "25:00",
                [ConstantesTiemposReparto.CLAVE_NOTA_COBRO] = "7,5"
            };

            ConstantesTiemposReparto c = ConstantesTiemposReparto.Desde(k => valores.TryGetValue(k, out string v) ? v : null);

            Assert.AreEqual(12d, c.MinutosEnvio);
            Assert.AreEqual(1.2d, c.FactorConduccion, 1e-9);
            Assert.AreEqual(new TimeSpan(14, 30, 0), c.CierreViernes);
            Assert.AreEqual(25d, c.MinutosReposicion, "No es un número: el del código");
            Assert.AreEqual(new TimeSpan(17, 0, 0), c.CierreLunesAJueves, "No es una hora: la del código");
            Assert.AreEqual(7.5d, c.MinutosNotaCobro);
            Assert.AreEqual(15d, ConstantesTiemposReparto.Desde(null).MinutosEnvio);
        }

        // ---------------------------------------------------------------- cierre

        [TestMethod]
        public void Cierre_LunesAJueves17_Viernes15_FinDeSemanaYDiasSinRepartoNinguno()
        {
            Assert.AreEqual(JUEVES.Date.AddHours(17), HorarioCierreReparto.De(JUEVES, C, null));
            Assert.AreEqual(JUEVES.Date.AddDays(1).AddHours(15), HorarioCierreReparto.De(JUEVES.AddDays(1), C, null));
            Assert.IsNull(HorarioCierreReparto.De(JUEVES.AddDays(2), C, null), "Sábado");
            Assert.IsNull(HorarioCierreReparto.De(JUEVES.AddDays(3), C, null), "Domingo");
            Assert.IsNull(HorarioCierreReparto.De(JUEVES, C, d => d == JUEVES.Date), "Día sin reparto propio");
        }

        // ---------------------------------------------------------------- cálculo

        [TestMethod]
        public void Calculo_ConduccionConColchon_Paradas_YDescargaEnAlgete()
        {
            ResultadoFinRuta r = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = JUEVES,
                MinutosTramos = new[] { 20d, 40d, 60d },
                KmTramos = new[] { 10d, 20d, 30d },
                MinutosParada = new[] { 15d, 25d },
                FactorConduccion = 1.15,
                PasaPorAlgete = true,
                MinutosDescargaAlgete = 15,
                HoraCierre = JUEVES.Date.AddHours(17)
            });

            Assert.AreEqual(138d, r.MinutosConduccion, 1e-9, "120 × 1,15");
            Assert.AreEqual(40d, r.MinutosParadas);
            Assert.AreEqual(15d, r.MinutosDescarga);
            Assert.AreEqual(60d, r.Km);
            Assert.AreEqual(JUEVES.AddMinutes(193), r.FinEstimado, "138 + 40 + 15");
            Assert.AreEqual(JUEVES.AddMinutes(178), r.LlegadaDestino, "Llega a Algete antes de descargar");
            CollectionAssert.AreEqual(new[] { JUEVES.AddMinutes(23), JUEVES.AddMinutes(84) }, r.Llegadas, "23; 23 + 15 + 46");
            Assert.IsTrue(r.SeComparaConCierre);
            Assert.IsFalse(r.PasaDelCierre);
            Assert.AreEqual(0, r.MinutosDeMas);
        }

        [TestMethod]
        public void Calculo_PasaDelCierre_ConLosMinutosDeMas()
        {
            ResultadoFinRuta r = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = JUEVES.Date.AddHours(16),
                MinutosTramos = new[] { 30d, 25d },
                MinutosParada = new[] { 30d },
                FactorConduccion = 1,
                PasaPorAlgete = true,
                MinutosDescargaAlgete = 15,
                HoraCierre = JUEVES.Date.AddHours(17)
            });

            Assert.AreEqual(JUEVES.Date.AddHours(17).AddMinutes(40), r.FinEstimado);
            Assert.IsTrue(r.PasaDelCierre);
            Assert.AreEqual(40, r.MinutosDeMas);
            Assert.AreEqual("Fin previsto 17:40 · 40 min después del cierre", TextosFinRuta.Aviso(r.FinEstimado, r.PasaDelCierre, r.MinutosDeMas));
        }

        [TestMethod]
        public void Calculo_AcabaEnCasa_SinPasarPorAlgete_NoSeCompara()
        {
            ResultadoFinRuta r = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = JUEVES.Date.AddHours(16),
                MinutosTramos = new[] { 30d, 60d },
                MinutosParada = new[] { 15d },
                PasaPorAlgete = false,
                MinutosDescargaAlgete = 15,
                HoraCierre = JUEVES.Date.AddHours(17)
            });

            Assert.AreEqual(JUEVES.Date.AddHours(17).AddMinutes(45), r.FinEstimado);
            Assert.AreEqual(r.FinEstimado, r.LlegadaDestino);
            Assert.AreEqual(0d, r.MinutosDescarga);
            Assert.IsFalse(r.SeComparaConCierre);
            Assert.IsFalse(r.PasaDelCierre);
            Assert.AreEqual("Fin previsto 17:45", TextosFinRuta.Aviso(r.FinEstimado, r.PasaDelCierre, r.MinutosDeMas));
        }

        [TestMethod]
        public void Calculo_AcabaEnCasa_PeroLlevaAlgoParaAlgete_PasaPorAlgete_YLuegoACasa()
        {
            ResultadoFinRuta r = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = JUEVES.Date.AddHours(15),
                MinutosTramos = new[] { 30d, 40d },
                KmTramos = new[] { 10d, 15d },
                MinutosParada = new[] { 15d },
                PasaPorAlgete = true,
                MinutosDescargaAlgete = 15,
                MinutosAlgeteADestino = 25,
                KmAlgeteADestino = 20,
                HoraCierre = JUEVES.Date.AddHours(17)
            });

            Assert.AreEqual(JUEVES.Date.AddHours(16).AddMinutes(40), r.FinEstimado, "Termina de descargar en Algete");
            Assert.AreEqual(JUEVES.Date.AddHours(17).AddMinutes(5), r.LlegadaDestino, "Y llega a casa 25 min después");
            Assert.AreEqual(45d, r.Km);
            Assert.IsTrue(r.SeComparaConCierre);
            Assert.IsFalse(r.PasaDelCierre, "Se compara la hora de Algete, no la de casa");
        }

        [TestMethod]
        public void Calculo_SinParadas_SoloLaVuelta()
        {
            ResultadoFinRuta r = CalculoFinRuta.Calcular(new EntradaFinRuta
            {
                Salida = JUEVES,
                MinutosTramos = new[] { 20d },
                MinutosParada = new double[0],
                PasaPorAlgete = true,
                MinutosDescargaAlgete = 15
            });

            Assert.AreEqual(JUEVES.AddMinutes(35), r.FinEstimado);
            Assert.AreEqual(0, r.Llegadas.Count);
            Assert.IsFalse(r.SeComparaConCierre, "Sin cierre ese día, no se compara");
        }

        [TestMethod]
        public void Textos_Duracion()
        {
            Assert.AreEqual("40 min", TextosFinRuta.Duracion(40));
            Assert.AreEqual("1 h", TextosFinRuta.Duracion(60));
            Assert.AreEqual("2 h 5 min", TextosFinRuta.Duracion(125));
            Assert.AreEqual("Fin previsto 9:05", TextosFinRuta.Aviso(JUEVES.AddMinutes(5), false, 0));
        }

        [TestMethod]
        public void Matriz_ConFactor_MultiplicaLosMinutos_NoLosKm()
        {
            var m = new MatrizTiempos(new double[,] { { 0, 10 }, { 20, 0 } }, new double[,] { { 0, 5 }, { 6, 0 } }, OrdenSugeridoRepartoDTO.MOTOR_OSRM);

            MatrizTiempos con = m.ConFactor(1.15);

            Assert.AreEqual(11.5d, con.Minutos[0, 1], 1e-9);
            Assert.AreEqual(23d, con.Minutos[1, 0], 1e-9);
            Assert.AreEqual(5d, con.Kilometros[0, 1]);
            Assert.AreSame(m, m.ConFactor(1));
            Assert.AreEqual(10d, m.Minutos[0, 1], "La original no cambia");
        }
    }
}
