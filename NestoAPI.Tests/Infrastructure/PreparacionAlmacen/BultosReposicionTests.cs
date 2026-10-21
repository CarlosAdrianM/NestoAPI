using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#645 (corte 1): etiqueta con código de barras por bulto al cerrar una reposición y lectura obligatoria al recibir.
    [TestClass]
    public class BultosReposicionTests
    {
        private IRepositorioBultosReposicion repositorio;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetasAlmacen impresora;
        private ServicioBultosReposicion servicio;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioBultosReposicion>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            impresora = A.Fake<IImpresoraEtiquetasAlmacen>();
            A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            A.CallTo(() => repositorio.LeerOrigenDestino("1", 81010)).Returns(new OrigenDestinoReposicion { Origen = "ALC", Destino = "ALG" });
            A.CallTo(() => repositorio.Reemplazar("1", 81010, A<int>._, A<string>._)).Returns(true);
            A.CallTo(() => parametros.LeerParametro("1", "Paloma", ServicioEtiquetasHueco.PARAMETRO_IMPRESORA)).Returns("\\\\RDS2016\\Zebra ALC ");
            servicio = new ServicioBultosReposicion(repositorio, parametros, impresora);
        }

        // ---------------------------------------------------------------- código y texto

        [TestMethod]
        public void Codigo_YTexto_ComoLaEtiqueta()
        {
            Assert.AreEqual("R81010-2", CodigosBultoReposicion.Codigo(81010, 2));
            Assert.AreEqual("81010 · bulto 2/3 · ALC→ALG", CodigosBultoReposicion.Texto(81010, 2, 3, "ALC", "ALG"));
        }

        [DataTestMethod]
        [DataRow("R81010-2", true, 81010, 2)]
        [DataRow(" r81010-12 ", true, 81010, 12)]
        [DataRow("81010-2", false, 0, 0)]
        [DataRow("R81010", false, 0, 0)]
        [DataRow("8437017506362", false, 0, 0)]
        [DataRow("R-2", false, 0, 0)]
        // NestoAPI#675: cualquier separador que no sea cifra ni letra (la pistola con teclado americano manda «'»)
        [DataRow("R81029-1", true, 81029, 1)]
        [DataRow("R81029'1", true, 81029, 1)]
        [DataRow("r81029?1", true, 81029, 1)]
        [DataRow(" R81029-1 ", true, 81029, 1)]
        [DataRow("R81029_1", true, 81029, 1)]
        [DataRow("R81029/1", true, 81029, 1)]
        [DataRow("R81029 1", true, 81029, 1)]
        [DataRow("R81029A1", false, 0, 0)]
        [DataRow("R810291", false, 0, 0)]
        [DataRow("8102901", false, 0, 0)]
        [DataRow("8437017509999", false, 0, 0)]
        [DataRow("AB-1234", false, 0, 0)]
        [DataRow("RE81029-1", false, 0, 0)]
        [DataRow("R81029-", false, 0, 0)]
        [DataRow("R81029-0", false, 0, 0)]
        [DataRow("", false, 0, 0)]
        [DataRow(null, false, 0, 0)]
        // Reposición 81097: el prefijo AIM de la simbología («]C0» Code 128, «]C1» con FNC1) delante
        [DataRow("]C0R81097-1", true, 81097, 1)]
        [DataRow("]C1R81097-1", true, 81097, 1)]
        [DataRow(" ]c0r81097'1 ", true, 81097, 1)]
        [DataRow("]C0", false, 0, 0)]
        [DataRow("]C0R81097", false, 0, 0)]
        [DataRow("]E08437017509999", false, 0, 0)]
        [DataRow("]C0]C0R81097-1", false, 0, 0)]
        public void Leer_CodigoDeBulto(string codigo, bool esBulto, int traspaso, int bulto)
        {
            Assert.AreEqual(esBulto, CodigosBultoReposicion.Leer(codigo, out int t, out int b));
            Assert.AreEqual(traspaso, esBulto ? t : 0);
            Assert.AreEqual(bulto, esBulto ? b : 0);
        }

        [DataTestMethod]
        [DataRow("R81029-1", "R81029-1")]
        [DataRow("R81029'1", "R81029-1")]
        [DataRow("r81029?1", "R81029-1")]
        [DataRow(" R81029-1 ", "R81029-1")]
        [DataRow("R081029_01", "R81029-1")]
        [DataRow("]C0R81097-1", "R81097-1")]
        [DataRow(" 8437017509999 ", "8437017509999")]
        [DataRow("abc-12", "ABC-12")]
        [DataRow("   ", null)]
        [DataRow(null, null)]
        public void Normalizar_LaEtiquetaSiempreConGuion_YLoDemasSinTocar(string codigo, string esperado)
        {
            Assert.AreEqual(esperado, CodigosBultoReposicion.Normalizar(codigo));
        }

        [TestMethod]
        public void Epl_UnaEtiquetaPorBultoConSuCodigoYElTexto()
        {
            var bultos = new List<BultoReposicionDTO>
            {
                new BultoReposicionDTO { Codigo = "R81010-1", Bulto = 1, Total = 2 },
                new BultoReposicionDTO { Codigo = "R81010-2", Bulto = 2, Total = 2 }
            };

            string epl = GeneradorEtiquetasBulto.Epl(bultos, 81010, "ALC", "ALG");

            Assert.AreEqual(2, epl.Split(new[] { "P1\r\n" }, StringSplitOptions.None).Length - 1);
            StringAssert.Contains(epl, "\"R81010-1\"");
            StringAssert.Contains(epl, "\"R81010-2\"");
            StringAssert.Contains(epl, "\"81010 - bulto 2/2\"");
            StringAssert.Contains(epl, "\"ALC > ALG\"");
            StringAssert.Contains(epl, "q240"); // la misma etiqueta de 30×20 que la de hueco
        }

        [TestMethod]
        public void Zpl_ElMismoContenidoQueElEplEnLasMismasPosiciones()
        {
            var bultos = new List<BultoReposicionDTO>
            {
                new BultoReposicionDTO { Codigo = "R81093-1", Bulto = 1, Total = 2 },
                new BultoReposicionDTO { Codigo = "R81093-2", Bulto = 2, Total = 2 }
            };

            string zpl = GeneradorEtiquetasBulto.Zpl(bultos, 81093, "ALC", "ALG");
            string epl = GeneradorEtiquetasBulto.Epl(bultos, 81093, "ALC", "ALG");

            Assert.AreEqual(2, zpl.Split(new[] { "^XA" }, StringSplitOptions.None).Length - 1);
            StringAssert.StartsWith(zpl,
                "^XA^CI28^LH0,0\r\n" +
                "^FO57,8^BY1^BCN,64,N,N,N,A^FH^FDR81093-1^FS\r\n" +
                "^FO18,84^ADN,18,10^FH^FD81093 - bulto 1/2^FS\r\n" +
                "^FO66,112^ADN,18,10^FH^FDALC > ALG^FS\r\n" +
                "^PQ1^XZ\r\n");
            // Las mismas posiciones que el EPL
            StringAssert.Contains(epl, "B57,8,0,1,1,1,64,N,\"R81093-1\"");
            StringAssert.Contains(epl, "A18,84,0,3,1,1,N,\"81093 - bulto 1/2\"");
            StringAssert.Contains(epl, "A66,112,0,3,1,1,N,\"ALC > ALG\"");
            StringAssert.Contains(zpl, "^FDR81093-2^FS");
            Assert.IsFalse(zpl.Contains("^PW") || zpl.Contains("^LL"));
        }

        /// <summary>
        /// Reposición 81097 (21/10/26): la etiqueta hecha a mano para Alcobendas llevaba el código con módulo de 2 puntos
        /// desde x=40: 123 módulos × 2 = 246 puntos, hasta el 286, en una etiqueta de 240 (30 mm). Sin el final (control y
        /// parada) ni margen a la derecha, no lo lee ningún lector. El de la API tiene que caber con su margen (10 módulos
        /// a cada lado) incluso con un traspaso de 6 cifras y un bulto de 2, contando todo en el juego B (lo más largo).
        /// </summary>
        [DataTestMethod]
        [DataRow("R81097-1")]
        [DataRow("R999999-99")]
        public void Reposicion81097_ElCodigoDeLaEtiquetaCabeConSuMargen(string codigo)
        {
            const int margenModulos = 10;
            int modulos = 11 + 11 * codigo.Length + 11 + 13; // inicio + datos + control + parada (juego B)
            var bultos = new List<BultoReposicionDTO> { new BultoReposicionDTO { Codigo = codigo, Bulto = 1, Total = 1 } };
            string epl = GeneradorEtiquetasBulto.Epl(bultos, 81097, "ALC", "ALG");
            string zpl = GeneradorEtiquetasBulto.Zpl(bultos, 81097, "ALC", "ALG");
            int estrecho = GeneradorEtiquetasBulto.CODIGO_ESTRECHO;
            int x = int.Parse(System.Text.RegularExpressions.Regex.Match(zpl, @"\^FO(\d+),\d+\^BY").Groups[1].Value);

            StringAssert.Contains(epl, $"B{x},{GeneradorEtiquetasBulto.CODIGO_Y},0,1,{estrecho},{estrecho},");
            StringAssert.Contains(zpl, $"^BY{estrecho}^BC");
            Assert.IsTrue(x >= margenModulos * estrecho, $"margen izquierdo {x} puntos");
            Assert.IsTrue(x + modulos * estrecho <= GeneradorEtiquetasHueco.ANCHO - margenModulos * estrecho,
                $"el código acaba en el punto {x + modulos * estrecho} y la etiqueta tiene {GeneradorEtiquetasHueco.ANCHO}");
        }

        [TestMethod]
        public async Task Reimprimir_MandaElTrabajoEnLosDosIdiomas()
        {
            A.CallTo(() => repositorio.Leer("1", 81010)).Returns(new List<FilaBultoReposicion>
            {
                new FilaBultoReposicion { Traspaso = 81010, Bulto = 1, Total = 1, Codigo = "R81010-1" }
            });
            TrabajoEtiquetas trabajo = null;
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).Invokes((string i, TrabajoEtiquetas t) => trabajo = t);

            _ = await servicio.Reimprimir("1", 81010, "NUEVAVISION\\Paloma");

            Assert.AreEqual("Bultos reposición 81010", trabajo.Nombre);
            StringAssert.Contains(trabajo.Epl, "\"R81010-1\"");
            StringAssert.Contains(trabajo.Zpl, "^FDR81010-1^FS");
        }

        // ---------------------------------------------------------------- crear

        [TestMethod]
        public async Task Crear_CreaLosBultosEImprimeUnaEtiquetaPorBulto()
        {
            string impreso = null;
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).Invokes((string i, TrabajoEtiquetas t) => impreso = t.Epl);

            ResultadoBultosReposicionDTO resultado = await servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 3, Imprimir = true }, "NUEVAVISION\\Paloma");

            A.CallTo(() => repositorio.Reemplazar("1", 81010, 3, "NUEVAVISION\\Paloma")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(81010, resultado.Traspaso);
            Assert.AreEqual("ALC", resultado.Origen);
            Assert.AreEqual("ALG", resultado.Destino);
            Assert.AreEqual("\\\\RDS2016\\Zebra ALC", resultado.Impresora);
            CollectionAssert.AreEqual(new[] { "R81010-1", "R81010-2", "R81010-3" }, resultado.Bultos.Select(b => b.Codigo).ToArray());
            Assert.AreEqual("81010 · bulto 2/3 · ALC→ALG", resultado.Bultos[1].Texto);
            Assert.AreEqual(3, resultado.Bultos[1].Total);
            A.CallTo(() => impresora.Imprimir("\\\\RDS2016\\Zebra ALC", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(impreso, "\"R81010-3\"");
        }

        [TestMethod]
        public async Task Crear_SinImprimir_NoMandaNadaALaImpresora()
        {
            ResultadoBultosReposicionDTO resultado = await servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 1, Imprimir = false }, "Paloma");

            Assert.AreEqual(1, resultado.Bultos.Count);
            Assert.IsNull(resultado.Impresora);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Crear_YaTeniaBultosSinLeer_SeRehacenConElNuevoTotal()
        {
            _ = await servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 2, Imprimir = false }, "Paloma");
            ResultadoBultosReposicionDTO resultado = await servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 4, Imprimir = false }, "Paloma");

            A.CallTo(() => repositorio.Reemplazar("1", 81010, 4, "Paloma")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(4, resultado.Bultos.Count);
            Assert.IsTrue(resultado.Bultos.All(b => b.Total == 4));
        }

        [TestMethod]
        public async Task Crear_AlgunBultoYaLeido_409()
        {
            A.CallTo(() => repositorio.Reemplazar("1", 81010, A<int>._, A<string>._)).Returns(false);

            BultosReposicionException ex = await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 2 }, "Paloma"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(51)]
        [DataRow(-1)]
        public async Task Crear_NumeroDeBultosQueNoVale_400(int bultos)
        {
            BultosReposicionException ex = await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = bultos }, "Paloma"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            A.CallTo(() => repositorio.Reemplazar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Crear_TraspasoQueNoExiste_404()
        {
            A.CallTo(() => repositorio.LeerOrigenDestino("1", 99999)).Returns((OrigenDestinoReposicion)null);

            BultosReposicionException ex = await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Crear("1", 99999, new CrearBultosReposicionDTO { Bultos = 1 }, "Paloma"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task SinTabla_503EnTodo()
        {
            A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 1 }, "Paloma"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Reimprimir("1", 81010, "Paloma"))).StatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Assert.ThrowsExceptionAsync<BultosReposicionException>(() =>
                servicio.Leer("1", "R81010-1"))).StatusCode);
            A.CallTo(() => repositorio.Reemplazar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Crear_SiFallaLaImpresion_LosBultosQuedanCreadosYSeDiceElMotivo()
        {
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._))
                .Throws(new ImpresionEtiquetasException("No se puede abrir la impresora X (error de Windows 5)."));

            ImpresionBultosException ex = await Assert.ThrowsExceptionAsync<ImpresionBultosException>(() =>
                servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 2 }, "Paloma"));

            A.CallTo(() => repositorio.Reemplazar("1", 81010, 2, "Paloma")).MustHaveHappenedOnceExactly();
            StringAssert.Contains(ex.Message, "están creados");
            StringAssert.Contains(ex.Message, "error de Windows 5");
            Assert.AreEqual(2, ex.Resultado.Bultos.Count);
        }

        [TestMethod]
        public async Task Crear_UsuarioSinImpresora_LosBultosQuedanCreadosYSeDiceDeUsted()
        {
            ImpresionBultosException ex = await Assert.ThrowsExceptionAsync<ImpresionBultosException>(() =>
                servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 1 }, "NUEVAVISION\\Nadie"));

            A.CallTo(() => repositorio.Reemplazar("1", 81010, 1, A<string>._)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(ex.Message, "Pídasela");
        }

        [TestMethod]
        public async Task Crear_UsuarioSinImpresoraPropia_UsaLaDeDefecto()
        {
            A.CallTo(() => parametros.LeerParametro("1", "(defecto)", ServicioEtiquetasHueco.PARAMETRO_IMPRESORA)).Returns("Zebra ALG");

            ResultadoBultosReposicionDTO resultado = await servicio.Crear("1", 81010, new CrearBultosReposicionDTO { Bultos = 1 }, "Andre");

            Assert.AreEqual("Zebra ALG", resultado.Impresora);
        }

        // ---------------------------------------------------------------- reimprimir y leer

        [TestMethod]
        public async Task Reimprimir_LasQueTiene()
        {
            A.CallTo(() => repositorio.Leer("1", 81010)).Returns(new List<FilaBultoReposicion>
            {
                new FilaBultoReposicion { Traspaso = 81010, Bulto = 2, Total = 2, Codigo = "R81010-2" },
                new FilaBultoReposicion { Traspaso = 81010, Bulto = 1, Total = 2, Codigo = "R81010-1" }
            });

            ResultadoBultosReposicionDTO resultado = await servicio.Reimprimir("1", 81010, "Paloma");

            CollectionAssert.AreEqual(new[] { "R81010-1", "R81010-2" }, resultado.Bultos.Select(b => b.Codigo).ToArray());
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Reemplazar(A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reimprimir_SinBultos_404()
        {
            A.CallTo(() => repositorio.Leer("1", 81010)).Returns(new List<FilaBultoReposicion>());

            BultosReposicionException ex = await Assert.ThrowsExceptionAsync<BultosReposicionException>(() => servicio.Reimprimir("1", 81010, "Paloma"));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
        }

        [TestMethod]
        public async Task Leer_PorCodigo_DiceDeQueReposicionEsYSiSeHaLeido()
        {
            var fecha = new DateTime(2026, 10, 16, 9, 30, 0);
            A.CallTo(() => repositorio.LeerPorCodigo("1", "R81010-2")).Returns(new FilaBultoReposicion
            {
                Traspaso = 81010, Bulto = 2, Total = 3, Codigo = "R81010-2", FechaLectura = fecha
            });

            LecturaBultoReposicionDTO bulto = await servicio.Leer("1", " r81010-2 ");

            Assert.AreEqual("R81010-2", bulto.Codigo);
            Assert.AreEqual(81010, bulto.Traspaso);
            Assert.AreEqual(2, bulto.Bulto);
            Assert.AreEqual(3, bulto.Total);
            Assert.AreEqual("ALC", bulto.Origen);
            Assert.AreEqual("ALG", bulto.Destino);
            Assert.IsTrue(bulto.Leido);
            Assert.AreEqual(fecha, bulto.FechaLectura);
        }

        [TestMethod]
        public async Task Leer_PorCodigoConApostrofo_LoBuscaConGuion()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("1", "R81029-1")).Returns(new FilaBultoReposicion
            {
                Traspaso = 81029, Bulto = 1, Total = 1, Codigo = "R81029-1"
            });

            LecturaBultoReposicionDTO bulto = await servicio.Leer("1", "R81029'1");

            Assert.AreEqual(81029, bulto.Traspaso);
            A.CallTo(() => repositorio.LeerPorCodigo("1", "R81029-1")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Leer_CodigoQueNoEsDeNinguna_Null()
        {
            A.CallTo(() => repositorio.LeerPorCodigo("1", A<string>._)).Returns((FilaBultoReposicion)null);

            Assert.IsNull(await servicio.Leer("1", "R1-1"));
        }

        // ---------------------------------------------------------------- endpoints

        private static ReposicionesController Controlador(IServicioBultosReposicion bultos)
        {
            return new ReposicionesController(null, A.Fake<IServicioPreparacionReposicion>(), bultos: bultos)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma") }, "prueba"))
            };
        }

        [TestMethod]
        public async Task PostBultos_Ok()
        {
            IServicioBultosReposicion bultos = A.Fake<IServicioBultosReposicion>();
            A.CallTo(() => bultos.Crear("1", 81010, A<CrearBultosReposicionDTO>._, "NUEVAVISION\\Paloma"))
                .Returns(new ResultadoBultosReposicionDTO { Traspaso = 81010 });

            IHttpActionResult respuesta = await Controlador(bultos).PostBultos(81010, new CrearBultosReposicionDTO { Bultos = 1 });

            Assert.AreEqual(81010, ((OkNegotiatedContentResult<ResultadoBultosReposicionDTO>)respuesta).Content.Traspaso);
        }

        [TestMethod]
        public async Task PostBultos_FallaLaImpresion_502ConElMotivo()
        {
            IServicioBultosReposicion bultos = A.Fake<IServicioBultosReposicion>();
            A.CallTo(() => bultos.Crear(A<string>._, A<int>._, A<CrearBultosReposicionDTO>._, A<string>._))
                .Throws(new ImpresionBultosException("Los bultos están creados, pero no se han podido imprimir.", null, null));

            HttpResponseMessage respuesta = await (await Controlador(bultos).PostBultos(81010, new CrearBultosReposicionDTO { Bultos = 1 }))
                .ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.BadGateway, respuesta.StatusCode);
            HttpError error = await respuesta.Content.ReadAsAsync<HttpError>();
            Assert.AreEqual("Los bultos están creados, pero no se han podido imprimir.", error.Message);
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.Conflict)]
        [DataRow(HttpStatusCode.ServiceUnavailable)]
        [DataRow(HttpStatusCode.NotFound)]
        public async Task PostBultos_ErroresConSuCodigo(HttpStatusCode codigo)
        {
            IServicioBultosReposicion bultos = A.Fake<IServicioBultosReposicion>();
            A.CallTo(() => bultos.Crear(A<string>._, A<int>._, A<CrearBultosReposicionDTO>._, A<string>._))
                .Throws(new BultosReposicionException(codigo, "motivo"));

            HttpResponseMessage respuesta = await (await Controlador(bultos).PostBultos(81010, new CrearBultosReposicionDTO { Bultos = 1 }))
                .ExecuteAsync(default);

            Assert.AreEqual(codigo, respuesta.StatusCode);
        }

        [TestMethod]
        public async Task GetBulto_QueNoEsDeNinguna_404ConMensaje()
        {
            IServicioBultosReposicion bultos = A.Fake<IServicioBultosReposicion>();
            A.CallTo(() => bultos.Leer(A<string>._, A<string>._)).Returns((LecturaBultoReposicionDTO)null);

            HttpResponseMessage respuesta = await (await Controlador(bultos).GetBulto("R1-1")).ExecuteAsync(default);

            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.AreEqual("Esa etiqueta no es de ninguna reposición.", (await respuesta.Content.ReadAsAsync<HttpError>()).Message);
        }

        // ---------------------------------------------------------------- recepción

        private static OrigenRecepcionReposiciones Origen(IRepositorioCierreReposiciones cierre, IRepositorioBultosReposicion bultos = null,
            IServicioRecepcionReposiciones servicio = null)
        {
            return new OrigenRecepcionReposiciones(servicio ?? A.Fake<IServicioRecepcionReposiciones>(), cierre ?? A.Fake<IRepositorioCierreReposiciones>(),
                A.Fake<IAvisadorReposiciones>(), (empresa, usuario) => "REI", A.Fake<IServicioDiferenciasReposicion>(), bultos ?? A.Fake<IRepositorioBultosReposicion>());
        }

        private static (IRepositorioCierreReposiciones Cierre, ITransaccionCierreReposicion Transaccion) Cierre(List<FilaBultoReposicion> bultos)
        {
            var cierre = A.Fake<IRepositorioCierreReposiciones>();
            var transaccion = A.Fake<ITransaccionCierreReposicion>();
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> t, bool deshacer) => t(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.DiarioDeEntrada("1", "REI")).Returns("PendRepo");
            A.CallTo(() => transaccion.TraspasosEnDiario("1", "REI", "PendRepo")).Returns(new List<int> { 81010 });
            A.CallTo(() => transaccion.LeerLineas("1", "REI", 81010)).Returns(new List<FilaReposicion>
            {
                new FilaReposicion { Producto = "A", Descripcion = "Producto A", Cantidad = 2 }
            });
            A.CallTo(() => transaccion.LeerBultos("1", 81010)).Returns(bultos);
            A.CallTo(() => transaccion.LeerBulto("1", A<string>._)).Returns((FilaBultoReposicion)null);
            return (cierre, transaccion);
        }

        private static List<FilaBultoReposicion> DosBultos() => new List<FilaBultoReposicion>
        {
            new FilaBultoReposicion { Traspaso = 81010, Bulto = 1, Total = 2, Codigo = "R81010-1" },
            new FilaBultoReposicion { Traspaso = 81010, Bulto = 2, Total = 2, Codigo = "R81010-2" }
        };

        private static SolicitudTerminarRecepcion Solicitud(params string[] codigos) => new SolicitudTerminarRecepcion
        {
            Empresa = "1", Almacen = "REI", Documento = "81010", IdRecepcion = Guid.NewGuid(),
            Lecturas = new Dictionary<string, int> { ["A"] = 2 }, Usuario = "NUEVAVISION\\Reina",
            CodigosBulto = codigos.ToList()
        };

        [TestMethod]
        public async Task LeerEsperado_ConBultos_ExigeEtiqueta()
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerRecepcion("1", "REI", 81010)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "REI", Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "A", Cantidad = 2 } }
            });
            var bultos = A.Fake<IRepositorioBultosReposicion>();
            A.CallTo(() => bultos.Leer("1", 81010)).Returns(DosBultos());

            RecepcionDTO recepcion = await Origen(null, bultos, servicio).LeerEsperado("1", "REI", "81010");

            Assert.IsTrue(recepcion.ExigeEtiqueta);
            Assert.AreEqual(2, recepcion.Bultos);
        }

        [DataTestMethod]
        [DataRow(true)]  // cerrada antes de las etiquetas: sin filas
        [DataRow(false)] // sin la tabla
        public async Task LeerEsperado_SinBultos_NoExigeEtiqueta(bool hayTabla)
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerRecepcion("1", "REI", 81010)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "REI", Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "A", Cantidad = 2 } }
            });
            var bultos = A.Fake<IRepositorioBultosReposicion>();
            A.CallTo(() => bultos.Leer("1", 81010)).Returns(hayTabla ? new List<FilaBultoReposicion>() : null);

            RecepcionDTO recepcion = await Origen(null, bultos, servicio).LeerEsperado("1", "REI", "81010");

            Assert.IsFalse(recepcion.ExigeEtiqueta);
            Assert.AreEqual(0, recepcion.Bultos);
        }

        [TestMethod]
        public async Task BuscarPorCodigo_LaEtiquetaDelBultoLlevaASuReposicion()
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 81010 }, new ReposicionPendienteDTO { Traspaso = 81011 }
            });

            List<RecepcionPendienteDTO> encontradas = await Origen(null, null, servicio).BuscarPorCodigo("1", "REI", "R81011-1");

            Assert.AreEqual("81011", encontradas.Single().Documento);
        }

        [TestMethod]
        public async Task Terminar_ExigeEtiquetaYNoViene_400SinTocarNada()
        {
            var (cierre, transaccion) = Cierre(DosBultos());

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Origen(cierre).Terminar(Solicitud()));

            Assert.AreEqual("Lea la etiqueta del bulto antes de recibir la reposición 81010.", ex.Message);
            A.CallTo(() => transaccion.Contabilizar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.ApartarOtros(A<string>._, A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_ConLaEtiqueta_RecibeYMarcaLosLeidos()
        {
            var (cierre, transaccion) = Cierre(DosBultos());

            ResultadoTerminarRecepcionDTO resultado = await Origen(cierre).Terminar(Solicitud("R81010-2", "R99999-1"));

            Assert.IsFalse(resultado.YaEstabaTerminada);
            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.MarcarBultosLeidos("1", A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "R81010-2" }), "NUEVAVISION\\Reina"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_ConLaEtiquetaConApostrofo_LaReconoceYMarcaLaNormalizada()
        {
            var (cierre, transaccion) = Cierre(DosBultos());

            _ = await Origen(cierre).Terminar(Solicitud("R81010'2", "r81010?2"));

            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", "NUEVAVISION\\Reina")).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.MarcarBultosLeidos("1", A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "R81010-2" }), "NUEVAVISION\\Reina"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task BuscarPorCodigo_LaEtiquetaConApostrofoTambienLlevaASuReposicion()
        {
            var servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => servicio.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 81010 }, new ReposicionPendienteDTO { Traspaso = 81011 }
            });

            List<RecepcionPendienteDTO> encontradas = await Origen(null, null, servicio).BuscarPorCodigo("1", "REI", "R81011'1");

            Assert.AreEqual("81011", encontradas.Single().Documento);
        }

        [TestMethod]
        public async Task Terminar_EtiquetaDeOtraReposicion_400()
        {
            var (cierre, transaccion) = Cierre(DosBultos());
            A.CallTo(() => transaccion.LeerBulto("1", "R81011-1")).Returns(new FilaBultoReposicion { Traspaso = 81011, Bulto = 1, Total = 1, Codigo = "R81011-1" });

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Origen(cierre).Terminar(Solicitud("R81010-1", "R81011-1")));

            Assert.AreEqual("Esa etiqueta es de la reposición 81011, no de la 81010.", ex.Message);
            A.CallTo(() => transaccion.Contabilizar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_SinBultos_SeRecibeComoSiempre()
        {
            var (cierre, transaccion) = Cierre(new List<FilaBultoReposicion>());

            _ = await Origen(cierre).Terminar(Solicitud());

            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.MarcarBultosLeidos(A<string>._, A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_SinTabla_SeRecibeComoSiempre()
        {
            var (cierre, transaccion) = Cierre(null);

            _ = await Origen(cierre).Terminar(Solicitud("R81010-1"));

            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.MarcarBultosLeidos(A<string>._, A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_BultoYaLeidoEnOtraRecepcion_NoImpideNada()
        {
            List<FilaBultoReposicion> bultos = DosBultos();
            bultos[0].FechaLectura = new DateTime(2026, 10, 15, 18, 0, 0);
            var (cierre, transaccion) = Cierre(bultos);

            _ = await Origen(cierre).Terminar(Solicitud("R81010-1"));

            A.CallTo(() => transaccion.Contabilizar("1", "PendRepo", A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.MarcarBultosLeidos("1", A<IEnumerable<string>>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_EnUnEnsayo_NoSeMarcaNada()
        {
            var (cierre, transaccion) = Cierre(DosBultos());
            SolicitudTerminarRecepcion solicitud = Solicitud("R81010-1");
            solicitud.Ensayo = new RegistroEnsayoRecepcion();

            _ = await Origen(cierre).Terminar(solicitud);

            A.CallTo(() => transaccion.MarcarBultosLeidos(A<string>._, A<IEnumerable<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_EnUnEnsayo_TambienExigeLaEtiqueta()
        {
            var (cierre, _) = Cierre(DosBultos());
            SolicitudTerminarRecepcion solicitud = Solicitud();
            solicitud.Ensayo = new RegistroEnsayoRecepcion();

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Origen(cierre).Terminar(solicitud));
        }

        [TestMethod]
        public async Task Nucleo_PasaLasEtiquetasLeidasNormalizadas()
        {
            IOrigenRecepcion origen = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => origen.Tipo).Returns("REPO");
            A.CallTo(() => origen.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => origen.PuedeTerminar(A<IPrincipal>._, A<string>._, A<string>._)).Returns(true);
            SolicitudTerminarRecepcion recibida = null;
            A.CallTo(() => origen.Terminar(A<SolicitudTerminarRecepcion>._))
                .ReturnsLazily((SolicitudTerminarRecepcion s) => { recibida = s; return new ResultadoTerminarRecepcionDTO(); });
            var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Reina") }, "prueba"));

            _ = await new ServicioRecepciones(new[] { origen }).Terminar("REPO", "1", "REI", "81010", new TerminarRecepcionDTO
            {
                IdRecepcion = Guid.NewGuid(),
                Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 2 } },
                CodigosBulto = new List<string> { " r81010-1 ", "R81010-1", "", null }
            }, usuario);

            CollectionAssert.AreEqual(new[] { "R81010-1" }, recibida.CodigosBulto);
        }
    }
}
