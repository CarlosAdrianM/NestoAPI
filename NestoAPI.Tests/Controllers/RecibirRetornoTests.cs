using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// POST api/EnviosAgencias/{id}/RecibirRetorno (Nesto#340 slice A4.2): el almacén confirma que
    /// la agencia ha traído de vuelta la mercancía de un envío. Sustituye al UPDATE por Entity
    /// Framework de AgenciasViewModel.OnRecibirRetorno; la fecha y el usuario los pone el servidor.
    /// </summary>
    [TestClass]
    public class RecibirRetornoTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            controller = new EnviosAgenciasController(db);
        }

        private void ConUsuario(string nombre)
        {
            IPrincipal usuario = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, nombre) }, "Bearer"));
            controller.User = usuario;
        }

        private EnviosAgencia ConEnvio(DateTime? fechaRetornoRecibido)
        {
            EnviosAgencia envio = new EnviosAgencia
            {
                Numero = 248001,
                Pedido = 925100,
                Cliente = "29268     ",
                Retorno = 2,
                FechaRetornoRecibido = fechaRetornoRecibido,
                Usuario = @"NUEVAVISION\almacen"
            };
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            return envio;
        }

        [TestMethod]
        public async Task RecibirRetorno_SinFecha_EstampaHoyYElUsuarioDelJwt()
        {
            ConUsuario(@"NUEVAVISION\laura");
            EnviosAgencia envio = ConEnvio(null);

            var resultado = await controller.RecibirRetorno(248001) as OkNegotiatedContentResult<RetornoRecibidoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(DateTime.Today, envio.FechaRetornoRecibido);
            Assert.AreEqual(@"NUEVAVISION\laura", envio.Usuario, "el usuario lo pone el servidor desde el token, nunca el cliente");
            Assert.AreEqual(248001, resultado.Content.Numero);
            Assert.AreEqual(925100, resultado.Content.Pedido);
            Assert.AreEqual(DateTime.Today, resultado.Content.FechaRetornoRecibido);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RecibirRetorno_YaRecibido_Devuelve400YNoGuarda()
        {
            // La lista de retornos solo enseña los que no tienen fecha: si llega uno con fecha es
            // otra sesión pisando la misma fila, y la primera fecha es la buena.
            ConUsuario("carlos");
            DateTime ayer = DateTime.Today.AddDays(-1);
            EnviosAgencia envio = ConEnvio(ayer);

            var resultado = await controller.RecibirRetorno(248001) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "925100");
            StringAssert.Contains(resultado.Message, ayer.ToString("dd/MM/yyyy"));
            Assert.AreEqual(ayer, envio.FechaRetornoRecibido);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RecibirRetorno_EnvioInexistente_Devuelve404()
        {
            ConUsuario("carlos");
            A.CallTo(() => fakeEnvios.FindAsync(999)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.RecibirRetorno(999);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public void RecibirRetorno_ContratoDelJson_NombresQueEsperaNesto()
        {
            // Si estos nombres cambian, Newtonsoft en Nesto deja la fecha por defecto (01/01/0001)
            // sin dar ningún error. Contrato espejo en AgenciaServiceRetornosTests (Nesto).
            string[] propiedades = typeof(RetornoRecibidoDTO).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "FechaRetornoRecibido", "Numero", "Pedido" }, propiedades);
        }
    }
}
