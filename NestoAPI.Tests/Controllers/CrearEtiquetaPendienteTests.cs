using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class CrearEtiquetaPendienteTests
    {
        private NVEntities db;
        private EnviosAgenciasController controller;
        private DbSet<CabPedidoVta> fakePedidos;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<Cliente> fakeClientes;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakePedidos = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeClientes = A.Fake<DbSet<Cliente>>(o => o.Implements<IQueryable<Cliente>>().Implements<IDbAsyncEnumerable<Cliente>>());

            A.CallTo(() => db.CabPedidoVtas).Returns(fakePedidos);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.Clientes).Returns(fakeClientes);

            A.CallTo(() => fakePedidos.Include(A<string>.Ignored)).Returns(fakePedidos);

            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta>().AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>().AsQueryable());
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>().AsQueryable());

            controller = new EnviosAgenciasController(db);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_DatosValidos_CreaConEstadoPendienteYDireccionContacto()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1  ",
                Número = 12345,
                Nº_Cliente = "10000",
                Contacto = "0  ",
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var direccion = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "10000",
                Contacto = "0  ",
                Nombre = "Cliente Test",
                Dirección = "Calle Mayor 1",
                CodPostal = "28001",
                Población = "Madrid",
                Provincia = "Madrid",
                Teléfono = "911234567"
            };
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente> { direccion }.AsQueryable());

            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>().AsQueryable());

            EnviosAgencia envioCreado = null;
            A.CallTo(() => fakeEnvios.Add(A<EnviosAgencia>.Ignored))
                .Invokes((EnviosAgencia e) => envioCreado = e)
                .ReturnsLazily((EnviosAgencia e) => e);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var request = new CrearEtiquetaPendienteDTO
            {
                Empresa = "1  ",
                Pedido = 12345,
                Agencia = 3,
                Retorno = 1
            };

            // Act
            var resultado = await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.IsNotNull(envioCreado);
            Assert.AreEqual((short)-1, envioCreado.Estado);
            Assert.AreEqual((short)1, envioCreado.Retorno);
            Assert.AreEqual(3, envioCreado.Agencia);
            Assert.AreEqual("10000", envioCreado.Cliente);
            Assert.AreEqual("Cliente Test", envioCreado.Nombre);
            Assert.AreEqual("Calle Mayor 1", envioCreado.Direccion);
            Assert.AreEqual("28001", envioCreado.CodPostal);
            Assert.AreEqual("Madrid", envioCreado.Poblacion);
            Assert.AreEqual("Madrid", envioCreado.Provincia);
            Assert.AreEqual("911234567", envioCreado.Telefono);
            Assert.AreEqual(0, envioCreado.Reembolso);
            Assert.AreEqual((short)1, envioCreado.Bultos);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_PedidoNoExiste_Retorna404()
        {
            // Arrange
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta>().AsQueryable());

            var request = new CrearEtiquetaPendienteDTO
            {
                Empresa = "1  ",
                Pedido = 99999,
                Agencia = 3,
                Retorno = 1
            };

            // Act
            var resultado = await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_YaExisteEtiquetaPendiente_Retorna409Conflict()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1  ",
                Número = 12345,
                Nº_Cliente = "10000",
                Contacto = "0  ",
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var envioExistente = new EnviosAgencia
            {
                Empresa = "1  ",
                Pedido = 12345,
                Estado = -1
            };
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia> { envioExistente }.AsQueryable());

            var request = new CrearEtiquetaPendienteDTO
            {
                Empresa = "1  ",
                Pedido = 12345,
                Agencia = 3,
                Retorno = 1
            };

            // Act
            var resultado = await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(ConflictResult));
        }

        [TestMethod]
        public async Task DeleteEnviosAgencia_EtiquetaPendiente_LaElimina()
        {
            // Arrange
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Estado = -1,
                Empresa = "1  "
            };
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult(envio));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            // Act
            var resultado = await controller.DeleteEnviosAgencia(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnviosAgencia>));
            A.CallTo(() => fakeEnvios.Remove(envio)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeleteEnviosAgencia_EtiquetaEnCurso_RetornaBadRequest()
        {
            // Arrange
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Estado = 0,
                Empresa = "1  "
            };
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult(envio));

            // Act
            var resultado = await controller.DeleteEnviosAgencia(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task DeleteEnviosAgencia_NoExiste_Retorna404()
        {
            // Arrange
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult((EnviosAgencia)null));

            // Act
            var resultado = await controller.DeleteEnviosAgencia(1);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        #region Helpers

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }

        #endregion
    }
}
