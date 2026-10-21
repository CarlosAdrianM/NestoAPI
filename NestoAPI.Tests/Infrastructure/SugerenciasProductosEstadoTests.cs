using FakeItEasy;
using Microsoft.ML;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModeloSugerenciasProductos.Datos;
using ModeloSugerenciasProductos.Entrenamiento;
using ModeloSugerenciasProductos.Evaluacion;
using ModeloSugerenciasProductos.Features;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.SugerenciasProductos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#683: el estado del cliente en el modelo 1 de sugerencias: grupos, historial sin fuga, popularidad por estado
    /// (job y entrenamiento con las mismas cuentas), features en caliente = entrenamiento campo a campo, versión del esquema
    /// de features en el json y fallback a zips compatibles (el de la versión 1 sigue funcionando con la API nueva).
    /// </summary>
    public partial class SugerenciasProductosTests
    {
        // ---------------- Núcleo: grupos e historial ----------------

        [TestMethod]
        public void EstadosCliente_AgrupaDistribuidoresVisitasYRaros()
        {
            Assert.AreEqual("6", EstadosCliente.Grupo(6));
            Assert.AreEqual("6", EstadosCliente.Grupo(67), "Contacto de distribuidor: con los distribuidores");
            Assert.AreEqual("9", EstadosCliente.Grupo(99));
            Assert.AreEqual("0", EstadosCliente.Grupo(21));
            Assert.AreEqual("0", EstadosCliente.Grupo(88));
            Assert.AreEqual("2", EstadosCliente.Grupo(3));
            Assert.AreEqual("11", EstadosCliente.Grupo(22));
            Assert.AreEqual("93", EstadosCliente.Grupo(93));
            Assert.AreEqual("-1", EstadosCliente.Grupo(-1));
            Assert.AreEqual(EstadosCliente.OTRO, EstadosCliente.Grupo(47));
            Assert.AreEqual(EstadosCliente.OTRO, EstadosCliente.Grupo(5));
            Assert.AreEqual(EstadosCliente.NADA, EstadosCliente.Grupo(null));
            Assert.IsTrue(EstadosCliente.EsDistribuidor(EstadosCliente.Grupo(67)));
        }

        [TestMethod]
        public void HistorialEstados_DeshaceLosCambiosPosteriores_SinFuga()
        {
            var historial = new HistorialEstadosCliente(
                new Dictionary<string, short?> { ["C1"] = 9, ["C2"] = 6 },
                new[]
                {
                    new CambioEstadoCliente { Cliente = "C1", Fecha = new DateTime(2026, 3, 10, 12, 0, 0), EstadoAnterior = 0, EstadoNuevo = 7 },
                    new CambioEstadoCliente { Cliente = "C1", Fecha = new DateTime(2026, 6, 1, 9, 0, 0), EstadoAnterior = 7, EstadoNuevo = 9 }
                });

            Assert.AreEqual((short)0, historial.EstadoEn("C1", new DateTime(2026, 1, 1)), "Antes del primer cambio: el anterior del primero");
            Assert.AreEqual((short)0, historial.EstadoEn("C1", new DateTime(2026, 3, 10)), "El día del cambio cuenta el de antes (estado al empezar el día)");
            Assert.AreEqual((short)7, historial.EstadoEn("C1", new DateTime(2026, 3, 11)));
            Assert.AreEqual((short)9, historial.EstadoEn("C1", new DateTime(2026, 6, 2)), "Después del último: el de hoy");
            Assert.AreEqual((short)6, historial.EstadoEn("c2 ", new DateTime(2025, 1, 1)), "Sin cambios: el de hoy");
            Assert.IsNull(historial.EstadoEn("SINFICHA", new DateTime(2026, 1, 1)));
        }

        [TestMethod]
        public void PopularidadEstado_ClientesDistintosPorGrupoProductoFamiliaYTotal_Ventana12Meses()
        {
            var hoy = new DateTime(2026, 10, 1);
            var fichas = new Dictionary<string, FichaProducto>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = new FichaProducto { Producto = "A", Grupo = "COS", SubGrupo = "CRE", Familia = "ANUBIS" },
                ["B"] = new FichaProducto { Producto = "B", Grupo = "COS", SubGrupo = "CRE", Familia = "ANUBIS" },
                ["M"] = new FichaProducto { Producto = "M", Grupo = "COS", SubGrupo = "MMP", Familia = "ANUBIS" } // muestra: no sugerible
            };
            var compras = new List<Compra>
            {
                new Compra { Cliente = "D1", Producto = "A", Dia = hoy.AddDays(-10) },
                new Compra { Cliente = "D1", Producto = "A", Dia = hoy.AddDays(-20) },
                new Compra { Cliente = "D1", Producto = "B", Dia = hoy.AddDays(-20) },
                new Compra { Cliente = "S1", Producto = "A", Dia = hoy.AddDays(-5) },
                new Compra { Cliente = "S2", Producto = "M", Dia = hoy.AddDays(-5) },
                new Compra { Cliente = "S3", Producto = "B", Dia = hoy.AddMonths(-13) } // fuera de la ventana
            };
            var estados = new Dictionary<string, short?> { ["D1"] = 6, ["S1"] = 0, ["S2"] = 0, ["S3"] = 0 };

            List<PopularidadPorEstado> filas = TablasNocturnas.PopularidadEstado(compras, p => fichas.TryGetValue(p, out FichaProducto f) ? f : null,
                c => EstadosCliente.Grupo(estados[c]), hoy);
            int Clientes(string e, string t, string k) => filas.SingleOrDefault(f => f.Estado == e && f.Tipo == t && f.Clave == k)?.Clientes ?? 0;

            Assert.AreEqual(1, Clientes("6", "P", "A"));
            Assert.AreEqual(1, Clientes("6", "P", "B"));
            Assert.AreEqual(1, Clientes("6", "F", "ANUBIS"));
            Assert.AreEqual(1, Clientes("6", "T", ""));
            Assert.AreEqual(1, Clientes("0", "P", "A"));
            Assert.AreEqual(0, Clientes("0", "P", "B"), "La de hace 13 meses no cuenta");
            Assert.AreEqual(1, Clientes("0", "T", ""), "La muestra no es sugerible: S2 no cuenta");
            Assert.AreEqual(2, Clientes("*", "P", "A"));
            Assert.AreEqual(2, Clientes("*", "T", ""));
            Assert.IsFalse(filas.Any(f => f.Clave == "M"));
        }

        [TestMethod]
        public void Features_DelEstado_ProporcionesEIndices()
        {
            Func<string, FichaProducto> ficha = p => _fichas.TryGetValue(p, out FichaProductoSugerencia f) ? f : null;
            _fichas["JUNTO1"].Familia = "ANUBIS";
            ContextoInstantanea contexto = ContextoInstantanea.Desde(HOY.Date, _pares.Select(MotorSugerenciasProductos.AParNucleo), null, null, null,
                PopularidadEstadoDePrueba());

            FilaEntrenamiento distribuidor = CalculadoraFeatures.Calcular(contexto, CLIENTE, MotorSugerenciasProductos.ACompras(_compras), Conjunto("CARRO1"),
                ficha, HOY.Date, 15, estadoCliente: 67).Filas.Single(f => f.Producto == "JUNTO1");
            FilaEntrenamiento sinEstado = CalculadoraFeatures.Calcular(contexto, CLIENTE, MotorSugerenciasProductos.ACompras(_compras), Conjunto("CARRO1"),
                ficha, HOY.Date, 15).Filas.Single(f => f.Producto == "JUNTO1");

            Assert.AreEqual("6", distribuidor.EstadoCliente);
            Assert.AreEqual(2f, distribuidor.ClientesEstado12Meses);
            Assert.AreEqual(0.1f, distribuidor.ProporcionEstado12Meses, 1e-6);        // 2 de 20 distribuidores
            Assert.AreEqual(0.05f, distribuidor.ProporcionFamiliaEstado12Meses, 1e-6); // 1 de 20 compra la marca
            Assert.AreEqual(0.5f, distribuidor.IndiceEstado, 1e-6);                   // 10 % frente al 20 % de todos (200 de 1000)
            Assert.AreEqual(0.1f, distribuidor.IndiceFamiliaEstado, 1e-6);            // 5 % frente al 50 %: «a distribuidores, esta marca no»
            Assert.AreEqual(EstadosCliente.NADA, sinEstado.EstadoCliente);
            Assert.AreEqual(0f, sinEstado.ProporcionEstado12Meses, "Sin estado (o sin tabla): a 0");
            Assert.AreEqual(0f, sinEstado.IndiceEstado);
        }

        private static List<PopularidadPorEstado> PopularidadEstadoDePrueba() => new List<PopularidadPorEstado>
        {
            new PopularidadPorEstado { Estado = "6", Tipo = "T", Clave = "", Clientes = 20 },
            new PopularidadPorEstado { Estado = "6", Tipo = "P", Clave = "JUNTO1", Clientes = 2 },
            new PopularidadPorEstado { Estado = "6", Tipo = "F", Clave = "ANUBIS", Clientes = 1 },
            new PopularidadPorEstado { Estado = "*", Tipo = "T", Clave = "", Clientes = 1000 },
            new PopularidadPorEstado { Estado = "*", Tipo = "P", Clave = "JUNTO1", Clientes = 200 },
            new PopularidadPorEstado { Estado = "*", Tipo = "F", Clave = "ANUBIS", Clientes = 500 },
            new PopularidadPorEstado { Estado = "0", Tipo = "P", Clave = "JUNTO1", Clientes = 150 }
        };

        // ---------------- Calcular: en caliente = entrenamiento ----------------

        [TestMethod]
        public async Task ModeloV2_LasFeaturesEnCaliente_SonLasDelNucleo_CampoACampoConElEstado()
        {
            _fichas["JUNTO1"].Familia = "ANUBIS";
            _estadoCliente = 67;
            _popularidadEstado.AddRange(PopularidadEstadoDePrueba());
            _popularidad.Add(new PopularidadProducto { Producto = "JUNTO1", Clientes12Meses = 40, Clientes3Meses = 15, Cestas12Meses = 60, Rango = 3 });
            var predictor = new PredictorFake { VersionFeatures = 2 };

            _ = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

            // Lo mismo con el núcleo, como el entrenamiento: contexto con las tablas del job y el estado del cliente en d
            Func<string, FichaProducto> ficha = p => _fichas.TryGetValue(p, out FichaProductoSugerencia f) ? f : null;
            var perfiles = _clientesPerfil.Select(c => new ProductoDePerfil { Grupo = "COS", SubGrupo = "CRE", Producto = c.Key, Clientes = c.Value });
            ContextoInstantanea contexto = ContextoInstantanea.Desde(HOY.Date, _pares.Select(MotorSugerenciasProductos.AParNucleo), null,
                new[] { new PopularidadDeProducto { Producto = "JUNTO1", Clientes12Meses = 40, Clientes3Meses = 15, Cestas12Meses = 60, Rango = 3 } }, perfiles,
                PopularidadEstadoDePrueba());
            ResultadoCandidatos esperado = CalculadoraFeatures.Calcular(contexto, CLIENTE, MotorSugerenciasProductos.ACompras(_compras),
                Conjunto("CARRO1"), ficha, HOY.Date, new OpcionesEntrenamiento().MaximoCandidatosPorFuente, estadoCliente: 67);

            Assert.IsTrue(esperado.Filas.Count >= 5);
            CollectionAssert.IsSubsetOf(Entrenador.NumericasV2, Entrenador.Numericas, "La versión actual lleva las del estado");
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
            ModeloSugerenciaEntrada junto1 = predictor.UltimasEntradas.Single(e => e.Producto == "JUNTO1");
            Assert.AreEqual("6", junto1.EstadoCliente, "67 va con los distribuidores");
            Assert.AreEqual(0.1f, junto1.IndiceFamiliaEstado, 1e-6);
            Assert.AreEqual(0.5f, junto1.IndiceEstado, 1e-6);
            A.CallTo(() => _repo.LeerPopularidadEstado("6", A<IEnumerable<string>>._, A<IEnumerable<string>>.That.Contains("ANUBIS")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ModeloV1_NiSeLeeElEstado_YLasColumnasNuevasQuedanNeutras()
        {
            _estadoCliente = 6;
            _popularidadEstado.AddRange(PopularidadEstadoDePrueba());
            var predictor = new PredictorFake { VersionFeatures = 1 };

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count);
            A.CallTo(() => _repo.LeerEstadoCliente(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => _repo.LeerPopularidadEstado(A<string>._, A<IEnumerable<string>>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
            Assert.IsTrue(predictor.UltimasEntradas.All(e => e.EstadoCliente == EstadosCliente.NADA && e.IndiceEstado == 0));
        }

        [TestMethod]
        public async Task ModeloV2_FallaLaLecturaDelEstado_ReglasUnoSinRomper()
        {
            A.CallTo(() => _repo.LeerEstadoCliente(A<string>._, A<string>._)).Throws(new InvalidOperationException("se denegó el permiso"));
            var predictor = new PredictorFake { VersionFeatures = 2 };

            SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

            Assert.AreEqual(3, r.Sugerencias.Count, "Calcular nunca se rompe");
            Assert.AreEqual(1, predictor.Fallos.Count);
            Assert.IsTrue(_mostradas.All(m => m.VersionMotor != VERSION_FAKE), "Reglas-1");
        }

        // ---------------- Versión de las features: json y elección del zip ----------------

        [TestMethod]
        public void Metadatos_SinVersionFeaturesEnElJson_EsLaUno_YLosNuevosLlevanLaActual()
        {
            MetadatosModelo antiguo = JsonConvert.DeserializeObject<MetadatosModelo>("{\"PasaPuerta\": true, \"P3\": 0.261}");
            Assert.AreEqual(1, antiguo.VersionFeatures, "El json del 20/10 no la lleva");
            Assert.AreEqual(2, ModeloSugerenciaEntrada.VERSION_FEATURES);
            string nuevo = JsonConvert.SerializeObject(new MetadatosModelo { VersionFeatures = ModeloSugerenciaEntrada.VERSION_FEATURES });
            Assert.AreEqual(2, JsonConvert.DeserializeObject<MetadatosModelo>(nuevo).VersionFeatures);
        }

        [TestMethod]
        public void ElegirModelo_SoloVersionesQueSabeCalcular_ElUltimoCompatible()
        {
            string carpeta = CarpetaTemporal();
            try
            {
                EscribirModelo(carpeta, "20261020_2330", pasa: true);                       // sin VersionFeatures: la 1
                EscribirModeloVersion(carpeta, "20261022_2330", pasa: true, version: 2);
                EscribirModeloVersion(carpeta, "20261101_0230", pasa: true, version: 3);   // de una API futura
                EscribirModeloVersion(carpeta, "20261023_2330", pasa: false, version: 2);

                ModeloSugerenciasElegido conEstado = PredictorSugerenciasProductos.ElegirModelo(carpeta, 2);
                Assert.AreEqual("m1-20261022_2330", conEstado.Version, "La 3 no la sabe calcular; la del 23 no pasa");
                Assert.AreEqual(2, conEstado.VersionFeatures);

                ModeloSugerenciasElegido sinTablaEstado = PredictorSugerenciasProductos.ElegirModelo(carpeta, 1);
                Assert.AreEqual("m1-20261020_2330", sinTablaEstado.Version, "Sin la popularidad por estado, el último de la versión 1");
                Assert.AreEqual(1, sinTablaEstado.VersionFeatures);

                Assert.AreEqual("m1-20261022_2330", PredictorSugerenciasProductos.ElegirModelo(carpeta, 99).Version,
                    "Nunca más de lo que sabe calcular este núcleo");
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public void ElegirModelo_SoloHayUnoDeOtraVersion_Ninguno()
        {
            string carpeta = CarpetaTemporal();
            try
            {
                EscribirModeloVersion(carpeta, "20261022_2330", pasa: true, version: 2);
                Assert.IsNull(PredictorSugerenciasProductos.ElegirModelo(carpeta, 1));
                EscribirModeloVersion(carpeta, "20261101_0230", pasa: true, version: 3);
                Assert.AreEqual("m1-20261022_2330", PredictorSugerenciasProductos.ElegirModelo(carpeta).Version);
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public void ModeloDeVerdad_ElZipDeLaVersionUno_SigueFuncionandoConLaApiNueva_YElDeLaDosSoloConLaTablaDelEstado()
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
                var ligeras = new OpcionesEntrenamiento { Iteraciones = 30, MinimoEjemplosPorHoja = 10, EvaluarLambdaRank = false };
                VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, ligeras);
                ProcesoEntrenamiento proceso = ProcesoEntrenamiento.Evaluar(
                    ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta), ligeras, hoy);
                List<FilaEntrenamiento> filas = proceso.Dataset.Todas.SelectMany(s => s.Filas).ToList();

                // Un zip como el del 20/10: pipeline de la versión 1 guardado con el esquema de entrada de entonces, json sin versión
                MLContext ml = Entrenador.CrearContexto();
                ITransformer v1 = Entrenador.EntrenarBinario(ml, filas, ligeras, 1);
                string zipV1 = Path.Combine(carpeta, "modelo_sugerencias_productos_20261020_2330.zip");
                ml.Model.Save(v1, ml.Data.LoadFromEnumerable(Array.Empty<EntradaVersion1>()).Schema, zipV1);
                File.WriteAllText(Path.ChangeExtension(zipV1, ".json"), "{\"PasaPuerta\": true, \"Fichero\": \"modelo_sugerencias_productos_20261020_2330.zip\"}");

                // El nuevo (versión 2)
                string zipV2 = Path.Combine(carpeta, "modelo_sugerencias_productos_20261022_2330.zip");
                _ = proceso.EntrenarFinalYGuardar(zipV2);
                MetadatosModelo meta = proceso.Metadatos(hoy, "test", zipV2);
                Assert.AreEqual(2, meta.VersionFeatures);
                meta.PasaPuerta = true;
                File.WriteAllText(Path.ChangeExtension(zipV2, ".json"), JsonConvert.SerializeObject(meta));

                // Con la entrada nueva (columnas de más), el zip viejo puntúa
                List<float> p = Entrenador.Probabilidades(ml, Entrenador.Cargar(ml, zipV1), filas.Take(20));
                Assert.AreEqual(20, p.Count);
                Assert.IsFalse(p.Any(float.IsNaN));

                // Sin la popularidad por estado (antes del script o del job): el de la versión 1
                var errores = new List<Exception>();
                var sinTabla = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null, () => 1);
                Assert.AreEqual("m1-20261020_2330", sinTabla.ModeloDisponible());
                Assert.AreEqual(1, sinTabla.VersionFeatures);
                Assert.AreEqual(20, sinTabla.Puntuar(filas.Take(20).Cast<ModeloSugerenciaEntrada>().ToList()).Count);

                // Con ella: el de la versión 2
                var conTabla = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null, () => 2);
                Assert.AreEqual("m1-20261022_2330", conTabla.ModeloDisponible());
                Assert.AreEqual(2, conTabla.VersionFeatures);
                Assert.AreEqual(20, conTabla.Puntuar(filas.Take(20).Cast<ModeloSugerenciaEntrada>().ToList()).Count);

                // Si no se puede mirar la tabla: la versión 1 y un solo aviso
                var falla = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null, () => throw new InvalidOperationException("sin BD"));
                Assert.AreEqual("m1-20261020_2330", falla.ModeloDisponible());
                Assert.AreEqual(1, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
                StringAssert.Contains(errores[0].Message, "#683");
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        [TestMethod]
        public async Task ModeloDeVerdad_V1_EnCalcular_TrasPublicarLaApiNueva()
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
                var ligeras = new OpcionesEntrenamiento { Iteraciones = 30, MinimoEjemplosPorHoja = 10, EvaluarLambdaRank = false };
                VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, ligeras);
                ProcesoEntrenamiento proceso = ProcesoEntrenamiento.Evaluar(
                    ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta), ligeras, hoy);
                MLContext ml = Entrenador.CrearContexto();
                ITransformer v1 = Entrenador.EntrenarBinario(ml, proceso.Dataset.Todas.SelectMany(s => s.Filas).ToList(), ligeras, 1);
                string zipV1 = Path.Combine(carpeta, "modelo_sugerencias_productos_20261020_2330.zip");
                ml.Model.Save(v1, ml.Data.LoadFromEnumerable(Array.Empty<EntradaVersion1>()).Schema, zipV1);
                File.WriteAllText(Path.ChangeExtension(zipV1, ".json"), "{\"PasaPuerta\": true}");
                var errores = new List<Exception>();
                var predictor = new PredictorSugerenciasProductos(() => carpeta, null, errores.Add, null, () => 2);

                SugerenciasProductosDTO r = await ServicioConModelo(predictor).Calcular(Peticion(Sesion(explora: false), "CARRO1"), "carlos");

                Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.ToString())));
                Assert.AreEqual(3, r.Sugerencias.Count);
                Assert.IsTrue(_mostradas.All(m => m.VersionMotor == "m1-20261020_2330"), "Sigue el zip de antes aunque la API sepa la versión 2");
                A.CallTo(() => _repo.LeerEstadoCliente(A<string>._, A<string>._)).MustNotHaveHappened();
            }
            finally
            {
                Directory.Delete(carpeta, true);
            }
        }

        private static void EscribirModeloVersion(string carpeta, string sufijo, bool pasa, int version)
        {
            string baseNombre = Path.Combine(carpeta, "modelo_sugerencias_productos_" + sufijo);
            File.WriteAllText(baseNombre + ".json", JsonConvert.SerializeObject(new MetadatosModelo { PasaPuerta = pasa, VersionFeatures = version }));
            File.WriteAllText(baseNombre + ".zip", "no es un zip");
        }

        // ---------------- Entrenamiento: sin fuga, P@3 por estado y puerta ----------------

        [TestMethod]
        public void Instantaneas_ElEstadoEsElDeEseDia_NoElDeHoy()
        {
            var hoy = new DateTime(2026, 10, 20);
            var op = new OpcionesEntrenamiento { EvaluarLambdaRank = false };
            VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, op);
            DatosCrudos datos = ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta);
            DateTime cambio = datos.CambiosEstado.Single(c => c.Cliente == "C000").Fecha;

            Dataset dataset = ConstructorInstantaneas.Construir(datos, v, op);

            List<Sesion> c000 = dataset.Todas.Where(s => s.Cliente == "C000").ToList();
            Assert.IsTrue(c000.Any(s => s.Dia < cambio) && c000.Any(s => s.Dia > cambio));
            Assert.IsTrue(c000.Where(s => s.Dia <= cambio).All(s => s.EstadoCliente == "0"), "Antes de pasar a distribuidor era «0»");
            Assert.IsTrue(c000.Where(s => s.Dia > cambio).All(s => s.EstadoCliente == "6"));
            Assert.IsTrue(c000.SelectMany(s => s.Filas).All(f => f.EstadoCliente == dataset.Todas.First(s => s.Id == f.Sesion).EstadoCliente));
            Assert.IsTrue(dataset.Todas.Where(s => s.Cliente == "C001").All(s => s.EstadoCliente == "6"), "67, con los distribuidores");
            Assert.IsTrue(dataset.Todas.SelectMany(s => s.Filas).Any(f => f.ProporcionEstado12Meses > 0), "La popularidad por estado entra en las instantáneas");
        }

        [TestMethod]
        public void Puerta_PeorQueElModeloSinLasFeaturesNuevas_NoPasa()
        {
            var reglas = new ResultadoRanking { P3 = 0.10, Ndcg3 = 0.10 };
            var modelo = new ResultadoRanking { P3 = 0.20, Ndcg3 = 0.20 };

            Assert.IsTrue(PuertaCalidad.Decidir(modelo, reglas, 0.02, new ResultadoRanking { P3 = 0.203 }).Pasa, "Medio punto de tolerancia");
            DecisionPuerta peor = PuertaCalidad.Decidir(modelo, reglas, 0.02, new ResultadoRanking { P3 = 0.21 });
            Assert.IsFalse(peor.Pasa);
            StringAssert.Contains(peor.Motivo, "sin las features nuevas");
            Assert.IsTrue(PuertaCalidad.Decidir(modelo, reglas, 0.02).Pasa, "Sin el anterior, como antes");
        }

        [TestMethod]
        public void Diversidad_UnaSolaFuenteDeVerdad_LaPuertaMideComoCalcular()
        {
            Assert.AreEqual(Diversidad.APLICAR, MotorSugerenciasProductos.APLICAR_DIVERSIDAD, "Calcular sigue a la constante del núcleo");
            Assert.IsFalse(Diversidad.APLICAR, "Decisión de Carlos (21/10): sin diversidad");
            // Tres de la misma familia y habituales: sin diversidad salen las tres mejores; con ella, no
            var filas = new List<FilaEntrenamiento>
            {
                new FilaEntrenamiento { Producto = "A", Familia = "F", EsHabitual = 1 },
                new FilaEntrenamiento { Producto = "B", Familia = "F", EsHabitual = 1 },
                new FilaEntrenamiento { Producto = "C", Familia = "F", EsHabitual = 1 },
                new FilaEntrenamiento { Producto = "D", Familia = "G", EsHabitual = 0 },
                new FilaEntrenamiento { Producto = "E", Familia = "H", EsHabitual = 0 }
            };
            var p = new Dictionary<string, double> { ["A"] = 0.9, ["B"] = 0.8, ["C"] = 0.7, ["D"] = 0.2, ["E"] = 0.1 };

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Diversidad.TopComoLaApi(filas, f => p[f.Producto]));
            CollectionAssert.AreEqual(new[] { "A", "D", "E" }, Diversidad.Top(filas, f => p[f.Producto]));
            Assert.AreEqual("Modelo con diversidad (informativo)", ProcesoEntrenamiento.MODELO_OTRA_DIVERSIDAD);
        }

        [TestMethod]
        public void Entrenamiento_InformeConP3PorEstado_YSinHistorialNoPasa()
        {
            if (!Environment.Is64BitProcess)
            {
                Assert.Inconclusive("LightGBM solo entrena en 64 bits y el proceso de los tests es de 32");
            }
            _ = CargadorLightGbmNativo.Asegurar();
            var hoy = new DateTime(2026, 10, 20);
            var ligeras = new OpcionesEntrenamiento { Iteraciones = 30, MinimoEjemplosPorHoja = 10, EvaluarLambdaRank = false, MinimaMejoraP3 = -1 };
            VentanaEntrenamiento v = VentanaEntrenamiento.Calcular(hoy, ligeras);

            ProcesoEntrenamiento con = ProcesoEntrenamiento.Evaluar(ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta), ligeras, hoy);
            string informe = con.InformeMetricas();
            StringAssert.Contains(informe, ProcesoEntrenamiento.SEGMENTO_DISTRIBUIDORES);
            StringAssert.Contains(informe, ProcesoEntrenamiento.SEGMENTO_RESTO);
            StringAssert.Contains(informe, ProcesoEntrenamiento.MODELO_SIN_ESTADO);
            Assert.IsNotNull(con.SinEstado);
            // La puerta mide el modelo como lo ve el vendedor (sin diversidad); la otra variante queda como informativa
            ResultadoRanking informativo = con.Resultados.Single(r => r.Modelo == ProcesoEntrenamiento.MODELO_OTRA_DIVERSIDAD);
            Assert.AreEqual(PuertaCalidad.Decidir(con.Modelo, con.Reglas, ligeras.MinimaMejoraP3, con.SinEstado).Pasa, con.Decision.Pasa);
            Assert.AreNotEqual(informativo.P3, con.Modelo.P3, "Con datos de prueba la diversidad cambia el top-3");
            Assert.IsFalse(con.Metadatos(hoy, "test", null).Diversidad.Value);
            MetadatosModelo meta = con.Metadatos(hoy, "test", null);
            Assert.AreEqual(2, meta.VersionFeatures);
            Assert.IsTrue(meta.P3SinEstado.HasValue && meta.P3Distribuidores.HasValue && meta.P3Resto.HasValue);
            Assert.IsTrue(meta.HistorialEstados);

            DatosCrudos sinHistorial = ReentrenamientoModeloSugerenciasTests.DatosSinteticos(v.DatosDesde, v.DatosHasta);
            sinHistorial.HistorialEstados = false;
            sinHistorial.MotivoSinHistorialEstados = "se denegó el permiso SELECT";
            ProcesoEntrenamiento sin = ProcesoEntrenamiento.Evaluar(sinHistorial, ligeras, hoy);
            Assert.IsFalse(sin.Decision.Pasa, "Sin historial, el estado de hoy en el pasado es fuga");
            StringAssert.Contains(sin.Decision.Motivo, "historial");
            StringAssert.Contains(sin.InformeMetricas(), "SIN historial");
        }

        // ---------------- Job nocturno ----------------

        [TestMethod]
        public void Job_ConTablaDelEstado_GuardaLaPopularidadPorEstadoConElEstadoDeHoy()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            A.CallTo(() => repo.ExistenTablasEstado()).Returns(true);
            var compras = Cesta("D1", -10, "A", "B").Concat(Cesta("S1", -10, "A")).Concat(Cesta("S2", -3, "A")).ToList();
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(compras);
            A.CallTo(() => repo.LeerFichas("1")).Returns(FichasCos("A", "B"));
            A.CallTo(() => repo.LeerEstadosClientes("1")).Returns(new Dictionary<string, short?> { ["D1"] = 67, ["S1"] = 0 });
            List<PopularidadPorEstado> guardadas = null;
            A.CallTo(() => repo.GuardarPopularidadEstado(A<List<PopularidadPorEstado>>._, A<DateTime>._))
                .Invokes((List<PopularidadPorEstado> f, DateTime d) => guardadas = f);

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.AreEqual(guardadas.Count, r.PopularidadEstado);
            Assert.AreEqual(1, guardadas.Single(f => f.Estado == "6" && f.Tipo == "P" && f.Clave == "B").Clientes);
            Assert.AreEqual(1, guardadas.Single(f => f.Estado == "0" && f.Tipo == "P" && f.Clave == "A").Clientes);
            Assert.AreEqual(1, guardadas.Single(f => f.Estado == EstadosCliente.NADA && f.Tipo == "T").Clientes, "S2 no tiene ficha de cliente");
            Assert.AreEqual(3, guardadas.Single(f => f.Estado == "*" && f.Tipo == "T").Clientes);
        }

        [TestMethod]
        public void Job_SinTablaDelEstado_NiLeeLosEstados()
        {
            var repo = A.Fake<IRepositorioJobSugerenciasProductos>();
            A.CallTo(() => repo.ExistenTablas()).Returns(true);
            A.CallTo(() => repo.ExistenTablasEstado()).Returns(false);
            A.CallTo(() => repo.LeerCompras(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(Cesta("C1", -1, "A").ToList());
            A.CallTo(() => repo.LeerFichas("1")).Returns(FichasCos("A"));

            ResultadoJobSugerenciasProductos r = new SugerenciasProductosJobsService(repo, () => "Activo", () => HOY).Ejecutar();

            Assert.IsNull(r.MotivoNoCalcula);
            Assert.AreEqual(0, r.PopularidadEstado);
            A.CallTo(() => repo.LeerEstadosClientes(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repo.GuardarPopularidadEstado(A<List<PopularidadPorEstado>>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void EstadosPorCliente_ElDelContactoPrincipalDeMenorCodigo()
        {
            Dictionary<string, short?> estados = RepositorioJobSugerenciasProductosSql.EstadosPorCliente(new[]
            {
                new EstadoClienteFila { Cliente = "100 ", Contacto = "2", Estado = 9 },
                new EstadoClienteFila { Cliente = "100", Contacto = "0", Estado = 6 },
                new EstadoClienteFila { Cliente = "200", Contacto = "0", Estado = null }
            });

            Assert.AreEqual((short)6, estados["100"]);
            Assert.IsNull(estados["200"]);
        }

        /// <summary>La entrada del modelo tal y como era en la versión 1 (NestoAPI#671, 20/10/26): lo que guarda el zip de entonces.</summary>
        private class EntradaVersion1
        {
            public string Cliente { get; set; } = "";
            public string Producto { get; set; } = "";
            public string Mes { get; set; } = "";
            public string GrupoSubgrupoProducto { get; set; } = "";
            public string PerfilCliente { get; set; } = "";
            public float EsCompradoJunto { get; set; }
            public float ConfianzaCompradoJunto { get; set; }
            public float LiftCompradoJunto { get; set; }
            public float AntecedentesEnCarrito { get; set; }
            public float EsLeToca { get; set; }
            public float EsParecidos { get; set; }
            public float Afinidad { get; set; }
            public float RangoAfinidad { get; set; }
            public float EsPerfil { get; set; }
            public float ClientesPerfil { get; set; }
            public float NumeroFuentes { get; set; }
            public float ComprasProducto24Meses { get; set; }
            public float DiasDesdeUltimaCompraProducto { get; set; }
            public float IntervaloMedianoProducto { get; set; }
            public float RatioLeToca { get; set; }
            public float EsHabitual { get; set; }
            public float ImporteProducto24Meses { get; set; }
            public float ComprasFamilia24Meses { get; set; }
            public float ComprasSubgrupo24Meses { get; set; }
            public float Clientes12Meses { get; set; }
            public float Clientes3Meses { get; set; }
            public float TendenciaPopularidad { get; set; }
            public float PedidosCliente12Meses { get; set; }
            public float ImporteCliente12Meses { get; set; }
            public float DiasDesdeUltimoPedidoCliente { get; set; }
            public float ProductosDistintosCliente { get; set; }
            public float TamanoCarrito { get; set; }
            public float CarritoVacio { get; set; }
            public float MismoSubgrupoEnCarrito { get; set; }
            public float MismaFamiliaEnCarrito { get; set; }
        }
    }
}
