using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Videos
{
    /// <summary>
    /// NestoAPI#545: borrar un vídeo desde Nesto solo cuando está DUPLICADO (otro con el mismo
    /// VideoId de YouTube, como 1981/1980 el 25/09/26). Retirar un vídeo es darlo de baja, no borrarlo.
    /// </summary>
    [TestClass]
    public class GestorBorradoVideosTests
    {
        private NVEntities db;
        private DbSet<Video> fakeVideos;
        private DbSet<VideoProducto> fakeVideosProductos;
        private DbSet<LogVideoProducto> fakeLog;
        private GestorBorradoVideos gestor;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeVideos = A.Fake<DbSet<Video>>(o => o.Implements<IQueryable<Video>>().Implements<IDbAsyncEnumerable<Video>>());
            fakeVideosProductos = A.Fake<DbSet<VideoProducto>>(o => o.Implements<IQueryable<VideoProducto>>().Implements<IDbAsyncEnumerable<VideoProducto>>());
            fakeLog = A.Fake<DbSet<LogVideoProducto>>(o => o.Implements<IQueryable<LogVideoProducto>>().Implements<IDbAsyncEnumerable<LogVideoProducto>>());
            A.CallTo(() => db.Videos).Returns(fakeVideos);
            A.CallTo(() => db.VideosProductos).Returns(fakeVideosProductos);
            A.CallTo(() => db.LogVideosProductos).Returns(fakeLog);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            gestor = new GestorBorradoVideos(db);
        }

        private void Datos(IEnumerable<Video> videos, IEnumerable<VideoProducto> productos)
        {
            ConfigurarFakeDbSet(fakeVideos, videos.ToList().AsQueryable());
            ConfigurarFakeDbSet(fakeVideosProductos, productos.ToList().AsQueryable());
        }

        private static List<Video> Duplicados()
        {
            return new List<Video>
            {
                new Video { Id = 1980, VideoId = "abc123", Titulo = "Protocolo facial" },
                new Video { Id = 1981, VideoId = "abc123", Titulo = "Protocolo facial" },
                new Video { Id = 1990, VideoId = "otro", Titulo = "Otro vídeo" }
            };
        }

        private static List<VideoProducto> ProductosDe1981()
        {
            return new List<VideoProducto>
            {
                new VideoProducto { Id = 10, VideoId = 1981, NombreProducto = "Sérum", Referencia = "12345" },
                new VideoProducto { Id = 11, VideoId = 1981, NombreProducto = "Crema", Referencia = "23456" },
                new VideoProducto { Id = 12, VideoId = 1980, NombreProducto = "Sérum", Referencia = "12345" }
            };
        }

        [TestMethod]
        public async Task Borrar_VideoDuplicado_BorraElVideoYSusProductosYLoRegistra()
        {
            Datos(Duplicados(), ProductosDe1981());

            ResultadoBorradoVideo resultado = await gestor.Borrar(1981, forzar: false, puedeForzar: false, usuario: "laura");

            Assert.AreEqual(EstadoBorradoVideo.Borrado, resultado.Estado);
            Assert.AreEqual(1981, resultado.Borrado.Id);
            Assert.AreEqual("abc123", resultado.Borrado.VideoId);
            Assert.AreEqual(2, resultado.Borrado.ProductosBorrados);
            CollectionAssert.AreEqual(new[] { 1980 }, resultado.Borrado.DuplicadoDe);
            A.CallTo(() => fakeVideos.Remove(A<Video>.That.Matches(v => v.Id == 1981))).MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeVideosProductos.RemoveRange(A<IEnumerable<VideoProducto>>.That.Matches(
                l => l.Select(p => p.Id).OrderBy(i => i).SequenceEqual(new[] { 10, 11 })))).MustHaveHappenedOnceExactly();
            // Lo borrado queda en el log, un apunte por producto, para poder rehacerlo.
            A.CallTo(() => fakeLog.Add(A<LogVideoProducto>.That.Matches(l =>
                (l.VideoProductoId == 10 || l.VideoProductoId == 11) &&
                l.Accion == GestorBorradoVideos.ACCION_LOG &&
                l.Usuario == "laura" &&
                l.ValorAnterior.Contains("Referencia: ") &&
                l.Observaciones.Contains("1981") && l.Observaciones.Contains("abc123") && l.Observaciones.Contains("1980"))))
                .MustHaveHappenedTwiceExactly();
            // Todo en un único SaveChanges: EF lo hace en una transacción.
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Borrar_VideoQueNoExiste_NoExisteYNoGuardaNada()
        {
            Datos(Duplicados(), ProductosDe1981());

            ResultadoBorradoVideo resultado = await gestor.Borrar(5000, forzar: false, puedeForzar: true, usuario: "laura");

            Assert.AreEqual(EstadoBorradoVideo.NoExiste, resultado.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Borrar_VideoNoDuplicadoSinForzar_NoSeBorraYDiceQueSeUseLaBaja()
        {
            Datos(Duplicados(), ProductosDe1981());

            ResultadoBorradoVideo resultado = await gestor.Borrar(1990, forzar: false, puedeForzar: true, usuario: "carlos");

            Assert.AreEqual(EstadoBorradoVideo.NoEsDuplicado, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "baja");
            A.CallTo(() => fakeVideos.Remove(A<Video>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Borrar_VideoNoDuplicadoForzandoSinSerDireccion_NoSeBorra()
        {
            Datos(Duplicados(), ProductosDe1981());

            ResultadoBorradoVideo resultado = await gestor.Borrar(1990, forzar: true, puedeForzar: false, usuario: "laura");

            Assert.AreEqual(EstadoBorradoVideo.ForzarNoPermitido, resultado.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Borrar_VideoNoDuplicadoForzandoDesdeDireccion_SeBorra()
        {
            Datos(Duplicados(), ProductosDe1981());

            ResultadoBorradoVideo resultado = await gestor.Borrar(1990, forzar: true, puedeForzar: true, usuario: "carlos");

            Assert.AreEqual(EstadoBorradoVideo.Borrado, resultado.Estado);
            Assert.AreEqual(0, resultado.Borrado.DuplicadoDe.Count);
            A.CallTo(() => fakeVideos.Remove(A<Video>.That.Matches(v => v.Id == 1990))).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Borrar_VideoSinVideoId_NoCuentaComoDuplicadoDeOtroSinVideoId()
        {
            // Dos vídeos sin VideoId no son "el mismo vídeo de YouTube".
            Datos(new List<Video>
            {
                new Video { Id = 1, VideoId = null, Titulo = "A" },
                new Video { Id = 2, VideoId = null, Titulo = "B" }
            }, new List<VideoProducto>());

            ResultadoBorradoVideo resultado = await gestor.Borrar(1, forzar: false, puedeForzar: false, usuario: "laura");

            Assert.AreEqual(EstadoBorradoVideo.NoEsDuplicado, resultado.Estado);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
