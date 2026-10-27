using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloSugerenciasProductos.Aceptacion;
using ModeloSugerenciasProductos.Features;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#671 (corte 2e): modelo 2 de las sugerencias (aceptación) en sombra: instantánea de features, tasas suavizadas,
    /// bayesiano, features sin fuga, partición temporal, umbral de datos, métricas (AUC, precisión@1), puerta, el proceso de
    /// entrenamiento (uno de verdad), el ranking de la API y el job semanal. Datos inventados; sin BD.
    /// </summary>
    [TestClass]
    public class SugerenciasProductosAceptacionTests
    {
        private static readonly DateTime INICIO = new DateTime(2026, 11, 2, 10, 0, 0);
        private string carpeta;

        [TestInitialize]
        public void Inicializar()
        {
            carpeta = Path.Combine(Path.GetTempPath(), "NestoAPI671e_" + Guid.NewGuid().ToString("N"));
        }

        [TestCleanup]
        public void Limpiar()
        {
            try
            {
                if (Directory.Exists(carpeta))
                {
                    Directory.Delete(carpeta, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static MostradaAceptacion M(int id, string sesion, string producto, string respuesta, string motivo = "LeToca", string vendedor = "ASH",
            decimal p1 = 0.3m, string version = "m1-test", DateTime? fecha = null, bool? entro = null, string features = null, string cliente = "1001", byte posicion = 1)
            => new MostradaAceptacion
            {
                Id = id,
                SesionId = sesion,
                Producto = producto,
                Motivo = motivo,
                Fuentes = motivo,
                Puntuacion = p1,
                Posicion = posicion,
                VersionMotor = version,
                FechaMostrada = fecha ?? INICIO.AddMinutes(id),
                Respuesta = respuesta,
                EntroEnPedido = entro,
                TamanoCarrito = 2,
                Vendedor = vendedor,
                Aplicacion = "Nesto",
                Cliente = cliente,
                Features = features
            };

        // ---------------- Instantánea ----------------

        [TestMethod]
        public void Instantanea_SerializarYLeer_DevuelveLasMismasFeatures()
        {
            var e = new ModeloSugerenciaEntrada { Cliente = "1001", Producto = "P1", Mes = "11", EstadoCliente = "6", LiftCompradoJunto = 3.25f, RatioLeToca = 1.5f, DiasDesdeUltimaCompraProducto = ModeloSugerenciaEntrada.SIN_DATO, RangoAfinidad = 51 };

            string texto = InstantaneaFeatures.Serializar(e);
            ModeloSugerenciaEntrada leida = InstantaneaFeatures.Leer(texto);

            string[] claves = texto.Split('|').Select(p => p.Split('=')[0]).ToArray();
            CollectionAssert.DoesNotContain(claves, "Cliente", "Cliente y Producto ya están en la fila");
            CollectionAssert.DoesNotContain(claves, "Producto");
            Assert.AreEqual("11", leida.Mes);
            Assert.AreEqual("6", leida.EstadoCliente);
            Assert.AreEqual(3.25f, leida.LiftCompradoJunto);
            Assert.AreEqual(1.5f, leida.RatioLeToca);
            Assert.AreEqual(ModeloSugerenciaEntrada.SIN_DATO, leida.DiasDesdeUltimaCompraProducto);
            Assert.AreEqual(51f, leida.RangoAfinidad);
            Assert.AreEqual(0f, leida.Afinidad, "Los ceros no se escriben y vuelven como 0");
        }

        [TestMethod]
        public void Instantanea_TextoVacioORaro_NoLanza()
        {
            Assert.IsNull(InstantaneaFeatures.Leer(null));
            Assert.IsNull(InstantaneaFeatures.Leer("  "));
            ModeloSugerenciaEntrada e = InstantaneaFeatures.Leer("NoExiste=3|LiftCompradoJunto=abc|=4|RatioLeToca=2|sinigual");
            Assert.AreEqual(2f, e.RatioLeToca);
            Assert.AreEqual(0f, e.LiftCompradoJunto);
        }

        // ---------------- Tasas y bayesiano ----------------

        [TestMethod]
        public void Tasas_SinRespuestas_TodoEsElPrior()
        {
            var t = new TasasAceptacion();
            Assert.AreEqual(TasasAceptacion.PRIOR_GLOBAL, t.Global, 1e-9);
            Assert.AreEqual(TasasAceptacion.PRIOR_GLOBAL, t.Producto("X", "LeToca"), 1e-9);
            Assert.AreEqual(TasasAceptacion.PRIOR_GLOBAL, t.Vendedor("ASH"), 1e-9);
        }

        [TestMethod]
        public void Tasas_ProductoMuyAceptado_SubeSobreSuMotivo_YSoloCuentanAnadidoYNoLeInteresa()
        {
            var filas = new List<MostradaAceptacion>();
            for (int i = 0; i < 10; i++) filas.Add(M(i, "s" + i, "BUENO", RespuestasAceptacion.ANADIDO));
            for (int i = 10; i < 20; i++) filas.Add(M(i, "s" + i, "MALO", RespuestasAceptacion.NO_LE_INTERESA));
            filas.Add(M(30, "s30", "BUENO", "DescartadoVendedor"));
            filas.Add(M(31, "s31", "BUENO", "Sustituida"));
            filas.Add(M(32, "s32", "BUENO", null, entro: true));

            TasasAceptacion t = TasasAceptacion.Desde(filas);

            Assert.AreEqual(20, t.Respondidas, "Ni el descarte del vendedor, ni la sustituida, ni la señal débil");
            Assert.AreEqual(10, t.RespondidasProducto("bueno"));
            Assert.IsTrue(t.Producto("BUENO", "LeToca") > t.Motivo("LeToca"));
            Assert.IsTrue(t.Producto("MALO", "LeToca") < t.Motivo("LeToca"));
            Assert.AreEqual(t.Motivo("LeToca"), t.Producto("NUNCA_VISTO", "LeToca"), 1e-9, "Sin respuestas, la del motivo");
        }

        [TestMethod]
        public void Bayesiano_SinRespuestas_DevuelveP1_YConRespuestasPremiaLoAceptado()
        {
            var vacias = new TasasAceptacion();
            Assert.AreEqual(0.4f, ReordenadorBayesiano.Puntuar(0.4f, vacias, "X", "LeToca"), 1e-6f);

            TasasAceptacion t = TasasAceptacion.Desde(Enumerable.Range(0, 10).Select(i => M(i, "s" + i, "BUENO", RespuestasAceptacion.ANADIDO))
                .Concat(Enumerable.Range(10, 10).Select(i => M(i, "s" + i, "MALO", RespuestasAceptacion.NO_LE_INTERESA))));
            Assert.IsTrue(ReordenadorBayesiano.Puntuar(0.3f, t, "BUENO", "LeToca") > ReordenadorBayesiano.Puntuar(0.3f, t, "MALO", "LeToca"));
        }

        // ---------------- Features ----------------

        [TestMethod]
        public void Features_CopiaFuentesP1YLaInstantanea()
        {
            MostradaAceptacion m = M(1, "s", "P", null, motivo: "CompradoJunto", p1: 0.42m, posicion: 3);
            m.Fuentes = "CompradoJunto,Parecidos,Perfil";
            var inst = new ModeloSugerenciaEntrada { LiftCompradoJunto = 4f, RatioLeToca = 0f, EstadoCliente = "6", DiasDesdeUltimaCompraProducto = 30 };

            ModeloAceptacionEntrada e = CalculadoraFeaturesAceptacion.Calcular<ModeloAceptacionEntrada>(m, inst, null);

            Assert.AreEqual("COMPRADOJUNTO", e.Motivo);
            Assert.AreEqual("ASH", e.Vendedor);
            Assert.AreEqual("6", e.EstadoCliente);
            Assert.AreEqual(0.42f, e.P1, 1e-6f);
            Assert.AreEqual(1f, e.TieneP1);
            Assert.AreEqual(3f, e.Posicion);
            Assert.AreEqual(3f, e.NumeroFuentes);
            Assert.AreEqual(1f, e.FuenteCompradoJunto);
            Assert.AreEqual(1f, e.FuenteParecidos);
            Assert.AreEqual(1f, e.FuentePerfil);
            Assert.AreEqual(0f, e.FuenteLeToca);
            Assert.AreEqual(1f, e.TieneInstantanea);
            Assert.AreEqual(4f, e.LiftCompradoJunto);
            Assert.AreEqual(30f, e.DiasDesdeUltimaCompraProducto);
            Assert.AreEqual("11", e.Mes);
            Assert.AreEqual((float)TasasAceptacion.PRIOR_GLOBAL, e.TasaProducto, 1e-6f);
        }

        [TestMethod]
        public void Features_DeLasReglas_SinP1_YSinInstantanea_DiasSinDato()
        {
            MostradaAceptacion m = M(1, "s", "P", null, p1: 1.8m, version: "reglas-1");
            m.Fuentes = null;
            m.Posicion = 0;

            ModeloAceptacionEntrada e = CalculadoraFeaturesAceptacion.Calcular<ModeloAceptacionEntrada>(m, null, new TasasAceptacion());

            Assert.AreEqual(0f, e.P1, "La puntuación de las reglas no es P1");
            Assert.AreEqual(0f, e.TieneP1);
            Assert.AreEqual(1f, e.NumeroFuentes, "Sin Fuentes, la del motivo");
            Assert.AreEqual(1f, e.FuenteLeToca);
            Assert.AreEqual(1f, e.Posicion, "Posición 0 (la API) cuenta como 1");
            Assert.AreEqual(0f, e.TieneInstantanea);
            Assert.AreEqual(ModeloSugerenciaEntrada.SIN_DATO, e.DiasDesdeUltimaCompraProducto);
            Assert.AreEqual(CalculadoraFeaturesAceptacion.NADA, e.EstadoCliente);
        }

        [TestMethod]
        public void ConstruirFilas_CadaFilaSoloVeLasRespuestasAnteriores_SinFuga()
        {
            var mostradas = new List<MostradaAceptacion>
            {
                M(2, "b", "P", RespuestasAceptacion.NO_LE_INTERESA, fecha: INICIO.AddHours(2)),
                M(1, "a", "P", RespuestasAceptacion.ANADIDO, fecha: INICIO.AddHours(1)),
                M(3, "c", "P", RespuestasAceptacion.ANADIDO, fecha: INICIO.AddHours(3))
            };

            List<FilaAceptacion> filas = ProcesoAceptacion.ConstruirFilas(mostradas, 0f);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, filas.Select(f => f.Sesion).ToArray(), "Por fecha");
            CollectionAssert.AreEqual(new[] { 0f, 1f, 2f }, filas.Select(f => f.RespuestasProducto).ToArray(), "Su propia respuesta no cuenta");
            Assert.AreEqual((float)TasasAceptacion.PRIOR_GLOBAL, filas[0].TasaProducto, 1e-6f);
            Assert.IsTrue(filas[1].TasaProducto > filas[0].TasaProducto, "Tras un «Añadido», sube");
            CollectionAssert.AreEqual(new[] { true, false, true }, filas.Select(f => f.Label).ToArray());
        }

        [TestMethod]
        public void ConstruirFilas_SenalDebil_PositivaConMenosPeso_YConPesoCeroNoEntra_YLoNoTocadoNunca()
        {
            var mostradas = new List<MostradaAceptacion>
            {
                M(1, "a", "P", RespuestasAceptacion.ANADIDO),
                M(2, "a", "Q", null, entro: true),
                M(3, "a", "R", null, entro: false),
                M(4, "a", "S", null),
                M(5, "a", "T", "DescartadoVendedor")
            };

            List<FilaAceptacion> conDebiles = ProcesoAceptacion.ConstruirFilas(mostradas, 0.25f);
            List<FilaAceptacion> sinDebiles = ProcesoAceptacion.ConstruirFilas(mostradas, 0f);

            Assert.AreEqual(2, conDebiles.Count);
            FilaAceptacion debil = conDebiles.Single(f => f.Producto == "Q");
            Assert.IsTrue(debil.Label);
            Assert.AreEqual(0.25f, debil.Peso);
            Assert.IsFalse(debil.Respondida);
            Assert.AreEqual(1, sinDebiles.Count);
        }

        [TestMethod]
        public void Partir_LasUltimasSesionesValidan_YEnLaValidacionSoloRespuestas()
        {
            var mostradas = new List<MostradaAceptacion>();
            for (int s = 0; s < 8; s++)
            {
                mostradas.Add(M(s * 10 + 1, "s" + s, "P" + s, s % 2 == 0 ? RespuestasAceptacion.ANADIDO : RespuestasAceptacion.NO_LE_INTERESA, fecha: INICIO.AddDays(s)));
                mostradas.Add(M(s * 10 + 2, "s" + s, "Q" + s, null, entro: true, fecha: INICIO.AddDays(s).AddMinutes(1)));
            }
            List<FilaAceptacion> filas = ProcesoAceptacion.ConstruirFilas(mostradas, 0.25f);

            (List<FilaAceptacion> entrenamiento, List<FilaAceptacion> validacion, DateTime? corte) = ProcesoAceptacion.Partir(filas, 0.25);

            Assert.AreEqual(INICIO.AddDays(6), corte);
            CollectionAssert.AreEquivalent(new[] { "s6", "s7" }, validacion.Select(f => f.Sesion).Distinct().ToArray());
            Assert.IsTrue(validacion.All(f => f.Respondida), "Las señales débiles no se validan");
            Assert.IsTrue(entrenamiento.All(f => f.Fecha < corte.Value));
            Assert.AreEqual(12, entrenamiento.Count, "6 sesiones × (respuesta + señal débil)");
        }

        // ---------------- Umbral ----------------

        private static List<MostradaAceptacion> Respuestas(int respondidas, int anadidos, int vendedores)
            => Enumerable.Range(0, respondidas).Select(i => M(i, "s" + i, "P" + (i % 50), i < anadidos ? RespuestasAceptacion.ANADIDO : RespuestasAceptacion.NO_LE_INTERESA,
                vendedor: "V" + (i % vendedores))).ToList();

        [TestMethod]
        public void Umbral_ConMilRespuestas150AnadidosYTresVendedores_Suficientes()
        {
            UmbralDatosAceptacion.Resultado r = UmbralDatosAceptacion.Comprobar(Respuestas(1000, 150, 3), new OpcionesAceptacion());
            Assert.IsTrue(r.Suficientes, r.Motivo);
            Assert.AreEqual(1000, r.Respondidas);
            Assert.AreEqual(150, r.Anadidos);
            Assert.AreEqual(3, r.Vendedores);
        }

        [TestMethod]
        public void Umbral_PorDebajoDeCualquieraDeLosTres_Insuficientes_YDiceQueFalta()
        {
            UmbralDatosAceptacion.Resultado pocas = UmbralDatosAceptacion.Comprobar(Respuestas(999, 150, 3), null);
            UmbralDatosAceptacion.Resultado pocosAnadidos = UmbralDatosAceptacion.Comprobar(Respuestas(1000, 149, 3), null);
            UmbralDatosAceptacion.Resultado pocosVendedores = UmbralDatosAceptacion.Comprobar(Respuestas(1000, 150, 2), null);

            Assert.IsFalse(pocas.Suficientes);
            StringAssert.Contains(pocas.Motivo, "999 respuestas de 1000");
            Assert.IsFalse(pocosAnadidos.Suficientes);
            StringAssert.Contains(pocosAnadidos.Motivo, "149 «Añadido» de 150");
            Assert.IsFalse(pocosVendedores.Suficientes);
            StringAssert.Contains(pocosVendedores.Motivo, "2 vendedores de 3");
        }

        [TestMethod]
        public void Umbral_HoyEnProduccion_21RespuestasNoLlegan()
        {
            // 27/10/26: 8 «Añadido» y 13 «No le interesa» de 7 vendedores (y 145 señales débiles, que no cuentan)
            List<MostradaAceptacion> hoy = Respuestas(21, 8, 7);
            hoy.AddRange(Enumerable.Range(100, 145).Select(i => M(i, "d" + i, "D", null, entro: true)));
            Assert.IsFalse(UmbralDatosAceptacion.Comprobar(hoy, new OpcionesAceptacion()).Suficientes);
        }

        // ---------------- Métricas y puerta ----------------

        [TestMethod]
        public void Auc_PerfectoInversoYEmpates()
        {
            Assert.AreEqual(1.0, MetricasAceptacion.Auc(new[] { (0.9, true), (0.8, true), (0.2, false), (0.1, false) }), 1e-9);
            Assert.AreEqual(0.0, MetricasAceptacion.Auc(new[] { (0.1, true), (0.9, false) }), 1e-9);
            Assert.AreEqual(0.5, MetricasAceptacion.Auc(new[] { (0.5, true), (0.5, false), (0.5, true), (0.5, false) }), 1e-9);
            Assert.AreEqual(0.75, MetricasAceptacion.Auc(new[] { (0.9, true), (0.5, false), (0.4, true), (0.1, false) }), 1e-9);
            Assert.IsTrue(double.IsNaN(MetricasAceptacion.Auc(new[] { (0.9, true), (0.4, true) })), "Sin negativos");
        }

        [TestMethod]
        public void PrecisionPrimera_SoloSesionesConDosRespuestasYAlgunAnadido()
        {
            var filas = new List<FilaAceptacion>
            {
                new FilaAceptacion { Sesion = "a", Label = true, PuntuacionActual = 0.9, Posicion = 1 },
                new FilaAceptacion { Sesion = "a", Label = false, PuntuacionActual = 0.1, Posicion = 2 },
                new FilaAceptacion { Sesion = "b", Label = false, PuntuacionActual = 0.9, Posicion = 1 },
                new FilaAceptacion { Sesion = "b", Label = true, PuntuacionActual = 0.1, Posicion = 2 },
                new FilaAceptacion { Sesion = "c", Label = false, PuntuacionActual = 0.9, Posicion = 1 },
                new FilaAceptacion { Sesion = "c", Label = false, PuntuacionActual = 0.1, Posicion = 2 },
                new FilaAceptacion { Sesion = "d", Label = true, PuntuacionActual = 0.5, Posicion = 1 }
            };

            (int sesiones, double precision) = MetricasAceptacion.PrecisionPrimera(filas, f => f.PuntuacionActual);

            Assert.AreEqual(2, sesiones, "c no tiene «Añadido» y d solo tiene una respuesta");
            Assert.AreEqual(0.5, precision, 1e-9);
        }

        private static ResultadoAceptacion R(double auc, int filas = 300, int positivos = 60, int sesiones = 0, double precision = double.NaN)
            => new ResultadoAceptacion { Filas = filas, Positivos = positivos, Auc = auc, SesionesPrecision = sesiones, PrecisionPrimera = precision };

        [TestMethod]
        public void Puerta_GanaAP1PorDosCentesimas_Pasa()
        {
            DecisionAceptacion d = MetricasAceptacion.Decidir(R(0.62), R(0.60), R(0.61), new OpcionesAceptacion());
            Assert.IsTrue(d.Pasa, d.Motivo);
        }

        [TestMethod]
        public void Puerta_NoGanaPorLoBastante_NoPasa()
        {
            DecisionAceptacion d = MetricasAceptacion.Decidir(R(0.615), R(0.60), null, new OpcionesAceptacion());
            Assert.IsFalse(d.Pasa);
            StringAssert.Contains(d.Motivo, "no gana");
        }

        [TestMethod]
        public void Puerta_ValidacionPequena_NoPasaAunqueGane()
        {
            Assert.IsFalse(MetricasAceptacion.Decidir(R(0.9, filas: 199), R(0.5, filas: 199), null, new OpcionesAceptacion()).Pasa);
            Assert.IsFalse(MetricasAceptacion.Decidir(R(0.9, positivos: 29), R(0.5, positivos: 29), null, new OpcionesAceptacion()).Pasa);
        }

        [TestMethod]
        public void Puerta_PeorQueElBayesiano_NoPasa()
        {
            DecisionAceptacion d = MetricasAceptacion.Decidir(R(0.65), R(0.60), R(0.70), new OpcionesAceptacion());
            Assert.IsFalse(d.Pasa);
            StringAssert.Contains(d.Motivo, "bayesiano");
        }

        [TestMethod]
        public void Puerta_ConSesionesSuficientes_LaPrecisionArribaNoPuedeBajar()
        {
            DecisionAceptacion baja = MetricasAceptacion.Decidir(R(0.70, sesiones: 25, precision: 0.40), R(0.60, sesiones: 25, precision: 0.48), null, new OpcionesAceptacion());
            DecisionAceptacion pocas = MetricasAceptacion.Decidir(R(0.70, sesiones: 10, precision: 0.40), R(0.60, sesiones: 10, precision: 0.48), null, new OpcionesAceptacion());
            Assert.IsFalse(baja.Pasa);
            StringAssert.Contains(baja.Motivo, "precisión@1");
            Assert.IsTrue(pocas.Pasa, "Con menos de 20 sesiones la precisión@1 no decide");
        }

        // ---------------- Proceso ----------------

        [TestMethod]
        public void Proceso_ConPocasRespuestas_NiEntrena_YElInformeLoDice()
        {
            ProcesoAceptacion p = ProcesoAceptacion.Evaluar(Respuestas(21, 8, 7));

            Assert.IsFalse(p.DatosSuficientes);
            Assert.IsFalse(p.Decision.Pasa);
            Assert.IsNull(p.Modelo);
            string informe = p.Informe();
            StringAssert.Contains(informe, "NO se entrena");
            StringAssert.Contains(informe, "## Tasa de aceptación");
            Assert.ThrowsException<InvalidOperationException>(() => p.EntrenarFinalYGuardar(Path.Combine(carpeta, "x.zip")));
        }

        /// <summary>
        /// Datos sintéticos donde la aceptación depende del motivo y de lo pasado que está «le toca» (en la instantánea), y P1 no
        /// sabe nada de eso: el modelo 2 tiene que aprenderlo y ganar a P1 fuera de muestra.
        /// </summary>
        internal static List<MostradaAceptacion> Sinteticos(int sesiones, int semilla = 671)
        {
            var azar = new Random(semilla);
            var filas = new List<MostradaAceptacion>();
            string[] vendedores = { "ASH", "PA", "IM", "DLS", "CAM" };
            int id = 0;
            for (int s = 0; s < sesiones; s++)
            {
                DateTime fecha = INICIO.AddHours(s * 2);
                string vendedor = vendedores[s % vendedores.Length];
                for (int k = 0; k < 3; k++)
                {
                    bool leToca = azar.NextDouble() < 0.5;
                    float ratio = leToca ? (float)(0.8 + azar.NextDouble() * 1.6) : 0f;
                    double prob = leToca ? (ratio > 1.4f ? 0.75 : 0.35) : 0.12;
                    bool aceptada = azar.NextDouble() < prob;
                    var inst = new ModeloSugerenciaEntrada { RatioLeToca = ratio, EsLeToca = leToca ? 1 : 0, Mes = fecha.Month.ToString() };
                    filas.Add(M(++id, "S" + s, "P" + azar.Next(200), aceptada ? RespuestasAceptacion.ANADIDO : RespuestasAceptacion.NO_LE_INTERESA,
                        motivo: leToca ? "LeToca" : "Parecidos", vendedor: vendedor, p1: (decimal)Math.Round(azar.NextDouble(), 4),
                        fecha: fecha.AddMinutes(k), features: InstantaneaFeatures.Serializar(inst), cliente: "C" + azar.Next(300), posicion: (byte)(k + 1)));
                }
            }
            return filas;
        }

        [TestMethod]
        public void Proceso_EntrenaDeVerdad_GanaAP1EnLaValidacion_GuardaYElRankingLoUsa()
        {
            _ = CargadorLightGbmNativo.Asegurar();
            List<MostradaAceptacion> datos = Sinteticos(500);

            ProcesoAceptacion p = ProcesoAceptacion.Evaluar(datos, new OpcionesAceptacion());

            Assert.IsTrue(p.DatosSuficientes, p.Umbral.Motivo);
            Assert.IsTrue(p.Validacion.Count >= 300, "La cuarta parte de las sesiones");
            Assert.IsTrue(p.Entrenamiento.All(f => f.Fecha < p.Corte.Value), "Partición temporal");
            Assert.IsTrue(p.Modelo.Auc > 0.65, $"AUC del modelo {p.Modelo.Auc}");
            Assert.IsTrue(Math.Abs(p.Actual.Auc - 0.5) < 0.1, $"P1 es azar aquí: {p.Actual.Auc}");
            Assert.IsTrue(p.Decision.Pasa, p.Decision.Motivo);

            string zip = Path.Combine(carpeta, RankingAceptacionSugerencias.PREFIJO_FICHERO + "20261101_0330.zip");
            p.EntrenarFinalYGuardar(zip);
            File.WriteAllText(Path.ChangeExtension(zip, ".json"), JsonConvert.SerializeObject(p.Metadatos(INICIO, "test", zip)));
            var ranking = new RankingAceptacionSugerencias(() => ModoRankingAceptacion.Activo, () => carpeta, () => datos, ex => throw ex, null);

            var candidatos = new List<MostradaAceptacion> { M(0, "nueva", "X", null, motivo: "LeToca"), M(0, "nueva", "Y", null, motivo: "Parecidos") };
            var instantaneas = new List<ModeloSugerenciaEntrada> { new ModeloSugerenciaEntrada { RatioLeToca = 2f, EsLeToca = 1 }, new ModeloSugerenciaEntrada() };
            PuntuacionAceptacion r = ranking.Puntuar(candidatos, instantaneas);

            Assert.IsNotNull(r);
            Assert.IsTrue(r.EsModeloEntrenado);
            Assert.AreEqual("m2-20261101_0330", r.Version);
            Assert.IsTrue(r.Puntuaciones[0] > r.Puntuaciones[1], "Le toca muy pasado > parecidos");
        }

        // ---------------- Ranking de la API ----------------

        [TestMethod]
        public void Ranking_LeerModo_PorDefectoSombra()
        {
            Assert.AreEqual(ModoRankingAceptacion.Sombra, RankingAceptacionSugerencias.LeerModo(_ => null));
            Assert.AreEqual(ModoRankingAceptacion.Sombra, RankingAceptacionSugerencias.LeerModo(_ => "loquesea"));
            Assert.AreEqual(ModoRankingAceptacion.Sombra, RankingAceptacionSugerencias.LeerModo(_ => "7"));
            Assert.AreEqual(ModoRankingAceptacion.Activo, RankingAceptacionSugerencias.LeerModo(_ => " activo "));
            Assert.AreEqual(ModoRankingAceptacion.Apagado, RankingAceptacionSugerencias.LeerModo(_ => "Apagado"));
        }

        private void Modelo(string sufijo, bool pasa, int version = ModeloAceptacionEntrada.VERSION_FEATURES, bool conZip = true)
        {
            _ = Directory.CreateDirectory(carpeta);
            string json = Path.Combine(carpeta, RankingAceptacionSugerencias.PREFIJO_FICHERO + sufijo + ".json");
            File.WriteAllText(json, JsonConvert.SerializeObject(new MetadatosModeloAceptacion { PasaPuerta = pasa, VersionFeatures = version }));
            if (conZip)
            {
                File.WriteAllText(Path.ChangeExtension(json, ".zip"), "zip");
            }
        }

        [TestMethod]
        public void Ranking_ElegirModelo_ElUltimoQuePasaLaPuertaConSuVersionYSuZip()
        {
            Assert.IsNull(RankingAceptacionSugerencias.ElegirModelo(carpeta), "Sin carpeta");
            Modelo("20261101_0330", pasa: true);
            Modelo("20261108_0330", pasa: false);
            Modelo("20261115_0330", pasa: true, version: ModeloAceptacionEntrada.VERSION_FEATURES + 1);
            Modelo("20261122_0330", pasa: true, conZip: false);
            File.WriteAllText(Path.Combine(carpeta, "modelo_sugerencias_productos_20261130_0230.json"), "{\"PasaPuerta\":true}");

            Assert.AreEqual(RankingAceptacionSugerencias.PREFIJO_FICHERO + "20261101_0330.zip", Path.GetFileName(RankingAceptacionSugerencias.ElegirModelo(carpeta)));
        }

        [TestMethod]
        public void Ranking_SinModelo_PuntuaConElBayesiano_QueNoOrdena()
        {
            var ranking = new RankingAceptacionSugerencias(() => ModoRankingAceptacion.Activo, () => carpeta, () => new List<MostradaAceptacion>(), ex => throw ex, null);

            PuntuacionAceptacion r = ranking.Puntuar(new[] { M(0, "s", "X", null, p1: 0.5m), M(0, "s", "Y", null, p1: 0.2m) }, null);

            Assert.AreEqual(ReordenadorBayesiano.VERSION, r.Version);
            Assert.IsFalse(r.EsModeloEntrenado);
            CollectionAssert.AreEqual(new[] { 0.5f, 0.2f }, r.Puntuaciones.ToArray(), "Sin respuestas, P1");
        }

        [TestMethod]
        public void Ranking_Apagado_NoPuntua_NiLeeLasRespuestas()
        {
            int lecturas = 0;
            var ranking = new RankingAceptacionSugerencias(() => ModoRankingAceptacion.Apagado, () => carpeta, () => { lecturas++; return new List<MostradaAceptacion>(); }, null, null);
            Assert.IsNull(ranking.Puntuar(new[] { M(0, "s", "X", null) }, null));
            Assert.AreEqual(0, lecturas);
        }

        [TestMethod]
        public void Ranking_FallaLaLecturaDeLasTasasOElZip_SigueConElBayesiano_YAvisaUnaVez()
        {
            Modelo("20261101_0330", pasa: true); // un «zip» que no es un zip
            var errores = new List<Exception>();
            var ranking = new RankingAceptacionSugerencias(() => ModoRankingAceptacion.Sombra, () => carpeta,
                () => throw new InvalidOperationException("sin BD"), errores.Add, null);

            PuntuacionAceptacion r1 = ranking.Puntuar(new[] { M(0, "s", "X", null, p1: 0.5m) }, null);
            PuntuacionAceptacion r2 = ranking.Puntuar(new[] { M(0, "s", "X", null, p1: 0.5m) }, null);

            Assert.AreEqual(ReordenadorBayesiano.VERSION, r1.Version);
            Assert.AreEqual(ReordenadorBayesiano.VERSION, r2.Version);
            Assert.AreEqual(2, errores.Count, "Uno por las tasas y otro por el zip, no uno por llamada");
        }

        [TestMethod]
        public void Ranking_Version_CabeEnVeinte()
        {
            Assert.AreEqual("m2-20261101_0330", RankingAceptacionSugerencias.Version(@"C:\x\modelo_aceptacion_sugerencias_20261101_0330.zip"));
        }

        // ---------------- Job semanal ----------------

        private ReentrenamientoModeloAceptacionJobsService Job(Func<List<MostradaAceptacion>> leer, DateTime ahora, IServicioNotificacionesPush notificaciones, List<Exception> errores)
            => new ReentrenamientoModeloAceptacionJobsService(leer, new AlmacenModeloSugerencias(carpeta), notificaciones, () => ahora,
                _ => null, () => { }, errores.Add);

        [TestMethod]
        public void Job_EsDomingoDeMadrugada_EnLaColaDeEntrenamiento()
        {
            Assert.AreEqual("30 3 * * 0", ReentrenamientoModeloAceptacionJobsService.CRON);
            Assert.AreEqual(ReentrenamientoModeloLlamadasJobsService.COLA, ReentrenamientoModeloAceptacionJobsService.COLA);
        }

        [TestMethod]
        public void Job_EnHorario_NiLee()
        {
            int lecturas = 0;
            var errores = new List<Exception>();
            EstadoEntrenamientoAceptacion e = Job(() => { lecturas++; return new List<MostradaAceptacion>(); }, new DateTime(2026, 10, 27, 10, 0, 0),
                A.Fake<IServicioNotificacionesPush>(), errores).Ejecutar(false, "test");

            Assert.AreEqual(EstadoEntrenamientoAceptacion.EnHorario, e);
            Assert.AreEqual(0, lecturas);
        }

        [TestMethod]
        public void Job_DatosInsuficientes_DejaElInforme_SinZipNiAviso()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var errores = new List<Exception>();
            DateTime domingo = new DateTime(2026, 11, 1, 3, 30, 0);

            EstadoEntrenamientoAceptacion e = Job(() => Respuestas(21, 8, 7), domingo, notificaciones, errores).Ejecutar(false, "test");

            Assert.AreEqual(EstadoEntrenamientoAceptacion.DatosInsuficientes, e);
            Assert.IsTrue(File.Exists(Path.Combine(carpeta, "informe_aceptacion_sugerencias_20261101_0330.md")));
            Assert.IsTrue(File.Exists(Path.Combine(carpeta, "logs", "entrenamiento_aceptacion_20261101_0330.log")));
            Assert.AreEqual(0, Directory.GetFiles(carpeta, RankingAceptacionSugerencias.PREFIJO_FICHERO + "*").Length);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void Job_FallaLaLectura_AElmahYALaCampana_YNoLanza()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var errores = new List<Exception>();

            EstadoEntrenamientoAceptacion e = Job(() => throw new InvalidOperationException("sin BD"), new DateTime(2026, 11, 1, 3, 30, 0), notificaciones, errores)
                .Ejecutar(false, "test");

            Assert.AreEqual(EstadoEntrenamientoAceptacion.Error, e);
            Assert.AreEqual(1, errores.Count);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("NUEVAVISION\\Carlos", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Job_ConDatos_EntrenaGuardaZipYJsonConLaPuerta_YAvisaQueSigueEnSombra()
        {
            var notificaciones = A.Fake<IServicioNotificacionesPush>();
            var errores = new List<Exception>();
            List<MostradaAceptacion> datos = Sinteticos(500);
            var job = new ReentrenamientoModeloAceptacionJobsService(() => datos, new AlmacenModeloSugerencias(carpeta), notificaciones,
                () => new DateTime(2026, 11, 1, 3, 30, 0), _ => null, () => CargadorLightGbmNativo.Asegurar(), errores.Add);

            EstadoEntrenamientoAceptacion e = job.Ejecutar(false, "test");

            Assert.AreEqual(EstadoEntrenamientoAceptacion.Pasa, e, string.Join(" | ", errores.Select(x => x.Message)));
            string json = Path.Combine(carpeta, RankingAceptacionSugerencias.PREFIJO_FICHERO + "20261101_0330.json");
            Assert.IsTrue(File.Exists(Path.ChangeExtension(json, ".zip")));
            MetadatosModeloAceptacion meta = JsonConvert.DeserializeObject<MetadatosModeloAceptacion>(File.ReadAllText(json));
            Assert.IsTrue(meta.PasaPuerta);
            Assert.AreEqual(1500, meta.Respondidas);
            Assert.IsTrue(meta.Auc > meta.AucActual);
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(A<string>._, A<string>._,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("PASA") && n.Cuerpo.Contains("sombra")))).MustHaveHappenedOnceExactly();
        }
    }

    /// <summary>NestoAPI#671 (corte 2e): la sombra del modelo 2 dentro de Calcular (con el repositorio de mentira del otro fichero).</summary>
    public partial class SugerenciasProductosTests
    {
        private sealed class RankingFake : IRankingAceptacionSugerencias
        {
            public ModoRankingAceptacion Modo { get; set; } = ModoRankingAceptacion.Sombra;
            public Dictionary<string, float> P2 { get; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public bool Entrenado { get; set; }
            public bool Fallar { get; set; }
            public List<MostradaAceptacion> UltimosCandidatos { get; private set; } = new List<MostradaAceptacion>();
            public List<ModeloSugerenciaEntrada> UltimasInstantaneas { get; private set; } = new List<ModeloSugerenciaEntrada>();
            public int Llamadas { get; private set; }

            public PuntuacionAceptacion Puntuar(IList<MostradaAceptacion> candidatos, IList<ModeloSugerenciaEntrada> instantaneas)
            {
                Llamadas++;
                if (Fallar)
                {
                    throw new InvalidOperationException("la sombra ha fallado");
                }
                UltimosCandidatos = candidatos.ToList();
                UltimasInstantaneas = instantaneas.ToList();
                return new PuntuacionAceptacion
                {
                    Version = Entrenado ? "m2-test" : ReordenadorBayesiano.VERSION,
                    EsModeloEntrenado = Entrenado,
                    Puntuaciones = candidatos.Select(c => P2.TryGetValue(c.Producto, out float p) ? p : 0.001f).ToList()
                };
            }
        }

        private List<SombraSugerencia> ConfigurarSombras(bool fallar = false)
        {
            var sombras = new List<SombraSugerencia>();
            A.CallTo(() => _repo.InsertarSombra(A<SombraSugerencia>._)).ReturnsLazily((SombraSugerencia s) =>
            {
                if (fallar)
                {
                    throw new InvalidOperationException("permiso denegado");
                }
                sombras.Add(s);
                return Task.FromResult(true);
            });
            return sombras;
        }

        private PredictorFake PredictorDeLaSombra()
        {
            var predictor = new PredictorFake();
            predictor.P["POT3"] = 0.6f;
            predictor.P["LETOCA2"] = 0.5f;
            predictor.P["POT2"] = 0.4f;
            predictor.P["LETOCA1"] = 0.1f;
            predictor.P["POT1"] = 0.05f;
            return predictor;
        }

        private static RankingFake RankingAlReves(ModoRankingAceptacion modo, bool entrenado)
        {
            var ranking = new RankingFake { Modo = modo, Entrenado = entrenado };
            ranking.P2["POT1"] = 0.9f;
            ranking.P2["LETOCA1"] = 0.8f;
            ranking.P2["POT2"] = 0.7f;
            ranking.P2["LETOCA2"] = 0.2f;
            ranking.P2["POT3"] = 0.1f;
            return ranking;
        }

        private ServicioSugerenciasProductos ServicioConSombra(IPredictorSugerenciasProductos predictor, IRankingAceptacionSugerencias ranking)
            => new ServicioSugerenciasProductos(_repo, () => HOY, ex => _errores.Add(ex), predictor: predictor, rankingAceptacion: ranking);

        [TestMethod]
        public async Task Sombra_RegistraP2YLosPuestosYLaInstantanea_SinCambiarLoQueSeEnsena()
        {
            List<SombraSugerencia> sombras = ConfigurarSombras();
            RankingFake ranking = RankingAlReves(ModoRankingAceptacion.Sombra, entrenado: true);
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), ranking).Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "POT3", "LETOCA2", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray(), "Lo de siempre (P1)");
            Assert.AreEqual(VERSION_FAKE, Mostrada(sesion, "POT3").VersionMotor);
            Assert.AreEqual(3, sombras.Count);
            SombraSugerencia pot3 = sombras.Single(s => s.MostradaId == Mostrada(sesion, "POT3").Id);
            Assert.AreEqual("m2-test", pot3.VersionSombra);
            Assert.AreEqual(0.1m, pot3.PuntuacionSombra);
            Assert.AreEqual((short)1, pot3.RangoModelo1);
            Assert.AreEqual(pot3.Candidatos, pot3.RangoSombra, "El último para el modelo 2");
            Assert.IsTrue(pot3.Features.Contains("EsPotenciar=1") || pot3.Features.Contains("NumeroFuentes="), pot3.Features);
            Assert.AreEqual((byte)ModeloSugerenciaEntrada.VERSION_FEATURES, pot3.VersionFeatures);
            SombraSugerencia pot2 = sombras.Single(s => s.MostradaId == Mostrada(sesion, "POT2").Id);
            Assert.IsTrue(pot2.RangoSombra < pot3.RangoSombra);

            MostradaAceptacion candidato = ranking.UltimosCandidatos.Single(c => c.Producto == "POT3");
            Assert.AreEqual(1, candidato.Posicion, "Se puntúa como si fuera la primera");
            Assert.AreEqual(0.6m, Math.Round(candidato.Puntuacion, 4), "P1");
            Assert.AreEqual("SAN", candidato.Vendedor);
            Assert.AreEqual(CLIENTE, candidato.Cliente);
            Assert.IsTrue(ranking.UltimasInstantaneas.All(i => i != null), "Las features del modelo 1 de cada candidato");
            Assert.AreEqual(0, _errores.Count);
        }

        [TestMethod]
        public async Task Sombra_ModoActivoConModeloEntrenado_OrdenaPorP2_YVersionM2()
        {
            ConfigurarSombras();
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), RankingAlReves(ModoRankingAceptacion.Activo, entrenado: true))
                .Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "POT1", "LETOCA1", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.AreEqual("m2-test", Mostrada(sesion, "POT1").VersionMotor);
            Assert.AreEqual(0.05m, Mostrada(sesion, "POT1").Puntuacion, "Puntuacion sigue siendo P1");
        }

        [TestMethod]
        public async Task Sombra_ModoActivoConElBayesiano_NoOrdena()
        {
            List<SombraSugerencia> sombras = ConfigurarSombras();
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), RankingAlReves(ModoRankingAceptacion.Activo, entrenado: false))
                .Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "POT3", "LETOCA2", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray(), "Sin puerta no ordena");
            Assert.AreEqual(VERSION_FAKE, Mostrada(sesion, "POT3").VersionMotor);
            Assert.IsTrue(sombras.All(s => s.VersionSombra == ReordenadorBayesiano.VERSION));
        }

        [TestMethod]
        public async Task Sombra_Apagada_NiPuntuaNiRegistra()
        {
            List<SombraSugerencia> sombras = ConfigurarSombras();
            RankingFake ranking = RankingAlReves(ModoRankingAceptacion.Apagado, entrenado: true);

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), ranking).Calcular(Peticion(Sesion(explora: false)), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count);
            Assert.AreEqual(0, ranking.Llamadas);
            Assert.AreEqual(0, sombras.Count);
        }

        [TestMethod]
        public async Task Sombra_SinModelo1_ConLasReglas_NoHaySombra()
        {
            List<SombraSugerencia> sombras = ConfigurarSombras();
            RankingFake ranking = RankingAlReves(ModoRankingAceptacion.Activo, entrenado: true);

            SugerenciasProductosDTO r = await ServicioConSombra(null, ranking).Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Count > 0);
            Assert.AreEqual(0, ranking.Llamadas);
            Assert.AreEqual(0, sombras.Count);
        }

        [TestMethod]
        public async Task Sombra_FallaAlRegistrar_NoRompeCalcular_YAvisa()
        {
            ServicioSugerenciasProductos.ReiniciarAvisoSombra();
            ConfigurarSombras(fallar: true);
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), RankingAlReves(ModoRankingAceptacion.Sombra, entrenado: true))
                .Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "POT3", "LETOCA2", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.AreEqual(1, _errores.Count, "Un aviso, no uno por sugerencia");
            StringAssert.Contains(_errores[0].Message, "sombra");
        }

        [TestMethod]
        public async Task Sombra_FallaElRanking_NoRompeCalcular()
        {
            ServicioSugerenciasProductos.ReiniciarAvisoSombra();
            List<SombraSugerencia> sombras = ConfigurarSombras();
            var ranking = new RankingFake { Modo = ModoRankingAceptacion.Activo, Entrenado = true, Fallar = true };

            SugerenciasProductosDTO r = await ServicioConSombra(PredictorDeLaSombra(), ranking).Calcular(Peticion(Sesion(explora: false)), "carlos");

            CollectionAssert.AreEqual(new[] { "POT3", "LETOCA2", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.AreEqual(0, sombras.Count);
            Assert.AreEqual(1, _errores.Count);
        }
    }
}
