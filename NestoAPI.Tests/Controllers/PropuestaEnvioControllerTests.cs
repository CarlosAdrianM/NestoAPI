using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#595 (slice 1): GET api/EnviosAgencias/Propuesta y la sombra del POST api/EnviosAgencias (compara lo que
    /// graba Nesto con la propuesta del servidor; si difiere, PropuestaEnvioDifiereInfo a ELMAH; nunca rompe el POST).
    /// </summary>
    [TestClass]
    public class PropuestaEnvioControllerTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private IPropuestaEnvioService propuestas;
        private EnviosAgenciasController controller;
        private List<Exception> registrados;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.SaveChangesAsync()).Invokes(() => { }).Returns(Task.FromResult(1));
            A.CallTo(() => fakeEnvios.Add(A<EnviosAgencia>.Ignored)).Invokes((EnviosAgencia e) => e.Numero = 250001).ReturnsLazily((EnviosAgencia e) => e);

            propuestas = A.Fake<IPropuestaEnvioService>();
            registrados = new List<Exception>();
            controller = new EnviosAgenciasController(db)
            {
                PropuestaEnvio = propuestas,
                RegistrarEnElmah = ex => registrados.Add(ex)
            };
            controller.Request = new System.Net.Http.HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias") };
        }

        private static EnviosAgencia EnvioDeNesto(short estado = 0) => new EnviosAgencia
        {
            Empresa = "1  ", Agencia = Constantes.Agencias.AGENCIA_GLS, Cliente = "15191", Contacto = "0", Pedido = 927700, Estado = estado,
            Servicio = 96, Horario = 18, Retorno = 0, Bultos = 2, Peso = 3.5m, Reembolso = 120.50m,
            Nombre = "PELUQUERÍA LA BONITA", Direccion = "Calle Mayor, 1", CodPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID",
            Pais = 34, Telefono = "916281914", Movil = "600123456", Email = "agencia@bonita.es", Atencion = "PELUQUERÍA LA BONITA",
            Vendedor = "JE", ImporteGasto = 4.10m, Fecha = new DateTime(2026, 10, 6)
        };

        private static PropuestaEnvioDTO PropuestaIgualA(EnviosAgencia e) => new PropuestaEnvioDTO
        {
            Empresa = "1", Pedido = e.Pedido.Value, Origen = PropuestaEnvioDTO.ORIGEN_NUEVO, Agencia = e.Agencia, Servicio = e.Servicio,
            Horario = e.Horario, Retorno = e.Retorno, Bultos = e.Bultos, Peso = e.Peso, Reembolso = e.Reembolso, Nombre = e.Nombre,
            Direccion = e.Direccion, CodPostal = e.CodPostal, Poblacion = e.Poblacion, Provincia = e.Provincia, Pais = e.Pais,
            Telefono = e.Telefono, Movil = e.Movil, Email = e.Email, Atencion = e.Atencion, Vendedor = e.Vendedor, ImporteGasto = e.ImporteGasto
        };

        [TestMethod]
        public async Task Post_SombraQueDifiere_RegistraEnElmahConLosCamposYNoRompe()
        {
            EnviosAgencia envio = EnvioDeNesto();
            PropuestaEnvioDTO propuesta = PropuestaIgualA(envio);
            propuesta.Agencia = Constantes.Agencias.AGENCIA_CTT;
            propuesta.Servicio = 48;
            propuesta.Horario = 0;
            A.CallTo(() => propuestas.Calcular("1", 927700, (short?)2, (decimal?)3.5m, (int?)250001)).Returns(Task.FromResult(propuesta));

            var resultado = await controller.PostEnviosAgencia(envio);

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<EnviosAgencia>));
            Assert.AreEqual(1, registrados.Count);
            Assert.IsInstanceOfType(registrados[0], typeof(PropuestaEnvioDifiereInfo));
            StringAssert.Contains(registrados[0].Message, "Envío 250001 (pedido 927700)");
            StringAssert.Contains(registrados[0].Message, "agencia: Nesto=1 / propuesta=13");
            StringAssert.Contains(registrados[0].Message, "servicio: Nesto=96 / propuesta=48");
            StringAssert.Contains(registrados[0].Message, "horario: Nesto=18 / propuesta=0");
        }

        [TestMethod]
        public async Task Post_SombraQueCoincide_NoRegistraNada()
        {
            EnviosAgencia envio = EnvioDeNesto();
            A.CallTo(() => propuestas.Calcular(A<string>._, A<int>._, A<short?>._, A<decimal?>._, A<int?>._))
                .Returns(Task.FromResult(PropuestaIgualA(envio)));

            var resultado = await controller.PostEnviosAgencia(envio);

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<EnviosAgencia>));
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Post_SiLaSombraFalla_SeRegistraYElPostSigueBien()
        {
            A.CallTo(() => propuestas.Calcular(A<string>._, A<int>._, A<short?>._, A<decimal?>._, A<int?>._))
                .Throws(new InvalidOperationException("BD caída"));

            var resultado = await controller.PostEnviosAgencia(EnvioDeNesto());

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<EnviosAgencia>));
            Assert.AreEqual(1, registrados.Count);
            Assert.IsNotInstanceOfType(registrados[0], typeof(PropuestaEnvioDifiereInfo));
            StringAssert.Contains(registrados[0].Message, "BD caída");
        }

        [TestMethod]
        public async Task Post_SinPropuestaInyectada_LaSombraNoRompeElPost()
        {
            // Con la BD falsa, la propuesta real revienta al leer el pedido: el alta sigue y queda registrado.
            var real = new EnviosAgenciasController(db) { RegistrarEnElmah = ex => registrados.Add(ex) };
            real.Request = new System.Net.Http.HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias") };

            var resultado = await real.PostEnviosAgencia(EnvioDeNesto());

            Assert.IsInstanceOfType(resultado, typeof(CreatedAtRouteNegotiatedContentResult<EnviosAgencia>));
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public async Task Post_EtiquetaPendiente_NoHaceSombra()
        {
            _ = await controller.PostEnviosAgencia(EnvioDeNesto(estado: -1));

            A.CallTo(() => propuestas.Calcular(A<string>._, A<int>._, A<short?>._, A<decimal?>._, A<int?>._)).MustNotHaveHappened();
            Assert.AreEqual(0, registrados.Count);
        }

        [TestMethod]
        public async Task Post_LaPropuestaVeAmpliacion_SeRegistraElOrigen()
        {
            EnviosAgencia envio = EnvioDeNesto();
            PropuestaEnvioDTO propuesta = PropuestaIgualA(envio);
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_AMPLIACION;
            propuesta.EnvioOrigen = 249400;
            A.CallTo(() => propuestas.Calcular(A<string>._, A<int>._, A<short?>._, A<decimal?>._, A<int?>._)).Returns(Task.FromResult(propuesta));

            _ = await controller.PostEnviosAgencia(envio);

            Assert.AreEqual(1, registrados.Count);
            StringAssert.Contains(registrados[0].Message, "origen: Nesto=Nuevo / propuesta=Ampliacion 249400");
        }

        [TestMethod]
        public async Task GetPropuesta_PedidoInexistente_404()
        {
            A.CallTo(() => propuestas.Calcular("1", 1, null, null, null)).Returns(Task.FromResult<PropuestaEnvioDTO>(null));

            var resultado = await controller.GetPropuestaEnvio("1  ", 1);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GetPropuesta_DevuelveLaPropuesta()
        {
            var propuesta = new PropuestaEnvioDTO { Pedido = 927700, Agencia = 13, Origen = PropuestaEnvioDTO.ORIGEN_NUEVO };
            A.CallTo(() => propuestas.Calcular("1", 927700, (short?)2, (decimal?)1.5m, null)).Returns(Task.FromResult(propuesta));

            var resultado = await controller.GetPropuestaEnvio("1", 927700, 2, 1.5m) as OkNegotiatedContentResult<PropuestaEnvioDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreSame(propuesta, resultado.Content);
        }
    }
}
