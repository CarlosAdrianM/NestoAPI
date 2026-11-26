using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>26/11/26: rastro de quién llama sin token a los endpoints que aún no lo exigen (una vez al día por endpoint e IP).</summary>
    [TestClass]
    public class RastroLlamadasSinTokenTests
    {
        private const string RUTA = "PUT api/ParametrosUsuario";

        [TestMethod]
        public void DebeApuntar_LaPrimeraDelDia_SiYLasSiguientesNo()
        {
            var memoria = new MemoriaRastroSinToken();
            DateTime manana = new DateTime(2026, 11, 26, 9, 0, 0);

            Assert.IsTrue(memoria.DebeApuntar(RUTA, "10.0.0.5", manana));
            Assert.IsFalse(memoria.DebeApuntar(RUTA, "10.0.0.5", manana.AddHours(3)));
            Assert.IsFalse(memoria.DebeApuntar(RUTA, "10.0.0.5", manana.AddHours(14).AddMinutes(59)), "el mismo día, a las 23:59");
        }

        [TestMethod]
        public void DebeApuntar_OtroDia_OtraVez()
        {
            var memoria = new MemoriaRastroSinToken();
            DateTime dia = new DateTime(2026, 11, 26, 23, 59, 0);

            Assert.IsTrue(memoria.DebeApuntar(RUTA, "10.0.0.5", dia));
            Assert.IsTrue(memoria.DebeApuntar(RUTA, "10.0.0.5", dia.AddMinutes(2)), "pasada la medianoche");
        }

        [TestMethod]
        public void DebeApuntar_PorEndpointEIp()
        {
            var memoria = new MemoriaRastroSinToken();
            DateTime ahora = new DateTime(2026, 11, 26, 9, 0, 0);

            Assert.IsTrue(memoria.DebeApuntar(RUTA, "10.0.0.5", ahora));
            Assert.IsTrue(memoria.DebeApuntar(RUTA, "10.0.0.6", ahora), "otra IP");
            Assert.IsTrue(memoria.DebeApuntar("GET api/ExtractosCliente/EnviarCorreoDomiciliacionDia", "10.0.0.5", ahora), "otro endpoint");
            Assert.IsFalse(memoria.DebeApuntar(RUTA + " ", " 10.0.0.5", ahora), "sin relleno");
        }

        [TestMethod]
        public void Texto_ConYSinDatos()
        {
            Assert.AreEqual("[Sin token] PUT api/ParametrosUsuario desde 10.0.0.5, User-Agent Nesto/1.10",
                RastroLlamadasSinToken.Texto(RUTA, "10.0.0.5", "Nesto/1.10"));
            Assert.AreEqual("[Sin token] PUT api/ParametrosUsuario desde (IP desconocida), User-Agent (sin User-Agent)",
                RastroLlamadasSinToken.Texto(RUTA, null, " "));
        }

        [TestMethod]
        public void Apuntar_NuncaLanza()
        {
            RastroLlamadasSinToken.Apuntar(null, null, RUTA);
            RastroLlamadasSinToken.Apuntar(new System.Net.Http.HttpRequestMessage(), null, RUTA);
        }
    }
}
