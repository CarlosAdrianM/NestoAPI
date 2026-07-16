using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Direcciones;
using NestoAPI.Models.Direcciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// NestoAPI#306: proxy de Google Places para el autocompletado de direcciones.
    /// </summary>
    [TestClass]
    public class ServicioDireccionesGoogleTests
    {
        [TestMethod]
        public void ParsearSugerencias_RespuestaOK_DevuelveDescripcionYPlaceId()
        {
            string json = @"{
                ""status"": ""OK"",
                ""predictions"": [
                    { ""description"": ""Avenida de Castilla, 3, San Fernando de Henares, España"", ""place_id"": ""ChIJ111"" },
                    { ""description"": ""Calle de Castilla, 3, Madrid, España"", ""place_id"": ""ChIJ222"" }
                ]}";

            List<SugerenciaDireccionDTO> sugerencias = ServicioDireccionesGoogle.ParsearSugerencias(json);

            Assert.AreEqual(2, sugerencias.Count);
            Assert.AreEqual("Avenida de Castilla, 3, San Fernando de Henares, España", sugerencias[0].Descripcion);
            Assert.AreEqual("ChIJ111", sugerencias[0].PlaceId);
        }

        [TestMethod]
        public void ParsearSugerencias_ZeroResults_DevuelveListaVaciaSinError()
        {
            List<SugerenciaDireccionDTO> sugerencias =
                ServicioDireccionesGoogle.ParsearSugerencias(@"{ ""status"": ""ZERO_RESULTS"", ""predictions"": [] }");

            Assert.AreEqual(0, sugerencias.Count);
        }

        [TestMethod]
        public void ParsearSugerencias_RequestDenied_LanzaConElMotivo()
        {
            // Lo que devolverá Google hasta que se habilite Places API en la consola
            var ex = Assert.ThrowsException<Exception>(() => ServicioDireccionesGoogle.ParsearSugerencias(
                @"{ ""status"": ""REQUEST_DENIED"", ""error_message"": ""This API project is not authorized to use this API."" }"));

            StringAssert.Contains(ex.Message, "REQUEST_DENIED");
            StringAssert.Contains(ex.Message, "not authorized");
        }

        [TestMethod]
        public void ParsearDetalle_TroceaCalleNumeroYCodigoPostal()
        {
            string json = @"{
                ""status"": ""OK"",
                ""result"": {
                    ""formatted_address"": ""Av. de Castilla, 3, 28830 San Fernando de Henares, Madrid, España"",
                    ""address_components"": [
                        { ""long_name"": ""3"", ""types"": [""street_number""] },
                        { ""long_name"": ""Avenida de Castilla"", ""types"": [""route""] },
                        { ""long_name"": ""San Fernando de Henares"", ""types"": [""locality"", ""political""] },
                        { ""long_name"": ""Madrid"", ""types"": [""administrative_area_level_2"", ""political""] },
                        { ""long_name"": ""28830"", ""types"": [""postal_code""] }
                    ]}}";

            DireccionDetalleDTO detalle = ServicioDireccionesGoogle.ParsearDetalle(json);

            Assert.AreEqual("Avenida de Castilla", detalle.Calle);
            Assert.AreEqual("3", detalle.Numero);
            Assert.AreEqual("28830", detalle.CodigoPostal);
            Assert.AreEqual("San Fernando de Henares", detalle.Poblacion);
            Assert.AreEqual("Madrid", detalle.Provincia);
            StringAssert.Contains(detalle.DireccionFormateada, "28830");
        }

        [TestMethod]
        public void ParsearDetalle_SinNumero_LosDemasComponentesLlegan()
        {
            // Direcciones sin número (el usuario aún no lo ha tecleado o Google no lo tiene)
            string json = @"{
                ""status"": ""OK"",
                ""result"": {
                    ""formatted_address"": ""Avenida de Castilla, San Fernando de Henares, España"",
                    ""address_components"": [
                        { ""long_name"": ""Avenida de Castilla"", ""types"": [""route""] },
                        { ""long_name"": ""28830"", ""types"": [""postal_code""] }
                    ]}}";

            DireccionDetalleDTO detalle = ServicioDireccionesGoogle.ParsearDetalle(json);

            Assert.AreEqual("Avenida de Castilla", detalle.Calle);
            Assert.IsNull(detalle.Numero);
            Assert.AreEqual("28830", detalle.CodigoPostal);
        }

        [TestMethod]
        public async Task GetSugerencias_TextoCorto_DevuelveVaciaSinLlamarAGoogle()
        {
            IServicioDireccionesGoogle servicio = A.Fake<IServicioDireccionesGoogle>();
            var controller = new DireccionesController(servicio);

            var resultado = await controller.GetSugerencias("Av") as OkNegotiatedContentResult<List<SugerenciaDireccionDTO>>;

            Assert.AreEqual(0, resultado.Content.Count);
            A.CallTo(() => servicio.BuscarSugerencias(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetSugerencias_TextoValido_DelegaEnElServicioConElSessionToken()
        {
            IServicioDireccionesGoogle servicio = A.Fake<IServicioDireccionesGoogle>();
            _ = A.CallTo(() => servicio.BuscarSugerencias("Avenida Castilla 3", "token-1"))
                .Returns(new List<SugerenciaDireccionDTO> { new SugerenciaDireccionDTO { PlaceId = "ChIJ111" } });
            var controller = new DireccionesController(servicio);

            var resultado = await controller.GetSugerencias("Avenida Castilla 3", "token-1") as OkNegotiatedContentResult<List<SugerenciaDireccionDTO>>;

            Assert.AreEqual("ChIJ111", resultado.Content.Single().PlaceId);
        }

        [TestMethod]
        public async Task GetDetalle_SinPlaceId_DevuelveBadRequest()
        {
            var controller = new DireccionesController(A.Fake<IServicioDireccionesGoogle>());

            var resultado = await controller.GetDetalle("  ");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
