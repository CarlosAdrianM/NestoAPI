using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#847 (sugerencia 1103, Alfredo, 06/11/26): la etiqueta del envío 250662 (pedido 929042, cliente 1179) no salió,
    /// se pulsó otra vez «Insertar e Imprimir» y quedaron tres envíos del reparto propio (250662, 250663 y 250664) para el mismo
    /// pedido. El POST ya no crea otro: 409 con el envío que tiene el pedido, para reimprimirlo.
    /// </summary>
    [TestClass]
    public class EnvioRepartoPropioRepetidoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 6);
        private const int PEDIDO = 929042;

        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private EnviosAgenciasController controller;
        private EnviosAgencia insertado;
        private List<EnviosAgencia> existentes;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeEnvios.Add(A<EnviosAgencia>.Ignored))
                .Invokes((EnviosAgencia e) => insertado = e)
                .ReturnsLazily((EnviosAgencia e) => e);
            existentes = new List<EnviosAgencia>();
            A.CallTo(() => ((IDbAsyncEnumerable<EnviosAgencia>)fakeEnvios).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<EnviosAgencia>(existentes.GetEnumerator()));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<EnviosAgencia>(existentes.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).Expression).ReturnsLazily(() => existentes.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).ElementType).Returns(typeof(EnviosAgencia));
            A.CallTo(() => ((IQueryable<EnviosAgencia>)fakeEnvios).GetEnumerator()).ReturnsLazily(() => existentes.GetEnumerator());
            A.CallTo(() => fakeEnvios.AsNoTracking()).Returns(fakeEnvios);

            controller = new EnviosAgenciasController(db, A.Fake<IFabricaAgenciasRemotas>())
            {
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                HoyRepartoPropio = () => HOY
            };
            controller.Request = new System.Net.Http.HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias") };
            insertado = null;
        }

        private static EnviosAgencia Envio(int numero, int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, short estado = 0,
            DateTime? fecha = null, int pedido = PEDIDO, string empresa = "1  ") => new EnviosAgencia
            {
                Numero = numero,
                Empresa = empresa,
                Cliente = "1179      ",
                Contacto = "0  ",
                Pedido = pedido,
                Agencia = agencia,
                Estado = estado,
                Fecha = fecha ?? HOY,
                Servicio = 1,
                Horario = 0,
                Retorno = 0,
                Bultos = 1,
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodPostal = "28905",
                Poblacion = "GETAFE",
                Reembolso = 212.08m,
                CodigoBarras = estado >= 1 ? "RP" + numero : null
            };

        private static EnviosAgencia Nuevo() => Envio(0);

        [TestMethod]
        public async Task Post_PedidoConEnvioTramitadoHoy_409ConElEnvioYNoInserta()
        {
            existentes.Add(Envio(250662, estado: Constantes.Agencias.ESTADO_TRAMITADO));

            var resultado = await controller.PostEnviosAgencia(Nuevo());

            var conflicto = resultado as NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>;
            Assert.IsNotNull(conflicto, "409 con el envío para reimprimir");
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreEqual(250662, conflicto.Content.Envio);
            Assert.AreEqual("RP250662", conflicto.Content.CodigoBarras);
            Assert.AreEqual(EnvioRepartoPropioRepetido.CODIGO, conflicto.Content.Codigo);
            StringAssert.Contains(conflicto.Content.Message, "ya tiene el envío 250662");
            Assert.IsNull(insertado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_PedidoConEnvioEnCursoSinTramitar_409()
        {
            existentes.Add(Envio(250662, estado: Constantes.Agencias.ESTADO_EN_CURSO));

            var resultado = await controller.PostEnviosAgencia(Nuevo());

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
            Assert.IsNull(insertado);
        }

        [TestMethod]
        public async Task Post_EnvioTramitadoConElDiaDeRepartoDeMannana_409()
        {
            // El Tramitar del reparto propio le pone la fecha del día de reparto (#831), que puede ser mañana.
            existentes.Add(Envio(250662, estado: Constantes.Agencias.ESTADO_TRAMITADO, fecha: HOY.AddDays(1)));

            var resultado = await controller.PostEnviosAgencia(Nuevo());

            Assert.IsInstanceOfType(resultado, typeof(NegotiatedContentResult<EnvioRepartoPropioRepetidoDTO>));
        }

        [TestMethod]
        public async Task Post_EnvioTramitadoDeOtroDiaEntregadoOPendiente_Inserta()
        {
            existentes.Add(Envio(250500, estado: Constantes.Agencias.ESTADO_TRAMITADO, fecha: HOY.AddDays(-3)));
            existentes.Add(Envio(250600, estado: Constantes.Agencias.ESTADO_ENTREGADO));
            existentes.Add(Envio(250601, estado: Constantes.Agencias.ESTADO_PENDIENTE));

            _ = await controller.PostEnviosAgencia(Nuevo());

            Assert.IsNotNull(insertado, "tramitado de otro día, entregado o etiqueta pendiente: se crea el envío como siempre");
        }

        [TestMethod]
        public async Task Post_EnvioDeOtroPedidoOEmpresaODeOtraAgencia_Inserta()
        {
            existentes.Add(Envio(250662, estado: Constantes.Agencias.ESTADO_TRAMITADO, pedido: PEDIDO + 1));
            existentes.Add(Envio(250663, estado: Constantes.Agencias.ESTADO_TRAMITADO, empresa: "3  "));
            existentes.Add(Envio(250664, agencia: Constantes.Agencias.AGENCIA_CTT, estado: Constantes.Agencias.ESTADO_TRAMITADO));

            _ = await controller.PostEnviosAgencia(Nuevo());

            Assert.IsNotNull(insertado);
        }

        [TestMethod]
        public async Task Post_OtraAgencia_NoSeMira()
        {
            existentes.Add(Envio(250662, agencia: Constantes.Agencias.AGENCIA_CTT, estado: Constantes.Agencias.ESTADO_TRAMITADO));

            EnviosAgencia nuevoCtt = Envio(0, agencia: Constantes.Agencias.AGENCIA_CTT);
            nuevoCtt.Servicio = 0; // el de CTT sin elegir (catálogo de #546)

            _ = await controller.PostEnviosAgencia(nuevoCtt);

            Assert.IsNotNull(insertado, "la regla es solo del reparto propio");
        }

        [TestMethod]
        public void Buscar_ConVarios_ElMasAntiguo()
        {
            var candidatos = new List<EnviosAgencia>
            {
                Envio(250664, estado: Constantes.Agencias.ESTADO_TRAMITADO),
                Envio(250662, estado: Constantes.Agencias.ESTADO_TRAMITADO)
            };

            Assert.AreEqual(250662, EnvioRepartoPropioRepetido.Buscar(candidatos, "1", PEDIDO, HOY).Numero);
        }

        [TestMethod]
        public void AplicaA_EtiquetaPendienteOSinPedido_No()
        {
            Assert.IsFalse(EnvioRepartoPropioRepetido.AplicaA(Envio(0, estado: Constantes.Agencias.ESTADO_PENDIENTE)));
            EnviosAgencia sinPedido = Nuevo();
            sinPedido.Pedido = null;
            Assert.IsFalse(EnvioRepartoPropioRepetido.AplicaA(sinPedido));
            Assert.IsTrue(EnvioRepartoPropioRepetido.AplicaA(Nuevo()));
        }
    }
}
