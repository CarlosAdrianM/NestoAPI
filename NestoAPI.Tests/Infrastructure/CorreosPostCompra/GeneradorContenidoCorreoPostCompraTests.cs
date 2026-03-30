using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class GeneradorContenidoCorreoPostCompraTests
    {
        private IServicioOpenAI _servicioOpenAIMock;
        private GeneradorContenidoCorreoPostCompra _generador;

        [TestInitialize]
        public void Setup()
        {
            _servicioOpenAIMock = A.Fake<IServicioOpenAI>();
            _generador = new GeneradorContenidoCorreoPostCompra(_servicioOpenAIMock);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConCorreoNull_DevuelveNull()
        {
            var resultado = await _generador.GenerarContenidoHtml(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SinProductosComprados_DevuelveNull()
        {
            var correo = new CorreoPostCompraClienteDTO
            {
                ClienteNombre = "Test Cliente",
                ProductosComprados = new List<ProductoCompradoConVideoDTO>()
            };

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ProductosCompradosNull_DevuelveNull()
        {
            var correo = new CorreoPostCompraClienteDTO
            {
                ClienteNombre = "Test Cliente",
                ProductosComprados = null
            };

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_ConProductosComprados_LlamaAOpenAI()
        {
            var correo = CrearCorreoValido();

            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Returns("<div>HTML generado</div>");

            var resultado = await _generador.GenerarContenidoHtml(correo);

            Assert.IsNotNull(resultado);
            Assert.AreEqual("<div>HTML generado</div>", resultado);
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeNombreClienteEnMensaje()
        {
            var correo = CrearCorreoValido();
            correo.ClienteNombre = "Peluquería María";

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Peluquería María"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeProductosCompradosEnMensaje()
        {
            var correo = CrearCorreoValido();

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Champú Profesional"));
            Assert.IsTrue(mensajeCapturado.Contains("abc123"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_IncluyeProductosRecomendadosEnMensaje()
        {
            var correo = CrearCorreoValido();
            correo.ProductosRecomendados = new List<ProductoRecomendadoDTO>
            {
                new ProductoRecomendadoDTO
                {
                    ProductoId = "RECO1",
                    NombreProducto = "Mascarilla Premium",
                    VideoYoutubeId = "abc123",
                    VideoTitulo = "Tutorial",
                    EnlaceVideoProducto = "https://youtube.com/watch?v=abc123&t=200"
                }
            };

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(mensajeCapturado.Contains("Mascarilla Premium"));
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SinProductosRecomendados_NoIncluyeSeccionRecomendados()
        {
            var correo = CrearCorreoValido();
            correo.ProductosRecomendados = new List<ProductoRecomendadoDTO>();

            string mensajeCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => mensajeCapturado = user)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsFalse(mensajeCapturado.Contains("PRODUCTOS RECOMENDADOS"));
        }

        [TestMethod]
        public void AgregarUtm_UrlSinParametros_AgregaConInterrogacion()
        {
            var url = "https://youtube.com/watch?v=abc123&t=90";

            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(url);

            Assert.IsTrue(resultado.Contains("&utm_source=correo_postcompra"));
            Assert.IsTrue(resultado.Contains("&utm_medium=email"));
            Assert.IsTrue(resultado.Contains("&utm_campaign=tutoriales_postcompra"));
        }

        [TestMethod]
        public void AgregarUtm_UrlSinQueryString_AgregaConInterrogacion()
        {
            var url = "https://tienda.nuevavision.es/producto.html";

            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(url);

            Assert.IsTrue(resultado.Contains("?utm_source=correo_postcompra"));
        }

        [TestMethod]
        public void AgregarUtm_UrlNull_DevuelveNull()
        {
            var resultado = GeneradorContenidoCorreoPostCompra.AgregarUtm(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public async Task GenerarContenidoHtml_SystemPromptMencionaNuevaVision()
        {
            var correo = CrearCorreoValido();

            string systemCapturado = null;
            A.CallTo(() => _servicioOpenAIMock.GenerarCorreoHtmlAsync(
                A<string>.Ignored, A<string>.Ignored))
                .Invokes((string system, string user) => systemCapturado = system)
                .Returns("<div>HTML</div>");

            await _generador.GenerarContenidoHtml(correo);

            Assert.IsTrue(systemCapturado.Contains("Nueva Visi"));
            Assert.IsTrue(systemCapturado.Contains("916"));
        }

        #region Issue #152: AplicarPlantilla

        [TestMethod]
        public void AplicarPlantilla_SustituyeSaludo()
        {
            string plantilla = "<p>{{SALUDO}}, te enviamos tus vídeos.</p>";
            var datos = CrearCorreoValido();

            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(plantilla, "Hola María", datos);

            Assert.IsTrue(resultado.Contains("Hola María"));
            Assert.IsFalse(resultado.Contains("{{SALUDO}}"));
        }

        [TestMethod]
        public void AplicarPlantilla_SustituyeProductosComprados()
        {
            string plantilla = @"{{#PRODUCTOS_COMPRADOS}}<div>{{NOMBRE_PRODUCTO}} - {{YOUTUBE_ID}}</div>{{/PRODUCTOS_COMPRADOS}}";
            var datos = CrearCorreoValido();

            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(plantilla, "Hola", datos);

            Assert.IsTrue(resultado.Contains("Champú Profesional"));
            Assert.IsTrue(resultado.Contains("abc123"));
            Assert.IsFalse(resultado.Contains("{{NOMBRE_PRODUCTO}}"));
        }

        [TestMethod]
        public void AplicarPlantilla_VariosProductosComprados_RepiteBloque()
        {
            string plantilla = @"{{#PRODUCTOS_COMPRADOS}}<div>{{NOMBRE_PRODUCTO}}</div>{{/PRODUCTOS_COMPRADOS}}";
            var datos = CrearCorreoValido();
            datos.ProductosComprados.Add(new ProductoCompradoConVideoDTO
            {
                ProductoId = "PROD002",
                NombreProducto = "Mascarilla Premium",
                VideoYoutubeId = "xyz789",
                VideoTitulo = "Tutorial Mascarilla",
                EnlaceVideoProducto = "https://youtube.com/watch?v=xyz789"
            });

            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(plantilla, "Hola", datos);

            Assert.IsTrue(resultado.Contains("Champú Profesional"));
            Assert.IsTrue(resultado.Contains("Mascarilla Premium"));
        }

        [TestMethod]
        public void AplicarPlantilla_ConRecomendados_MantieneBloque()
        {
            string plantilla = @"Comprados{{#SI_HAY_RECOMENDADOS}}<div>Recomendados:{{#PRODUCTOS_RECOMENDADOS}}<a>{{NOMBRE_PRODUCTO}}</a>{{/PRODUCTOS_RECOMENDADOS}}</div>{{/SI_HAY_RECOMENDADOS}}";
            var datos = CrearCorreoValido();
            datos.ProductosRecomendados = new List<ProductoRecomendadoDTO>
            {
                new ProductoRecomendadoDTO { NombreProducto = "Sérum Extra", EnlaceVideoProducto = "https://youtube.com/watch?v=rec1", EnlaceTienda = "https://tienda.com/serum" }
            };

            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(plantilla, "Hola", datos);

            Assert.IsTrue(resultado.Contains("Sérum Extra"));
            Assert.IsTrue(resultado.Contains("Recomendados:"));
        }

        [TestMethod]
        public void AplicarPlantilla_SinRecomendados_EliminaBloqueCondicional()
        {
            string plantilla = @"Comprados{{#SI_HAY_RECOMENDADOS}}<div>Recomendados</div>{{/SI_HAY_RECOMENDADOS}}Fin";
            var datos = CrearCorreoValido();
            datos.ProductosRecomendados = new List<ProductoRecomendadoDTO>();

            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(plantilla, "Hola", datos);

            Assert.IsFalse(resultado.Contains("Recomendados"));
            Assert.IsTrue(resultado.Contains("Comprados"));
            Assert.IsTrue(resultado.Contains("Fin"));
        }

        [TestMethod]
        public void AplicarPlantilla_DatosNull_DevuelveNull()
        {
            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla("<p>test</p>", "Hola", null);
            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void AplicarPlantilla_PlantillaNull_DevuelveNull()
        {
            var resultado = GeneradorContenidoCorreoPostCompra.AplicarPlantilla(null, "Hola", CrearCorreoValido());
            Assert.IsNull(resultado);
        }

        #endregion

        #region PartirEnLotes

        [TestMethod]
        public void PartirEnLotes_ListaMenorQueLote_DevuelveUnSoloLote()
        {
            var lista = new List<string> { "A", "B", "C" };
            var resultado = GeneradorContenidoCorreoPostCompra.PartirEnLotes(lista, 200);
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(3, resultado[0].Count);
        }

        [TestMethod]
        public void PartirEnLotes_ListaExactaAlLote_DevuelveUnSoloLote()
        {
            var lista = Enumerable.Range(1, 200).Select(i => i.ToString()).ToList();
            var resultado = GeneradorContenidoCorreoPostCompra.PartirEnLotes(lista, 200);
            Assert.AreEqual(1, resultado.Count);
        }

        [TestMethod]
        public void PartirEnLotes_ListaMayorQueLote_DevuelveVariosLotes()
        {
            var lista = Enumerable.Range(1, 450).Select(i => i.ToString()).ToList();
            var resultado = GeneradorContenidoCorreoPostCompra.PartirEnLotes(lista, 200);
            Assert.AreEqual(3, resultado.Count);
            Assert.AreEqual(200, resultado[0].Count);
            Assert.AreEqual(200, resultado[1].Count);
            Assert.AreEqual(50, resultado[2].Count);
        }

        #endregion

        #region EliminarBloque y SustituirBloqueRepetible

        [TestMethod]
        public void EliminarBloque_EliminaContenidoEntreMarcadores()
        {
            string html = "Antes{{#BLOQUE}}contenido a eliminar{{/BLOQUE}}Después";
            var resultado = GeneradorContenidoCorreoPostCompra.EliminarBloque(html, "{{#BLOQUE}}", "{{/BLOQUE}}");
            Assert.AreEqual("AntesDespués", resultado);
        }

        [TestMethod]
        public void SustituirBloqueRepetible_SinMarcadores_DevuelveOriginal()
        {
            string html = "<p>Sin marcadores</p>";
            var resultado = GeneradorContenidoCorreoPostCompra.SustituirBloqueRepetible(
                html, "{{#X}}", "{{/X}}", new List<Dictionary<string, string>>());
            Assert.AreEqual(html, resultado);
        }

        #endregion

        private CorreoPostCompraClienteDTO CrearCorreoValido()
        {
            return new CorreoPostCompraClienteDTO
            {
                Empresa = "1",
                ClienteId = "00001",
                ClienteNombre = "Cliente Test",
                ClienteEmail = "test@test.com",
                ProductosComprados = new List<ProductoCompradoConVideoDTO>
                {
                    new ProductoCompradoConVideoDTO
                    {
                        ProductoId = "PROD001",
                        NombreProducto = "Champú Profesional",
                        BaseImponibleTotal = 50m,
                        VideoYoutubeId = "abc123",
                        VideoTitulo = "Tutorial Champú",
                        EnlaceVideoProducto = "https://youtube.com/watch?v=abc123&t=120"
                    }
                },
                ProductosRecomendados = new List<ProductoRecomendadoDTO>()
            };
        }
    }
}
