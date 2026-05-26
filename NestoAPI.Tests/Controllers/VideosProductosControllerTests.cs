using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class VideosProductosControllerTests
    {
        private NVEntities db;
        private VideosProductosController controller;
        private DbSet<VideoProducto> fakeVideosProductos;
        private DbSet<LogVideoProducto> fakeLogVideosProductos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeVideosProductos = A.Fake<DbSet<VideoProducto>>(o => o.Implements<IQueryable<VideoProducto>>().Implements<IDbAsyncEnumerable<VideoProducto>>());
            fakeLogVideosProductos = A.Fake<DbSet<LogVideoProducto>>(o => o.Implements<IQueryable<LogVideoProducto>>().Implements<IDbAsyncEnumerable<LogVideoProducto>>());

            A.CallTo(() => db.VideosProductos).Returns(fakeVideosProductos);
            A.CallTo(() => db.LogVideosProductos).Returns(fakeLogVideosProductos);

            controller = new VideosProductosController(db);

            // El controller exige usuario autenticado con claim IsEmployee=true
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim("IsEmployee", "true"));
            identity.AddClaim(new Claim(ClaimTypes.Name, "TestUser"));
            controller.User = new ClaimsPrincipal(identity);
        }

        // Nesto#360: hasta ahora el PUT no aceptaba NombreProducto. Si el nombre
        // del vídeo está mal escrito, el usuario no podía corregirlo desde la
        // pantalla "Reportar error" (el campo era de solo lectura). Tras el fix,
        // si el DTO trae NombreProducto distinto al de BD, se actualiza y se
        // registra en LogVideosProductos.
        [TestMethod]
        public async Task Actualizar_NombreProductoNuevo_ActualizaEntidadYRegistraLog()
        {
            // Arrange
            var vp = new VideoProducto
            {
                Id = 42,
                NombreProducto = "Alta Frecuencia",
                Referencia = "REF1",
                EnlaceTienda = "https://tienda.example/alta",
                TiempoAparicion = "120"
            };
            A.CallTo(() => fakeVideosProductos.FindAsync(42)).Returns(Task.FromResult(vp));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new VideosProductosController.ActualizacionVideoProductoDto
            {
                NombreProducto = "Alta Frecuencia (corregido)",
                Observaciones = "Estaba mal escrito"
            };

            // Act
            var resultado = await controller.Actualizar(42, dto);

            // Assert: el campo se actualiza en la entidad y se guarda.
            Assert.AreEqual("Alta Frecuencia (corregido)", vp.NombreProducto);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();

            // Y se registra el cambio en LogVideosProductos con el campo correcto.
            A.CallTo(() => fakeLogVideosProductos.Add(A<LogVideoProducto>.That.Matches(
                l => l.CampoModificado == "NombreProducto" &&
                     l.ValorAnterior == "Alta Frecuencia" &&
                     l.ValorNuevo == "Alta Frecuencia (corregido)" &&
                     l.Accion == "Actualizacion" &&
                     l.Usuario == "TestUser" &&
                     l.Observaciones == "Estaba mal escrito"))).MustHaveHappenedOnceExactly();
        }

        // Caracterización: si el DTO trae el mismo NombreProducto que la BD, no
        // se considera cambio (filtro != en el controller).
        [TestMethod]
        public async Task Actualizar_NombreProductoIgual_NoRegistraCambio()
        {
            // Arrange
            var vp = new VideoProducto
            {
                Id = 7,
                NombreProducto = "Crema antiarrugas"
            };
            A.CallTo(() => fakeVideosProductos.FindAsync(7)).Returns(Task.FromResult(vp));

            var dto = new VideosProductosController.ActualizacionVideoProductoDto
            {
                NombreProducto = "Crema antiarrugas"
            };

            // Act
            await controller.Actualizar(7, dto);

            // Assert: ningún log de NombreProducto, ningún Save.
            A.CallTo(() => fakeLogVideosProductos.Add(A<LogVideoProducto>.That.Matches(
                l => l.CampoModificado == "NombreProducto"))).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        // Regresión: el DTO sin NombreProducto (null) no debe pisar el nombre
        // existente. Importante porque clientes anteriores al fix no incluían
        // este campo.
        [TestMethod]
        public async Task Actualizar_DtoSinNombreProducto_NoPisaElExistente()
        {
            // Arrange
            var vp = new VideoProducto
            {
                Id = 8,
                NombreProducto = "Tratamiento facial",
                EnlaceTienda = "https://tienda.example/old"
            };
            A.CallTo(() => fakeVideosProductos.FindAsync(8)).Returns(Task.FromResult(vp));
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            var dto = new VideosProductosController.ActualizacionVideoProductoDto
            {
                NombreProducto = null, // cliente legacy
                EnlaceTienda = "https://tienda.example/new"
            };

            // Act
            await controller.Actualizar(8, dto);

            // Assert: el nombre se queda como estaba, solo cambia el enlace.
            Assert.AreEqual("Tratamiento facial", vp.NombreProducto);
            Assert.AreEqual("https://tienda.example/new", vp.EnlaceTienda);
        }
    }
}
