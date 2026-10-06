using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Sincronizacion;

namespace NestoAPI.Tests.Infraestructure.Direcciones
{
    /// <summary>
    /// NestoAPI#596 (paso 2): el CP se graba canónico, lo teclee el usuario como lo teclee, para que la
    /// BD deje de mezclar «4480 670», «4480670» y «4480-670». Nunca se rechaza nada por formato.
    /// </summary>
    [TestClass]
    public class CodigoPostalAlGuardarTests
    {
        private static GestorClientes CrearGestor(IServicioGestorClientes servicio)
        {
            A.CallTo(() => servicio.CalcularSiguienteContacto(A<string>.Ignored, A<string>.Ignored)).Returns("0");
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string>());
            return new GestorClientes(servicio, A.Fake<IServicioAgencias>(),
                new SincronizacionEventWrapper(A.Fake<ISincronizacionEventPublisher>()));
        }

        [TestMethod]
        public async Task AltaCliente_PortugalConEspacio_SeGuardaConGuionYElCodigoPostalNaceCanonico()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<CodigoPostal>(null));
            GestorClientes gestor = CrearGestor(servicio);
            var alta = new ClienteCrear
            {
                Cliente = "41990",
                Nombre = "CLIENTE VILA DO CONDE",
                Pais = "PT",
                CodigoPostal = "4480 670 ",
                Poblacion = "VILA DO CONDE",
                PersonasContacto = new List<PersonaContactoDTO>()
            };
            NVEntities db = A.Fake<NVEntities>();

            Cliente cliente = await gestor.PrepararClienteCrear(alta, db);

            Assert.AreEqual("4480-670", cliente.CodPostal);
            Assert.AreEqual("4480-670", cliente.CódigosPostales.Número, "CódigosPostales nace en el formato canónico");
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, "4480-670")).MustHaveHappened();
        }

        [TestMethod]
        public async Task AltaCliente_EspanaSinElCero_SeGuardaConCincoCifras()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, "08850"))
                .Returns(new CodigoPostal { Empresa = "1", Número = "08850", Descripción = "GAVA" });
            GestorClientes gestor = CrearGestor(servicio);
            var alta = new ClienteCrear
            {
                Cliente = "41991",
                Nombre = "CLIENTE GAVA",
                Pais = "ES",
                CodigoPostal = "8850",
                PersonasContacto = new List<PersonaContactoDTO>()
            };

            Cliente cliente = await gestor.PrepararClienteCrear(alta, A.Fake<NVEntities>());

            Assert.AreEqual("08850", cliente.CodPostal);
        }

        [TestMethod]
        public async Task AltaCliente_LaTablaSoloTieneElFormatoTecleado_SeQuedaElTecleadoParaNoRomperLaFK()
        {
            // Hasta que el script del paso 4 fusione los duplicados: si CódigosPostales solo tiene
            // «4450 189», el cliente apunta a esa fila en vez de crear otra.
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, "4450-189")).Returns(Task.FromResult<CodigoPostal>(null));
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, "4450 189"))
                .Returns(new CodigoPostal { Empresa = "1", Número = "4450 189", Pais = "PT" });
            GestorClientes gestor = CrearGestor(servicio);

            string cp = await gestor.CodigoPostalParaGuardar("1", "4450 189", "PT");

            Assert.AreEqual("4450 189", cp);
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_Extranjero_DevuelveElCanonico()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<CodigoPostal>(null));
            GestorClientes gestor = CrearGestor(servicio);

            RespuestaDatosGeneralesClientes r = await gestor.ComprobarDatosGenerales("RUA DA PRAIA 1", "4480670", "912345678", pais: "PT");

            Assert.AreEqual("4480-670", r.CodigoPostal);
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_EspanaSinElCero_BuscaElCanonico()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.CogerDatosCodigoPostal("08850"))
                .Returns(new RespuestaDatosGeneralesClientes { CodigoPostal = "08850", Poblacion = "GAVA" });
            GestorClientes gestor = CrearGestor(servicio);

            RespuestaDatosGeneralesClientes r = await gestor.ComprobarDatosGenerales("CALLE MAYOR 1", "8850", "912345678", direccionVerificada: true, pais: "ES");

            Assert.AreEqual("08850", r.CodigoPostal);
            A.CallTo(() => servicio.CogerDatosCodigoPostal("08850")).MustHaveHappenedOnceExactly();
        }

        // ---- Envíos ----

        [DataTestMethod]
        [DataRow("4480 670 ", "PT", "4480-670")]
        [DataRow("4480670", "", "4480-670")]
        [DataRow("4480 670", "ES", "4480-670")] // envío 249519: 7 cifras son Portugal aunque diga España
        [DataRow("28001 ", "ES", "28001")]
        [DataRow("2000", "ES", "2000")]         // ficha belga con país ES: no se convierte en Albacete
        [DataRow(null, "ES", "")]
        public void CodigoPostalEnvio_GrabaElCanonicoSinInventarCeros(string cp, string pais, string esperado)
        {
            Assert.AreEqual(esperado, EnviosAgenciasController.CodigoPostalEnvio(cp, pais));
        }
    }
}
