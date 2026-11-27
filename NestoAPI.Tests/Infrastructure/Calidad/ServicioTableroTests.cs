using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Calidad
{
    /// <summary>Las tablas del tablero en memoria.</summary>
    internal class RepositorioTableroEnMemoria : IRepositorioTablero
    {
        public bool Existen { get; set; } = true;
        public List<SemanaKpiGuardada> Semanas { get; } = new List<SemanaKpiGuardada>();
        public List<UmbralesKpi> Umbrales { get; } = new List<UmbralesKpi>();
        public List<string> Guardadas { get; } = new List<string>();
        public List<(string Anterior, string Nuevo)> Rastros { get; } = new List<(string, string)>();

        public bool TablasExisten() => Existen;

        public void GuardarSemana(string kpi, DateTime lunes, IReadOnlyList<ValorKpi> valores, DateTime calculadoEl)
        {
            Guardadas.Add($"{kpi} {lunes:dd/MM}");
            _ = Semanas.RemoveAll(s => s.Kpi == kpi && s.Semana == lunes);
            Semanas.AddRange(valores.Select(v => new SemanaKpiGuardada
            {
                Kpi = kpi, TipoDueno = v.TipoDueno, Dueno = v.Dueno, Semana = lunes, Valor = v.Valor, Numerador = v.Numerador,
                Denominador = v.Denominador, Detalle = v.Detalle, CalculadoEl = calculadoEl
            }));
        }

        public List<SemanaKpiGuardada> LeerSemanas(string kpi, TipoDuenoKpi? tipo, string dueno, DateTime desde, DateTime hasta) =>
            Semanas.Where(s => s.Semana >= desde && s.Semana < hasta && (kpi == null || s.Kpi == kpi) && (!tipo.HasValue || s.TipoDueno == tipo)
                && (dueno == null || string.Equals(s.Dueno, dueno, StringComparison.OrdinalIgnoreCase))).ToList();

        public HashSet<DateTime> SemanasGuardadas(string kpi, DateTime desde) =>
            new HashSet<DateTime>(Semanas.Where(s => s.Kpi == kpi && s.Semana >= desde).Select(s => s.Semana));

        public List<UmbralesKpi> LeerUmbrales(string kpi = null) => Umbrales.Where(u => kpi == null || u.Kpi == kpi).ToList();

        public void AprobarUmbrales(UmbralesKpi umbrales, string rastroAnterior, string rastroNuevo)
        {
            _ = Umbrales.RemoveAll(u => u.Kpi == umbrales.Kpi && u.TipoDueno == umbrales.TipoDueno && u.VigenteDesde == umbrales.VigenteDesde);
            Umbrales.Add(umbrales);
            Rastros.Add((rastroAnterior, rastroNuevo));
        }

        public void Guardar(string kpi, TipoDuenoKpi tipo, string dueno, DateTime lunes, decimal? valor) =>
            Semanas.Add(new SemanaKpiGuardada { Kpi = kpi, TipoDueno = tipo, Dueno = dueno, Semana = lunes, Valor = valor, CalculadoEl = lunes.AddDays(7) });
    }

    /// <summary>Un KPI de prueba: devuelve lo que se le diga y apunta los periodos que le piden.</summary>
    internal class KpiDePrueba : IKpiTablero
    {
        public KpiDePrueba(string clave, int maduracion, params TipoDuenoKpi[] tipos)
        {
            Clave = clave;
            DiasMaduracion = maduracion;
            TiposDueno = tipos.Length == 0 ? new[] { TipoDuenoKpi.Almacen } : tipos;
        }

        public string Clave { get; }
        public string Nombre => "Prueba " + Clave;
        public string Descripcion => "Para los tests.";
        public IReadOnlyList<TipoDuenoKpi> TiposDueno { get; }
        public UnidadKpi Unidad => UnidadKpi.Porcentaje;
        public SentidoKpi Sentido { get; set; } = SentidoKpi.MasEsMejor;
        public int DiasMaduracion { get; }
        public string PorQueMadura => DiasMaduracion == 0 ? null : "Hay que esperar.";
        public bool TieneFuente { get; set; } = true;
        public List<PeriodoKpi> Pedidos { get; } = new List<PeriodoKpi>();
        public Func<PeriodoKpi, List<ValorKpi>> Valores { get; set; } = p => new List<ValorKpi>
        {
            new ValorKpi { TipoDueno = TipoDuenoKpi.Almacen, Dueno = "ALG", Valor = 80 },
            new ValorKpi { TipoDueno = TipoDuenoKpi.Almacen, Dueno = "REI", Valor = 40 }
        };

        public List<ValorKpi> Calcular(PeriodoKpi periodo, IFuentesKpi fuentes)
        {
            Pedidos.Add(periodo);
            return Valores(periodo);
        }
    }

    /// <summary>NestoAPI#914, tablero: el job, Mis indicadores, la evolución, el tablero de Dirección y los umbrales.</summary>
    [TestClass]
    public class ServicioTableroTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 27, 10, 0, 0); // viernes
        private static readonly DateTime LUNES_ACTUAL = new DateTime(2026, 11, 23);
        private RepositorioTableroEnMemoria repo;
        private List<Exception> errores;

        [TestInitialize]
        public void Preparar()
        {
            repo = new RepositorioTableroEnMemoria();
            errores = new List<Exception>();
        }

        private ServicioTablero Servicio(params IKpiTablero[] kpis) =>
            new ServicioTablero(repo, () => new FuentesKpiFalsas(), () => HOY, new MemoryCache("tablero" + Guid.NewGuid()), kpis, errores.Add);

        private static IdentidadTablero Almacen(string almacen) => new IdentidadTablero { Usuario = "Andre", Almacen = almacen, Preparador = "Andre" };

        [TestMethod]
        public void Job_RecalculaLasUltimasNYRellenaLasQueFaltan()
        {
            var kpi = new KpiDePrueba("A", 0);
            // Ya guardadas: 16/11 (se recalcula: está entre las 2 últimas) y 26/10 (no se recalcula)
            repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", new DateTime(2026, 11, 16), 1);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", new DateTime(2026, 10, 26), 1);

            ResultadoCalculoTablero r = Servicio(kpi).CalcularSemanas(semanasBuscar: 4, semanasRecalcular: 2);

            CollectionAssert.AreEqual(new[] { "A 02/11", "A 09/11", "A 16/11" }, repo.Guardadas);
            Assert.AreEqual(3, r.SemanasCalculadas);
            Assert.IsTrue(kpi.Pedidos.All(p => p.Entera));
            Assert.AreEqual(80m, repo.Semanas.Single(s => s.Semana == new DateTime(2026, 11, 16) && s.Dueno == "ALG").Valor);
        }

        [TestMethod]
        public void Job_ConMaduracion_SoloSemanasMaduras_ySinFuenteNada()
        {
            var madura = new KpiDePrueba("M", 21);
            var sinFuente = new KpiDePrueba("S", 0) { TieneFuente = false };

            _ = Servicio(madura, sinFuente).CalcularSemanas(1, 1);

            CollectionAssert.AreEqual(new[] { "M 26/10" }, repo.Guardadas);
            Assert.AreEqual(0, sinFuente.Pedidos.Count);
        }

        [TestMethod]
        public void Job_UnaSemanaQueFallaNoParaElResto()
        {
            var kpi = new KpiDePrueba("A", 0) { Valores = p => p.Lunes == new DateTime(2026, 11, 9) ? throw new InvalidOperationException("caída") : new List<ValorKpi>() };

            ResultadoCalculoTablero r = Servicio(kpi).CalcularSemanas(3, 3);

            Assert.AreEqual(1, r.Fallos);
            Assert.AreEqual(2, r.SemanasCalculadas);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "09/11/2026");
        }

        [TestMethod]
        public void Mios_SoloLoSuyo_ConEnCursoTiraYTendencia()
        {
            var kpi = new KpiDePrueba("A", 0);
            for (int i = 1; i <= 10; i++)
            {
                repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", LUNES_ACTUAL.AddDays(-7 * i), i == 1 ? 90 : 80);
                repo.Guardar("A", TipoDuenoKpi.Almacen, "REI", LUNES_ACTUAL.AddDays(-7 * i), 10);
            }
            repo.Umbrales.Add(new UmbralesKpi { Kpi = "A", TipoDueno = TipoDuenoKpi.Almacen, CortePoorFair = 50, CorteFairGood = 70, CorteGoodFantastic = 85, VigenteDesde = ReglaNotaKpi.DESDE_SIEMPRE });

            KpiMioDTO mio = Servicio(kpi).Mios(Almacen("alg")).Single();

            Assert.AreEqual("ALG", mio.Dueno);
            Assert.AreEqual("Almacen", mio.TipoDueno);
            Assert.AreEqual(new DateTime(2026, 11, 16), mio.UltimaSemana.Semana);
            Assert.AreEqual("FANTASTIC", mio.UltimaSemana.Nota);
            Assert.AreEqual("Sube", mio.Tendencia);
            Assert.AreEqual(true, mio.Mejora);
            Assert.AreEqual(ServicioTablero.SEMANAS_TIRA, mio.Ultimas.Count);
            Assert.AreEqual(new DateTime(2026, 11, 16), mio.Ultimas.Last().Semana);
            Assert.IsTrue(mio.Ultimas.Take(7).All(u => u.Nota == "GOOD"));
            Assert.IsNotNull(mio.EnCurso);
            Assert.IsTrue(mio.EnCurso.EnCurso);
            Assert.AreEqual(80m, mio.EnCurso.Valor);
            Assert.AreEqual("GOOD", mio.EnCurso.NotaProvisional);
            Assert.AreEqual(new DateTime(2026, 11, 27), mio.EnCurso.HastaDia);
            Assert.AreEqual(new DateTime(2026, 11, 28), kpi.Pedidos.Single().Hasta);
        }

        [TestMethod]
        public void Mios_ElEnCursoSeCalculaUnaVezCada15Minutos()
        {
            var kpi = new KpiDePrueba("A", 0);
            ServicioTablero servicio = Servicio(kpi);

            _ = servicio.Mios(Almacen("ALG"));
            _ = servicio.Mios(Almacen("REI"));

            Assert.AreEqual(1, kpi.Pedidos.Count);
        }

        [TestMethod]
        public void Mios_ConMaduracion_SinEnCursoYCuandoEstara()
        {
            var kpi = new KpiDePrueba("M", 21);

            KpiMioDTO mio = Servicio(kpi).Mios(Almacen("ALG")).Single();

            Assert.IsNull(mio.EnCurso);
            Assert.AreEqual(new DateTime(2026, 12, 21), mio.SemanaActualDisponibleEl);
            StringAssert.Contains(mio.Aviso, "21/12/2026");
            Assert.AreEqual(0, kpi.Pedidos.Count);
        }

        [TestMethod]
        public void Mios_SinNingunKpi_ListaVacia_yUnoSinFuenteTodaviaNoDisponible()
        {
            var vendedor = new KpiDePrueba("V", 0, TipoDuenoKpi.Vendedor);
            var sinFuente = new KpiDePrueba("S", 0, TipoDuenoKpi.Preparador) { TieneFuente = false };

            Assert.AreEqual(0, Servicio(vendedor, sinFuente).Mios(new IdentidadTablero { Usuario = "Nadie" }).Count);
            KpiMioDTO mio = Servicio(vendedor, sinFuente).Mios(Almacen("ALG")).Single();
            Assert.AreEqual("S", mio.Kpi);
            Assert.IsFalse(mio.Disponible);
            StringAssert.Contains(mio.Aviso, "Todavía no disponible");
        }

        [TestMethod]
        public void Mios_SinUmbrales_SinNota()
        {
            var kpi = new KpiDePrueba("A", 0);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", new DateTime(2026, 11, 16), 50);

            KpiMioDTO mio = Servicio(kpi).Mios(Almacen("ALG")).Single();

            Assert.IsFalse(mio.TieneUmbrales);
            Assert.IsNull(mio.UltimaSemana.Nota);
            Assert.IsNull(mio.EnCurso.NotaProvisional);
            Assert.IsNull(mio.Tendencia);
        }

        [TestMethod]
        public void Evolucion_DelDueno_DeLaMasNuevaALaMasAntigua()
        {
            var kpi = new KpiDePrueba("A", 0);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "REI", new DateTime(2026, 11, 9), 1);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "REI", new DateTime(2026, 11, 16), 2);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", new DateTime(2026, 11, 16), 3);

            EvolucionKpiDTO e = Servicio(kpi).Evolucion(kpi, TipoDuenoKpi.Almacen, "rei", 12);

            CollectionAssert.AreEqual(new decimal?[] { 2, 1 }, e.Semanas.Select(s => s.Valor).ToList());
            Assert.AreEqual(40m, e.EnCurso.Valor);
        }

        [TestMethod]
        public void Tablero_SemanaCerradaConNota_yLaQueAunMaduraConSuFecha()
        {
            var cerrado = new KpiDePrueba("A", 0);
            var madura = new KpiDePrueba("M", 21);
            repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", new DateTime(2026, 11, 16), 75);
            repo.Umbrales.Add(new UmbralesKpi { Kpi = "A", TipoDueno = TipoDuenoKpi.Almacen, CortePoorFair = 50, CorteFairGood = 70, CorteGoodFantastic = 85, VigenteDesde = ReglaNotaKpi.DESDE_SIEMPRE });

            TableroSemanaDTO t = Servicio(cerrado, madura).Tablero(new DateTime(2026, 11, 18));

            Assert.AreEqual(new DateTime(2026, 11, 16), t.Semana);
            KpiTableroDTO a = t.Kpis.Single(k => k.Kpi == "A");
            Assert.IsTrue(a.Cerrada);
            Assert.AreEqual("GOOD", a.Valores.Single().Nota);
            KpiTableroDTO m = t.Kpis.Single(k => k.Kpi == "M");
            Assert.IsFalse(m.Cerrada);
            Assert.AreEqual(new DateTime(2026, 12, 14), m.DisponibleEl);
            Assert.AreEqual(0, m.Valores.Count);
        }

        [TestMethod]
        public void Tablero_SemanaActual_EnCursoConNotaProvisionalYSinNota()
        {
            var kpi = new KpiDePrueba("A", 0);

            KpiTableroDTO a = Servicio(kpi).Tablero(LUNES_ACTUAL).Kpis.Single();

            Assert.IsTrue(a.EnCurso);
            Assert.AreEqual(2, a.Valores.Count);
            Assert.IsTrue(a.Valores.All(v => v.Nota == null));
        }

        [TestMethod]
        public void Propuesta_ConLasSemanasGuardadas_YCuantasCaenEnCadaNota()
        {
            var kpi = new KpiDePrueba("A", 0);
            for (int i = 1; i <= 12; i++)
            {
                repo.Guardar("A", TipoDuenoKpi.Almacen, "ALG", LUNES_ACTUAL.AddDays(-7 * i), 10 * i);
            }

            PropuestaTipoDTO p = Servicio(kpi).Propuesta(kpi, null).Tipos.Single();

            Assert.AreEqual(12, p.SemanasDueno);
            Assert.AreEqual(38m, p.Propuesta.CortePoorFair);    // P25 de 10..120 = 37,5
            Assert.AreEqual(65m, p.Propuesta.CorteFairGood);    // 65
            Assert.AreEqual(93m, p.Propuesta.CorteGoodFantastic); // 92,5
            Assert.AreEqual(12, p.Distribucion.POOR + p.Distribucion.FAIR + p.Distribucion.GOOD + p.Distribucion.FANTASTIC);
            Assert.AreEqual(3, p.Distribucion.POOR);
            Assert.IsNull(p.Vigentes);
        }

        [TestMethod]
        public void Aprobar_ElPrimeroValeDesdeSiempre_yElSiguienteDesdeEsteLunes_ConRastro()
        {
            var kpi = new KpiDePrueba("A", 0);
            ServicioTablero servicio = Servicio(kpi);

            UmbralesKpi primero = servicio.Aprobar(new AprobarUmbralesDTO { Kpi = "a", TipoDueno = "almacen", CortePoorFair = 50, CorteFairGood = 70, CorteGoodFantastic = 85 }, "NUEVAVISION\\Carlos");
            UmbralesKpi segundo = servicio.Aprobar(new AprobarUmbralesDTO { Kpi = "A", TipoDueno = "Almacen", CortePoorFair = 60, CorteFairGood = 75, CorteGoodFantastic = 90 }, "NUEVAVISION\\Carlos");

            Assert.AreEqual(ReglaNotaKpi.DESDE_SIEMPRE, primero.VigenteDesde);
            Assert.AreEqual(LUNES_ACTUAL, segundo.VigenteDesde);
            Assert.AreEqual(2, repo.Umbrales.Count);
            Assert.IsNull(repo.Rastros[0].Anterior);
            StringAssert.Contains(repo.Rastros[1].Anterior, "\"CortePoorFair\":50");
            StringAssert.Contains(repo.Rastros[1].Nuevo, "\"VigenteDesde\":\"2026-11-23\"");
            Assert.AreEqual("NUEVAVISION\\Carlos", segundo.Usuario);
        }

        [TestMethod]
        public void Aprobar_ConUmbralesYaAprobados_NoSePuedeCambiarElPasado()
        {
            var kpi = new KpiDePrueba("A", 0);
            ServicioTablero servicio = Servicio(kpi);
            _ = servicio.Aprobar(new AprobarUmbralesDTO { Kpi = "A", TipoDueno = "Almacen", CortePoorFair = 50, CorteFairGood = 70, CorteGoodFantastic = 85 }, "Carlos");

            var ex = Assert.ThrowsException<ErrorTableroException>(() => servicio.Aprobar(new AprobarUmbralesDTO
            {
                Kpi = "A", TipoDueno = "Almacen", CortePoorFair = 50, CorteFairGood = 70, CorteGoodFantastic = 85, VigenteDesde = new DateTime(2026, 11, 2)
            }, "Carlos"));

            StringAssert.Contains(ex.Message, "no cambiar las notas de las semanas pasadas");
        }

        [DataTestMethod]
        [DataRow("NoExiste", "Almacen", 50, 70, 85, "No existe el indicador")]
        [DataRow("A", "Vendedor", 50, 70, 85, "tiene que ser Almacen")]
        [DataRow("A", "Almacen", 85, 70, 50, "de menor a mayor")]
        public void Aprobar_Errores400DeUsted(string kpi, string tipo, int a, int b, int c, string mensaje)
        {
            var ex = Assert.ThrowsException<ErrorTableroException>(() => Servicio(new KpiDePrueba("A", 0)).Aprobar(new AprobarUmbralesDTO
            {
                Kpi = kpi, TipoDueno = tipo, CortePoorFair = a, CorteFairGood = b, CorteGoodFantastic = c
            }, "Carlos"));

            StringAssert.Contains(ex.Message, mensaje);
        }

        [TestMethod]
        public void Aprobar_FaltaUnCorte_400()
        {
            Assert.ThrowsException<ErrorTableroException>(() => Servicio(new KpiDePrueba("A", 0)).Aprobar(new AprobarUmbralesDTO
            {
                Kpi = "A", TipoDueno = "Almacen", CortePoorFair = 1, CorteFairGood = 2
            }, "Carlos"));
        }

        [TestMethod]
        public void JobsService_SinTablas_NoHaceNadaYAvisaUnaVez()
        {
            TableroKpiJobsService.OlvidarAvisoSinTablas();
            repo.Existen = false;
            var avisos = new List<Exception>();

            _ = TableroKpiJobsService.Ejecutar(12, 8, "test", repo, () => new FuentesKpiFalsas(), avisos.Add);
            _ = TableroKpiJobsService.Ejecutar(12, 8, "test", repo, () => new FuentesKpiFalsas(), avisos.Add);

            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, TableroKpiJobsService.SCRIPT);
            Assert.AreEqual(0, repo.Guardadas.Count);
            TableroKpiJobsService.OlvidarAvisoSinTablas();
        }

        // ---------------------------------------------------------------- Quién soy

        private static IPrincipal Usuario(string nombre, params string[] roles) =>
            new GenericPrincipal(new GenericIdentity(nombre, "Bearer"), roles);

        [TestMethod]
        public async Task Identidad_AlmacenSinParametro_Algete_yPreparadorSinDominio()
        {
            IdentidadTablero id = await ResolutorIdentidadTablero.Resolver(Usuario(@"NUEVAVISION\Andre", "Almacén"),
                u => Task.FromResult(new List<string>()), n => null);

            Assert.AreEqual("ALG", id.Almacen);
            Assert.AreEqual("Andre", id.Preparador);
            Assert.IsFalse(id.EsDireccion);
            Assert.AreEqual(2, id.Duenos(new KpiEntregasIncompletas()).Count + id.Duenos(new KpiReenviadoSinCoste()).Count);
        }

        [TestMethod]
        public async Task Identidad_TiendaConSuAlmacen_ySinElNinguno()
        {
            IdentidadTablero reina = await ResolutorIdentidadTablero.Resolver(Usuario("Paloma", "Tiendas"), u => Task.FromResult(new List<string>()), n => "rei ");
            IdentidadTablero sinTienda = await ResolutorIdentidadTablero.Resolver(Usuario("Otra", "Tiendas"), u => Task.FromResult(new List<string>()), n => null);

            Assert.AreEqual("REI", reina.Almacen);
            Assert.IsNull(sinTienda.Almacen);
        }

        [TestMethod]
        public async Task Identidad_VendedorSinGrupoDeAlmacen_NoTieneAlmacenAunqueTengaAlmacenPedidoVta()
        {
            string almacenPreguntado = null;
            IdentidadTablero id = await ResolutorIdentidadTablero.Resolver(Usuario(@"NUEVAVISION\Ines", "Vendedores"),
                u => Task.FromResult(new List<string> { "IA" }), n => almacenPreguntado = "ALG");

            Assert.IsNull(id.Almacen);
            Assert.IsNull(almacenPreguntado);
            DuenoKpi suyo = id.Duenos(new KpiPedidoPerfecto()).Single();
            Assert.AreEqual(TipoDuenoKpi.Vendedor, suyo.Tipo);
            Assert.AreEqual("IA", suyo.Dueno);
        }

        [TestMethod]
        public async Task Identidad_Direccion_LosDeLaEmpresa()
        {
            IdentidadTablero id = await ResolutorIdentidadTablero.Resolver(Usuario("Carlos", "Dirección"), u => Task.FromResult(new List<string>()), n => null);

            Assert.IsTrue(id.PuedeVerTodo);
            Assert.AreEqual("1", id.Duenos(new KpiPedidoPerfecto()).Single(d => d.Tipo == TipoDuenoKpi.Empresa).Dueno);
            Assert.IsTrue(id.EsSuyo(new KpiEntregasIncompletas(), TipoDuenoKpi.Empresa, "1"));
            Assert.IsFalse(id.EsSuyo(new KpiEntregasIncompletas(), TipoDuenoKpi.Almacen, "ALG"));
        }

        [TestMethod]
        public async Task Identidad_SinAutenticar_Nada()
        {
            IdentidadTablero id = await ResolutorIdentidadTablero.Resolver(new GenericPrincipal(new GenericIdentity(""), new string[0]),
                u => throw new InvalidOperationException("no debería preguntar"), n => "ALG");

            Assert.AreEqual(0, CatalogoKpiTablero.Todos.Sum(k => id.Duenos(k).Count));
        }
    }
}
