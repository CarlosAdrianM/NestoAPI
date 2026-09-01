using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#406: el job que marca con el sentinel -1 los productos de las familias que venden
    /// al público al mismo precio que al profesional. Sin él, un producto nuevo de esas marcas
    /// sale a la venta un 42,86 % más caro sin dar ningún error.
    /// </summary>
    [TestClass]
    public class SentinelPrecioPublicoJobsServiceTests
    {
        private NVEntities db;
        private DbSet<Familia> fakeFamilias;
        private DbSet<Producto> fakeProductos;
        private DbSet<PrestashopProducto> fakeFichas;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeFamilias = A.Fake<DbSet<Familia>>(o => o.Implements<IQueryable<Familia>>().Implements<IDbAsyncEnumerable<Familia>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            fakeFichas = A.Fake<DbSet<PrestashopProducto>>(o => o.Implements<IQueryable<PrestashopProducto>>().Implements<IDbAsyncEnumerable<PrestashopProducto>>());

            A.CallTo(() => db.Familias).Returns(fakeFamilias);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.PrestashopProductos).Returns(fakeFichas);

            Configurar(new List<Familia>(), new List<Producto>(), new List<PrestashopProducto>());
        }

        private void Configurar(List<Familia> familias, List<Producto> productos, List<PrestashopProducto> fichas)
        {
            ConfigurarFakeDbSet(fakeFamilias, familias.AsQueryable());
            ConfigurarFakeDbSet(fakeProductos, productos.AsQueryable());
            ConfigurarFakeDbSet(fakeFichas, fichas.AsQueryable());
        }

        // Los char llegan de la base de datos CON RELLENO, y los datos de prueba lo replican en
        // Número y Familia a propósito: ahí es donde se rompen las comparaciones (Nesto#254) y el
        // código las recorta explícitamente.
        //
        // Empresa va SIN relleno, y esto merece explicación: se compara con "== EMPRESA_POR_DEFECTO"
        // como en todo el resto del proyecto, apoyándose en que SQL Server ignora el relleno al
        // comparar. En producción esto se traduce a SQL y funciona; en estos tests el filtro se
        // resuelve en memoria, donde "1" y "1  " NO son iguales. Poner aquí el relleno no probaría
        // un fallo real, solo la diferencia entre los dos motores.
        private static Familia Familia(string codigo, bool publicoIgual) => new Familia
        {
            Empresa = "1",
            Número = codigo.PadRight(10),
            Descripción = codigo,
            PublicoIgualQueProfesional = publicoIgual
        };

        private static Producto Producto(string numero, string familia, short estado = 0) => new Producto
        {
            Empresa = "1",
            Número = numero.PadRight(15),
            Familia = familia.PadRight(10),
            Estado = estado
        };

        private static PrestashopProducto Ficha(string numero, decimal? precio) => new PrestashopProducto
        {
            Empresa = "1",
            Número = numero.PadRight(15),
            PVP_IVA_Incluido = precio
        };

        [TestMethod]
        public async Task Sentinel_ProductoNuevoDeFamiliaMarcadaSinFicha_SeCreaLaFichaConElSentinel()
        {
            List<PrestashopProducto> creadas = new List<PrestashopProducto>();
            A.CallTo(() => fakeFichas.Add(A<PrestashopProducto>._)).Invokes((PrestashopProducto p) => creadas.Add(p));

            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks") },
                new List<PrestashopProducto>());

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(1, marcados);
            Assert.AreEqual(1, creadas.Count);
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL, creadas[0].PVP_IVA_Incluido);
            Assert.AreEqual("45001", creadas[0].Número);
        }

        [TestMethod]
        public async Task Sentinel_FichaExistenteSinPrecio_SeRellenaSinTocarNadaMas()
        {
            PrestashopProducto ficha = Ficha("45001", null);
            ficha.Nombre = "Nombre currado a mano";
            ficha.Descripción = "Descripción larga";
            ficha.VistoBueno = true;

            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks") },
                new List<PrestashopProducto> { ficha });

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(1, marcados);
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL, ficha.PVP_IVA_Incluido);
            Assert.AreEqual("Nombre currado a mano", ficha.Nombre, "El trabajo de la ficha no se toca");
            Assert.AreEqual("Descripción larga", ficha.Descripción);
            Assert.AreEqual(true, ficha.VistoBueno);
            A.CallTo(() => fakeFichas.Add(A<PrestashopProducto>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sentinel_ConPrecioPublicoFijo_NoSePisa()
        {
            // Un precio fijo es una decisión deliberada para ESE producto y gana a la regla de la
            // familia. Pisarlo con el sentinel le cambiaría el precio a la web sin pedirlo nadie.
            PrestashopProducto ficha = Ficha("45001", 149.95M);

            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks") },
                new List<PrestashopProducto> { ficha });

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(0, marcados);
            Assert.AreEqual(149.95M, ficha.PVP_IVA_Incluido);
        }

        [TestMethod]
        public async Task Sentinel_YaMarcado_NoHaceNadaNiVuelveAEncolar()
        {
            // Idempotencia: el job corre cada noche sobre el catálogo entero.
            PrestashopProducto ficha = Ficha("45001", Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL);

            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks") },
                new List<PrestashopProducto> { ficha });

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(0, marcados);
            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Any()), A<string>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sentinel_FamiliaSinMarcar_NoSeToca()
        {
            // Ceras Depilatorias es el caso vivo: su descuento es del 25 %, no del 30 %, así que el
            // público NO queda igual y marcarla le BAJARÍA el precio a 52 productos.
            Configurar(
                new List<Familia> { Familia("Staleks", true), Familia("Ceras", false) },
                new List<Producto> { Producto("45001", "Staleks"), Producto("30001", "Ceras") },
                new List<PrestashopProducto>());

            List<PrestashopProducto> creadas = new List<PrestashopProducto>();
            A.CallTo(() => fakeFichas.Add(A<PrestashopProducto>._)).Invokes((PrestashopProducto p) => creadas.Add(p));

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(1, marcados);
            Assert.AreEqual("45001", creadas.Single().Número);
        }

        [TestMethod]
        public async Task Sentinel_ProductoDescatalogado_NoSeToca()
        {
            // Un producto muerto no se va a crear nunca en la tienda: marcarlo es ruido.
            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks", estado: -1) },
                new List<PrestashopProducto>());

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(0, marcados);
        }

        [TestMethod]
        public async Task Sentinel_LoMarcado_SeEncolaParaRepublicar()
        {
            // El precio que la tienda tiene publicado de ese producto es el inflado: si no se
            // republica, el sentinel se queda en la base de datos y el cliente sigue viendo el caro.
            Configurar(
                new List<Familia> { Familia("Staleks", true) },
                new List<Producto> { Producto("45001", "Staleks") },
                new List<PrestashopProducto> { Ficha("45001", null) });

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(1, marcados);
            A.CallTo(() => db.EncolarProductosSync(
                    A<IEnumerable<string>>.That.Matches(l => l.Contains("45001")),
                    SentinelPrecioPublicoJobsService.USUARIO))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Sentinel_SinFamiliasMarcadas_NoConsultaNada()
        {
            Configurar(
                new List<Familia> { Familia("Ceras", false) },
                new List<Producto> { Producto("30001", "Ceras") },
                new List<PrestashopProducto>());

            int marcados = await SentinelPrecioPublicoJobsService.Marcar(db);

            Assert.AreEqual(0, marcados);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
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
