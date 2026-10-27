using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#701: guardar los textos web de un producto no debe fallar si Google Pub/Sub tarda. El 23/10/26
    /// Enrique tuvo 8 errores DeadlineExceeded en dos minutos: lo guardado estaba hecho, pero Nesto enseñaba error
    /// y la tienda no se enteraba hasta que alguien volvía a guardar.
    /// </summary>
    [TestClass]
    public class PublicacionTiendaTests
    {
        private ISincronizacionEventPublisher publisher;
        private GestorProductos gestor;
        private IServicioCorreoElectronico correo;
        private List<Exception> logueados;
        private List<(string productoId, string source, string usuario, int intento, TimeSpan espera)> programados;
        private ResultadoPuertaPublicacion puertaActual;

        [TestInitialize]
        public void Setup()
        {
            publisher = A.Fake<ISincronizacionEventPublisher>();
            gestor = new GestorProductos(new SincronizacionEventWrapper(publisher));
            correo = A.Fake<IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Returns(true);
            logueados = new List<Exception>();
            programados = new List<(string, string, string, int, TimeSpan)>();
            puertaActual = ResultadoPuertaPublicacion.SiPublicable();
            puertaActual.Dto = Dto("17404", "Nombre ACTUAL en la ficha");
        }

        private PublicacionTienda Publicacion() => new PublicacionTienda(
            gestor,
            construirPorLaPuerta: id => Task.FromResult(puertaActual),
            programar: (productoId, source, usuario, intento, espera) =>
            {
                programados.Add((productoId, source, usuario, intento, espera));
                return "job-" + intento;
            },
            correo: correo,
            log: logueados.Add);

        private static ProductoDTO Dto(string producto, string nombre = "Nombre") => new ProductoDTO
        {
            Producto = producto,
            Nombre = nombre,
            PrecioProfesional = 10M,
            PrecioPublicoFinal = 12.1M
        };

        private void PubSubFalla() =>
            A.CallTo(() => publisher.PublishEventAsync(A<string>._, A<object>._))
                .Throws(new TimeoutException("Status(StatusCode=\"DeadlineExceeded\", Detail=\"Deadline Exceeded\")"));

        #region La espera creciente

        [TestMethod]
        public void Espera_Creciente_2_4_8_16Minutos()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(2), PublicacionTienda.Espera(1));
            Assert.AreEqual(TimeSpan.FromMinutes(4), PublicacionTienda.Espera(2));
            Assert.AreEqual(TimeSpan.FromMinutes(8), PublicacionTienda.Espera(3));
            Assert.AreEqual(TimeSpan.FromMinutes(16), PublicacionTienda.Espera(4));
            Assert.AreEqual(4, PublicacionTienda.MAXIMO_INTENTOS, "Entre 3 y 5 reintentos, con espera creciente");
        }

        #endregion

        #region El intento en línea (justo después de guardar)

        [TestMethod]
        public async Task PublicarOAplazar_PubSubBien_PublicadaSinAviso()
        {
            ResultadoPublicacionTienda resultado = await Publicacion().PublicarOAplazar(Dto("17404"), "Nesto", "Enrique");

            Assert.IsTrue(resultado.Publicada);
            Assert.IsNull(resultado.Aviso);
            Assert.AreEqual(0, programados.Count, "Si se publica, no hay nada que reintentar");
        }

        [TestMethod]
        public async Task PublicarOAplazar_PubSubFalla_NoLanzaDevuelveAvisoYProgramaElPrimerReintentoA2Min()
        {
            PubSubFalla();

            ResultadoPublicacionTienda resultado = await Publicacion().PublicarOAplazar(Dto("17404"), "Nesto", "Enrique");

            Assert.IsFalse(resultado.Publicada);
            Assert.AreEqual("Guardado. La tienda se actualizará en unos minutos.", resultado.Aviso);
            Assert.AreEqual("job-1", resultado.JobReintento);
            Assert.AreEqual(1, programados.Count);
            Assert.AreEqual(("17404", "Nesto", "Enrique", 1, TimeSpan.FromMinutes(2)), programados.Single());
            Assert.AreEqual(0, logueados.Count, "Un fallo transitorio con reintento programado no es un error de ELMAH");
        }

        [TestMethod]
        public async Task PublicarOAplazar_PubSubFallaYNoSePuedeProgramar_LanzaElErrorOriginalYLoLoguea()
        {
            // Hangfire apagado (DEBUG) o caído: el usuario tiene que saber que la tienda NO se va a actualizar
            PubSubFalla();
            PublicacionTienda publicacion = new PublicacionTienda(gestor,
                programar: (a, b, c, d, e) => throw new InvalidOperationException("JobStorage.Current no configurado"),
                correo: correo, log: logueados.Add);

            try
            {
                await publicacion.PublicarOAplazar(Dto("17404"), "Nesto", "Enrique");
                Assert.Fail("Tenía que lanzar");
            }
            catch (TimeoutException ex)
            {
                StringAssert.Contains(ex.Message, "DeadlineExceeded");
            }
            Assert.AreEqual(1, logueados.Count);
            StringAssert.Contains(logueados.Single().Message, "tampoco se ha podido programar el reintento");
        }

        [TestMethod]
        public async Task PublicarOAplazar_SinGestor_NadaQuePublicar()
        {
            ResultadoPublicacionTienda resultado = await new PublicacionTienda(null).PublicarOAplazar(Dto("17404"), "Nesto", null);

            Assert.IsFalse(resultado.Publicada);
            Assert.IsNull(resultado.Aviso);
            Assert.IsNotNull(resultado.Motivo);
        }

        #endregion

        #region Los reintentos programados

        [TestMethod]
        public async Task Reintentar_UsaElEstadoActualDelProductoNoElDeAquelMomento()
        {
            // Entre el guardado y el reintento la ficha ha cambiado: lo que viaja es lo de AHORA (por la puerta)
            ProductoSyncMessage publicado = null;
            A.CallTo(() => publisher.PublishEventAsync("sincronizacion-tablas", A<object>._))
                .Invokes((string _, object message) => publicado = message as ProductoSyncMessage);

            ResultadoPublicacionTienda resultado = await Publicacion().Reintentar("17404", "Nesto", "Enrique", 1);

            Assert.IsTrue(resultado.Publicada);
            Assert.AreEqual("Nombre ACTUAL en la ficha", publicado.Nombre);
            Assert.AreEqual("Enrique", publicado.Usuario, "El usuario que guardó sigue siendo el autor del cambio");
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public async Task Reintentar_VuelveAFallar_ProgramaElSiguienteConMasEspera()
        {
            PubSubFalla();

            ResultadoPublicacionTienda resultado = await Publicacion().Reintentar("17404", "Nesto", "Enrique", 2);

            Assert.IsFalse(resultado.Publicada);
            Assert.IsFalse(resultado.Agotada);
            Assert.AreEqual(1, programados.Count);
            Assert.AreEqual(3, programados.Single().intento);
            Assert.AreEqual(TimeSpan.FromMinutes(8), programados.Single().espera, "El 3º espera 8 min (2, 4, 8, 16)");
            Assert.AreEqual(0, logueados.Count);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reintentar_AgotaLosIntentos_ElmahYCorreoATiendaOnline()
        {
            PubSubFalla();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => enviado = m).Returns(true);

            ResultadoPublicacionTienda resultado = await Publicacion().Reintentar("17404", "Nesto", "Enrique", PublicacionTienda.MAXIMO_INTENTOS);

            Assert.IsTrue(resultado.Agotada);
            Assert.AreEqual(0, programados.Count, "Era el último: no se programa otro");
            Assert.AreEqual(1, logueados.Count);
            StringAssert.Contains(logueados.Single().Message, "No se ha podido actualizar en la tienda el producto 17404");
            Assert.IsInstanceOfType(logueados.Single().InnerException, typeof(TimeoutException), "Con el último error dentro");
            Assert.IsNotNull(enviado);
            Assert.AreEqual(Constantes.Correos.TIENDA_ONLINE, enviado.To.Single().Address);
            StringAssert.Contains(enviado.Subject, "No se ha podido actualizar en la tienda el producto 17404");
            StringAssert.Contains(enviado.Body, "vuelva a guardar", "De usted");
        }

        [TestMethod]
        public async Task Reintentar_ProductoYaNoPublicable_NoPublicaNiInsiste()
        {
            puertaActual = ResultadoPuertaPublicacion.NoPublicable("es un producto ficticio");

            ResultadoPublicacionTienda resultado = await Publicacion().Reintentar("17404", "Nesto", "Enrique", 1);

            Assert.IsFalse(resultado.Publicada);
            StringAssert.Contains(resultado.Motivo, "ya no es publicable");
            A.CallTo(() => publisher.PublishEventAsync(A<string>._, A<object>._)).MustNotHaveHappened();
            Assert.AreEqual(0, programados.Count);
        }

        [TestMethod]
        public async Task Reintentar_ProductoBorrado_NadaQuePublicar()
        {
            puertaActual = null;

            ResultadoPublicacionTienda resultado = await Publicacion().Reintentar("17404", "Nesto", "Enrique", 1);

            StringAssert.Contains(resultado.Motivo, "ya no existe");
            A.CallTo(() => publisher.PublishEventAsync(A<string>._, A<object>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reintentar_SinSaberConstruirPorLaPuerta_Lanza()
        {
            PublicacionTienda sinPuerta = new PublicacionTienda(gestor, programar: (a, b, c, d, e) => "x", correo: correo, log: logueados.Add);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => sinPuerta.Reintentar("17404", "Nesto", null, 1));
        }

        #endregion
    }
}
