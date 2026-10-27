using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.SugerenciasProductos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#707: guardar lo enviado en el correo post-compra (best-effort), grupo control, orden por puntuación e informe
    /// de conversión.
    /// </summary>
    [TestClass]
    public class RecomendacionesPostCompraMedicionTests
    {
        private static readonly DateTime SABADO = new DateTime(2026, 10, 31, 10, 0, 0);

        #region Registro (corte 1)

        [TestMethod]
        public void ConstruirFilas_CompradosYRecomendadosQueSalen_ConPosicionReglaYVersion()
        {
            CorreoPostCompraClienteDTO correo = Correo("15191",
                comprados: new[] { "C1", "C2" },
                recomendados: new[] { Recomendado("R1", "pares", 0.42m), Recomendado("R2", null, null), Recomendado("SINTIENDA", "pares", 0.9m, enlaceTienda: null) });

            List<FilaRecomendacionPostCompra> filas = RegistroRecomendacionesPostCompra.ConstruirFilas(correo, SABADO);

            Assert.AreEqual(4, filas.Count, "El recomendado sin enlace a la tienda no sale en el correo (RecomendadosConEnlaces): no se guarda");
            Assert.IsTrue(filas.All(f => f.FechaEnvio == SABADO && f.Empresa == "1" && f.Cliente == "15191" && f.Contacto == "0" && f.Correo == "cliente@example.com"));
            CollectionAssert.AreEqual(new[] { "C1", "C2" }, filas.Where(f => f.Tipo == TiposRecomendacionPostCompra.COMPRADO).Select(f => f.Producto).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2 }, filas.Where(f => f.Tipo == TiposRecomendacionPostCompra.COMPRADO).Select(f => f.Posicion).ToArray());
            Assert.IsTrue(filas.Where(f => f.Tipo == TiposRecomendacionPostCompra.COMPRADO).All(f => f.Regla == null && f.Puntuacion == null && f.VersionModelo == null));
            var recomendados = filas.Where(f => f.Tipo == TiposRecomendacionPostCompra.RECOMENDADO).ToList();
            Assert.AreEqual("R1", recomendados[0].Producto);
            Assert.AreEqual(1, recomendados[0].Posicion);
            Assert.AreEqual("pares", recomendados[0].Regla);
            Assert.AreEqual(0.42m, recomendados[0].Puntuacion);
            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.VERSION_MODELO, recomendados[0].VersionModelo);
            Assert.AreEqual(2, recomendados[1].Posicion);
            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL, recomendados[1].Regla, "Sin regla (programado antes de #707): orden_original");
            Assert.IsFalse(filas.Any(f => f.GrupoControl));
        }

        [TestMethod]
        public void ConstruirFilas_GrupoControl_GuardaLosNoMostradosConGrupoControl()
        {
            CorreoPostCompraClienteDTO correo = Correo("15191", new[] { "C1" }, new[] { Recomendado("R1", "afinidad", 0.3m) });
            GrupoControlPostCompra.Aplicar(new[] { correo }, 100);

            List<FilaRecomendacionPostCompra> filas = RegistroRecomendacionesPostCompra.ConstruirFilas(correo, SABADO);

            Assert.AreEqual(0, correo.ProductosRecomendados.Count, "En el correo no sale ningún recomendado");
            Assert.AreEqual(2, filas.Count);
            Assert.IsTrue(filas.All(f => f.GrupoControl));
            Assert.AreEqual("R1", filas.Single(f => f.Tipo == TiposRecomendacionPostCompra.RECOMENDADO).Producto);
        }

        [TestMethod]
        public async Task Registrar_ConTabla_InsertaLasFilas()
        {
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);

            bool guardado = await RegistroRecomendacionesPostCompra.Registrar(repositorio,
                Correo("15191", new[] { "C1" }, new[] { Recomendado("R1", "pares", 0.5m) }), SABADO, ex => Assert.Fail(ex.Message));

            Assert.IsTrue(guardado);
            A.CallTo(() => repositorio.Insertar(A<IList<FilaRecomendacionPostCompra>>.That.Matches(f => f.Count == 2))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Registrar_SinTabla_AvisaYNoInsertaNiLanza()
        {
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            var avisos = new List<Exception>();

            bool guardado = await RegistroRecomendacionesPostCompra.Registrar(repositorio, Correo("15191", new[] { "C1" }, null), SABADO, avisos.Add);

            Assert.IsFalse(guardado);
            Assert.AreEqual(1, avisos.Count);
            StringAssert.Contains(avisos[0].Message, "Issue707_CorreosPostCompraRecomendaciones.sql");
            A.CallTo(() => repositorio.Insertar(A<IList<FilaRecomendacionPostCompra>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Registrar_FallaElInsert_AvisaYNoLanza()
        {
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.Insertar(A<IList<FilaRecomendacionPostCompra>>._)).ThrowsAsync(new InvalidOperationException("timeout"));
            var avisos = new List<Exception>();

            bool guardado = await RegistroRecomendacionesPostCompra.Registrar(repositorio, Correo("15191", new[] { "C1" }, null), SABADO, avisos.Add);

            Assert.IsFalse(guardado);
            Assert.AreEqual(1, avisos.Count);
            Assert.IsInstanceOfType(avisos[0].InnerException, typeof(InvalidOperationException));
        }

        [TestMethod]
        public async Task EnviarYRegistrar_FallaElRegistro_ElCorreoSaleIgual()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            A.CallTo(() => repositorio.TablaExiste()).ThrowsAsync(new Exception("sin conexión"));

            using (var mail = new MailMessage())
            {
                bool enviado = await CorreosPostCompraJobsService.EnviarYRegistrar(correo, mail,
                    Correo("15191", new[] { "C1" }, null), repositorio, ex => { });

                Assert.IsTrue(enviado);
                A.CallTo(() => correo.EnviarCorreoSMTP(mail)).MustHaveHappenedOnceExactly();
            }
        }

        [TestMethod]
        public async Task EnviarYRegistrar_ElCorreoNoSale_NoSeGuarda()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(false);
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();

            using (var mail = new MailMessage())
            {
                bool enviado = await CorreosPostCompraJobsService.EnviarYRegistrar(correo, mail,
                    Correo("15191", new[] { "C1" }, null), repositorio, ex => Assert.Fail(ex.Message));

                Assert.IsFalse(enviado);
                A.CallTo(() => repositorio.TablaExiste()).MustNotHaveHappened();
                A.CallTo(() => repositorio.Insertar(A<IList<FilaRecomendacionPostCompra>>._)).MustNotHaveHappened();
            }
        }

        [TestMethod]
        public async Task EnviarYRegistrar_SinRepositorio_ModoTest_SoloEnvia()
        {
            var correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);

            using (var mail = new MailMessage())
            {
                Assert.IsTrue(await CorreosPostCompraJobsService.EnviarYRegistrar(correo, mail, Correo("15191", new[] { "C1" }, null), null));
            }
        }

        #endregion

        #region Grupo control

        [TestMethod]
        public void EsGrupoControl_CeroPorCiento_Nadie()
        {
            for (int i = 0; i < 500; i++)
            {
                Assert.IsFalse(GrupoControlPostCompra.EsGrupoControl("1", i.ToString(), 0));
            }
        }

        [TestMethod]
        public void EsGrupoControl_CienPorCiento_Todos()
        {
            Assert.IsTrue(GrupoControlPostCompra.EsGrupoControl("1", "15191", 100));
        }

        [TestMethod]
        public void EsGrupoControl_EsDeterministaPorClienteSinEspaciosNiMayusculas()
        {
            for (int i = 0; i < 200; i++)
            {
                string cliente = (10000 + i).ToString();
                bool primera = GrupoControlPostCompra.EsGrupoControl("1", cliente, 10);
                Assert.AreEqual(primera, GrupoControlPostCompra.EsGrupoControl("1  ", cliente + "   ", 10));
                Assert.AreEqual(GrupoControlPostCompra.Cubo("1", cliente), GrupoControlPostCompra.Cubo("1", cliente));
            }
            Assert.AreEqual(GrupoControlPostCompra.Cubo("1", "abc"), GrupoControlPostCompra.Cubo("1", "ABC"));
        }

        [TestMethod]
        public void EsGrupoControl_DiezPorCiento_ReparteAproximadamenteUnoDeCadaDiez()
        {
            int enGrupo = Enumerable.Range(10000, 5000).Count(c => GrupoControlPostCompra.EsGrupoControl("1", c.ToString(), 10));

            Assert.IsTrue(enGrupo > 350 && enGrupo < 650, $"Con el 10 % de 5000 clientes han caído {enGrupo}");
        }

        [TestMethod]
        public void EsGrupoControl_AlSubirElPorcentaje_LosQueEstabanSiguenDentro()
        {
            foreach (string cliente in Enumerable.Range(10000, 1000).Select(c => c.ToString()))
            {
                if (GrupoControlPostCompra.EsGrupoControl("1", cliente, 5))
                {
                    Assert.IsTrue(GrupoControlPostCompra.EsGrupoControl("1", cliente, 10));
                }
            }
        }

        [TestMethod]
        public void Aplicar_CeroPorCiento_NoTocaNada()
        {
            CorreoPostCompraClienteDTO correo = Correo("15191", new[] { "C1" }, new[] { Recomendado("R1", "pares", 0.5m) });

            int enGrupo = GrupoControlPostCompra.Aplicar(new[] { correo }, 0);

            Assert.AreEqual(0, enGrupo);
            Assert.IsFalse(correo.GrupoControl);
            Assert.AreEqual(1, correo.ProductosRecomendados.Count);
            Assert.AreEqual(0, correo.ProductosRecomendadosNoMostrados.Count);
        }

        [TestMethod]
        public void Porcentaje_ValoresRaros_CeroOAcotado()
        {
            Assert.AreEqual(0, GrupoControlPostCompra.Porcentaje(null));
            Assert.AreEqual(0, GrupoControlPostCompra.Porcentaje("diez"));
            Assert.AreEqual(0, GrupoControlPostCompra.Porcentaje("-5"));
            Assert.AreEqual(100, GrupoControlPostCompra.Porcentaje("250"));
            Assert.AreEqual(10, GrupoControlPostCompra.Porcentaje(" 10 "));
        }

        [TestMethod]
        public void LeerPorcentaje_SinClaveEnElConfigDeTests_Cero()
        {
            Assert.AreEqual(0, GrupoControlPostCompra.LeerPorcentaje());
        }

        #endregion

        #region Puntuación (corte 2)

        [TestMethod]
        public void Puntuar_SinDatos_TodosOrdenOriginalSinPuntuacion()
        {
            Dictionary<string, PuntuacionCandidato> puntuaciones = PuntuadorRecomendacionesPostCompra.Puntuar(new[] { "A", "B" }, null);

            Assert.AreEqual(2, puntuaciones.Count);
            Assert.IsTrue(puntuaciones.Values.All(p => p.Regla == PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL && p.Puntuacion == null));
        }

        [TestMethod]
        public void Puntuar_CombinaParesAfinidadYPopularidad()
        {
            var datos = new DatosPuntuacionRecomendados
            {
                ProductosComprados = new[] { "COMPRADO" },
                Pares = new[]
                {
                    new ParCompradoJunto { ProductoA = "COMPRADO", ProductoB = "PAR", Confianza = 0.8m },
                    new ParCompradoJunto { ProductoA = "OTRO", ProductoB = "AFIN", Confianza = 1m } // no es de lo comprado: no cuenta
                },
                Afinidad = new[] { new AfinidadCliente { Producto = "AFIN", Afinidad = 4m }, new AfinidadCliente { Producto = "PAR", Afinidad = 1m } },
                Popularidad = new[] { new PopularidadProducto { Producto = "POPULAR", Clientes12Meses = 300 }, new PopularidadProducto { Producto = "PAR", Clientes12Meses = 3 } }
            };

            Dictionary<string, PuntuacionCandidato> p = PuntuadorRecomendacionesPostCompra.Puntuar(new[] { "NADA", "POPULAR", "AFIN", "PAR" }, datos);

            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.REGLA_PARES, p["PAR"].Regla);
            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.REGLA_AFINIDAD, p["AFIN"].Regla);
            Assert.AreEqual(0.3m, p["AFIN"].Puntuacion, "Afinidad máxima de los candidatos: 0,3 × 1");
            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.REGLA_POPULARIDAD, p["POPULAR"].Regla);
            Assert.AreEqual(0.2m, p["POPULAR"].Puntuacion, "Popularidad máxima de los candidatos: 0,2 × 1");
            Assert.AreEqual(PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL, p["NADA"].Regla);
            Assert.IsNull(p["NADA"].Puntuacion);
            // PAR: 0,5 × 0,8 + 0,3 × (1/4) + 0,2 × log(4)/log(301)
            decimal esperada = Math.Round(0.4m + 0.075m + 0.2m * (decimal)(Math.Log(4) / Math.Log(301)), 4, MidpointRounding.AwayFromZero);
            Assert.AreEqual(esperada, p["PAR"].Puntuacion);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_ConDatos_OrdenaPorPuntuacionYDejaLosSinDatosDetrasEnSuOrden()
        {
            var productos = new[] { "SIN1", "POP", "SIN2", "PAR", "AFIN" }
                .Select((id, i) => new DatosProductoEnVideo { ProductoId = id, NombreProducto = id, VideoYoutubeId = "v1", EnlaceVideo = "link" + i, Familia = "FAM" })
                .ToList();
            var datos = new DatosPuntuacionRecomendados
            {
                ProductosComprados = new[] { "COMPRADO" },
                Pares = new[] { new ParCompradoJunto { ProductoA = "COMPRADO", ProductoB = "PAR", Confianza = 0.9m } },
                Afinidad = new[] { new AfinidadCliente { Producto = "AFIN", Afinidad = 2m } },
                Popularidad = new[] { new PopularidadProducto { Producto = "POP", Clientes12Meses = 50 } }
            };

            List<ProductoRecomendadoDTO> resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productos, Vacio(), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "COMPRADO" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FAM" }, datos);

            CollectionAssert.AreEqual(new[] { "PAR", "AFIN", "POP", "SIN1" }, resultado.Select(r => r.ProductoId).ToArray(), "4 como máximo, los puntuados primero");
            CollectionAssert.AreEqual(new[] { "pares", "afinidad", "popularidad", "orden_original" }, resultado.Select(r => r.Regla).ToArray());
            Assert.IsNull(resultado[3].Puntuacion);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_SinDatos_OrdenDeLaConsultaComoAntes()
        {
            var productos = new[] { "A", "B", "C", "D", "E" }
                .Select((id, i) => new DatosProductoEnVideo { ProductoId = id, NombreProducto = id, VideoYoutubeId = "v1", EnlaceVideo = "link" + i })
                .ToList();

            List<ProductoRecomendadoDTO> resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productos, Vacio(), Vacio(), Vacio(), new DatosPuntuacionRecomendados());

            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, resultado.Select(r => r.ProductoId).ToArray());
            Assert.IsTrue(resultado.All(r => r.Regla == PuntuadorRecomendacionesPostCompra.REGLA_ORDEN_ORIGINAL && r.Puntuacion == null));
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_ConDatos_MantieneFiltrosYQuitaEnlaceRepetidoQuedandoseElMejor()
        {
            var productos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = "PEOR", VideoYoutubeId = "v1", EnlaceVideo = "mismo", Familia = "FAM" },
                new DatosProductoEnVideo { ProductoId = "MEJOR", VideoYoutubeId = "v1", EnlaceVideo = "mismo", Familia = "FAM" },
                new DatosProductoEnVideo { ProductoId = "YACOMPRADO", VideoYoutubeId = "v1", EnlaceVideo = "otro1", Familia = "FAM" },
                new DatosProductoEnVideo { ProductoId = "OTRAFAMILIA", VideoYoutubeId = "v1", EnlaceVideo = "otro2", Familia = "XXX", TipoExclusiva = "MAD" }
            };
            var datos = new DatosPuntuacionRecomendados
            {
                ProductosComprados = new[] { "COMPRADO" },
                Pares = new[]
                {
                    new ParCompradoJunto { ProductoA = "COMPRADO", ProductoB = "MEJOR", Confianza = 0.9m },
                    new ParCompradoJunto { ProductoA = "COMPRADO", ProductoB = "YACOMPRADO", Confianza = 1m },
                    new ParCompradoJunto { ProductoA = "COMPRADO", ProductoB = "OTRAFAMILIA", Confianza = 1m }
                }
            };

            List<ProductoRecomendadoDTO> resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productos, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "YACOMPRADO" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "COMPRADO" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FAM" }, datos);

            CollectionAssert.AreEqual(new[] { "MEJOR" }, resultado.Select(r => r.ProductoId).ToArray());
        }

        #endregion

        #region Informe de conversión

        [TestMethod]
        public void Calcular_AgrupaPorSemanaTipoReglaYGrupoControl()
        {
            DateTime hoy = new DateTime(2026, 12, 31);
            var filas = new List<FilaConversionPostCompra>
            {
                Conversion(SABADO, "A", "R1", "Recomendado", "pares", false, 1, 2, 30m),
                Conversion(SABADO, "A", "R2", "Recomendado", "pares", false, 0, 0, 0m),
                Conversion(SABADO, "B", "R1", "Recomendado", "pares", false, 2, 3, 45.5m),
                Conversion(SABADO, "C", "R3", "Recomendado", "pares", true, 0, 0, 0m),
                Conversion(SABADO, "A", "C1", "Comprado", null, false, 1, 1, 10m),
                Conversion(SABADO.AddDays(7), "A", "R1", "Recomendado", "pares", false, 0, 0, 0m)
            };

            InformeConversionPostCompraDTO informe = InformeConversionPostCompra.Calcular(filas, new DateTime(2026, 10, 1), new DateTime(2026, 12, 1), hoy);

            ConversionPostCompraGrupoDTO pares = informe.PorSemana.Single(g => g.Semana == new DateTime(2026, 10, 26) && g.Tipo == "Recomendado" && g.Regla == "pares" && !g.GrupoControl);
            Assert.AreEqual(3, pares.Recomendaciones);
            Assert.AreEqual(2, pares.Clientes);
            Assert.AreEqual(2, pares.Compradas);
            Assert.AreEqual(2, pares.ClientesCompradores);
            Assert.AreEqual(5, pares.Unidades);
            Assert.AreEqual(75.5m, pares.BaseImponible);
            Assert.AreEqual(66.7m, pares.PorcentajeConversion);
            Assert.IsTrue(pares.VentanaCerrada);

            ConversionPostCompraGrupoDTO control = informe.PorSemana.Single(g => g.GrupoControl);
            Assert.AreEqual(1, control.Recomendaciones);
            Assert.AreEqual(0m, control.PorcentajeConversion);

            ConversionPostCompraGrupoDTO comprado = informe.PorSemana.Single(g => g.Tipo == "Comprado");
            Assert.AreEqual("", comprado.Regla);
            Assert.AreEqual(1, comprado.Compradas);

            Assert.AreEqual(4, informe.PorSemana.Count);
            ConversionPostCompraGrupoDTO totalPares = informe.Totales.Single(g => g.Tipo == "Recomendado" && g.Regla == "pares" && !g.GrupoControl);
            Assert.IsNull(totalPares.Semana);
            Assert.AreEqual(4, totalPares.Recomendaciones);
            Assert.AreEqual(2, totalPares.Compradas);
            Assert.AreEqual(50m, totalPares.PorcentajeConversion);
            Assert.AreEqual(30, informe.DiasVentana);
        }

        [TestMethod]
        public void Calcular_VentanaAbierta_SiNoHanPasadoTreintaDias()
        {
            InformeConversionPostCompraDTO informe = InformeConversionPostCompra.Calcular(
                new[] { Conversion(SABADO, "A", "R1", "Recomendado", "pares", false, 0, 0, 0m) },
                SABADO.Date, SABADO.Date.AddDays(1), SABADO.Date.AddDays(29));

            Assert.IsFalse(informe.PorSemana.Single().VentanaCerrada);
        }

        [TestMethod]
        public void Calcular_SinFilas_Vacio()
        {
            InformeConversionPostCompraDTO informe = InformeConversionPostCompra.Calcular(null, SABADO, SABADO, SABADO);

            Assert.AreEqual(0, informe.PorSemana.Count);
            Assert.AreEqual(0, informe.Totales.Count);
        }

        [TestMethod]
        public void Lunes_DevuelveElLunesDeLaSemana()
        {
            Assert.AreEqual(new DateTime(2026, 10, 26), InformeConversionPostCompra.Lunes(new DateTime(2026, 11, 1, 23, 0, 0)));
            Assert.AreEqual(new DateTime(2026, 10, 26), InformeConversionPostCompra.Lunes(new DateTime(2026, 10, 26)));
        }

        [TestMethod]
        public async Task GetConversion_SinSerDireccionNiInformatica_Forbidden()
        {
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            CorreosPostCompraController controller = Controller("NUEVAVISION\\Vendedores", repositorio);

            var resultado = await controller.GetConversion() as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.LeerConversion(A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetConversion_PorDefecto_UltimasDoceSemanasConHoyIncluido()
        {
            var repositorio = A.Fake<IRepositorioRecomendacionesPostCompra>();
            A.CallTo(() => repositorio.LeerConversion(A<DateTime>._, A<DateTime>._))
                .Returns(new List<FilaConversionPostCompra> { Conversion(SABADO, "A", "R1", "Recomendado", "pares", false, 1, 1, 10m) });
            CorreosPostCompraController controller = Controller("NUEVAVISION\\Dirección", repositorio);

            var resultado = await controller.GetConversion() as OkNegotiatedContentResult<InformeConversionPostCompraDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => repositorio.LeerConversion(new DateTime(2026, 8, 18), new DateTime(2026, 11, 11))).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Content.Totales.Single().Compradas);
        }

        [TestMethod]
        public async Task GetConversion_Informatica_HastaAnteriorADesde_BadRequest()
        {
            CorreosPostCompraController controller = Controller("NUEVAVISION\\Informatica", A.Fake<IRepositorioRecomendacionesPostCompra>());

            var resultado = await controller.GetConversion(new DateTime(2026, 11, 1), new DateTime(2026, 10, 1));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        #endregion

        #region Ayudantes

        private static HashSet<string> Vacio() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static CorreoPostCompraClienteDTO Correo(string cliente, IEnumerable<string> comprados, IEnumerable<ProductoRecomendadoDTO> recomendados)
            => new CorreoPostCompraClienteDTO
            {
                Empresa = "1  ",
                ClienteId = cliente,
                ClienteContacto = "0  ",
                ClienteEmail = "cliente@example.com",
                ProductosComprados = (comprados ?? Enumerable.Empty<string>()).Select(c => new ProductoCompradoConVideoDTO { ProductoId = c, VideoYoutubeId = "v" }).ToList(),
                ProductosRecomendados = (recomendados ?? Enumerable.Empty<ProductoRecomendadoDTO>()).ToList(),
                VersionModelo = PuntuadorRecomendacionesPostCompra.VERSION_MODELO
            };

        private static ProductoRecomendadoDTO Recomendado(string producto, string regla, decimal? puntuacion,
            string enlaceTienda = "https://www.productosdeesteticaypeluqueriaprofesional.com/p.html")
            => new ProductoRecomendadoDTO
            {
                ProductoId = producto,
                Regla = regla,
                Puntuacion = puntuacion,
                EnlaceVideoProducto = "https://youtu.be/x?t=1",
                EnlaceTienda = enlaceTienda
            };

        private static FilaConversionPostCompra Conversion(DateTime fecha, string cliente, string producto, string tipo, string regla,
            bool grupoControl, int lineas, int unidades, decimal baseImponible)
            => new FilaConversionPostCompra
            {
                FechaEnvio = fecha,
                Empresa = "1",
                Cliente = cliente,
                Producto = producto,
                Tipo = tipo,
                Regla = regla,
                GrupoControl = grupoControl,
                Lineas = lineas,
                Unidades = unidades,
                BaseImponible = baseImponible
            };

        private static CorreosPostCompraController Controller(string grupo, IRepositorioRecomendacionesPostCompra repositorio)
        {
            var controller = new CorreosPostCompraController(A.Fake<IServicioRecomendacionesPostCompra>(), A.Fake<IGeneradorContenidoCorreoPostCompra>(),
                A.Fake<IServicioCorreoElectronico>())
            {
                RepositorioRecomendaciones = repositorio,
                Hoy = () => new DateTime(2026, 11, 10, 9, 0, 0),
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { grupo });
            return controller;
        }

        #endregion
    }
}
