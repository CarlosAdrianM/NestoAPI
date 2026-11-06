using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#230 (06/11/26): los errores de cliente repetidos (misma app, usuario y mensaje en 24 h) no inundan ELMAH.
    /// </summary>
    [TestClass]
    public class DeduplicadorErroresClienteTests
    {
        private const string INSTALL_ERROR = "Install Error(-10): The app is not owned by any user on this device";
        private const string GENERICO = "Referencia a objeto no establecida como instancia de un objeto.";

        private DateTime ahora;
        private DeduplicadorErroresCliente deduplicador;
        private ILogService log;
        private ErroresController controlador;

        [TestInitialize]
        public void Setup()
        {
            ahora = new DateTime(2026, 11, 6, 9, 0, 0, DateTimeKind.Utc);
            deduplicador = new DeduplicadorErroresCliente(() => ahora);
            log = A.Fake<ILogService>();
            controlador = new ErroresController(log, deduplicador);
        }

        private static ErrorClienteDTO Error(string mensaje = INSTALL_ERROR, string usuario = "Alfredo", string pila = null, string app = "Ariadna")
            => new ErrorClienteDTO { Aplicacion = app, Version = "2.30", Mensaje = mensaje, StackTrace = pila, UsuarioCliente = usuario };

        [TestMethod]
        public void Post_PrimerError_SeLoguea()
        {
            _ = controlador.Post(Error());

            A.CallTo(() => log.LogError(A<string>.That.Contains("Install Error(-10)"), A<Exception>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Post_RepetidoAUnaHora_NoSeLoguea()
        {
            _ = controlador.Post(Error());
            ahora = ahora.AddHours(1);
            _ = controlador.Post(Error());

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Post_TrasLas24Horas_SeLogueaConElContador()
        {
            _ = controlador.Post(Error());
            for (int i = 1; i <= 16; i++)
            {
                ahora = ahora.AddHours(1);
                _ = controlador.Post(Error());
            }
            ahora = ahora.AddHours(9); // 25 h después del primero

            _ = controlador.Post(Error());

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => log.LogError(A<string>.That.EndsWith("(repetido 16 veces en las últimas 24 h)"), A<Exception>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Post_TrasLas24HorasSinRepeticiones_SinContador()
        {
            _ = controlador.Post(Error());
            ahora = ahora.AddHours(25);

            _ = controlador.Post(Error());

            A.CallTo(() => log.LogError(A<string>.That.Contains("repetido"), A<Exception>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Post_UsuariosDistintos_NoSeConfunden()
        {
            _ = controlador.Post(Error(usuario: "Alfredo"));
            _ = controlador.Post(Error(usuario: "Pedro"));

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Post_AplicacionesDistintas_NoSeConfunden()
        {
            _ = controlador.Post(Error(app: "Ariadna"));
            _ = controlador.Post(Error(app: "NestoApp"));

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Post_MensajeGenericoConPilasDistintas_SeLogueanLosDos()
        {
            _ = controlador.Post(Error(GENERICO, pila: "System.NullReferenceException: " + GENERICO + "\r\n   en Nesto.ClientesViewModel.OnReclamarDeuda()"));
            _ = controlador.Post(Error(GENERICO, pila: "System.NullReferenceException: " + GENERICO + "\r\n   en Nesto.PedidosViewModel.Guardar()"));

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public void Post_MensajeGenericoConLaMismaPila_SoloUnaVez()
        {
            string pila = "System.NullReferenceException: " + GENERICO + "\r\n   en Nesto.ClientesViewModel.OnReclamarDeuda()";
            _ = controlador.Post(Error(GENERICO, pila: pila));
            _ = controlador.Post(Error(GENERICO, pila: pila));

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Post_MensajeNoGenericoConPilasDistintas_SoloUnaVez()
        {
            _ = controlador.Post(Error(pila: "   at Android.App.A()"));
            _ = controlador.Post(Error(pila: "   at Android.App.B()"));

            A.CallTo(() => log.LogError(A<string>.Ignored, A<Exception>.Ignored)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Normalizar_PrimeraLineaSinNumerosLargosNiFechas()
        {
            string a = DeduplicadorErroresCliente.Normalizar("Pedido 912345 no encontrado el 06/11/2026 10:15:22\r\nmás detalle");
            string b = DeduplicadorErroresCliente.Normalizar("Pedido 912399 no encontrado el 05/11/2026 08:01:02\r\notro detalle");

            Assert.AreEqual(a, b);
            Assert.IsFalse(a.Contains("detalle"), "Solo la primera línea");
        }

        [TestMethod]
        public void Normalizar_NumerosCortosSeQuedan()
        {
            Assert.AreNotEqual(DeduplicadorErroresCliente.Normalizar("Install Error(-10)"), DeduplicadorErroresCliente.Normalizar("Install Error(-12)"));
        }

        [TestMethod]
        public void SufijoRepetidos_UnaVezYVarias()
        {
            Assert.AreEqual(string.Empty, DeduplicadorErroresCliente.SufijoRepetidos(0));
            Assert.AreEqual(" (repetido 1 vez en las últimas 24 h)", DeduplicadorErroresCliente.SufijoRepetidos(1));
            Assert.AreEqual(" (repetido 3 veces en las últimas 24 h)", DeduplicadorErroresCliente.SufijoRepetidos(3));
        }
    }
}
