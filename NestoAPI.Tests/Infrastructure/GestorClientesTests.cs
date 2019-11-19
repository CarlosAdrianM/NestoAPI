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
using static NestoAPI.Models.Clientes.RespuestaDatosGeneralesClientes;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorClientesTests
    {
        IServicioAgencias servicioAgencia;
        NVEntities db;
        public GestorClientesTests()
        {
            servicioAgencia = A.Fake<IServicioAgencias>();
            db = A.Fake<NVEntities>();

            var respuestaAgencia = new RespuestaAgencia
            {
                DireccionFormateada = "Calle de la Reina, 5, 28110, Madrid"
            };
            A.CallTo(() => servicioAgencia.LeerDireccionGoogleMaps(A<string>.Ignored, A<string>.Ignored)).Returns(respuestaAgencia);
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
        public async Task GestorClientes_ComprobarDatosGenerales_SiHayOtroClienteConElMismoTelefonoLoDevuelve()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            ClienteTelefonoLookup clienteFake = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "12345",
                Contacto = "0",
                Nombre = "Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("915311923")).Returns(new List<ClienteTelefonoLookup> { clienteFake });
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "915311923");

            Assert.AreEqual(1, respuesta.ClientesMismoTelefono?.Count);
            Assert.AreEqual(clienteFake, respuesta.ClientesMismoTelefono.First());
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiHayUnClienteConAlgunTelefonoIgualLoDevuelve()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            ClienteTelefonoLookup clienteFake = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "12345",
                Contacto = "0",
                Nombre = "Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("915311923")).Returns(new List<ClienteTelefonoLookup> { clienteFake });
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "916281914/915311923");

            Assert.AreEqual(1, respuesta.ClientesMismoTelefono?.Count);
            Assert.AreEqual(clienteFake, respuesta.ClientesMismoTelefono.First());
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiHayMasDeUnClienteConAlgunTelefonoIgualLosDevuelveTodos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            ClienteTelefonoLookup clienteFake = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "12345",
                Contacto = "0",
                Nombre = "Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("915311923")).Returns(new List<ClienteTelefonoLookup> { clienteFake });
            ClienteTelefonoLookup clienteFake2 = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "54321",
                Contacto = "0",
                Nombre = "Otra Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("916281914")).Returns(new List<ClienteTelefonoLookup> { clienteFake2 });

            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "916281914/915311923");

            Assert.AreEqual(2, respuesta.ClientesMismoTelefono?.Count);
            Assert.AreEqual(clienteFake2, respuesta.ClientesMismoTelefono.First());
            Assert.AreEqual(clienteFake, respuesta.ClientesMismoTelefono.Last());

        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiHayUnClienteRepetidoConAlgunTelefonoIgualLoDevuelveUnaVez()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            ClienteTelefonoLookup clienteFake = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "12345",
                Contacto = "0",
                Nombre = "Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("915311923")).Returns(new List<ClienteTelefonoLookup> { clienteFake });
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "915311923/915311923");

            Assert.AreEqual(1, respuesta.ClientesMismoTelefono?.Count);
            Assert.AreEqual(clienteFake, respuesta.ClientesMismoTelefono.First());
        }

        [TestMethod]
        public async Task GestorClientes_ComprobarDatosGenerales_SiElTelefonoNoEstaBienFormateadoLoDevuelveIgualmente()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            var respuestaFake = new RespuestaDatosGeneralesClientes();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("28110")).Returns(respuestaFake);
            ClienteTelefonoLookup clienteFake = new ClienteTelefonoLookup
            {
                Empresa = "1",
                Cliente = "12345",
                Contacto = "0",
                Nombre = "Prueba"
            };
            A.CallTo(() => servicio.ClientesMismoTelefono("915311923")).Returns(new List<ClienteTelefonoLookup> { clienteFake });
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);

            var respuesta = await gestor.ComprobarDatosGenerales(" C /reina 5 - 1º2", "28110", "(91)628.1914915(31) 19-23");

            Assert.AreEqual(1, respuesta.ClientesMismoTelefono?.Count);
            Assert.AreEqual(clienteFake, respuesta.ClientesMismoTelefono.First());
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

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SustituyeAbreviaturaEnMitad()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "alameda, 18 - Urbanización Nuestra Señora del Pilar",
                "Avenida de la alameda 18 local 8, 28140 Fuente el Saz de Jarama, Madrid, Spain",
                "28140");

            Assert.AreEqual("Av. ALAMEDA, 18 - Urb.Ntra.Sra.DEL PILAR", respuesta);
        }

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SustituyeAbreviaturaAlFinal()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "alameda, 18 - duplicado",
                "Avenida de la alameda 18 local 8, 28140 Fuente el Saz de Jarama, Madrid, Spain",
                "28140");

            Assert.AreEqual("Av. ALAMEDA, 18 - dup.", respuesta);
        }

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_QuitaElDelAntesDeLaCalle()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "capitan fco. sanchez 1",
                "Calle del Capitán Francisco Sánchez, 1, 28100 Alcobendas, Madrid, España",
                "28100");

            Assert.AreEqual("C/ CAPITÁN FRANCISCO SÁNCHEZ, 1", respuesta);
        }

        //"15969 BRETAL, LA CORUÑA, ESPAÑA"

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SiGoogleDevuelveElCodigoPostalLoPonemosDeDireccion()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "C/ BRETAL, 195",
                "15969 BRETAL, LA CORUÑA, ESPAÑA",
                "15969");

            Assert.AreEqual("C/ BRETAL, 195", respuesta);
        }

        [TestMethod]
        public void GestorClientes_LimpiarDireccion_SiNoHayEspacioLoPonemos()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarDireccion(
                "reina5",
                "Calle de la Reina, 28004 Madrid, España",
                "28004");

            Assert.AreEqual("C/ REINA, 5", respuesta);
        }

        [TestMethod]
        public void GestorClientes_LimpiarTelefono_SeQuitanEspaciosEnBlanco()
        {
            GestorClientes gestor = new GestorClientes();

            string respuesta = gestor.LimpiarTelefono("925 337 754    618538006 AA+///12345-678. 9");

            Assert.AreEqual("925337754/618538006/123456789", respuesta);
        }

        [TestMethod]
        public void GestorClientes_ConstruirClienteCrear_LosDatosBasicosCoincidenConElCliente()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            Cliente clienteDevuelto = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "1234    ",
                Contacto = "  0",
                CodPostal = "28001",
                Dirección = "Rue del percebe, 13      ",
                Estado = 54,
                CIF_NIF = "A78368255",
                Nombre = "ACME, S.A.      ",
                Población = "ALGETE    ",
                Provincia = "MADRID    ",
                Ruta = "AT ",
                Teléfono = "915311923/916281914     ",
                Vendedor = "CA ",
                ClientePrincipal = false
            };
            A.CallTo(() => servicio.BuscarCliente("1", "1234", "0")).Returns(clienteDevuelto);
            
            var clienteCrear = gestor.ConstruirClienteCrear("1", "1234", "0").Result;

            Assert.AreEqual(clienteCrear.Empresa, "1");
            Assert.AreEqual(clienteCrear.Cliente, "1234");
            Assert.AreEqual(clienteCrear.Contacto, "0");
            Assert.AreEqual(clienteCrear.CodigoPostal, "28001");
            Assert.AreEqual(clienteCrear.Direccion, "Rue del percebe, 13");
            Assert.AreEqual(clienteCrear.Estado, (short)54);
            Assert.AreEqual(clienteCrear.Nif, "A78368255");
            Assert.AreEqual(clienteCrear.Nombre, "ACME, S.A.");
            Assert.AreEqual(clienteCrear.Poblacion, "ALGETE");
            Assert.AreEqual(clienteCrear.Provincia, "MADRID");
            Assert.AreEqual(clienteCrear.Ruta, "AT");
            Assert.AreEqual(clienteCrear.Telefono, "915311923/916281914");
            Assert.AreEqual(clienteCrear.VendedorEstetica, "CA");
            Assert.AreEqual(clienteCrear.EsContacto, true);
        }

        [TestMethod]
        public void GestorClientes_ConstruirClienteCrear_LosDatosDeLosVendedoresCuadran()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            Cliente clienteDevuelto = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "1234    ",
                Contacto = "  0",
                Vendedor = "CA "
            };
            A.CallTo(() => servicio.BuscarCliente("1", "1234", "0")).Returns(clienteDevuelto);
            VendedorClienteGrupoProducto vendedorGrupo = new VendedorClienteGrupoProducto
            {
                Empresa = "1",
                Cliente = "1234",
                Contacto = "0",
                GrupoProducto = "PEL",
                Vendedor="NV"
            };
            A.CallTo(() => servicio.BuscarVendedorGrupo("1", "1234", "0", "PEL")).Returns(vendedorGrupo);

            var clienteCrear = gestor.ConstruirClienteCrear("1", "1234", "0").Result;

            Assert.AreEqual("CA", clienteCrear.VendedorEstetica);
            Assert.AreEqual("NV", clienteCrear.VendedorPeluqueria);
            Assert.IsTrue(clienteCrear.Estetica);
            Assert.IsFalse(clienteCrear.Peluqueria);
        }

        [TestMethod]
        public void GestorClientes_ConstruirClienteCrear_LosDatosDePagoCuadran()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            Cliente clienteDevuelto = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "1234    ",
                Contacto = "  0",
                CCC = "1  "
            };
            A.CallTo(() => servicio.BuscarCliente("1", "1234", "0")).Returns(clienteDevuelto);

            CondPagoCliente condPagoCliente = new CondPagoCliente
            {
                Empresa = "1",
                Nº_Cliente = "1234",
                Contacto = "0",
                FormaPago = "RCB   ",
                PlazosPago = "1/30   "
            };
            A.CallTo(() => servicio.BuscarCondicionesPago("1", "1234", "0")).Returns(condPagoCliente);

            var clienteCrear = gestor.ConstruirClienteCrear("1", "1234", "0").Result;

            Assert.AreEqual("RCB", clienteCrear.FormaPago);
            Assert.AreEqual("1/30", clienteCrear.PlazosPago);
        }

        [TestMethod]
        public void GestorClientes_ConstruirClienteCrear_ElIbanCuadra()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            Cliente clienteDevuelto = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "1234    ",
                Contacto = "  0",
                CCC = "1  "
            };
            A.CallTo(() => servicio.BuscarCliente("1", "1234", "0")).Returns(clienteDevuelto);

            CCC cccCliente = new CCC
            {
                Empresa = "1",
                Cliente = "1234",
                Contacto = "0",
                Número = "1  ",
                Pais = "ES",
                DC_IBAN = "12",
                Entidad = "3456",
                Oficina = "7890",
                DC = "**",
                Nº_Cuenta = "0123456789"
            };
            A.CallTo(() => servicio.BuscarCCC("1", "1234", "0", "1  ")).Returns(cccCliente);

            var clienteCrear = gestor.ConstruirClienteCrear("1", "1234", "0").Result;

            Assert.AreEqual("ES1234567890**0123456789", clienteCrear.Iban);
        }

        [TestMethod]
        public void GestorClientes_ConstruirClienteCrear_LasPersonasDeContactoSeCrean()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            Cliente clienteDevuelto = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "1234    ",
                Contacto = "  0",
                CCC = "1  "
            };
            A.CallTo(() => servicio.BuscarCliente("1", "1234", "0")).Returns(clienteDevuelto);

            List<PersonaContactoCliente> personas = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente
                {
                    Nombre = "Carlos     ",
                    CorreoElectrónico = "carlos@yo.com     "
                },
                new PersonaContactoCliente
                {
                    Nombre = "Adrián          ",
                    CorreoElectrónico = "adrian@yo.com         "
                }
            };
            A.CallTo(() => servicio.BuscarPersonasContacto("1", "1234", "0")).Returns(personas);

            var clienteCrear = gestor.ConstruirClienteCrear("1", "1234", "0").Result;

            Assert.AreEqual("Carlos", clienteCrear.PersonasContacto.First().Nombre);
            Assert.AreEqual("carlos@yo.com", clienteCrear.PersonasContacto.First().CorreoElectronico);
            Assert.AreEqual("Adrián", clienteCrear.PersonasContacto.Last().Nombre);
            Assert.AreEqual("adrian@yo.com", clienteCrear.PersonasContacto.Last().CorreoElectronico);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteModificar_SiHayUnaPersonaDeContactoNuevaSeCrea() 
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ICollection<PersonaContactoDTO> personasContactoNuevas = new List<PersonaContactoDTO>
            {
                new PersonaContactoDTO
                {
                    Numero = 2,
                    Nombre = "Carlos",
                    CorreoElectronico = ""
                },
                new PersonaContactoDTO
                {
                    Nombre = "Adrián",
                    CorreoElectronico = ""
                }
            };
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.PersonasContacto = personasContactoNuevas;
            Cliente clienteExistente = A.Fake<Cliente>();
            clienteExistente.Nombre = "Cliente existente";
            clienteExistente.PersonasContactoClientes = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente
                {
                    Número = "2  ",
                    Nombre = "Carlos",
                    CorreoElectrónico = ""
                }
            };
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clienteExistente);
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;

            Assert.AreEqual(2, clienteNuevo.PersonasContactoClientes.Count);
        }


        [TestMethod]
        public void GestorClientes_PrepararClienteModificar_SiCambiaUnaPersonaDeContactoSeModificaEnElCliente()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ICollection<PersonaContactoDTO> personasContactoNuevas = new List<PersonaContactoDTO>
            {
                new PersonaContactoDTO
                {
                    Numero = 2,
                    Nombre = "Carlos",
                    CorreoElectronico = "carlosadrian@nuevavision.es"
                },
                new PersonaContactoDTO
                {
                    Numero = 3,
                    Nombre = "Adrián",
                    CorreoElectronico = ""
                }
            };
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.PersonasContacto = personasContactoNuevas;
            Cliente clienteExistente = A.Fake<Cliente>();
            clienteExistente.Nombre = "Cliente existente";
            clienteExistente.PersonasContactoClientes = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente
                {
                    Número = "2  ",
                    Nombre = "Carlos",
                    CorreoElectrónico = ""
                },
                new PersonaContactoCliente
                {
                    Número = "3  ",
                    Nombre = "Martínez",
                    CorreoElectrónico = ""
                },
            };
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clienteExistente);
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;

            Assert.AreEqual(2, clienteNuevo.PersonasContactoClientes.Count);
            Assert.AreEqual("Carlos", clienteNuevo.PersonasContactoClientes.Single(p=>p.Número.Trim() == "2").Nombre);
            Assert.AreEqual("carlosadrian@nuevavision.es", clienteNuevo.PersonasContactoClientes.Single(p => p.Número.Trim() == "2").CorreoElectrónico);
            Assert.AreEqual("Adrián", clienteNuevo.PersonasContactoClientes.Single(p => p.Número.Trim() == "3").Nombre);
            Assert.AreEqual("", clienteNuevo.PersonasContactoClientes.Single(p => p.Número.Trim() == "3").CorreoElectrónico);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteModificar_SiSeBorraUnaPersonaDeContactoSeQuitaDelCliente()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ICollection<PersonaContactoDTO> personasContactoNuevas = new List<PersonaContactoDTO>
            {
                new PersonaContactoDTO
                {
                    Numero = 2,
                    Nombre = "Carlos",
                    CorreoElectronico = "carlosadrian@nuevavision.es"
                }
            };
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.PersonasContacto = personasContactoNuevas;
            Cliente clienteExistente = A.Fake<Cliente>();
            clienteExistente.Nombre = "Cliente existente";
            clienteExistente.PersonasContactoClientes = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente
                {
                    Número = "2  ",
                    Nombre = "Carlos",
                    CorreoElectrónico = ""
                },
                new PersonaContactoCliente
                {
                    Número = "3  ",
                    Nombre = "Martínez",
                    CorreoElectrónico = ""
                },
            };
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clienteExistente);
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;

            Assert.AreEqual(1, clienteNuevo.PersonasContactoClientes.Count);
            Assert.AreEqual("Carlos", clienteNuevo.PersonasContactoClientes.Single(p => p.Número.Trim() == "2").Nombre);
            Assert.AreEqual("carlosadrian@nuevavision.es", clienteNuevo.PersonasContactoClientes.Single(p => p.Número.Trim() == "2").CorreoElectrónico);
        }


        [TestMethod]
        public void GestorClientes_PrepararClienteModificar_SiElClienteEsEstado5PeroTieneNifSePasaAEstado0()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.Nif = "1234A";
            Cliente clienteExistente = A.Fake<Cliente>();
            clienteExistente.Nombre = "Cliente existente";
            clienteExistente.Estado = Constantes.Clientes.Estados.PRIMERA_VISITA;
            clienteExistente.CIF_NIF = "1234A"; // no debería ser estado 5, pero en la práctica ocurre
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clienteExistente);
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;

            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, clienteNuevo.Estado);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteModificar_SiLaFormaDePagoEsEfectivoNoLeeElCCC()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.FormaPago = "EFC";
            clienteCrear.Iban = "NULL XXXX 7890 1234 5678 9012";
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;

            // no lanza error como en el test anterior
            Assert.AreEqual(0, clienteNuevo.CCCs.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(AggregateException),
        "El IBAN no se puede modificar. Debe hacerlo administración cuando tenga el mandato firmado en su poder.")]
        public void GestorClientes_PrepararClienteModificar_SiLaFormaDePagoEsReciboDaError()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.FormaPago = "RCB";
            clienteCrear.Iban = "NULL XXXX 7890 1234 5678 9012";
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteModificar(clienteCrear, db).Result;
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteCrear_SiNoTieneEsteticaYHayVendedorDePeluqueriaCreaElVendedorDePeluqueria()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.VendedorEstetica = "JE";
            clienteCrear.VendedorPeluqueria = "AH";
            clienteCrear.Estetica = false;
            clienteCrear.Peluqueria = true;
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteCrear(clienteCrear, db).Result;

            Assert.AreEqual("NV", clienteNuevo.Vendedor);
            Assert.AreEqual(1, clienteNuevo.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("AH", clienteNuevo.VendedoresClienteGrupoProductoes.First().Vendedor);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteCrear_SiNoTieneEsteticaYNoHayVendedorDePeluqueriaCreaElVendedorDePeluqueria()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.VendedorEstetica = "JE";
            clienteCrear.VendedorPeluqueria = null;
            clienteCrear.Estetica = false;
            clienteCrear.Peluqueria = true;
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteCrear(clienteCrear, db).Result;

            Assert.AreEqual("NV", clienteNuevo.Vendedor);
            Assert.AreEqual(1, clienteNuevo.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("JE", clienteNuevo.VendedoresClienteGrupoProductoes.First().Vendedor);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteCrear_SiLaFormaDePagoEsEfectivoNoCreaCCC()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.FormaPago = "EFC";
            clienteCrear.Iban = "NULL XXXX 7890 1234 5678 9012";
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteCrear(clienteCrear, db).Result;

            Assert.AreEqual("EFC", clienteNuevo.CondPagoClientes.First().FormaPago);
            Assert.AreEqual(0, clienteNuevo.CCCs.Count);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteCrear_SiLaFormaDePagoEsReciboSiCreaCCC()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.FormaPago = "RCB";
            clienteCrear.Iban = "XX12 3456 7890 1234 5678 9012";
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteCrear(clienteCrear, db).Result;

            Assert.AreEqual("RCB", clienteNuevo.CondPagoClientes.First().FormaPago);
            Assert.AreEqual(1, clienteNuevo.CCCs.Count);
        }

        [TestMethod]
        public void GestorClientes_PrepararClienteCrear_SiElNifEsCadenaVaciaLoPonemosNulo()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            GestorClientes gestor = new GestorClientes(servicio, servicioAgencia);
            ClienteCrear clienteCrear = A.Fake<ClienteCrear>();
            clienteCrear.Nif = "";
            NVEntities db = A.Fake<NVEntities>();

            Cliente clienteNuevo = gestor.PrepararClienteCrear(clienteCrear, db).Result;

            Assert.IsNull(clienteNuevo.CIF_NIF);
        }

        // SiHayGrupo -> se lo quita el de peluquería
        // SiNoHayGrupo -> se lo quita el de estética

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_DevuelveElVendedorYEstadoCambiados()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime());
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(
                new List<string>
                {
                    "YO"
                });
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL,
                Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                Usuario = "cestmoi"
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("YO", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiNoHayGrupoYNoTieneVendedorPeluqueriaAsignoElNuevoAAmbos()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "EL",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "NV"
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("YO", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }
        
        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoCambiaSoloElVendedorDelGrupoYNoElEstado()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string>{"YO","TU"});
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "nosotros",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "vosotros"
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("nosotros", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYTieneVendedorPresencialSeAsignaAlGeneral()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "EL",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "TU"
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("EL", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual(Constantes.Vendedores.VENDEDOR_GENERAL, clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }
        
        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYNoTieneVendedorPresencialSeAsignaTambienEnEstetica()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "NV",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "EL"
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("YO", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }
        
        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYNoTieneVendedorPresencialSeAsignaLaPeluqueriaAlVendedorActual()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "TU",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "EL"
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("TU", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("TU", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }
        
        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYAlgunContactoYaTieneVendedorTelefonicoSeLeAsignaAEseVendedor()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            //A.CallTo(() => servicio.VendedoresContactosCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(new List<string> { "TU", "NOSOTROS" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "NV",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto> {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = Constantes.Productos.GRUPO_PELUQUERIA,
                            Vendedor = "EL"
                        }
                    }
                }
            );
            A.CallTo(() => servicio.BuscarContactos(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new List<Cliente> {
                    new Cliente
                    {
                        Vendedor = "TU",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "fuiyo"
                            }
                        }
                    },
                    new Cliente
                    {
                        Vendedor = "NOSOTROS", 
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fuistetu",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "eraella"
                            }
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            Cliente clienteDB = gestor.DejarDeVisitar(db, cliente).Result[0];

            Assert.AreEqual("TU", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("TU", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiNoHayGrupoYTieneContactosEnEstado7LosCambiamosTambienDeVendedor()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            //A.CallTo(() => servicio.VendedoresContactosCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(new List<string> { "TU", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "EL",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>()
                }
            );
            A.CallTo(() => servicio.BuscarContactos(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new List<Cliente> {
                    new Cliente
                    {
                        Vendedor = "TU",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>()
                    },
                    new Cliente
                    {
                        Vendedor = "ELLA",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>()
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL
            };

            
            List<Cliente> clientesDB = gestor.DejarDeVisitar(db, cliente).Result;
            Cliente clienteDB = clientesDB[1];
            Cliente contactoDB = clientesDB[0];
            

            Assert.AreEqual("TU", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);

            Assert.AreEqual("TU", contactoDB.Vendedor);
            Assert.AreEqual("cestmoi", contactoDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contactoDB.Estado);

            // el contacto con vendedor "ELLA" no lo tocamos, porque es de otro vendedor presencial
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYTieneContactosConVendedorPresencialDeEsteticaPonemosVendedorGeneralDePeluqueria()
        {
            /*
             * AL quitar un vendedor de peluquería miramos todos los contactos. Si uno solo de ellos tiene vendedor presencial de estética,
             * ponemos todos a NV.
             *
             * Y un tercer test donde si está mezclado (pero no hay ninguno presencial de estética), asignamos el de peluquería al mismo de estética
             * */
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            //A.CallTo(() => servicio.VendedoresContactosCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(new List<string> { "NV", "EL" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "NV",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                    {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = "PEL",
                            Vendedor = "ELLA",
                            Usuario = "eraella"
                        }
                    }
                }
            );
            A.CallTo(() => servicio.BuscarContactos(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new List<Cliente> {
                    new Cliente
                    {
                        Vendedor = "NV",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "fuiyo"
                            }
                        }
                    },
                    new Cliente
                    {
                        Vendedor = "EL", // <-- esta es la clave, vendedor presencial
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fuistetu",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "eraella"
                            }
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };


            List<Cliente> clientesDB = gestor.DejarDeVisitar(db, cliente).Result;
            Cliente clienteDB = clientesDB[2];
            Cliente contactoDB = clientesDB[0];
            Cliente contacto2DB = clientesDB[1];


            Assert.AreEqual("NV", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_PRESENCIAL, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("NV", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("NV", contactoDB.Vendedor);
            Assert.AreEqual("cestmoi", contactoDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contactoDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("NV", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("EL", contacto2DB.Vendedor);
            Assert.AreEqual("cestmoi", contacto2DB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contacto2DB.Estado);
            Assert.AreEqual(1, contacto2DB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("NV", contacto2DB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYTieneContactosPeroTodosSonDeNVPonemosElNuevoVendedorParaPeluqueria()
        {
            /*
             * Al quitar un vendedor de peluquería miramos todos los contactos. Si todos son NV, los ponemos al nuevo vendedor
             * */
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            //A.CallTo(() => servicio.VendedoresContactosCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(new List<string> { "NV", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "NV",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                    {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = "PEL",
                            Vendedor = "ELLA",
                            Usuario = "eraella"
                        }
                    }
                }
            );
            A.CallTo(() => servicio.BuscarContactos(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new List<Cliente> {
                    new Cliente
                    {
                        Vendedor = "NV",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "fuiyo"
                            }
                        }
                    },
                    new Cliente
                    {
                        Vendedor = "NV", 
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fuistetu",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "ELLA",
                                Usuario = "eraella"
                            }
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };


            List<Cliente> clientesDB = gestor.DejarDeVisitar(db, cliente).Result;
            Cliente clienteDB = clientesDB[2];
            Cliente contactoDB = clientesDB[0];
            Cliente contacto2DB = clientesDB[1];


            Assert.AreEqual("YO", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("YO", contactoDB.Vendedor);
            Assert.AreEqual("cestmoi", contactoDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contactoDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("YO", contacto2DB.Vendedor);
            Assert.AreEqual("cestmoi", contacto2DB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contacto2DB.Estado);
            Assert.AreEqual(1, contacto2DB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("YO", contacto2DB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }

        [TestMethod]
        public void GestorClientes_DejarDeVisitar_SiHayGrupoYTieneContactosConVendedorTelefonicoEnEsteticaPonemosDeVendedorAlMismoTelefonico()
        {
            /*
             * Y un tercer test donde si está mezclado (pero no hay ninguno presencial de estética), asignamos el de peluquería al mismo de estética
             * */
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            IServicioAgencias servicioAgencias = A.Fake<IServicioAgencias>();
            A.CallTo(() => servicio.Hoy()).Returns(new DateTime(2017, 12, 31, 23, 2, 0)); //2 minutos -> "YO"
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string> { "YO", "TU" });
            A.CallTo(() => servicio.VendedoresPresenciales()).Returns(new List<string> { "EL", "ELLA" });
            A.CallTo(() => servicio.BuscarCliente(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new Cliente
                {
                    Vendedor = "NV",
                    Estado = Constantes.Clientes.Estados.VISITA_PRESENCIAL,
                    Usuario = "erastu",
                    VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                    {
                        new VendedorClienteGrupoProducto
                        {
                            GrupoProducto = "PEL",
                            Vendedor = "ELLA",
                            Usuario = "eraella"
                        }
                    }
                }
            );
            A.CallTo(() => servicio.BuscarContactos(A<NVEntities>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(
                new List<Cliente> {
                    new Cliente
                    {
                        Vendedor = "TU",
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fueel",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "fuiyo"
                            }
                        }
                    },
                    new Cliente
                    {
                        Vendedor = "NV", // <-- esta es la clave, vendedor presencial
                        Estado = Constantes.Clientes.Estados.COMISIONA_SIN_VISITA,
                        Usuario = "fuistetu",
                        VendedoresClienteGrupoProductoes = new List<VendedorClienteGrupoProducto>
                        {
                            new VendedorClienteGrupoProducto
                            {
                                GrupoProducto = "PEL",
                                Vendedor = "NV",
                                Usuario = "eraella"
                            }
                        }
                    }
                }
            );
            IGestorClientes gestor = new GestorClientes(servicio, servicioAgencias);
            ClienteCrear cliente = new ClienteCrear
            {
                Empresa = "1",
                Cliente = "1000",
                Contacto = "0",
                Usuario = "cestmoi",
                VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL
            };


            List<Cliente> clientesDB = gestor.DejarDeVisitar(db, cliente).Result;
            Cliente clienteDB = clientesDB[2];
            Cliente contactoDB = clientesDB[0];
            Cliente contacto2DB = clientesDB[1];


            Assert.AreEqual("TU", clienteDB.Vendedor);
            Assert.AreEqual("cestmoi", clienteDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.VISITA_TELEFONICA, clienteDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("TU", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("TU", contactoDB.Vendedor);
            Assert.AreEqual("cestmoi", contactoDB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contactoDB.Estado);
            Assert.AreEqual(1, clienteDB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("TU", clienteDB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);

            Assert.AreEqual("TU", contacto2DB.Vendedor);
            Assert.AreEqual("cestmoi", contacto2DB.Usuario);
            Assert.AreEqual(Constantes.Clientes.Estados.COMISIONA_SIN_VISITA, contacto2DB.Estado);
            Assert.AreEqual(1, contacto2DB.VendedoresClienteGrupoProductoes.Count);
            Assert.AreEqual("TU", contacto2DB.VendedoresClienteGrupoProductoes.ElementAt(0).Vendedor);
        }
    }
}
