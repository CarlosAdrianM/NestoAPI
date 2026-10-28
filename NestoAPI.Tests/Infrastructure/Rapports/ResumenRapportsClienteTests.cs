using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infraestructure.Rapports;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Rapports
{
    /// <summary>Sugerencia 830 (Javier): «Resumir rapports» por el servicio común de OpenAI, con el texto de siempre.</summary>
    [TestClass]
    public class ResumenRapportsClienteTests
    {
        [TestMethod]
        public void TextoEntrada_EnOrdenCronologico_ConFechaYSiTerminoEnPedido()
        {
            string texto = ResumenRapportsCliente.TextoEntrada(new[]
            {
                new RapportParaResumen { Fecha = new DateTime(2026, 10, 20, 10, 0, 0), Comentarios = "Quiere probar Visnú", Pedido = true },
                new RapportParaResumen { Fecha = new DateTime(2026, 10, 2, 9, 30, 0), Comentarios = "Usa Eva Visnú", Pedido = false }
            });

            StringAssert.StartsWith(texto, "Resumen de seguimientos de un cliente.");
            Assert.IsTrue(texto.IndexOf("2026-10-02", StringComparison.Ordinal) < texto.IndexOf("2026-10-20", StringComparison.Ordinal), "cronológico");
            StringAssert.Contains(texto, "Fecha: 2026-10-02 09:30:00\nComentario: Usa Eva Visnú\nTerminó en pedido: No");
            StringAssert.Contains(texto, "Terminó en pedido: Sí");
        }

        [TestMethod]
        public async Task Resumir_UsaElServicioComun_ConElPromptYElTextoRecortado()
        {
            IServicioOpenAI openAI = A.Fake<IServicioOpenAI>();
            A.CallTo(() => openAI.GenerarContenidoAsync(ResumenRapportsCliente.PROMPT_SISTEMA, A<string>.That.Contains("Usa Eva Visnú"),
                ResumenRapportsCliente.MAX_TOKENS, ResumenRapportsCliente.TEMPERATURA, A<string>._))
                .Returns(Task.FromResult("  El cliente usa Eva Visnú.  "));

            string resumen = await ResumenRapportsCliente.Resumir(openAI, new[] { new RapportParaResumen { Fecha = DateTime.Today, Comentarios = "Usa Eva Visnú" } });

            Assert.AreEqual("El cliente usa Eva Visnú.", resumen);
        }

        [TestMethod]
        public async Task Resumir_SinRespuesta_Null_YSiOpenAILanza_SaleTalCual()
        {
            IServicioOpenAI vacio = A.Fake<IServicioOpenAI>();
            A.CallTo(() => vacio.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Returns(Task.FromResult("  "));
            Assert.IsNull(await ResumenRapportsCliente.Resumir(vacio, new List<RapportParaResumen>()));

            IServicioOpenAI roto = A.Fake<IServicioOpenAI>();
            A.CallTo(() => roto.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._)).Throws(new InvalidOperationException("model_not_found"));
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => ResumenRapportsCliente.Resumir(roto, new List<RapportParaResumen>()));
        }
    }
}
