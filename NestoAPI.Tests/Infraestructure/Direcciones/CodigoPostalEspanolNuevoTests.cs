using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Sincronizacion;

namespace NestoAPI.Tests.Infraestructure.Direcciones
{
    /// <summary>
    /// NestoAPI#821 (Carlos 05/11/26): un CP español que aún no está en CódigosPostales se da de alta solo
    /// cuando la dirección viene verificada por Google (combo de Places); sin verificar, se rechaza con un
    /// mensaje que dice cómo darlo de alta. Los extranjeros siguen como siempre (#376).
    /// </summary>
    [TestClass]
    public class CodigoPostalEspanolNuevoTests
    {
        private const string MENSAJE_48530 = "No existe el código postal 48530. Elija la dirección en el buscador de Google para darlo de alta.";

        private static IServicioGestorClientes ServicioSinElCp(string rutaProvincia = "00")
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<CodigoPostal>(null));
            A.CallTo(() => servicio.RutaMasFrecuenteProvincia(A<string>.Ignored, A<string>.Ignored)).Returns(rutaProvincia);
            A.CallTo(() => servicio.CalcularSiguienteContacto(A<string>.Ignored, A<string>.Ignored)).Returns("0");
            A.CallTo(() => servicio.VendedoresTelefonicos()).Returns(new List<string>());
            return servicio;
        }

        private static GestorClientes Gestor(IServicioGestorClientes servicio)
            => new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(A.Fake<ISincronizacionEventPublisher>()));

        [TestMethod]
        public async Task AsegurarCodigoPostal_EspanolNuevoVerificado_SeCreaConEsYLaRutaDeLaProvincia()
        {
            IServicioGestorClientes servicio = ServicioSinElCp("FW");
            NVEntities db = A.Fake<NVEntities>();

            (CodigoPostal cp, bool esNuevo) = await Gestor(servicio).AsegurarCodigoPostal(db, "1", "28999", "Villanueva del Pardillo", "Madrid", "ES", direccionVerificada: true);

            Assert.IsTrue(esNuevo);
            Assert.AreEqual("28999", cp.Número);
            Assert.AreEqual("VILLANUEVA DEL PARDILLO", cp.Descripción);
            Assert.AreEqual("MADRID", cp.Provincia);
            Assert.AreEqual("ES", cp.Pais);
            Assert.AreEqual("FW", cp.Ruta, "La más frecuente de la provincia");
            Assert.AreEqual(Constantes.Vendedores.VENDEDOR_GENERAL, cp.Vendedor);
            A.CallTo(() => db.CodigosPostales.Add(cp)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicio.RutaMasFrecuenteProvincia("1", "28999")).MustHaveHappened();
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_EspanolSinPais_SeTrataComoEspanolYNormalizaElCp()
        {
            (CodigoPostal cp, bool esNuevo) = await Gestor(ServicioSinElCp()).AsegurarCodigoPostal(A.Fake<NVEntities>(), "1", "8999", "Gavà", "Barcelona", null, direccionVerificada: true);

            Assert.IsTrue(esNuevo);
            Assert.AreEqual("08999", cp.Número, "Formato canónico de 5 cifras (#596)");
            Assert.AreEqual("ES", cp.Pais);
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_ProvinciaSinRutas_RutaCero()
        {
            (CodigoPostal cp, _) = await Gestor(ServicioSinElCp(rutaProvincia: null)).AsegurarCodigoPostal(A.Fake<NVEntities>(), "1", "48530", "Ortuella", "Bizkaia", "ES", direccionVerificada: true);

            Assert.AreEqual(Constantes.Clientes.RUTA_CLIENTES_EXTRANJEROS, cp.Ruta);
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_PoblacionMuyLarga_SeTrunca()
        {
            string larga = new string('A', 60);
            (CodigoPostal cp, _) = await Gestor(ServicioSinElCp()).AsegurarCodigoPostal(A.Fake<NVEntities>(), "1", "48530", larga, larga, "ES", direccionVerificada: true);

            Assert.AreEqual(50, cp.Descripción.Length);
            Assert.AreEqual(30, cp.Provincia.Length);
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_EspanolNuevoSinVerificar_RechazaConElMensajeNuevoYNoCrea()
        {
            NVEntities db = A.Fake<NVEntities>();

            CodigoPostalNoExisteException ex = await Assert.ThrowsExceptionAsync<CodigoPostalNoExisteException>(() =>
                Gestor(ServicioSinElCp()).AsegurarCodigoPostal(db, "1", "48530", "ORTUELLA", "VIZCAYA", "ES", direccionVerificada: false));

            Assert.AreEqual(MENSAJE_48530, ex.Message);
            A.CallTo(() => db.CodigosPostales.Add(A<CodigoPostal>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_VerificadoSinPoblacion_NoLoCreaEnBlanco()
        {
            NVEntities db = A.Fake<NVEntities>();

            CodigoPostalNoExisteException ex = await Assert.ThrowsExceptionAsync<CodigoPostalNoExisteException>(() =>
                Gestor(ServicioSinElCp()).AsegurarCodigoPostal(db, "1", "48530", null, "VIZCAYA", "ES", direccionVerificada: true));

            StringAssert.Contains(ex.Message, "48530");
            StringAssert.Contains(ex.Message, "población");
            A.CallTo(() => db.CodigosPostales.Add(A<CodigoPostal>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_EspanolExistente_NoSeToca()
        {
            IServicioGestorClientes servicio = ServicioSinElCp();
            var existente = new CodigoPostal { Empresa = "1", Número = "28001", Descripción = "MADRID", Provincia = "MADRID", Ruta = "FW", Vendedor = "AL", Pais = null };
            A.CallTo(() => servicio.BuscarCodigoPostal(A<string>.Ignored, "28001")).Returns(existente);
            NVEntities db = A.Fake<NVEntities>();

            (CodigoPostal cp, bool esNuevo) = await Gestor(servicio).AsegurarCodigoPostal(db, "1", "28001", "OTRA", "OTRA", "ES", direccionVerificada: true);

            Assert.IsFalse(esNuevo);
            Assert.AreSame(existente, cp);
            Assert.AreEqual("MADRID", cp.Descripción);
            Assert.AreEqual("FW", cp.Ruta);
            Assert.IsNull(cp.Pais, "No se autocompleta el país de un CP español existente");
            A.CallTo(() => db.CodigosPostales.Add(A<CodigoPostal>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => db.CodigosPostales.Attach(A<CodigoPostal>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task AsegurarCodigoPostal_ExtranjeroSinVerificar_SeCreaComoSiempre()
        {
            (CodigoPostal cp, bool esNuevo) = await Gestor(ServicioSinElCp("FW")).AsegurarCodigoPostal(A.Fake<NVEntities>(), "1", "20121", "Milano", "MI", "IT", direccionVerificada: false);

            Assert.IsTrue(esNuevo);
            Assert.AreEqual("IT", cp.Pais);
            Assert.AreEqual(Constantes.Clientes.RUTA_CLIENTES_EXTRANJEROS, cp.Ruta, "El extranjero no usa la ruta de la provincia");
        }

        // ---- Alta de cliente ----

        private static ClienteCrear Alta(bool verificada, string poblacion = "ORTUELLA", string provincia = "VIZCAYA") => new ClienteCrear
        {
            Cliente = "41992",
            Nombre = "CLIENTE ORTUELLA",
            Pais = "ES",
            CodigoPostal = "48530",
            Poblacion = poblacion,
            Provincia = provincia,
            DireccionVerificada = verificada,
            PersonasContacto = new List<PersonaContactoDTO>()
        };

        [TestMethod]
        public async Task AltaCliente_CpEspanolNuevoVerificado_CuelgaElCpNuevo()
        {
            Cliente cliente = await Gestor(ServicioSinElCp()).PrepararClienteCrear(Alta(verificada: true), A.Fake<NVEntities>());

            Assert.AreEqual("48530", cliente.CodPostal);
            Assert.IsNotNull(cliente.CódigosPostales, "El CP nuevo se inserta antes que el cliente");
            Assert.AreEqual("ORTUELLA", cliente.CódigosPostales.Descripción);
            Assert.AreEqual("ES", cliente.CódigosPostales.Pais);
        }

        [TestMethod]
        public async Task AltaCliente_CpEspanolNuevoSinVerificar_Rechaza()
        {
            CodigoPostalNoExisteException ex = await Assert.ThrowsExceptionAsync<CodigoPostalNoExisteException>(() =>
                Gestor(ServicioSinElCp()).PrepararClienteCrear(Alta(verificada: false), A.Fake<NVEntities>()));

            Assert.AreEqual(MENSAJE_48530, ex.Message);
        }

        // ---- ComprobarDatosGenerales ----

        [TestMethod]
        public async Task ComprobarDatosGenerales_VerificadaConCpNuevo_NoFallaYDevuelveLosDatosDeGoogle()
        {
            IServicioGestorClientes servicio = ServicioSinElCp("00");

            RespuestaDatosGeneralesClientes r = await Gestor(servicio).ComprobarDatosGenerales("Calle Mayor 1", "48530", "944000000",
                direccionVerificada: true, pais: "ES", poblacion: "Ortuella", provincia: "Bizkaia");

            Assert.AreEqual("48530", r.CodigoPostal);
            Assert.AreEqual("ORTUELLA", r.Poblacion);
            Assert.AreEqual("BIZKAIA", r.Provincia);
            Assert.AreEqual("00", r.Ruta);
            Assert.AreEqual(Constantes.Vendedores.VENDEDOR_GENERAL, r.VendedorEstetica);
            Assert.IsFalse(string.IsNullOrWhiteSpace(r.DireccionFormateada));
            A.CallTo(() => servicio.CogerDatosCodigoPostal(A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_VerificadaConCpNuevoSinPoblacion_DevuelveNullParaQueElClienteConserveLaSuya()
        {
            RespuestaDatosGeneralesClientes r = await Gestor(ServicioSinElCp("FW")).ComprobarDatosGenerales("Calle Mayor 1", "28999", "916000000", direccionVerificada: true);

            Assert.IsNull(r.Poblacion);
            Assert.IsNull(r.Provincia);
            Assert.AreEqual("FW", r.Ruta);
        }

        [TestMethod]
        public void MensajeCodigoPostalNoExiste_DiceComoDarloDeAlta()
        {
            Assert.AreEqual(MENSAJE_48530, GestorClientes.MensajeCodigoPostalNoExiste("48530 "));
        }
    }
}
