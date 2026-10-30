using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): almacén decide qué pedidos van por ruta interna (AT/16, RutasRepartoPropio); de esos, el
    /// código postal de entrega (tabla CodigosPostalesDiaReparto, 01 = lunes … 05 = viernes) dice qué día se sirven. Festivo de
    /// Algete → el siguiente laborable. Picking el mismo día del reparto. El día de su ruta, antes de las 9:00 sale ese día; a
    /// partir de las 9:00, su siguiente día de ruta. Lo pendiente espera al siguiente día de su ruta. Sin día en la tabla (la 16
    /// de Algete, CP fuera) o pedidos de agencia: como siempre.
    ///
    /// <para>Semana de referencia: lunes 02/11/2026 … viernes 06/11/2026 (sin festivos salvo que el test los ponga). CP de prueba:
    /// 28003 lunes, 28007 martes, 28001 miércoles, 28023 jueves, 28005 viernes; 28760 (Tres Cantos) sin día.</para>
    /// </summary>
    [TestClass]
    public class RutaInternaDiaRepartoTests
    {
        private static readonly TimeSpan CORTE = CalendarioRutasDia.HORA_CORTE;
        private static readonly TimeSpan CORTE_AGENCIA = new TimeSpan(11, 0, 0);
        private static readonly DateTime LUNES = new DateTime(2026, 11, 2);

        private const string CP_LUNES = "28003";
        private const string CP_MARTES = "28007";
        private const string CP_MIERCOLES = "28001";
        private const string CP_JUEVES = "28023";
        private const string CP_VIERNES = "28005";
        private const string CP_SIN_DIA = "28760";

        private static DateTime D(string valor) => DateTime.ParseExact(valor, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime Instante(string valor) => DateTime.ParseExact(valor, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>Sábados, domingos y los días que se digan.</summary>
        private static Func<DateTime, bool> Festivos(params string[] dias)
        {
            HashSet<DateTime> lista = new HashSet<DateTime>(dias.Select(D));
            return f => f.DayOfWeek == DayOfWeek.Saturday || f.DayOfWeek == DayOfWeek.Sunday || lista.Contains(f.Date);
        }

        private static Func<DateTime, string, bool> FestivosAlmacen(params string[] dias)
        {
            Func<DateTime, bool> f = Festivos(dias);
            return (dia, almacen) => f(dia);
        }

        [TestInitialize]
        public void Preparar()
        {
            RutasPropiasConfiguradas.Reiniciar(); // AT y 16
            DiasRepartoCodigosPostales.ConfigurarPara(new Dictionary<string, string>
            {
                [CP_LUNES] = "01",
                [CP_MARTES] = "02",
                [CP_MIERCOLES] = "03",
                [CP_JUEVES] = "04",
                [CP_VIERNES] = "05"
            });
        }

        [TestCleanup]
        public void Limpiar()
        {
            RutasPropiasConfiguradas.Reiniciar();
            DiasRepartoCodigosPostales.Reiniciar();
        }

        // ================================================================ CalendarioRutasDia: qué día es cada código

        [DataTestMethod]
        [DataRow("01", DayOfWeek.Monday)]
        [DataRow("02", DayOfWeek.Tuesday)]
        [DataRow("03", DayOfWeek.Wednesday)]
        [DataRow("04", DayOfWeek.Thursday)]
        [DataRow("05", DayOfWeek.Friday)]
        [DataRow("03 ", DayOfWeek.Wednesday)] // char(3) de la BD, con relleno
        public void DiaDeLaSemana_CadaCodigoSuDia(string codigo, DayOfWeek esperado)
        {
            Assert.AreEqual(esperado, CalendarioRutasDia.DiaDeLaSemana(codigo));
        }

        [DataTestMethod]
        [DataRow("16")]
        [DataRow("AT")]
        [DataRow("FW")]
        [DataRow("00")]
        [DataRow("06")]
        [DataRow("")]
        [DataRow(null)]
        public void DiaDeLaSemana_LoDemasNoEsUnDia(string codigo)
        {
            Assert.IsNull(CalendarioRutasDia.DiaDeLaSemana(codigo));
        }

        // ================================================================ cada día

        [DataTestMethod]
        [DataRow(DayOfWeek.Monday, "2026-11-09")] // es el propio lunes, después del corte → el siguiente
        [DataRow(DayOfWeek.Tuesday, "2026-11-03")]
        [DataRow(DayOfWeek.Wednesday, "2026-11-04")]
        [DataRow(DayOfWeek.Thursday, "2026-11-05")]
        [DataRow(DayOfWeek.Friday, "2026-11-06")]
        public void ProximoDiaReparto_PedidoDelLunesPorLaTarde_CadaDiaElSuyo(DayOfWeek dia, string esperado)
        {
            Assert.AreEqual(D(esperado), CalendarioRutasDia.ProximoDiaReparto(dia, LUNES, Instante("2026-11-02 16:00"), CORTE, Festivos()));
        }

        [DataTestMethod]
        [DataRow(DayOfWeek.Monday, "2026-11-02")]
        [DataRow(DayOfWeek.Tuesday, "2026-11-03")]
        [DataRow(DayOfWeek.Wednesday, "2026-11-04")]
        [DataRow(DayOfWeek.Thursday, "2026-11-05")]
        [DataRow(DayOfWeek.Friday, "2026-11-06")]
        public void ProximoDiaReparto_PedidoDelLunesPronto_CadaDiaElSuyo(DayOfWeek dia, string esperado)
        {
            Assert.AreEqual(D(esperado), CalendarioRutasDia.ProximoDiaReparto(dia, LUNES, Instante("2026-11-02 08:00"), CORTE, Festivos()));
        }

        [DataTestMethod]
        [DataRow("2026-11-07 10:00", "2026-11-11")] // sábado → miércoles
        [DataRow("2026-11-08 20:00", "2026-11-11")] // domingo
        [DataRow("2026-11-05 08:00", "2026-11-11")] // jueves por la mañana → el miércoles siguiente
        [DataRow("2026-11-03 23:59", "2026-11-04")] // martes por la noche → el miércoles
        public void ProximoDiaReparto_OtroDia_ElProximoDiaDeSuRuta(string ahora, string esperado)
        {
            DateTime instante = Instante(ahora);
            Assert.AreEqual(D(esperado), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, instante.Date, instante, CORTE, Festivos()));
        }

        // ================================================================ hora de corte (9:00) el día de su ruta

        [TestMethod]
        public void ProximoDiaReparto_ElDiaDeSuRutaAntesDelCorte_SaleEseDia()
        {
            Assert.AreEqual(D("2026-11-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, D("2026-11-04"), Instante("2026-11-04 08:59"), CORTE, Festivos()));
        }

        [TestMethod]
        public void ProximoDiaReparto_ElDiaDeSuRutaJustoALaHoraDeCorte_ElSiguienteDiaDeSuRuta()
        {
            // El corte es EN PUNTO: a las 09:00 ya no sale hoy
            Assert.AreEqual(D("2026-11-11"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, D("2026-11-04"), Instante("2026-11-04 09:00"), CORTE, Festivos()));
        }

        [TestMethod]
        public void ProximoDiaReparto_ElDiaDeSuRutaDespuesDelCorte_ElSiguienteDiaDeSuRuta()
        {
            Assert.AreEqual(D("2026-11-13"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, D("2026-11-06"), Instante("2026-11-06 12:30"), CORTE, Festivos()));
        }

        [TestMethod]
        public void HoraCorte_EsLaDeLas9YElCronDelPickingSaleDeElla()
        {
            Assert.AreEqual(new TimeSpan(9, 0, 0), CalendarioRutasDia.HORA_CORTE);
            Assert.AreEqual("0 9 * * 1-5", CalendarioRutasDia.CRON_PICKING);
        }

        [TestMethod]
        public void ProximoDiaReparto_RespetaUnaFechaPedidaPosterior()
        {
            Assert.AreEqual(D("2026-11-18"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, D("2026-11-12"), Instante("2026-11-02 08:00"), CORTE, Festivos()));
            Assert.AreEqual(D("2026-11-11"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, D("2026-11-11"), Instante("2026-11-02 08:00"), CORTE, Festivos()));
        }

        // ================================================================ festivos

        [TestMethod]
        public void ProximoDiaReparto_SuDiaEsFestivo_ElSiguienteLaborable()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-04"); // miércoles festivo
            Assert.AreEqual(D("2026-11-05"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, LUNES, Instante("2026-11-02 08:00"), CORTE, festivos));
            // El jueves reparte los dos
            Assert.AreEqual(D("2026-11-05"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Thursday, LUNES, Instante("2026-11-02 08:00"), CORTE, festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_LunesYMartesFestivos_LunesYMartesVanElMiercoles()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-02", "2026-11-03");
            DateTime viernesAntes = Instante("2026-10-30 16:00");
            Assert.AreEqual(D("2026-11-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Monday, viernesAntes.Date, viernesAntes, CORTE, festivos));
            Assert.AreEqual(D("2026-11-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Tuesday, viernesAntes.Date, viernesAntes, CORTE, festivos));
            Assert.AreEqual(D("2026-11-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, viernesAntes.Date, viernesAntes, CORTE, festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_DiaDesplazado_ElMiercolesAntesDelCorteTodaviaSaleElDelLunes()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-02", "2026-11-03");
            Assert.AreEqual(D("2026-11-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Monday, D("2026-11-04"), Instante("2026-11-04 08:00"), CORTE, festivos));
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Monday, D("2026-11-04"), Instante("2026-11-04 10:00"), CORTE, festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_ViernesFestivo_ElLunes()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-06");
            DateTime miercoles = Instante("2026-11-04 12:00");
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, miercoles.Date, miercoles, CORTE, festivos));
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Monday, miercoles.Date, miercoles, CORTE, festivos));
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, D("2026-11-09"), Instante("2026-11-09 08:00"), CORTE, festivos));
            Assert.AreEqual(D("2026-11-13"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, D("2026-11-09"), Instante("2026-11-09 10:00"), CORTE, festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_FestivosEncadenados_SaltaHastaElPrimerLaborable()
        {
            // Miércoles, jueves y viernes festivos: X, J y V se van al lunes siguiente
            Func<DateTime, bool> festivos = Festivos("2026-11-04", "2026-11-05", "2026-11-06");
            DateTime pronto = Instante("2026-11-02 08:00");
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, LUNES, pronto, CORTE, festivos));
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Thursday, LUNES, pronto, CORTE, festivos));
            Assert.AreEqual(D("2026-11-09"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, LUNES, pronto, CORTE, festivos));
            Assert.AreEqual(D("2026-11-03"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Tuesday, LUNES, pronto, CORTE, festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_FinDeAno()
        {
            // Viernes 01/01/2027 festivo, miércoles 06/01/2027 festivo (Reyes)
            Func<DateTime, bool> festivos = Festivos("2027-01-01", "2027-01-06");
            DateTime miercoles30 = Instante("2026-12-30 12:00");
            Assert.AreEqual(D("2026-12-31"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Thursday, miercoles30.Date, miercoles30, CORTE, festivos));
            Assert.AreEqual(D("2027-01-04"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Friday, miercoles30.Date, miercoles30, CORTE, festivos), "Viernes 1 festivo → lunes 4");
            Assert.AreEqual(D("2027-01-07"), CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, miercoles30.Date, miercoles30, CORTE, festivos), "Reyes en miércoles → jueves 7");
        }

        [TestMethod]
        public void EsDiaDeReparto_SoloElDiaEfectivo()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-06");
            Assert.IsTrue(CalendarioRutasDia.EsDiaDeReparto(DayOfWeek.Friday, D("2026-11-09"), festivos), "Viernes festivo: reparte el lunes");
            Assert.IsFalse(CalendarioRutasDia.EsDiaDeReparto(DayOfWeek.Friday, D("2026-11-06"), festivos));
            Assert.IsFalse(CalendarioRutasDia.EsDiaDeReparto(DayOfWeek.Friday, D("2026-11-10"), festivos));
            Assert.IsTrue(CalendarioRutasDia.EsDiaDeReparto(DayOfWeek.Friday, D("2026-11-13"), festivos));
        }

        [TestMethod]
        public void ProximoDiaReparto_CalendarioRotoTodoFestivo_NoSeCuelga()
        {
            Assert.IsTrue(CalendarioRutasDia.ProximoDiaReparto(DayOfWeek.Wednesday, LUNES, f => true) > LUNES);
        }

        [TestMethod]
        public void Textos()
        {
            Assert.AreEqual("hoy, miércoles 04/11", CalendarioRutasDia.Texto(D("2026-11-04"), D("2026-11-04")));
            Assert.AreEqual("mañana, miércoles 04/11", CalendarioRutasDia.Texto(D("2026-11-04"), D("2026-11-03")));
            Assert.AreEqual("el martes 03/11", CalendarioRutasDia.Texto(D("2026-11-03"), D("2026-10-30")));
            Assert.AreEqual("los miércoles", CalendarioRutasDia.NombrePlural(DayOfWeek.Wednesday));
            Assert.AreEqual("los lunes", CalendarioRutasDia.NombrePlural(DayOfWeek.Monday));
        }

        // ================================================================ la regla: ruta interna + CP con día

        [DataTestMethod]
        [DataRow("AT", CP_MIERCOLES, DayOfWeek.Wednesday)]
        [DataRow("16", CP_MIERCOLES, DayOfWeek.Wednesday)]
        [DataRow("16 ", " 28001 ", DayOfWeek.Wednesday)] // char con relleno
        [DataRow("at", CP_LUNES, DayOfWeek.Monday)]
        [DataRow("AT", CP_VIERNES, DayOfWeek.Friday)]
        public void DiaRutaInterna_RutaInternaYCodigoPostalConDia(string ruta, string cp, DayOfWeek esperado)
        {
            Assert.AreEqual(esperado, DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp));
        }

        [DataTestMethod]
        [DataRow("FW", CP_MIERCOLES)] // agencia: nada cambia
        [DataRow("OT", CP_MIERCOLES)]
        [DataRow("00", CP_MIERCOLES)]
        [DataRow("03", CP_MIERCOLES)] // las 01-05 ya no son rutas de pedido
        [DataRow("AT", CP_SIN_DIA)] // CP fuera de la tabla (la 16 de Algete): como hoy
        [DataRow("16", null)]
        [DataRow(null, CP_MIERCOLES)]
        public void DiaRutaInterna_SinDia(string ruta, string cp)
        {
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp));
        }

        [TestMethod]
        public void DiaRutaInterna_SiLa16DejaDeSerRutaPropia_YaNoTieneDia()
        {
            RutasPropiasConfiguradas.Configurar(() => "AT");
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna("16", CP_MIERCOLES));
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MIERCOLES));
        }

        [TestMethod]
        public void DiaRutaInterna_SinTablaONoConfigurado_NingunDia()
        {
            DiasRepartoCodigosPostales.Reiniciar();
            Assert.IsNull(DiasRepartoCodigosPostales.DiaRutaInterna("AT", CP_MIERCOLES));
            Assert.IsFalse(DiasRepartoCodigosPostales.PuedeTenerDia("AT"));
        }

        [TestMethod]
        public void PuedeTenerDia_SoloRutasInternasConTablaConDatos()
        {
            Assert.IsTrue(DiasRepartoCodigosPostales.PuedeTenerDia("AT"));
            Assert.IsTrue(DiasRepartoCodigosPostales.PuedeTenerDia("16"));
            Assert.IsFalse(DiasRepartoCodigosPostales.PuedeTenerDia("FW"));
        }

        [TestMethod]
        public void Interpretar_IgnoraFilasVaciasYDiasQueNoSonDe01a05()
        {
            IReadOnlyDictionary<string, DayOfWeek> dias = DiasRepartoCodigosPostales.Interpretar(new[]
            {
                new KeyValuePair<string, string>("28001 ", "03 "),
                new KeyValuePair<string, string>("28002", "06"),
                new KeyValuePair<string, string>("", "01"),
                new KeyValuePair<string, string>(null, "01"),
                new KeyValuePair<string, string>("28003", null)
            });
            Assert.AreEqual(1, dias.Count);
            Assert.AreEqual(DayOfWeek.Wednesday, dias["28001"]);
        }

        [TestMethod]
        public void Cache_LeeUnaVezYSiFallaSeQuedaConLoAnteriorYLoRegistra()
        {
            int lecturas = 0;
            bool fallar = false;
            DateTime ahora = new DateTime(2026, 11, 2, 8, 0, 0);
            var errores = new List<Exception>();
            DiasRepartoCodigosPostales.Configurar(() =>
            {
                lecturas++;
                if (fallar)
                {
                    throw new InvalidOperationException("sin BD");
                }
                return new[] { new KeyValuePair<string, string>("28001", "03") };
            }, errores.Add, () => ahora);

            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaDe("28001"));
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaDe("28001"));
            Assert.AreEqual(1, lecturas, "Dentro de la caché no vuelve a leer");

            fallar = true;
            ahora = ahora + DiasRepartoCodigosPostales.DURACION_CACHE;
            Assert.AreEqual(DayOfWeek.Wednesday, DiasRepartoCodigosPostales.DiaDe("28001"), "Si falla, sigue con lo último");
            Assert.AreEqual(2, lecturas);
            Assert.AreEqual(1, errores.Count);
        }

        // ================================================================ FechaEntregaAjustada (la fecha de las líneas)

        private static DateTime Ajustada(string ruta, string fecha, string ahora, string cp, string almacen = Constantes.Almacenes.ALGETE,
            params string[] festivos)
        {
            return GestorPedidosVenta.FechaEntregaAjustada(D(fecha), ruta, almacen, Instante(ahora), CORTE_AGENCIA, FestivosAlmacen(festivos),
                DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp));
        }

        [DataTestMethod]
        [DataRow(CP_LUNES, "2026-11-09")]
        [DataRow(CP_MARTES, "2026-11-03")]
        [DataRow(CP_MIERCOLES, "2026-11-04")]
        [DataRow(CP_JUEVES, "2026-11-05")]
        [DataRow(CP_VIERNES, "2026-11-06")]
        public void FechaEntregaAjustada_RutaInterna_CadaCodigoPostalSuDia(string cp, string esperado)
        {
            Assert.AreEqual(D(esperado), Ajustada("AT", "2026-11-02", "2026-11-02 10:00", cp));
        }

        [TestMethod]
        public void FechaEntregaAjustada_ElDiaDeSuRutaAntesDeLas9_Hoy()
        {
            Assert.AreEqual(D("2026-11-04"), Ajustada("16", "2026-11-04", "2026-11-04 08:30", CP_MIERCOLES));
        }

        [TestMethod]
        public void FechaEntregaAjustada_ElDiaDeSuRutaALas10_ElSiguienteAunqueLaAgenciaCierreALas11()
        {
            Assert.AreEqual(D("2026-11-11"), Ajustada("16", "2026-11-04", "2026-11-04 10:00", CP_MIERCOLES));
        }

        [TestMethod]
        public void FechaEntregaAjustada_ViernesFestivo_ElLunes()
        {
            Assert.AreEqual(D("2026-11-09"), Ajustada("AT", "2026-11-04", "2026-11-04 12:00", CP_VIERNES, Constantes.Almacenes.ALGETE, "2026-11-06"));
        }

        [TestMethod]
        public void FechaEntregaAjustada_FestivosEncadenados()
        {
            Assert.AreEqual(D("2026-11-09"), Ajustada("AT", "2026-11-02", "2026-11-02 08:00", CP_MIERCOLES, Constantes.Almacenes.ALGETE,
                "2026-11-04", "2026-11-05", "2026-11-06"));
        }

        [TestMethod]
        public void FechaEntregaAjustada_FechaDeRetencion_NoSeMueve()
        {
            DateTime retencion = new DateTime(2099, 12, 31);
            Assert.AreEqual(retencion, GestorPedidosVenta.FechaEntregaAjustada(retencion, "AT", "", Instante("2026-11-02 08:00"), CORTE_AGENCIA,
                FestivosAlmacen(), DayOfWeek.Wednesday));
        }

        [TestMethod]
        public void FechaEntregaAjustada_LineaDeOtroAlmacen_ComoHastaAhora()
        {
            Assert.AreEqual(LUNES, Ajustada("AT", "2026-11-02", "2026-11-02 12:00", CP_MIERCOLES, Constantes.Almacenes.REINA));
        }

        [DataTestMethod]
        [DataRow("FW", CP_MIERCOLES, "2026-11-02 08:00", "2026-11-02")] // agencia: sin cambios aunque el CP tenga día
        [DataRow("FW", CP_MIERCOLES, "2026-11-02 12:00", "2026-11-03")]
        [DataRow("16", CP_SIN_DIA, "2026-11-06 12:00", "2026-11-09")] // la 16 de Algete sin día: viernes por la tarde → lunes
        [DataRow("AT", CP_SIN_DIA, "2026-11-03 08:00", "2026-11-03")]
        [DataRow("00", CP_MIERCOLES, "2026-11-03 12:00", "2026-11-04")]
        public void FechaEntregaAjustada_SinDia_SinCambios(string ruta, string cp, string ahora, string esperado)
        {
            DateTime instante = Instante(ahora);
            DateTime sinRegla = GestorPedidosVenta.FechaEntregaAjustada(instante.Date, ruta, Constantes.Almacenes.ALGETE, instante, CORTE_AGENCIA, FestivosAlmacen());
            Assert.AreEqual(D(esperado), sinRegla);
            Assert.AreEqual(sinRegla, Ajustada(ruta, ahora.Substring(0, 10), ahora, cp));
        }

        [TestMethod]
        public void CodigoPostalSiRutaInterna_SoloLoLeeSiLaRutaEsInternaYUnaVezPorContacto()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.LeerCodigoPostalEntrega("1", "15191", "0")).Returns(CP_MIERCOLES);
            var gestor = new GestorPedidosVenta(servicio);

            Assert.IsNull(gestor.CodigoPostalSiRutaInterna("1", "15191", "0", "FW"));
            A.CallTo(() => servicio.LeerCodigoPostalEntrega(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();

            Assert.AreEqual(CP_MIERCOLES, gestor.CodigoPostalSiRutaInterna("1", "15191", "0", "AT"));
            Assert.AreEqual(CP_MIERCOLES, gestor.CodigoPostalSiRutaInterna("1", "15191", "0", "16"));
            A.CallTo(() => servicio.LeerCodigoPostalEntrega("1", "15191", "0")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void CodigoPostalSiRutaInterna_SinTabla_NoLeeNada()
        {
            DiasRepartoCodigosPostales.Reiniciar();
            var servicio = A.Fake<IServicioPedidosVenta>();
            var gestor = new GestorPedidosVenta(servicio);
            Assert.IsNull(gestor.CodigoPostalSiRutaInterna("1", "15191", "0", "AT"));
            A.CallTo(() => servicio.LeerCodigoPostalEntrega(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ================================================================ picking: el mismo día y solo el que le toca

        private static PedidoPicking Pedido(int id, string ruta, string cp, string diasEnServir, params DateTime[] fechas)
        {
            return new PedidoPicking
            {
                Id = id,
                Cliente = "15191",
                Ruta = ruta,
                CodigoPostal = cp,
                DiasEnServir = diasEnServir,
                Lineas = fechas.Select((f, i) => new LineaPedidoPicking
                {
                    Id = i + 1,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Cantidad = 1,
                    CantidadReservada = 1,
                    FechaEntrega = f
                }).ToList()
            };
        }

        [TestMethod]
        public void Picking_ElDiaDeSuRuta_SaleLoDeHoyYLoAtrasadoPeroNoLoDeLaSemanaQueViene()
        {
            DateTime miercoles = D("2026-11-04");
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11111", miercoles, D("2026-10-28"), D("2026-11-11"));

            List<PedidoPicking> noLesToca = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pedido }, miercoles, Festivos(), true);

            Assert.AreEqual(0, noLesToca.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, pedido.Lineas.Select(l => l.Id).ToList());
            Assert.AreEqual(D("2026-11-11"), pedido.PrimeraEntregaFuturaQuitada);
        }

        [TestMethod]
        public void Picking_OtroDia_NoSaleYApuntaSuProximoDia()
        {
            DateTime jueves = D("2026-11-05");
            PedidoPicking pedido = Pedido(1, "16", CP_MIERCOLES, "11111", D("2026-11-04"));

            List<PedidoPicking> noLesToca = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pedido }, jueves, Festivos(), true);

            Assert.AreEqual(1, noLesToca.Count);
            Assert.AreEqual(0, pedido.Lineas.Count);
            Assert.AreEqual(D("2026-11-11"), pedido.PrimeraEntregaFuturaQuitada);
        }

        [TestMethod]
        public void Picking_PendienteSinStock_EsperaAlSiguienteDiaDeSuRuta()
        {
            // Lo que no salió el miércoles 04 (sin stock) conserva su fecha. Jueves, viernes, lunes y martes no sale; el 11 sí.
            foreach (string dia in new[] { "2026-11-05", "2026-11-06", "2026-11-09", "2026-11-10" })
            {
                PedidoPicking pendiente = Pedido(1, "AT", CP_MIERCOLES, "11111", D("2026-11-04"));
                _ = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pendiente }, D(dia), Festivos(), true);
                Assert.AreEqual(0, pendiente.Lineas.Count, dia);
            }
            PedidoPicking elDia = Pedido(1, "AT", CP_MIERCOLES, "11111", D("2026-11-04"));
            _ = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { elDia }, D("2026-11-11"), Festivos(), true);
            Assert.AreEqual(1, elDia.Lineas.Count);
        }

        [TestMethod]
        public void Picking_SuDiaEsFestivo_SaleElSiguienteLaborable()
        {
            Func<DateTime, bool> festivos = Festivos("2026-11-04");
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11111", D("2026-11-02"));
            _ = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pedido }, D("2026-11-05"), festivos, true);
            Assert.AreEqual(1, pedido.Lineas.Count, "Miércoles festivo: la ruta del miércoles sale el jueves");
        }

        [TestMethod]
        public void Picking_PorLaTardeConHorizonteManana_NoAdelantaLoDeManana()
        {
            // Martes por la tarde, horizonte miércoles: la ruta del miércoles se prepara el miércoles; la agencia, como siempre
            DateTime martes = D("2026-11-03");
            DateTime miercoles = D("2026-11-04");
            PedidoPicking rutaInterna = Pedido(1, "AT", CP_MIERCOLES, "11111", miercoles);
            PedidoPicking agencia = Pedido(2, "FW", CP_MIERCOLES, "11111", miercoles);
            PedidoPicking dieciseisSinDia = Pedido(3, "16", CP_SIN_DIA, "11111", miercoles);

            _ = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { rutaInterna, agencia, dieciseisSinDia }, martes, Festivos(), true);

            Assert.AreEqual(0, rutaInterna.Lineas.Count);
            Assert.AreEqual(1, agencia.Lineas.Count, "Agencia: sin cambios");
            Assert.AreEqual(1, dieciseisSinDia.Lineas.Count, "La 16 sin día: sin cambios (#588)");
        }

        [TestMethod]
        public void Picking_DeUnSoloPedido_NoMiraElDiaPeroTampocoAdelantaFechas()
        {
            // Almacén saca a mano el picking de un pedido de la ruta del miércoles un jueves: sale lo atrasado, no lo futuro
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11111", D("2026-11-04"), D("2026-11-11"));
            List<PedidoPicking> noLesToca = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pedido }, D("2026-11-05"), Festivos(), false);
            Assert.AreEqual(0, noLesToca.Count);
            CollectionAssert.AreEqual(new[] { 1 }, pedido.Lineas.Select(l => l.Id).ToList());
        }

        [TestMethod]
        public void Picking_SinTabla_NadaCambia()
        {
            DiasRepartoCodigosPostales.Reiniciar();
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11111", D("2026-11-04"), D("2026-11-06"));
            _ = GestorRutaInternaPicking.Aplicar(new List<PedidoPicking> { pedido }, D("2026-11-05"), Festivos(), true);
            Assert.AreEqual(2, pedido.Lineas.Count);
        }

        [TestMethod]
        public void DiasEnServir_ConDia_SeMiraElMismoDiaYNoElSiguiente()
        {
            // Miércoles: la ruta del miércoles se entrega hoy. Cierra los jueves ("11101"): por agencia (jueves) no sale; por la ruta sí.
            DateTime miercoles = D("2026-11-04");
            DateTime jueves = D("2026-11-05");
            PedidoPicking rutaInterna = Pedido(1, "AT", CP_MIERCOLES, "11101", miercoles);
            PedidoPicking agencia = Pedido(2, "FW", CP_MIERCOLES, "11101", miercoles);
            PedidoPicking dieciseisSinDia = Pedido(3, "16", CP_SIN_DIA, "11101", miercoles);

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { rutaInterna, agencia, dieciseisSinDia }, jueves, jueves, miercoles);

            CollectionAssert.AreEquivalent(new[] { 2, 3 }, retirados.Select(p => p.Id).ToList(), "La 16 sin día se entrega mañana (#588)");
            Assert.AreEqual(1, rutaInterna.Lineas.Count);
        }

        [TestMethod]
        public void DiasEnServir_ClienteCierraSuDiaDeReparto_SeRetiraConEseDiaYSinPista()
        {
            DateTime miercoles = D("2026-11-04");
            DateTime jueves = D("2026-11-05");
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11011", miercoles); // cierra los miércoles

            List<PedidoPicking> retirados = GestorDiasEnServir.RetirarPedidosDeClientesCerrados(
                new List<PedidoPicking> { pedido }, jueves, jueves, miercoles);

            Assert.AreEqual(1, retirados.Count);
            Assert.AreEqual(miercoles, pedido.DiaEntregaRetiradoPorCierre);
            Assert.IsNull(pedido.DiaEntregaSiVaPorNuestraRuta);
        }

        [TestMethod]
        public void DiasEnServir_LlamadaAntigua_ComoHastaAhora()
        {
            DateTime miercoles = D("2026-11-04");
            DateTime jueves = D("2026-11-05");
            PedidoPicking pedido = Pedido(1, "AT", CP_MIERCOLES, "11101", miercoles);
            Assert.AreEqual(1, GestorDiasEnServir.RetirarPedidosDeClientesCerrados(new List<PedidoPicking> { pedido }, jueves, jueves).Count);
        }

        // ================================================================ calculadora de la fecha de entrega

        private static ResultadoFechaEntregaAgencia Calcular(DayOfWeek? dia, string ahora, string diasEnServir = "11111",
            DateTime? llegaProveedor = null, params string[] festivos)
        {
            var linea = new LineaFechaEntregaAgencia { Producto = "A", Cantidad = 1, BaseImponible = 10, EnAlgete = llegaProveedor.HasValue ? 0 : 1 };
            if (llegaProveedor.HasValue)
            {
                linea.DelProveedor = 1;
                linea.FechaProveedor = llegaProveedor;
            }
            var entrada = new EntradaFechaEntregaAgencia
            {
                Ruta = dia.HasValue ? "AT" : "FW",
                DiaRutaInterna = dia,
                DiasEnServir = diasEnServir,
                Ahora = Instante(ahora),
                HoraCorte = CORTE_AGENCIA,
                Lineas = new List<LineaFechaEntregaAgencia> { linea }
            };
            return new CalculadoraFechaEntregaAgencia(FestivosAlmacen(festivos)).Calcular(entrada);
        }

        [TestMethod]
        public void Calculadora_PedidoDelLunes_SaleElMiercolesPorNuestraRuta()
        {
            ResultadoFechaEntregaAgencia r = Calcular(DayOfWeek.Wednesday, "2026-11-02 10:00");
            Assert.AreEqual(D("2026-11-04"), r.PrimeraEntrega);
            StringAssert.Contains(r.Motivo, "Sale entero por nuestra ruta el");
            StringAssert.Contains(r.Motivo, "se reparte los miércoles");
            StringAssert.Contains(r.Motivo, "El próximo reparto de nuestra ruta es el");
        }

        [TestMethod]
        public void Calculadora_ElMiercolesALas10_LaSemanaSiguiente()
        {
            ResultadoFechaEntregaAgencia r = Calcular(DayOfWeek.Wednesday, "2026-11-04 10:00");
            Assert.AreEqual(D("2026-11-11"), r.PrimeraEntrega);
            StringAssert.Contains(r.Motivo, "ya no sale hoy");
        }

        [TestMethod]
        public void Calculadora_ElMiercolesAntesDeLas9_Hoy()
        {
            Assert.AreEqual(D("2026-11-04"), Calcular(DayOfWeek.Wednesday, "2026-11-04 08:30").PrimeraEntrega);
        }

        [TestMethod]
        public void Calculadora_ViernesFestivo_ElLunes()
        {
            Assert.AreEqual(D("2026-11-09"), Calcular(DayOfWeek.Friday, "2026-11-04 10:00", "11111", null, "2026-11-06").PrimeraEntrega);
        }

        [TestMethod]
        public void Calculadora_ClienteCierraLosJueves_SaleElMiercoles()
        {
            // Por agencia el miércoles se entregaría el jueves (cerrado); por nuestra ruta, el mismo miércoles
            Assert.AreEqual(D("2026-11-04"), Calcular(DayOfWeek.Wednesday, "2026-11-02 10:00", "11101").PrimeraEntrega);
        }

        [TestMethod]
        public void Calculadora_LoQueLlegaDelProveedorElJueves_EsperaAlMiercolesSiguiente()
        {
            ResultadoFechaEntregaAgencia r = Calcular(DayOfWeek.Wednesday, "2026-11-02 10:00", "11111", D("2026-11-05"));
            Assert.AreEqual(D("2026-11-11"), r.PrimeraEntrega);
        }

        [TestMethod]
        public void Calculadora_Agencia_SinCambios()
        {
            ResultadoFechaEntregaAgencia r = Calcular(null, "2026-11-02 10:00");
            Assert.AreEqual(D("2026-11-02"), r.PrimeraEntrega);
            StringAssert.Contains(r.Motivo, "a la agencia");
            Assert.IsFalse(r.Motivo.Contains("se reparte los"));
        }

        // ================================================================ sugerencia 575: el día en que saldría

        private static GestorSugerenciasRuta GestorSugerencias(string hoy, params DatosPedidoSugerenciaRuta[] candidatos)
        {
            var repositorio = A.Fake<IRepositorioSugerenciasRuta>();
            A.CallTo(() => repositorio.LeerCandidatos(A<string>._, A<int?>._, A<string>._, A<DateTime>._))
                .Returns(Task.FromResult(candidatos.ToList()));
            return new GestorSugerenciasRuta(repositorio) { Hoy = () => D(hoy), EsFestivo = Festivos() };
        }

        private static DatosPedidoSugerenciaRuta Candidato(string cp, string diasEnServir = "11111")
        {
            // Empresa 3 con código postal de Madrid: se sugiere nuestra ruta
            return new DatosPedidoSugerenciaRuta
            {
                Empresa = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO,
                Pedido = 929999,
                Cliente = "15191",
                Contacto = "0",
                Ruta = "FW",
                CodigoPostal = cp,
                DiasEnServir = diasEnServir
            };
        }

        [TestMethod]
        public async Task Sugerencia_DiceElDiaEnQueSaldria()
        {
            List<SugerenciaRutaPedidoDTO> s = await GestorSugerencias("2026-10-30", Candidato(CP_MARTES)).LeerSugerencias(null, null, null, D("2026-10-30"));
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(D("2026-11-03"), s[0].DiaRepartoRutaInterna);
            Assert.AreEqual("Saldría el martes 03/11 por nuestra ruta", s[0].TextoDiaReparto);
            Assert.AreEqual(s[0].TextoDiaReparto, s[0].Motivos.Last(), "Nesto enseña los motivos: ahí lo ve sin cambios");
        }

        [TestMethod]
        public async Task Sugerencia_HoyEsSuDia_SaldriaHoy()
        {
            List<SugerenciaRutaPedidoDTO> s = await GestorSugerencias("2026-11-04", Candidato(CP_MIERCOLES)).LeerSugerencias(null, null, null, D("2026-11-04"));
            Assert.AreEqual("Saldría hoy, miércoles 04/11, por nuestra ruta", s[0].TextoDiaReparto);
        }

        [TestMethod]
        public async Task Sugerencia_ElClienteCierraEseDia_LoDice()
        {
            List<SugerenciaRutaPedidoDTO> s = await GestorSugerencias("2026-10-30", Candidato(CP_MARTES, "10111")).LeerSugerencias(null, null, null, D("2026-10-30"));
            Assert.AreEqual("Saldría el martes 03/11 por nuestra ruta, pero el cliente cierra los martes: mejor por agencia", s[0].TextoDiaReparto);
        }

        [TestMethod]
        public async Task Sugerencia_CodigoPostalSinDia_ComoAntes()
        {
            List<SugerenciaRutaPedidoDTO> s = await GestorSugerencias("2026-10-30", Candidato("28100")).LeerSugerencias(null, null, null, D("2026-10-30"));
            Assert.AreEqual(1, s.Count);
            Assert.IsNull(s[0].DiaRepartoRutaInterna);
            Assert.IsNull(s[0].TextoDiaReparto);
            Assert.IsFalse(s[0].Motivos.Any(m => m.Contains("Saldría")));
        }

        // ================================================================ picking automático de las 9:00

        private sealed class JobDePrueba
        {
            public string Interruptor = "1";
            public bool LeerFalla;
            public Func<DateTime, bool> Festivos = RutaInternaDiaRepartoTests.Festivos();
            public DateTime Ahora = new DateTime(2026, 11, 4, 9, 0, 5);
            public Exception Lanza;
            public List<(List<Ruta> rutas, DateTime fecha)> Llamadas = new List<(List<Ruta>, DateTime)>();
            public List<Exception> Avisos = new List<Exception>();

            public PickingRutasInternasJobsService Crear()
            {
                var lector = A.Fake<ILectorParametrosUsuario>();
                if (LeerFalla)
                {
                    A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("sin BD"));
                }
                else
                {
                    A.CallTo(() => lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                        Constantes.ParametrosUsuario.PICKING_AUTOMATICO_RUTAS_INTERNAS)).Returns(Interruptor);
                }
                return new PickingRutasInternasJobsService(lector, Festivos, () => Ahora,
                    () => new List<Ruta> { new Ruta { Número = "AT " }, new Ruta { Número = "16 " }, new Ruta { Número = "FW " }, new Ruta { Número = "00 " } },
                    (rutas, fecha) =>
                    {
                        Llamadas.Add((rutas, fecha));
                        if (Lanza != null)
                        {
                            throw Lanza;
                        }
                        return new List<PedidoPicking>();
                    },
                    (ex, momento) => Avisos.Add(ex));
            }
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("0")]
        [DataRow("Activo")]
        public void Job_Apagado_NoHaceNada(string valor)
        {
            var job = new JobDePrueba { Interruptor = valor };
            Assert.AreEqual(ResultadoPickingRutasInternas.Apagado, job.Crear().Ejecutar());
            Assert.AreEqual(0, job.Llamadas.Count);
        }

        [TestMethod]
        public void Job_SiNoSePuedeLeerElInterruptor_Apagado()
        {
            var job = new JobDePrueba { LeerFalla = true };
            Assert.AreEqual(ResultadoPickingRutasInternas.Apagado, job.Crear().Ejecutar());
            Assert.AreEqual(0, job.Llamadas.Count);
        }

        [TestMethod]
        public void Job_EncendidoEnFestivoDeAlgete_NoHaceNada()
        {
            var job = new JobDePrueba { Festivos = Festivos("2026-11-04") };
            Assert.AreEqual(ResultadoPickingRutasInternas.NoLaborable, job.Crear().Ejecutar());
            Assert.AreEqual(0, job.Llamadas.Count);
        }

        [TestMethod]
        public void Job_Encendido_SacaElPickingDeLasRutasInternasConHorizonteHoy()
        {
            var job = new JobDePrueba { Interruptor = " 1 " };
            Assert.AreEqual(ResultadoPickingRutasInternas.Lanzado, job.Crear().Ejecutar());
            Assert.AreEqual(1, job.Llamadas.Count);
            CollectionAssert.AreEquivalent(new[] { "AT", "16" }, job.Llamadas[0].rutas.Select(r => r.Número.Trim()).ToList(), "Las mismas que «Solo nuestra ruta»");
            Assert.AreEqual(D("2026-11-04"), job.Llamadas[0].fecha);
            Assert.AreEqual(0, job.Avisos.Count);
        }

        [TestMethod]
        public void Job_SinNadaQueSacar_AvisaAlAlmacenYNoQuedaEnRojo()
        {
            var job = new JobDePrueba
            {
                Lanza = new NestoBusinessException("No hay stock", new ErrorContext { ErrorCode = Constantes.Picking.ERROR_SIN_STOCK })
            };
            Assert.AreEqual(ResultadoPickingRutasInternas.Lanzado, job.Crear().Ejecutar());
            Assert.AreEqual(1, job.Avisos.Count);
        }

        [TestMethod]
        public void Job_FalloDeVerdad_AvisaYRelanza()
        {
            var job = new JobDePrueba { Lanza = new InvalidOperationException("se cayó la BD") };
            _ = Assert.ThrowsException<InvalidOperationException>(() => job.Crear().Ejecutar());
            Assert.AreEqual(1, job.Avisos.Count);
        }

        // ================================================================ aviso al grabar: el cliente cierra su día de reparto

        [TestMethod]
        public void Aviso_ClienteCierraSuDiaDeReparto_MensajeDeUsted()
        {
            Assert.AreEqual("El cliente cierra el miércoles, que es su día de reparto por nuestra ruta: envíelo por agencia o cambie el día.",
                AvisoClienteCierraDiaReparto.Mensaje("AT", CP_MIERCOLES, "11011"));
            Assert.AreEqual("El cliente cierra el lunes, que es su día de reparto por nuestra ruta: envíelo por agencia o cambie el día.",
                AvisoClienteCierraDiaReparto.Mensaje("16", CP_LUNES, "01111"));
        }

        [DataTestMethod]
        [DataRow("AT", CP_MIERCOLES, "11111")] // abre ese día
        [DataRow("AT", CP_MIERCOLES, "11101")] // cierra otro día
        [DataRow("FW", CP_MIERCOLES, "11011")] // agencia
        [DataRow("AT", CP_SIN_DIA, "11011")] // sin día
        [DataRow("AT", CP_MIERCOLES, "00000")] // dato roto = abierto
        [DataRow("AT", CP_MIERCOLES, null)]
        public void Aviso_NadaQueAvisar(string ruta, string cp, string dias)
        {
            Assert.IsNull(AvisoClienteCierraDiaReparto.Mensaje(ruta, cp, dias));
            Assert.IsNull(AvisoClienteCierraDiaReparto.Crear(ruta, cp, dias));
        }

        [TestMethod]
        public void Aviso_Crear_TipoYMensaje()
        {
            AvisoPedidoDTO aviso = AvisoClienteCierraDiaReparto.Crear("AT", CP_VIERNES, "11110");
            Assert.AreEqual(AvisoClienteCierraDiaReparto.TIPO, aviso.Tipo);
            StringAssert.Contains(aviso.Mensaje, "el viernes");
        }

        [TestMethod]
        public void Aviso_EnElControlador_LeeLosDiasDelContactoYNuncaLanza()
        {
            var controller = new PedidosVentaController(A.Fake<NVEntities>())
            {
                LeerDiasEnServirContacto = (empresa, cliente, contacto) => cliente == "15191" && contacto == "0" ? "11011" : "11111"
            };
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 1, cliente = "15191", contacto = "0", ruta = "AT" };

            Assert.IsNotNull(controller.AvisoCierreDiaReparto(pedido, CP_MIERCOLES));
            Assert.IsNull(controller.AvisoCierreDiaReparto(pedido, CP_SIN_DIA));
            pedido.ruta = "FW";
            Assert.IsNull(controller.AvisoCierreDiaReparto(pedido, CP_MIERCOLES));

            pedido.ruta = "AT";
            controller.LeerDiasEnServirContacto = (e, c, k) => throw new InvalidOperationException("sin BD");
            Assert.IsNull(controller.AvisoCierreDiaReparto(pedido, CP_MIERCOLES));
        }
    }
}
