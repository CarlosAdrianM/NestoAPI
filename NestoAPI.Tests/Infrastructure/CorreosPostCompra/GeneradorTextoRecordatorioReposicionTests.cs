using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532: una llamada a OpenAI por semana; si no vale, la plantilla fija y un error a ELMAH.</summary>
    [TestClass]
    public class GeneradorTextoRecordatorioReposicionTests
    {
        private static readonly DateTime SEMANA = new DateTime(2026, 10, 22);
        private IServicioOpenAI openAI;
        private List<Exception> errores;

        [TestInitialize]
        public void Setup()
        {
            openAI = A.Fake<IServicioOpenAI>();
            errores = new List<Exception>();
        }

        private void Responde(string respuesta)
            => A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Returns(respuesta);

        private Task<TextoRecordatorioReposicion> Generar()
            => new GeneradorTextoRecordatorioReposicion(openAI, errores.Add).Generar(SEMANA);

        private const string BUENO = @"```json
{""asunto"": ""Que no se quede sin {PRODUCTO} esta semana"",
 ""entrada"": ""Un pequeño aviso: puede que dentro de poco se le termine {PRODUCTOS}. Mejor saberlo ahora que con un cliente delante:"",
 ""cierre"": ""Si aún le queda, no haga nada. Si le hace falta, puede pedirlo en la tienda o a {VENDEDOR}, como siempre.""}
```";

        [TestMethod]
        public async Task Generar_TextoQueCumple_LoUsaConSuVersion()
        {
            Responde(BUENO);

            TextoRecordatorioReposicion texto = await Generar();

            Assert.AreEqual(TextoRecordatorioReposicion.ORIGEN_OPENAI, texto.Origen);
            Assert.AreEqual("ia-2026-10-22", texto.Version);
            Assert.AreEqual("Que no se quede sin {PRODUCTO} esta semana", texto.Asunto);
            Assert.AreEqual(0, errores.Count);
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Generar_SiOpenAIFallaODevuelveNada_LaFijaYAvisaAElmah()
        {
            Responde(null);
            TextoRecordatorioReposicion texto = await Generar();
            Assert.AreEqual(TextoRecordatorioReposicion.ORIGEN_FIJA, texto.Origen);
            Assert.AreEqual(TextoRecordatorioReposicion.VERSION_FIJA, texto.Version);
            Assert.AreEqual(1, errores.Count);

            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Throws(new Exception("timeout"));
            texto = await Generar();
            Assert.AreEqual(TextoRecordatorioReposicion.ORIGEN_FIJA, texto.Origen);
            StringAssert.Contains(texto.MotivoFija, "timeout");
            Assert.AreEqual(2, errores.Count);
        }

        [TestMethod]
        public async Task Generar_SinMarcadores_LaFija()
        {
            Responde(BUENO.Replace("{VENDEDOR}", "su comercial"));

            TextoRecordatorioReposicion texto = await Generar();

            Assert.AreEqual(TextoRecordatorioReposicion.ORIGEN_FIJA, texto.Origen);
            StringAssert.Contains(texto.MotivoFija, "{VENDEDOR}");
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void MotivoNoValido_LaFijaVale()
        {
            Assert.IsNull(GeneradorTextoRecordatorioReposicion.MotivoNoValido(TextoRecordatorioReposicion.Fija()));
        }

        [TestMethod]
        public void MotivoNoValido_Tuteo()
        {
            foreach (string tuteo in new[] { "Si tu stock baja", "te lo dejamos", "Puedes pedirlo", "si lo necesitas", "Tú decides" })
            {
                TextoRecordatorioReposicion texto = TextoRecordatorioReposicion.Fija();
                texto.Cierre = tuteo + " a {VENDEDOR}.";
                StringAssert.Contains(GeneradorTextoRecordatorioReposicion.MotivoNoValido(texto), "tutea", tuteo);
            }
        }

        [TestMethod]
        public void MotivoNoValido_NoConfundeUstedConTu()
        {
            TextoRecordatorioReposicion texto = TextoRecordatorioReposicion.Fija();
            texto.Entrada = "Este aviso le recuerda que pronto se le puede terminar {PRODUCTOS}; si lo pide hoy, presente:";
            Assert.IsNull(GeneradorTextoRecordatorioReposicion.MotivoNoValido(texto));
        }

        [TestMethod]
        public void MotivoNoValido_PalabrasVetadas()
        {
            foreach (string vetada in new[] { "por lo que suele pedirnos", "en su camilla", "con un 10 % de descuento", "en su historial" })
            {
                TextoRecordatorioReposicion texto = TextoRecordatorioReposicion.Fija();
                texto.Entrada = "Le avisamos " + vetada + ": se le puede acabar {PRODUCTOS}.";
                Assert.IsNotNull(GeneradorTextoRecordatorioReposicion.MotivoNoValido(texto), vetada);
            }
        }

        [TestMethod]
        public void MotivoNoValido_DemasiadoLargoOConHtmlOMarcadorRepetido()
        {
            TextoRecordatorioReposicion largo = TextoRecordatorioReposicion.Fija();
            largo.Entrada = new string('a', GeneradorTextoRecordatorioReposicion.MAXIMO_ENTRADA) + " {PRODUCTOS}";
            StringAssert.Contains(GeneradorTextoRecordatorioReposicion.MotivoNoValido(largo), "largo");

            TextoRecordatorioReposicion html = TextoRecordatorioReposicion.Fija();
            html.Cierre = "<b>Pídalo</b> a {VENDEDOR}.";
            StringAssert.Contains(GeneradorTextoRecordatorioReposicion.MotivoNoValido(html), "HTML");

            TextoRecordatorioReposicion doble = TextoRecordatorioReposicion.Fija();
            doble.Asunto = "{PRODUCTO} y {PRODUCTO}";
            StringAssert.Contains(GeneradorTextoRecordatorioReposicion.MotivoNoValido(doble), "una vez");
        }

        [TestMethod]
        public void Interpretar_SinJsonONiPiezas_Null()
        {
            Assert.IsNull(GeneradorTextoRecordatorioReposicion.Interpretar("Aquí tiene su texto"));
            Assert.IsNull(GeneradorTextoRecordatorioReposicion.Interpretar("{\"asunto\": \"x {PRODUCTO}\"}"));
            Assert.IsNull(GeneradorTextoRecordatorioReposicion.Interpretar("{roto"));
        }
    }
}
