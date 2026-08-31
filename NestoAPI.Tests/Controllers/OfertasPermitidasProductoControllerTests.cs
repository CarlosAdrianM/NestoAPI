using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Mantenimiento de las ofertas "6+2" de un producto. Nace de la petición del 31/08/2026 de
    /// poner el 6+2 del 44724: hasta entonces solo se metían desde Nesto viejo, donde no se les
    /// puede poner fecha, así que apagar una oferta era borrar la fila y acordarse.
    /// </summary>
    [TestClass]
    public class OfertasPermitidasProductoControllerTests
    {
        private NVEntities db;
        private DbSet<OfertaPermitida> fakeOfertas;
        private DbSet<Producto> fakeProductos;
        private OfertasPermitidasProductoController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeOfertas = A.Fake<DbSet<OfertaPermitida>>(o => o.Implements<IQueryable<OfertaPermitida>>().Implements<IDbAsyncEnumerable<OfertaPermitida>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.OfertasPermitidas).Returns(fakeOfertas);
            A.CallTo(() => db.Productos).Returns(fakeProductos);

            Configurar();
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "44724", Nombre = "SERUM LEVEL LASH" }
            }.AsQueryable());

            controller = new OfertasPermitidasProductoController(db);
        }

        private void Configurar(params OfertaPermitida[] ofertas)
        {
            ConfigurarFakeDbSet(fakeOfertas, ofertas.ToList().AsQueryable());
        }

        private static OfertaPermitida Oferta(int orden, string producto, DateTime? desde = null, DateTime? hasta = null,
            string cliente = null, string filtro = null)
        {
            return new OfertaPermitida
            {
                Empresa = "1", NºOrden = orden, Número = producto,
                CantidadConPrecio = 6, CantidadRegalo = 2,
                FechaDesde = desde, FechaHasta = hasta, Cliente = cliente, FiltroProducto = filtro
            };
        }

        private static OfertaPermitidaProductoCreateDTO Nueva(DateTime? desde = null, DateTime? hasta = null)
        {
            return new OfertaPermitidaProductoCreateDTO
            {
                Producto = "44724", CantidadConPrecio = 6, CantidadRegalo = 2,
                FechaDesde = desde, FechaHasta = hasta
            };
        }

        private static List<OfertaPermitidaProductoDTO> Listado(System.Web.Http.IHttpActionResult r) =>
            (r as OkNegotiatedContentResult<List<OfertaPermitidaProductoDTO>>)?.Content;

        private static string MensajeDe(System.Web.Http.IHttpActionResult r) =>
            (r as BadRequestErrorMessageResult)?.Message;

        // ------------------------------------------------------------------
        // Qué se ve
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task GetOfertas_TraeElNombreDelProducto()
        {
            Configurar(Oferta(792, "44724"));

            OfertaPermitidaProductoDTO oferta = Listado(await controller.GetOfertasPermitidasProducto()).Single();

            Assert.AreEqual("44724", oferta.Producto);
            Assert.AreEqual("SERUM LEVEL LASH", oferta.ProductoNombre);
            Assert.AreEqual(6, oferta.CantidadConPrecio);
            Assert.AreEqual(2, oferta.CantidadRegalo);
        }

        /// <summary>
        /// Las ofertas de un cliente concreto NO se tocan desde aquí: son otra cosa y su sitio es
        /// la ficha de ese cliente. Si salieran, se podría borrar desde una pantalla general un
        /// acuerdo con un cliente.
        /// </summary>
        [TestMethod]
        public async Task GetOfertas_NoSacaLasDeUnClienteConcreto()
        {
            Configurar(Oferta(792, "44724"), Oferta(793, "44724", cliente: "2414"));

            Assert.AreEqual(1, Listado(await controller.GetOfertasPermitidasProducto()).Count);
        }

        [TestMethod]
        public async Task GetOfertas_LasCaducadas_NoSalenSalvoQueSePidan()
        {
            Configurar(Oferta(792, "44724", hasta: DateTime.Today.AddDays(-1)));

            Assert.AreEqual(0, Listado(await controller.GetOfertasPermitidasProducto()).Count);
            Assert.AreEqual(1, Listado(await controller.GetOfertasPermitidasProducto(incluirCaducadas: true)).Count);
        }

        [TestMethod]
        public async Task GetOfertas_MarcaSiEstaVigenteHoy()
        {
            Configurar(
                Oferta(792, "44724", desde: DateTime.Today.AddDays(-1), hasta: DateTime.Today.AddDays(1)),
                Oferta(793, "44724", desde: DateTime.Today.AddDays(10)));

            List<OfertaPermitidaProductoDTO> ofertas = Listado(await controller.GetOfertasPermitidasProducto());

            Assert.IsTrue(ofertas.Single(o => o.NOrden == 792).Vigente);
            Assert.IsFalse(ofertas.Single(o => o.NOrden == 793).Vigente, "La que empieza dentro de 10 días no está vigente");
        }

        // ------------------------------------------------------------------
        // Validaciones
        // ------------------------------------------------------------------

        [TestMethod]
        public async Task Post_ProductoQueNoExiste_Rechaza()
        {
            OfertaPermitidaProductoCreateDTO dto = Nueva();
            dto.Producto = "99999";

            StringAssert.Contains(MensajeDe(await controller.PostOfertaPermitidaProducto(dto)), "no existe");
        }

        [TestMethod]
        public async Task Post_SinCantidades_Rechaza()
        {
            OfertaPermitidaProductoCreateDTO dto = Nueva();
            dto.CantidadRegalo = 0;

            StringAssert.Contains(MensajeDe(await controller.PostOfertaPermitidaProducto(dto)), "al menos 1");
        }

        [TestMethod]
        public async Task Post_FechasAlReves_Rechaza()
        {
            OfertaPermitidaProductoCreateDTO dto = Nueva(DateTime.Today.AddDays(10), DateTime.Today);

            StringAssert.Contains(MensajeDe(await controller.PostOfertaPermitidaProducto(dto)), "no puede ser posterior");
        }

        // Dos ofertas vigentes a la vez sobre el mismo producto dejan el pedido a merced de cuál
        // se lea primero.
        [TestMethod]
        public async Task Post_SolapaConOtraDelMismoProducto_Rechaza()
        {
            Configurar(Oferta(792, "44724", desde: DateTime.Today.AddDays(-5), hasta: DateTime.Today.AddDays(5)));

            StringAssert.Contains(MensajeDe(await controller.PostOfertaPermitidaProducto(Nueva())), "se solapan");
        }

        // Sin fechas por ninguna parte también se solapan: las dos valen siempre.
        [TestMethod]
        public async Task Post_SobreUnaOfertaSinFechas_Rechaza()
        {
            Configurar(Oferta(792, "44724"));

            StringAssert.Contains(MensajeDe(await controller.PostOfertaPermitidaProducto(Nueva())), "se solapan");
        }

        /// <summary>
        /// Lo que las fechas vienen a permitir: encadenar una campaña detrás de otra sin tener que
        /// borrar la anterior a mano ni acordarse de nada.
        /// </summary>
        [TestMethod]
        public async Task Post_EncadenadaConLaAnterior_SeAdmite()
        {
            Configurar(Oferta(792, "44724", desde: DateTime.Today.AddDays(-30), hasta: DateTime.Today.AddDays(-1)));

            System.Web.Http.IHttpActionResult resultado = await controller.PostOfertaPermitidaProducto(
                Nueva(DateTime.Today, DateTime.Today.AddDays(30)));

            Assert.IsNull(MensajeDe(resultado), "Una oferta que empieza cuando acaba la otra no se solapa");
        }

        [TestMethod]
        public async Task Post_DistintoFiltroDeProducto_NoEsDuplicada()
        {
            Configurar(Oferta(792, "44724", filtro: "SERUM"));

            Assert.IsNull(MensajeDe(await controller.PostOfertaPermitidaProducto(Nueva())));
        }

        [TestMethod]
        public async Task Delete_DeUnClienteConcreto_NoLaEncuentra()
        {
            Configurar(Oferta(792, "44724", cliente: "2414"));

            Assert.IsInstanceOfType(await controller.DeleteOfertaPermitidaProducto(792), typeof(NotFoundResult));
        }

        [TestMethod]
        public async Task Put_DeUnaQueNoExiste_DevuelveNotFound()
        {
            Assert.IsInstanceOfType(await controller.PutOfertaPermitidaProducto(12345, Nueva()), typeof(NotFoundResult));
        }

        // ------------------------------------------------------------------
        // El solapamiento con extremos abiertos, aparte
        // ------------------------------------------------------------------

        [TestMethod]
        public void SeSolapan_DosSinFechas_SeSolapanSiempre()
        {
            Assert.IsTrue(OfertasPermitidasProductoController.SeSolapan(null, null, null, null));
        }

        [TestMethod]
        public void SeSolapan_Consecutivas_NoSeSolapan()
        {
            Assert.IsFalse(OfertasPermitidasProductoController.SeSolapan(
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30),
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        }

        [TestMethod]
        public void SeSolapan_CompartenUnSoloDia_SeSolapan()
        {
            Assert.IsTrue(OfertasPermitidasProductoController.SeSolapan(
                new DateTime(2026, 9, 1), new DateTime(2026, 10, 1),
                new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
