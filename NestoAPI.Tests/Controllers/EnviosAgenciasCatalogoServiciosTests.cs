using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
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
    /// <summary>
    /// NestoAPI#546 (28/09/26): POST/PUT api/EnviosAgencias y la tramitación remota rechazan (400) un
    /// servicio, horario o retorno que no es de la agencia del envío (pendientes de GLS con el 48/0 de CTT).
    /// </summary>
    [TestClass]
    public class EnviosAgenciasCatalogoServiciosTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<AgenciaLlamadaWeb> fakeLlamadas;
        private IFabricaAgenciasRemotas fakeFabrica;
        private IAgenciaRemota fakeAgencia;
        private EnviosAgenciasController controller;
        private EnviosAgencia insertado;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeEnvios.Add(A<EnviosAgencia>.Ignored))
                .Invokes((EnviosAgencia e) => insertado = e)
                .ReturnsLazily((EnviosAgencia e) => e);

            fakeAgencia = A.Fake<IAgenciaRemota>();
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>());
            fakeFabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fakeFabrica.Crear(Constantes.Agencias.AGENCIA_CTT)).Returns(fakeAgencia);

            controller = new EnviosAgenciasController(db, fakeFabrica);
            controller.Request = new System.Net.Http.HttpRequestMessage
            {
                RequestUri = new System.Uri("http://localhost/api/EnviosAgencias")
            };
            insertado = null;
        }

        private static EnviosAgencia Envio(int agencia, short servicio, short horario, short retorno, short estado = -1) => new EnviosAgencia
        {
            Numero = 249165,
            Empresa = "1",
            Cliente = "29606",
            Agencia = agencia,
            Estado = estado,
            Servicio = servicio,
            Horario = horario,
            Retorno = retorno,
            Nombre = "CLIENTE",
            Direccion = "Calle Mayor 1",
            CodPostal = "28001",
            Poblacion = "MADRID",
            Reembolso = 0m
        };

        private void ConfigurarFakeDbSet(IQueryable<EnviosAgencia> data)
        {
            A.CallTo(() => ((IDbAsyncEnumerable<EnviosAgencia>)fakeEnvios).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<EnviosAgencia>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Provider)
                .Returns(new TestDbAsyncQueryProvider<EnviosAgencia>(data.Provider));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => fakeEnvios.AsNoTracking()).Returns(fakeEnvios);
        }

        // ---- POST ----

        [TestMethod]
        public async Task Post_GLSConServicio48Horario0_Devuelve400YNoInserta()
        {
            var resultado = await controller.PostEnviosAgencia(Envio(Constantes.Agencias.AGENCIA_GLS, 48, 0, 0));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "servicio 48");
            Assert.IsNull(insertado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_GLSValido_Inserta()
        {
            _ = await controller.PostEnviosAgencia(Envio(Constantes.Agencias.AGENCIA_GLS, 96, 18, 1));

            Assert.IsNotNull(insertado);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Post_CTTConServicioCeroSinElegir_Inserta()
        {
            _ = await controller.PostEnviosAgencia(Envio(Constantes.Agencias.AGENCIA_CTT, 0, 0, 0));

            Assert.IsNotNull(insertado, "El servicio 0 de CTT se resuelve al imprimir: se sigue admitiendo");
        }

        [TestMethod]
        public async Task Post_AgenciaSinCatalogo_NoSeValida()
        {
            _ = await controller.PostEnviosAgencia(Envio(Constantes.Agencias.AGENCIA_SENDING, 48, 0, 7));

            Assert.IsNotNull(insertado, "Sending (histórica) no tiene catálogo: no se valida");
        }

        // ---- PUT ----

        [TestMethod]
        public async Task Put_PendienteGLSConServicioDeCTT_Devuelve400()
        {
            // Lo que había en BD: el pendiente ya estropeado (249124). Guardarlo tal cual no se deja.
            ConfigurarFakeDbSet(new List<EnviosAgencia> { Envio(Constantes.Agencias.AGENCIA_GLS, 48, 0, 0) }.AsQueryable());

            var resultado = await controller.PutEnviosAgencia(249165, Envio(Constantes.Agencias.AGENCIA_GLS, 48, 0, 0));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "GLS");
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_PendienteGLSQuePasaAServicioDeCTT_Devuelve400()
        {
            // El fallo del 28/09: el pendiente estaba bien (96/18/1) y la respuesta tardía del comparador le puso 48/0/0.
            ConfigurarFakeDbSet(new List<EnviosAgencia> { Envio(Constantes.Agencias.AGENCIA_GLS, 96, 18, 1) }.AsQueryable());

            var resultado = await controller.PutEnviosAgencia(249165, Envio(Constantes.Agencias.AGENCIA_GLS, 48, 0, 0));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Put_TramitadoQueCambiaAHorarioDeOtraAgencia_Devuelve400()
        {
            ConfigurarFakeDbSet(new List<EnviosAgencia> { Envio(Constantes.Agencias.AGENCIA_GLS, 96, 18, 0, estado: 1) }.AsQueryable());

            var resultado = await controller.PutEnviosAgencia(249165, Envio(Constantes.Agencias.AGENCIA_GLS, 96, 0, 0, estado: 1));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        // ---- Tramitación remota ----

        [TestMethod]
        public async Task Tramitar_CTTConServicioDeGLS_Devuelve400SinLlamarALaAgencia()
        {
            EnviosAgencia envio = Envio(Constantes.Agencias.AGENCIA_CTT, 96, 18, 0);
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));

            var resultado = await controller.TramitarEnvio(envio.Numero);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_CTTConServicioCero_LlamaALaAgencia()
        {
            EnviosAgencia envio = Envio(Constantes.Agencias.AGENCIA_CTT, 0, 0, 0);
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored))
                .Returns(Task.FromResult(new ResultadoTramitacionRemota { Exito = false, Error = "rechazado en la prueba" }));

            _ = await controller.TramitarEnvio(envio.Numero);

            A.CallTo(() => fakeAgencia.InsertarYEtiquetarAsync(A<DatosEnvioRemoto>.Ignored)).MustHaveHappenedOnceExactly();
        }
    }
}
