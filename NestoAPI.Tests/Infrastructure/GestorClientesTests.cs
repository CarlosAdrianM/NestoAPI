using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorClientesTests
    {
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task GestorClientes_ComprobarNifNombre_SiElNombreEstaVacioDaError()
        {
            GestorClientes gestor = new GestorClientes();

            _ = await gestor.ComprobarNifNombre("", "");
        }

        [TestMethod]
        public void GestorClientes_ComprobarNifNombre_SiElNifEstaVacioElEstadoEsCinco()
        {
            GestorClientes gestor = new GestorClientes();

            RespuestaNifNombreCliente respuesta = gestor.ComprobarNifNombre("", "Cliente Nuevo").Result;

            Assert.AreEqual(Constantes.Clientes.Estados.PRIMERA_VISITA, respuesta.EstadoCliente);
        }

        [TestMethod]
        public void GestorClientes_ComprobarNifNombre_SiElClienteExisteDevuelveLosDatos()
        {            
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            ClienteDTO cliente = new ClienteDTO
            {
                cliente = "1234",
                nombre = "CLIENTE 1234",
                cifNif = "12345678A"
            };
            A.CallTo(() => servicio.BuscarClientePorNif("12345678A")).Returns(cliente);
            GestorClientes gestor = new GestorClientes(servicio);

            RespuestaNifNombreCliente respuesta = gestor.ComprobarNifNombre("12345678A", "Cliente Nuevo").Result;

            Assert.IsTrue(respuesta.ExisteElCliente);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, respuesta.EstadoCliente);
            Assert.AreEqual("CLIENTE 1234", respuesta.NombreFormateado);
            Assert.AreEqual("12345678A", respuesta.NifFormateado);
            Assert.IsTrue(respuesta.NifValidado); // si ya existe lo damos por bueno
        }

        [TestMethod]
        public void GestorClientes_ComprobarNifNombre_SiElClienteNoExisteDevuelveLosDatosComprobados()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            ClienteDTO cliente = null;
            A.CallTo(() => servicio.BuscarClientePorNif("12345678A")).Returns(cliente);
            RespuestaNifNombreCliente respuestaAEAT = new RespuestaNifNombreCliente
            {
                NombreFormateado = "NOMBRE CLIENTE AEAT",
                NifFormateado = "12345678A",
                NifValidado = true
            };
            A.CallTo(() => servicio.ComprobarNifNombre("12345678-A", "Cliente Nuevo")).Returns(respuestaAEAT);
            GestorClientes gestor = new GestorClientes(servicio);

            RespuestaNifNombreCliente respuesta = gestor.ComprobarNifNombre("12345678-A", "Cliente Nuevo").Result;

            Assert.IsFalse(respuesta.ExisteElCliente);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, respuesta.EstadoCliente);
            Assert.AreEqual("NOMBRE CLIENTE AEAT", respuesta.NombreFormateado);
            Assert.AreEqual("12345678A", respuesta.NifFormateado);
            Assert.IsTrue(respuesta.NifValidado);
        }
    }
}
