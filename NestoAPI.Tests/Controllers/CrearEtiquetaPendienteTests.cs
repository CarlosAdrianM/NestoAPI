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
            A.CallTo(() => fakeClientes.Include(A<string>.Ignored)).Returns(fakeClientes);

            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta>().AsQueryable());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>().AsQueryable());
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>().AsQueryable());

            controller = new EnviosAgenciasController(db);
            controller.Request = new System.Net.Http.HttpRequestMessage
            {
                RequestUri = new System.Uri("http://localhost/api/EnviosAgencias/CrearEtiquetaPendiente")
            };
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
                Teléfono = "911234567/654987321",
                PersonasContactoClientes = new List<PersonaContactoCliente>
                {
                    new PersonaContactoCliente { Cargo = 26, CorreoElectrónico = "agencia@test.com" }
                }
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
            Assert.IsInstanceOfType(resultado, typeof(CreatedNegotiatedContentResult<EnvioAgenciaDTO>));
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
            Assert.AreEqual("654987321", envioCreado.Movil);
            Assert.AreEqual("agencia@test.com", envioCreado.Email);
            Assert.AreEqual("Cliente Test", envioCreado.Atencion);
            Assert.AreEqual(0, envioCreado.Reembolso);
            Assert.AreEqual((short)0, envioCreado.Bultos);
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

        [TestMethod]
        public async Task CrearEtiquetaPendiente_CobrarReembolsoFalse_UsaSentinelNegativo()
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
                Nombre = "Test",
                PersonasContactoClientes = new List<PersonaContactoCliente>()
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
                Retorno = 1,
                CobrarReembolso = false
            };

            // Act
            await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsNotNull(envioCreado);
            Assert.AreEqual(Constantes.Agencias.REEMBOLSO_NO_COBRAR, envioCreado.Reembolso);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_ImporteReembolsoFijado_UsaImporteFijado()
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
                Nombre = "Test",
                PersonasContactoClientes = new List<PersonaContactoCliente>()
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
                Retorno = 1,
                CobrarReembolso = true,
                ImporteReembolso = 150.50M
            };

            // Act
            await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsNotNull(envioCreado);
            Assert.AreEqual(150.50M, envioCreado.Reembolso);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_SinEspecificarReembolso_DefaultAutoCalc()
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
                Nombre = "Test",
                PersonasContactoClientes = new List<PersonaContactoCliente>()
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
            await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsNotNull(envioCreado);
            Assert.AreEqual(0M, envioCreado.Reembolso);
        }

        [TestMethod]
        public async Task ActualizarDireccionEtiqueta_EtiquetaPendiente_ActualizaDireccion()
        {
            // Arrange
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Empresa = "1  ",
                Pedido = 12345,
                Estado = -1,
                Contacto = "0  ",
                Nombre = "Direccion Antigua",
                Direccion = "Calle Vieja 1"
            };
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult(envio));

            var nuevaDireccion = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "10000",
                Contacto = "1  ",
                Nombre = "Nueva Direccion",
                Dirección = "Calle Nueva 5",
                CodPostal = "28002",
                Población = "Barcelona",
                Provincia = "Barcelona",
                Teléfono = "934567890/612345678",
                PersonasContactoClientes = new List<PersonaContactoCliente>
                {
                    new PersonaContactoCliente { Cargo = 26, CorreoElectrónico = "nueva@test.com" }
                }
            };
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente> { nuevaDireccion }.AsQueryable());

            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var request = new ActualizarDireccionEtiquetaDTO
            {
                Empresa = "1  ",
                Cliente = "10000",
                Contacto = "1  "
            };

            // Act
            var resultado = await controller.ActualizarDireccionEtiqueta(1, request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<EnvioAgenciaDTO>));
            Assert.AreEqual("1  ", envio.Contacto);
            Assert.AreEqual("Nueva Direccion", envio.Nombre);
            Assert.AreEqual("Calle Nueva 5", envio.Direccion);
            Assert.AreEqual("28002", envio.CodPostal);
            Assert.AreEqual("Barcelona", envio.Poblacion);
            Assert.AreEqual("934567890", envio.Telefono);
            Assert.AreEqual("612345678", envio.Movil);
            Assert.AreEqual("nueva@test.com", envio.Email);
        }

        [TestMethod]
        public async Task ActualizarDireccionEtiqueta_EtiquetaEnCurso_RetornaBadRequest()
        {
            // Arrange
            var envio = new EnviosAgencia
            {
                Numero = 1,
                Empresa = "1  ",
                Estado = 0
            };
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult(envio));

            var request = new ActualizarDireccionEtiquetaDTO
            {
                Empresa = "1  ",
                Cliente = "10000",
                Contacto = "1  "
            };

            // Act
            var resultado = await controller.ActualizarDireccionEtiqueta(1, request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task ActualizarDireccionEtiqueta_NoExiste_Retorna404()
        {
            // Arrange
            A.CallTo(() => fakeEnvios.FindAsync(1)).Returns(Task.FromResult((EnviosAgencia)null));

            var request = new ActualizarDireccionEtiquetaDTO
            {
                Empresa = "1  ",
                Cliente = "10000",
                Contacto = "1  "
            };

            // Act
            var resultado = await controller.ActualizarDireccionEtiqueta(1, request);

            // Assert
            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public void GuardaReembolso_EstadoEnCursoConReembolsoNegativo_ConvierteACero()
        {
            // La guarda del PUT: si Estado >= EN_CURSO y Reembolso < 0, convierte a 0
            var envio = new EnviosAgencia { Estado = 0, Reembolso = Constantes.Agencias.REEMBOLSO_NO_COBRAR };

            // Act - simula la guarda del PUT
            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && envio.Reembolso < 0)
            {
                envio.Reembolso = 0;
            }

            // Assert
            Assert.AreEqual(0M, envio.Reembolso);
        }

        [TestMethod]
        public void GuardaReembolso_EstadoPendienteConReembolsoNegativo_MantieneNegativo()
        {
            var envio = new EnviosAgencia { Estado = -1, Reembolso = Constantes.Agencias.REEMBOLSO_NO_COBRAR };

            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && envio.Reembolso < 0)
            {
                envio.Reembolso = 0;
            }

            Assert.AreEqual(Constantes.Agencias.REEMBOLSO_NO_COBRAR, envio.Reembolso);
        }

        [TestMethod]
        public void GuardaReembolso_EstadoEnCursoConReembolsoPositivo_MantienePositivo()
        {
            var envio = new EnviosAgencia { Estado = 0, Reembolso = 150.50M };

            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && envio.Reembolso < 0)
            {
                envio.Reembolso = 0;
            }

            Assert.AreEqual(150.50M, envio.Reembolso);
        }

        [TestMethod]
        public void GuardaReembolso_EstadoEnCursoConReembolsoCero_MantieneCero()
        {
            // Reembolso = 0 (auto-calc) no debe convertirse
            var envio = new EnviosAgencia { Estado = 0, Reembolso = 0M };

            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && envio.Reembolso < 0)
            {
                envio.Reembolso = 0;
            }

            Assert.AreEqual(0M, envio.Reembolso);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_AgenciaGLS_DefaultsBusinessParcelEconomy()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_GLS, "28001");

            // Assert
            Assert.AreEqual((short)96, envioCreado.Servicio); // BusinessParcel
            Assert.AreEqual((short)18, envioCreado.Horario);  // Economy
            Assert.AreEqual(34, envioCreado.Pais);             // España
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_AgenciaCEX_CodigoPostalEspanol_DefaultsEpaq24()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, "28001");

            // Assert
            Assert.AreEqual((short)93, envioCreado.Servicio); // ePaq24
            Assert.AreEqual((short)0, envioCreado.Horario);
            Assert.AreEqual(724, envioCreado.Pais);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_AgenciaCEX_CodigoPostalPortugues_DefaultsPaq24()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, "1000-001");

            // Assert
            Assert.AreEqual((short)63, envioCreado.Servicio); // Paq24 Portugal
            Assert.AreEqual((short)0, envioCreado.Horario);
            Assert.AreEqual(724, envioCreado.Pais);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_AgenciaCEX_CodigoPostalInternacional_DefaultsMonobulto()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS, "75008");

            // Assert
            Assert.AreEqual((short)90, envioCreado.Servicio); // Internacional monobulto
            Assert.AreEqual((short)0, envioCreado.Horario);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_AgenciaSending_DefaultsExpressNormal()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_SENDING, "28001");

            // Assert
            Assert.AreEqual((short)1, envioCreado.Servicio); // Send Express
            Assert.AreEqual((short)1, envioCreado.Horario);  // Normal
            Assert.AreEqual(34, envioCreado.Pais);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_FechaEntrega_UsaFechaPedido()
        {
            // Arrange
            var fechaPedido = new System.DateTime(2026, 3, 15);
            var pedido = new CabPedidoVta
            {
                Empresa = "1  ",
                Número = 12345,
                Nº_Cliente = "10000",
                Contacto = "0  ",
                Fecha = fechaPedido,
                LinPedidoVtas = new List<LinPedidoVta>()
            };
            ConfigurarFakeDbSet(fakePedidos, new List<CabPedidoVta> { pedido }.AsQueryable());

            var direccion = new Cliente
            {
                Empresa = "1  ",
                Nº_Cliente = "10000",
                Contacto = "0  ",
                Nombre = "Test",
                CodPostal = "28001",
                PersonasContactoClientes = new List<PersonaContactoCliente>()
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
                Agencia = 1,
                Retorno = 1
            };

            // Act
            await controller.CrearEtiquetaPendiente(request);

            // Assert
            Assert.IsNotNull(envioCreado);
            Assert.AreEqual(fechaPedido, envioCreado.FechaEntrega);
        }

        [TestMethod]
        public async Task CrearEtiquetaPendiente_Bultos_SiempreCero()
        {
            // Arrange
            var envioCreado = await CrearEtiquetaConAgenciaYCodPostal(Constantes.Agencias.AGENCIA_GLS, "28001");

            // Assert
            Assert.AreEqual((short)0, envioCreado.Bultos);
        }

        private async Task<EnviosAgencia> CrearEtiquetaConAgenciaYCodPostal(int agencia, string codPostal)
        {
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
                Nombre = "Test",
                CodPostal = codPostal,
                PersonasContactoClientes = new List<PersonaContactoCliente>()
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
                Agencia = agencia,
                Retorno = 1
            };

            await controller.CrearEtiquetaPendiente(request);
            Assert.IsNotNull(envioCreado, "No se creó el envío");
            return envioCreado;
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
