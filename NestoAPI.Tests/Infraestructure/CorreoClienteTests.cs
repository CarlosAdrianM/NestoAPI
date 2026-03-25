using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infraestructure
{
    [TestClass]
    public class CorreoClienteTests
    {
        [TestMethod]
        public void CorreoAgencia_ConCargoAgencia_DevuelveCorreoAgencia()
        {
            var personas = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente { Cargo = 0, CorreoElectrónico = "general@test.com" },
                new PersonaContactoCliente { Cargo = 26, CorreoElectrónico = "agencia@test.com" }
            };
            var correo = new CorreoCliente(personas);
            Assert.AreEqual("agencia@test.com", correo.CorreoAgencia());
        }

        [TestMethod]
        public void CorreoAgencia_SinCargoAgencia_DevuelvePrimeroConCorreo()
        {
            var personas = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente { Cargo = 0, CorreoElectrónico = null },
                new PersonaContactoCliente { Cargo = 1, CorreoElectrónico = "otro@test.com" }
            };
            var correo = new CorreoCliente(personas);
            Assert.AreEqual("otro@test.com", correo.CorreoAgencia());
        }

        [TestMethod]
        public void CorreoAgencia_ListaVacia_DevuelveVacio()
        {
            var correo = new CorreoCliente(new List<PersonaContactoCliente>());
            Assert.AreEqual(string.Empty, correo.CorreoAgencia());
        }

        [TestMethod]
        public void CorreoAgencia_ConEspacios_DevuelveTrimeado()
        {
            var personas = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente { Cargo = 26, CorreoElectrónico = "  agencia@test.com  " }
            };
            var correo = new CorreoCliente(personas);
            Assert.AreEqual("agencia@test.com", correo.CorreoAgencia());
        }
    }
}
