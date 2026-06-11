using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#197: captura del correo del cliente en la página de pago cuando el enlace se
    /// generó sin él. Guarda PagoTPV.Correo y da de alta el correo como persona de contacto
    /// (cargo 22 si quiere facturas electrónicas y nadie las recibe aún; cargo 14 si no).
    /// </summary>
    [TestClass]
    public class PagoRedirectControllerGuardarCorreoTests
    {
        private static readonly Guid TOKEN = Guid.Parse("11111111-2222-3333-4444-555555555555");

        private NVEntities _db;
        private DbSet<PagoTPV> _fakePagos;
        private DbSet<PersonaContactoCliente> _fakePersonas;
        private List<PersonaContactoCliente> _personasCreadas;
        private PagoRedirectController _controller;
        private PagoTPV _pago;

        [TestInitialize]
        public void Setup()
        {
            _db = A.Fake<NVEntities>();
            _fakePagos = A.Fake<DbSet<PagoTPV>>(o => o.Implements<IQueryable<PagoTPV>>().Implements<IDbAsyncEnumerable<PagoTPV>>());
            _fakePersonas = A.Fake<DbSet<PersonaContactoCliente>>(o => o.Implements<IQueryable<PersonaContactoCliente>>().Implements<IDbAsyncEnumerable<PersonaContactoCliente>>());

            A.CallTo(() => _db.PagosTPV).Returns(_fakePagos);
            A.CallTo(() => _db.PersonasContactoClientes).Returns(_fakePersonas);
            A.CallTo(() => _fakePagos.Include(A<string>.Ignored)).Returns(_fakePagos);

            _pago = new PagoTPV
            {
                Id = 1,
                TokenAcceso = TOKEN,
                Empresa = "1  ",
                Cliente = "15191     ",
                Contacto = "0  ",
                Importe = 100m,
                NumeroOrden = "TEST123",
                Estado = Constantes.EstadosPagoTPV.PENDIENTE
            };
            ConfigurarFakeDbSet(_fakePagos, new List<PagoTPV> { _pago }.AsQueryable());
            ConfigurarFakeDbSet(_fakePersonas, new List<PersonaContactoCliente>().AsQueryable());

            _personasCreadas = new List<PersonaContactoCliente>();
            A.CallTo(() => _fakePersonas.Add(A<PersonaContactoCliente>.Ignored))
                .Invokes((PersonaContactoCliente p) => _personasCreadas.Add(p))
                .ReturnsLazily((PersonaContactoCliente p) => p);
            A.CallTo(() => _db.SaveChangesAsync()).Returns(Task.FromResult(1));

            _controller = new PagoRedirectController(A.Fake<IRedsysService>(), _db);
        }

        private void ConfigurarPersonasExistentes(params PersonaContactoCliente[] personas)
        {
            ConfigurarFakeDbSet(_fakePersonas, personas.AsQueryable());
        }

        #region Endpoint POST /pago/{token}/correo

        [TestMethod]
        public async Task GuardarCorreo_PagoNoExiste_Devuelve404()
        {
            var resultado = await _controller.GuardarCorreo(Guid.NewGuid(),
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com" });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task GuardarCorreo_PagoYaPagado_Devuelve409()
        {
            _pago.Estado = Constantes.EstadosPagoTPV.AUTORIZADO;

            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com" });

            Assert.IsInstanceOfType(resultado, typeof(ConflictResult));
        }

        [TestMethod]
        public async Task GuardarCorreo_FormatoInvalido_Devuelve400()
        {
            foreach (string correoInvalido in new[] { null, "", "   ", "sinarroba", "dos@arrobas@x.com", "sin@punto", "con espacios@x.com" })
            {
                var resultado = await _controller.GuardarCorreo(TOKEN,
                    new GuardarCorreoPagoDTO { Correo = correoInvalido });

                Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult),
                    $"'{correoInvalido}' debería rechazarse");
            }
            A.CallTo(() => _db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarCorreo_PersistePagoTPVCorreo()
        {
            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "  cliente@test.com  " });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<RespuestaGuardarCorreoPago>));
            Assert.AreEqual("cliente@test.com", _pago.Correo, "Debe persistir el correo recortado");
            A.CallTo(() => _db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GuardarCorreo_PrimerCorreoSinCargo22_DeseaFacturas_CreaConCargo22()
        {
            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com", DeseaFacturasElectronicas = true });

            var ok = (OkNegotiatedContentResult<RespuestaGuardarCorreoPago>)resultado;
            Assert.IsTrue(ok.Content.FacturasElectronicas);
            var creada = _personasCreadas.Single();
            Assert.AreEqual(Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO, creada.Cargo);
            Assert.AreEqual("cliente@test.com", creada.CorreoElectrónico);
            Assert.AreEqual(_pago.Empresa, creada.Empresa);
            Assert.AreEqual(_pago.Cliente, creada.NºCliente);
            Assert.AreEqual(_pago.Contacto, creada.Contacto);
        }

        [TestMethod]
        public async Task GuardarCorreo_PrimerCorreoSinCargo22_NoDeseaFacturas_CreaConCargo14()
        {
            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com", DeseaFacturasElectronicas = false });

            var ok = (OkNegotiatedContentResult<RespuestaGuardarCorreoPago>)resultado;
            Assert.IsFalse(ok.Content.FacturasElectronicas);
            Assert.AreEqual(Constantes.Clientes.CARGO_POR_DEFECTO, _personasCreadas.Single().Cargo);
        }

        [TestMethod]
        public async Task GuardarCorreo_ClienteYaTieneCargo22_CreaConCargo14_IgnoraCheckbox()
        {
            ConfigurarPersonasExistentes(new PersonaContactoCliente
            {
                Empresa = _pago.Empresa,
                NºCliente = _pago.Cliente,
                Contacto = _pago.Contacto,
                Número = "1",
                Cargo = Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO,
                CorreoElectrónico = "otro@test.com"
            });

            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com", DeseaFacturasElectronicas = true });

            var ok = (OkNegotiatedContentResult<RespuestaGuardarCorreoPago>)resultado;
            Assert.IsFalse(ok.Content.FacturasElectronicas, "Ya hay quien recibe las facturas: no debe cambiarse");
            Assert.AreEqual(Constantes.Clientes.CARGO_POR_DEFECTO, _personasCreadas.Single().Cargo);
        }

        [TestMethod]
        public async Task GuardarCorreo_CorreoYaExisteEnPCC_TrimYCaseInsensitive_NoDuplica()
        {
            ConfigurarPersonasExistentes(new PersonaContactoCliente
            {
                Empresa = _pago.Empresa,
                NºCliente = _pago.Cliente,
                Contacto = _pago.Contacto,
                Número = "1",
                Cargo = Constantes.Clientes.CARGO_POR_DEFECTO,
                CorreoElectrónico = "  CLIENTE@Test.com "
            });

            var resultado = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<RespuestaGuardarCorreoPago>));
            Assert.AreEqual(0, _personasCreadas.Count, "El correo ya existía (trim + case-insensitive): no debe duplicarse");
            Assert.AreEqual("cliente@test.com", _pago.Correo, "El correo del pago sí se actualiza");
        }

        [TestMethod]
        public async Task GuardarCorreo_NumeracionYUsuarioIdentificable()
        {
            ConfigurarPersonasExistentes(new PersonaContactoCliente
            {
                Empresa = _pago.Empresa,
                NºCliente = _pago.Cliente,
                Contacto = _pago.Contacto,
                Número = "3",
                Cargo = Constantes.Clientes.CARGO_POR_DEFECTO,
                CorreoElectrónico = "otro@test.com"
            });

            var unused = await _controller.GuardarCorreo(TOKEN,
                new GuardarCorreoPagoDTO { Correo = "cliente@test.com" });

            var creada = _personasCreadas.Single();
            Assert.AreEqual("4", creada.Número, "Debe seguir la numeración del cliente/contacto (max + 1)");
            Assert.AreEqual(PagoRedirectController.USUARIO_NESTOPAGO, creada.Usuario,
                "El Usuario debe permitir rastrear que vino de NestoPago");
            Assert.IsFalse(creada.EnviarBoletin, "No se asume consentimiento de marketing");
            Assert.AreEqual(Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO, creada.Estado);
        }

        #endregion

        #region Render de la página

        private void PrepararRequest()
        {
            _controller.Request = new HttpRequestMessage(HttpMethod.Get, $"https://api.nuevavision.es/pago/{TOKEN}");
            _controller.Configuration = new HttpConfiguration();
        }

        [TestMethod]
        public async Task PaginaPago_ConPagoSinCorreo_RenderizaBloqueCapturaConCheckbox()
        {
            PrepararRequest();
            _pago.Correo = null;

            var respuesta = await _controller.PaginaPago(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync();

            Assert.IsTrue(html.Contains("bloque-correo"), "Sin correo debe ofrecer la captura");
            // El JS siempre menciona 'chk-facturas' (getElementById defensivo): buscamos el elemento.
            Assert.IsTrue(html.Contains(@"id=""chk-facturas"""), "Sin nadie con cargo 22 debe ofrecer la checkbox de facturas");
            Assert.IsTrue(html.Contains($"/pago/{TOKEN}/correo"), "El JS debe apuntar al endpoint del token");
        }

        [TestMethod]
        public async Task PaginaPago_ConPagoConCorreo_NoRenderizaBloqueCaptura()
        {
            PrepararRequest();
            _pago.Correo = "cliente@test.com";

            var respuesta = await _controller.PaginaPago(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync();

            Assert.IsFalse(html.Contains("bloque-correo"), "Con correo informado no se muestra nada nuevo");
        }

        [TestMethod]
        public async Task PaginaPago_ClienteConCargo22_NoRenderizaCheckboxFacturas()
        {
            PrepararRequest();
            _pago.Correo = null;
            ConfigurarPersonasExistentes(new PersonaContactoCliente
            {
                Empresa = _pago.Empresa,
                NºCliente = _pago.Cliente,
                Contacto = _pago.Contacto,
                Número = "1",
                Cargo = Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO,
                CorreoElectrónico = "facturas@test.com"
            });

            var respuesta = await _controller.PaginaPago(TOKEN);
            string html = await respuesta.Content.ReadAsStringAsync();

            Assert.IsTrue(html.Contains("bloque-correo"), "Debe ofrecer la captura del correo");
            Assert.IsFalse(html.Contains(@"id=""chk-facturas"""), "Ya recibe facturas en otro correo: sin checkbox");
        }

        #endregion

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
    }
}
