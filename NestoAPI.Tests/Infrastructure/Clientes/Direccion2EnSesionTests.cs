using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#874 (42154/0): la clave de la Dirección 2 en la sesión de SQL, la misma que compone trgClientesUpd.</summary>
    [TestClass]
    public class Direccion2EnSesionTests
    {
        [TestMethod]
        public void Clave_SinRellenoDeLosChar()
        {
            Assert.AreEqual("Direccion2Alta:1|42154|0", Direccion2EnSesion.Clave("1  ", "42154     ", "0  "));
            Assert.AreEqual("Direccion2Alta:1|42154|0", Direccion2EnSesion.Clave(null, " 42154", null));
        }

        [TestMethod]
        public void Clave_NoPasaDeLoQueAdmiteSqlServer()
        {
            Assert.IsTrue(Direccion2EnSesion.Clave("1", new string('9', 300), "0").Length <= Direccion2EnSesion.LARGO_MAXIMO_CLAVE);
        }

        [TestMethod]
        public void Entradas_SoloLasFichasConDireccion2()
        {
            var con = new Cliente { Empresa = "1  ", Nº_Cliente = "42154     ", Contacto = "0  " };
            var sin = new Cliente { Empresa = "1  ", Nº_Cliente = "42154     ", Contacto = "1  " };

            List<KeyValuePair<string, string>> entradas = Direccion2EnSesion.Entradas(new[]
            {
                (con, ResultadoDireccion2.Bien("Piso", " 1º 42 ")),
                (sin, ResultadoDireccion2.Bien(null, null)),
                (sin, (ResultadoDireccion2)null)
            });

            Assert.AreEqual(1, entradas.Count);
            Assert.AreEqual("Direccion2Alta:1|42154|0", entradas[0].Key);
            Assert.AreEqual("1º 42", entradas[0].Value);
        }

        [TestMethod]
        public void Poner_SinNadaQuePoner_NoTocaLaBd()
        {
            using (IDisposable ambito = Direccion2EnSesion.Poner(null, new List<KeyValuePair<string, string>>()))
            {
                Assert.IsNotNull(ambito);
            }
        }

        [TestMethod]
        public void Seguro_UnFalloNoSale()
        {
            Direccion2EnSesion.ReiniciarAvisoParaTests();
            IDisposable ambito = Direccion2EnSesion.Seguro(() => throw new InvalidOperationException("sin BD"));

            Assert.IsNotNull(ambito);
            ambito.Dispose();
        }
    }
}
