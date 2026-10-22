using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// Decisiones de Carlos (22/10/26): la referencia del curso de un evento. O la propia del evento (EVENTO00123 / EVENTO00123NR,
    /// POST api/Eventos/{id}/ProductoCurso para Nesto#517) o un producto forzado (cualquier curso del grupo CUR, compartible).
    /// </summary>
    [TestClass]
    public class ProductoCursoEventoTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 22, 10, 0, 0);

        private NVEntities db;
        private List<Evento> eventos;
        private List<Producto> productos;
        private IRepositorioProductoCurso repositorio;
        private List<string> creados;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = 123, Empresa = "1  ", Titulo = "Masterclass Rosácea: diagnóstico, protocolo de cabina y venta de tratamiento en casa",
                    Fecha = new DateTime(2026, 11, 20), ImporteSenal = 100, ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI",
                    Usuario = "Reina", FechaModificacion = HOY },
                new Evento { Id = 7, Empresa = "1", Titulo = "Presentación gratuita", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 0,
                    ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY }
            };
            productos = new List<Producto>
            {
                new Producto { Empresa = "1", Número = "90077", Grupo = "CUR", Nombre = "MASTERCLASS ROSACEA" },
                new Producto { Empresa = "1", Número = "17404", Grupo = "COS", Nombre = "CREMA" }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.Productos).Returns(Crear(productos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(new List<EventoInscripcion>()));

            creados = new List<string>();
            repositorio = A.Fake<IRepositorioProductoCurso>();
            A.CallTo(() => repositorio.LeerNombre(A<string>._, A<string>._)).ReturnsLazily((string e, string p) =>
                Task.FromResult(creados.Contains(e + "/" + p) ? "CREADO" : null));
            A.CallTo(() => repositorio.CrearDesdePlantilla(A<string>._, A<string>._, A<string>._, A<string>._, A<decimal>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string p, string t, string n, decimal pvp, string c, string u) =>
                {
                    creados.Add(e + "/" + p);
                    return Task.FromResult(1);
                });
        }

        private ServicioEventos Servicio() => new ServicioEventos(db, () => HOY, (e, u) => u == "Reina" ? "REI" : null,
            A.Fake<IRepositorioFacturacionCursos>(), A.Fake<IRepositorioCupones>(), t => t(), null, repositorio);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        // ---------------- Reglas ----------------

        [TestMethod]
        public void Reglas_ReferenciaYConvencion()
        {
            Assert.AreEqual("EVENTO00123", ReglasProductoCurso.Referencia(123, ModalidadInscripcionEvento.Reembolsable));
            Assert.AreEqual("EVENTO00123NR", ReglasProductoCurso.Referencia(123, ModalidadInscripcionEvento.NoReembolsable));
            Assert.IsTrue(ReglasProductoCurso.Referencia(123, ModalidadInscripcionEvento.NoReembolsable).Length <= 15, "Productos.Número es char(15)");
            Assert.AreEqual((123, ModalidadInscripcionEvento.NoReembolsable), ReglasProductoCurso.Interpretar(" evento00123nr "));
            Assert.AreEqual((123, ModalidadInscripcionEvento.Reembolsable), ReglasProductoCurso.Interpretar("EVENTO00123"));
            Assert.IsNull(ReglasProductoCurso.Interpretar("90077"));
            Assert.IsNull(ReglasProductoCurso.Interpretar("EVENTO123"));
            Assert.ThrowsException<NestoBusinessException>(() => ReglasProductoCurso.Referencia(1, ModalidadInscripcionEvento.Gratuita));
        }

        [TestMethod]
        public void Reglas_ParaFacturar_LaDeSuModalidadSiExisteYElForzadoTalCual()
        {
            var evento = new Evento { Id = 123, ProductoCurso = "EVENTO00123" };
            Assert.AreEqual("EVENTO00123NR", ReglasProductoCurso.ParaFacturar(evento, ModalidadInscripcionEvento.NoReembolsable, p => true));
            Assert.AreEqual("EVENTO00123", ReglasProductoCurso.ParaFacturar(evento, ModalidadInscripcionEvento.NoReembolsable, p => false),
                "Sin la NR creada, la única que hay");
            Assert.AreEqual("90077", ReglasProductoCurso.ParaFacturar(new Evento { Id = 123, ProductoCurso = "90077 " }, ModalidadInscripcionEvento.NoReembolsable, p => true));
        }

        [TestMethod]
        public void Reglas_ProductoDelEvento_PrefiereLaReembolsableYSustituyeAlForzado()
        {
            Assert.AreEqual("EVENTO00123NR", ReglasProductoCurso.ProductoDelEvento(new Evento { Id = 123 }, "EVENTO00123NR"));
            Assert.AreEqual("EVENTO00123", ReglasProductoCurso.ProductoDelEvento(new Evento { Id = 123, ProductoCurso = "EVENTO00123NR" }, "EVENTO00123"));
            Assert.AreEqual("EVENTO00123", ReglasProductoCurso.ProductoDelEvento(new Evento { Id = 123, ProductoCurso = "EVENTO00123" }, "EVENTO00123NR"));
            Assert.AreEqual("EVENTO00123NR", ReglasProductoCurso.ProductoDelEvento(new Evento { Id = 123, ProductoCurso = "90077" }, "EVENTO00123NR"));
        }

        [TestMethod]
        public void Reglas_ElSqlCopiaLaFichaDeLaPlantillaSinDuplicar()
        {
            StringAssert.Contains(RepositorioProductoCurso.SQL_CREAR, "FROM dbo.Productos t");
            StringAssert.Contains(RepositorioProductoCurso.SQL_CREAR, "t.Grupo, @pvp");
            StringAssert.Contains(RepositorioProductoCurso.SQL_CREAR, "t.Ficticio");
            StringAssert.Contains(RepositorioProductoCurso.SQL_CREAR, "AND NOT EXISTS");
        }

        // ---------------- POST api/Eventos/{id}/ProductoCurso ----------------

        [TestMethod]
        public async Task CrearProductoCurso_NoReembolsable_SeCreaConElTituloRecortadoYLaSenalYSeLigaAlEvento()
        {
            ProductoCursoDTO r = await Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "NoReembolsable" }, "NUEVAVISION\\Reina");

            Assert.AreEqual("EVENTO00123NR", r.Producto);
            Assert.IsTrue(r.Creado);
            Assert.AreEqual(50, r.Nombre.Length, "Productos.Nombre es char(50)");
            A.CallTo(() => repositorio.CrearDesdePlantilla("1", "EVENTO00123NR", "90077", r.Nombre, 60m, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.CrearDesdePlantilla("3", "EVENTO00123NR", "90077", r.Nombre, 60m, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly(); // y en la empresa espejo, por si la factura se traspasa
            Assert.AreEqual("EVENTO00123NR", eventos[0].ProductoCurso);
        }

        [TestMethod]
        public async Task CrearProductoCurso_Idempotente_LaSegundaVezDevuelveLaExistente()
        {
            _ = await Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }, "x");
            ProductoCursoDTO r = await Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }, "x");

            Assert.AreEqual("EVENTO00123", r.Producto);
            Assert.IsFalse(r.Creado);
            Assert.AreEqual("CREADO", r.Nombre);
            A.CallTo(() => repositorio.CrearDesdePlantilla("1", A<string>._, A<string>._, A<string>._, A<decimal>._, A<string>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CrearProductoCurso_SinPlantillaEnLaEmpresa_400()
        {
            A.CallTo(() => repositorio.CrearDesdePlantilla(A<string>._, A<string>._, A<string>._, A<string>._, A<decimal>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(0));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }, "x"));

            StringAssert.Contains(ex.Message, "90077");
            Assert.IsNull(eventos[0].ProductoCurso);
        }

        [TestMethod]
        public async Task CrearProductoCurso_GratuitoOModalidadRaraOSinImporte_400()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearProductoCurso(7, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }, "x"));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Gratuita" }, "x"));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().CrearProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Quizá" }, "x"));
        }

        [TestMethod]
        public async Task Controlador_ProductoCurso_ComoCrearEvento_SinPermiso403Y404()
        {
            var vendedor = new EventosController(db, Servicio()) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\Jesus", "Ventas") };
            var r403 = await vendedor.PostProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }) as ResponseMessageResult;
            Assert.AreEqual(HttpStatusCode.Forbidden, r403.Response.StatusCode);

            var reina = new EventosController(db, Servicio()) { Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\Reina", "Tiendas") };
            Assert.IsInstanceOfType(await reina.PostProductoCurso(999, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }), typeof(NotFoundResult));
            var ok = await reina.PostProductoCurso(123, new PeticionProductoCursoDTO { Modalidad = "Reembolsable" }) as OkNegotiatedContentResult<ProductoCursoDTO>;
            Assert.AreEqual("EVENTO00123", ok.Content.Producto);
        }

        // ---------------- Producto forzado al guardar el evento ----------------

        [TestMethod]
        public async Task GuardarEvento_ProductoForzado_CualquierCursoQueExista()
        {
            EventoDTO creado = await Servicio().CrearEvento(new EventoDTO
            {
                Titulo = "Masterclass Rosácea", Fecha = new DateTime(2026, 12, 1), Almacen = "REI", ProductoCurso = " 90077 "
            }, "Reina");

            Assert.AreEqual("90077", creado.ProductoCurso);
        }

        [TestMethod]
        public async Task GuardarEvento_ProductoQueNoEsCurso_400()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CrearEvento(new EventoDTO
            {
                Titulo = "X", Fecha = new DateTime(2026, 12, 1), Almacen = "REI", ProductoCurso = "17404"
            }, "Reina"));

            StringAssert.Contains(ex.Message, "no es un curso (grupo CUR)");
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
