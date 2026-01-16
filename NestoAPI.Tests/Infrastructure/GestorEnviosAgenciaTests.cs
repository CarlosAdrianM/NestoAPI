using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorEnviosAgenciaTests
    {
        #region Tests para GenerarPiePromocionApp (Issue #73)

        [TestMethod]
        public void GenerarPiePromocionApp_ConVideoprotocolo_IncluyeTituloYMiniatura()
        {
            // Arrange
            var videoprotocolo = new VideoLookupModel
            {
                Id = 1,
                VideoId = "dQw4w9WgXcQ",
                Titulo = "Protocolo de tratamiento facial",
                Descripcion = "Aprende paso a paso cómo realizar un tratamiento facial profesional",
                EsUnProtocolo = true
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsTrue(html.Contains("Protocolo de tratamiento facial"), "Debe incluir el título del videoprotocolo");
            Assert.IsTrue(html.Contains("dQw4w9WgXcQ"), "Debe incluir el VideoId en la URL de la miniatura");
            Assert.IsTrue(html.Contains("img.youtube.com"), "Debe incluir la URL de YouTube para la miniatura");
            Assert.IsTrue(html.Contains("ÚLTIMO PROTOCOLO"), "Debe indicar que es el último protocolo");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_SinVideoprotocolo_GeneraHTMLSinSeccionVideo()
        {
            // Arrange
            VideoLookupModel videoprotocolo = null;

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsFalse(html.Contains("ÚLTIMO PROTOCOLO"), "No debe incluir sección de último protocolo");
            Assert.IsTrue(html.Contains("Google Play"), "Debe incluir la promoción de Google Play");
            Assert.IsTrue(html.Contains("play.google.com"), "Debe incluir el enlace a la app");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_Siempre_IncluyeEnlaceGooglePlay()
        {
            // Arrange
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "test123",
                Titulo = "Test"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsTrue(html.Contains("play.google.com/store/apps/details?id=com.nuevavision.nestotiendas"),
                "Debe incluir el enlace completo a Google Play");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_Siempre_IncluyeEnlaceTextoFallback()
        {
            // Arrange - Outlook bloquea imágenes, necesitamos enlace de texto
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "test123",
                Titulo = "Test"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsTrue(html.Contains("¿No ve la imagen?"), "Debe incluir texto de fallback para Outlook");
            // Debe haber al menos 2 enlaces a Google Play (imagen + texto)
            int countEnlaces = System.Text.RegularExpressions.Regex.Matches(html, "play.google.com").Count;
            Assert.IsTrue(countEnlaces >= 2, "Debe incluir al menos 2 enlaces a Google Play (imagen + texto fallback)");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_TituloLargo_SeTrunca()
        {
            // Arrange
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "test123",
                Titulo = "Este es un título muy largo que debería ser truncado porque excede el límite de caracteres permitidos para la visualización",
                Descripcion = "Descripción corta"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsFalse(html.Contains("permitidos para la visualización"), "El título largo debe truncarse");
            Assert.IsTrue(html.Contains("..."), "El título truncado debe terminar con ...");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_DescripcionLarga_SeTrunca()
        {
            // Arrange
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "test123",
                Titulo = "Título corto",
                Descripcion = "Esta es una descripción muy larga que definitivamente excede el límite de 100 caracteres y por lo tanto debería ser truncada con puntos suspensivos al final"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsFalse(html.Contains("puntos suspensivos al final"), "La descripción larga debe truncarse");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_CaracteresEspeciales_SeEscapanCorrectamente()
        {
            // Arrange - Prevenir XSS y problemas de renderizado HTML
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "test123",
                Titulo = "Título con <script>alert('xss')</script> y \"comillas\"",
                Descripcion = "Descripción con & ampersand"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsFalse(html.Contains("<script>"), "Los tags HTML deben escaparse");
            Assert.IsTrue(html.Contains("&lt;script&gt;") || !html.Contains("script"), "Los caracteres < > deben escaparse");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_VideoSinVideoId_NoMuestraSeccionVideo()
        {
            // Arrange
            var videoprotocolo = new VideoLookupModel
            {
                Id = 1,
                VideoId = null, // Sin VideoId
                Titulo = "Título sin video",
                EsUnProtocolo = true
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsFalse(html.Contains("ÚLTIMO PROTOCOLO"), "No debe mostrar sección de protocolo si no hay VideoId");
            Assert.IsFalse(html.Contains("img.youtube.com"), "No debe intentar mostrar miniatura sin VideoId");
        }

        [TestMethod]
        public void GenerarPiePromocionApp_UsaUrlMiniaturaMqdefault()
        {
            // Arrange - mqdefault (320x180) es el tamaño recomendado para email
            var videoprotocolo = new VideoLookupModel
            {
                VideoId = "ABC123xyz",
                Titulo = "Test"
            };

            // Act
            string html = GestorEnviosAgencia.GenerarPiePromocionApp(videoprotocolo);

            // Assert
            Assert.IsTrue(html.Contains("img.youtube.com/vi/ABC123xyz/mqdefault.jpg"),
                "Debe usar mqdefault para la miniatura (320x180, buen tamaño para email)");
        }

        #endregion
    }
}
