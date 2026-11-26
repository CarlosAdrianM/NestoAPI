using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ParametrosUsuario;
using NestoAPI.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>
    /// Incidencia 1481 (Lidia 26/11/26): PUT api/ParametrosUsuario daba 404 si el usuario no tenía aún su fila. Con la «(defecto)» de
    /// la clave se crea; sin ella, 404 de usted y una línea en ELMAH al día por clave.
    /// </summary>
    [TestClass]
    public class GuardadoParametroUsuarioTests
    {
        private IAlmacenParametrosUsuario almacen;

        [TestInitialize]
        public void Preparar()
        {
            almacen = A.Fake<IAlmacenParametrosUsuario>();
            AvisoParametroNoDadoDeAlta.Reiniciar();
        }

        private static ParametroUsuario Pedida(string usuario = "Lidia", string clave = "PlantillaBorradorActualizarCargado", string valor = "1")
            => new ParametroUsuario { Empresa = "1  ", Usuario = usuario + "   ", Clave = clave, Valor = valor };

        [TestMethod]
        public void Decidir_LasTresSalidas()
        {
            Assert.AreEqual(AccionGuardarParametroUsuario.Modificar, ReglaGuardarParametroUsuario.Decidir(true, false));
            Assert.AreEqual(AccionGuardarParametroUsuario.Modificar, ReglaGuardarParametroUsuario.Decidir(true, true));
            Assert.AreEqual(AccionGuardarParametroUsuario.CrearDesdeDefecto, ReglaGuardarParametroUsuario.Decidir(false, true));
            Assert.AreEqual(AccionGuardarParametroUsuario.NoDadoDeAlta, ReglaGuardarParametroUsuario.Decidir(false, false));
        }

        [TestMethod]
        public void MotivoNoDadoDeAlta_DeUsted()
        {
            Assert.AreEqual("El parámetro PlantillaBorradorActualizarCargado no está dado de alta: avise a Informática.",
                ReglaGuardarParametroUsuario.MotivoNoDadoDeAlta(" PlantillaBorradorActualizarCargado "));
        }

        [TestMethod]
        public void FilaNueva_ElValorPedidoConQuienYCuando()
        {
            var ahora = new DateTime(2026, 11, 26, 10, 0, 0);
            ParametroUsuario fila = ReglaGuardarParametroUsuario.FilaNueva(Pedida(), ahora);
            Assert.AreEqual("1", fila.Empresa);
            Assert.AreEqual("Lidia", fila.Usuario);
            Assert.AreEqual("Lidia", fila.Usuario2);
            Assert.AreEqual("1", fila.Valor);
            Assert.AreEqual(ahora, fila.Fecha_Modificación);
        }

        [TestMethod]
        public async Task Guardar_ConFilaDelUsuario_Modifica()
        {
            A.CallTo(() => almacen.Existe("1", "Lidia", "PlantillaBorradorActualizarCargado")).Returns(true);

            AccionGuardarParametroUsuario accion = await new ServicioGuardarParametroUsuario(almacen).Guardar(Pedida());

            Assert.AreEqual(AccionGuardarParametroUsuario.Modificar, accion);
            A.CallTo(() => almacen.Modificar(A<ParametroUsuario>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => almacen.Crear(A<ParametroUsuario>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_SinFilaDelUsuarioConDefecto_CreaLaDelUsuarioConElValorPedido()
        {
            A.CallTo(() => almacen.Existe("1", "Lidia", A<string>._)).Returns(false);
            A.CallTo(() => almacen.Existe("1", "(defecto)", "PlantillaBorradorActualizarCargado")).Returns(true);
            ParametroUsuario creada = null;
            A.CallTo(() => almacen.Crear(A<ParametroUsuario>._)).Invokes((ParametroUsuario p) => creada = p);

            AccionGuardarParametroUsuario accion = await new ServicioGuardarParametroUsuario(almacen).Guardar(Pedida(valor: "0"));

            Assert.AreEqual(AccionGuardarParametroUsuario.CrearDesdeDefecto, accion);
            Assert.AreEqual("Lidia", creada.Usuario);
            Assert.AreEqual("0", creada.Valor);
            A.CallTo(() => almacen.Modificar(A<ParametroUsuario>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_SinNinguna_NoDadoDeAltaYNoEscribe()
        {
            AccionGuardarParametroUsuario accion = await new ServicioGuardarParametroUsuario(almacen).Guardar(Pedida());

            Assert.AreEqual(AccionGuardarParametroUsuario.NoDadoDeAlta, accion);
            A.CallTo(() => almacen.Modificar(A<ParametroUsuario>._)).MustNotHaveHappened();
            A.CallTo(() => almacen.Crear(A<ParametroUsuario>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Aviso_UnaVezPorClaveYDia()
        {
            var hoy = new DateTime(2026, 11, 26, 9, 0, 0);
            Assert.IsTrue(AvisoParametroNoDadoDeAlta.TocaAvisar("1", "X", hoy));
            Assert.IsFalse(AvisoParametroNoDadoDeAlta.TocaAvisar("1", "X", hoy.AddHours(5)));
            Assert.IsTrue(AvisoParametroNoDadoDeAlta.TocaAvisar("1", "Y", hoy));
            Assert.IsTrue(AvisoParametroNoDadoDeAlta.TocaAvisar("1", "X", hoy.AddDays(1)));
        }

        private ParametrosUsuarioController Controlador(string usuario = "Lidia")
        {
            return new ParametrosUsuarioController
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, usuario) }, "Bearer")),
                AlmacenParametros = almacen
            };
        }

        [TestMethod]
        public async Task Put_SinFilaConDefecto_204()
        {
            A.CallTo(() => almacen.Existe("1", "(defecto)", A<string>._)).Returns(true);

            IHttpActionResult resultado = await Controlador().PutParametroUsuario(Pedida());

            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)resultado).StatusCode);
            A.CallTo(() => almacen.Crear(A<ParametroUsuario>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Put_NoDadoDeAlta_404ConElMotivo()
        {
            var resultado = (NegotiatedContentResult<string>)await Controlador().PutParametroUsuario(Pedida());

            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "no está dado de alta");
        }

        [TestMethod]
        public async Task Put_FilaDeOtroUsuario_403SinTocarNada()
        {
            var resultado = (NegotiatedContentResult<string>)await Controlador("Pilar").PutParametroUsuario(Pedida());

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => almacen.Existe(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }
    }
}
