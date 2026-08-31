using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#423 (Slice 2): el job que dispara la republicación de las campañas con fechas.
    ///
    /// Sin él, el Slice 1 está a medias: una campaña caducada dejaría de cobrarse en Nesto pero
    /// seguiría anunciada en la tienda para siempre, porque caducar por fecha no modifica ninguna
    /// fila y ningún detector por [Fecha Modificación] puede verlo.
    /// </summary>
    [TestClass]
    public class VigenciaCampanasJobsServiceTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;
        private DbSet<Producto> fakeProductos;

        private static readonly DateTime HOY = new DateTime(2026, 9, 15);

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeDescuentos = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            fakeProductos = A.Fake<DbSet<Producto>>(o => o.Implements<IQueryable<Producto>>().Implements<IDbAsyncEnumerable<Producto>>());
            A.CallTo(() => db.DescuentosProductoes).Returns(fakeDescuentos);
            A.CallTo(() => db.Productos).Returns(fakeProductos);
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>().AsQueryable());
        }

        /// <summary>
        /// Una ficha de producto. Los char van CON relleno, como llegan de la BD: es donde se
        /// rompen las comparaciones (Nesto#254) y el código las recorta explícitamente.
        /// </summary>
        private static Producto Ficha(string numero, string familia, string grupo = "COS", short estado = 0)
        {
            return new Producto
            {
                Empresa = "1",
                Número = numero.PadRight(15),
                Familia = familia.PadRight(10),
                Grupo = grupo.PadRight(3),
                Estado = estado
            };
        }

        private static DescuentosProducto CampanaDeFamilia(string familia, DateTime? desde, DateTime? hasta,
            string grupo = null, byte audiencia = 2)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Familia = familia.PadRight(10),
                GrupoProducto = grupo?.PadRight(3),
                CantidadMínima = 1,
                Descuento = 0.15m,
                AudienciaOferta = audiencia,
                FechaDesde = desde,
                FechaHasta = hasta
            };
        }

        /// <summary>
        /// Una fila de campaña "normal": de tarifa (sin cliente ni proveedor), no escalonada y
        /// marcada para viajar a la web. Los char van CON relleno, como llegan de la BD.
        /// </summary>
        private static DescuentosProducto Campana(string producto, DateTime? desde, DateTime? hasta,
            byte audiencia = 2, short cantidadMinima = 1)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Nº_Producto = producto.PadRight(15),
                CantidadMínima = cantidadMinima,
                Descuento = 0.20m,
                AudienciaOferta = audiencia,
                FechaDesde = desde,
                FechaHasta = hasta
            };
        }

        private void Configurar(params DescuentosProducto[] filas)
        {
            ConfigurarFakeDbSet(fakeDescuentos, filas.ToList().AsQueryable());
        }

        private async Task<List<string>> Ejecutar()
        {
            return await VigenciaCampanasJobsService.ProductosARepublicar(db, HOY, VigenciaCampanasJobsService.DIAS_VENTANA);
        }

        // El caso que da nombre al slice: la campaña terminó ayer y hay que retirarla de la tienda.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaTerminadaAyer_SeEncola()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1)));

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEqual(new List<string> { "44166" }, productos);
        }

        // El simétrico: la campaña arranca hoy y hay que publicarla.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaQueEmpiezaHoy_SeEncola()
        {
            Configurar(Campana("44166", HOY, HOY.AddDays(30)));

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEqual(new List<string> { "44166" }, productos);
        }

        // Una campaña que lleva meses corriendo y le quedan meses no ha cambiado hoy: republicarla
        // sería trabajo para nada (3 stocks y 2 llamadas HTTP por producto).
        [TestMethod]
        public async Task ProductosARepublicar_CampanaEnMedioDeSuVigencia_NoSeEncola()
        {
            Configurar(Campana("44166", HOY.AddDays(-60), HOY.AddDays(60)));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // Una campaña programada para dentro de un mes tampoco: ya la cogerá la ventana el día que
        // le toque arrancar.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaFutura_TodaviaNoSeEncola()
        {
            Configurar(Campana("44166", HOY.AddDays(30), HOY.AddDays(60)));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // Las filas sin fechas son las 48.870 de siempre: no cambian nunca por vigencia.
        [TestMethod]
        public async Task ProductosARepublicar_FilaSinFechas_NoSeEncolaNunca()
        {
            Configurar(Campana("44166", null, null));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // La ventana de 2 días es la que hace el job tolerante a una noche caída: una campaña que
        // terminó anteayer todavía se recoge.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaTerminadaAnteayer_SeRecogeEnLaVentana()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-2)));

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEqual(new List<string> { "44166" }, productos);
        }

        [TestMethod]
        public async Task ProductosARepublicar_CampanaTerminadaHaceUnaSemana_YaFueraDeLaVentana()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-7)));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // Los mismos filtros que CargarDescuentosPorAudiencia: si la fila no viaja, republicar por
        // ella no cambia nada en la tienda.
        [TestMethod]
        public async Task ProductosARepublicar_AudienciaCero_NoSeEncolaPorqueNoViaja()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1), audiencia: 0));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public async Task ProductosARepublicar_FilaEscalonada_NoSeEncolaPorqueNoViaja()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1), cantidadMinima: 6));

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public async Task ProductosARepublicar_FilaDeCliente_NoSeEncolaPorqueNoViaja()
        {
            DescuentosProducto fila = Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1));
            fila.Nº_Cliente = "2414".PadRight(10);
            Configurar(fila);

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        [TestMethod]
        public async Task ProductosARepublicar_FilaDeProveedor_NoSeEncolaPorqueNoViaja()
        {
            DescuentosProducto fila = Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1));
            fila.NºProveedor = "123".PadRight(10);
            Configurar(fila);

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // Nesto_sync guarda el ModificadoId recortado: es lo que compara la pasada de
        // sincronización, así que aquí no puede salir con el relleno del char(15).
        [TestMethod]
        public async Task ProductosARepublicar_ElNumeroSaleSinElRellenoDelChar()
        {
            Configurar(Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1)));

            List<string> productos = await Ejecutar();

            Assert.AreEqual("44166", productos.Single());
        }

        // Un producto con dos campañas que cambian el mismo día se publica UNA vez: publicar dos
        // veces manda el mismo mensaje y son 3 stocks y 2 HTTP de más.
        [TestMethod]
        public async Task ProductosARepublicar_DosCampanasDelMismoProducto_SeEncolaUnaSolaVez()
        {
            Configurar(
                Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1)),   // una que termina
                Campana("44166", HOY, HOY.AddDays(30)));               // y la siguiente que arranca

            List<string> productos = await Ejecutar();

            Assert.AreEqual(1, productos.Count);
            Assert.AreEqual("44166", productos.Single());
        }

        // ---------------------------------------------------------------------------------
        // Slice 3: campañas por familia. Una fila de marca no la ve ninguna de sus referencias
        // por su cuenta, así que el job tiene que expandirla.
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public async Task ProductosARepublicar_CampanaDeFamiliaTerminada_EncolaLosProductosDeEsaFamilia()
        {
            Configurar(CampanaDeFamilia("Ufaes", HOY.AddDays(-30), HOY.AddDays(-1)));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes"),
                Ficha("44167", "Ufaes"),
                Ficha("99999", "Lisap")
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEquivalent(new List<string> { "44166", "44167" }, productos);
        }

        // La familia se expande sola, así que aquí sí se podan los muertos: Lisap tiene 1.317
        // referencias y solo 843 vivas, y cada republicación son 3 stocks y 2 llamadas HTTP.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaDeFamilia_NoEncolaLosProductosDeBaja()
        {
            Configurar(CampanaDeFamilia("Ufaes", HOY.AddDays(-30), HOY.AddDays(-1)));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes"),
                Ficha("44167", "Ufaes", estado: -1)
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEqual(new List<string> { "44166" }, productos);
        }

        // Nivel 5 del motor: familia Y grupo a la vez, así que solo alcanza a los de ese grupo.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaDeFamiliaYGrupo_SoloLosDeEseGrupo()
        {
            Configurar(CampanaDeFamilia("Ufaes", HOY.AddDays(-30), HOY.AddDays(-1), grupo: "COS"));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes", grupo: "COS"),
                Ficha("44167", "Ufaes", grupo: "PEL")
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            CollectionAssert.AreEqual(new List<string> { "44166" }, productos);
        }

        // Una campaña de marca en medio de su vigencia no ha cambiado hoy: no se toca nada.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaDeFamiliaEnMedioDeSuVigencia_NoEncolaNada()
        {
            Configurar(CampanaDeFamilia("Ufaes", HOY.AddDays(-60), HOY.AddDays(60)));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes")
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        // Un producto alcanzado por su marca Y con campaña propia se encola una sola vez.
        [TestMethod]
        public async Task ProductosARepublicar_ProductoAlcanzadoPorFamiliaYPorSuPropiaFila_UnaSolaVez()
        {
            Configurar(
                CampanaDeFamilia("Ufaes", HOY.AddDays(-30), HOY.AddDays(-1)),
                Campana("44166", HOY.AddDays(-30), HOY.AddDays(-1)));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes")
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            Assert.AreEqual(1, productos.Count);
            Assert.AreEqual("44166", productos.Single());
        }

        // Los mismos filtros de publicación que en el nivel de producto.
        [TestMethod]
        public async Task ProductosARepublicar_CampanaDeFamiliaConAudienciaCero_NoEncolaNada()
        {
            Configurar(CampanaDeFamilia("Ufaes", HOY.AddDays(-30), HOY.AddDays(-1), audiencia: 0));
            ConfigurarFakeDbSet(fakeProductos, new List<Producto>
            {
                Ficha("44166", "Ufaes")
            }.AsQueryable());

            List<string> productos = await Ejecutar();

            Assert.AreEqual(0, productos.Count);
        }

        private void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
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
