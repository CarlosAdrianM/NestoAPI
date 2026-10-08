using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>
    /// NestoAPI#603 c3b: la API calcula la entrada del modelo nuevo igual que el entrenamiento. Los casos de features son copia
    /// de ModeloLlamadaPedido.Tests\CalculadoraFeaturesTests.cs (xUnit → MSTest): si allí cambia el cálculo, aquí tienen que
    /// fallar. El zip de muestra (Recursos\modelo_llamadas_muestra_MPP_36m.zip) es un entrenamiento ligero de MPP solo para
    /// probar la carga; el de producción sale de la ejecución completa.
    /// </summary>
    [TestClass]
    public class ModeloContactoTests
    {
        private const string ID = "15191/0";
        private static readonly DateTime Dia = new DateTime(2026, 6, 15, 10, 30, 0); // lunes

        private static PedidoDiaContacto Pedido(DateTime dia, decimal importe = 100) => new PedidoDiaContacto(dia, importe);
        private static RapportContactoCrudo Rapport(DateTime fecha, bool pedido = false) => new RapportContactoCrudo(fecha, pedido);

        private static HistorialContacto Historial(IEnumerable<PedidoDiaContacto> pedidos = null, IEnumerable<RapportContactoCrudo> rapports = null,
            IEnumerable<VentaGrupoMesContacto> ventas = null, DateTime? ultimaInteraccion = null)
            => new HistorialContacto(ID, pedidos, rapports, ventas, ultimaInteraccion);

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

        // ---------------- Features (copiados de ModeloLlamadaPedido.Tests) ----------------

        [TestMethod]
        public void Features_NoUsaPedidosDelDiaDelContactoNiPosteriores()
        {
            var h = Historial(new[] { Pedido(Dia.Date.AddDays(-10)), Pedido(Dia.Date), Pedido(Dia.Date.AddDays(3)) });

            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(h, Dia, "Llamada");

            Assert.AreEqual(1f, f.Pedidos12Meses);
            Assert.AreEqual(10f, f.DiasDesdeUltimoPedido);
            Assert.AreEqual(100f, f.Importe12Meses);
        }

        [TestMethod]
        public void Features_SinHistorialSeCodificaConValoresFijos()
        {
            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(Historial(), Dia, "Llamada");

            Assert.AreEqual(1f, f.SinHistorial);
            Assert.AreEqual(0f, f.Pedidos12Meses);
            Assert.AreEqual(1000f, f.DiasDesdeUltimoPedido);
            Assert.AreEqual(1000f, f.DiasEntrePedidos);
            Assert.AreEqual(0f, f.ImporteMedioPedido);
            Assert.AreEqual(0f, f.TendenciaImporte);
            Assert.AreEqual(0.25f, f.TasaConversionCliente, 0.0005f);
            Assert.AreEqual(0f, f.ContactosPrevios);
            Assert.AreEqual("NADA", f.GrupoSubgrupoMasVendido);
        }

        [TestMethod]
        public void Features_PedidoDeHace18MesesNoEsSinHistorialPeroNoCuentaEn12Meses()
        {
            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(Historial(new[] { Pedido(Dia.Date.AddMonths(-18)) }), Dia, "Llamada");

            Assert.AreEqual(0f, f.SinHistorial);
            Assert.AreEqual(0f, f.Pedidos12Meses);
            Assert.AreEqual((float)(Dia.Date - Dia.Date.AddMonths(-18)).TotalDays, f.DiasDesdeUltimoPedido);
        }

        [TestMethod]
        public void Features_ImporteMedioTendenciaYMismoMesDelAnnoAnterior()
        {
            var h = Historial(new[]
            {
                Pedido(Dia.Date.AddMonths(-12).AddDays(5), 300), // mismo mes del año anterior, semestre B
                Pedido(Dia.Date.AddMonths(-2), 100)              // semestre A
            });

            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(h, Dia, "Visita");

            Assert.AreEqual(2f, f.Pedidos12Meses);
            Assert.AreEqual(1f, f.PedidosMismoMesAnnoAnterior);
            Assert.AreEqual(200f, f.ImporteMedioPedido);
            Assert.AreEqual((100f - 300f) / 400f, f.TendenciaImporte, 0.00005f);
            Assert.AreEqual(365f / 2, f.DiasEntrePedidos);
        }

        [TestMethod]
        public void Features_PositivoPorMarcaDePedidoOPorPedidoEnSieteDias()
        {
            var h = Historial(new[] { Pedido(Dia.Date.AddDays(7)) });
            Assert.IsTrue(CalculadoraFeaturesContacto.EsPositivo(Rapport(Dia), h));
            Assert.IsFalse(CalculadoraFeaturesContacto.EsPositivo(Rapport(Dia.AddDays(-1)), h)); // el pedido cae el día 8
            Assert.IsTrue(CalculadoraFeaturesContacto.EsPositivo(Rapport(Dia, pedido: true), Historial()));
            Assert.IsFalse(CalculadoraFeaturesContacto.EsPositivo(Rapport(Dia), Historial(new[] { Pedido(Dia.Date.AddDays(-1)) })));
        }

        [TestMethod]
        public void Features_TasaDeConversionSoloConContactosConLaVentanaCerrada()
        {
            var rapports = new[]
            {
                Rapport(Dia.AddDays(-30), pedido: true), // positivo, cerrado
                Rapport(Dia.AddDays(-20)),               // negativo, cerrado
                Rapport(Dia.AddDays(-5), pedido: true),  // ventana abierta: no se puede saber todavía
                Rapport(Dia)                             // el propio contacto
            };

            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(Historial(rapports: rapports), Dia, "Llamada");

            Assert.AreEqual(2f, f.ContactosPrevios);
            Assert.AreEqual((1 + 0.5f) / (2 + 2f), f.TasaConversionCliente, 0.00005f);
        }

        [TestMethod]
        public void Features_TasaDeConversionCuentaPedidosPosterioresAlContactoPrevio()
        {
            var h = Historial(new[] { Pedido(Dia.Date.AddDays(-28)) }, new[] { Rapport(Dia.AddDays(-30)) });

            CalculadoraFeaturesContacto.ConversionPrevia(h, Dia.Date, out int contactos, out int positivos);

            Assert.AreEqual(1, contactos);
            Assert.AreEqual(1, positivos);
        }

        [TestMethod]
        public void Features_GrupoMasVendidoIgnoraElMesEnCursoYEmpataPorOrden()
        {
            var ventas = new[]
            {
                new VentaGrupoMesContacto(202606, "ZZZZZZ", 999), // mes en curso: fuera
                new VentaGrupoMesContacto(202605, "COSCRE", 10),
                new VentaGrupoMesContacto(202601, "APAAPA", 10),
                new VentaGrupoMesContacto(202505, "MUYVIE", 50)   // hace 13 meses: fuera
            };

            Assert.AreEqual("APAAPA", CalculadoraFeaturesContacto.GrupoSubgrupoMasVendido(Historial(ventas: ventas), Dia));
        }

        [TestMethod]
        public void Features_CategoricasYTarde()
        {
            ModeloContactoEntrada f = CalculadoraFeaturesContacto.Calcular(Historial(), new DateTime(2026, 6, 14, 15, 0, 0), "WhatsApp");

            Assert.AreEqual("7", f.DiaSemana); // domingo
            Assert.AreEqual("6", f.Mes);
            Assert.AreEqual(1f, f.EsPorLaTarde);
            Assert.AreEqual("WhatsApp", f.TipoInteraccion);
            Assert.AreEqual(ID, f.ClienteId);
        }

        [TestMethod]
        public void Features_EntradasNormalizanElTipoDeInteraccion()
        {
            List<ModeloContactoEntrada> entradas = ModeloContacto.Entradas(new[] { Historial() }, Dia, "Teléfono");

            Assert.AreEqual("Llamada", entradas.Single().TipoInteraccion);
        }

        // ---------------- La clase de entrada es la del entrenamiento ----------------

        [TestMethod]
        public void Entrada_TieneExactamenteLasColumnasDelEntrenamiento()
        {
            var esperadas = new Dictionary<string, Type>
            {
                ["ClienteId"] = typeof(string), ["TipoInteraccion"] = typeof(string), ["Mes"] = typeof(string), ["DiaSemana"] = typeof(string),
                ["GrupoSubgrupoMasVendido"] = typeof(string), ["EsPorLaTarde"] = typeof(float), ["Pedidos12Meses"] = typeof(float),
                ["Importe12Meses"] = typeof(float), ["DiasDesdeUltimoPedido"] = typeof(float), ["DiasEntrePedidos"] = typeof(float),
                ["PedidosMismoMesAnnoAnterior"] = typeof(float), ["ImporteMedioPedido"] = typeof(float), ["TendenciaImporte"] = typeof(float),
                ["TasaConversionCliente"] = typeof(float), ["ContactosPrevios"] = typeof(float), ["SinHistorial"] = typeof(float)
            };

            Dictionary<string, Type> reales = typeof(ModeloContactoEntrada).GetProperties().ToDictionary(p => p.Name, p => p.PropertyType);

            CollectionAssert.AreEquivalent(esperadas.Keys.ToList(), reales.Keys.ToList());
            foreach (KeyValuePair<string, Type> e in esperadas)
            {
                Assert.AreEqual(e.Value, reales[e.Key], e.Key);
            }
        }

        // ---------------- Carga del zip y puntuación ----------------

        [TestMethod]
        public void Modelo_CargaElZipDeMuestraYPuntuaUnaFila()
        {
            var bueno = Historial(
                Enumerable.Range(1, 30).Select(i => Pedido(Dia.Date.AddDays(-12 * i), 150)),
                Enumerable.Range(1, 20).Select(i => Rapport(Dia.AddDays(-12 * i - 1))),
                new[] { new VentaGrupoMesContacto(202605, "COSCRE", 40) });
            var entradas = new List<ModeloContactoEntrada>
            {
                CalculadoraFeaturesContacto.Calcular(bueno, Dia, "Llamada"),
                CalculadoraFeaturesContacto.Calcular(Historial(), Dia, "Llamada")
            };

            List<float> probabilidades = ModeloContacto.Puntuar(entradas, RutaModeloMuestra());

            Assert.AreEqual(2, probabilidades.Count);
            Assert.IsTrue(probabilidades.All(p => p > 0f && p < 1f), string.Join("; ", probabilidades));
            Assert.IsTrue(probabilidades[0] > probabilidades[1],
                $"un cliente que compra cada 12 días puntúa más que uno sin historial ({probabilidades[0]} vs {probabilidades[1]})");
        }

        [TestMethod]
        public void Modelo_SinEntradas_NoCargaNada()
        {
            Assert.AreEqual(0, ModeloContacto.Puntuar(new List<ModeloContactoEntrada>(), "no-existe.zip").Count);
        }

        [TestMethod]
        public void Modelo_ConGrupoSubgrupo_CopiaSinTocarLasOriginales()
        {
            var originales = new List<ModeloContactoEntrada>
            {
                new ModeloContactoEntrada { ClienteId = "1/0", GrupoSubgrupoMasVendido = "COSCRE", DiasDesdeUltimoPedido = 12, TasaConversionCliente = 0.4f }
            };

            List<ModeloContactoEntrada> conGrupo = ModeloContacto.ConGrupoSubgrupo(originales, "PELTIN");
            List<ModeloContactoEntrada> sinGrupo = ModeloContacto.ConGrupoSubgrupo(originales, "");

            Assert.AreEqual("PELTIN", conGrupo[0].GrupoSubgrupoMasVendido);
            Assert.AreEqual(12f, conGrupo[0].DiasDesdeUltimoPedido);
            Assert.AreEqual(0.4f, conGrupo[0].TasaConversionCliente);
            Assert.AreEqual("COSCRE", sinGrupo[0].GrupoSubgrupoMasVendido);
            Assert.AreEqual("COSCRE", originales[0].GrupoSubgrupoMasVendido, "la caché no se pisa");
            Assert.AreNotSame(originales[0], sinGrupo[0]);
        }

        // ---------------- Sugerencias: del historial a PrediccionContacto ----------------

        private class RepositorioFalso : IRepositorioFeaturesContacto
        {
            private readonly List<HistorialContacto> historiales;
            public int Lecturas { get; private set; }
            public RepositorioFalso(List<HistorialContacto> historiales) { this.historiales = historiales; }
            public List<HistorialContacto> Leer(string vendedor, DateTime hoy)
            {
                Lecturas++;
                return historiales;
            }
        }

        [TestMethod]
        public void Probabilidades_DevuelveElGrupoRealAunquePuntueConElPreguntado_YNadaEsNull()
        {
            var conGrupo = new HistorialContacto("1/0", new[] { Pedido(Dia.Date.AddDays(-20)) }, null,
                new[] { new VentaGrupoMesContacto(202605, "COSCRE", 10) });
            var sinNada = new HistorialContacto("2/0", null, null, null);
            var repositorio = new RepositorioFalso(new List<HistorialContacto> { conGrupo, sinNada });
            string vendedor = "T603A" + Guid.NewGuid().ToString("N").Substring(0, 4); // caché estática: vendedor único
            var probabilidades = new ProbabilidadesContactoModelo(repositorio, () => Dia, RutaModeloMuestra());

            Dictionary<string, PrediccionContacto> resultado = probabilidades.Calcular(vendedor, "Llamada", "PELTIN", Dia);
            _ = probabilidades.Calcular(vendedor, "Visita", "", Dia);

            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual("COSCRE", resultado["1/0"].GrupoSubgrupoMasVendido);
            Assert.IsNull(resultado["2/0"].GrupoSubgrupoMasVendido);
            Assert.IsTrue(resultado["1/0"].Probabilidad > 0f && resultado["1/0"].Probabilidad < 1f);
            Assert.AreEqual(1, repositorio.Lecturas, "otro tipo o grupo no vuelve a la BD");
        }

        // ---------------- Endpoint antiguo (GetClientesProbabilidadVenta) ----------------

        [TestMethod]
        public void EndpointAntiguo_FiltraSieteYSeisDias_PisaElGrupoYOrdenaPorProbabilidad()
        {
            var historiales = new List<HistorialContacto>
            {
                new HistorialContacto("1/0", new[] { Pedido(Dia.Date.AddDays(-30)) }, null, null, Dia.AddDays(-10)),   // pasa
                new HistorialContacto("2/0", new[] { Pedido(Dia.Date.AddDays(-30)) }, null, null, Dia.AddDays(-3)),    // habló hace 3 días
                new HistorialContacto("3/0", new[] { Pedido(Dia.Date.AddDays(-2)) }, null, null, null),                 // pidió hace 2 días
                new HistorialContacto("4/0", null, null, null, null)                                                    // sin nada: pasa
            };
            IList<ModeloContactoEntrada> puntuadas = null;

            List<GestorClientes.ClientePuntuadoAntiguo> resultado = GestorClientes.PuntuarParaEndpointAntiguo(historiales, Dia, "", "PELTIN", 10,
                entradas => { puntuadas = entradas; return entradas.Select(e => e.ClienteId == "4/0" ? 0.9f : 0.3f).ToList(); });

            CollectionAssert.AreEqual(new[] { "4/0", "1/0" }, resultado.Select(r => r.ClienteId).ToArray());
            Assert.IsTrue(puntuadas.All(e => e.GrupoSubgrupoMasVendido == "PELTIN" && e.TipoInteraccion == "Llamada"));
            Assert.AreEqual(9999, resultado[0].DiasDesdeUltimaInteraccion);
            Assert.AreEqual(1000, resultado[0].DiasDesdeUltimoPedido);
            Assert.AreEqual(10, resultado[1].DiasDesdeUltimaInteraccion);
            Assert.AreEqual(30, resultado[1].DiasDesdeUltimoPedido);
            Assert.AreEqual(0.3f, resultado[1].Probabilidad);
        }

        [TestMethod]
        public void EndpointAntiguo_RespetaElNumero()
        {
            var historiales = Enumerable.Range(1, 5).Select(i => new HistorialContacto($"{i}/0", null, null, null)).ToList();

            List<GestorClientes.ClientePuntuadoAntiguo> resultado = GestorClientes.PuntuarParaEndpointAntiguo(historiales, Dia, "Visita", "", 2,
                entradas => entradas.Select((e, i) => i / 10f).ToList());

            CollectionAssert.AreEqual(new[] { "5/0", "4/0" }, resultado.Select(r => r.ClienteId).ToArray());
        }

        // ---------------- Consulta ----------------

        [TestMethod]
        public void Consulta_VendedorComoParametro_FechasDelHistorialYTimeout()
        {
            string vendedorMalicioso = "PA'; DROP TABLE Clientes; --";

            using (SqlCommand comando = RepositorioFeaturesContactoSql.CrearComando(new SqlConnection(), vendedorMalicioso, new DateTime(2026, 10, 7, 9, 30, 0)))
            {
                Assert.IsFalse(comando.CommandText.Contains("DROP TABLE"));
                Assert.IsFalse(comando.CommandText.Contains("{"), "nada interpolado");
                Assert.AreEqual(vendedorMalicioso, comando.Parameters["@Vendedor"].Value);
                Assert.AreEqual(SqlDbType.VarChar, comando.Parameters["@Vendedor"].SqlDbType);
                Assert.AreEqual(new DateTime(2026, 10, 7), comando.Parameters["@Hoy"].Value);
                Assert.AreEqual(new DateTime(2024, 10, 6), comando.Parameters["@DesdeHistorial"].Value, "24 meses y un día de margen");
                Assert.AreEqual(new DateTime(2026, 10, 1), comando.Parameters["@InicioMes"].Value);
                Assert.AreEqual(new DateTime(2025, 10, 1), comando.Parameters["@DesdeVentas"].Value);
                Assert.AreEqual(120, comando.CommandTimeout);
            }
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL, "INTO #Clientes");
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL, "l.Estado >= -1");
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL, "s.Tipo IN ('T', 'V', 'W')");
        }

        [TestMethod]
        public void Consulta_AgruparIncluyeClientesSinHistorial()
        {
            List<HistorialContacto> historiales = RepositorioFeaturesContactoSql.Agrupar(
                new[] { "1/0", "2/0" },
                new[] { new KeyValuePair<string, RapportContactoCrudo>("1/0", Rapport(Dia.AddDays(-20))) },
                new[] { new KeyValuePair<string, PedidoDiaContacto>("1/0", Pedido(Dia.Date.AddDays(-5), 10)), new KeyValuePair<string, PedidoDiaContacto>("1/0", Pedido(Dia.Date.AddDays(-5), 15)) },
                new KeyValuePair<string, VentaGrupoMesContacto>[0],
                new Dictionary<string, DateTime> { ["1/0"] = Dia.AddDays(-20) });

            Assert.AreEqual(2, historiales.Count);
            HistorialContacto uno = historiales.Single(h => h.ClienteId == "1/0");
            Assert.AreEqual(1, uno.Pedidos.Count, "un pedido = un día");
            Assert.AreEqual(25m, uno.Pedidos[0].Importe);
            Assert.AreEqual(Dia.AddDays(-20), uno.UltimaInteraccion);
            Assert.AreEqual(0, historiales.Single(h => h.ClienteId == "2/0").Pedidos.Count);
        }

        [TestMethod]
        public void Cartera_LosPedidosSeCuentanPorNumeroDePedidoYSuFecha_NoPorDiasDeAlbaran()
        {
            // 31931 (JGP): un pedido servido en dos entregas contaba dos veces y el «último pedido» era el último albarán.
            string sql = RepositorioCarteraContactoSql.SQL_CARTERA;

            Assert.IsFalse(sql.Contains("[Fecha Albarán]"), "los días de albarán ya no cuentan como pedidos");
            StringAssert.Contains(sql, "INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número");
            StringAssert.Contains(sql, "GROUP BY l.[Nº Cliente], l.Contacto, l.Número, CAST(cab.Fecha AS date)");
            StringAssert.Contains(sql, "AND cab.Fecha >= @Hace24Meses", "la ventana va por la fecha del pedido");
            StringAssert.Contains(sql, "l.Estado = 4 AND l.[Base Imponible] > 0 AND l.SubGrupo <> 'MMP'", "mismos filtros de línea facturada");
        }

        [TestMethod]
        public void CarteraYModelo_ExcluyenLosClientesDeUnaSolaCompraPorAmazon()
        {
            // Estado 95 («Una sola compra, por Amazon»): no es cartera del vendedor, no se sugiere llamarlo.
            const string filtro = "c.Estado NOT IN (7, 67, 95)";

            StringAssert.Contains(RepositorioCarteraContactoSql.SQL_CARTERA, filtro);
            StringAssert.Contains(RepositorioFeaturesContactoSql.SQL, filtro);
            Assert.AreEqual(95, NestoAPI.Models.Constantes.Clientes.Estados.UNA_SOLA_COMPRA_AMAZON);
        }
    }
}
