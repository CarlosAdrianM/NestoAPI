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
    }
}
