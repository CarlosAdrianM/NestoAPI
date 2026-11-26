using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>NestoAPI#906 (Carlos 26/11/26, «evitará disputas»): foto por bulto al enviar la reposición.</summary>
    [TestClass]
    public class FotosBultosReposicionTests
    {
        private const string EMPRESA = "1";
        private const int TRASPASO = 81463;
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 12, 30, 0);

        private IRepositorioFotosBultosReposicion repositorio;
        private IAlmacenFotosBultos fotos;
        private List<FilaFotoBultoReposicion> filas;

        // JPEG mínimo: FF D8 FF + algo
        private static readonly string JPEG = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFotosBultosReposicion>();
            fotos = A.Fake<IAlmacenFotosBultos>();
            filas = new List<FilaFotoBultoReposicion>
            {
                new FilaFotoBultoReposicion { Traspaso = TRASPASO, Bulto = 1, Codigo = "R81463-1" },
                new FilaFotoBultoReposicion { Traspaso = TRASPASO, Bulto = 2, Codigo = "R81463-2", RutaBlob = "reposiciones/1/81463/bulto-2-x.jpg", FechaFoto = AHORA }
            };
            A.CallTo(() => repositorio.TablaCreada()).Returns(true);
            A.CallTo(() => repositorio.LeerOrigenDestino(EMPRESA, TRASPASO)).Returns(Task.FromResult(new OrigenDestinoReposicion { Origen = "ALC", Destino = "ALG" }));
            A.CallTo(() => repositorio.Leer(EMPRESA, A<IReadOnlyCollection<int>>._)).ReturnsLazily(() => Task.FromResult(filas.ToList()));
            A.CallTo(() => fotos.Configurado).Returns(true);
            A.CallTo(() => fotos.EnlaceDeLectura(A<string>._, A<TimeSpan>._)).ReturnsLazily((string ruta, TimeSpan _) => new Uri("https://blob/" + ruta));
        }

        /// <summary>«Prepara» quien tiene el almacén (aquí: Paloma en ALC, Andre en ALG) o Almacén/Dirección (el resto de la regla, como PuedeEscribir).</summary>
        private static bool PuedeEscribir(IPrincipal usuario, string empresa, string almacen)
        {
            string nombre = usuario?.Identity?.Name ?? string.Empty;
            return nombre.EndsWith("Almacen") || (nombre.EndsWith("Paloma") && almacen == "ALC") || (nombre.EndsWith("Andre") && almacen == "ALG");
        }

        private ServicioFotosBultosReposicion Servicio() => new ServicioFotosBultosReposicion(repositorio, fotos, PuedeEscribir, () => AHORA);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, @"NUEVAVISION\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, @"NUEVAVISION\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static async Task<NestoBusinessException> Lanza(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex;
            }
            Assert.Fail("Tenía que lanzar NestoBusinessException");
            return null;
        }

        [TestMethod]
        public async Task Subir_QuienLaPrepara_GuardaEnElBlobYEnLaTabla()
        {
            FotoBultoReposicionDTO r = await Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO { FotoBase64 = JPEG }, Usuario("Paloma"));

            Assert.AreEqual("R81463-1", r.Bulto);
            Assert.AreEqual(AHORA, r.Fecha);
            StringAssert.StartsWith(r.Ruta, "reposiciones/1/81463/bulto-1-");
            A.CallTo(() => fotos.Subir(r.Ruta, A<byte[]>.That.Matches(b => b.Length == 6), "image/jpeg")).MustHaveHappenedOnceExactly()
                .Then(A.CallTo(() => repositorio.Guardar(EMPRESA, TRASPASO, 1, r.Ruta, AHORA, A<string>._, 6)).MustHaveHappenedOnceExactly());
        }

        [TestMethod]
        public async Task Subir_ElDestinoNoPuede_403()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO { FotoBase64 = JPEG }, Usuario("Andre")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Subir_BultoQueNoExiste_404()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 7, new SubirFotoBultoReposicionDTO { FotoBase64 = JPEG }, Usuario("Paloma")));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Subir_SinFotoONoJpeg_400()
        {
            NestoBusinessException sin = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO(), Usuario("Paloma")));
            Assert.AreEqual(HttpStatusCode.BadRequest, sin.StatusCode);
            Assert.AreEqual(ReglasFotosBultosReposicion.MENSAJE_SIN_FOTO, sin.Message);

            string png = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            NestoBusinessException noJpeg = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO { FotoBase64 = png }, Usuario("Paloma")));
            StringAssert.Contains(noJpeg.Message, "JPEG");
        }

        [TestMethod]
        public async Task Subir_SinTablaOSinBlob_503()
        {
            A.CallTo(() => repositorio.TablaCreada()).Returns(false);
            NestoBusinessException sinTabla = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO { FotoBase64 = JPEG }, Usuario("Paloma")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, sinTabla.StatusCode);

            A.CallTo(() => repositorio.TablaCreada()).Returns(true);
            A.CallTo(() => fotos.Configurado).Returns(false);
            NestoBusinessException sinBlob = await Lanza(() => Servicio().Subir(EMPRESA, TRASPASO, 1, new SubirFotoBultoReposicionDTO { FotoBase64 = JPEG }, Usuario("Paloma")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, sinBlob.StatusCode);
        }

        [TestMethod]
        public async Task Enlace_OrigenDestinoYDireccion_SiLosDemasNo()
        {
            Assert.IsNotNull(await Servicio().Enlace(EMPRESA, TRASPASO, 2, Usuario("Paloma")), "el origen");
            Assert.IsNotNull(await Servicio().Enlace(EMPRESA, TRASPASO, 2, Usuario("Andre")), "el destino");
            Assert.IsNotNull(await Servicio().Enlace(EMPRESA, TRASPASO, 2, Usuario("Carlos", "Dirección")), "Dirección");

            NestoBusinessException ex = await Lanza(() => Servicio().Enlace(EMPRESA, TRASPASO, 2, Usuario("Laura")));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task Enlace_BultoSinFoto_404()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().Enlace(EMPRESA, TRASPASO, 1, Usuario("Paloma")));

            Assert.AreEqual(HttpStatusCode.NotFound, ex.StatusCode);
            StringAssert.Contains(ex.Message, "R81463-1");
        }

        [TestMethod]
        public async Task Completar_Seguimiento_MarcaLosQueTienenFoto()
        {
            var seguimiento = new SeguimientoReposicionDTO
            {
                Traspaso = TRASPASO,
                Bultos = new List<BultoSeguimientoReposicionDTO> { new BultoSeguimientoReposicionDTO { Bulto = 1 }, new BultoSeguimientoReposicionDTO { Bulto = 2 } }
            };

            await Servicio().Completar(EMPRESA, seguimiento);

            Assert.IsFalse(seguimiento.Bultos[0].TieneFoto);
            Assert.IsTrue(seguimiento.Bultos[1].TieneFoto);
        }

        [TestMethod]
        public async Task Completar_Discusiones_UnaLecturaParaTodas()
        {
            var discusiones = new List<DiscusionDiferenciaDTO> { new DiscusionDiferenciaDTO { Traspaso = TRASPASO }, new DiscusionDiferenciaDTO { Traspaso = TRASPASO } };

            await Servicio().Completar(EMPRESA, discusiones);

            Assert.AreEqual(2, discusiones[0].Bultos.Count);
            Assert.IsTrue(discusiones[1].Bultos.Single(b => b.Bulto == 2).TieneFoto);
            A.CallTo(() => repositorio.Leer(EMPRESA, A<IReadOnlyCollection<int>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Completar_SinTablaOSiFalla_NoLanzaYNadaTieneFoto()
        {
            A.CallTo(() => repositorio.TablaCreada()).Returns(false);
            var discusion = new DiscusionDiferenciaDTO { Traspaso = TRASPASO };
            await Servicio().Completar(EMPRESA, new[] { discusion });
            Assert.AreEqual(0, discusion.Bultos.Count);

            A.CallTo(() => repositorio.TablaCreada()).Returns(true);
            A.CallTo(() => repositorio.Leer(EMPRESA, A<IReadOnlyCollection<int>>._)).Throws(new InvalidOperationException("caída"));
            var seguimiento = new SeguimientoReposicionDTO { Traspaso = TRASPASO, Bultos = new List<BultoSeguimientoReposicionDTO> { new BultoSeguimientoReposicionDTO { Bulto = 2 } } };
            await Servicio().Completar(EMPRESA, seguimiento);
            Assert.IsFalse(seguimiento.Bultos[0].TieneFoto);
        }

        [TestMethod]
        public void Sql_LaFotoDeAntesDeRehacerLosBultosNoCuenta()
        {
            StringAssert.Contains(RepositorioFotosBultosReposicionSql.SQL_LEER, "f.Fecha >= b.FechaCreacion");
            StringAssert.Contains(RepositorioFotosBultosReposicionSql.SQL_GUARDAR, "IF @@ROWCOUNT = 0");
        }
    }
}
