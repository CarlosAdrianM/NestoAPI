using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ServicioAgenciasTests
    {
        // NestoAPI#370: el parseo de address_components indexaba types[0] y reventaba con
        // ArgumentOutOfRangeException cuando Google devolvía un componente con types vacío

        [TestMethod]
        public void ExtraerCodigoPostalGoogle_ComponenteConTypesVacio_NoRevientaYSigueBuscando()
        {
            JToken componentes = JToken.Parse(@"[
                { ""short_name"": ""Madrid"", ""types"": [] },
                { ""short_name"": ""28022"", ""types"": [""postal_code""] }
            ]");

            string codigoPostal = ServicioAgencias.ExtraerCodigoPostalGoogle(componentes);

            Assert.AreEqual("28022", codigoPostal);
        }

        [TestMethod]
        public void ExtraerCodigoPostalGoogle_PostalCodeNoEsElPrimerType_LoEncuentra()
        {
            JToken componentes = JToken.Parse(@"[
                { ""short_name"": ""28022"", ""types"": [""political"", ""postal_code""] }
            ]");

            string codigoPostal = ServicioAgencias.ExtraerCodigoPostalGoogle(componentes);

            Assert.AreEqual("28022", codigoPostal);
        }

        [TestMethod]
        public void ExtraerCodigoPostalGoogle_SinCodigoPostal_DevuelveVacio()
        {
            JToken componentes = JToken.Parse(@"[
                { ""short_name"": ""Madrid"", ""types"": [""locality""] },
                { ""short_name"": ""España"", ""types"": [""country""] }
            ]");

            string codigoPostal = ServicioAgencias.ExtraerCodigoPostalGoogle(componentes);

            Assert.AreEqual("", codigoPostal);
        }

        [TestMethod]
        public void ExtraerCodigoPostalGoogle_ComponentesNull_DevuelveVacio()
        {
            string codigoPostal = ServicioAgencias.ExtraerCodigoPostalGoogle(null);

            Assert.AreEqual("", codigoPostal);
        }

        [TestMethod]
        public void ExtraerCodigoPostalGoogle_ComponenteSinTypes_NoRevienta()
        {
            JToken componentes = JToken.Parse(@"[
                { ""short_name"": ""Madrid"" },
                { ""short_name"": ""28006"", ""types"": [""postal_code""] }
            ]");

            string codigoPostal = ServicioAgencias.ExtraerCodigoPostalGoogle(componentes);

            Assert.AreEqual("28006", codigoPostal);
        }
    }
}
