using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
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
    /// NestoAPI#423 (Slice 5): el mantenimiento de campañas. Lo que más importa aquí no es el CRUD
    /// sino las validaciones: una campaña mal metida o rompe el cálculo del precio (duplicados de
    /// #229) o anuncia en la tienda un descuento que Nesto no cobra, que es justo lo que #423
    /// viene a eliminar.
    /// </summary>
    [TestClass]
    public class CampanasControllerTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;
        private DbSet<Producto> fakeProductos;
        private DbSet<Familia> fakeFamilias;
        private CampanasController controller;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeDescuentos = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            fakeFamilias = A.Fake<DbSet<Familia>>(o => o.Implements<IQueryable<Familia>>().Implements<IDbAsyncEnumerable<Familia>>());

            A.CallTo(() => db.DescuentosProductoes).Returns(fakeDescuentos);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.Familias).Returns(fakeFamilias);
            A.CallTo(() => db.EncolarProductoSync(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult(1));

            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>().AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "44166", Familia = "Ufaes", Grupo = "COS", Estado = 0 }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeFamilias, new List<Familia>
            {
                new Familia { Empresa = "1", Número = "Ufaes", Descripción = "Ufaes" }
            }.AsQueryable());

            controller = new CampanasController(db);
        }

        private static CampanaDTO CampanaDeProducto(decimal descuento = 0.20M)
        {
            return new CampanaDTO
            {
                Producto = "44166",
                Descuento = descuento,
                AudienciaOferta = 2,
                FechaDesde = DateTime.Today,
                FechaHasta = DateTime.Today.AddDays(30)
            };
        }

        private static string MensajeDe(System.Web.Http.IHttpActionResult resultado)
        {
            return (resultado as BadRequestErrorMessageResult)?.Message;
        }

        // ---------------------------------------------------------------------------
        // Qué se ve en la pantalla
        // ---------------------------------------------------------------------------

        private static List<CampanaDTO> Listado(System.Web.Http.IHttpActionResult resultado)
        {
            return (resultado as OkNegotiatedContentResult<List<CampanaDTO>>)?.Content;
        }

        /// <summary>
        /// La primera versión escondía las filas sin fechas ni audiencia, y eso dejaba fuera de la
        /// pantalla justo lo que hay que mantener: las 2.016 filas de las rebajas de verano de 2026
        /// tienen AudienciaOferta 0 y ninguna fecha, porque se metieron antes de que existiera el
        /// concepto de campaña. Si no salen, hay que seguir borrándolas por SQL — lo contrario de
        /// para lo que sirve esta pantalla.
        /// </summary>
        [TestMethod]
        public async Task GetCampanas_UnDescuentoDeSiempreSinFechasNiAudiencia_TambienSale()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.30M, AudienciaOferta = 0   // ni fechas ni audiencia: una de las rebajas
                }
            }.AsQueryable());

            List<CampanaDTO> campanas = Listado(await controller.GetCampanas());

            Assert.AreEqual(1, campanas.Count);
            Assert.AreEqual("44166", campanas.Single().Producto);
        }

        // El filtro sigue existiendo, pero hay que pedirlo.
        [TestMethod]
        public async Task GetCampanas_ConSoloCampanas_EscondeLosDescuentosDeSiempre()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.30M, AudienciaOferta = 0
                },
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 501, Nº_Producto = "44167", CantidadMínima = 1,
                    Descuento = 0.20M, AudienciaOferta = 2, FechaHasta = DateTime.Today.AddDays(10)
                }
            }.AsQueryable());

            List<CampanaDTO> campanas = Listado(await controller.GetCampanas(soloCampanas: true));

            Assert.AreEqual(1, campanas.Count);
            Assert.AreEqual("44167", campanas.Single().Producto);
        }

        [TestMethod]
        public async Task GetCampanas_LasCaducadas_NoSalenSalvoQueSePidan()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.30M, AudienciaOferta = 2, FechaHasta = DateTime.Today.AddDays(-1)
                }
            }.AsQueryable());

            Assert.AreEqual(0, Listado(await controller.GetCampanas()).Count);
            Assert.AreEqual(1, Listado(await controller.GetCampanas(incluirCaducadas: true)).Count);
        }

        // Las de cliente, proveedor, filtro y las escalonadas NO son campañas de tarifa: si
        // salieran, se podría borrar desde aquí un precio pactado con un cliente concreto.
        [TestMethod]
        public async Task GetCampanas_NoSacaFilasQueNoSeanDeTarifaPura()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Orden = 1, Nº_Producto = "44166", CantidadMínima = 1, Nº_Cliente = "2414" },
                new DescuentosProducto { Empresa = "1", Nº_Orden = 2, Nº_Producto = "44166", CantidadMínima = 1, NºProveedor = "123" },
                new DescuentosProducto { Empresa = "1", Nº_Orden = 3, Familia = "Lisap", CantidadMínima = 1, FiltroProducto = "LK ANTIAGE" },
                new DescuentosProducto { Empresa = "1", Nº_Orden = 4, Nº_Producto = "44166", CantidadMínima = 6 }
            }.AsQueryable());

            Assert.AreEqual(0, Listado(await controller.GetCampanas()).Count);
        }

        // ---------------------------------------------------------------------------
        // Validaciones de nivel: los tres que el motor aplica de verdad, y solo esos.
        // ---------------------------------------------------------------------------

        // ---------------------------------------------------------------------------
        // NestoAPI#437: precio fijo de tarifa ("este producto a 10 €"), con sus fechas.
        // ---------------------------------------------------------------------------

        [TestMethod]
        public async Task PostCampana_ConPrecioFijo_LoGuardaYLoDevuelve()
        {
            CampanaDTO campana = CampanaDeProducto(descuento: 0M);
            campana.PrecioFijo = 10M;

            var resultado = await controller.PostCampana(campana)
                as System.Web.Http.Results.OkNegotiatedContentResult<CampanaDTO>;

            Assert.IsNotNull(resultado, "Debería haberse creado");
            Assert.AreEqual(10M, resultado.Content.PrecioFijo);
        }

        [TestMethod]
        public async Task PostCampana_PrecioFijoEnFamilia_Rechaza()
        {
            // El motor de precios solo lee Precio de las filas de tarifa CON producto: una fila
            // de familia con precio fijo no se la cobraría nadie, pero se anunciaría.
            CampanaDTO campana = new CampanaDTO
            {
                Familia = "Ufaes",
                PrecioFijo = 10M,
                AudienciaOferta = 2,
                FechaDesde = DateTime.Today,
                FechaHasta = DateTime.Today.AddDays(30)
            };

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "precio fijo solo puede ir en una fila de producto");
        }

        [TestMethod]
        public async Task PostCampana_PrecioFijoCeroONegativo_Rechaza()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.PrecioFijo = 0M;

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "mayor que cero");
        }

        [TestMethod]
        public async Task PostCampana_SinProductoNiFamilia_Rechaza()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.Producto = null;

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "producto O de una familia");
        }

        [TestMethod]
        public async Task PostCampana_ConProductoYFamiliaALaVez_Rechaza()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.Familia = "Ufaes";

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "producto O de una familia");
        }

        /// <summary>
        /// El hallazgo del Slice 3: el motor no tiene ningún nivel de tarifa que mire SOLO el
        /// grupo (el nivel 5 exige familia Y grupo). Una campaña solo por grupo no se la cobraría
        /// nadie, así que anunciarla sería mentir.
        /// </summary>
        [TestMethod]
        public async Task PostCampana_SoloPorGrupo_Rechaza()
        {
            CampanaDTO campana = new CampanaDTO { Grupo = "COS", Descuento = 0.20M, AudienciaOferta = 2 };

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "junto a una familia");
        }

        [TestMethod]
        public async Task PostCampana_ProductoQueNoExiste_Rechaza()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.Producto = "99999";

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "no existe");
        }

        [TestMethod]
        public async Task PostCampana_FamiliaQueNoExiste_Rechaza()
        {
            CampanaDTO campana = new CampanaDTO { Familia = "MarcaInventada", Descuento = 0.15M, AudienciaOferta = 2 };

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "no existe");
        }

        // ---------------------------------------------------------------------------
        // Validaciones de contenido
        // ---------------------------------------------------------------------------

        // El descuento va en tanto por uno: meter "20" en vez de "0,20" sería un 2.000 %.
        [TestMethod]
        public async Task PostCampana_DescuentoMayorQueUno_Rechaza()
        {
            string mensaje = MensajeDe(await controller.PostCampana(CampanaDeProducto(descuento: 20M)));

            StringAssert.Contains(mensaje, "tanto por uno");
        }

        [TestMethod]
        public async Task PostCampana_AudienciaSoloPublico_RechazaYExplicaPorQue()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.AudienciaOferta = 3;

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "prohibido");
        }

        [TestMethod]
        public async Task PostCampana_FechasAlReves_Rechaza()
        {
            CampanaDTO campana = CampanaDeProducto();
            campana.FechaDesde = DateTime.Today.AddDays(10);
            campana.FechaHasta = DateTime.Today;

            string mensaje = MensajeDe(await controller.PostCampana(campana));

            StringAssert.Contains(mensaje, "no puede ser posterior");
        }

        // ---------------------------------------------------------------------------
        // Solapes: encadenar campañas vale; solaparlas rompe el cálculo del precio (#229).
        // ---------------------------------------------------------------------------

        [TestMethod]
        public async Task PostCampana_SolapaConOtraDelMismoNivel_Rechaza()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.10M, AudienciaOferta = 2,
                    FechaDesde = DateTime.Today.AddDays(-5), FechaHasta = DateTime.Today.AddDays(5)
                }
            }.AsQueryable());

            string mensaje = MensajeDe(await controller.PostCampana(CampanaDeProducto()));

            StringAssert.Contains(mensaje, "se solapan");
        }

        // Justo lo que las fechas vienen a permitir: una acaba el día antes de que empiece la otra.
        [TestMethod]
        public async Task PostCampana_EncadenadaConLaAnterior_SeAdmite()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.10M, AudienciaOferta = 2,
                    FechaDesde = DateTime.Today.AddDays(-30), FechaHasta = DateTime.Today.AddDays(-1)
                }
            }.AsQueryable());

            System.Web.Http.IHttpActionResult resultado = await controller.PostCampana(CampanaDeProducto());

            Assert.IsNull(MensajeDe(resultado), "No debería rechazarla");
        }

        // Una campaña de producto y otra de su familia NO son el mismo nivel: pueden convivir, y
        // el cargador ya sabe cuál gana.
        [TestMethod]
        public async Task PostCampana_MismasFechasPeroDistintoNivel_SeAdmite()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Familia = "Ufaes", CantidadMínima = 1,
                    Descuento = 0.10M, AudienciaOferta = 2,
                    FechaDesde = DateTime.Today, FechaHasta = DateTime.Today.AddDays(30)
                }
            }.AsQueryable());

            System.Web.Http.IHttpActionResult resultado = await controller.PostCampana(CampanaDeProducto());

            Assert.IsNull(MensajeDe(resultado), "Producto y familia son niveles distintos");
        }

        // ---------------------------------------------------------------------------
        // Republicación: sin esto la tienda no se entera hasta la madrugada, o nunca.
        // ---------------------------------------------------------------------------

        [TestMethod]
        public async Task PostCampana_DeProducto_EncolaEseProducto()
        {
            _ = await controller.PostCampana(CampanaDeProducto());

            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Contains("44166")), A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostCampana_DeFamilia_EncolaTodosLosProductosVivosDeLaMarca()
        {
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = "44166", Familia = "Ufaes", Grupo = "COS", Estado = 0 },
                new Producto { Empresa = "1", Número = "44167", Familia = "Ufaes", Grupo = "COS", Estado = 0 },
                new Producto { Empresa = "1", Número = "44168", Familia = "Ufaes", Grupo = "COS", Estado = -1 }
            }.AsQueryable());

            _ = await controller.PostCampana(new CampanaDTO
            {
                Familia = "Ufaes", Descuento = 0.15M, AudienciaOferta = 2,
                FechaDesde = DateTime.Today, FechaHasta = DateTime.Today.AddDays(30)
            });

            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Contains("44166") && l.Contains("44167") && !l.Contains("44168")),
                    A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        /// <summary>
        /// Lo que pasó el 31/08 con el DELETE a mano: la fila desaparece sin dejar rastro que
        /// ningún disparador pueda detectar. Borrar por aquí TIENE que republicar.
        /// </summary>
        [TestMethod]
        public async Task DeleteCampana_RepublicaLoQueAlcanzaba()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", CantidadMínima = 1,
                    Descuento = 0.10M, AudienciaOferta = 2
                }
            }.AsQueryable());

            _ = await controller.DeleteCampana(500);

            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Contains("44166")), A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task DeleteCampana_QueNoExiste_DevuelveNotFound()
        {
            System.Web.Http.IHttpActionResult resultado = await controller.DeleteCampana(12345);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // Una fila de cliente no es una campaña y no se puede tocar desde aquí: se colaría un
        // descuento pactado con un cliente concreto en la pantalla de campañas.
        [TestMethod]
        public async Task DeleteCampana_FilaDeCliente_NoLaEncuentra()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Orden = 500, Nº_Producto = "44166", Nº_Cliente = "2414",
                    CantidadMínima = 1, Descuento = 0.10M
                }
            }.AsQueryable());

            System.Web.Http.IHttpActionResult resultado = await controller.DeleteCampana(500);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // ---------------------------------------------------------------------------
        // Operaciones en bloque por nombre de campaña (Slice 6)
        // ---------------------------------------------------------------------------

        private static DescuentosProducto FilaDeCampana(int orden, string producto, string nombre, byte audiencia = 0)
        {
            return new DescuentosProducto
            {
                Empresa = "1", Nº_Orden = orden, Nº_Producto = producto, CantidadMínima = 1,
                Descuento = 0.30M, AudienciaOferta = audiencia, Campana = nombre
            };
        }

        /// <summary>
        /// EL TEST QUE JUSTIFICA EL SLICE. Las 2.017 filas de las rebajas de verano de 2026 son
        /// todas de AudienciaOferta 0: no viajan a la tienda, así que borrarlas no cambia ni un
        /// byte del mensaje de ningún producto y NO hay que republicar nada.
        ///
        /// Sin este filtro, borrar esa campaña encolaría más de dos mil productos: 3 stocks y 2
        /// llamadas HTTP cada uno, horas de job contra PrestaShop, para mandar exactamente lo
        /// mismo que ya tenía.
        /// </summary>
        [TestMethod]
        public async Task DeleteCampanaPorNombre_FilasQueNoViajan_BorraPeroNoEncolaNada()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                FilaDeCampana(500, "44166", "Rebajas verano 2026"),
                FilaDeCampana(501, "44167", "Rebajas verano 2026"),
                FilaDeCampana(502, "44168", "Rebajas verano 2026")
            }.AsQueryable());

            var resultado = await controller.DeleteCampanaPorNombre("Rebajas verano 2026")
                as OkNegotiatedContentResult<ResultadoOperacionCampanaDTO>;

            Assert.AreEqual(3, resultado.Content.FilasAfectadas);
            Assert.AreEqual(0, resultado.Content.ProductosEncolados);
            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Any()), A<string>._))
                .MustNotHaveHappened();
        }

        // Y al revés: si la campaña sí se anunciaba, hay que retirarla de la tienda.
        [TestMethod]
        public async Task DeleteCampanaPorNombre_FilasQueSiViajan_EncolaSusProductos()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                FilaDeCampana(500, "44166", "Black Friday 2026", audiencia: 2),
                FilaDeCampana(501, "44167", "Black Friday 2026", audiencia: 0)
            }.AsQueryable());

            var resultado = await controller.DeleteCampanaPorNombre("Black Friday 2026")
                as OkNegotiatedContentResult<ResultadoOperacionCampanaDTO>;

            Assert.AreEqual(2, resultado.Content.FilasAfectadas);
            Assert.AreEqual(1, resultado.Content.ProductosEncolados);
            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Contains("44166") && !l.Contains("44167")), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        // Una operación en bloque no puede llevarse por delante lo que no es suyo.
        [TestMethod]
        public async Task DeleteCampanaPorNombre_NoTocaLasFilasDeOtraCampana()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                FilaDeCampana(500, "44166", "Rebajas verano 2026"),
                FilaDeCampana(501, "44167", "Black Friday 2026")
            }.AsQueryable());

            var resultado = await controller.DeleteCampanaPorNombre("Rebajas verano 2026")
                as OkNegotiatedContentResult<ResultadoOperacionCampanaDTO>;

            Assert.AreEqual(1, resultado.Content.FilasAfectadas);
        }

        // Ni las que no son de tarifa, aunque alguien las hubiera etiquetado a mano: por ahí se
        // podría borrar un precio pactado con un cliente concreto.
        [TestMethod]
        public async Task DeleteCampanaPorNombre_NoTocaFilasDeCliente()
        {
            DescuentosProducto deCliente = FilaDeCampana(501, "44167", "Rebajas verano 2026");
            deCliente.Nº_Cliente = "2414";
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                FilaDeCampana(500, "44166", "Rebajas verano 2026"),
                deCliente
            }.AsQueryable());

            var resultado = await controller.DeleteCampanaPorNombre("Rebajas verano 2026")
                as OkNegotiatedContentResult<ResultadoOperacionCampanaDTO>;

            Assert.AreEqual(1, resultado.Content.FilasAfectadas);
        }

        [TestMethod]
        public async Task DeleteCampanaPorNombre_CampanaQueNoExiste_DevuelveNotFound()
        {
            System.Web.Http.IHttpActionResult resultado = await controller.DeleteCampanaPorNombre("No existe");

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // Cerrar es la operación preferible: deja traza y es reversible.
        [TestMethod]
        public async Task CerrarCampana_PoneFechaHastaYRepublicaLoQueSeAnunciaba()
        {
            DescuentosProducto viaja = FilaDeCampana(500, "44166", "Black Friday 2026", audiencia: 2);
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto> { viaja }.AsQueryable());

            var resultado = await controller.CerrarCampana("Black Friday 2026", new DateTime(2026, 12, 1))
                as OkNegotiatedContentResult<ResultadoOperacionCampanaDTO>;

            Assert.AreEqual(new DateTime(2026, 12, 1), viaja.FechaHasta);
            Assert.AreEqual(1, resultado.Content.ProductosEncolados);
        }

        // Por defecto se cierra con fecha de AYER: "que deje de aplicarse ya".
        [TestMethod]
        public async Task CerrarCampana_SinFecha_LaDejaCaducadaDesdeAyer()
        {
            DescuentosProducto fila = FilaDeCampana(500, "44166", "Black Friday 2026", audiencia: 2);
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto> { fila }.AsQueryable());

            _ = await controller.CerrarCampana("Black Friday 2026");

            Assert.AreEqual(DateTime.Today.AddDays(-1), fila.FechaHasta);
        }

        [TestMethod]
        public async Task GetNombresDeCampana_ResumeCadaUnaConSusNumeros()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                FilaDeCampana(500, "44166", "Rebajas verano 2026"),
                FilaDeCampana(501, "44167", "Rebajas verano 2026"),
                FilaDeCampana(502, "44168", "Black Friday 2026", audiencia: 2),
                new DescuentosProducto { Empresa = "1", Nº_Orden = 503, Nº_Producto = "44169", CantidadMínima = 1 }
            }.AsQueryable());

            var resultado = await controller.GetNombresDeCampana()
                as OkNegotiatedContentResult<List<ResumenCampanaDTO>>;

            Assert.AreEqual(2, resultado.Content.Count, "La fila sin campaña no forma grupo");
            ResumenCampanaDTO rebajas = resultado.Content.Single(c => c.Campana == "Rebajas verano 2026");
            Assert.AreEqual(2, rebajas.Filas);
            Assert.AreEqual(0, rebajas.FilasQueViajan, "Ninguna de las rebajas se anuncia en la tienda");
            Assert.AreEqual(1, resultado.Content.Single(c => c.Campana == "Black Friday 2026").FilasQueViajan);
        }

        // ---------------------------------------------------------------------------
        // El solapamiento de rangos abiertos, aparte
        // ---------------------------------------------------------------------------

        [TestMethod]
        public void SeSolapan_DosRangosSinFechas_SeSolapanSiempre()
        {
            Assert.IsTrue(CampanasController.SeSolapan(null, null, null, null));
        }

        [TestMethod]
        public void SeSolapan_UnaSinFechasYOtraConEllas_SeSolapan()
        {
            Assert.IsTrue(CampanasController.SeSolapan(null, null,
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));
        }

        [TestMethod]
        public void SeSolapan_Consecutivas_NoSeSolapan()
        {
            Assert.IsFalse(CampanasController.SeSolapan(
                new DateTime(2026, 8, 1), new DateTime(2026, 8, 31),
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));
        }

        [TestMethod]
        public void SeSolapan_CompartenUnSoloDia_SeSolapan()
        {
            Assert.IsTrue(CampanasController.SeSolapan(
                new DateTime(2026, 8, 1), new DateTime(2026, 9, 1),
                new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));
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
