using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Features;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.SugerenciasProductos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#671 (corte 2c): el modelo 1 en Calcular (con un predictor de mentira detrás de la interfaz y, en un test, uno
    /// de verdad entrenado aquí). NestoAPI#682: sustituir una visible sin contestar por otra claramente mejor. NestoAPI#669:
    /// dos «🚫 Quitar» de una familia la excluyen para ese cliente.
    /// </summary>
    public partial class SugerenciasProductosTests
    {
        private const string VERSION_FAKE = "m1-test";
        private bool _admiteSustituida;
        private List<PopularidadProducto> _popularidad;

        /// <summary>Lo que hace falta de más en el repositorio de mentira (lo llama el Setup del otro fichero).</summary>
        private void ConfigurarFakesModelo()
        {
            _admiteSustituida = true;
            _popularidad = new List<PopularidadProducto>();
            A.CallTo(() => _repo.AdmiteSustituida()).ReturnsLazily(() => Task.FromResult(_admiteSustituida));
            A.CallTo(() => _repo.GuardarSustituida(A<int>._)).ReturnsLazily((int id) =>
            {
                // Lo mismo que SQL_GUARDAR_SUSTITUIDA
                SugerenciaMostrada m = _mostradas.Single(x => x.Id == id);
                if (m.Respuesta == null)
                {
                    m.Respuesta = RespuestasSugerencia.SUSTITUIDA;
                    m.FechaRespuesta = HOY;
                }
                return Task.CompletedTask;
            });
            A.CallTo(() => _repo.GuardarSustituidaProvisional(A<int>._)).ReturnsLazily((int id) =>
            {
                // Lo mismo que SQL_GUARDAR_SUSTITUIDA_PROVISIONAL
                SugerenciaMostrada m = _mostradas.Single(x => x.Id == id);
                if (m.Respuesta == null && !(m.Texto ?? "").StartsWith(RespuestasSugerencia.MARCA_SUSTITUIDA))
                {
                    m.Texto = RespuestasSugerencia.MARCA_SUSTITUIDA + m.Texto;
                }
                return Task.CompletedTask;
            });
            A.CallTo(() => _repo.LeerPopularidad(A<IEnumerable<string>>._)).ReturnsLazily((IEnumerable<string> productos) =>
                Task.FromResult(_popularidad.Where(p => productos.Contains(p.Producto, StringComparer.OrdinalIgnoreCase)).ToList()));
        }

        private sealed class PredictorFake : IPredictorSugerenciasProductos
        {
            public Dictionary<string, float> P { get; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            public string Version { get; set; } = VERSION_FAKE;
            public bool Fallar { get; set; }
            public List<ModeloSugerenciaEntrada> UltimasEntradas { get; private set; } = new List<ModeloSugerenciaEntrada>();
            public List<Exception> Fallos { get; } = new List<Exception>();

            public string ModeloDisponible() => Version;

            public IList<float> Puntuar(IList<ModeloSugerenciaEntrada> entradas)
            {
                if (Fallar)
                {
                    throw new InvalidOperationException("el modelo ha fallado");
                }
                UltimasEntradas = entradas.ToList();
                return entradas.Select(e => P.TryGetValue(e.Producto, out float p) ? p : 0.01f).ToList();
            }

            public void AvisarFallo(Exception ex) => Fallos.Add(ex);
        }

        private ServicioSugerenciasProductos ServicioConModelo(IPredictorSugerenciasProductos predictor)
            => new ServicioSugerenciasProductos(_repo, () => HOY, ex => _errores.Add(ex), predictor: predictor);

        private static Guid Sesion(bool explora)
        {
            for (int i = 0; ; i++)
            {
                var id = new Guid(i, 671, 682, 1, 2, 3, 4, 5, 6, 7, 8);
                if (MotorSugerenciasProductos.EsSesionDeExploracion(id) == explora)
                {
                    return id;
                }
            }
        }

        private SugerenciaMostrada Mostrada(Guid sesion, string producto) => _mostradas.Single(m => m.SesionId == sesion && m.Producto == producto);

        // ---------------- NestoAPI#671: inferencia ----------------

        [TestMethod]
        public async Task Modelo_OrdenaTodasLasFuentesPorP1_YRegistraP1FuentesYVersion()
        {
            var predictor = new PredictorFake();
            predictor.P["POT3"] = 0.6f;
            predictor.P["LETOCA2"] = 0.5f;
            predictor.P["POT2"] = 0.4f;
            predictor.P["LETOCA1"] = 0.1f;
            predictor.P["POT1"] = 0.05f;
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "POT3", "LETOCA2", "POT2" }, r.Sugerencias.Select(s => s.Producto).ToArray(),
                "Por P1, sin la mezcla por fuentes de reglas-1");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, r.Sugerencias.Select(s => s.Posicion).ToArray());
            SugerenciaMostrada pot3 = Mostrada(sesion, "POT3");
            Assert.AreEqual(0.6m, pot3.Puntuacion, "Puntuacion = P1");
            Assert.AreEqual(VERSION_FAKE, pot3.VersionMotor);
            Assert.AreEqual("Potenciar,Perfil", pot3.Fuentes, "Todas las fuentes, también la del perfil");
            Assert.AreEqual(MotivosSugerencia.POTENCIAR, pot3.Motivo, "Al vendedor, la fuente de la API");
            Assert.IsFalse(pot3.Exploracion);
            Assert.AreEqual(0, _errores.Count);
        }

        [TestMethod]
        public async Task Modelo_LoMasCompradoDelPerfil_SaleComoClientesParecidosConSuFicha()
        {
            Ficha("PERF1", "COS", "CRE");
            _clientesPerfil["PERF1"] = 30;
            var predictor = new PredictorFake();
            predictor.P["PERF1"] = 0.9f;

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false)), "carlos");

            SugerenciaProductoDTO perf = r.Sugerencias.First();
            Assert.AreEqual("PERF1", perf.Producto, "Solo lo propone el modelo (las reglas no tienen esa fuente)");
            Assert.AreEqual(MotivosSugerencia.PARECIDOS, perf.Motivo, "Los clientes solo conocen cuatro motivos");
            Assert.AreEqual("De lo más vendido entre los clientes parecidos (lo compran 30).", perf.Texto);
            Assert.AreEqual("Nombre de PERF1", perf.Nombre, "La ficha se ha leído aparte");
            Assert.AreEqual("Perfil", _mostradas.Single(m => m.Producto == "PERF1").Fuentes);
        }

        [TestMethod]
        public async Task Modelo_LasFeatures_SonLasDelNucleoConDIgualAHoy_SinLasComprasDeHoy()
        {
            Compras("LETOCA1", 0); // de hoy: en caliente «d» = hoy y lo de hoy no cuenta (como en el entrenamiento)
            _popularidad.Add(new PopularidadProducto { Producto = "JUNTO1", Clientes12Meses = 40, Clientes3Meses = 15, Cestas12Meses = 60, Rango = 3 });
            var predictor = new PredictorFake();

            _ = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

            // Lo mismo, calculado directamente con el núcleo (lo que hace el entrenamiento con el contexto de ese día)
            Func<string, FichaProducto> ficha = p => _fichas.TryGetValue(p, out FichaProductoSugerencia f) ? f : null;
            var perfiles = _clientesPerfil.Select(c => new ProductoDePerfil { Grupo = "COS", SubGrupo = "CRE", Producto = c.Key, Clientes = c.Value });
            ContextoInstantanea contexto = ContextoInstantanea.Desde(HOY.Date, _pares.Select(MotorSugerenciasProductos.AParNucleo), null,
                new[] { new PopularidadDeProducto { Producto = "JUNTO1", Clientes12Meses = 40, Clientes3Meses = 15, Cestas12Meses = 60, Rango = 3 } }, perfiles);
            ResultadoCandidatos esperado = CalculadoraFeatures.Calcular(contexto, CLIENTE, MotorSugerenciasProductos.ACompras(_compras),
                Conjunto("CARRO1"), ficha, HOY.Date, new OpcionesEntrenamiento().MaximoCandidatosPorFuente);

            Assert.IsTrue(esperado.Filas.Count >= 5);
            foreach (FilaEntrenamiento fila in esperado.Filas)
            {
                ModeloSugerenciaEntrada enCaliente = predictor.UltimasEntradas.SingleOrDefault(e => e.Producto == fila.Producto);
                Assert.IsNotNull(enCaliente, fila.Producto);
                foreach (string columna in Entrenador.Numericas.Concat(Entrenador.Categoricas))
                {
                    object a = typeof(ModeloSugerenciaEntrada).GetProperty(columna).GetValue(fila);
                    object b = typeof(ModeloSugerenciaEntrada).GetProperty(columna).GetValue(enCaliente);
                    Assert.AreEqual(a, b, $"{fila.Producto}.{columna}");
                }
            }
            ModeloSugerenciaEntrada letoca1 = predictor.UltimasEntradas.Single(e => e.Producto == "LETOCA1");
            Assert.AreEqual(4f, letoca1.ComprasProducto24Meses, "La compra de hoy no cuenta");
            Assert.AreEqual(60f, letoca1.DiasDesdeUltimaCompraProducto);
            ModeloSugerenciaEntrada junto1 = predictor.UltimasEntradas.Single(e => e.Producto == "JUNTO1");
            Assert.AreEqual(40f, junto1.Clientes12Meses, "La popularidad de la tabla del job");
            Assert.AreEqual(1.5f, junto1.TendenciaPopularidad);
            Assert.AreEqual(1f, junto1.TamanoCarrito);
            Assert.AreEqual("10", junto1.Mes);
            Assert.IsTrue(predictor.UltimasEntradas.Any(e => e.Producto == "POT1"), "Potenciar también se puntúa (como adicional)");
        }

        [TestMethod]
        public void Features_SinAdicionales_ElEntrenamientoNoCambia_YLosAdicionalesNoLlevanFuente()
        {
            Func<string, FichaProducto> ficha = p => _fichas.TryGetValue(p, out FichaProductoSugerencia f) ? f : null;
            ContextoInstantanea contexto = ContextoInstantanea.Desde(HOY.Date, _pares.Select(MotorSugerenciasProductos.AParNucleo), null, null, null);
            List<Compra> compras = MotorSugerenciasProductos.ACompras(_compras).ToList();

            ResultadoCandidatos sin = CalculadoraFeatures.Calcular(contexto, CLIENTE, compras, Conjunto("CARRO1"), ficha, HOY.Date, 15);
            ResultadoCandidatos con = CalculadoraFeatures.Calcular(contexto, CLIENTE, compras, Conjunto("CARRO1"), ficha, HOY.Date, 15,
                new[] { "POT1", "CARRO1", "JUNTO1", "NOEXISTE" });

            CollectionAssert.AreEqual(sin.Filas.Select(f => f.Producto).ToArray(), con.Filas.Take(sin.Filas.Count).Select(f => f.Producto).ToArray());
            CollectionAssert.AreEqual(new[] { "POT1" }, con.Filas.Skip(sin.Filas.Count).Select(f => f.Producto).ToArray(),
                "Ni lo del carrito, ni lo ya propuesto, ni lo que no tiene ficha");
            FilaEntrenamiento pot1 = con.Filas.Last();
            Assert.AreEqual(0f, pot1.NumeroFuentes);
            Assert.AreEqual(0f, pot1.EsPerfil);
            Assert.AreEqual(0f, pot1.EsParecidos);
        }

        [TestMethod]
        public async Task Modelo_SinModelo_ReglasUno()
        {
            var predictor = new PredictorFake { Version = null };
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(sesion), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "POT1", "LETOCA2" }, r.Sugerencias.Select(s => s.Producto).ToArray(), "La mezcla de reglas-1");
            Assert.IsTrue(_mostradas.All(m => m.VersionMotor == MotorSugerenciasProductos.VERSION_MOTOR));
        }

        [TestMethod]
        public async Task Modelo_FallaLaPrediccion_ReglasUnoSinRomperYSeAvisa()
        {
            var predictor = new PredictorFake { Fallar = true };

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false)), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "POT1", "LETOCA2" }, r.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.IsTrue(_mostradas.All(m => m.VersionMotor == MotorSugerenciasProductos.VERSION_MOTOR));
            Assert.AreEqual(1, predictor.Fallos.Count, "AvisarFallo (que lo manda a ELMAH una sola vez)");
        }

        [TestMethod]
        public async Task Modelo_FallaUnaLecturaDelModelo_ReglasUno()
        {
            A.CallTo(() => _repo.LeerPopularidad(A<IEnumerable<string>>._)).Throws(new InvalidOperationException("timeout"));
            var predictor = new PredictorFake();

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false)), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count);
            Assert.AreEqual(1, predictor.Fallos.Count);
        }

        [TestMethod]
        public void Diversidad_Apagada_YConElModeloSalenLasTresMejoresAunqueSeanDeLaMismaFamilia()
        {
            Assert.IsFalse(MotorSugerenciasProductos.APLICAR_DIVERSIDAD, "Decisión de Carlos con el informe (P@3 26,1 % sin ella, 17,2 % con ella)");
        }

        [TestMethod]
        public async Task Diversidad_Apagada_LasTresMejoresDeLaMismaFamiliaYHabituales()
        {
            foreach (string p in new[] { "LETOCA1", "LETOCA2", "POT1" })
            {
                _fichas[p].Familia = "MISMA";
            }
            var predictor = new PredictorFake();
            predictor.P["LETOCA1"] = 0.7f; // habitual (4 compras)
            predictor.P["LETOCA2"] = 0.6f; // habitual (3 compras)
            predictor.P["POT1"] = 0.5f;
            predictor.P["POT2"] = 0.1f;

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false)), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "LETOCA2", "POT1" }, r.Sugerencias.Select(s => s.Producto).ToArray());
        }

        // ---------------- NestoAPI#671: exploración ----------------

        [TestMethod]
        public void Exploracion_DecididaPorSesion_EstableYEnTornoAlDiezPorCiento()
        {
            Guid id = Guid.NewGuid();
            Assert.AreEqual(MotorSugerenciasProductos.EsSesionDeExploracion(id), MotorSugerenciasProductos.EsSesionDeExploracion(id));
            int exploran = Enumerable.Range(0, 20000).Count(_ => MotorSugerenciasProductos.EsSesionDeExploracion(Guid.NewGuid()));
            Assert.IsTrue(exploran > 1600 && exploran < 2400, $"{exploran} de 20000");
            Assert.AreEqual(10, MotorSugerenciasProductos.PORCENTAJE_SESIONES_EXPLORACION);
        }

        [TestMethod]
        public void IndiceExploracion_EntreLosPuestosCuatroYQuince()
        {
            var azar = new Random(1);
            var vistos = new HashSet<int>();
            for (int i = 0; i < 2000; i++)
            {
                _ = vistos.Add(MotorSugerenciasProductos.IndiceExploracion(40, azar).Value);
            }
            CollectionAssert.AreEquivalent(Enumerable.Range(3, 12).ToArray(), vistos.ToArray(), "Índices 3..14 = puestos 4..15");
            Assert.IsNull(MotorSugerenciasProductos.IndiceExploracion(3, azar), "Con menos de 4, sin explorar");
            Assert.AreEqual(3, MotorSugerenciasProductos.IndiceExploracion(4, azar));
        }

        [TestMethod]
        public async Task Exploracion_EnLaSesionQueExplora_ElHuecoTresVaAlAzarYSeMarca()
        {
            var predictor = new PredictorFake();
            predictor.P["LETOCA1"] = 0.9f;
            predictor.P["LETOCA2"] = 0.8f;
            predictor.P["POT1"] = 0.7f;
            predictor.P["POT2"] = 0.2f;
            predictor.P["POT3"] = 0.1f;
            Guid sesion = Sesion(explora: true);

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(sesion), "carlos");

            Assert.AreEqual("LETOCA1", r.Sugerencias[0].Producto);
            Assert.AreEqual("LETOCA2", r.Sugerencias[1].Producto);
            StringAssert.Matches(r.Sugerencias[2].Producto, new System.Text.RegularExpressions.Regex("^POT[23]$"), "Puesto 4 o 5, no el 3.º (POT1)");
            Assert.IsTrue(Mostrada(sesion, r.Sugerencias[2].Producto).Exploracion);
            Assert.IsFalse(Mostrada(sesion, "LETOCA1").Exploracion);

            // Estable: otra llamada con el mismo carrito no cambia nada (y el hueco de exploración no se sustituye)
            SugerenciasProductosDTO otra = await ServicioConModelo(predictor).Calcular(Peticion(sesion), "carlos");
            CollectionAssert.AreEqual(r.Sugerencias.Select(s => s.IdSugerencia).ToArray(), otra.Sugerencias.Select(s => s.IdSugerencia).ToArray());
        }

        [TestMethod]
        public async Task Exploracion_SinModelo_NoSeExplora()
        {
            SugerenciasProductosDTO r = await ServicioConModelo(new PredictorFake { Version = null }).Calcular(Peticion(Sesion(explora: true)), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count);
            Assert.IsTrue(_mostradas.All(m => !m.Exploracion));
        }

        // ---------------- NestoAPI#671: el predictor de verdad ----------------

        [TestMethod]
        public void ElegirModelo_ElUltimoQuePasaLaPuertaYTieneZip()
        {
            string carpeta = CarpetaTemporal();
            try
            {
                EscribirModelo(carpeta, "20261020_2330", pasa: true);
                EscribirModelo(carpeta, "20261114_0230", pasa: false);                 // más nuevo, pero no pasa
                EscribirModelo(carpeta, "20261101_0230", pasa: true, conZip: false);   // pasa, pero sin zip
                EscribirModelo(carpeta, "20260901_0230", pasa: true);                  // más viejo

                ModeloSugerenciasElegido elegido = PredictorSugerenciasProductos.ElegirModelo(carpeta);

                Assert.AreEqual(Path.Combine(carpeta, "modelo_sugerencias_productos_20261020_2330.zip"), elegido.RutaModelo);
                Assert.AreEqual("m1-20261020_2330", elegido.Version);
                Assert.IsTrue(elegido.Version.Length <= 20, "Cabe en VersionMotor varchar(20)");
                Assert.IsNull(PredictorSugerenciasProductos.ElegirModelo(Path.Combine(carpeta, "noexiste")));
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public void Predictor_SinModelo_ReglasYUnSoloAvisoAElmah()
        {
            string carpeta = CarpetaTemporal();
            try
            {
                var errores = new List<Exception>();
                var predictor = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null);

                Assert.IsNull(predictor.ModeloDisponible());
                Assert.IsNull(predictor.ModeloDisponible());
                Assert.AreEqual(1, errores.Count, "Una vez, no en cada llamada");
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public void Predictor_ZipQueNoCarga_ReglasUnSoloAvisoYNoSeReintentaHastaQueCambie()
        {
            string carpeta = CarpetaTemporal();
            try
            {
                EscribirModelo(carpeta, "20261020_2330", pasa: true); // el «zip» no es un modelo
                var errores = new List<Exception>();
                var predictor = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null);

                Assert.IsNull(predictor.ModeloDisponible());
                Assert.IsNull(predictor.ModeloDisponible());
                Assert.AreEqual(1, errores.Count);
                StringAssert.Contains(errores[0].Message, "reglas-1");
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public void Predictor_InterruptorApagado_NiMiraLaCarpeta()
        {
            var errores = new List<Exception>();
            var predictor = new PredictorSugerenciasProductos(() => throw new InvalidOperationException("no debería mirar la carpeta"),
                new InterruptorCacheado(() => "0", TimeSpan.FromMinutes(1)), errores.Add, null);

            Assert.IsNull(predictor.ModeloDisponible());
            Assert.AreEqual(0, errores.Count);
            Assert.IsTrue(InterruptorCacheado.Interpretar(null), "Sin fila en ParametrosUsuario: encendido (no hace falta script)");
        }

        [TestMethod]
        public async Task ModeloDeVerdad_EntrenadoAqui_PuntuaEnCalcular_YMideElCoste()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo entrena en 64 bits y el proceso de los tests es de 32");
            }
            _ = CargadorLightGbmNativo.Asegurar();
            string carpeta = CarpetaTemporal();
            try
            {
                var hoy = new DateTime(2026, 10, 20);
                var ligeras = new OpcionesEntrenamiento { Iteraciones = 40, MinimoEjemplosPorHoja = 10, EvaluarLambdaRank = false };
                VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, ligeras);
                ProcesoEntrenamiento proceso = ProcesoEntrenamiento.Evaluar(
                    ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta), ligeras, hoy);
                string zip = Path.Combine(carpeta, "modelo_sugerencias_productos_20261020_2330.zip");
                _ = proceso.EntrenarFinalYGuardar(zip);
                MetadatosModelo meta = proceso.Metadatos(hoy, "test", zip);
                meta.PasaPuerta = true;
                File.WriteAllText(Path.ChangeExtension(zip, ".json"), JsonConvert.SerializeObject(meta));

                var errores = new List<Exception>();
                var predictor = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null);
                ServicioSugerenciasProductos conModelo = ServicioConModelo(predictor);

                SugerenciasProductosDTO r = await conModelo.Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

                Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.ToString())));
                Assert.AreEqual(3, r.Sugerencias.Count);
                Assert.IsTrue(_mostradas.All(m => m.VersionMotor == "m1-20261020_2330"));
                Assert.IsTrue(_mostradas.All(m => m.Puntuacion >= 0 && m.Puntuacion <= 1), "Puntuacion = P1");

                // Coste: lo que añade el modelo (features + puntuar) con la BD de mentira (las lecturas reales se miden aparte)
                const int veces = 50;
                var reloj = Stopwatch.StartNew();
                for (int i = 0; i < veces; i++)
                {
                    _ = await conModelo.Calcular(Peticion(Guid.NewGuid(), "CARRO1"), "carlos");
                }
                double msModelo = reloj.Elapsed.TotalMilliseconds / veces;
                reloj.Restart();
                for (int i = 0; i < veces; i++)
                {
                    _ = await _servicio.Calcular(Peticion(Guid.NewGuid(), "CARRO1"), "carlos");
                }
                double msReglas = reloj.Elapsed.TotalMilliseconds / veces;
                Console.WriteLine($"[Coste #671] Calcular con el modelo: {msModelo:0.00} ms; con reglas: {msReglas:0.00} ms; añadido: {msModelo - msReglas:0.00} ms (sin BD).");
                Assert.IsTrue(msModelo - msReglas < 100, $"El modelo añade {msModelo - msReglas:0.0} ms");
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        private static string CarpetaTemporal()
        {
            string carpeta = Path.Combine(Path.GetTempPath(), "SugerenciasModelo_" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(carpeta);
            return carpeta;
        }

        private static void EscribirModelo(string carpeta, string sufijo, bool pasa, bool conZip = true)
        {
            string baseNombre = Path.Combine(carpeta, "modelo_sugerencias_productos_" + sufijo);
            File.WriteAllText(baseNombre + ".json", JsonConvert.SerializeObject(new MetadatosModelo { PasaPuerta = pasa, Fichero = Path.GetFileName(baseNombre + ".zip") }));
            if (conZip)
            {
                File.WriteAllText(baseNombre + ".zip", "no es un zip");
            }
        }

        // ---------------- NestoAPI#682: sustituir visibles sin contestar ----------------

        [TestMethod]
        public void MejoraClara_FactorYDiferencia()
        {
            Assert.IsTrue(MotorSugerenciasProductos.MejoraClara(0.20, 0.10));
            Assert.IsFalse(MotorSugerenciasProductos.MejoraClara(0.12, 0.10), "No llega a 1,3 veces");
            Assert.IsFalse(MotorSugerenciasProductos.MejoraClara(0.04, 0.02), "El doble, pero solo 2 puntos más");
            Assert.AreEqual(1.3, MotorSugerenciasProductos.FACTOR_MEJORA_SUSTITUCION);
            Assert.AreEqual(0.03, MotorSugerenciasProductos.DIFERENCIA_MINIMA_SUSTITUCION);
        }

        [TestMethod]
        public async Task Sustitucion_ConModelo_LaMejorNuevaOcupaElHuecoDeLaMasFloja_SoloUnaPorLlamada()
        {
            var predictor = new PredictorFake();
            predictor.P["LETOCA1"] = 0.30f;
            predictor.P["LETOCA2"] = 0.20f;
            predictor.P["POT1"] = 0.10f;
            predictor.P["POT2"] = 0.05f;
            predictor.P["POT3"] = 0.04f;
            predictor.P["JUNTO1"] = 0.50f;
            predictor.P["JUNTO2"] = 0.45f;
            ServicioSugerenciasProductos servicio = ServicioConModelo(predictor);
            Guid sesion = Sesion(explora: false);

            SugerenciasProductosDTO antes = await servicio.Calcular(Peticion(sesion), "carlos");
            CollectionAssert.AreEqual(new[] { "LETOCA1", "LETOCA2", "POT1" }, antes.Sugerencias.Select(s => s.Producto).ToArray());
            int filasAntes = _mostradas.Count;

            SugerenciasProductosDTO despues = await servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "LETOCA2", "JUNTO1" }, despues.Sugerencias.Select(s => s.Producto).ToArray(),
                "JUNTO1 entra en el hueco de POT1 (la más floja); JUNTO2 también mejora a LETOCA2, pero solo una por llamada");
            Assert.AreEqual(3, despues.Sugerencias.Single(s => s.Producto == "JUNTO1").Posicion, "En la misma posición: la tarjeta no salta");
            Assert.AreEqual(filasAntes + 1, _mostradas.Count);
            Assert.AreEqual(RespuestasSugerencia.SUSTITUIDA, Mostrada(sesion, "POT1").Respuesta);
            Assert.AreEqual(0.5m, Mostrada(sesion, "JUNTO1").Puntuacion);
        }

        [TestMethod]
        public async Task Sustitucion_ConModelo_SinMargenClaro_NoSeCambia()
        {
            var predictor = new PredictorFake();
            predictor.P["LETOCA1"] = 0.30f;
            predictor.P["LETOCA2"] = 0.20f;
            predictor.P["POT1"] = 0.10f;
            predictor.P["JUNTO1"] = 0.12f; // mejor, pero menos de 1,3 veces
            ServicioSugerenciasProductos servicio = ServicioConModelo(predictor);
            Guid sesion = Sesion(explora: false);
            _ = await servicio.Calcular(Peticion(sesion), "carlos");

            SugerenciasProductosDTO despues = await servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "LETOCA2", "POT1" }, despues.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.IsNull(Mostrada(sesion, "POT1").Respuesta);
        }

        /// <summary>Regresión de NestoAPI#682: sin el arreglo, con los tres huecos llenos, la «comprado junto» nunca entraba.</summary>
        [TestMethod]
        public async Task Sustitucion_SinModelo_LaCompradoJuntoNuevaEntraEnElHuecoDeLaMasFlojaSegunElOrdenDeFuentes()
        {
            Guid sesion = Guid.NewGuid();
            SugerenciasProductosDTO antes = await _servicio.Calcular(Peticion(sesion), "carlos");
            CollectionAssert.AreEqual(new[] { "LETOCA1", "POT1", "LETOCA2" }, antes.Sugerencias.Select(s => s.Producto).ToArray());

            SugerenciasProductosDTO despues = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "JUNTO1", "LETOCA2" }, despues.Sugerencias.Select(s => s.Producto).ToArray(),
                "Potenciar va detrás de LeToca en ORDEN_FUENTES: POT1 es la más floja");
            Assert.AreEqual(2, despues.Sugerencias.Single(s => s.Producto == "JUNTO1").Posicion);
            Assert.AreEqual(MotivosSugerencia.COMPRADO_JUNTO, despues.Sugerencias[1].Motivo);
            Assert.AreEqual(RespuestasSugerencia.SUSTITUIDA, Mostrada(sesion, "POT1").Respuesta);
            Assert.AreEqual(MotorSugerenciasProductos.VERSION_MOTOR, Mostrada(sesion, "JUNTO1").VersionMotor);

            // Ya hay una «comprado junto» a la vista: JUNTO2 no entra
            SugerenciasProductosDTO otra = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");
            CollectionAssert.AreEqual(new[] { "LETOCA1", "JUNTO1", "LETOCA2" }, otra.Sugerencias.Select(s => s.Producto).ToArray());
        }

        [TestMethod]
        public async Task Sustitucion_SinModelo_ConConfianzaBaja_NoSeCambia()
        {
            _pares = new List<ParCompradoJunto> { Par("CARRO1", "JUNTO1", 0.10m) };
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");

            SugerenciasProductosDTO despues = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");

            CollectionAssert.AreEqual(new[] { "LETOCA1", "POT1", "LETOCA2" }, despues.Sugerencias.Select(s => s.Producto).ToArray());
            Assert.AreEqual(0.15m, MotorSugerenciasProductos.CONFIANZA_MINIMA_SUSTITUCION_REGLAS);
        }

        [TestMethod]
        public async Task Sustitucion_AlResponderNoSeSustituyeNadaMas()
        {
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");
            int idLetoca2 = Mostrada(sesion, "LETOCA2").Id;
            _sesiones[sesion].Carrito = "CARRO1:1";

            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(idLetoca2, new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.NO_LE_INTERESA });

            Assert.IsNull(Mostrada(sesion, "POT1").Respuesta, "Responder solo rellena el hueco contestado");
            Assert.AreEqual(3, r.Sustituta.Posicion);
        }

        [TestMethod]
        public async Task Sustituida_NoVuelveEnLaSesion_YNoCuentaComoRechazo()
        {
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");
            _ = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");   // POT1 → Sustituida

            // Fuera el carrito: hay sitio, pero POT1 no vuelve en esta sesión
            SugerenciasProductosDTO sinCarrito = await _servicio.Calcular(Peticion(sesion), "carlos");
            Assert.IsFalse(sinCarrito.Sugerencias.Any(s => s.Producto == "POT1"));

            // Ni negativo ni exclusión de 60 días: en otra plantilla sí puede salir
            Assert.IsFalse(RespuestasSugerencia.ExcluyenSesentaDias(RespuestasSugerencia.SUSTITUIDA));
            Assert.IsFalse(RespuestasSugerencia.EsQuitada(RespuestasSugerencia.SUSTITUIDA, null));
            Assert.IsFalse(SugerenciasProductosSql.SQL_RECHAZADOS.Contains(RespuestasSugerencia.SUSTITUIDA));
            SugerenciasProductosDTO otraPlantilla = await _servicio.Calcular(Peticion(Guid.NewGuid()), "carlos");
            Assert.IsTrue(otraPlantilla.Sugerencias.Any(s => s.Producto == "POT1"));
            Assert.IsTrue(RespuestasSugerencia.SUSTITUIDA.Length <= 20, "Cabe en Respuesta varchar(20)");
        }

        [TestMethod]
        public async Task Sustituida_SinElScript_SeMarcaEnElTextoYSigueSinRespuesta()
        {
            _admiteSustituida = false;
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");

            _ = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");

            SugerenciaMostrada pot1 = Mostrada(sesion, "POT1");
            Assert.IsNull(pot1.Respuesta, "Sin respuesta: no cuenta como ofrecida");
            StringAssert.StartsWith(pot1.Texto, RespuestasSugerencia.MARCA_SUSTITUIDA);
            SugerenciasProductosDTO luego = await _servicio.Calcular(Peticion(sesion, "CARRO1"), "carlos");
            Assert.IsFalse(luego.Sugerencias.Any(s => s.Producto == "POT1"), "La provisional tampoco se vuelve a enseñar");
            StringAssert.Contains(SugerenciasProductosSql.SQL_GUARDAR_SUSTITUIDA_PROVISIONAL, RespuestasSugerencia.MARCA_SUSTITUIDA);
            StringAssert.Contains(SugerenciasProductosSql.SQL_GUARDAR_SUSTITUIDA, "Respuesta IS NULL");
            Assert.AreEqual(13, RespuestasSugerencia.MARCA_SUSTITUIDA.Length, "El script Issue682 quita 13 caracteres (SUBSTRING(Texto, 14, 200))");
        }

        // ---------------- NestoAPI#669: «Quitar» aprende la familia ----------------

        private void Quitado(string producto, string familia, string cliente = CLIENTE, int hace = 10)
            => _rechazosConFamilia.Add(Tuple.Create(new RechazoCliente { Producto = producto, Familia = familia, Quitado = true }, cliente, HOY.Date.AddDays(-hace)));

        [TestMethod]
        public void FamiliasQuitadas_DosQuitadosDistintosDeLaFamilia_NoLeInteresaNoCuenta()
        {
            var uno = new List<RechazoCliente> { new RechazoCliente { Producto = "A1", Familia = "ANU", Quitado = true } };
            Assert.AreEqual(0, MotorSugerenciasProductos.FamiliasQuitadas(uno, null, null).Count, "Uno solo no excluye la familia");

            var dos = new List<RechazoCliente>(uno) { new RechazoCliente { Producto = "A2", Familia = "ANU", Quitado = true } };
            CollectionAssert.AreEqual(new[] { "ANU" }, MotorSugerenciasProductos.FamiliasQuitadas(dos, null, null).ToArray());

            var conRechazo = new List<RechazoCliente>(uno) { new RechazoCliente { Producto = "A2", Familia = "ANU", Quitado = false } };
            Assert.AreEqual(0, MotorSugerenciasProductos.FamiliasQuitadas(conRechazo, null, null).Count, "«No le interesa» no cuenta");

            // El de la sesión (aún sin leer de la BD) cuenta, con la familia de su ficha
            var fichas = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase)
            {
                { "A3", new FichaProductoSugerencia { Producto = "A3", Familia = "ANU" } }
            };
            var sesion = new[] { new SugerenciaMostrada { Producto = "A3", Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR } };
            CollectionAssert.AreEqual(new[] { "ANU" }, MotorSugerenciasProductos.FamiliasQuitadas(uno, sesion, fichas).ToArray());
            Assert.AreEqual(2, MotorSugerenciasProductos.MINIMO_QUITADOS_FAMILIA);
        }

        [TestMethod]
        public async Task FamiliaQuitada_UnQuitadoNoExcluyeLaFamilia()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
        }

        [TestMethod]
        public async Task FamiliaQuitada_DosQuitadosExcluyenTodaLaFamilia()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");
            Quitado("OTRO2", "ANU", hace: 50);

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "POT1"));
            Assert.AreEqual(MotorSugerenciasProductos.DESCARTE_FAMILIA_QUITADA, MotorSugerenciasProductos.MotivoDescarte("POT1", _fichas["POT1"],
                FiltroGrupoVendedor.Todos, null, null, null, new HashSet<string> { "ANU" }));
        }

        [TestMethod]
        public async Task FamiliaQuitada_OtroClienteNoSeVeAfectado()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU", cliente: "2002");
            Quitado("OTRO2", "ANU", cliente: "2002");

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
        }

        [TestMethod]
        public async Task FamiliaQuitada_FueraDeLaVentanaDejaDeExcluir()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");
            Quitado("OTRO2", "ANU", hace: MotorSugerenciasProductos.DIAS_SIN_REPETIR_RECHAZO + 1);

            SugerenciasProductosDTO r = await _servicio.Calcular(Peticion(null), "carlos");

            Assert.IsTrue(r.Sugerencias.Any(s => s.Producto == "POT1"));
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "m.FechaRespuesta >= @p2");
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "p.Familia");
        }

        [TestMethod]
        public async Task FamiliaQuitada_ConElModelo_NiAunqueSeaLaMejor()
        {
            _fichas["POT1"].Familia = "ANU";
            Quitado("OTRO1", "ANU");
            Quitado("OTRO2", "ANU");
            var predictor = new PredictorFake();
            predictor.P["POT1"] = 0.99f;

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: true)), "carlos");

            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "POT1"), "Ni por P1 ni por la exploración");
        }

        [TestMethod]
        public async Task FamiliaQuitada_DosQuitarEnLaMismaSesion_LaVisibleDeEsaFamiliaSeVa()
        {
            foreach (string p in new[] { "LETOCA1", "POT1", "LETOCA2" })
            {
                _fichas[p].Familia = "ANU";
            }
            Guid sesion = Guid.NewGuid();
            _ = await _servicio.Calcular(Peticion(sesion), "carlos");
            _ = await _servicio.Responder(Mostrada(sesion, "LETOCA1").Id, new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR });
            ResultadoRespuestaSugerenciaDTO r = await _servicio.Responder(Mostrada(sesion, "POT1").Id,
                new RespuestaSugerenciaDTO { Respuesta = RespuestasSugerencia.DESCARTADO_VENDEDOR });

            Assert.IsFalse(r.Sugerencias.Any(s => s.Producto == "LETOCA2"), "Dos «Quitar» de ANU: tampoco LETOCA2");
        }
    }
}
