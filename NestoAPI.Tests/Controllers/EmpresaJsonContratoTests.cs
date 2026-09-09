using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340 (Agencias): Nesto deserializa GET api/Empresas sobre su entidad Empresas para armar
    /// el remitente de las etiquetas de ASM y Correos Express. Si un nombre de propiedad cambiara,
    /// Newtonsoft dejaría el valor en null sin avisar y la etiqueta saldría con el remitente vacío.
    /// Test gemelo en Nesto: ViewModels.Tests/AgenciaServiceEmpresasTests.vb.
    /// </summary>
    [TestClass]
    public class EmpresaJsonContratoTests
    {
        [TestMethod]
        public void GetEmpresas_SerializaLosCamposQueUsanLasEtiquetasConSusNombres()
        {
            var empresa = new Empresa
            {
                Número = "1  ",
                Nombre = "NUEVA VISIÓN",
                NIF = "B12345678",
                Dirección = "C/ Prueba 1",
                CodPostal = "28100",
                Población = "ALGETE",
                Provincia = "MADRID",
                Teléfono = "916000000",
                Email = "info@nuevavision.es"
            };

            JObject json = JObject.Parse(JsonConvert.SerializeObject(empresa));

            // Sin recortar: Nesto compara con CampoIgual (trim + ignore case), como hacía SQL Server.
            Assert.AreEqual("1  ", (string)json["Número"]);
            Assert.AreEqual("NUEVA VISIÓN", (string)json["Nombre"]);
            Assert.AreEqual("B12345678", (string)json["NIF"]);
            Assert.AreEqual("C/ Prueba 1", (string)json["Dirección"]);
            Assert.AreEqual("28100", (string)json["CodPostal"]);
            Assert.AreEqual("ALGETE", (string)json["Población"]);
            Assert.AreEqual("MADRID", (string)json["Provincia"]);
            Assert.AreEqual("916000000", (string)json["Teléfono"]);
            Assert.AreEqual("info@nuevavision.es", (string)json["Email"]);
        }
    }
}
