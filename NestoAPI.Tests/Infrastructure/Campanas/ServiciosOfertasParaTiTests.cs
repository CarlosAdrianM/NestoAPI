using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class ServiciosOfertasParaTiTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 10, 30, 0);
        private const string BF = "Black Friday 2026";

        private ILectorOfertasParaTi lector;
        private IPuntuadorReposicion puntuador;
        private IFuentesSugerenciasOfertas fuentes;
        private ICalculadorPreciosOferta precios;
        private List<Exception> errores;

        private static FilaCampanaOferta Fila(string producto, string familia = "EVA", decimal descuento = 0.30m) => new FilaCampanaOferta
        {
            Campana = BF,
            Producto = producto,
            Descuento = descuento,
            AudienciaOferta = 2,
            FechaDesde = new DateTime(2026, 11, 25),
            FechaHasta = new DateTime(2026, 11, 30),
            Nombre = "PRODUCTO " + producto,
            Familia = familia,
            Grupo = "COS",
            SubGrupo = "CRE",
            PVP = 10,
            Estado = 0
        };

        /// <summary>N productos «1».."N", cada uno de su familia (F1, F2…), todos con stock.</summary>
        private void Campana(int n, Func<int, string> familia = null)
        {
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._))
                .Returns(Enumerable.Range(1, n).Select(i => Fila(i.ToString(CultureInfo.InvariantCulture), familia?.Invoke(i) ?? "F" + i)).ToList());
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((IReadOnlyCollection<string> ps) => ps.ToDictionary(p => p, p => 5));
        }

        /// <summary>El modelo puntúa todo: «1» el que más, luego «2»…</summary>
        private void ModeloQuePuntuaTodo()
        {
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-20261125_0300");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((string e, string c, DateTime d, IReadOnlyCollection<string> ps)
                    => ps.ToDictionary(p => p, p => 1f - int.Parse(p, CultureInfo.InvariantCulture) / 10000f));
        }

        /// <summary>Con la campaña, 7 €; sin ella, 10 € (mejora) salvo los de <paramref name="noMejoran"/> (su precio de siempre ya es 7 €).</summary>
        private void Precios(params string[] noMejoran)
        {
            var no = new HashSet<string>(noMejoran);
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string c, string ct, string p, string camp)
                    => new PreciosOferta { Precio = 7m, PrecioAntes = no.Contains(p) ? 7m : 10m, Tarifa = 10m });
        }

        [TestInitialize]
        public void Preparar()
        {
            errores = new List<Exception>();
            lector = A.Fake<ILectorOfertasParaTi>();
            puntuador = A.Fake<IPuntuadorReposicion>();
            fuentes = A.Fake<IFuentesSugerenciasOfertas>();
            precios = A.Fake<ICalculadorPreciosOferta>();
            A.CallTo(() => puntuador.ModeloDisponible()).Returns(null);
            A.CallTo(() => fuentes.Leer(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._, A<ISet<string>>._))
                .Returns(new FuentesClienteOfertas());
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._))
                .Returns(new List<FilaCampanaOferta> { Fila("1", "A"), Fila("2", "B"), Fila("3", "C") });
            A.CallTo(() => lector.LeerPopularidad(A<IReadOnlyCollection<string>>._))
                .Returns(new Dictionary<string, int> { { "1", 30 }, { "2", 20 }, { "3", 10 } });
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((IReadOnlyCollection<string> ps) => ps.ToDictionary(p => p, p => p == "2" ? 0 : 5));
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._))
                .Returns(new List<CompraDiaReposicion>());
            A.CallTo(() => lector.LeerPendientes(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new List<PendienteClienteOferta>());
            A.CallTo(() => lector.LeerFamilias(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, string>());
            Precios();
        }

        private ServicioOfertasParaTi Nucleo() => new ServicioOfertasParaTi(lector, puntuador, errores.Add, fuentes);

        private ServicioOfertasCliente Servicio(ServicioOfertasParaTi nucleo = null, Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto = null)
            => new ServicioOfertasCliente(nucleo ?? Nucleo(), precios, c => Task.FromResult("0"), p => Task.FromResult("https://tienda.nuevavision.es/" + p + ".jpg"),
                contexto, errores.Add);

        // ---------------------------------------------------------------- núcleo: el ranking

        [TestMethod]
        public async Task Nucleo_SinHistorialNiModelo_NadaLeCuadra_NiRellenoGenerico()
        {
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> todo = await nucleo.Calcular(ctx, new[] { "1000" }, null);
            Dictionary<string, ResultadoOfertasCliente> cinco = await nucleo.Calcular(ctx, new[] { "1000" }, 5);

            Assert.AreEqual(3, todo["1000"].Ofertas.Count, "El ranking entero sigue estando (para «Ver todas»)");
            Assert.IsTrue(todo["1000"].Ofertas.All(o => !o.Personal));
            Assert.AreEqual(0, cinco["1000"].Ofertas.Count, "Sin fuentes suyas ni modelo, nada para la franja");
        }

        [TestMethod]
        public async Task Nucleo_ConModelo_OrdenaElModeloYSinStockFuera()
        {
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-20261125_0300");
            A.CallTo(() => puntuador.Puntuar(A<string>._, "1000", A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Returns(new Dictionary<string, float> { { "3", 0.8f }, { "1", 0.2f }, { "2", 0.9f } });
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> r = await nucleo.Calcular(ctx, new[] { "1000" }, 5);

            CollectionAssert.AreEqual(new[] { "3", "1" }, r["1000"].Ofertas.Select(o => o.Candidata.Producto).ToList(), "El 2 no tiene stock");
            Assert.AreEqual("m1-20261125_0300", r["1000"].VersionModelo);
            Assert.AreEqual(3, r["1000"].Puntuados);
        }

        [TestMethod]
        public async Task Nucleo_FuentesDeLaPlantilla_DanMotivoTextoYOrden()
        {
            A.CallTo(() => fuentes.Leer(A<string>._, "1000", A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._, A<ISet<string>>._))
                .Returns(new FuentesClienteOfertas
                {
                    CompradoJunto = new Dictionary<string, Tuple<decimal, string>> { { "3", Tuple.Create(0.4m, "JABON ISDRUC 500 ML") } },
                    Parecidos = new Dictionary<string, decimal> { { "1", 0.7m } }
                });
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            List<OfertaElegida> r = (await nucleo.Calcular(ctx, new[] { "1000" }, null))["1000"].Ofertas;

            CollectionAssert.AreEqual(new[] { "3", "1", "2" }, r.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(MotivosOfertaParaTi.COMPRADO_JUNTO, r[0].Motivo);
            Assert.AreEqual("Quienes piden Jabon Isdruc 500 ml suelen pedir también este", r[0].Texto);
            Assert.AreEqual(MotivosOfertaParaTi.PARECIDO, r[1].Motivo);
            Assert.AreEqual(ReglaRankingOfertasParaTi.TEXTO_PARECIDO, r[1].Texto);
            Assert.IsTrue(r[0].Personal && r[1].Personal);
            Assert.IsFalse(r[2].Personal);
            A.CallTo(() => fuentes.Leer(A<string>._, "1000", A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._,
                A<ISet<string>>.That.Matches(s => s.Count == 3 && s.Contains("2")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Nucleo_SiFallanLasFuentes_SigueSinEllasYNoRompe()
        {
            ModeloQuePuntuaTodo();
            A.CallTo(() => fuentes.Leer(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._, A<ISet<string>>._))
                .Throws(new Exception("pares rotos"));
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> r = await nucleo.Calcular(ctx, new[] { "1000", "2000" }, 5);

            CollectionAssert.AreEqual(new[] { "1", "3" }, r["2000"].Ofertas.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(1, errores.Count, "Falla una vez y la tanda sigue sin ellas");
        }

        [TestMethod]
        public async Task Nucleo_SiElModeloFalla_LaReglaYNoRompe()
        {
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-x");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Throws(new InvalidOperationException("roto"));
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            _ = await nucleo.Calcular(ctx, new[] { "1000", "2000" }, 5);

            Assert.AreEqual(1, errores.Count, "Falla una vez y la tanda sigue por la regla");
            // una sola lectura de compras para los dos clientes (por tanda, no por cliente)
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Nucleo_ElRankingSeGuardaConElContexto_YElStockSeLeeUnaVez()
        {
            ModeloQuePuntuaTodo();
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            _ = await nucleo.Calcular(ctx, new[] { "1000", "2000", "3000" }, 5);
            _ = await nucleo.Calcular(ctx, new[] { "1000" }, null);

            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public void Ranking_LeToca_Habitual_CompradoJunto_Parecido_Marcas_Perfil_Ninguna_YElModeloDelante()
        {
            CandidataEvaluada E(string p, int grupo, string motivo) => new CandidataEvaluada
            {
                Candidata = new CandidataOferta { Producto = p, Campana = BF, Descuento = 0.3m }, Grupo = grupo, Motivo = motivo, Texto = "t"
            };
            var evaluadas = new List<CandidataEvaluada>
            {
                E("toca", 1, MotivosOfertaParaTi.TE_TOCA), E("habitual", 2, MotivosOfertaParaTi.LO_SUELES_PEDIR), E("marca", 3, MotivosOfertaParaTi.DE_TUS_MARCAS),
                E("perfil", 4, MotivosOfertaParaTi.TE_PUEDE_INTERESAR), E("nada", 4, MotivosOfertaParaTi.TE_PUEDE_INTERESAR),
                E("parecido", 4, MotivosOfertaParaTi.TE_PUEDE_INTERESAR), E("junto", 4, MotivosOfertaParaTi.TE_PUEDE_INTERESAR)
            };
            var f = new FuentesClienteOfertas
            {
                CompradoJunto = new Dictionary<string, Tuple<decimal, string>> { { "junto", Tuple.Create(0.5m, "X") } },
                Parecidos = new Dictionary<string, decimal> { { "parecido", 0.2m } },
                Perfil = new Dictionary<string, int> { { "perfil", 30 } }
            };

            List<OfertaElegida> reglas = ReglaRankingOfertasParaTi.Clasificar(evaluadas, f);
            CollectionAssert.AreEqual(new[] { "toca", "habitual", "junto", "parecido", "marca", "perfil", "nada" }, reglas.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(ReglaRankingOfertasParaTi.TEXTO_PERFIL, reglas[5].Texto);

            List<OfertaElegida> conModelo = ReglaRankingOfertasParaTi.Ordenar(reglas, new Dictionary<string, float> { { "nada", 0.9f }, { "marca", 0.5f } });
            CollectionAssert.AreEqual(new[] { "nada", "marca", "toca", "habitual", "junto", "parecido", "perfil" }, conModelo.Select(o => o.Candidata.Producto).ToList());
            Assert.IsTrue(conModelo.All(o => o.Personal), "«nada» lo ha puntuado el modelo");
        }

        // ---------------------------------------------------------------- las fuentes de la plantilla, por su repositorio

        [TestMethod]
        public async Task Fuentes_ParesAfinidadYPerfil_SoloDeLaCampanaYSinLoSuyo()
        {
            IRepositorioSugerenciasProductos repo = A.Fake<IRepositorioSugerenciasProductos>();
            var compras = new List<CompraDiaReposicion>
            {
                new CompraDiaReposicion { Cliente = "1000", Producto = "100", Dia = new DateTime(2026, 10, 1), BaseImponible = 50, Cantidad = 1 },
                new CompraDiaReposicion { Cliente = "1000", Producto = "100", Dia = new DateTime(2026, 11, 1), BaseImponible = 50, Cantidad = 1 }
            };
            A.CallTo(() => repo.LeerFichas(A<string>._, A<IEnumerable<string>>._)).Returns(new Dictionary<string, FichaProductoSugerencia>
            {
                { "100", new FichaProductoSugerencia { Producto = "100", Nombre = "JABON ISDRUC", Grupo = "COS", SubGrupo = "CRE", Estado = 0 } }
            });
            A.CallTo(() => repo.LeerPares(A<IEnumerable<string>>._)).Returns(new List<ParCompradoJunto>
            {
                new ParCompradoJunto { ProductoA = "100", ProductoB = "3", Confianza = 0.4m, Lift = 2 },
                new ParCompradoJunto { ProductoA = "100", ProductoB = "999", Confianza = 0.9m, Lift = 3 } // no es de la campaña
            });
            A.CallTo(() => repo.LeerAfinidad("1000")).Returns(new List<AfinidadCliente>
            {
                new AfinidadCliente { Cliente = "1000", Producto = "1", Afinidad = 0.7m },
                new AfinidadCliente { Cliente = "1000", Producto = "999", Afinidad = 0.9m }
            });
            A.CallTo(() => repo.LeerClientesPerfil("COS", "CRE")).Returns(new Dictionary<string, int> { { "2", 40 }, { "100", 90 } });
            var campana = new HashSet<string>(new[] { "1", "2", "3" }, StringComparer.OrdinalIgnoreCase);

            FuentesClienteOfertas f = await new FuentesSugerenciasOfertasSql(repo).Leer("1", "1000", HOY, compras, campana);

            Assert.AreEqual("JABON ISDRUC", f.CompradoJunto["3"].Item2);
            Assert.AreEqual(1, f.CompradoJunto.Count);
            Assert.AreEqual(0.7m, f.Parecidos.Single(p => p.Key == "1").Value);
            Assert.AreEqual(1, f.Parecidos.Count);
            Assert.AreEqual(40, f.Perfil["2"]);
            Assert.AreEqual(1, f.Perfil.Count, "Lo que ya compra no es de su perfil");
            A.CallTo(() => repo.LeerPares(A<IEnumerable<string>>.That.Matches(a => a.Single() == "100"))).MustHaveHappenedOnceExactly();
        }

        // ---------------------------------------------------------------- endpoint de la franja

        [TestMethod]
        public async Task Cliente_ConDescuentosPropiosQueAnulanLasCincoPrimeras_SeRellenaConLasSiguientesQueMejoran()
        {
            // Caso 15191: casi nada del Black Friday le mejora su precio de siempre. Antes se elegían 5 y se quitaban: salía UNA.
            Campana(12);
            ModeloQuePuntuaTodo();
            Precios("1", "2", "3", "4", "5");

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("15191", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            CollectionAssert.AreEqual(new[] { "6", "7", "8", "9", "10" }, r.Ofertas.Select(o => o.Producto).ToList());
            Assert.IsTrue(r.HayOfertas);
            Assert.AreEqual(5, r.Total);
            Assert.IsNull(r.Mensaje);
            Assert.IsTrue(r.HayMasOfertas, "Quedan el 11 y el 12");
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustHaveHappened(10, Times.Exactly);
        }

        [TestMethod]
        public async Task Cliente_ComoMuchoDosPorFamiliaSiHayAlternativas()
        {
            Campana(6, i => i <= 4 ? "LISAP" : "F" + i);
            ModeloQuePuntuaTodo();

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("1000", BF, 4, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            CollectionAssert.AreEqual(new[] { "1", "2", "5", "6" }, r.Ofertas.Select(o => o.Producto).ToList());
        }

        [TestMethod]
        public async Task Cliente_NingunaMejora_SinOfertasConMensajeDeTu()
        {
            Campana(8);
            ModeloQuePuntuaTodo();
            Precios("1", "2", "3", "4", "5", "6", "7", "8");

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("15191", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsFalse(r.HayOfertas);
            Assert.AreEqual(0, r.Total);
            Assert.AreEqual(0, r.Ofertas.Count);
            Assert.IsFalse(r.HayMasOfertas);
            Assert.AreEqual("Ahora mismo no hay ofertas que mejoren tus precios habituales.", r.Mensaje);
            Assert.AreEqual(BF, r.Campana, "La cabecera sigue saliendo");
        }

        [TestMethod]
        public async Task Cliente_SiSoloMejoranDos_SeDevuelvenEsasDos()
        {
            Campana(6);
            ModeloQuePuntuaTodo();
            Precios("1", "3", "4", "6");

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("15191", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            CollectionAssert.AreEqual(new[] { "2", "5" }, r.Ofertas.Select(o => o.Producto).ToList());
            Assert.AreEqual(2, r.Total);
        }

        [TestMethod]
        public async Task Cliente_TopeDeConsultasDePrecio()
        {
            Campana(100);
            ModeloQuePuntuaTodo();
            Precios(Enumerable.Range(1, 100).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray());

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("15191", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsFalse(r.HayOfertas);
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .MustHaveHappened(ReglaSeleccionOfertasParaTi.TOPE_CONSULTAS_PRECIO, Times.Exactly);
        }

        [TestMethod]
        public async Task Cliente_SinHistorial_SeOrdenaPorElModelo_SinRellenoGenerico()
        {
            ModeloQuePuntuaTodo();

            OfertasParaTiRespuestaDTO con = await Servicio().Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            CollectionAssert.AreEqual(new[] { "1", "3" }, con.Ofertas.Select(o => o.Producto).ToList());
            Assert.IsTrue(con.Ofertas.All(o => o.Motivo == MotivosOfertaParaTi.TE_PUEDE_INTERESAR));

            A.CallTo(() => puntuador.ModeloDisponible()).Returns(null);
            OfertasParaTiRespuestaDTO sin = await Servicio().Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            Assert.IsFalse(sin.HayOfertas, "Si el modelo no dice nada de él, nada (ni lo más vendido)");
        }

        [TestMethod]
        public async Task Cliente_LosPreciosYElRankingNoSeRepitenConElMismoContexto()
        {
            ModeloQuePuntuaTodo();
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            ServicioOfertasCliente servicio = Servicio(nucleo, (c, d) => Task.FromResult(ctx));

            _ = await servicio.Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            OfertasParaTiRespuestaDTO r = await servicio.Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.AreEqual(2, r.Total);
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustHaveHappened(2, Times.Exactly);
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cliente_CabeceraDeLaCampanaYPrecios()
        {
            ModeloQuePuntuaTodo();
            A.CallTo(() => precios.Calcular(A<string>._, "1000", "0", "1", BF)).Returns(new PreciosOferta { Precio = 8.75m, PrecioAntes = 8.75m, Tarifa = 12.5m });
            A.CallTo(() => precios.Calcular(A<string>._, "1000", "0", "3", BF)).Returns(new PreciosOferta { Precio = 8.75m, PrecioAntes = 12.5m, Tarifa = 12.5m });

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("1000", null, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsTrue(r.HayOfertas);
            Assert.AreEqual(BF, r.Campana);
            Assert.AreEqual("Black Friday", r.Titulo);
            Assert.AreEqual("2026-11-30", r.FechaHasta);
            Assert.AreEqual("Hasta el lunes 30 de noviembre", r.TextoHasta);
            OfertaParaTiDTO o = r.Ofertas.Single();
            Assert.AreEqual("3", o.Producto);
            Assert.AreEqual("Producto 3", o.Nombre);
            Assert.AreEqual(8.75m, o.Precio);
            Assert.AreEqual(12.5m, o.PrecioAntes);
            Assert.AreEqual(0.3m, o.Descuento);
            Assert.AreEqual("https://tienda.nuevavision.es/3.jpg", o.UrlFoto);
        }

        [TestMethod]
        public async Task Cliente_SinPrecios_SinImportes()
        {
            ModeloQuePuntuaTodo();
            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.SinPrecios, HOY);

            Assert.IsTrue(r.PrecioOculto);
            Assert.AreEqual(2, r.Ofertas.Count);
            Assert.IsTrue(r.Ofertas.All(o => o.Precio == null && o.PrecioAntes == null && o.Descuento == null));
        }

        [TestMethod]
        public async Task Cliente_SiTodoFalla_SinOfertasYNoLanza()
        {
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Throws(new Exception("BD caída"));

            OfertasParaTiRespuestaDTO r = await Servicio().Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsFalse(r.HayOfertas);
            Assert.AreEqual(0, r.Ofertas.Count);
            Assert.AreEqual(BF, r.Campana);
            Assert.IsNotNull(r.Mensaje);
            Assert.AreEqual(1, errores.Count);
        }

        // ---------------------------------------------------------------- «Ver todas»

        [TestMethod]
        public async Task Todas_PaginaPorElRankingConSiguienteYFamiliasSoloEnLaPrimera()
        {
            Campana(30, i => i % 2 == 0 ? "LISAP" : "EVA");
            ModeloQuePuntuaTodo();
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            ServicioOfertasCliente servicio = Servicio(nucleo, (c, d) => Task.FromResult(ctx));

            TodasOfertasParaTiRespuestaDTO p1 = await servicio.Todas("1000", BF, 0, null, null, null, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            TodasOfertasParaTiRespuestaDTO p2 = await servicio.Todas("1000", BF, p1.Siguiente.Value, null, null, null, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.AreEqual(20, p1.Ofertas.Count, "Sin diversidad: es la lista entera por probabilidad");
            Assert.AreEqual("1", p1.Ofertas[0].Producto);
            Assert.AreEqual(20, p1.Siguiente);
            Assert.IsTrue(p1.HayMas);
            Assert.AreEqual(30, p1.TotalCampana);
            Assert.AreEqual(2, p1.Familias.Count);
            Assert.AreEqual(15, p1.Familias.Single(f => f.Familia == "Lisap").Productos);
            Assert.AreEqual(10, p2.Ofertas.Count);
            Assert.AreEqual("21", p2.Ofertas[0].Producto);
            Assert.IsNull(p2.Siguiente);
            Assert.IsFalse(p2.HayMas);
            Assert.IsNull(p2.Familias);
        }

        [TestMethod]
        public async Task Todas_SaltaLasQueNoMejoranYFiltraPorFamiliaYTextoSinTildes()
        {
            Campana(10, i => i <= 5 ? "LISAP" : "EVA");
            ModeloQuePuntuaTodo();
            Precios("2");

            TodasOfertasParaTiRespuestaDTO familia = await Servicio().Todas("1000", BF, 0, 3, "lisap", null, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            CollectionAssert.AreEqual(new[] { "1", "3", "4" }, familia.Ofertas.Select(o => o.Producto).ToList());
            Assert.AreEqual(4, familia.Siguiente, "Se ha mirado hasta el 4 (posición 3 del filtrado)");

            TodasOfertasParaTiRespuestaDTO texto = await Servicio().Todas("1000", BF, 0, 20, null, "PRODÚCTO 7", PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);
            Assert.AreEqual("7", texto.Ofertas.Single().Producto);
            Assert.IsFalse(texto.HayMas);
        }

        [TestMethod]
        public async Task Todas_TopeDeExaminadosPorPeticion_DevuelveLoQueHayConSiguiente()
        {
            Campana(200);
            ModeloQuePuntuaTodo();
            Precios(Enumerable.Range(1, 200).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray());

            TodasOfertasParaTiRespuestaDTO r = await Servicio().Todas("15191", BF, 0, 20, null, null, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.AreEqual(0, r.Ofertas.Count);
            Assert.IsTrue(r.HayMas);
            Assert.AreEqual(ReglaSeleccionOfertasParaTi.TOPE_EXAMINADOS_TODAS, r.Siguiente);
        }

        [TestMethod]
        public void Todas_CuantasEntre1y50()
        {
            Assert.AreEqual(20, ReglaSeleccionOfertasParaTi.CuantasTodas(null));
            Assert.AreEqual(1, ReglaSeleccionOfertasParaTi.CuantasTodas(0));
            Assert.AreEqual(50, ReglaSeleccionOfertasParaTi.CuantasTodas(80));
        }

        // ---------------------------------------------------------------- push

        private IFuentePushCampana Fuente(params string[] conApp)
        {
            IFuentePushCampana fuente = A.Fake<IFuentePushCampana>();
            A.CallTo(() => fuente.ClientesConConsejos()).Returns(conApp.ToList());
            A.CallTo(() => fuente.ClientesDeBaja()).Returns(new HashSet<string>());
            A.CallTo(() => fuente.ClientesConPushDeCampana(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HashSet<string>());
            return fuente;
        }

        private ServicioPushCampana Push(IFuentePushCampana fuente, IServicioNotificacionesPush push, Func<string, Task<string>> foto = null)
            => new ServicioPushCampana(Nucleo(), fuente, push, precios, c => Task.FromResult("0"), foto ?? (p => Task.FromResult<string>(null)), errores.Add);

        private void CompradoJuntoPara(string cliente, string producto, string nombreSuyo)
            => A.CallTo(() => fuentes.Leer(A<string>._, cliente, A<DateTime>._, A<IReadOnlyCollection<CompraDiaReposicion>>._, A<ISet<string>>._))
                .Returns(new FuentesClienteOfertas
                {
                    CompradoJunto = new Dictionary<string, Tuple<decimal, string>> { { producto, Tuple.Create(0.5m, nombreSuyo) } }
                });

        [TestMethod]
        public async Task Push_Ensayo_NoEnviaNiEscribe_YNombraSoloLoSuyo()
        {
            ModeloQuePuntuaTodo();
            CompradoJuntoPara("1000", "3", "JABON ISDRUC");
            IFuentePushCampana fuente = Fuente("1000", "2000");
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();

            ResultadoPushCampana r = await Push(fuente, push, p => Task.FromResult("https://tienda.nuevavision.es/" + p + ".jpg"))
                .Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");

            Assert.AreEqual(EstadoPushCampana.Ok, r.Estado);
            Assert.IsTrue(r.Respuesta.Ensayo);
            Assert.AreEqual(2, r.Respuesta.Destinatarios);
            Assert.AreEqual(2, r.Respuesta.ConOfertas);
            MensajePushCampanaDTO suyo = r.Respuesta.Mensajes.Single(m => m.Cliente == "1000");
            Assert.AreEqual("Black Friday", suyo.Titulo);
            Assert.AreEqual("Producto 3 (−30 %) y 1 más pensados para ti.", suyo.Cuerpo, "Solo se nombra lo suyo (comprado junto), no lo que solo puntúa el modelo");
            MensajePushCampanaDTO generico = r.Respuesta.Mensajes.Single(m => m.Cliente == "2000");
            Assert.AreEqual("Mira las ofertas del Black Friday que hemos elegido para ti.", generico.Cuerpo, "Todo «te puede interesar»: texto genérico");
            CollectionAssert.AreEquivalent(new[] { "1", "3" }, generico.Productos);
            Assert.AreEqual("https://tienda.nuevavision.es/1.jpg", generico.ImagenUrl);
            Assert.AreEqual(0, suyo.Enviados);
            Assert.IsTrue(r.Respuesta.ConsultasPrecio > 0);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => fuente.Anotar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Push_DeVerdad_EnviaAnotaYUnoQueFallaNoParaALosDemas()
        {
            ModeloQuePuntuaTodo();
            IFuentePushCampana fuente = Fuente("1000", "2000");
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, "1000", A<NotificacionPushDTO>._)).Throws(new Exception("FCM"));
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, "2000", A<NotificacionPushDTO>._)).Returns(2);

            ResultadoPushCampana r = await Push(fuente, push).Ejecutar(new PeticionCampanaPushDTO { Campana = BF, ImagenUrl = "https://x.es/bf.jpg" }, false, AHORA, "Carlos");

            Assert.AreEqual(2, r.Respuesta.Mensajes.Single().Enviados);
            Assert.AreEqual("2000", r.Respuesta.Mensajes.Single().Cliente);
            Assert.AreEqual("https://x.es/bf.jpg", r.Respuesta.Mensajes.Single().ImagenUrl);
            Assert.IsTrue(r.Respuesta.Saltados.Any(s => s.Cliente == "1000" && s.Motivo.StartsWith("Error")));
            A.CallTo(() => push.EnviarConsejoACliente("1", "2000", A<NotificacionPushDTO>.That.Matches(n =>
                n.Datos["tipo"] == "ofertas" && n.Datos["campana"] == BF && !n.Datos.ContainsKey("productos")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => fuente.Anotar(A<string>.That.Matches(s => s.Contains("\"ConOfertas\":2") && s.Contains("\"ClientesEnviados\":[\"2000\"]")), "Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Push_YaRecibidaEnLosDiasSinRepetir_SeSalta()
        {
            ModeloQuePuntuaTodo();
            IFuentePushCampana fuente = Fuente("1000", "2000");
            A.CallTo(() => fuente.ClientesConPushDeCampana(BF, A<IReadOnlyCollection<string>>._, new DateTime(2026, 11, 25)))
                .Returns(new HashSet<string> { "1000" });

            ResultadoPushCampana r = await Push(fuente, A.Fake<IServicioNotificacionesPush>())
                .Ejecutar(new PeticionCampanaPushDTO { Campana = BF, DiasSinRepetir = 2 }, true, AHORA, "Carlos");

            Assert.AreEqual(1, r.Respuesta.Destinatarios);
            Assert.AreEqual("2000", r.Respuesta.Mensajes.Single().Cliente);
            Assert.AreEqual("1000", r.Respuesta.Saltados.Single().Cliente);
        }

        [TestMethod]
        public async Task Push_SinOfertasQueLeMejoren_PorDefectoNoSeLeManda_ConIncluirSinOfertasElGenerico()
        {
            ModeloQuePuntuaTodo();
            Precios("1", "2", "3");
            ServicioPushCampana servicio = Push(Fuente("1000"), A.Fake<IServicioNotificacionesPush>());

            ResultadoPushCampana porDefecto = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");
            Assert.AreEqual(0, porDefecto.Respuesta.Mensajes.Count);
            Assert.AreEqual(1, porDefecto.Respuesta.SinOfertas);
            Assert.AreEqual("No hay ofertas que le mejoren el precio.", porDefecto.Respuesta.Saltados.Single().Motivo);

            ResultadoPushCampana generico = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF, IncluirSinOfertas = true }, true, AHORA, "Carlos");
            Assert.AreEqual("Ya tienes las ofertas de Black Friday en la app. ¡Échales un vistazo!", generico.Respuesta.Mensajes.Single().Cuerpo);
            Assert.AreEqual(0, generico.Respuesta.Mensajes.Single().Productos.Count);
        }

        [TestMethod]
        public async Task Push_DeVerdadFueraDeHorario_NoSeManda()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            ResultadoPushCampana r = await Push(Fuente("1000"), push)
                .Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, false, new DateTime(2026, 11, 26, 22, 0, 0), "Carlos");

            Assert.AreEqual(EstadoPushCampana.PeticionIncorrecta, r.Estado);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            Assert.AreEqual(ReglaPushCampana.MENSAJE_FUERA_DE_HORARIO, ServicioPushCampana.ValidarParaEnviar(new PeticionCampanaPushDTO { Campana = BF }, new DateTime(2026, 11, 26, 22, 0, 0)));
            Assert.IsNull(ServicioPushCampana.ValidarParaEnviar(new PeticionCampanaPushDTO { Campana = BF }, AHORA));
        }

        [TestMethod]
        public async Task Push_CampanaSinOfertasVigentes_400()
        {
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<FilaCampanaOferta>());
            ResultadoPushCampana r = await Push(Fuente("1000"), A.Fake<IServicioNotificacionesPush>())
                .Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");

            Assert.AreEqual(EstadoPushCampana.PeticionIncorrecta, r.Estado);
        }

        [TestMethod]
        public void Push_Destinatarios_LosDispositivosConEmpresaNullCuentan()
        {
            // Caso 25/11/26: los 60 dispositivos de NestoTiendas tienen Empresa NULL (la app no la manda) y la push no encontraba a nadie
            DispositivoNotificacion D(string cliente, string empresa, bool activo = true, bool consejos = true, string app = Constantes.Aplicaciones.NESTO_TIENDAS)
                => new DispositivoNotificacion { Cliente = cliente, Empresa = empresa, Activo = activo, RecibirConsejos = consejos, Aplicacion = app };
            var dispositivos = new List<DispositivoNotificacion>
            {
                D("1000", null), D("2000", "1"), D("3000", "3"), D("4000", null, activo: false), D("5000", null, consejos: false),
                D("6000", null, app: "NestoApp"), D(null, null)
            };

            List<string> clientes = dispositivos.AsQueryable().Where(FuentePushCampanaSql.FiltroDestinatarios("1")).Select(d => d.Cliente).ToList();

            CollectionAssert.AreEqual(new[] { "1000", "2000" }, clientes);
        }
    }
}
