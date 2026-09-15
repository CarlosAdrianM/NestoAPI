using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.OpenAI;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#484: el saludo de los correos post-compra no puede llevar el nombre de una
    /// empresa. El modelo lo hacía bien con lotes pequeños y dejó de hacerlo con 30-49 nombres:
    /// la regla se aplica ahora también en código.
    /// </summary>
    [TestClass]
    public class GeneradorContenidoCorreoPostCompraSaludosTests
    {
        [TestMethod]
        public void SanearSaludo_LosDosCasosRealesDel12Sep_QuedanEnGenerico()
        {
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("THE LOOK GETAFE SL", "Hola The Look Getafe SL"));
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("EUROBEAUTY CANARIAS, SRL", "Hola Eurobeauty Canarias, SRL"));
        }

        [TestMethod]
        public void SanearSaludo_SiglasDeSociedadEnElSaludo_ConYSinPuntos()
        {
            foreach (string saludo in new[] { "Hola Peluquería Rosa S.L.", "¡Hola Estética Sol S.L.U.!", "Hola Belleza SLU",
                "Hola Centro Luz, S.A.", "Hola Distribuciones Norte SA", "Hola Ana y Eva C.B.", "Hola Ana y Eva CB",
                "Hola Salón Marta S.C.P.", "Hola Cooperativa S.COOP." })
            {
                Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("cliente", saludo), saludo);
            }
        }

        [TestMethod]
        public void SanearSaludo_NombreDeClienteConSiglas_SiempreGenerico()
        {
            // Aunque el modelo saque un nombre de pila, si la ficha es una sociedad no personalizamos
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("MARIA PELUQUEROS S.L.", "Hola María"));
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("BEAUTY GETAFE SL", "¡Hola Beauty!"));
        }

        [TestMethod]
        public void SanearSaludo_SaludosAPersonas_SeRespetan()
        {
            Assert.AreEqual("Hola Rosa", GeneradorContenidoCorreoPostCompra.SanearSaludo("Rosa Martínez", "Hola Rosa"));
            Assert.AreEqual("¡Hola Carlos!", GeneradorContenidoCorreoPostCompra.SanearSaludo("Carlos Adrián Martínez", "¡Hola Carlos!"));
            Assert.AreEqual("Hola María José", GeneradorContenidoCorreoPostCompra.SanearSaludo("María José López", "Hola María José"));
            Assert.AreEqual("Buenos días, Ana", GeneradorContenidoCorreoPostCompra.SanearSaludo("Ana Ruiz", "Buenos días, Ana"));
            // Las siglas no casan dentro de una palabra ("Sara" contiene "sa"; "Isla" contiene "sl")
            Assert.AreEqual("Hola Sara", GeneradorContenidoCorreoPostCompra.SanearSaludo("Sara Gil", "Hola Sara"));
            Assert.AreEqual("Hola Isla", GeneradorContenidoCorreoPostCompra.SanearSaludo("Isla Bonita", "Hola Isla"));
        }

        [TestMethod]
        public void SanearSaludo_SaludosGenericos_SeRespetan()
        {
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("CENTRO ESTÉTICA LUZ", "Hola"));
            Assert.AreEqual("¡Hola!", GeneradorContenidoCorreoPostCompra.SanearSaludo("CENTRO ESTÉTICA LUZ", "¡Hola!"));
            Assert.AreEqual("Buenos días", GeneradorContenidoCorreoPostCompra.SanearSaludo("CENTRO ESTÉTICA LUZ", "Buenos días"));
        }

        [TestMethod]
        public void SanearSaludo_NombreEnteroSinSiglas_QuedaEnGenerico()
        {
            // Tres o más palabras que no son fórmula de saludo: es el nombre del cliente pegado
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("CENTRO DE ESTETICA LUZ Y SOL", "Hola Centro Estética Luz"));
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("Rosa Martínez García", "Hola Rosa Martínez García"));
        }

        [TestMethod]
        public void SanearSaludo_VacioONulo_Generico()
        {
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("Rosa", null));
            Assert.AreEqual("Hola", GeneradorContenidoCorreoPostCompra.SanearSaludo("Rosa", "   "));
        }

        [TestMethod]
        public async Task GenerarSaludosLote_LoQueDevuelveElModeloPasaPorLaGuarda()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Returns(Task.FromResult("[\"Hola The Look Getafe SL\", \"Hola Rosa\"]"));
            var generador = new GeneradorContenidoCorreoPostCompra(openAI);

            var saludos = await generador.GenerarSaludosLoteAsync(new List<string> { "THE LOOK GETAFE SL", "Rosa Martínez" });

            Assert.AreEqual("Hola", saludos["THE LOOK GETAFE SL"]);
            Assert.AreEqual("Hola Rosa", saludos["Rosa Martínez"]);
        }

        [TestMethod]
        public async Task GenerarSaludos_LotesDeComoMucho20Nombres()
        {
            // 49 nombres (la semana del 09/09) tienen que ir en 3 llamadas, no en 1
            var openAI = A.Fake<IServicioOpenAI>();
            var tamanosDeLote = new List<int>();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .ReturnsLazily((string sys, string user, int max, double temp, string modelo) =>
                {
                    var lote = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(user);
                    tamanosDeLote.Add(lote.Count);
                    return Task.FromResult(Newtonsoft.Json.JsonConvert.SerializeObject(lote.ConvertAll(n => "Hola")));
                });
            var nombres = new List<string>();
            for (int i = 0; i < 49; i++)
            {
                nombres.Add($"Cliente {i}");
            }

            var saludos = await new GeneradorContenidoCorreoPostCompra(openAI).GenerarSaludosAsync(nombres);

            Assert.AreEqual(49, saludos.Count);
            Assert.AreEqual(3, tamanosDeLote.Count);
            Assert.IsTrue(tamanosDeLote.TrueForAll(t => t <= GeneradorContenidoCorreoPostCompra.TAMANO_LOTE_SALUDOS));
        }
    }
}
