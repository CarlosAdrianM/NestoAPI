using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#464: el número de empleados del centro se pregunta al meter el rapport y se guarda
    /// en la ficha del cliente principal. Reglas puras.
    /// </summary>
    [TestClass]
    public class PoliticaEmpleadosClienteTests
    {
        [TestMethod]
        public void PreguntarEmpleados_SoloEnLaComunidadDeMadrid()
        {
            Assert.IsTrue(PoliticaEmpleadosCliente.PreguntarEmpleados("28100"));
            Assert.IsTrue(PoliticaEmpleadosCliente.PreguntarEmpleados(" 28001"), "el char de la BD viene con relleno");
            Assert.IsFalse(PoliticaEmpleadosCliente.PreguntarEmpleados("08001"), "Barcelona");
            Assert.IsFalse(PoliticaEmpleadosCliente.PreguntarEmpleados("45280"), "Toledo: el 28 no vale en medio");
            Assert.IsFalse(PoliticaEmpleadosCliente.PreguntarEmpleados(null));
            Assert.IsFalse(PoliticaEmpleadosCliente.PreguntarEmpleados("   "));
        }

        [TestMethod]
        public void ClienteDTO_PreguntarEmpleadosSaleDelCodigoPostal()
        {
            // Es lo que leen Nesto y NestoApp: la regla no viaja, solo el resultado.
            Assert.IsTrue(new ClienteDTO { codigoPostal = "28850" }.preguntarEmpleados);
            Assert.IsFalse(new ClienteDTO { codigoPostal = "19001" }.preguntarEmpleados);
            Assert.IsFalse(new ClienteDTO().preguntarEmpleados);
        }

        [TestMethod]
        public void MotivoParaNoGuardar_AceptaDe0A5YNull_RechazaElResto()
        {
            Assert.IsNull(PoliticaEmpleadosCliente.MotivoParaNoGuardar(null), "null = no se ha preguntado");
            Assert.IsNull(PoliticaEmpleadosCliente.MotivoParaNoGuardar(0), "sin empleados");
            Assert.IsNull(PoliticaEmpleadosCliente.MotivoParaNoGuardar(5), "5 o más");
            Assert.IsNotNull(PoliticaEmpleadosCliente.MotivoParaNoGuardar(6));
            Assert.IsNotNull(PoliticaEmpleadosCliente.MotivoParaNoGuardar(200));
        }

        [TestMethod]
        public void AplicarAlCliente_ConNull_NoTocaLaFicha()
        {
            Cliente ficha = new Cliente { Empleados = 3, EmpleadosFecha = new DateTime(2026, 1, 1), Usuario = "antes" };

            bool cambiado = PoliticaEmpleadosCliente.AplicarAlCliente(ficha, null, new DateTime(2026, 9, 9), "Laura");

            Assert.IsFalse(cambiado);
            Assert.AreEqual((byte)3, ficha.Empleados);
            Assert.AreEqual(new DateTime(2026, 1, 1), ficha.EmpleadosFecha);
            Assert.AreEqual("antes", ficha.Usuario);
        }

        [TestMethod]
        public void AplicarAlCliente_PrimeraVez_GuardaValorFechaYUsuario()
        {
            Cliente ficha = new Cliente();

            bool cambiado = PoliticaEmpleadosCliente.AplicarAlCliente(ficha, 2, new DateTime(2026, 9, 9, 10, 0, 0), "Laura");

            Assert.IsTrue(cambiado);
            Assert.AreEqual((byte)2, ficha.Empleados);
            Assert.AreEqual(new DateTime(2026, 9, 9, 10, 0, 0), ficha.EmpleadosFecha);
            Assert.AreEqual("Laura", ficha.Usuario);
        }

        [TestMethod]
        public void AplicarAlCliente_MismoValor_RefrescaSoloLaFecha()
        {
            // Confirmar que sigue igual también es información: el dato deja de estar viejo.
            Cliente ficha = new Cliente { Empleados = 2, EmpleadosFecha = new DateTime(2025, 1, 1) };

            bool cambiado = PoliticaEmpleadosCliente.AplicarAlCliente(ficha, 2, new DateTime(2026, 9, 9), "Laura");

            Assert.IsTrue(cambiado);
            Assert.AreEqual((byte)2, ficha.Empleados);
            Assert.AreEqual(new DateTime(2026, 9, 9), ficha.EmpleadosFecha);
        }

        [TestMethod]
        public void AplicarAlCliente_ValorDistinto_CambiaValorYFecha()
        {
            Cliente ficha = new Cliente { Empleados = 0, EmpleadosFecha = new DateTime(2025, 1, 1) };

            _ = PoliticaEmpleadosCliente.AplicarAlCliente(ficha, 5, new DateTime(2026, 9, 9), "Laura");

            Assert.AreEqual((byte)5, ficha.Empleados);
            Assert.AreEqual(new DateTime(2026, 9, 9), ficha.EmpleadosFecha);
        }

        [TestMethod]
        public void AplicarAlCliente_SinFichaPrincipal_NoRevienta()
        {
            Assert.IsFalse(PoliticaEmpleadosCliente.AplicarAlCliente(null, 3, DateTime.Now, "Laura"));
        }
    }
}
