using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;

namespace NestoAPI.Tests.Models
{
    /// <summary>
    /// Contrato del campo <c>PrestashopProductos.PVP_IVA_Incluido</c>. Tiene tres modos y uno de
    /// ellos es un sentinel, así que conviene que estén fijados por tests: este campo alimenta el
    /// precio de venta del cliente PUBLICO_FINAL, y un valor mal interpretado se convierte en un
    /// precio real de un pedido.
    ///
    ///   · positivo → precio público con IVA, fijado a mano
    ///   · NULL     → el público se deriva del PVP con el descuento por defecto (30 %)
    ///   · -1       → público = profesional
    ///
    /// Desde el cutover de precios (26/08/2026, módulo NestoSync 1.4.0) NestoAPI es EL DUEÑO del
    /// cálculo: los tres modos se resuelven en local y por el bus solo viajan los dos precios
    /// absolutos (profesional y público). El modo es información interna de Nesto; cuando un
    /// sistema externo publica sus precios, la intención se deduce con InferirModoPrecioPublico.
    /// </summary>
    [TestClass]
    public class PrecioPublicoFinalTests
    {
        [TestMethod]
        public void ResolverPrecioPublicoFinal_ValorPositivo_LoDevuelveTalCual()
        {
            // Precio fijado a mano: se sirve tal cual, sin fórmulas.
            Assert.AreEqual(29.95M, ProductoDTO.ResolverPrecioPublicoFinal(29.95M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Nulo_NoEsUnPrecio()
        {
            // El caso mayoritario (10.006 de 10.322 productos el 25/08/2026): el público se
            // calcula del PVP con el 30 %.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(null),
                "NULL es una intención (regla general del 30 %), no un precio");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Sentinel_NoEsUnPrecio()
        {
            Assert.IsNull(
                ProductoDTO.ResolverPrecioPublicoFinal(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL),
                "El sentinel es una intención (público = profesional), no un precio");
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_Cero_NoEsUnPrecio()
        {
            // Comportamiento de siempre: el 0 nunca viajó como precio.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(0M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_CualquierNegativo_NoViajaComoPrecio()
        {
            // REGRESIÓN: antes bastaba con ser distinto de 0 para devolverse como precio. Un -1
            // habría salido como precio público en la ficha y, dividido por el IVA en la plantilla
            // de PUBLICO_FINAL, como precio de venta de -0,83 €.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-1M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-5M));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(-0.01M));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_FijoSinVistoBueno_NoEsUnPrecio()
        {
            // NestoAPI#411: la puerta de publicación. Un precio fijado a mano sin revisar no
            // viaja; se cae al derivado (30 %), que no necesita revisión.
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(29.95M, vistoBueno: null));
            Assert.IsNull(ProductoDTO.ResolverPrecioPublicoFinal(29.95M, vistoBueno: false));
        }

        [TestMethod]
        public void ResolverPrecioPublicoFinal_FijoConVistoBueno_Viaja()
        {
            Assert.AreEqual(29.95M, ProductoDTO.ResolverPrecioPublicoFinal(29.95M, vistoBueno: true));
        }

        // ===== Cálculo del precio público desde el PVP =====

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_IvaGeneral_AplicaElDescuentoDelTreintaYElIva()
        {
            // PVP / 0,7 × 1,21. El profesional es el público MENOS el 30 %, así que se divide;
            // multiplicar por 1,30 daría un 9,9 % menos.
            Assert.AreEqual(17.29M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_IvaReducido_UsaElDiez()
        {
            Assert.AreEqual(15.71M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 10M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_ProductoExento_NoSumaIva()
        {
            // Hay 82 productos vivos exentos. El atajo "si no es R10, 1,21" que usan otros puntos
            // del código les habría añadido un 21 % que no les corresponde.
            Assert.AreEqual(14.29M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 0M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_Superreducido_UsaElCuatro()
        {
            Assert.AreEqual(14.86M, ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 4M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_RedondeaAlAlzaComoPrestashop()
        {
            // PrestaShop usa PS_PRICE_ROUND_MODE = HALF_UP, que es AwayFromZero: el céntimo tiene
            // que coincidir con el que muestra la web o el mostrador cobraría distinto.
            // 7,25 / 0,7 = 10,357142... × 1,21 = 12,53214... → 12,53
            Assert.AreEqual(12.53M, ProductoDTO.CalcularPrecioPublicoDesdePvp(7.25M, 21M));
            // Caso que cae justo en el medio: 2,893424... redondea al alza, no a par
            Assert.AreEqual(2.90M, ProductoDTO.CalcularPrecioPublicoDesdePvp(1.6757M, 21M));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_SiempreMayorQueElProfesionalConIva()
        {
            // Invariante de negocio: el público nunca puede salir por debajo del profesional, o
            // estaríamos vendiendo en tienda más barato que a los profesionales.
            decimal pvp = 12.50M;
            decimal profesionalConIva = pvp * 1.21M;

            Assert.IsTrue(ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, 21M) > profesionalConIva);
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_MismoQueProfesional_NoAplicaElTreinta()
        {
            // El sentinel -1: público = profesional + IVA. Aplicarle el 30 % lo dejaría un 42,86 %
            // por encima de lo que muestra la web.
            Assert.AreEqual(12.10M,
                ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M, mismoQueProfesional: true));
        }

        [TestMethod]
        public void CalcularPrecioPublicoDesdePvp_MismoQueProfesional_EsMasBaratoQueElModoNormal()
        {
            decimal conDescuento = ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M);
            decimal mismoPrecio = ProductoDTO.CalcularPrecioPublicoDesdePvp(10M, 21M, mismoQueProfesional: true);

            Assert.IsTrue(mismoPrecio < conDescuento,
                "El modo 'mismo precio' nunca puede salir mas caro que el que lleva el 30 %");
        }

        // ===== Inferencia del modo al recibir precios de fuera (PrestaShop, Odoo) =====
        //
        // La operación inversa: del par (público, PVP) que llega por el bus se deduce la intención
        // que hay que guardar en PVP_IVA_Incluido. Tolerancia de DOS CÉNTIMOS (decidida el
        // 26/08/2026): PHP y C# pueden redondear con distintos decimales por el camino.

        [TestMethod]
        public void InferirModoPrecioPublico_ElDerivadoExacto_GuardaNull()
        {
            // PVP 10, IVA 21: derivado = 17,29. Es la regla general → NULL.
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.29M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_ElDerivadoConDosCentimosDeBaile_SigueSiendoNull()
        {
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.31M, 10M, 21M));
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(17.27M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_IgualQueElProfesional_GuardaElSentinel()
        {
            // PVP 10, IVA 21: profesional con IVA = 12,10. Público igual → -1.
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.10M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_IgualQueElProfesionalConDosCentimos_SigueSiendoSentinel()
        {
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.12M, 10M, 21M));
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(12.08M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_TresCentimosYaNoEsIgual_GuardaElPrecio()
        {
            // La tolerancia son DOS céntimos, sin rangos generosos: desconocido = precio fijo.
            Assert.AreEqual(17.32M, ProductoDTO.InferirModoPrecioPublico(17.32M, 10M, 21M));
            Assert.AreEqual(12.13M, ProductoDTO.InferirModoPrecioPublico(12.13M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_PrecioQueNoSaleDeNingunaFormula_GuardaElPrecio()
        {
            Assert.AreEqual(29.95M, ProductoDTO.InferirModoPrecioPublico(29.95M, 10M, 21M));
        }

        [TestMethod]
        public void InferirModoPrecioPublico_ProductoExento_CompararSinIva()
        {
            // Un curso exento: PVP 715, derivado = 715/0,7 = 1.021,43; profesional = 715.
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(1021.43M, 715M, 0M));
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(715M, 715M, 0M));
        }

        // ===== Contrato de serialización con los consumidores =====

        /// <summary>
        /// El módulo de PrestaShop distingue (estilo <c>array_key_exists</c>) entre clave con
        /// valor, clave presente con null (= no tocar el texto de la tienda) y clave AUSENTE.
        /// Si algún día se serializara con <c>WhenWritingNull</c> u opciones que omitan nulls,
        /// los textos dejarían de comportarse como "no tocar" SIN NINGÚN ERROR VISIBLE. Este test
        /// fija que las claves viajan presentes, porque el publisher usa los defaults de
        /// System.Text.Json y no hay nada explícito que lo garantice.
        /// </summary>
        [TestMethod]
        public void MensajeProductos_TextosNulos_LasClavesViajanPresentesConNull()
        {
            var mensaje = new NestoAPI.Models.Sincronizacion.ProductoSyncMessage
            {
                Tabla = "Productos",
                Source = "Nesto",
                Producto = "17404",
                NombrePersonalizado = null,
                Descripcion = null,
                DescripcionBreve = null
            };

            // Exactamente como en GooglePubSubEventPublisher: como object y sin opciones.
            string json = System.Text.Json.JsonSerializer.Serialize((object)mensaje);

            StringAssert.Contains(json, "\"NombrePersonalizado\":null");
            StringAssert.Contains(json, "\"Descripcion\":null");
            StringAssert.Contains(json, "\"DescripcionBreve\":null");
        }

        [TestMethod]
        public void InferirModoPrecioPublico_LaIdaYLaVueltaCierran()
        {
            // Round-trip: lo que Nesto publica con un modo, al volver de la tienda se infiere como
            // ESE MISMO modo. Si esto se rompe, cada ciclo de sincronización cambiaría el modo.
            decimal pvp = 24.6M;
            decimal iva = 21M;

            decimal publicadoDerivado = ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, iva);
            Assert.IsNull(ProductoDTO.InferirModoPrecioPublico(publicadoDerivado, pvp, iva));

            decimal publicadoMismo = ProductoDTO.CalcularPrecioPublicoDesdePvp(pvp, iva, mismoQueProfesional: true);
            Assert.AreEqual(Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                ProductoDTO.InferirModoPrecioPublico(publicadoMismo, pvp, iva));
        }
    }

    /// <summary>
    /// NestoAPI#411: sin VistoBueno los datos de PrestashopProductos NO viajan. Es la regresión
    /// respecto al proceso legacy, que solo publicaba nombre/descripciones/precio fijo con
    /// VistoBueno = 1 (para eso existe la pestaña Revisar): sin la puerta, un texto a medio
    /// escribir o un precio sin revisar salen a la web en cuanto algo toca el producto.
    ///
    /// ⚠️ Los modos NULL y -1 NO se gatean: los pone un proceso deliberado (la pantalla o el
    /// script del sentinel del cutover), y las filas del sentinel tienen VistoBueno NULL —
    /// gatearlas las devolvería al 30 % y desharía el cutover del 26/08/2026.
    /// Foto de producción del 27/08/2026: 0 filas con precio fijo o textos sin VistoBueno = 1
    /// (las 617 con VB NULL son todas sentinel), así que la puerta entra sin backfill.
    /// </summary>
    [TestClass]
    public class VistoBuenoPuertaPublicacionTests
    {
        private NVEntities db;
        private DbSet<PrestashopProducto> fakePrestashop;
        private DbSet<Producto> fakeProductos;
        private DbSet<ParametroIVA> fakeParametros;

        private const string PRODUCTO = "17404";

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakePrestashop = A.Fake<DbSet<PrestashopProducto>>(o =>
                o.Implements<IQueryable<PrestashopProducto>>().Implements<IDbAsyncEnumerable<PrestashopProducto>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o =>
                o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            fakeParametros = A.Fake<DbSet<ParametroIVA>>(o =>
                o.Implements<IQueryable<ParametroIVA>>().Implements<IDbAsyncEnumerable<ParametroIVA>>());

            A.CallTo(() => db.PrestashopProductos).Returns(fakePrestashop);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            A.CallTo(() => db.ParametrosIVA).Returns(fakeParametros);

            // Ficha con PVP 10 e IVA general: derivado (30 %) = 17,29; profesional con IVA = 12,10.
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                new Producto { Empresa = "1", Número = PRODUCTO, PVP = 10M, IVA_Repercutido = "G21" }
            }.AsQueryable());
            ConfigurarFakeDbSet(fakeParametros, new List<ParametroIVA>
            {
                new ParametroIVA { Empresa = "1", IVA_Producto = "G21", IVA_Cliente_Prov = "G21", C__IVA = 21M }
            }.AsQueryable());
        }

        private void FilaPrestashop(PrestashopProducto fila)
        {
            ConfigurarFakeDbSet(fakePrestashop,
                new List<PrestashopProducto> { fila }.AsQueryable());
        }

        // ===== Precio fijo =====

        [TestMethod]
        public async Task LeerPrecioPublicoFinal_FijoConVistoBueno_SirveElFijo()
        {
            FilaPrestashop(new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                PVP_IVA_Incluido = 50M,
                VistoBueno = true
            });

            Assert.AreEqual(50M, await ProductoDTO.LeerPrecioPublicoFinal(PRODUCTO, db));
        }

        [TestMethod]
        public async Task LeerPrecioPublicoFinal_FijoSinVistoBueno_CaeAlDerivado()
        {
            // La puerta: un precio fijado a mano que nadie ha revisado no se sirve; se cae a la
            // regla general del 30 %, que no necesita revisión.
            FilaPrestashop(new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                PVP_IVA_Incluido = 50M,
                VistoBueno = null
            });

            Assert.AreEqual(17.29M, await ProductoDTO.LeerPrecioPublicoFinal(PRODUCTO, db));
        }

        [TestMethod]
        public async Task LeerPrecioPublicoFinal_FijoConVistoBuenoAFalse_CaeAlDerivado()
        {
            FilaPrestashop(new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                PVP_IVA_Incluido = 50M,
                VistoBueno = false
            });

            Assert.AreEqual(17.29M, await ProductoDTO.LeerPrecioPublicoFinal(PRODUCTO, db));
        }

        // ===== Los modos NULL y -1 no se gatean =====

        [TestMethod]
        public async Task LeerPrecioPublicoFinal_SentinelConVistoBuenoNull_SigueSiendoElProfesional()
        {
            // Las filas del sentinel del cutover tienen VistoBueno NULL (617 el 27/08/2026).
            // Si la puerta las tocara, volverían al 30 % y la web subiría un 42,86 %.
            FilaPrestashop(new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                PVP_IVA_Incluido = Constantes.Productos.PVP_IVA_MISMO_QUE_PROFESIONAL,
                VistoBueno = null
            });

            Assert.AreEqual(12.10M, await ProductoDTO.LeerPrecioPublicoFinal(PRODUCTO, db));
        }

        [TestMethod]
        public async Task LeerPrecioPublicoFinal_ModoNullConVistoBuenoNull_DerivadoComoSiempre()
        {
            FilaPrestashop(new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                PVP_IVA_Incluido = null,
                VistoBueno = null
            });

            Assert.AreEqual(17.29M, await ProductoDTO.LeerPrecioPublicoFinal(PRODUCTO, db));
        }

        // ===== Textos de tienda =====

        private static PrestashopProducto FilaConTextos(bool? vistoBueno)
        {
            return new PrestashopProducto
            {
                Empresa = "1",
                Número = PRODUCTO,
                Nombre = "Nombre bonito para la web",
                Descripción = "Descripción completa",
                DescripciónBreve = "Breve",
                VistoBueno = vistoBueno
            };
        }

        [TestMethod]
        public async Task CargarTextosTienda_ConVistoBueno_LosTextosViajan()
        {
            FilaPrestashop(FilaConTextos(vistoBueno: true));
            var dto = new ProductoDTO { Producto = PRODUCTO };

            await ProductoDTO.CargarTextosTienda(dto, db);

            Assert.AreEqual("Nombre bonito para la web", dto.NombrePersonalizado);
            Assert.AreEqual("Descripción completa", dto.Descripcion);
            Assert.AreEqual("Breve", dto.DescripcionBreve);
        }

        [TestMethod]
        public async Task CargarTextosTienda_SinVistoBueno_LosTextosNoViajan()
        {
            // null en el mensaje = "no tocar lo que tenga la tienda": un texto a medio escribir
            // se queda en casa hasta que alguien lo revise.
            FilaPrestashop(FilaConTextos(vistoBueno: false));
            var dto = new ProductoDTO { Producto = PRODUCTO };

            await ProductoDTO.CargarTextosTienda(dto, db);

            Assert.IsNull(dto.NombrePersonalizado);
            Assert.IsNull(dto.Descripcion);
            Assert.IsNull(dto.DescripcionBreve);
        }

        [TestMethod]
        public async Task CargarTextosTienda_VistoBuenoNull_LosTextosNoViajan()
        {
            FilaPrestashop(FilaConTextos(vistoBueno: null));
            var dto = new ProductoDTO { Producto = PRODUCTO };

            await ProductoDTO.CargarTextosTienda(dto, db);

            Assert.IsNull(dto.NombrePersonalizado);
            Assert.IsNull(dto.Descripcion);
            Assert.IsNull(dto.DescripcionBreve);
        }

        // ===== NestoAPI#415: tipo de IVA en el DTO que viaja =====

        [TestMethod]
        public async Task CargarTipoIva_TipoConocido_CargaCodigoYPorcentaje()
        {
            var dto = new ProductoDTO { Producto = PRODUCTO };

            await ProductoDTO.CargarTipoIva(dto, db, "G21");

            Assert.AreEqual("G21", dto.TipoIva);
            Assert.AreEqual(21M, dto.PorcentajeIva);
        }

        [TestMethod]
        public async Task CargarTipoIva_TipoSinParametro_ElCodigoViajaYElPorcentajeCaeAlGeneral()
        {
            // Igual que LeerPorcentajeIvaProducto: un tipo que no está en ParametrosIVA usa el
            // general (equivocarse al alza nunca regala nada), pero el CÓDIGO viaja tal cual
            // para que el consumidor pueda mapearlo o quejarse.
            var dto = new ProductoDTO { Producto = PRODUCTO };

            await ProductoDTO.CargarTipoIva(dto, db, "RARO");

            Assert.AreEqual("RARO", dto.TipoIva);
            Assert.AreEqual(21M, dto.PorcentajeIva);
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
