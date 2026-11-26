using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#890: cargar, cambiar y borrar las novedades de una versión por la API (sin scripts de sa).</summary>
    [TestClass]
    public class NovedadesVersionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private IRepositorioNovedadesVersion repo;
        private IServicioNovedades novedades;
        private ServicioNovedadesVersion servicio;

        [TestInitialize]
        public void Setup()
        {
            repo = A.Fake<IRepositorioNovedadesVersion>();
            novedades = A.Fake<IServicioNovedades>();
            A.CallTo(() => repo.LeerDeVersion(A<string>._, A<string>._)).Returns(new List<NovedadVersionFila>());
            A.CallTo(() => repo.Leer(A<int>._)).Returns(null);
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>._, A<string>._))
                .ReturnsLazily((CargaNormalizada c, IReadOnlyList<NovedadNormalizada> n, string u) =>
                    n.Select((x, i) => new { x.Titulo, Id = 100 + i }).ToDictionary(x => x.Titulo, x => x.Id));
            A.CallTo(() => novedades.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).Returns(true);
            servicio = new ServicioNovedadesVersion(repo, novedades, () => HOY);
        }

        private static CargaNovedadesVersionDTO Carga(params NovedadVersionDTO[] n) => new CargaNovedadesVersionDTO
        {
            Version = "1.10.68.0",
            Ambito = "Nesto",
            Novedades = n.ToList()
        };

        private static NovedadVersionDTO Nov(string titulo, string categoria = "Nuevo", int? sugerencia = null, List<string> perfiles = null) =>
            new NovedadVersionDTO { Titulo = titulo, Categoria = categoria, Descripcion = "Texto de " + titulo, SugerenciaId = sugerencia, Perfiles = perfiles };

        private static HttpStatusCode Estado(Action accion)
        {
            try
            {
                accion();
            }
            catch (ErrorNovedadesVersion ex)
            {
                return ex.Estado;
            }
            return HttpStatusCode.OK;
        }

        [TestMethod]
        public void Normalizar_ValidaVersionAmbitoYCategorias()
        {
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(new CargaNovedadesVersionDTO { Version = "no", Ambito = "Nesto", Novedades = new List<NovedadVersionDTO> { Nov("A") } }, HOY, out _));
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(new CargaNovedadesVersionDTO { Version = "1.10.68.0", Ambito = "Otro", Novedades = new List<NovedadVersionDTO> { Nov("A") } }, HOY, out _));
            // Una 2.x no es de Nesto y una 1.x no es de Ariadna (lección de #703)
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(new CargaNovedadesVersionDTO { Version = "2.45", Ambito = "Nesto", Novedades = new List<NovedadVersionDTO> { Nov("A") } }, HOY, out _));
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(new CargaNovedadesVersionDTO { Version = "1.10.68.0", Ambito = "Ariadna", Novedades = new List<NovedadVersionDTO> { Nov("A") } }, HOY, out _));
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(Carga(Nov("A", "Incidencia")), HOY, out _));
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(Carga(Nov("A"), Nov(" a ")), HOY, out _), "título repetido");
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(Carga(Nov("A", perfiles: new List<string> { "Marcianos" })), HOY, out _));
            Assert.IsNotNull(ReglasNovedadesVersion.Normalizar(Carga(), HOY, out _));

            Assert.IsNull(ReglasNovedadesVersion.Normalizar(Carga(Nov(" A ", "mejorado", perfiles: new List<string> { "almacen" })), HOY, out CargaNormalizada ok));
            Assert.AreEqual("Mejorado", ok.Novedades[0].Categoria);
            Assert.AreEqual("A", ok.Novedades[0].Titulo);
            Assert.AreEqual("Almacén", ok.Novedades[0].PerfilesColumna);
            Assert.AreEqual(HOY, ok.Fecha);
        }

        [TestMethod]
        public void Cargar_CreaLasQueFaltanYNoRepiteLasQueYaEstan()
        {
            A.CallTo(() => repo.LeerDeVersion("1.10.68.0", "Nesto")).Returns(new List<NovedadVersionFila>
            {
                new NovedadVersionFila { Id = 7, Version = "1.10.68.0", Ambito = "Nesto", Titulo = "Ya cargada" }
            });

            ResultadoCargaNovedadesDTO r = servicio.Cargar(Carga(Nov("Nueva"), Nov("ya cargada ")), false, "NUEVAVISION\\Carlos");

            Assert.AreEqual(1, r.Creadas.Count);
            Assert.AreEqual("Nueva", r.Creadas[0].Titulo);
            Assert.AreEqual(100, r.Creadas[0].Id);
            Assert.AreEqual(1, r.YaEstaban.Count);
            Assert.AreEqual(7, r.YaEstaban[0].Id);
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>.That.Matches(l => l.Count == 1 && l[0].Titulo == "Nueva"), "NUEVAVISION\\Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Cargar_Ensayo_NoEscribeNada()
        {
            A.CallTo(() => repo.Leer(55)).Returns(new NovedadVersionFila { Id = 55, EsSugerencia = true });

            ResultadoCargaNovedadesDTO r = servicio.Cargar(Carga(Nov("Nueva"), Nov("Sugerencia hecha", sugerencia: 55)), true, "Carlos");

            Assert.IsTrue(r.Ensayo);
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => novedades.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repo.Anotar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, r.Creadas.Count);
            Assert.IsNull(r.Creadas[0].Id);
            Assert.AreEqual(1, r.SugerenciasImplementadas.Count);
        }

        [TestMethod]
        public void Cargar_ConSugerenciaId_LaImplementaEnVezDeCrearOtra()
        {
            A.CallTo(() => repo.Leer(55)).Returns(new NovedadVersionFila { Id = 55, EsSugerencia = true, Ambito = "Nesto" });

            ResultadoCargaNovedadesDTO r = servicio.Cargar(
                Carga(Nov("Lo que pidió Manuel", "Mejorado", 55, new List<string> { "Tiendas" })), false, "Carlos");

            Assert.AreEqual(0, r.Creadas.Count);
            Assert.AreEqual(55, r.SugerenciasImplementadas.Single().Id);
            A.CallTo(() => novedades.ActualizarSugerencia(55, A<ActualizarSugerenciaNovedadDTO>.That.Matches(c =>
                    c.Version == "1.10.68.0" && c.Estado == ReglasSugerenciasNovedades.ESTADO_IMPLEMENTADA && c.Ambito == "Nesto"
                    && c.Titulo == "Lo que pidió Manuel" && c.Categoria == "Mejorado"), "Carlos"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => novedades.GuardarPerfiles(55, "Tiendas", "Carlos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.Anotar(A<string>.That.StartsWith("SugerenciaImplementada|55"), A<string>._, "Carlos")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Cargar_SugerenciaYaImplementadaEnEstaVersion_NoSeToca()
        {
            A.CallTo(() => repo.Leer(55)).Returns(new NovedadVersionFila { Id = 55, EsSugerencia = true, Version = "1.10.68.0" });

            ResultadoCargaNovedadesDTO r = servicio.Cargar(Carga(Nov("X", sugerencia: 55)), false, "Carlos");

            Assert.AreEqual(55, r.YaEstaban.Single().Id);
            A.CallTo(() => novedades.ActualizarSugerencia(A<int>._, A<ActualizarSugerenciaNovedadDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Cargar_SugerenciaDeOtraVersionONoSugerencia_NoEscribeNada()
        {
            A.CallTo(() => repo.Leer(55)).Returns(new NovedadVersionFila { Id = 55, EsSugerencia = true, Version = "1.10.60.0" });
            A.CallTo(() => repo.Leer(56)).Returns(new NovedadVersionFila { Id = 56, EsSugerencia = false, Version = "1.10.60.0" });

            Assert.AreEqual(HttpStatusCode.Conflict, Estado(() => servicio.Cargar(Carga(Nov("Nueva"), Nov("X", sugerencia: 55)), false, "Carlos")));
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(() => servicio.Cargar(Carga(Nov("Y", sugerencia: 56)), false, "Carlos")));
            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(() => servicio.Cargar(Carga(Nov("Z", sugerencia: 57)), false, "Carlos")));
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Borrar_SoloNovedadesDeVersionSinFeedback()
        {
            A.CallTo(() => repo.Leer(1)).Returns(new NovedadVersionFila { Id = 1, Version = "1.10.68.0" });
            A.CallTo(() => repo.Leer(2)).Returns(new NovedadVersionFila { Id = 2, Version = "1.10.68.0", TieneFeedback = true });
            A.CallTo(() => repo.Leer(3)).Returns(new NovedadVersionFila { Id = 3, Version = "1.10.68.0", EsSugerencia = true });
            A.CallTo(() => repo.Borrar(1, A<string>._, A<string>._)).Returns(true);

            Assert.AreEqual(HttpStatusCode.OK, Estado(() => servicio.Borrar(1, "Carlos")));
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(() => servicio.Borrar(2, "Carlos")));
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(() => servicio.Borrar(3, "Carlos")));
            Assert.AreEqual(HttpStatusCode.NotFound, Estado(() => servicio.Borrar(4, "Carlos")));
            A.CallTo(() => repo.Borrar(A<int>.That.Not.IsEqualTo(1), A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Cambiar_ValidaYNoDejaTituloRepetido()
        {
            A.CallTo(() => repo.Leer(1)).Returns(new NovedadVersionFila { Id = 1, Version = "1.10.68.0", Ambito = "Nesto", Titulo = "Uno" });
            A.CallTo(() => repo.Leer(9)).Returns(new NovedadVersionFila { Id = 9, Version = null, EsSugerencia = true });
            A.CallTo(() => repo.LeerDeVersion("1.10.68.0", "Nesto")).Returns(new List<NovedadVersionFila>
            {
                new NovedadVersionFila { Id = 1, Titulo = "Uno" },
                new NovedadVersionFila { Id = 2, Titulo = "Dos" }
            });
            A.CallTo(() => repo.Actualizar(1, A<CambioNovedadNormalizado>._, A<string>._, A<string>._)).Returns(true);

            Assert.AreEqual(HttpStatusCode.BadRequest, Estado(() => servicio.Cambiar(1, new CambioNovedadVersionDTO(), "Carlos")));
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(() => servicio.Cambiar(1, new CambioNovedadVersionDTO { Titulo = "dos" }, "Carlos")));
            Assert.AreEqual(HttpStatusCode.Conflict, Estado(() => servicio.Cambiar(9, new CambioNovedadVersionDTO { Publicada = false }, "Carlos")));
            Assert.AreEqual(HttpStatusCode.OK, Estado(() => servicio.Cambiar(1, new CambioNovedadVersionDTO { Publicada = false, Perfiles = new List<string>() }, "Carlos")));
            A.CallTo(() => repo.Actualizar(1, A<CambioNovedadNormalizado>.That.Matches(c => c.Publicada == false && c.TocarPerfiles && c.PerfilesColumna == null), A<string>._, "Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        private NovedadesController Controlador(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alguien") };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, g)));
            return new NovedadesController(novedades)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
                NovedadesVersion = servicio
            };
        }

        [TestMethod]
        public void Controlador_SoloDireccionEInformatica()
        {
            IHttpActionResult r = Controlador("NUEVAVISION\\Tiendas").PostNovedadesVersion(Carga(Nov("A")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)r).StatusCode);
            A.CallTo(() => repo.Insertar(A<CargaNormalizada>._, A<IReadOnlyList<NovedadNormalizada>>._, A<string>._)).MustNotHaveHappened();

            Assert.IsInstanceOfType(Controlador("NUEVAVISION\\Dirección").PostNovedadesVersion(Carga(Nov("A"))), typeof(OkNegotiatedContentResult<ResultadoCargaNovedadesDTO>));
            Assert.IsInstanceOfType(Controlador("NUEVAVISION\\Informática").GetNovedadesVersion("1.10.68.0"), typeof(OkNegotiatedContentResult<List<NovedadVersionLeidaDTO>>));
        }

        [TestMethod]
        public void Controlador_ErrorDeRegla_DaSuEstadoYMensaje()
        {
            IHttpActionResult r = Controlador("Dirección").DeleteNovedadVersion(4);
            Assert.AreEqual(HttpStatusCode.NotFound, r.GetType().GetProperty("StatusCode").GetValue(r));
        }
    }
}
