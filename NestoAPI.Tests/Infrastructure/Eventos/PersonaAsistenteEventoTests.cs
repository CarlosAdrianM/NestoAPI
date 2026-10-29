using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#731 (sugerencia 862, Laura Camacho; Carlos 29/10/26): la persona que asiste (una persona de contacto del cliente,
    /// que se guarda en la inscripción) y, solo lectura y de la ficha, el correo y la dirección.
    /// </summary>
    [TestClass]
    public class PersonaAsistenteEventoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 29, 9, 0, 0);
        private const int EVENTO_REINA = 1;

        private NVEntities db;
        private List<EventoInscripcion> inscripciones;
        private List<PersonaContactoCliente> personas;
        private IRepositorioPersonaAsistenteEventos repositorio;
        private Dictionary<int, string> guardadas;
        private int siguienteId;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            var eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_REINA, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "CENTRO DE ESTÉTICA LUNA", ClientePrincipal = true, Vendedor = "JE ",
                    Dirección = "C/ Mayor, 1 ", CodPostal = "28001", Población = "MADRID ", Provincia = "MADRID", Teléfono = "915555555" },
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "1  ", Nombre = "LUNA (ALCALÁ)", ClientePrincipal = false, Vendedor = "JE ",
                    Dirección = "C/ Libreros, 5", CodPostal = "28801", Población = "ALCALÁ DE HENARES", Provincia = "MADRID" }
            };
            personas = new List<PersonaContactoCliente>
            {
                new PersonaContactoCliente { Empresa = "1", NºCliente = "15191", Contacto = "0  ", Número = "1  ", Nombre = "ANA LUNA", CorreoElectrónico = "ana@luna.es", Teléfono = "600111222" },
                new PersonaContactoCliente { Empresa = "1", NºCliente = "15191", Contacto = "0  ", Número = "2  ", Nombre = "MARTA (ESTETICISTA)", CorreoElectrónico = "marta@luna.es" },
                new PersonaContactoCliente { Empresa = "1", NºCliente = "15191", Contacto = "1  ", Número = "1  ", Nombre = "LOLA ALCALÁ", CorreoElectrónico = "lola@luna.es" }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            DbSet<EventoInscripcion> fakeInscripciones = Crear(inscripciones);
            A.CallTo(() => fakeInscripciones.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fakeInscripciones);
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            A.CallTo(() => db.PersonasContactoClientes).Returns(Crear(personas));

            guardadas = new Dictionary<int, string>();
            repositorio = A.Fake<IRepositorioPersonaAsistenteEventos>();
            A.CallTo(() => repositorio.Poner(A<int>._, A<string>._)).ReturnsLazily((int id, string numero) =>
            {
                if (numero == null)
                {
                    guardadas.Remove(id);
                }
                else
                {
                    guardadas[id] = numero;
                }
                return Task.FromResult(true);
            });
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._))
                .ReturnsLazily((IReadOnlyCollection<int> ids) => Task.FromResult(guardadas.Where(g => ids.Contains(g.Key)).ToDictionary(g => g.Key, g => g.Value)));
        }

        private ServicioInscripcionesEventos Servicio(bool conRepositorio = true)
        {
            var servicio = new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), AlmacenDe, A.Fake<IProcesadorInscripcionPagada>());
            servicio.PersonasAsistentes = conRepositorio ? repositorio : null;
            return servicio;
        }

        private static string AlmacenDe(string empresa, string usuario) => usuario == "Reina" ? "REI" : null;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente, string correo) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, correo), new Claim("cliente", cliente) }, "prueba"));

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");

        private EventoInscripcion Inscrita(int id, string contacto = "0  ")
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = EVENTO_REINA, Empresa = "1", Cliente = "15191", Contacto = contacto, Origen = "Nesto",
                Estado = EstadoInscripcionEvento.Inscrita.ToString(), Modalidad = "Gratuita",
                UsuarioCreacion = "NUEVAVISION\\Jesus", FechaCreacion = HOY.AddDays(-1), Usuario = "NUEVAVISION\\Jesus", FechaModificacion = HOY.AddDays(-1)
            };
            inscripciones.Add(i);
            return i;
        }

        // ---------------- Inscribir ----------------

        [TestMethod]
        public async Task Inscribir_ConPersonaDeLaFicha_LaGuardaYDevuelveCorreoYDireccion()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", PersonaContacto = "2" }, VENDEDOR);

            Assert.AreEqual("2", guardadas[dto.Id]);
            Assert.AreEqual("2", dto.PersonaContacto.Numero);
            Assert.AreEqual("MARTA (ESTETICISTA)", dto.PersonaContacto.Nombre);
            Assert.AreEqual("marta@luna.es", dto.Correo, "El correo es el de quien asiste");
            Assert.AreEqual("C/ Mayor, 1, 28001 MADRID (MADRID)", dto.Direccion);
        }

        [TestMethod]
        public async Task Inscribir_PersonaDeOtraDireccion_400YNoSeInscribe()
        {
            // La 2 (Marta) es de la dirección 0, no de la 1
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Contacto = "1", Origen = "Nesto", PersonaContacto = "2" }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "no es de la ficha del cliente 15191/1");
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_SinPersona_SinPersonaYCorreoDeLaFicha()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Contacto = "1", Origen = "Nesto" }, VENDEDOR);

            Assert.IsNull(dto.PersonaContacto);
            Assert.IsFalse(guardadas.Any());
            Assert.AreEqual("lola@luna.es", dto.Correo, "Sin persona que asiste, el de la misma dirección");
            Assert.AreEqual("C/ Libreros, 5, 28801 ALCALÁ DE HENARES (MADRID)", dto.Direccion);
        }

        [TestMethod]
        public async Task Inscribir_LaClientaDesdeLaTienda_AsisteLaPersonaDeSuCorreo()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191" }, ClienteTienda("15191", "ANA@luna.es"));

            Assert.AreEqual("1", guardadas[dto.Id]);
            Assert.AreEqual("ANA LUNA", dto.PersonaContacto.Nombre);
        }

        [TestMethod]
        public async Task Inscribir_SinLaColumna_SeInscribeIgualSinPersona()
        {
            InscripcionEventoDTO dto = await Servicio(conRepositorio: false).Inscribir(EVENTO_REINA,
                new InscribirEventoDTO { Cliente = "15191", Origen = "Nesto", PersonaContacto = "2" }, VENDEDOR);

            Assert.AreEqual(1, inscripciones.Count);
            Assert.IsNull(dto.PersonaContacto);
        }

        // ---------------- Cambiar ----------------

        [TestMethod]
        public async Task CambiarPersona_LaTiendaDelEvento_LaCambia()
        {
            Inscrita(7);

            InscripcionEventoDTO dto = await Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = "1" }, REINA);

            Assert.AreEqual("1", guardadas[7]);
            Assert.AreEqual("ANA LUNA", dto.PersonaContacto.Nombre);
            Assert.AreEqual("ana@luna.es", dto.Correo);
            Assert.AreEqual("Reina", inscripciones.Single().Usuario.Split('\\').Last());
        }

        [TestMethod]
        public async Task CambiarPersona_Vacia_LaQuita()
        {
            Inscrita(7);
            guardadas[7] = "2";

            InscripcionEventoDTO dto = await Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = " " }, REINA);

            Assert.IsFalse(guardadas.ContainsKey(7));
            Assert.IsNull(dto.PersonaContacto);
        }

        [TestMethod]
        public async Task CambiarPersona_UnVendedor_403()
        {
            Inscrita(7);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = "1" }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.IsFalse(guardadas.Any());
        }

        [TestMethod]
        public async Task CambiarPersona_LaClientaDesdeLaTienda_403()
        {
            Inscrita(7);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = "1" }, ClienteTienda("15191", "ana@luna.es")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task CambiarPersona_DeOtraDireccion_400()
        {
            Inscrita(7, contacto: "1  ");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = "2" }, REINA));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.IsFalse(guardadas.Any());
        }

        [TestMethod]
        public async Task CambiarPersona_SinLaColumna_400QueFaltaElScript()
        {
            Inscrita(7);
            A.CallTo(() => repositorio.Poner(A<int>._, A<string>._)).Returns(Task.FromResult(false));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarPersonaContacto(7, new CambiarPersonaContactoInscripcionDTO { PersonaContacto = "1" }, REINA));

            StringAssert.Contains(ex.Message, "Issue731");
        }

        // ---------------- Leer ----------------

        [TestMethod]
        public async Task ListaDeInscritos_LlevaQuienAsisteCorreoYDireccion()
        {
            Inscrita(7);
            Inscrita(8, contacto: "1  ");
            guardadas[7] = "2";

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_REINA, REINA);

            InscripcionEventoDTO conPersona = lista.Single(i => i.Id == 7);
            Assert.AreEqual("MARTA (ESTETICISTA)", conPersona.PersonaContacto.Nombre);
            Assert.AreEqual("marta@luna.es", conPersona.Correo);
            Assert.AreEqual("C/ Mayor, 1, 28001 MADRID (MADRID)", conPersona.Direccion);
            InscripcionEventoDTO sinPersona = lista.Single(i => i.Id == 8);
            Assert.IsNull(sinPersona.PersonaContacto);
            Assert.AreEqual("lola@luna.es", sinPersona.Correo);
        }

        [TestMethod]
        public async Task ListaDeInscritos_SiFallaLaLecturaDeQuienAsiste_SaleIgual()
        {
            Inscrita(7);
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).Throws(new InvalidOperationException("sin columna"));

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_REINA, REINA);

            Assert.AreEqual(1, lista.Count);
            Assert.IsNull(lista.Single().PersonaContacto);
        }

        [TestMethod]
        public async Task PersonasContacto_DeUnaDireccion()
        {
            List<PersonaContactoEventoDTO> lista = await Servicio().LeerPersonasContacto("1", "15191", "0", VENDEDOR);

            CollectionAssert.AreEqual(new[] { "1", "2" }, lista.Select(p => p.Numero).ToArray());
            Assert.IsTrue(lista.All(p => p.Contacto == "0"));
            Assert.AreEqual("600111222", lista.First().Telefono);
        }

        [TestMethod]
        public async Task PersonasContacto_SinContacto_TodasLasDelCliente()
        {
            List<PersonaContactoEventoDTO> lista = await Servicio().LeerPersonasContacto("1", "15191", null, VENDEDOR);

            Assert.AreEqual(3, lista.Count);
        }

        [TestMethod]
        public async Task PersonasContacto_ConElTokenDeLaTienda_403()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().LeerPersonasContacto("1", "15191", "0", ClienteTienda("15191", "ana@luna.es")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        // ---------------- Ficha ----------------

        [TestMethod]
        public void Ficha_Direccion()
        {
            Assert.AreEqual("C/ Mayor 1, 28001 Madrid (Madrid)", FichaInscripcionEvento.Direccion(" C/ Mayor 1 ", "28001", "Madrid", "Madrid"));
            Assert.AreEqual("28001 Madrid", FichaInscripcionEvento.Direccion(null, "28001", "Madrid", null));
            Assert.AreEqual("C/ Mayor 1", FichaInscripcionEvento.Direccion("C/ Mayor 1", " ", null, ""));
            Assert.IsNull(FichaInscripcionEvento.Direccion(null, null, " ", null));
        }

        [TestMethod]
        public void Ficha_Correo_PrimeroElDeQuienAsisteLuegoLaDireccionLuegoElCliente()
        {
            var lista = new List<PersonaContactoEventoDTO>
            {
                new PersonaContactoEventoDTO { Contacto = "1", Numero = "1", Correo = "otra@luna.es" },
                new PersonaContactoEventoDTO { Contacto = "0", Numero = "1", Correo = "no es correo; ana@luna.es" },
                new PersonaContactoEventoDTO { Contacto = "0", Numero = "2", Correo = null }
            };

            Assert.AreEqual("ana@luna.es", FichaInscripcionEvento.Correo("0", "2", lista), "Quien asiste no tiene: el de su dirección");
            Assert.AreEqual("ana@luna.es", FichaInscripcionEvento.Correo("0", null, lista));
            Assert.AreEqual("otra@luna.es", FichaInscripcionEvento.Correo("1", "1", lista));
            Assert.AreEqual("otra@luna.es", FichaInscripcionEvento.Correo("5", null, lista), "Sin nadie en la dirección, el de cualquiera del cliente");
            Assert.IsNull(FichaInscripcionEvento.Correo("0", null, new List<PersonaContactoEventoDTO>()));
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
