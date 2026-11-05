using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#819 (sugerencia 1049, Alfredo 05/11/26): recortar los textos del alta antes de validar y decir qué campo
    /// se pasa de largo en vez de «La solicitud no es válida».
    /// </summary>
    [TestClass]
    public class MensajesValidacionClienteTests
    {
        private static string Correo(int longitud)
        {
            const string dominio = "@nuevavision.es";
            return new string('a', longitud - dominio.Length) + dominio;
        }

        private static ClienteCrear ClienteConCorreo(string correo)
        {
            return new ClienteCrear
            {
                Nombre = "PELUQUERÍA PRUEBA",
                Telefono = "916230000",
                PersonasContacto = new List<PersonaContactoDTO>
                {
                    new PersonaContactoDTO { Numero = 1, Nombre = "Alfredo", CorreoElectronico = correo }
                }
            };
        }

        // Regresión del caso de Alfredo: 50 caracteres de correo + 15 espacios ya no rompen el alta.
        [TestMethod]
        public void Normalizar_CorreoDe50ConEspaciosAlFinal_PasaLaValidacion()
        {
            string correo = Correo(50);
            ClienteCrear cliente = ClienteConCorreo(correo + new string(' ', 15));
            Assert.IsNotNull(MensajesValidacionCliente.Desde(cliente), "Sin recortar no cabe");

            NormalizadorClienteCrear.Normalizar(cliente);

            Assert.AreEqual(correo, cliente.PersonasContacto.Single().CorreoElectronico);
            Assert.IsNull(MensajesValidacionCliente.Desde(cliente));
        }

        [TestMethod]
        public void Desde_CorreoDe63_DiceElCampoElLimiteYLaLongitud()
        {
            ClienteCrear cliente = ClienteConCorreo(Correo(63));
            NormalizadorClienteCrear.Normalizar(cliente);

            string mensaje = MensajesValidacionCliente.Desde(cliente);

            Assert.AreEqual("El correo electrónico de la persona de contacto no puede superar 50 caracteres (tiene 63).", mensaje);
        }

        [TestMethod]
        public void Desde_TelefonoDe35_DiceElCampoYElLimite()
        {
            ClienteCrear cliente = ClienteConCorreo(Correo(30));
            cliente.Telefono = new string('9', 35);

            Assert.AreEqual("El teléfono no puede superar 29 caracteres (tiene 35).", MensajesValidacionCliente.Desde(cliente));
        }

        [TestMethod]
        public void Desde_VariosCamposMal_TodosEnElMismoMensaje()
        {
            ClienteCrear cliente = ClienteConCorreo(Correo(63));
            cliente.Telefono = new string('9', 35);
            cliente.Direccion = new string('C', 51);
            cliente.Nombre = new string('N', 60);

            string mensaje = MensajesValidacionCliente.Desde(cliente);

            StringAssert.Contains(mensaje, "El teléfono no puede superar 29 caracteres (tiene 35).");
            StringAssert.Contains(mensaje, "La dirección no puede superar 50 caracteres (tiene 51).");
            StringAssert.Contains(mensaje, "El nombre fiscal no puede superar 50 caracteres (tiene 60).");
            StringAssert.Contains(mensaje, "El correo electrónico de la persona de contacto no puede superar 50 caracteres (tiene 63).");
            Assert.AreEqual(4, mensaje.Split('\n').Length);
            Assert.IsFalse(mensaje.Contains("La solicitud no es válida"));
        }

        [TestMethod]
        public void Desde_CorreoMalFormado_DiceCualNoEsValido()
        {
            ClienteCrear cliente = ClienteConCorreo("alfredo.nuevavision.es");

            Assert.AreEqual("El correo electrónico \"alfredo.nuevavision.es\" no es válido.", MensajesValidacionCliente.Desde(cliente));
        }

        [TestMethod]
        public void Desde_VariasPersonas_DiceDeCualEsElError()
        {
            ClienteCrear cliente = ClienteConCorreo(Correo(30));
            cliente.PersonasContacto.Add(new PersonaContactoDTO { Numero = 2, Nombre = "Laura", Telefono = new string('6', 30) });

            Assert.AreEqual("El teléfono de la persona de contacto «Laura» no puede superar 29 caracteres (tiene 30).",
                MensajesValidacionCliente.Desde(cliente));
        }

        [TestMethod]
        public void Normalizar_RecortaTodosLosTextosYDejaLosNull()
        {
            var cliente = new ClienteCrear
            {
                Nombre = "  NOMBRE  ",
                Direccion = " C/ Mayor 1 ",
                Poblacion = "Algete   ",
                Provincia = null,
                Comentarios = "  hola ",
                Nif = " 12345678Z ",
                PersonasContacto = new List<PersonaContactoDTO>
                {
                    new PersonaContactoDTO { Nombre = " Ana ", Telefono = " 600 ", CorreoElectronico = null },
                    null
                }
            };

            NormalizadorClienteCrear.Normalizar(cliente);

            Assert.AreEqual("NOMBRE", cliente.Nombre);
            Assert.AreEqual("C/ Mayor 1", cliente.Direccion);
            Assert.AreEqual("Algete", cliente.Poblacion);
            Assert.IsNull(cliente.Provincia);
            Assert.AreEqual("hola", cliente.Comentarios);
            Assert.AreEqual("12345678Z", cliente.Nif);
            PersonaContactoDTO persona = cliente.PersonasContacto.First();
            Assert.AreEqual("Ana", persona.Nombre);
            Assert.AreEqual("600", persona.Telefono);
            Assert.IsNull(persona.CorreoElectronico);
        }

        [TestMethod]
        public void Normalizar_Null_NoRevienta()
        {
            NormalizadorClienteCrear.Normalizar(null);
            NormalizadorClienteCrear.Normalizar(new ClienteCrear());
            Assert.AreEqual(MensajesValidacionCliente.SIN_DATOS, MensajesValidacionCliente.Desde((ClienteCrear)null));
            Assert.IsNull(MensajesValidacionCliente.Desde(new ClienteCrear()));
        }
    }
}
