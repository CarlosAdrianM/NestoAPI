using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#447: el titular (cargo 22) gestiona desde la app qué ve cada persona de su
    /// centro, sin poder dejar al centro sin titular.
    /// </summary>
    [TestClass]
    public class PoliticaPersonasContactoTests
    {
        private static PersonaContactoCliente Persona(string contacto, string numero, short cargo, string email)
        {
            return new PersonaContactoCliente { Contacto = contacto, Número = numero, Cargo = cargo, CorreoElectrónico = email, Nombre = "Ángela" };
        }

        // El Edén: el titular (22) y la encargada (11), cada una con su correo
        private static List<PersonaContactoCliente> ElEden()
        {
            return new List<PersonaContactoCliente>
            {
                Persona("0", "1", 22, "info@esteticaeleden.com"),
                Persona("2", "1", 11, "angelamaritzaperalta@gmail.com")
            };
        }

        [TestMethod]
        public void EsTitular_SoloLaPersonaConCargo22DelEmail()
        {
            Assert.IsTrue(PoliticaPersonasContacto.EsTitular(ElEden(), "info@esteticaeleden.com"));
            Assert.IsTrue(PoliticaPersonasContacto.EsTitular(ElEden(), " INFO@esteticaeleden.com "));
            Assert.IsFalse(PoliticaPersonasContacto.EsTitular(ElEden(), "angelamaritzaperalta@gmail.com"));
            Assert.IsFalse(PoliticaPersonasContacto.EsTitular(ElEden(), null));
            Assert.IsFalse(PoliticaPersonasContacto.EsTitular(null, "info@esteticaeleden.com"));
        }

        [TestMethod]
        public void MotivoParaNoCambiar_ALaEncargadaSeLePuedePonerCualquieraDeLosTres()
        {
            List<PersonaContactoCliente> personas = ElEden();
            PersonaContactoCliente encargada = personas[1];

            Assert.IsNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, encargada, 30));
            Assert.IsNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, encargada, 31));
            Assert.IsNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, encargada, 22));
        }

        [TestMethod]
        public void MotivoParaNoCambiar_SoloLosTresCargosDeLaApp()
        {
            List<PersonaContactoCliente> personas = ElEden();

            Assert.IsNotNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, personas[1], 5), "Gerente no se pone desde la app");
            Assert.IsNotNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, personas[1], 14));
        }

        [TestMethod]
        public void MotivoParaNoCambiar_ElUnicoTitularNoPuedeDejarDeSerlo()
        {
            // Si el titular se pone a sí mismo en 30 se bloquea: nadie más vería facturas ni gestionaría
            List<PersonaContactoCliente> personas = ElEden();

            string motivo = PoliticaPersonasContacto.MotivoParaNoCambiar(personas, personas[0], 30);

            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "sin ninguna persona");
        }

        [TestMethod]
        public void MotivoParaNoCambiar_ConOtroTitularConCorreo_SiPuede()
        {
            List<PersonaContactoCliente> personas = ElEden();
            personas[1].Cargo = 22; // la encargada ya es titular también

            Assert.IsNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, personas[0], 31));
        }

        [TestMethod]
        public void MotivoParaNoCambiar_OtroTitularSinCorreoNoCuenta()
        {
            // Un 22 sin correo no puede entrar en la app: no sirve como relevo
            List<PersonaContactoCliente> personas = ElEden();
            personas.Add(Persona("0", "2", 22, ""));

            Assert.IsNotNull(PoliticaPersonasContacto.MotivoParaNoCambiar(personas, personas[0], 30));
        }

        [TestMethod]
        public void MotivoParaNoCambiar_PersonaInexistente()
        {
            Assert.IsNotNull(PoliticaPersonasContacto.MotivoParaNoCambiar(ElEden(), null, 30));
        }

        [TestMethod]
        public void TextoNivel_LosCuatroCasos()
        {
            Assert.AreEqual("Solo pide, sin precios", PoliticaPersonasContacto.TextoNivel(30));
            Assert.AreEqual("Ve precios, no descuentos", PoliticaPersonasContacto.TextoNivel(31));
            Assert.AreEqual("Ve todo y gestiona (facturas)", PoliticaPersonasContacto.TextoNivel(22));
            Assert.AreEqual("Ve precios y descuentos", PoliticaPersonasContacto.TextoNivel(11));
            Assert.AreEqual("Ve precios y descuentos", PoliticaPersonasContacto.TextoNivel(null));
        }
    }
}
