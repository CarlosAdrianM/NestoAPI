using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Tests.Infrastructure.CanalesExternos.Amazon
{
    [TestClass]
    public class ServicioRotacionCredencialesAmazonTests
    {
        private const string CLIENT_ID = "amzn1.application-oa2-client.test";
        private static readonly DateTime AHORA = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        private IAmazonSpApiGateway _gateway;
        private IAmazonCredencialStore _store;

        [TestInitialize]
        public void Init()
        {
            _gateway = A.Fake<IAmazonSpApiGateway>();
            _store = A.Fake<IAmazonCredencialStore>();
            A.CallTo(() => _gateway.RecibirMensajesColaAsync())
                .Returns(Task.FromResult<IReadOnlyList<AmazonSqsMessage>>(new List<AmazonSqsMessage>()));
            A.CallTo(() => _gateway.ObtenerTokenRotacionAsync(A<string>._, A<string>._))
                .Returns(Task.FromResult("token-rotacion"));
        }

        private ServicioRotacionCredencialesAmazon CrearServicio(int diasAntes = 15)
        {
            return new ServicioRotacionCredencialesAmazon(_gateway, _store, diasAntes, () => AHORA);
        }

        private static AmazonSpApiCredencial Credencial(DateTime? secretExpiry, DateTime? oldExpiry = null)
        {
            return new AmazonSpApiCredencial
            {
                Id = 1,
                ClientId = CLIENT_ID,
                ClientSecret = "secreto-actual",
                RefreshToken = "refresh",
                SecretExpiry = secretExpiry,
                OldSecretExpiry = oldExpiry
            };
        }

        private static string JsonNuevoSecreto(string clientId, string nuevoSecreto)
        {
            return "{\"notificationType\":\"APPLICATION_OAUTH_CLIENT_NEW_SECRET\"," +
                   "\"payload\":{\"applicationOAuthClientNewSecret\":{" +
                   "\"clientId\":\"" + clientId + "\"," +
                   "\"newClientSecret\":\"" + nuevoSecreto + "\"," +
                   "\"newClientSecretExpiryTime\":\"2027-05-01T10:00:00Z\"," +
                   "\"oldClientSecretExpiryTime\":\"2026-11-08T10:00:00Z\"}}}";
        }

        [TestMethod]
        public async Task ProcesarColaAsync_SinCredencialEnBd_NoHaceNada()
        {
            A.CallTo(() => _store.Obtener()).Returns(null);

            ResultadoProcesoRotacion r = await CrearServicio().ProcesarColaAsync();

            Assert.IsFalse(r.RotacionDisparada);
            Assert.AreEqual(0, r.SecretosPersistidos);
            A.CallTo(() => _gateway.RecibirMensajesColaAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_NuevoSecretoCoincideClientId_PersisteYBorra()
        {
            // SecretExpiry lejano para que NO rote tras persistir
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(120)));
            A.CallTo(() => _gateway.RecibirMensajesColaAsync())
                .Returns(Task.FromResult<IReadOnlyList<AmazonSqsMessage>>(new List<AmazonSqsMessage>
                {
                    new AmazonSqsMessage { Body = JsonNuevoSecreto(CLIENT_ID, "SECRETO-NUEVO"), ReceiptHandle = "rh-1" }
                }));

            ResultadoProcesoRotacion r = await CrearServicio().ProcesarColaAsync();

            Assert.AreEqual(1, r.SecretosPersistidos);
            Assert.IsFalse(r.RotacionDisparada);
            A.CallTo(() => _store.GuardarSecretoNuevo(CLIENT_ID, "SECRETO-NUEVO",
                A<DateTime?>._, A<DateTime?>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => _gateway.BorrarMensajeColaAsync("rh-1")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_NuevoSecretoOtroClientId_NoPersiste()
        {
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(120)));
            A.CallTo(() => _gateway.RecibirMensajesColaAsync())
                .Returns(Task.FromResult<IReadOnlyList<AmazonSqsMessage>>(new List<AmazonSqsMessage>
                {
                    new AmazonSqsMessage { Body = JsonNuevoSecreto("OTRO-CLIENT-ID", "X"), ReceiptHandle = "rh-2" }
                }));

            ResultadoProcesoRotacion r = await CrearServicio().ProcesarColaAsync();

            Assert.AreEqual(0, r.SecretosPersistidos);
            A.CallTo(() => _store.GuardarSecretoNuevo(A<string>._, A<string>._, A<DateTime?>._, A<DateTime?>._, A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_SecretoDentroDeUmbral_Rota()
        {
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(10))); // 10 < 15 días

            ResultadoProcesoRotacion r = await CrearServicio(15).ProcesarColaAsync();

            Assert.IsTrue(r.RotacionDisparada);
            A.CallTo(() => _gateway.ObtenerTokenRotacionAsync(CLIENT_ID, "secreto-actual")).MustHaveHappenedOnceExactly();
            A.CallTo(() => _gateway.RotarClientSecretAsync("token-rotacion")).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.MarcarRotacionSolicitada(CLIENT_ID)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_RotacionPendiente_NoRotaDeNuevo()
        {
            // Cerca de caducar pero con OldSecretExpiry futuro => rotación ya solicitada (guard)
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(10), AHORA.AddDays(5)));

            ResultadoProcesoRotacion r = await CrearServicio(15).ProcesarColaAsync();

            Assert.IsFalse(r.RotacionDisparada);
            A.CallTo(() => _gateway.RotarClientSecretAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_SecretoLejosDeCaducar_NoRota()
        {
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(120)));

            ResultadoProcesoRotacion r = await CrearServicio(15).ProcesarColaAsync();

            Assert.IsFalse(r.RotacionDisparada);
            A.CallTo(() => _gateway.RotarClientSecretAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ProcesarColaAsync_SinSecretExpiry_NoRota()
        {
            A.CallTo(() => _store.Obtener()).Returns(Credencial(null));

            ResultadoProcesoRotacion r = await CrearServicio().ProcesarColaAsync();

            Assert.IsFalse(r.RotacionDisparada);
            A.CallTo(() => _gateway.RotarClientSecretAsync(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RotarAhoraAsync_ConCredencial_RotaYMarca()
        {
            A.CallTo(() => _store.Obtener()).Returns(Credencial(AHORA.AddDays(120)));

            bool rotó = await CrearServicio().RotarAhoraAsync();

            Assert.IsTrue(rotó);
            A.CallTo(() => _gateway.RotarClientSecretAsync("token-rotacion")).MustHaveHappenedOnceExactly();
            A.CallTo(() => _store.MarcarRotacionSolicitada(CLIENT_ID)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task RotarAhoraAsync_SinCredencial_DevuelveFalse()
        {
            A.CallTo(() => _store.Obtener()).Returns(null);

            bool rotó = await CrearServicio().RotarAhoraAsync();

            Assert.IsFalse(rotó);
            A.CallTo(() => _gateway.RotarClientSecretAsync(A<string>._)).MustNotHaveHappened();
        }
    }
}
