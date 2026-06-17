using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class ErroresControllerTests
    {
        private ILogService _logService;
        private ErroresController _controller;

        [TestInitialize]
        public void Setup()
        {
            _logService = A.Fake<ILogService>();
            _controller = new ErroresController(_logService);
        }

        [TestMethod]
        public void Post_ConErrorValido_RegistraEnLogYDevuelveOk()
        {
            var error = new ErrorClienteDTO
            {
                Aplicacion = "Nesto",
                Version = "1.10.5.0",
                TipoExcepcion = "NullReferenceException",
                Mensaje = "Object reference not set to an instance of an object",
                StackTrace = "   en Nesto.ClientesViewModel.OnReclamarDeuda()",
                Contexto = "Reclamar deuda"
            };

            var resultado = _controller.Post(error);

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => _logService.LogError(
                A<string>.That.Matches(m => m.Contains("Nesto") && m.Contains("Object reference")),
                A<ErrorClienteException>.That.Matches(ex => ex.StackTrace.Contains("OnReclamarDeuda"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void ErrorClienteException_ToString_IncluyeLaPilaDelCliente()
        {
            // Regresión (Nesto#377): en .NET Framework, Exception.ToString() NO usa la propiedad
            // virtual StackTrace (lee el campo interno), así que el override no bastaba y el
            // Detail de ELMAH (que se construye con ToString() del wrapper de ElmahLogService)
            // llegaba sin la pila capturada en el cliente — imposible localizar el origen.
            string pilaCliente = "System.ArgumentNullException: Value cannot be null. (Parameter 'source')\r\n"
                + "   en System.Linq.Enumerable.Where[TSource](IEnumerable`1 source, Func`2 predicate)\r\n"
                + "   en Nesto.ViewModels.AlgunViewModel.CargarDatos()";
            var excepcionCliente = new ErrorClienteException("Value cannot be null. (Parameter 'source')", pilaCliente);

            // El mismo wrapper que crea ElmahLogService.LogError(resumen, excepcion)
            var wrapper = new System.Exception("[Nesto 1.10.4 (Windows)] DispatcherUnhandledException", excepcionCliente);

            Assert.IsTrue(excepcionCliente.ToString().Contains("CargarDatos"),
                "ToString() de la excepción debe incluir la pila del cliente");
            Assert.IsTrue(wrapper.ToString().Contains("CargarDatos"),
                "ToString() del wrapper (lo que ELMAH guarda como Detail) debe incluir la pila del cliente");
        }

        [TestMethod]
        public void Post_SinMensaje_DevuelveBadRequestYNoRegistra()
        {
            var error = new ErrorClienteDTO
            {
                Aplicacion = "Nesto",
                Mensaje = "   "
            };

            var resultado = _controller.Post(error);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _logService.LogError(A<string>._, A<System.Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Post_ErrorNull_DevuelveBadRequest()
        {
            var resultado = _controller.Post(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => _logService.LogError(A<string>._, A<System.Exception>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void ConstruirResumen_SinAplicacion_UsaClientePorDefecto()
        {
            var error = new ErrorClienteDTO { Mensaje = "Algo falló" };

            string resumen = ErroresController.ConstruirResumen(error);

            Assert.IsTrue(resumen.StartsWith("[Cliente]"), resumen);
            Assert.IsTrue(resumen.Contains("Algo falló"), resumen);
        }

        [TestMethod]
        public void ConstruirResumen_ConTodosLosCampos_IncluyeVersionContextoYTipo()
        {
            var error = new ErrorClienteDTO
            {
                Aplicacion = "Nesto",
                Version = "1.10.5.0",
                Contexto = "Reclamar deuda",
                TipoExcepcion = "InvalidOperationException",
                Mensaje = "No cuadra"
            };

            string resumen = ErroresController.ConstruirResumen(error);

            Assert.IsTrue(resumen.Contains("Nesto 1.10.5.0"), resumen);
            Assert.IsTrue(resumen.Contains("Reclamar deuda"), resumen);
            Assert.IsTrue(resumen.Contains("InvalidOperationException"), resumen);
            Assert.IsTrue(resumen.Contains("No cuadra"), resumen);
        }

        private static System.Security.Principal.IPrincipal Principal(string nombre)
            => new System.Security.Principal.GenericPrincipal(
                new System.Security.Principal.GenericIdentity(nombre ?? ""), new string[0]);

        [TestMethod]
        public void UsuarioParaElmah_PrincipalAutenticado_NoLoSobrescribe()
        {
            // Si el JWT/Windows ya dio usuario (UserSyncHandler), se respeta (devuelve null = no tocar).
            var error = new ErrorClienteDTO { Mensaje = "x", UsuarioCliente = "Otro" };

            string usuario = ErroresController.UsuarioParaElmah(Principal("Ines"), error);

            Assert.IsNull(usuario);
        }

        [TestMethod]
        public void UsuarioParaElmah_AnonimoConUsuarioCliente_DevuelveElDelCuerpo()
        {
            // Petición anónima (NestoApp): el usuario sale del cuerpo para que ELMAH no quede vacío.
            var error = new ErrorClienteDTO { Mensaje = "x", UsuarioCliente = "  Ines  " };

            string usuario = ErroresController.UsuarioParaElmah(Principal(""), error);

            Assert.AreEqual("Ines", usuario);
        }

        [TestMethod]
        public void UsuarioParaElmah_SinPrincipalConUsuarioCliente_DevuelveElDelCuerpo()
        {
            var error = new ErrorClienteDTO { Mensaje = "x", UsuarioCliente = "Ines" };

            string usuario = ErroresController.UsuarioParaElmah(null, error);

            Assert.AreEqual("Ines", usuario);
        }

        [TestMethod]
        public void UsuarioParaElmah_AnonimoSinUsuarioCliente_DevuelveNull()
        {
            var error = new ErrorClienteDTO { Mensaje = "x" };

            string usuario = ErroresController.UsuarioParaElmah(Principal(""), error);

            Assert.IsNull(usuario);
        }

        [TestMethod]
        public void ConstruirResumen_ConPlataformaYUsuarioCliente_LosIncluye()
        {
            // NestoApp sin token válido (pre-login): no hay usuario en el Identity,
            // pero el cliente puede aportar plataforma y usuario conocido.
            var error = new ErrorClienteDTO
            {
                Aplicacion = "NestoApp",
                Version = "2.3.1",
                Plataforma = "Android",
                UsuarioCliente = "vendedor01",
                Mensaje = "Cannot read property of undefined"
            };

            string resumen = ErroresController.ConstruirResumen(error);

            Assert.IsTrue(resumen.Contains("NestoApp 2.3.1"), resumen);
            Assert.IsTrue(resumen.Contains("Android"), resumen);
            Assert.IsTrue(resumen.Contains("vendedor01"), resumen);
            Assert.IsTrue(resumen.Contains("Cannot read property"), resumen);
        }
    }
}
