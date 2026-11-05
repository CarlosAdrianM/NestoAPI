using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
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
    /// NestoAPI#829 (Reina, sugerencia 1064; Carlos 05/11/26): en los eventos mixtos (presenciales y online a la vez), cada
    /// inscripción dice si la clienta viene presencial u online. Sin el script (o en un evento que no es mixto) no se apunta.
    /// </summary>
    [TestClass]
    public class FormaAsistenciaInscripcionEventoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 5, 10, 0, 0);
        private const int EVENTO_MIXTO = 8;
        private const int EVENTO_PRESENCIAL = 9;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private IRepositorioFormaAsistenciaEventos repositorio;
        private Dictionary<int, string> guardadas;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_MIXTO, Empresa = "1", Titulo = "Masterclass mixta", Fecha = new DateTime(2026, 11, 19), Formato = "Mixto",
                    ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY },
                new Evento { Id = EVENTO_PRESENCIAL, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20), Formato = null,
                    ImporteSenal = 0, ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            var clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "LUNA", ClientePrincipal = true, Vendedor = "JE ", Teléfono = "915555555" },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV ", Teléfono = "915555555" }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            A.CallTo(() => db.PersonasContactoClientes).Returns(Crear(new List<PersonaContactoCliente>()));

            guardadas = new Dictionary<int, string>();
            repositorio = A.Fake<IRepositorioFormaAsistenciaEventos>();
            A.CallTo(() => repositorio.Disponible()).Returns(true);
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).ReturnsLazily((IReadOnlyCollection<int> ids) =>
                Task.FromResult(guardadas.Where(g => ids.Contains(g.Key) && g.Value != null).ToDictionary(g => g.Key, g => g.Value)));
            A.CallTo(() => repositorio.Poner(A<int>._, A<string>._)).ReturnsLazily((int id, string forma) =>
            {
                guardadas[id] = forma;
                return Task.FromResult(true);
            });
        }

        private ServicioInscripcionesEventos Servicio(bool conRepositorio = true) =>
            new ServicioInscripcionesEventos(db, () => HOY, () => A.Fake<IServicioPagos>(), (e, u) => null, A.Fake<IProcesadorInscripcionPagada>())
            {
                FormasAsistencia = conRepositorio ? repositorio : null
            };

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal ADMINISTRACION = Usuario("NUEVAVISION\\Aida", Constantes.GruposSeguridad.ADMINISTRACION);

        private EventoInscripcion Inscrita(int id, string cliente, int eventoId, string forma = null)
        {
            var i = new EventoInscripcion
            {
                Id = id, EventoId = eventoId, Empresa = "1", Cliente = cliente, Contacto = "0  ", Origen = "Nesto",
                Estado = EstadoInscripcionEvento.Inscrita.ToString(), Modalidad = "Gratuita",
                UsuarioCreacion = "NUEVAVISION\\Jesus", FechaCreacion = HOY.AddDays(-1), Usuario = "NUEVAVISION\\Jesus", FechaModificacion = HOY.AddDays(-1)
            };
            inscripciones.Add(i);
            if (forma != null)
            {
                guardadas[id] = forma;
            }
            return i;
        }

        // ---------------- Reglas puras ----------------

        [TestMethod]
        public void Interpretar_PresencialOnlineYVacio_YLoRaroEs400()
        {
            Assert.AreEqual("Presencial", ReglasFormaAsistenciaEvento.Interpretar(" presencial "));
            Assert.AreEqual("Online", ReglasFormaAsistenciaEvento.Interpretar("ONLINE"));
            Assert.IsNull(ReglasFormaAsistenciaEvento.Interpretar(null));
            Assert.IsNull(ReglasFormaAsistenciaEvento.Interpretar("  "));
            _ = Assert.ThrowsException<NestoBusinessException>(() => ReglasFormaAsistenciaEvento.Interpretar("Mixto"));
        }

        [TestMethod]
        public void FormatosEvento_Mixto_NoEsOnline()
        {
            Assert.AreEqual("Mixto", FormatosEvento.Interpretar("mixto"));
            Assert.AreEqual("Mixto", FormatosEvento.De(new Evento { Formato = "Mixto" }));
            Assert.IsTrue(FormatosEvento.EsMixto(new Evento { Formato = "mixto " }));
            Assert.IsFalse(FormatosEvento.EsOnline(new Evento { Formato = "Mixto" }));
            Assert.IsNull(FormatosEvento.EnlaceOnline(new Evento { Formato = "Mixto", Lugar = "https://meet.example.com/x" }));
        }

        // ---------------- PUT api/Eventos/Inscripciones/{id}/FormaAsistencia ----------------

        [TestMethod]
        public async Task Cambiar_EnUnMixto_SeGuardaYVuelveEnLaLista()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO);
            _ = Inscrita(2, "20000", EVENTO_MIXTO);

            InscripcionEventoDTO dto = await Servicio().CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "online" }, ADMINISTRACION);

            Assert.AreEqual("Online", dto.FormaAsistencia);
            Assert.AreEqual("Online", guardadas[1]);
            Assert.AreEqual("NUEVAVISION\\Aida", inscripciones.Single(i => i.Id == 1).Usuario);
            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_MIXTO, ADMINISTRACION);
            Assert.AreEqual("Online", lista.Single(i => i.Id == 1).FormaAsistencia);
            Assert.IsNull(lista.Single(i => i.Id == 2).FormaAsistencia, "Sin indicar");
        }

        [TestMethod]
        public async Task Cambiar_ConVacio_LaQuita()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO, forma: "Presencial");

            InscripcionEventoDTO dto = await Servicio().CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "" }, ADMINISTRACION);

            Assert.IsNull(dto.FormaAsistencia);
            A.CallTo(() => repositorio.Poner(1, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cambiar_EnUnEventoQueNoEsMixto_400YNoSeGuarda()
        {
            _ = Inscrita(1, "15191", EVENTO_PRESENCIAL);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "Online" }, ADMINISTRACION));

            StringAssert.Contains(ex.Message, "no es un evento mixto");
            A.CallTo(() => repositorio.Poner(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cambiar_ValorRaro_400()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "A distancia" }, ADMINISTRACION));
            A.CallTo(() => repositorio.Poner(A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cambiar_ElVendedorNoPuede_403()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "Online" }, VENDEDOR));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task SinElScript_ElPutLoDiceYLaListaSaleSinIndicar()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio(conRepositorio: false)
                .CambiarFormaAsistencia(1, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "Online" }, ADMINISTRACION));
            StringAssert.Contains(ex.Message, "Issue829_EventosInscripcionesModalidad.sql");

            List<InscripcionEventoDTO> lista = await Servicio(conRepositorio: false).LeerInscripcionesEvento(EVENTO_MIXTO, ADMINISTRACION);
            Assert.IsNull(lista.Single().FormaAsistencia);
        }

        [TestMethod]
        public async Task Lista_EnUnEventoQueNoEsMixto_NoLaLee()
        {
            _ = Inscrita(1, "15191", EVENTO_PRESENCIAL, forma: "Online");

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_PRESENCIAL, ADMINISTRACION);

            Assert.IsNull(lista.Single().FormaAsistencia);
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>.That.Matches(ids => ids.Any()))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SiFallaLaLectura_LaListaSaleIgual()
        {
            _ = Inscrita(1, "15191", EVENTO_MIXTO, forma: "Online");
            A.CallTo(() => repositorio.Leer(A<IReadOnlyCollection<int>>._)).Throws(new InvalidOperationException("El nombre de columna 'FormaAsistencia' no es válido."));

            List<InscripcionEventoDTO> lista = await Servicio().LeerInscripcionesEvento(EVENTO_MIXTO, ADMINISTRACION);

            Assert.IsNull(lista.Single().FormaAsistencia);
        }

        [TestMethod]
        public async Task Controlador_PutFormaAsistencia_LlamaAlServicio()
        {
            IServicioInscripcionesEventos servicio = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicio.CambiarFormaAsistencia(7, A<CambiarFormaAsistenciaInscripcionDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 7, FormaAsistencia = "Online" }));
            var controlador = new NestoAPI.Controllers.EventosController(db, A.Fake<IServicioEventos>(), servicio, A.Fake<IServicioPropuestasSenales>());

            var respuesta = await controlador.PutFormaAsistenciaInscripcion(7, new CambiarFormaAsistenciaInscripcionDTO { FormaAsistencia = "Online" })
                as System.Web.Http.Results.OkNegotiatedContentResult<InscripcionEventoDTO>;

            Assert.AreEqual("Online", respuesta.Content.FormaAsistencia);
            A.CallTo(() => servicio.CambiarFormaAsistencia(7, A<CambiarFormaAsistenciaInscripcionDTO>.That.Matches(p => p.FormaAsistencia == "Online"), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
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
