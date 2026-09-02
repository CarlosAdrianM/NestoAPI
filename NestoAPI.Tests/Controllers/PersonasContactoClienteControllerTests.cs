using FakeItEasy;
using NestoAPI.Tests.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#447: el titular de un centro gestiona desde la app a sus personas de contacto.
    /// </summary>
    [TestClass]
    public class PersonasContactoClienteControllerTests
    {
        private NVEntities db;
        private DbSet<PersonaContactoCliente> fakePersonas;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakePersonas = A.Fake<DbSet<PersonaContactoCliente>>(o => o
                .Implements<IQueryable<PersonaContactoCliente>>()
                .Implements<IDbAsyncEnumerable<PersonaContactoCliente>>());
            A.CallTo(() => db.PersonasContactoClientes).Returns(fakePersonas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            Datos(
                Persona("15191", "0", "1", 22, "info@esteticaeleden.com"),
                Persona("15191", "2", "1", 11, "angelamaritzaperalta@gmail.com"),
                Persona("99999", "0", "1", 22, "otro@cliente.com"));
        }

        private static PersonaContactoCliente Persona(string cliente, string contacto, string numero, short cargo, string email)
        {
            return new PersonaContactoCliente
            {
                Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                NºCliente = cliente,
                Contacto = contacto,
                Número = numero,
                Cargo = cargo,
                CorreoElectrónico = email,
                Nombre = "Persona " + numero
            };
        }

        private void Datos(params PersonaContactoCliente[] personas)
        {
            IQueryable<PersonaContactoCliente> data = personas.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<PersonaContactoCliente>)fakePersonas).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<PersonaContactoCliente>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<PersonaContactoCliente>)fakePersonas).Provider)
                .Returns(new TestDbAsyncQueryProvider<PersonaContactoCliente>(data.Provider));
            A.CallTo(() => ((IQueryable<PersonaContactoCliente>)fakePersonas).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<PersonaContactoCliente>)fakePersonas).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<PersonaContactoCliente>)fakePersonas).GetEnumerator()).Returns(data.GetEnumerator());
        }

        private PersonasContactoClienteController Controller(string cliente, string email)
        {
            List<Claim> claims = new List<Claim>();
            if (cliente != null)
            {
                claims.Add(new Claim("cliente", cliente));
            }
            if (email != null)
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }
            return new PersonasContactoClienteController(db)
            {
                RequestContext = new HttpRequestContext
                {
                    Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "JWT"))
                }
            };
        }

        [TestMethod]
        public async Task Get_SinClaimCliente_Unauthorized()
        {
            IHttpActionResult resultado = await Controller(null, "info@esteticaeleden.com").GetPersonasDelCentro();

            Assert.IsInstanceOfType(resultado, typeof(UnauthorizedResult));
        }

        [TestMethod]
        public async Task Get_ElTitular_VeSoloLasPersonasDeSuCliente()
        {
            IHttpActionResult resultado = await Controller("15191", "info@esteticaeleden.com").GetPersonasDelCentro();

            var ok = resultado as OkNegotiatedContentResult<List<PersonaContactoCentroDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(2, ok.Content.Count, "la persona del cliente 99999 no aparece");
            PersonaContactoCentroDTO titular = ok.Content.Single(p => p.EsTitular);
            Assert.IsTrue(titular.EsYo);
            Assert.AreEqual("Ve todo y gestiona (facturas)", titular.Nivel);
            PersonaContactoCentroDTO encargada = ok.Content.Single(p => !p.EsTitular);
            Assert.AreEqual("Ve precios y descuentos", encargada.Nivel);
            Assert.AreEqual((short)11, encargada.Cargo);
        }

        [TestMethod]
        public async Task Get_QuienNoEsTitular_Forbidden()
        {
            IHttpActionResult resultado = await Controller("15191", "angelamaritzaperalta@gmail.com").GetPersonasDelCentro();

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
        }

        [TestMethod]
        public async Task Put_ElTitularPoneALaEncargadaSinPrecios_YQuedaAuditado()
        {
            IHttpActionResult resultado = await Controller("15191", "info@esteticaeleden.com")
                .PutCargo("2", "1", new CambioCargoPersonaContactoRequest { Cargo = 30 });

            var ok = resultado as OkNegotiatedContentResult<PersonaContactoCentroDTO>;
            Assert.IsNotNull(ok);
            Assert.AreEqual((short)30, ok.Content.Cargo);
            Assert.AreEqual("Solo pide, sin precios", ok.Content.Nivel);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            PersonaContactoCliente encargada = fakePersonas.Single(p => p.Contacto == "2");
            Assert.AreEqual((short)30, encargada.Cargo);
            Assert.AreEqual("info@esteticaeleden.com", encargada.Usuario, "auditoría: quién lo cambió");
        }

        [TestMethod]
        public async Task Put_ElUnicoTitularNoPuedeQuitarseElPermiso()
        {
            IHttpActionResult resultado = await Controller("15191", "info@esteticaeleden.com")
                .PutCargo("0", "1", new CambioCargoPersonaContactoRequest { Cargo = 31 });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_QuienNoEsTitular_Forbidden()
        {
            IHttpActionResult resultado = await Controller("15191", "angelamaritzaperalta@gmail.com")
                .PutCargo("0", "1", new CambioCargoPersonaContactoRequest { Cargo = 30 });

            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido);
            Assert.AreEqual(HttpStatusCode.Forbidden, contenido.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Put_PersonaDeOtroCliente_NoSeEncuentra()
        {
            // El titular de 15191 no puede tocar a nadie de 99999, aunque acierte contacto y número
            IHttpActionResult resultado = await Controller("15191", "info@esteticaeleden.com")
                .PutCargo("0", "1", new CambioCargoPersonaContactoRequest { Cargo = 30 });

            // "0"/"1" existe en 15191 (es el propio titular) y en 99999; solo se mira el 15191:
            // el resultado es el bloqueo del único titular, no un cambio en el otro cliente
            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            PersonaContactoCliente otro = fakePersonas.Single(p => p.NºCliente == "99999");
            Assert.AreEqual((short)22, otro.Cargo);
        }
    }
}
