using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloLlamadaPedido.Datos;
using ModeloLlamadaPedido.Entrenamiento;
using ModeloLlamadaPedido.Evaluacion;
using ModeloLlamadaPedido.Features;
using ModeloLlamadaPedido.Reentrenamiento;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using PrediccionContacto = NestoAPI.Infraestructure.Rapports.PrediccionContacto;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#618: modelo de llamadas por subgrupo (objetivo: pedido en 7 días que INCLUYA el subgrupo elegido). Features de
    /// relación cliente↔subgrupo y etiqueta (núcleo, funciones puras), dataset, precision@k por subgrupo, elección del modelo en
    /// la API con su motivo legible, el job que lo entrena tras el general con la misma puerta, y un entrenamiento real en net48.
    /// </summary>
    [TestClass]
    public class ModeloSubgrupoTests
    {
        private const string ID = "1/0";
        private static readonly DateTime D = new DateTime(2026, 6, 15, 10, 30, 0);

        private static PedidoSubgrupoDia L(string fecha, string gs, decimal importe, string id = ID)
            => new PedidoSubgrupoDia(id, DateTime.Parse(fecha), gs, importe);

        private static List<PedidoSubgrupoDia> Lineas() => new List<PedidoSubgrupoDia>
        {
            L("2024-05-01", "COS001", 999),  // más de 24 meses: fuera
            L("2025-01-10", "COS003", 30),   // solo 24 meses
            L("2025-06-20", "COS001", 40),   // mismo mes del año anterior (B)
            L("2025-10-01", "COS001", 60),   // B
            L("2026-03-10", "COS002", 80),   // A
            L("2026-06-01", "COS001", 100),  // A
            L("2026-06-01", "COS001", 20),   // mismo día: un solo pedido
            L("2026-06-15", "COS001", 50),   // día del contacto: no es feature, sí etiqueta
            L("2026-06-20", "COS002", 10),   // dentro de los 7 días
            L("2026-06-23", "COS003", 10)    // fuera de los 7 días
        };

        private static ModeloContactoEntrada General(float pedidos12 = 5, string masVendido = "COS001")
            => new ModeloContactoEntrada { ClienteId = ID, TipoInteraccion = "Llamada", Mes = "6", DiaSemana = "1", GrupoSubgrupoMasVendido = masVendido,
                Pedidos12Meses = pedidos12, DiasDesdeUltimoPedido = 14, TasaConversionCliente = 0.4f };

        private static string RutaModeloMuestra()
        {
            string[] candidatas =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recursos", "modelo_llamadas_muestra_MPP_36m.zip"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Recursos", "modelo_llamadas_muestra_MPP_36m.zip")
            };
            string ruta = candidatas.FirstOrDefault(File.Exists);
            Assert.IsNotNull(ruta, "falta el zip de muestra en la salida de los tests (Recursos, CopyToOutputDirectory)");
            return ruta;
        }

        // ---------------- Features y etiqueta (núcleo) ----------------

        [TestMethod]
        public void Features_SinFuga_VentanasDe12y24Meses_UnPedidoPorDia()
        {
            ResumenSubgrupos r = CalculadoraFeaturesSubgrupo.Resumir(Lineas(), D);

            EstadisticaSubgrupo cos001 = r.De("COS001");
            Assert.AreEqual(3, cos001.Pedidos12, "01/06 (dos filas = un pedido), 01/10 y 20/06/2025; no el del día del contacto");
            Assert.AreEqual(3, cos001.Pedidos24, "el de 2024 queda fuera de los 24 meses");
            Assert.AreEqual(new DateTime(2026, 6, 1), cos001.Ultimo);
            Assert.AreEqual(220m, cos001.Importe12);
            Assert.AreEqual(120m, cos001.ImporteA);
            Assert.AreEqual(100m, cos001.ImporteB);
            Assert.AreEqual(1, cos001.MismoMes);
            Assert.AreEqual(0, r.De("COS003").Pedidos12);
            Assert.AreEqual(1, r.De("COS003").Pedidos24);
            Assert.AreEqual(300m, r.ImporteTotal12);
            Assert.AreSame(EstadisticaSubgrupo.Vacia, r.De("PEL999"));
        }

        [TestMethod]
        public void Features_RelacionConElSubgrupo_YCopiaDeLasGenerales()
        {
            ResumenSubgrupos r = CalculadoraFeaturesSubgrupo.Resumir(Lineas(), D);

            ModeloContactoSubgrupoEntrada e = CalculadoraFeaturesSubgrupo.Calcular<ModeloContactoSubgrupoEntrada>(General(), r, " cos001 ");

            Assert.AreEqual("COS001", e.GrupoSubgrupo, "recortado y en mayúsculas");
            Assert.AreEqual(3f, e.PedidosSubgrupo12Meses);
            Assert.AreEqual(3f, e.PedidosSubgrupo24Meses);
            Assert.AreEqual(14f, e.DiasDesdeUltimoPedidoSubgrupo);
            Assert.AreEqual(3.5f / 6f, e.FrecuenciaSubgrupo, 1e-6, "(3 + 0,5) / (5 + 1)");
            Assert.AreEqual(220f / 300f, e.CuotaImporteSubgrupo, 1e-6);
            Assert.AreEqual(1f, e.SubgrupoMismoMesAnnoAnterior);
            Assert.AreEqual(20f / 220f, e.TendenciaSubgrupo, 1e-6);
            Assert.AreEqual(1f, e.EsSubgrupoMasVendido);
            Assert.AreEqual(0f, e.SubgrupoNuncaComprado);
            // Las generales, tal cual.
            Assert.AreEqual(ID, e.ClienteId);
            Assert.AreEqual("Llamada", e.TipoInteraccion);
            Assert.AreEqual("COS001", e.GrupoSubgrupoMasVendido);
            Assert.AreEqual(0.4f, e.TasaConversionCliente);
        }

        [TestMethod]
        public void Features_SubgrupoNuncaComprado_ValoresFijos()
        {
            ModeloContactoSubgrupoEntrada e = CalculadoraFeaturesSubgrupo.Calcular<ModeloContactoSubgrupoEntrada>(General(),
                CalculadoraFeaturesSubgrupo.Resumir(Lineas(), D), "PEL999");

            Assert.AreEqual(0f, e.PedidosSubgrupo12Meses);
            Assert.AreEqual(CalculadoraFeatures.SIN_PEDIDO_DIAS, e.DiasDesdeUltimoPedidoSubgrupo);
            Assert.AreEqual(0.5f / 6f, e.FrecuenciaSubgrupo, 1e-6);
            Assert.AreEqual(0f, e.CuotaImporteSubgrupo);
            Assert.AreEqual(0f, e.TendenciaSubgrupo);
            Assert.AreEqual(0f, e.EsSubgrupoMasVendido);
            Assert.AreEqual(1f, e.SubgrupoNuncaComprado);
        }

        [TestMethod]
        public void Etiqueta_PedidoConElSubgrupoEnLosSieteDias_DesdeElMismoDia()
        {
            HashSet<string> enVentana = CalculadoraFeaturesSubgrupo.SubgruposEnVentana(Lineas(), D);

            CollectionAssert.AreEquivalent(new[] { "COS001", "COS002" }, enVentana.ToList());
            Assert.IsTrue(CalculadoraFeaturesSubgrupo.EsPositivo(Lineas(), "cos002", D));
            Assert.IsFalse(CalculadoraFeaturesSubgrupo.EsPositivo(Lineas(), "COS003", D), "el 23/06 ya pasa de 7 días");
        }

        [TestMethod]
        public void Candidatos_LosMasCompradosYUnaMuestraEstableDeNoComprados()
        {
            ResumenSubgrupos r = CalculadoraFeaturesSubgrupo.Resumir(Lineas(), D);
            var catalogo = new List<string> { "APA001", "COS001", "COS002", "COS003", "PEL001", "PEL002" };

            List<string> candidatos = CalculadoraFeaturesSubgrupo.Candidatos(r, catalogo, ID, D, maxComprados: 2, noComprados: 2);
            List<string> otraVez = CalculadoraFeaturesSubgrupo.Candidatos(r, catalogo, ID, D, maxComprados: 2, noComprados: 2);

            Assert.AreEqual(4, candidatos.Count);
            CollectionAssert.AreEqual(new[] { "COS001", "COS002" }, candidatos.Take(2).ToList(), "más días primero; empate por orden");
            Assert.IsTrue(candidatos.Skip(2).All(g => new[] { "APA001", "PEL001", "PEL002" }.Contains(g)), "los no comprados nunca son comprados");
            Assert.AreEqual(2, candidatos.Skip(2).Distinct().Count());
            CollectionAssert.AreEqual(candidatos, otraVez, "determinista");
            Assert.AreEqual(CalculadoraFeaturesSubgrupo.SemillaEstable("1/0|20260615"), CalculadoraFeaturesSubgrupo.SemillaEstable("1/0|20260615"));
            Assert.AreEqual(440920331, CalculadoraFeaturesSubgrupo.SemillaEstable("abc"), "FNV-1a fijo: el mismo dataset en net48 y net8");
        }

        [TestMethod]
        public void Catalogo_SoloSubgruposConDiasSuficientes()
        {
            var lineas = Enumerable.Range(0, 5).Select(i => L("2026-01-01", "COS001", 1, $"{i}/0"))
                .Concat(new[] { L("2026-01-01", "COS002", 1), L("2026-01-01", "COS002", 2) })
                .ToList();

            CollectionAssert.AreEqual(new[] { "COS001" }, CalculadoraFeaturesSubgrupo.Catalogo(lineas, minimoDias: 3));
        }

        [TestMethod]
        public void Dataset_UnaFilaPorSubgrupoCompradoMasMuestra_ConSuEtiqueta()
        {
            var datos = new DatosCrudos();
            datos.Rapports.Add(new RapportCrudo(ID, D, "T", true, "MPP"));
            datos.PedidosSubgrupo.AddRange(Lineas());

            List<FilaEntrenamientoSubgrupo> filas = ConstructorDatasetSubgrupo.Construir(datos, D.Date, D.Date.AddDays(1), null, maxComprados: 15, noComprados: 0);

            CollectionAssert.AreEquivalent(new[] { "COS001", "COS002", "COS003" }, filas.Select(f => f.GrupoSubgrupo).ToList());
            Assert.IsTrue(filas.Single(f => f.GrupoSubgrupo == "COS001").Label);
            Assert.IsTrue(filas.Single(f => f.GrupoSubgrupo == "COS002").Label);
            Assert.IsFalse(filas.Single(f => f.GrupoSubgrupo == "COS003").Label, "la marca Pedido del rapport no dice de qué subgrupo");
            Assert.IsTrue(filas.All(f => f.Vendedor == "MPP" && f.AnnoMes == 202606));
        }

        [TestMethod]
        public void PrecisionEnK_PorVendedorMesYSubgrupo()
        {
            // 2 clientes por subgrupo y k = 2: con el subgrupo como segmento hay dos grupos (uno acierta 2/2, el otro 0/2).
            var filas = new List<FilaEntrenamientoSubgrupo>
            {
                new FilaEntrenamientoSubgrupo { ClienteId = "1/0", GrupoSubgrupo = "A", Label = true, Vendedor = "V", AnnoMes = 202606, Fecha = D },
                new FilaEntrenamientoSubgrupo { ClienteId = "2/0", GrupoSubgrupo = "A", Label = true, Vendedor = "V", AnnoMes = 202606, Fecha = D },
                new FilaEntrenamientoSubgrupo { ClienteId = "1/0", GrupoSubgrupo = "B", Label = false, Vendedor = "V", AnnoMes = 202606, Fecha = D },
                new FilaEntrenamientoSubgrupo { ClienteId = "2/0", GrupoSubgrupo = "B", Label = false, Vendedor = "V", AnnoMes = 202606, Fecha = D }
            };

            ResultadoPrecision p = Metricas.PrecisionEnK(filas, new List<float> { 0.9f, 0.8f, 0.7f, 0.6f }, 2);

            Assert.AreEqual(2, p.Grupos);
            Assert.AreEqual(0.5, p.Media, 1e-9);
        }

        [TestMethod]
        public void Entrada_ColumnasDelModeloPorSubgrupo()
        {
            var nuevas = new[] { "GrupoSubgrupo", "PedidosSubgrupo12Meses", "PedidosSubgrupo24Meses", "DiasDesdeUltimoPedidoSubgrupo", "FrecuenciaSubgrupo",
                "CuotaImporteSubgrupo", "SubgrupoMismoMesAnnoAnterior", "TendenciaSubgrupo", "EsSubgrupoMasVendido", "SubgrupoNuncaComprado" };
            List<string> propias = typeof(ModeloContactoSubgrupoEntrada).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => p.Name).ToList();

            CollectionAssert.AreEquivalent(nuevas, propias);
            CollectionAssert.IsSubsetOf(nuevas.Skip(1).ToList(), EntrenadorSubgrupo.Numericas);
            CollectionAssert.Contains(EntrenadorSubgrupo.Categoricas, "GrupoSubgrupo");
            CollectionAssert.IsSubsetOf(Entrenador.Numericas, EntrenadorSubgrupo.Numericas, "las generales siguen");
        }

        // ---------------- API: qué modelo puntúa y con qué motivo ----------------

        private class RepositorioFalso : IRepositorioFeaturesContacto
        {
            private readonly List<HistorialContacto> historiales;
            public RepositorioFalso(List<HistorialContacto> historiales) { this.historiales = historiales; }
            public List<HistorialContacto> Leer(string vendedor, DateTime hoy) => historiales;
        }

        private static List<HistorialContacto> Cartera() => new List<HistorialContacto>
        {
            new HistorialContacto("1/0", new[] { new PedidoDiaContacto(D.Date.AddDays(-14), 120) }, null,
                new[] { new VentaGrupoMesContacto(202605, "COS001", 10) }, null, Lineas().Where(l => l.Dia < D.Date)),
            new HistorialContacto("2/0", null, null, null)
        };

        private static string VendedorUnico() => "T618" + Guid.NewGuid().ToString("N").Substring(0, 5);

        [TestMethod]
        public void UsarModeloSubgrupo_SoloConSubgrupoInterruptorYZip()
        {
            Assert.IsTrue(ProbabilidadesContactoModelo.UsarModeloSubgrupo("COS001", true, @"C:\x.zip"));
            Assert.IsFalse(ProbabilidadesContactoModelo.UsarModeloSubgrupo("", true, @"C:\x.zip"), "sin subgrupo, el general");
            Assert.IsFalse(ProbabilidadesContactoModelo.UsarModeloSubgrupo("COS001", false, @"C:\x.zip"), "apagado = sombra");
            Assert.IsFalse(ProbabilidadesContactoModelo.UsarModeloSubgrupo("COS001", true, null), "sin zip promovido");
        }

        [TestMethod]
        public void SubgrupoActivo_SoloConTrue()
        {
            Assert.IsFalse(ProbabilidadesContactoModelo.SubgrupoActivo(_ => null));
            Assert.IsFalse(ProbabilidadesContactoModelo.SubgrupoActivo(_ => "Sombra"));
            Assert.IsTrue(ProbabilidadesContactoModelo.SubgrupoActivo(c => c == ProbabilidadesContactoModelo.CLAVE_SUBGRUPO_ACTIVO ? " True " : null));
        }

        [TestMethod]
        public void Probabilidades_ConSubgrupoEInterruptor_UsaElModeloPorSubgrupoConMotivo()
        {
            List<ModeloContactoSubgrupoEntrada> recibidas = null;
            var probabilidades = new ProbabilidadesContactoModelo(new RepositorioFalso(Cartera()), () => D, RutaModeloMuestra(),
                () => true, () => @"C:\NestoAPI\ModelosIA\modelo_llamadas_subgrupo.zip",
                (entradas, ruta) => { recibidas = entradas.ToList(); return new List<float> { 0.42f, 0.03f }; });

            Dictionary<string, PrediccionContacto> resultado = probabilidades.Calcular(VendedorUnico(), "Llamada", "COS001", D);

            Assert.IsNotNull(recibidas);
            Assert.AreEqual(3f, recibidas[0].PedidosSubgrupo12Meses);
            Assert.AreEqual(1f, recibidas[1].SubgrupoNuncaComprado);
            Assert.AreEqual(0.42f, resultado["1/0"].Probabilidad);
            Assert.AreEqual("COS001", resultado["1/0"].GrupoSubgrupoMasVendido, "el real del cliente");
            Assert.AreEqual("Con COS001: 3 pedidos en el último año (el último hace 14 días); probabilidad de pedido con COS001 del 42 %", resultado["1/0"].MotivoSubgrupo);
            Assert.AreEqual("No ha comprado COS001 en los dos últimos años (venta cruzada); probabilidad de pedido con COS001 del 3 %", resultado["2/0"].MotivoSubgrupo);
        }

        [TestMethod]
        public void Probabilidades_Apagado_SinSubgrupoOSinZip_ElGeneralComoAntes()
        {
            int llamadas = 0;
            Func<IList<ModeloContactoSubgrupoEntrada>, string, List<float>> puntuar = (e, r) => { llamadas++; return e.Select(_ => 0.9f).ToList(); };

            var apagado = new ProbabilidadesContactoModelo(new RepositorioFalso(Cartera()), () => D, RutaModeloMuestra(), () => false, () => "x.zip", puntuar);
            var sinZip = new ProbabilidadesContactoModelo(new RepositorioFalso(Cartera()), () => D, RutaModeloMuestra(), () => true, () => null, puntuar);
            var encendido = new ProbabilidadesContactoModelo(new RepositorioFalso(Cartera()), () => D, RutaModeloMuestra(), () => true, () => "x.zip", puntuar);

            Dictionary<string, PrediccionContacto> a = apagado.Calcular(VendedorUnico(), "Llamada", "COS001", D);
            Dictionary<string, PrediccionContacto> b = sinZip.Calcular(VendedorUnico(), "Llamada", "COS001", D);
            Dictionary<string, PrediccionContacto> c = encendido.Calcular(VendedorUnico(), "Llamada", "", D);

            Assert.AreEqual(0, llamadas);
            foreach (Dictionary<string, PrediccionContacto> r in new[] { a, b, c })
            {
                Assert.AreEqual(2, r.Count);
                Assert.IsTrue(r.Values.All(p => p.MotivoSubgrupo == null && p.Probabilidad > 0f && p.Probabilidad != 0.9f));
            }
        }

        [TestMethod]
        public void Probabilidades_FallaElModeloPorSubgrupo_ElmahYElGeneral()
        {
            var errores = new List<Exception>();
            var probabilidades = new ProbabilidadesContactoModelo(new RepositorioFalso(Cartera()), () => D, RutaModeloMuestra(),
                () => true, () => "x.zip", (e, r) => throw new FileNotFoundException("no está el zip"), errores.Add);

            Dictionary<string, PrediccionContacto> resultado = probabilidades.Calcular(VendedorUnico(), "Llamada", "COS001", D);

            Assert.AreEqual(2, resultado.Count);
            Assert.IsTrue(resultado.Values.All(p => p.MotivoSubgrupo == null));
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "NestoAPI#618");
        }

        [TestMethod]
        public void MotivoSubgrupo_LoDejoDeComprar_YUnSoloPedido()
        {
            var dejo = new ModeloContactoSubgrupoEntrada { GrupoSubgrupo = "PEL001", PedidosSubgrupo24Meses = 2, DiasDesdeUltimoPedidoSubgrupo = 400 };
            var uno = new ModeloContactoSubgrupoEntrada { GrupoSubgrupo = "PEL001", PedidosSubgrupo12Meses = 1, PedidosSubgrupo24Meses = 1, DiasDesdeUltimoPedidoSubgrupo = 1 };

            Assert.AreEqual("Con PEL001: no lo compra desde hace 400 días; probabilidad de pedido con PEL001 del 5 %", ProbabilidadesContactoModelo.MotivoSubgrupo(dejo, 0.049f));
            Assert.AreEqual("Con PEL001: 1 pedido en el último año (el último hace 1 día); probabilidad de pedido con PEL001 del 13 %", ProbabilidadesContactoModelo.MotivoSubgrupo(uno, 0.125f));
        }

        [TestMethod]
        public void ConMotivoSubgrupo_DetrasDelMotivoDeSiempre()
        {
            Assert.AreEqual("Compra cada 12 días. Con COS001: x", ServicioSugerenciasContacto.ConMotivoSubgrupo("Compra cada 12 días", "Con COS001: x"));
            Assert.AreEqual("Con COS001: x", ServicioSugerenciasContacto.ConMotivoSubgrupo(null, "Con COS001: x"));
            Assert.AreEqual("Igual", ServicioSugerenciasContacto.ConMotivoSubgrupo("Igual", null));
        }

        [TestMethod]
        public void Consulta_ConSubgrupos_AnnadeLaSextaConsultaYAgruparLaReparte()
        {
            using (SqlCommand sin = RepositorioFeaturesContactoSql.CrearComando(new SqlConnection(), "MPP", D))
            using (SqlCommand con = RepositorioFeaturesContactoSql.CrearComando(new SqlConnection(), "MPP", D, conSubgrupos: true))
            {
                Assert.IsFalse(sin.CommandText.Contains("NestoAPI#618"), "apagado, la consulta de siempre");
                StringAssert.EndsWith(con.CommandText, RepositorioFeaturesContactoSql.SQL_SUBGRUPOS);
            }
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL_SUBGRUPOS, "WITH (NOLOCK)");
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL_SUBGRUPOS, "l.SubGrupo <> 'MMP'");
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL_SUBGRUPOS, "cab.Fecha < @Hoy");

            List<HistorialContacto> h = RepositorioFeaturesContactoSql.Agrupar(new[] { "1/0", "2/0" },
                new KeyValuePair<string, RapportContactoCrudo>[0], new KeyValuePair<string, PedidoDiaContacto>[0],
                new KeyValuePair<string, VentaGrupoMesContacto>[0], new Dictionary<string, DateTime>(),
                new[] { L("2026-06-01", "COS001", 5), L("2026-01-01", "COS002", 5) });
            CollectionAssert.AreEqual(new[] { "COS002", "COS001" }, h.Single(x => x.ClienteId == "1/0").PedidosSubgrupo.Select(p => p.GrupoSubgrupo).ToList(), "por día");
            Assert.AreEqual(0, h.Single(x => x.ClienteId == "2/0").PedidosSubgrupo.Count);
        }

        // ---------------- Almacén y job ----------------

        private string carpeta;
        private string rutaDeploy;

        [TestInitialize]
        public void Inicializar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "NestoAPI618_" + Guid.NewGuid().ToString("N"));
            string deploy = Path.Combine(Path.GetTempPath(), "NestoAPI618_deploy_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(deploy);
            rutaDeploy = Path.Combine(deploy, "modelo_llamadas.zip");
            File.WriteAllText(rutaDeploy, "deploy");
        }

        [TestCleanup]
        public void Limpiar()
        {
            foreach (string c in new[] { carpeta, Path.GetDirectoryName(rutaDeploy) })
            {
                try
                {
                    if (Directory.Exists(c))
                    {
                        Directory.Delete(c, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        [TestMethod]
        public void Almacen_Subgrupo_SusFicherosYSinRespaldoDelDeploy()
        {
            var almacen = new AlmacenModeloLlamadas(carpeta, null, AlmacenModeloLlamadas.BASE_SUBGRUPO);

            Assert.AreEqual(Path.Combine(carpeta, "modelo_llamadas_subgrupo.zip"), almacen.RutaModelo);
            Assert.AreEqual(Path.Combine(carpeta, "modelo_llamadas_subgrupo_anterior.zip"), almacen.RutaAnterior);
            Assert.AreEqual(Path.Combine(carpeta, "modelo_llamadas_subgrupo.json"), almacen.RutaMetadatos);
            Assert.AreEqual(Path.Combine(carpeta, "ultimo_reentrenamiento_subgrupo.md"), almacen.RutaUltimoInforme);
            Assert.IsNull(almacen.RutaModeloActivo);
            Assert.IsNull(almacen.CorteModeloActivo());
            Assert.AreEqual(Path.Combine(carpeta, "modelo_llamadas.zip"), new AlmacenModeloLlamadas(carpeta, null).RutaModelo, "el general, como siempre");
        }

        private sealed class Job
        {
            public AlmacenModeloLlamadas Almacen;
            public AlmacenModeloLlamadas AlmacenSubgrupo;
            public IEntrenadorModeloLlamadas Entrenador = A.Fake<IEntrenadorModeloLlamadas>();
            public IEvaluacionModeloLlamadas General = A.Fake<IEvaluacionModeloLlamadas>();
            public IEvaluacionModeloLlamadas Subgrupo = A.Fake<IEvaluacionModeloLlamadas>();
            public IServicioNotificacionesPush Notificaciones = A.Fake<IServicioNotificacionesPush>();
            public List<Exception> Errores = new List<Exception>();
            public Dictionary<string, string> Parametros = new Dictionary<string, string>();
            public OpcionesReentrenamiento OpcionesSubgrupo;

            public ReentrenamientoModeloLlamadasJobsService Servicio()
                => new ReentrenamientoModeloLlamadasJobsService(Entrenador, Almacen, Notificaciones, () => new DateTime(2026, 11, 7, 2, 30, 0),
                    c => Parametros.TryGetValue(c, out string v) ? v : null, () => { }, Errores.Add, AlmacenSubgrupo);

            public List<string> Avisos()
                => Fake.GetCalls(Notificaciones).Select(c => ((NotificacionPushDTO)c.Arguments[2]).Cuerpo).ToList();
        }

        private Job NuevoJob(bool generalPromueve, bool subgrupoPromueve)
        {
            var j = new Job
            {
                Almacen = new AlmacenModeloLlamadas(carpeta, rutaDeploy),
                AlmacenSubgrupo = new AlmacenModeloLlamadas(carpeta, null, AlmacenModeloLlamadas.BASE_SUBGRUPO)
            };
            _ = A.CallTo(() => j.Entrenador.Evaluar(A<OpcionesReentrenamiento>._, A<DateTime>._)).Returns(j.General);
            _ = A.CallTo(() => j.Entrenador.EvaluarSubgrupo(A<OpcionesReentrenamiento>._, A<DateTime>._))
                .ReturnsLazily((OpcionesReentrenamiento o, DateTime d) => { j.OpcionesSubgrupo = o; return j.Subgrupo; });
            foreach ((IEvaluacionModeloLlamadas e, bool promueve, string nombre) in new[] { (j.General, generalPromueve, "general"), (j.Subgrupo, subgrupoPromueve, "subgrupo") })
            {
                _ = A.CallTo(() => e.Decision).Returns(new DecisionPuerta(promueve, $"motivo {nombre}"));
                _ = A.CallTo(() => e.Comparacion).Returns($"comparación {nombre}");
                _ = A.CallTo(() => e.Readme(A<string>._, A<string>._)).ReturnsLazily((string r, string res) => $"README {nombre} {res}");
                _ = A.CallTo(() => e.Metadatos(A<DateTime>._, A<string>._)).ReturnsLazily((DateTime f, string o) =>
                    new MetadatosModelo { Fecha = f, ContactosHasta = new DateTime(2026, 10, 30), Origen = o });
                _ = A.CallTo(() => e.EntrenarFinalYGuardar(A<string>._)).Invokes((string ruta) => File.WriteAllText(ruta, nombre));
            }
            return j;
        }

        [TestMethod]
        public void Job_TrasElGeneral_EntrenaElDeSubgrupoContraElGeneralConSuCorte_YLoPromueveEnSombra()
        {
            Job j = NuevoJob(generalPromueve: false, subgrupoPromueve: true);

            EstadoReentrenamiento estado = j.Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(EstadoReentrenamiento.NoPromovido, estado, "devuelve lo del general");
            Assert.IsNull(j.OpcionesSubgrupo.ModeloActual, "todavía no hay modelo por subgrupo");
            Assert.AreEqual(rutaDeploy, j.OpcionesSubgrupo.ModeloGeneral, "compara con lo de hoy: el general con el subgrupo pisado");
            Assert.AreEqual(VentanaReentrenamiento.CorteDesdeFechaModelo(File.GetLastWriteTime(rutaDeploy)), j.OpcionesSubgrupo.CorteActual, "solo contactos que el general no vio");
            Assert.AreEqual("subgrupo", File.ReadAllText(j.AlmacenSubgrupo.RutaModelo));
            Assert.IsFalse(File.Exists(j.AlmacenSubgrupo.RutaAnterior), "no había anterior");
            Assert.IsFalse(File.Exists(j.Almacen.RutaModelo), "el general no se ha tocado");
            Assert.AreEqual("job mensual", j.AlmacenSubgrupo.LeerMetadatos().Origen);
            List<string> avisos = j.Avisos();
            Assert.AreEqual(2, avisos.Count);
            StringAssert.StartsWith(avisos[0], "Modelo de llamadas: no promovido (motivo general)");
            StringAssert.StartsWith(avisos[1], "Modelo de llamadas por subgrupo: promovido (comparación subgrupo). En sombra: la API no lo usa hasta poner ModeloLlamadas:Subgrupo:Activo = true");
            Assert.AreEqual(0, j.Errores.Count);
        }

        [TestMethod]
        public void Job_ConModeloPorSubgrupo_ComparaConElSuyoYSuCorte_YSiNoPasaSeQuedaElQueHabia()
        {
            Job j = NuevoJob(generalPromueve: false, subgrupoPromueve: false);
            _ = Directory.CreateDirectory(carpeta);
            File.WriteAllText(j.AlmacenSubgrupo.RutaModelo, "subgrupo viejo");
            File.WriteAllText(j.AlmacenSubgrupo.RutaMetadatos, "{\"ContactosHasta\":\"2026-10-01T00:00:00\"}");
            j.Parametros[ProbabilidadesContactoModelo.CLAVE_SUBGRUPO_ACTIVO] = "true";

            _ = j.Servicio().Ejecutar(simular: false, "job mensual");

            Assert.AreEqual(j.AlmacenSubgrupo.RutaModelo, j.OpcionesSubgrupo.ModeloActual);
            Assert.AreEqual(new DateTime(2026, 10, 1), j.OpcionesSubgrupo.CorteActual);
            Assert.AreEqual("subgrupo viejo", File.ReadAllText(j.AlmacenSubgrupo.RutaModelo));
            Assert.AreEqual("README subgrupo no pasa", File.ReadAllText(j.AlmacenSubgrupo.RutaUltimoInforme));
            StringAssert.StartsWith(j.Avisos()[1], "Modelo de llamadas por subgrupo: no promovido (motivo subgrupo). Se queda el que había.");
        }

        [TestMethod]
        public void Job_GeneralRecienPromovido_ElDeSubgrupoComparaConElGeneralAnterior()
        {
            Job j = NuevoJob(generalPromueve: true, subgrupoPromueve: false);
            _ = Directory.CreateDirectory(carpeta);
            File.WriteAllText(j.Almacen.RutaModelo, "general viejo");

            Assert.AreEqual(EstadoReentrenamiento.Promovido, j.Servicio().Ejecutar(simular: false, "job mensual"));

            Assert.AreEqual(j.Almacen.RutaAnterior, j.OpcionesSubgrupo.ModeloGeneral, "el nuevo ya vio todos los contactos");
            Assert.AreEqual("general viejo", File.ReadAllText(j.OpcionesSubgrupo.ModeloGeneral));
            Assert.AreEqual("general", File.ReadAllText(j.Almacen.RutaModelo));
        }

        [TestMethod]
        public void Job_ErrorEnElGeneral_NoEntrenaElDeSubgrupo()
        {
            Job j = NuevoJob(generalPromueve: true, subgrupoPromueve: true);
            _ = A.CallTo(() => j.Entrenador.Evaluar(A<OpcionesReentrenamiento>._, A<DateTime>._)).Throws(new TimeoutException("BD caída"));

            Assert.AreEqual(EstadoReentrenamiento.Error, j.Servicio().Ejecutar(simular: false, "job mensual"));

            A.CallTo(() => j.Entrenador.EvaluarSubgrupo(A<OpcionesReentrenamiento>._, A<DateTime>._)).MustNotHaveHappened();
            Assert.AreEqual(1, j.Avisos().Count);
        }

        [TestMethod]
        public void Job_ErrorEnElDeSubgrupo_ElmahYAvisoSinTocarElGeneral()
        {
            Job j = NuevoJob(generalPromueve: true, subgrupoPromueve: true);
            _ = A.CallTo(() => j.Entrenador.EvaluarSubgrupo(A<OpcionesReentrenamiento>._, A<DateTime>._)).Throws(new InvalidOperationException("sin memoria"));

            Assert.AreEqual(EstadoReentrenamiento.Promovido, j.Servicio().Ejecutar(simular: false, "job mensual"));

            Assert.AreEqual("general", File.ReadAllText(j.Almacen.RutaModelo));
            Assert.AreEqual(1, j.Errores.Count);
            StringAssert.StartsWith(j.Errores[0].Message, "[Modelo de llamadas por subgrupo] Error en el reentrenamiento");
            StringAssert.StartsWith(j.Avisos()[1], "Modelo de llamadas por subgrupo: error (sin memoria). Se queda el que había.");
        }

        [TestMethod]
        public void Job_Simulacion_ElDeSubgrupoTampocoPromueve()
        {
            Job j = NuevoJob(generalPromueve: true, subgrupoPromueve: true);

            Assert.AreEqual(EstadoReentrenamiento.Pasaria, j.Servicio().Ejecutar(simular: true, "a mano"));

            Assert.IsFalse(File.Exists(j.AlmacenSubgrupo.RutaModelo));
            StringAssert.StartsWith(j.Avisos()[1], "Modelo de llamadas por subgrupo: simulación, pasaría la puerta (motivo subgrupo)");
        }

        [TestMethod]
        public void Job_UsoSubgrupo_SegunElInterruptor()
        {
            Job j = NuevoJob(false, false);
            StringAssert.StartsWith(j.Servicio().UsoSubgrupo(), "En sombra");
            j.Parametros[ProbabilidadesContactoModelo.CLAVE_SUBGRUPO_ACTIVO] = "true";
            Assert.AreEqual("Lo usan las sugerencias de contacto al elegir un subgrupo.", j.Servicio().UsoSubgrupo());
        }

        // ---------------- Entrenar de verdad en .NET Framework ----------------

        [TestMethod]
        public void EntrenarEnNet48_ModeloPorSubgrupo_ComparaConElGeneralPisadoYGuardaUnZipQueLaApiPuntua()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo funciona en 64 bits y el proceso de los tests es de 32");
            }
            _ = CargadorLightGbmNativo.Asegurar();
            DateTime hoy = new DateTime(2026, 10, 9);
            var opciones = new OpcionesReentrenamiento
            {
                Completo = false, Vendedor = "MPP", Meses = 8, EvaluarAlternativas = false, ModeloGeneral = RutaModeloMuestra()
            };

            ProcesoReentrenamientoSubgrupo proceso = ProcesoReentrenamientoSubgrupo.Evaluar(DatosSinteticos(hoy), opciones, hoy);

            Assert.IsTrue(proceso.DatosSuficientes);
            Assert.IsTrue(proceso.HayModeloActual, "sin modelo por subgrupo, compara con el general pisado");
            Assert.AreEqual("hoy: modelo general con el subgrupo pisado", proceso.Comparado);
            Assert.IsFalse(double.IsNaN(proceso.Nuevo.Auc));
            Assert.IsNotNull(proceso.Actual);
            StringAssert.Contains(proceso.Decision.Motivo, "contra el hoy: modelo general con el subgrupo pisado");
            StringAssert.Contains(proceso.Readme(null, "no pasa", "a mano"), "Interino: P(pedido) × frecuencia del subgrupo");

            string ruta = Path.Combine(Path.GetTempPath(), "NestoAPI618_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                List<float> prueba = proceso.EntrenarFinalYGuardar(ruta);
                Assert.IsTrue(prueba.All(x => x >= 0 && x <= 1));

                ModeloContactoEntrada general = new ModeloContactoEntrada { ClienteId = "C0/0", TipoInteraccion = "Llamada", Mes = "10", DiaSemana = "5",
                    GrupoSubgrupoMasVendido = "COS001", Pedidos12Meses = 30, Importe12Meses = 3000, DiasDesdeUltimoPedido = 10, DiasEntrePedidos = 12,
                    ImporteMedioPedido = 100, TasaConversionCliente = 0.8f, ContactosPrevios = 20 };
                var comprador = new List<PedidoSubgrupoDia>();
                for (DateTime d = hoy.AddMonths(-12); d < hoy; d = d.AddDays(12))
                {
                    comprador.Add(new PedidoSubgrupoDia("C0/0", d, "COS001", 100));
                }
                List<float> p = ModeloContacto.PuntuarSubgrupo(new List<ModeloContactoSubgrupoEntrada>
                {
                    CalculadoraFeaturesSubgrupo.Calcular<ModeloContactoSubgrupoEntrada>(general, CalculadoraFeaturesSubgrupo.Resumir(comprador, hoy), "COS001"),
                    CalculadoraFeaturesSubgrupo.Calcular<ModeloContactoSubgrupoEntrada>(general, CalculadoraFeaturesSubgrupo.Resumir(comprador, hoy), "PEL001")
                }, ruta);
                Assert.AreEqual(2, p.Count);
                Assert.IsTrue(p[0] > p[1], $"lo que compra cada 12 días ({p[0]}) antes que lo que nunca compra ({p[1]})");
            }
            finally
            {
                try { File.Delete(ruta); } catch (IOException) { }
            }
        }

        /// <summary>
        /// 80 clientes de MPP llamados cada 10 días durante 2 años. Uno de cada tres compra COS001 cada 12 días (y COS002 uno de
        /// cada dos pedidos); otro de cada tres compra PEL001 cada 30 días; el resto nunca.
        /// </summary>
        private static DatosCrudos DatosSinteticos(DateTime hoy)
        {
            var datos = new DatosCrudos();
            DateTime inicio = hoy.AddMonths(-24);
            for (int i = 0; i < 80; i++)
            {
                string id = $"C{i}/0";
                for (DateTime d = inicio.AddDays(i % 10); d < hoy; d = d.AddDays(10))
                {
                    datos.Rapports.Add(new RapportCrudo(id, d.AddHours(10 + i % 8), "T", false, "MPP"));
                }
                int tipo = i % 3;
                int cada = tipo == 0 ? 12 : 30;
                if (tipo == 2)
                {
                    continue;
                }
                int n = 0;
                for (DateTime d = inicio.AddDays(i % cada); d < hoy; d = d.AddDays(cada), n++)
                {
                    datos.Pedidos.Add(new PedidoDia(id, d, 100 + i));
                    datos.PedidosSubgrupo.Add(new PedidoSubgrupoDia(id, d, tipo == 0 ? "COS001" : "PEL001", 80));
                    if (tipo == 0 && n % 2 == 0)
                    {
                        datos.PedidosSubgrupo.Add(new PedidoSubgrupoDia(id, d, "COS002", 20 + i));
                    }
                }
            }
            return datos;
        }
    }
}
