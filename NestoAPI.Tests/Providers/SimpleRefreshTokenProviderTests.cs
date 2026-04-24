using FakeItEasy;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Providers;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Providers
{
    // NestoAPI#188: tests del SimpleRefreshTokenProvider.
    // Cubre el contrato del IAuthenticationTokenProvider de OWIN sin tocar BD (fake del store).
    [TestClass]
    public class SimpleRefreshTokenProviderTests
    {
        private const string UserName = "Javier";
        private static readonly TimeSpan Ttl = TimeSpan.FromDays(90);

        private IRefreshTokenStore _store;
        private ISecureDataFormat<AuthenticationTicket> _formato;
        private IOwinContext _owin;
        private SimpleRefreshTokenProvider _provider;

        [TestInitialize]
        public void Setup()
        {
            _store = A.Fake<IRefreshTokenStore>();
            _formato = A.Fake<ISecureDataFormat<AuthenticationTicket>>();
            _owin = A.Fake<IOwinContext>();

            A.CallTo(() => _formato.Protect(A<AuthenticationTicket>._)).Returns("ticket-protegido");

            _provider = new SimpleRefreshTokenProvider(() => _store, Ttl);
        }

        [TestMethod]
        public async Task CreateAsync_persiste_hash_y_devuelve_secret_en_claro()
        {
            RefreshToken capturado = null;
            A.CallTo(() => _store.AddAsync(A<RefreshToken>._))
                .Invokes(call => capturado = call.GetArgument<RefreshToken>(0))
                .Returns(Task.FromResult(0));

            AuthenticationTokenCreateContext contexto = CrearContextoCreate(UserName);

            await _provider.CreateAsync(contexto);

            Assert.IsNotNull(capturado, "El provider debe llamar a AddAsync");
            Assert.AreEqual(UserName, capturado.UserName);
            Assert.AreEqual("NestoApp", capturado.ClientId);
            Assert.AreEqual(64, capturado.Id.Length, "El Id almacenado es SHA-256 en hex (64 chars)");
            Assert.AreEqual("ticket-protegido", capturado.ProtectedTicket);
            Assert.IsFalse(string.IsNullOrEmpty(contexto.Token), "El cliente recibe el secret en claro");
            Assert.AreNotEqual(contexto.Token, capturado.Id, "En BD guardamos el hash, no el secret");
            Assert.AreEqual(SimpleRefreshTokenProvider.HashToken(contexto.Token), capturado.Id);
        }

        [TestMethod]
        public async Task CreateAsync_usa_el_TTL_inyectado()
        {
            RefreshToken capturado = null;
            A.CallTo(() => _store.AddAsync(A<RefreshToken>._))
                .Invokes(call => capturado = call.GetArgument<RefreshToken>(0))
                .Returns(Task.FromResult(0));

            DateTime antes = DateTime.UtcNow;
            await _provider.CreateAsync(CrearContextoCreate(UserName));
            DateTime despues = DateTime.UtcNow;

            TimeSpan vida = capturado.ExpiresUtc - capturado.IssuedUtc;
            Assert.AreEqual(Ttl, vida, "ExpiresUtc - IssuedUtc debe ser el TTL configurado");
            Assert.IsTrue(capturado.IssuedUtc >= antes && capturado.IssuedUtc <= despues);
        }

        [TestMethod]
        public async Task ReceiveAsync_con_token_valido_deserializa_y_marca_revocado()
        {
            string secret = "un-secret-cualquiera";
            string hash = SimpleRefreshTokenProvider.HashToken(secret);
            RefreshToken enBd = TokenVigente(hash);

            A.CallTo(() => _store.FindAsync(hash)).Returns(Task.FromResult(enBd));
            A.CallTo(() => _formato.Unprotect("ticket-protegido")).Returns(TicketConIdentidad(UserName));

            AuthenticationTokenReceiveContext contexto = CrearContextoReceive(secret);
            await _provider.ReceiveAsync(contexto);

            Assert.IsNotNull(contexto.Ticket, "Debe establecer el ticket cuando el token es válido");
            Assert.AreEqual(UserName, contexto.Ticket.Identity.Name);
            Assert.IsTrue(enBd.RevokedUtc.HasValue, "Rotación: el token usado queda revocado");
            A.CallTo(() => _store.SaveChangesAsync()).MustHaveHappened();
        }

        [TestMethod]
        public async Task ReceiveAsync_con_token_desconocido_no_establece_ticket()
        {
            A.CallTo(() => _store.FindAsync(A<string>._)).Returns(Task.FromResult<RefreshToken>(null));

            AuthenticationTokenReceiveContext contexto = CrearContextoReceive("desconocido");
            await _provider.ReceiveAsync(contexto);

            Assert.IsNull(contexto.Ticket, "Sin ticket → OWIN devuelve invalid_grant");
            A.CallTo(() => _store.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ReceiveAsync_con_token_revocado_no_establece_ticket()
        {
            string secret = "x";
            string hash = SimpleRefreshTokenProvider.HashToken(secret);
            RefreshToken revocado = TokenVigente(hash);
            revocado.RevokedUtc = DateTime.UtcNow.AddMinutes(-1);

            A.CallTo(() => _store.FindAsync(hash)).Returns(Task.FromResult(revocado));

            AuthenticationTokenReceiveContext contexto = CrearContextoReceive(secret);
            await _provider.ReceiveAsync(contexto);

            Assert.IsNull(contexto.Ticket);
            A.CallTo(() => _store.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ReceiveAsync_con_token_expirado_no_establece_ticket()
        {
            string secret = "x";
            string hash = SimpleRefreshTokenProvider.HashToken(secret);
            RefreshToken expirado = TokenVigente(hash);
            expirado.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);

            A.CallTo(() => _store.FindAsync(hash)).Returns(Task.FromResult(expirado));

            AuthenticationTokenReceiveContext contexto = CrearContextoReceive(secret);
            await _provider.ReceiveAsync(contexto);

            Assert.IsNull(contexto.Ticket);
            A.CallTo(() => _store.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public void HashToken_es_determinista_y_distingue_inputs()
        {
            Assert.AreEqual(
                SimpleRefreshTokenProvider.HashToken("mismo"),
                SimpleRefreshTokenProvider.HashToken("mismo"));

            Assert.AreNotEqual(
                SimpleRefreshTokenProvider.HashToken("uno"),
                SimpleRefreshTokenProvider.HashToken("otro"));
        }

        [TestMethod]
        public void HashToken_vector_conocido_SHA256_hex()
        {
            // SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
            string esperado = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            Assert.AreEqual(esperado, SimpleRefreshTokenProvider.HashToken("abc"));
        }

        private AuthenticationTokenCreateContext CrearContextoCreate(string userName)
        {
            AuthenticationTicket ticket = TicketConIdentidad(userName);
            return new AuthenticationTokenCreateContext(_owin, _formato, ticket);
        }

        private AuthenticationTokenReceiveContext CrearContextoReceive(string secret)
        {
            return new AuthenticationTokenReceiveContext(_owin, _formato, secret);
        }

        private static AuthenticationTicket TicketConIdentidad(string userName)
        {
            ClaimsIdentity identidad = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, userName) },
                "JWT");
            return new AuthenticationTicket(identidad, new AuthenticationProperties());
        }

        private static RefreshToken TokenVigente(string hash)
        {
            return new RefreshToken
            {
                Id = hash,
                UserName = UserName,
                ClientId = "NestoApp",
                IssuedUtc = DateTime.UtcNow.AddDays(-1),
                ExpiresUtc = DateTime.UtcNow.AddDays(89),
                RevokedUtc = null,
                ProtectedTicket = "ticket-protegido"
            };
        }
    }
}
