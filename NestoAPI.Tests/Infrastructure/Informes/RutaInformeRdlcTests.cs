using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;

namespace NestoAPI.Tests.Infrastructure.Informes
{
    /// <summary>
    /// Regresión 29/10/26 (Enrique): el mandato SEPA enviado por correo buscaba el RDLC en c:\windows\system32\inetsrv porque la
    /// ruta era relativa y el envío corre fuera del contexto de la petición.
    /// </summary>
    [TestClass]
    public class RutaInformeRdlcTests
    {
        [TestMethod]
        public void Absoluta_RutaRelativa_ColgandoDeLaRaizDeLaAplicacion()
        {
            string ruta = RutaInformeRdlc.Absoluta(@"Models\Clientes\MandatoCoreSEPA.rdlc");

            Assert.IsTrue(Path.IsPathRooted(ruta));
            Assert.AreEqual(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Models\Clientes\MandatoCoreSEPA.rdlc"), ruta);
        }

        [TestMethod]
        public void Absoluta_RutaYaAbsolutaONula_NoLaToca()
        {
            Assert.AreEqual(@"C:\x\y.rdlc", RutaInformeRdlc.Absoluta(@"C:\x\y.rdlc"));
            Assert.IsNull(RutaInformeRdlc.Absoluta(null));
        }
    }
}
