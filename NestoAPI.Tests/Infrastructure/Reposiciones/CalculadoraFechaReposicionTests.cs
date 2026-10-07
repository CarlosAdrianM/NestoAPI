using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#577 (corte 1): cuándo llega la próxima reposición de una tienda a Algete y qué día sale el pedido.
    /// Semana de referencia: lunes 19/10/2026 … viernes 23/10/2026 (sin festivos salvo los que pone cada test).
    /// </summary>
    [TestClass]
    public class CalculadoraFechaReposicionTests
    {
        private static readonly TimeSpan CORTE_11 = new TimeSpan(11, 0, 0);
        private static readonly DateTime LUNES = new DateTime(2026, 10, 19);
        private static readonly DateTime MARTES = LUNES.AddDays(1);
        private static readonly DateTime MIERCOLES = LUNES.AddDays(2);
        private static readonly DateTime VIERNES = LUNES.AddDays(4);
        private static readonly DateTime LUNES_SIGUIENTE = LUNES.AddDays(7);

        private HashSet<string> festivos;
        private CalculadoraFechaReposicion calculadora;

        [TestInitialize]
        public void Preparar()
        {
            festivos = new HashSet<string>();
            calculadora = new CalculadoraFechaReposicion((dia, almacen) => festivos.Contains($"{almacen}|{dia:yyyyMMdd}"));
        }

        private void Festivo(string almacen, DateTime dia) => festivos.Add($"{almacen}|{dia:yyyyMMdd}");

        private static ReposicionCalendario Fila(string origen, byte dia, string cierre = "10:00", string llegada = "13:30",
            bool activo = true, string destino = "ALG")
        {
            return new ReposicionCalendario
            {
                Empresa = "1  ",
                AlmacenOrigen = origen + " ",
                AlmacenDestino = destino,
                DiaSemana = dia,
                HoraCierre = TimeSpan.Parse(cierre),
                HoraLlegadaHabitual = TimeSpan.Parse(llegada),
                Activo = activo
            };
        }

        /// <summary>El de hoy (decisión de Carlos 07/10/26): REI lunes, miércoles y viernes; ALC lunes, martes y jueves.</summary>
        private static List<ReposicionCalendario> CalendarioDeHoy() => new List<ReposicionCalendario>
        {
            Fila("REI", 1), Fila("REI", 3), Fila("REI", 5),
            Fila("ALC", 1), Fila("ALC", 2), Fila("ALC", 4)
        };

        private ProximaReposicionDTO Rei(DateTime ahora, List<ReposicionCalendario> calendario = null) =>
            calculadora.Calcular(calendario ?? CalendarioDeHoy(), "REI", "ALG", CORTE_11, ahora);

        [TestMethod]
        public void Calcular_Lunes0900_CierraHoyALas10LlegaALas1330YElPedidoSaleElMartes()
        {
            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(9));

            Assert.AreEqual("REI", proxima.Origen);
            Assert.AreEqual("ALG", proxima.Destino);
            Assert.AreEqual(LUNES.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(LUNES.AddHours(13).AddMinutes(30), proxima.LlegaEl);
            Assert.AreEqual(MARTES, proxima.PedidoSaleEl);
            Assert.AreEqual(1, proxima.DiasHastaSalida);
        }

        [TestMethod]
        public void Calcular_Lunes1030_YaHaCerradoLaDeHoy_LaProximaEsElMiercoles()
        {
            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(MIERCOLES.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(MIERCOLES.AddHours(13).AddMinutes(30), proxima.LlegaEl);
            Assert.AreEqual(MIERCOLES.AddDays(1), proxima.PedidoSaleEl);
            Assert.AreEqual(3, proxima.DiasHastaSalida);
        }

        [TestMethod]
        public void Calcular_JustoALaHoraDeCierre_YaNoEntra()
        {
            Assert.AreEqual(MIERCOLES.AddHours(10), Rei(LUNES.AddHours(10)).CierraEl);
        }

        [TestMethod]
        public void Calcular_Viernes1030_SaltaElFinDeSemana_LunesSiguienteYSaleElMartes()
        {
            ProximaReposicionDTO proxima = Rei(VIERNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(LUNES_SIGUIENTE.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(LUNES_SIGUIENTE.AddDays(1), proxima.PedidoSaleEl);
            Assert.AreEqual(4, proxima.DiasHastaSalida);
        }

        [TestMethod]
        public void Calcular_FestivoEnReinaElMiercoles_NoHayRecogida_SaltaAlViernesYSaleElLunes()
        {
            Festivo("REI", MIERCOLES);

            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(VIERNES.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(LUNES_SIGUIENTE, proxima.PedidoSaleEl, "Llega el viernes después del corte: el siguiente laborable es el lunes");
        }

        [TestMethod]
        public void Calcular_FestivoEnAlgeteElDiaDeLlegada_NoHayRecepcion_SaltaALaSiguiente()
        {
            Festivo("ALG", MIERCOLES);

            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual(VIERNES.AddHours(10), proxima.CierraEl);
        }

        [TestMethod]
        public void Calcular_FestivoEnAlgeteElDiaSiguienteALaLlegada_ElPedidoSaleElSiguienteLaborable()
        {
            Festivo("ALG", MARTES);

            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(9));

            Assert.AreEqual(LUNES.AddHours(10), proxima.CierraEl);
            Assert.AreEqual(MIERCOLES, proxima.PedidoSaleEl);
        }

        [TestMethod]
        public void Calcular_FestivoEnOtraTienda_NoAfecta()
        {
            Festivo("ALC", LUNES);

            Assert.AreEqual(LUNES.AddHours(10), Rei(LUNES.AddHours(9)).CierraEl);
        }

        [TestMethod]
        public void Calcular_LlegaAntesDelCorteDelPicking_ElPedidoSaleElMismoDia()
        {
            var calendario = new List<ReposicionCalendario> { Fila("REI", 1, cierre: "08:30", llegada: "10:00") };

            ProximaReposicionDTO proxima = Rei(LUNES.AddHours(8), calendario);

            Assert.AreEqual(LUNES.AddHours(10), proxima.LlegaEl);
            Assert.AreEqual(LUNES, proxima.PedidoSaleEl);
            Assert.AreEqual(0, proxima.DiasHastaSalida);
        }

        [TestMethod]
        public void Calcular_LlegaJustoALaHoraDelCorte_YaNoEntraEnElPickingDeEseDia()
        {
            var calendario = new List<ReposicionCalendario> { Fila("REI", 1, cierre: "08:30", llegada: "11:00") };

            Assert.AreEqual(MARTES, Rei(LUNES.AddHours(8), calendario).PedidoSaleEl);
        }

        [TestMethod]
        public void Calcular_DosReposicionesElMismoDia_CogeLaPrimeraQueAunNoHaCerrado()
        {
            var calendario = new List<ReposicionCalendario>
            {
                Fila("REI", 1, cierre: "16:00", llegada: "18:00"),
                Fila("REI", 1, cierre: "08:00", llegada: "10:00")
            };

            Assert.AreEqual(LUNES.AddHours(8), Rei(LUNES.AddHours(7), calendario).CierraEl);
            Assert.AreEqual(LUNES.AddHours(16), Rei(LUNES.AddHours(9), calendario).CierraEl);
        }

        [TestMethod]
        public void Calcular_RutaSinCalendario_Null()
        {
            Assert.IsNull(calculadora.Calcular(CalendarioDeHoy(), "REI", "ALC", CORTE_11, LUNES.AddHours(9)));
            Assert.IsNull(calculadora.Calcular(null, "REI", "ALG", CORTE_11, LUNES.AddHours(9)));
        }

        [TestMethod]
        public void Calcular_RutaConTodasLasFilasInactivas_Null()
        {
            var calendario = new List<ReposicionCalendario> { Fila("REI", 1, activo: false), Fila("REI", 3, activo: false) };

            Assert.IsNull(Rei(LUNES.AddHours(9), calendario));
        }

        [TestMethod]
        public void Calcular_OtraEmpresa_NoCuenta()
        {
            List<ReposicionCalendario> calendario = CalendarioDeHoy();

            Assert.IsNull(calculadora.Calcular(calendario, "REI", "ALG", CORTE_11, LUNES.AddHours(9), "3"));
        }

        [TestMethod]
        public void Calcular_Alcobendas_UsaSusDias()
        {
            ProximaReposicionDTO proxima = calculadora.Calcular(CalendarioDeHoy(), " alc ", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30));

            Assert.AreEqual("ALC", proxima.Origen);
            Assert.AreEqual(MARTES.AddHours(10), proxima.CierraEl);
        }

        [TestMethod]
        public void Calcular_ConLosFestivosDeVerdad_El12DeOctubreEsFiestaNacional()
        {
            var conGestorFestivos = new CalculadoraFechaReposicion();
            DateTime viernes9 = new DateTime(2026, 10, 9, 10, 30, 0);

            ProximaReposicionDTO proxima = conGestorFestivos.Calcular(CalendarioDeHoy(), "REI", "ALG", CORTE_11, viernes9);

            Assert.AreEqual(new DateTime(2026, 10, 14, 10, 0, 0), proxima.CierraEl, "El lunes 12/10 no hay reposición");
            Assert.AreEqual(new DateTime(2026, 10, 15), proxima.PedidoSaleEl);
        }

        [TestMethod]
        public void DiaSemana_LunesEs1YDomingoEs7()
        {
            Assert.AreEqual(1, CalculadoraFechaReposicion.DiaSemana(LUNES));
            Assert.AreEqual(5, CalculadoraFechaReposicion.DiaSemana(VIERNES));
            Assert.AreEqual(7, CalculadoraFechaReposicion.DiaSemana(LUNES.AddDays(6)));
        }

        [TestMethod]
        public void MasTardia_ConLineasDeVariasTiendas_MandaLaQueSaleMasTarde()
        {
            ProximaReposicionDTO rei = Rei(LUNES.AddHours(10).AddMinutes(30)); // sale el jueves
            ProximaReposicionDTO alc = calculadora.Calcular(CalendarioDeHoy(), "ALC", "ALG", CORTE_11, LUNES.AddHours(10).AddMinutes(30)); // sale el miércoles

            ProximaReposicionDTO masTardia = CalculadoraFechaReposicion.MasTardia(new[] { alc, null, rei });

            Assert.AreSame(rei, masTardia);
        }

        [TestMethod]
        public void MasTardia_AIgualSalida_MandaLaQueLlegaMasTarde()
        {
            var temprana = new ProximaReposicionDTO { Origen = "REI", PedidoSaleEl = MARTES, LlegaEl = LUNES.AddHours(12) };
            var tardia = new ProximaReposicionDTO { Origen = "ALC", PedidoSaleEl = MARTES, LlegaEl = LUNES.AddHours(14) };

            Assert.AreSame(tardia, CalculadoraFechaReposicion.MasTardia(new[] { temprana, tardia }));
        }

        [TestMethod]
        public void MasTardia_SinNinguna_Null()
        {
            Assert.IsNull(CalculadoraFechaReposicion.MasTardia(new ProximaReposicionDTO[] { null }));
            Assert.IsNull(CalculadoraFechaReposicion.MasTardia(null));
            Assert.IsNull(CalculadoraFechaReposicion.MasTardia(Enumerable.Empty<ProximaReposicionDTO>()));
        }
    }
}
