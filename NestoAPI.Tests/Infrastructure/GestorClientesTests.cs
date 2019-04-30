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
        IServicioAgencias servicioAgencia;
        public GestorClientesTests()
        {
            servicioAgencia = A.Fake<IServicioAgencias>();
            var respuestaAgencia = new RespuestaAgencia
            {
                DireccionFormateada = "Calle de la Reina, 5, 28110, Madrid"
            };
            A.CallTo(() => servicioAgencia.LeerDireccionGoogleMaps(A<string>.Ignored)).Returns(respuestaAgencia);
        }


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
            GestorClientes gestor = new GestorClientes(servicio, null);

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
            A.CallTo(() => servicio.ComprobarNifNombre("12345678A", "Cliente Nuevo")).Returns(respuestaAEAT);
            GestorClientes gestor = new GestorClientes(servicio, null);

            RespuestaNifNombreCliente respuesta = gestor.ComprobarNifNombre("12345678-A", "Cliente Nuevo").Result;

            Assert.IsFalse(respuesta.ExisteElCliente);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, respuesta.EstadoCliente);
            Assert.AreEqual("NOMBRE CLIENTE AEAT", respuesta.NombreFormateado);
            Assert.AreEqual("12345678A", respuesta.NifFormateado);
            Assert.IsTrue(respuesta.NifValidado);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task GestorClientes_ComprobarDatosGenerales_SiLaDireccionEstaVaciaDaError()
        {
            GestorClientes gestor = new GestorClientes();

            _ = await gestor.ComprobarDatosGenerales("", "28004", "915311923");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task GestorClientes_ComprobarDatosGenerales_SiElCodigoPostalEstaVacioDaError()
        {
            GestorClientes gestor = new GestorClientes();

            _ = await gestor.ComprobarDatosGenerales("RUE DEL PERCEBE, 13", "", "915311923");
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_CogeLosVendedoresDelCodigoPostal()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes
            {
                Poblacion = "Algete",
                Provincia = "Madrid",
                VendedorEstetica = "David",
                VendedorPeluqueria = "Israel"
            };
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales("C/ REINA, 5", "28110", "915311923");

            Assert.AreEqual("Algete", respuesta.Poblacion);
            Assert.AreEqual("Madrid", respuesta.Provincia);
            Assert.AreEqual("David", respuesta.VendedorEstetica);
            Assert.AreEqual("Israel", respuesta.VendedorPeluqueria);
            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
            Assert.AreEqual("915311923", respuesta.TelefonoFormateado);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_LaDireccionVaEnMayusculas()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C/ reina, 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiNoPoneCalleAbreviadoLoCorregimos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" CALLE reina, 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiPoneCalleDeLaLoAbreviamos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" CALLE de la reina, 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiElEspacioDeLaCalleEstaMalLoCorregimos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C / reina, 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiNoHayEspacioEntreLaBarraYLaCalleLoPonemos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina, 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiNoHayEspacioEntreLaComaYElNumeroLoPonemos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina,5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiNoHayComaAntesDelNumeroLaPonemos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiHayDatosDespuesDelNumeroLosPonemos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "915311923");

            Assert.AreEqual("C/ REINA, 5 - 1º2", respuesta.DireccionFormateada);
        }

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SiNoHayComaBuscamosElPrimerNumero()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "alameda, 18 - 1º2", 
                "Avenida de la alameda 18 local 8, 28140 Fuente el Saz de Jarama, Madrid, Spain",
                "28140");

            Assert.AreEqual("Av. ALAMEDA, 18 - 1º2", respuesta);
        }

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SiHayEspacioDetrasDelSimboloDePrimeroLoQuitamos()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "C/ DE LA FLORIDA, 18 - PORTAL C, 2º 1",
                "C/ DE LA FLORIDA, 18, 28140",
                "28140");

            Assert.AreEqual("C/ FLORIDA, 18 - PORTAL C, 2º1", respuesta);
        }
        
        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SiPoneSBarraNLoTratamosComoSinNumero()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "PL. ARATOCA, S/N",
                "Plaza Aratoca, 28033 Madrid, Spain",
                "28033");

            Assert.AreEqual("Pl. ARATOCA, S/N", respuesta);
        }
    }
}
