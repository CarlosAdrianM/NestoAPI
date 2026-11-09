using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Festivos;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#779: GET/POST/DELETE api/Festivos (la tabla Festivos).</summary>
    [TestClass]
    public class FestivosControllerTests
    {
        private IRepositorioFestivos repositorio;
        private int lecturasCalendario;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFestivos>();
            lecturasCalendario = 0;
            CalendarioFestivos.Configurar(() =>
            {
                lecturasCalendario++;
                return new CalendarioFestivos.LecturaFestivos { HayTabla = false };
            });
        }

        [TestCleanup]
        public void Restaurar() => CalendarioFestivos.Reiniciar();

        private FestivosController Controller(params string[] roles) => new FestivosController(repositorio)
        {
            User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Carlos", "Bearer"), roles),
            Request = new HttpRequestMessage()
        };

        [TestMethod]
        public async Task Get_ConFilasEnLaTabla_DevuelveLasDeLaTabla()
        {
            A.CallTo(() => repositorio.Leer(2027)).Returns(new LecturaFestivosAnio
            {
                HayTabla = true,
                Filas =
                {
                    new FilaTablaFestivo { Fecha = new DateTime(2027, 3, 19), Tipo = "Autonómica", Descripcion = "San José", Usuario = "NUEVAVISION\\Carlos" },
                    new FilaTablaFestivo { Fecha = new DateTime(2027, 1, 1), Tipo = "Nacional", Descripcion = "Año nuevo" }
                }
            });

            var resultado = await Controller().GetFestivos(2027) as OkNegotiatedContentResult<List<FestivoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual(new DateTime(2027, 1, 1), resultado.Content[0].Fecha, "Por fecha");
            Assert.AreEqual("Autonómica", resultado.Content[1].Tipo);
            Assert.IsTrue(resultado.Content.All(f => f.Origen == FestivosController.ORIGEN_TABLA));
        }

        [DataTestMethod]
        [DataRow(false, DisplayName = "Sin tabla")]
        [DataRow(true, DisplayName = "Tabla sin ese año")]
        public async Task Get_SinFilasDelAnio_DevuelveLasDelCodigo(bool hayTabla)
        {
            A.CallTo(() => repositorio.Leer(2026)).Returns(new LecturaFestivosAnio { HayTabla = hayTabla });

            var resultado = await Controller().GetFestivos(2026) as OkNegotiatedContentResult<List<FestivoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(GestorFestivos.FestivosDelCodigo(2026).Count, resultado.Content.Count);
            Assert.IsTrue(resultado.Content.Any(f => f.Fecha == new DateTime(2026, 11, 2) && f.Tipo == "Autonómica"));
            Assert.IsTrue(resultado.Content.All(f => f.Origen == FestivosController.ORIGEN_CODIGO));
        }

        [TestMethod]
        public async Task Get_AnioFueraDeRango_400()
        {
            Assert.IsInstanceOfType(await Controller().GetFestivos(1900), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Post_SinPermiso_403_YNoEscribe()
        {
            var resultado = await Controller("Almacén").PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2027, 3, 19), Tipo = "Autonómica", Descripcion = "San José"
            }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow("Administración")]
        [DataRow("Dirección")]
        [DataRow("Informática")]
        public async Task Post_ConPermiso_Guarda_EInvalidaLaCache(string rol)
        {
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).Returns(ResultadoEscrituraFestivo.Creado);
            _ = CalendarioFestivos.DelAnio(2027);
            int antes = lecturasCalendario;

            var resultado = await Controller(rol).PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2027, 3, 19, 15, 0, 0), Tipo = "autonomica", Descripcion = "  San José  "
            }) as OkNegotiatedContentResult<FestivoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>.That.Matches(f =>
                f.Fecha == new DateTime(2027, 3, 19) && f.Tipo == "Autonómica" && f.Descripcion == "San José"),
                @"NUEVAVISION\Carlos")).MustHaveHappenedOnceExactly();
            _ = CalendarioFestivos.DelAnio(2027);
            Assert.AreEqual(antes + 1, lecturasCalendario, "Al escribir se invalida la caché");
        }

        [DataTestMethod]
        [DataRow("Local", "San José", DisplayName = "Tipo desconocido")]
        [DataRow("Nacional", " ", DisplayName = "Sin descripción")]
        public async Task Post_DatosMalos_400(string tipo, string descripcion)
        {
            var resultado = await Controller("Dirección").PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2027, 3, 19), Tipo = tipo, Descripcion = descripcion
            });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Post_SinTabla_409()
        {
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).Returns(ResultadoEscrituraFestivo.SinTabla);

            var resultado = await Controller("Dirección").PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2027, 3, 19), Tipo = "Autonómica", Descripcion = "San José"
            }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Conflict, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "Issue779_Festivos.sql");
        }

        [TestMethod]
        public async Task Post_DescripcionLarga_SeCortaA100()
        {
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).Returns(ResultadoEscrituraFestivo.Modificado);

            _ = await Controller("Dirección").PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2027, 3, 19), Tipo = "Autonómica", Descripcion = new string('x', 150)
            });

            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>.That.Matches(f => f.Descripcion.Length == 100), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Delete_Existe_200_EInvalida()
        {
            A.CallTo(() => repositorio.Borrar(new DateTime(2027, 3, 19), "Autonómica", A<string>._)).Returns(ResultadoEscrituraFestivo.Borrado);
            _ = CalendarioFestivos.DelAnio(2027);
            int antes = lecturasCalendario;

            var resultado = await Controller("Administración").DeleteFestivo(new DateTime(2027, 3, 19), "Autonomica");

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            _ = CalendarioFestivos.DelAnio(2027);
            Assert.AreEqual(antes + 1, lecturasCalendario);
        }

        [TestMethod]
        public async Task Delete_NoExiste_404()
        {
            A.CallTo(() => repositorio.Borrar(A<DateTime>._, A<string>._, A<string>._)).Returns(ResultadoEscrituraFestivo.NoExiste);

            Assert.IsInstanceOfType(await Controller("Dirección").DeleteFestivo(new DateTime(2027, 3, 19), "Nacional"), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Delete_SinPermiso_403()
        {
            var resultado = await Controller().DeleteFestivo(new DateTime(2027, 3, 19), "Nacional") as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Borrar(A<DateTime>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }


        // ================================================================ NestoAPI#841: día sin reparto propio

        [DataTestMethod]
        [DataRow("Almacén")]
        public async Task Issue841_Post_RepartoPropio_AlmacenPuede_YSeGuardaConSuTipo(string rol)
        {
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).Returns(ResultadoEscrituraFestivo.Creado);

            var resultado = await Controller(rol).PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2026, 11, 9), Tipo = "RepartoPropio", Descripcion = "Andre y Miguel libran (Almudena)"
            }) as OkNegotiatedContentResult<FestivoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("RepartoPropio", resultado.Content.Tipo);
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>.That.Matches(f =>
                f.Fecha == new DateTime(2026, 11, 9) && f.Tipo == "RepartoPropio"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow("Dirección")]
        [DataRow("Administración")]
        public async Task Issue841_Post_RepartoPropio_LosDeSiemprePueden(string rol)
        {
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).Returns(ResultadoEscrituraFestivo.Creado);

            var resultado = await Controller(rol).PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2026, 11, 9), Tipo = "repartopropio", Descripcion = "Sin furgoneta"
            });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<FestivoDTO>));
        }

        [DataTestMethod]
        [DataRow("Almacén", "Madrid")]
        [DataRow("Almacén", "Nacional")]
        public async Task Issue841_Post_OtroTipo_AlmacenSigueSinPoder(string rol, string tipo)
        {
            var resultado = await Controller(rol).PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2026, 11, 9), Tipo = tipo, Descripcion = "Almudena"
            }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            A.CallTo(() => repositorio.Guardar(A<FilaTablaFestivo>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Issue841_Post_RepartoPropio_SinGrupo_403()
        {
            var resultado = await Controller("Vendedores").PostFestivo(new PeticionFestivoDTO
            {
                Fecha = new DateTime(2026, 11, 9), Tipo = "RepartoPropio", Descripcion = "Sin furgoneta"
            }) as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [DataTestMethod]
        [DataRow("Almacén")]
        public async Task Issue841_Delete_RepartoPropio_AlmacenPuede_YOtroTipoNo(string rol)
        {
            A.CallTo(() => repositorio.Borrar(new DateTime(2026, 11, 9), "RepartoPropio", A<string>._)).Returns(ResultadoEscrituraFestivo.Borrado);

            Assert.IsInstanceOfType(await Controller(rol).DeleteFestivo(new DateTime(2026, 11, 9), "RepartoPropio"), typeof(OkResult));

            var otro = await Controller(rol).DeleteFestivo(new DateTime(2026, 11, 9), "Nacional") as NegotiatedContentResult<string>;
            Assert.AreEqual(HttpStatusCode.Forbidden, otro.StatusCode);
        }

        [TestMethod]
        public async Task Issue841_Get_DevuelveLosDiasSinRepartoPropio()
        {
            A.CallTo(() => repositorio.Leer(2026)).Returns(new LecturaFestivosAnio
            {
                HayTabla = true,
                Filas = { new FilaTablaFestivo { Fecha = new DateTime(2026, 11, 9), Tipo = "RepartoPropio", Descripcion = "Sin furgoneta" } }
            });

            var resultado = await Controller().GetFestivos(2026) as OkNegotiatedContentResult<List<FestivoDTO>>;

            Assert.AreEqual("RepartoPropio", resultado.Content.Single().Tipo);
        }
    }
}
