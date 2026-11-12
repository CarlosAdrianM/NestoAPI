using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Tests.Models.RecursosHumanos
{
    /// <summary>
    /// NestoAPI#779: los festivos salen de la tabla Festivos (caché de 10 min, <see cref="CalendarioFestivos.Invalidar"/>) y,
    /// si no hay tabla o le falta el año, de la lista del código de <see cref="GestorFestivos"/>.
    /// </summary>
    [TestClass]
    public class CalendarioFestivosTests
    {
        private static readonly DateTime LUNES_02_11_2026 = new DateTime(2026, 11, 2);
        private static readonly DateTime MARTES_03_11_2026 = new DateTime(2026, 11, 3);

        private List<Exception> errores;
        private int lecturas;
        private DateTime ahora;

        [TestInitialize]
        public void Preparar()
        {
            errores = new List<Exception>();
            lecturas = 0;
            ahora = new DateTime(2026, 11, 4, 10, 0, 0);
        }

        [TestCleanup]
        public void Restaurar() => CalendarioFestivos.Reiniciar();

        private void ConTabla(params Festivo[] festivos)
        {
            CalendarioFestivos.Configurar(() =>
            {
                lecturas++;
                return new CalendarioFestivos.LecturaFestivos { HayTabla = true, Festivos = festivos.ToList() };
            }, errores.Add, () => ahora);
        }

        private static Festivo F(DateTime fecha, TipoFestivo tipo, string descripcion = "Festivo") =>
            new Festivo { Fecha = fecha, TipoFestivo = tipo, Fiesta = descripcion };

        [TestMethod]
        public void AnioDisparatado_SinFestivosNiElmahNiLectura()
        {
            ConTabla(F(LUNES_02_11_2026, TipoFestivo.Algete));

            Assert.AreEqual(0, CalendarioFestivos.DelAnio(2099).Count);
            Assert.AreEqual(0, CalendarioFestivos.DelAnio(2100).Count);
            Assert.AreEqual(0, CalendarioFestivos.DelAnio(1999).Count);
            Assert.IsFalse(GestorFestivos.EsFestivo(new DateTime(2099, 12, 31), Constantes.Almacenes.ALGETE));

            Assert.AreEqual(0, errores.Count, "Las fechas centinela no avisan");
            Assert.AreEqual(0, lecturas, "Ni siquiera se lee la tabla");
        }

        [TestMethod]
        public void AnioQueVieneSinCargar_SigueAvisando()
        {
            ConTabla(F(LUNES_02_11_2026, TipoFestivo.Algete));

            _ = CalendarioFestivos.DelAnio(2027);

            Assert.AreEqual(1, errores.Count, "El aviso legítimo no cambia");
            Assert.IsTrue(CalendarioFestivos.EsAnioDisparatado(2032, 2026));
            Assert.IsFalse(CalendarioFestivos.EsAnioDisparatado(2031, 2026));
        }

        [TestMethod]
        public void SinLector_UsaLaListaDelCodigo()
        {
            CalendarioFestivos.Reiniciar();

            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11_2026, Constantes.Almacenes.ALGETE));
            Assert.IsFalse(GestorFestivos.EsFestivo(MARTES_03_11_2026, Constantes.Almacenes.ALGETE));
        }

        [TestMethod]
        public void SinTabla_UsaLaListaDelCodigo_SinElmah()
        {
            CalendarioFestivos.Configurar(() => new CalendarioFestivos.LecturaFestivos { HayTabla = false }, errores.Add, () => ahora);

            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11_2026, Constantes.Almacenes.ALGETE));
            Assert.IsTrue(GestorFestivos.EsFestivo(new DateTime(2026, 12, 24), Constantes.Almacenes.ALGETE));
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ConTabla_MandaLaTabla()
        {
            ConTabla(F(MARTES_03_11_2026, TipoFestivo.Nacional), F(new DateTime(2026, 5, 15), TipoFestivo.Madrid));

            Assert.IsTrue(GestorFestivos.EsFestivo(MARTES_03_11_2026, Constantes.Almacenes.ALGETE));
            Assert.IsFalse(GestorFestivos.EsFestivo(LUNES_02_11_2026, Constantes.Almacenes.ALGETE), "El 02/11 ya no está en la tabla");
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ConTabla_LosTiposPorDelegacionComoHastaAhora()
        {
            var viernes = new DateTime(2027, 5, 14);
            var lunes = new DateTime(2027, 5, 17);
            var martes = new DateTime(2027, 5, 18);
            var miercoles = new DateTime(2027, 5, 19);
            ConTabla(F(viernes, TipoFestivo.Madrid), F(lunes, TipoFestivo.Alcobendas), F(martes, TipoFestivo.Empresa),
                F(miercoles, TipoFestivo.Algete));

            Assert.IsTrue(GestorFestivos.EsFestivo(viernes, Constantes.Almacenes.REINA));
            Assert.IsFalse(GestorFestivos.EsFestivo(viernes, Constantes.Almacenes.ALGETE));
            Assert.IsTrue(GestorFestivos.EsFestivo(lunes, Constantes.Almacenes.ALCOBENDAS));
            Assert.IsFalse(GestorFestivos.EsFestivo(lunes, Constantes.Almacenes.REINA));
            Assert.IsTrue(GestorFestivos.EsFestivo(martes, Constantes.Almacenes.ALGETE));
            Assert.IsFalse(GestorFestivos.EsFestivo(martes, ""), "Empresa no cuenta sin delegación");
            Assert.IsFalse(GestorFestivos.EsFestivo(miercoles, Constantes.Almacenes.ALGETE), "El tipo Algete no se aplica a ALG (como hoy)");
        }

        [TestMethod]
        public void ConTablaSinElAnio_UsaElCodigoDeEseAnio_YAvisaUnaSolaVez()
        {
            ConTabla(F(new DateTime(2027, 1, 1), TipoFestivo.Nacional));

            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11_2026, Constantes.Almacenes.ALGETE));
            Assert.IsTrue(GestorFestivos.EsFestivo(new DateTime(2026, 12, 8), Constantes.Almacenes.ALGETE));

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "2026");
            StringAssert.Contains(errores[0].Message, "lista del código");
        }

        [TestMethod]
        public void ConTablaSinElAnioNiCodigo_TodoLaborable_YAvisa()
        {
            ConTabla(F(new DateTime(2026, 1, 1), TipoFestivo.Nacional));

            Assert.IsFalse(GestorFestivos.EsFestivo(new DateTime(2030, 1, 1), Constantes.Almacenes.ALGETE));
            Assert.IsTrue(GestorFestivos.EsFestivo(new DateTime(2030, 1, 5), Constantes.Almacenes.ALGETE), "Sábado");
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "laborable");
        }

        [TestMethod]
        public void Cache_LeeComoMuchoCadaDiezMinutos()
        {
            ConTabla(F(MARTES_03_11_2026, TipoFestivo.Nacional));

            _ = CalendarioFestivos.DelAnio(2026);
            _ = CalendarioFestivos.DelAnio(2026);
            ahora = ahora.AddMinutes(9);
            _ = CalendarioFestivos.DelAnio(2026);
            Assert.AreEqual(1, lecturas);

            ahora = ahora.AddMinutes(2);
            _ = CalendarioFestivos.DelAnio(2026);
            Assert.AreEqual(2, lecturas);
        }

        [TestMethod]
        public void Invalidar_VuelveALeerEnLaSiguienteConsulta()
        {
            ConTabla(F(MARTES_03_11_2026, TipoFestivo.Nacional));
            _ = CalendarioFestivos.DelAnio(2026);

            CalendarioFestivos.Invalidar();
            _ = CalendarioFestivos.DelAnio(2026);

            Assert.AreEqual(2, lecturas);
        }

        [TestMethod]
        public void SiFallaLaLectura_SeQuedaConLoUltimo_YLoRegistra()
        {
            bool fallar = false;
            CalendarioFestivos.Configurar(() =>
            {
                if (fallar)
                {
                    throw new InvalidOperationException("sin conexión");
                }
                return new CalendarioFestivos.LecturaFestivos { HayTabla = true, Festivos = { F(MARTES_03_11_2026, TipoFestivo.Nacional) } };
            }, errores.Add, () => ahora);
            Assert.IsTrue(GestorFestivos.EsFestivo(MARTES_03_11_2026, ""));

            fallar = true;
            ahora = ahora.AddMinutes(11);

            Assert.IsTrue(GestorFestivos.EsFestivo(MARTES_03_11_2026, ""));
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "sin conexión");
        }

        [TestMethod]
        public void SiFallaLaPrimeraLectura_UsaElCodigo()
        {
            CalendarioFestivos.Configurar(() => throw new InvalidOperationException("sin conexión"), errores.Add, () => ahora);

            Assert.IsTrue(GestorFestivos.EsFestivo(LUNES_02_11_2026, Constantes.Almacenes.ALGETE));
            Assert.AreEqual(1, errores.Count);
        }

        [DataTestMethod]
        [DataRow("Autonómica", TipoFestivo.Autonómica)]
        [DataRow("Autonomica", TipoFestivo.Autonómica)]
        [DataRow(" nacional ", TipoFestivo.Nacional)]
        [DataRow("ALCOBENDAS", TipoFestivo.Alcobendas)]
        [DataRow("Empresa", TipoFestivo.Empresa)]
        [DataRow("RepartoPropio", TipoFestivo.RepartoPropio)]
        [DataRow("repartopropio", TipoFestivo.RepartoPropio)]
        public void TiposFestivo_TryParse_SinImportarTildesNiMayusculas(string texto, TipoFestivo esperado)
        {
            Assert.IsTrue(TiposFestivo.TryParse(texto, out TipoFestivo tipo));
            Assert.AreEqual(esperado, tipo);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("Local")]
        public void TiposFestivo_TryParse_Desconocido(string texto)
        {
            Assert.IsFalse(TiposFestivo.TryParse(texto, out _));
        }

        /// <summary>El script carga EXACTAMENTE la lista del código de 2024-2026 (mismo comportamiento con la tabla).</summary>
        [TestMethod]
        public void Script_CargaExactamenteLaListaDelCodigo()
        {
            string script = Script();
            int desde = script.IndexOf("-- BEGIN CODIGO", StringComparison.Ordinal);
            int hasta = script.IndexOf("-- END CODIGO", StringComparison.Ordinal);
            Assert.IsTrue(desde > 0 && hasta > desde, "Faltan las marcas BEGIN/END CODIGO");
            List<string> delScript = Regex.Matches(script.Substring(desde, hasta - desde),
                    @"\('(\d{8})', N'([^']+)', N'([^']+)'\)")
                .Cast<Match>()
                .Select(m => $"{m.Groups[1].Value}|{m.Groups[2].Value}|{m.Groups[3].Value}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            List<string> delCodigo = GestorFestivos.TodosLosFestivosDelCodigo
                .Select(f => $"{f.Fecha:yyyyMMdd}|{TiposFestivo.Texto(f.TipoFestivo)}|{f.Fiesta}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            CollectionAssert.AreEqual(delCodigo, delScript);
        }

        private static string Script()
        {
            string carpeta = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && carpeta != null; i++)
            {
                string ruta = Path.Combine(carpeta, "Scripts", "Issue779_Festivos.sql");
                if (File.Exists(ruta))
                {
                    return File.ReadAllText(ruta);
                }
                carpeta = Path.GetDirectoryName(carpeta.TrimEnd(Path.DirectorySeparatorChar));
            }
            Assert.Inconclusive("No se encuentra Scripts/Issue779_Festivos.sql");
            return null;
        }


        // ================================================================ NestoAPI#841: día sin reparto propio

        private static readonly DateTime MIERCOLES_04_11_2026 = new DateTime(2026, 11, 4);
        private static readonly DateTime NOCHEBUENA_2026 = new DateTime(2026, 12, 24);

        [TestMethod]
        public void Issue841_DiaSinRepartoPropio_NoEsFestivoDeNingunAlmacenNiTienda()
        {
            ConTabla(F(MIERCOLES_04_11_2026, TipoFestivo.RepartoPropio, "Andre y Miguel libran"), F(NOCHEBUENA_2026, TipoFestivo.Empresa));

            foreach (string almacen in new[] { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS, "", null })
            {
                Assert.IsFalse(GestorFestivos.EsFestivo(MIERCOLES_04_11_2026, almacen), $"«{almacen}» trabaja ese día");
            }
            Assert.AreEqual(MIERCOLES_04_11_2026, GestorFestivos.PrimerLaborableDesde(MIERCOLES_04_11_2026, Constantes.Almacenes.ALGETE));
        }

        [TestMethod]
        public void Issue841_DiaSinRepartoPropio_SiQueFrenaElRepartoPropio()
        {
            ConTabla(F(MIERCOLES_04_11_2026, TipoFestivo.RepartoPropio, "Andre y Miguel libran"), F(NOCHEBUENA_2026, TipoFestivo.Empresa));

            Assert.IsTrue(GestorFestivos.EsDiaSinRepartoPropio(MIERCOLES_04_11_2026));
            Assert.IsTrue(GestorFestivos.EsDiaSinRepartoPropio(MIERCOLES_04_11_2026.AddHours(15)), "la hora no cuenta");
            Assert.IsTrue(GestorFestivos.EsFestivoRepartoPropio(MIERCOLES_04_11_2026));
            Assert.IsTrue(GestorFestivos.EsFestivo(MIERCOLES_04_11_2026, GestorFestivos.REPARTO_PROPIO));
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.RepartoPropio.FechaRepartoPropio.EsDiaSinReparto(MIERCOLES_04_11_2026));

            Assert.IsFalse(GestorFestivos.EsDiaSinRepartoPropio(MARTES_03_11_2026));
            Assert.IsFalse(GestorFestivos.EsFestivoRepartoPropio(MARTES_03_11_2026));
            // Los festivos de Algete (de donde sale la furgoneta) también frenan el reparto, pero no son «días sin reparto propio»
            Assert.IsTrue(GestorFestivos.EsFestivoRepartoPropio(NOCHEBUENA_2026));
            Assert.IsFalse(GestorFestivos.EsDiaSinRepartoPropio(NOCHEBUENA_2026));
            Assert.IsTrue(GestorFestivos.EsFestivoRepartoPropio(new DateTime(2026, 11, 7)), "sábado");
        }

        [TestMethod]
        public void Issue841_DiaSinRepartoPropio_SeVeAlInvalidarLaCache()
        {
            var festivos = new List<Festivo> { F(new DateTime(2026, 1, 1), TipoFestivo.Nacional) };
            CalendarioFestivos.Configurar(() => new CalendarioFestivos.LecturaFestivos { HayTabla = true, Festivos = festivos.ToList() },
                errores.Add, () => ahora);
            Assert.IsFalse(GestorFestivos.EsDiaSinRepartoPropio(MIERCOLES_04_11_2026));

            festivos.Add(F(MIERCOLES_04_11_2026, TipoFestivo.RepartoPropio));
            CalendarioFestivos.Invalidar();

            Assert.IsTrue(GestorFestivos.EsDiaSinRepartoPropio(MIERCOLES_04_11_2026));
        }

        [TestMethod]
        public void Issue841_EsFestivoRepartoPropio_ConFuncionInyectada_FestivoDeAlgeteODeRepartoPropio()
        {
            Func<DateTime, string, bool> esFestivo = (dia, almacen) =>
                (almacen == Constantes.Almacenes.ALGETE && dia == NOCHEBUENA_2026)
                || (almacen == GestorFestivos.REPARTO_PROPIO && dia == MIERCOLES_04_11_2026)
                || (almacen == Constantes.Almacenes.REINA && dia == MARTES_03_11_2026);

            Assert.IsTrue(GestorFestivos.EsFestivoRepartoPropio(NOCHEBUENA_2026, esFestivo));
            Assert.IsTrue(GestorFestivos.EsFestivoRepartoPropio(MIERCOLES_04_11_2026, esFestivo));
            Assert.IsFalse(GestorFestivos.EsFestivoRepartoPropio(MARTES_03_11_2026, esFestivo), "un festivo de Reina no frena la furgoneta");
        }
    }
}
