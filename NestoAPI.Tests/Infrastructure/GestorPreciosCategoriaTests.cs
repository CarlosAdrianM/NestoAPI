using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#467: nivel de tarifa familia + grupo + subgrupo, donde la categoría del producto vale
    /// tanto la principal de la ficha como cualquier secundaria (ProductosCategoriasSecundarias).
    /// Es lo que permite que "Maystar en COS/OUT al 15 %" sea UNA fila y que un producto de Maystar
    /// que entre mañana en Outlet herede el descuento sin tocar nada.
    ///
    /// Precedencia: familia → familia+grupo → familia+grupo+subgrupo (los tres asignan, el último
    /// pisa) → producto (gana solo si es mayor).
    /// </summary>
    [TestClass]
    public class GestorPreciosCategoriaTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;
        private DbSet<ProductoCategoriaSecundaria> fakeCategorias;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeDescuentos = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            fakeCategorias = A.Fake<DbSet<ProductoCategoriaSecundaria>>(o => o.Implements<IQueryable<ProductoCategoriaSecundaria>>().Implements<IDbAsyncEnumerable<ProductoCategoriaSecundaria>>());
            A.CallTo(() => db.DescuentosProductoes).Returns(fakeDescuentos);
            A.CallTo(() => db.ProductosCategoriasSecundarias).Returns(fakeCategorias);
            ConfigurarFakeDbSet(fakeCategorias, new List<ProductoCategoriaSecundaria>().AsQueryable());
        }

        private const string PRODUCTO = "40001";

        private static Producto Maystar(string subGrupoPrincipal = "ACB")
        {
            return new Producto
            {
                Empresa = "1",
                Número = PRODUCTO,
                Nombre = "CREMA MAYSTAR",
                PVP = 100m,
                Familia = "Maystar",
                Grupo = "COS",
                SubGrupo = subGrupoPrincipal,
                Aplicar_Dto = true
            };
        }

        private static DescuentosProducto DeFamilia(decimal descuento)
        {
            return new DescuentosProducto { Empresa = "1", Familia = "Maystar", CantidadMínima = 1, Descuento = descuento, Producto = new Producto { Número = "OTRO" } };
        }

        private static DescuentosProducto DeFamiliaYGrupo(decimal descuento)
        {
            return new DescuentosProducto { Empresa = "1", Familia = "Maystar", GrupoProducto = "COS", CantidadMínima = 1, Descuento = descuento, Producto = new Producto { Número = "OTRO" } };
        }

        private static DescuentosProducto DeCategoria(decimal descuento, string grupo = "COS", string subGrupo = "OUT", short cantidadMinima = 1, string familia = "Maystar")
        {
            return new DescuentosProducto { Empresa = "1", Familia = familia, GrupoProducto = grupo, SubGrupoProducto = subGrupo, CantidadMínima = cantidadMinima, Descuento = descuento, Producto = new Producto { Número = "OTRO" } };
        }

        private static DescuentosProducto DeProducto(decimal descuento)
        {
            return new DescuentosProducto { Empresa = "1", Nº_Producto = PRODUCTO, CantidadMínima = 1, Descuento = descuento, Producto = new Producto { Número = PRODUCTO } };
        }

        private void Descuentos(params DescuentosProducto[] filas)
        {
            ConfigurarFakeDbSet(fakeDescuentos, filas.ToList().AsQueryable());
        }

        private void Secundarias(params string[] categorias)
        {
            List<ProductoCategoriaSecundaria> filas = categorias.Select((c, i) => new ProductoCategoriaSecundaria
            {
                Empresa = "1",
                Número = PRODUCTO,
                Orden = i + 1,
                Grupo = c.Split('/')[0],
                SubGrupo = c.Split('/')[1]
            }).ToList();
            ConfigurarFakeDbSet(fakeCategorias, filas.AsQueryable());
        }

        private static decimal Calcular(Producto producto, NVEntities db, short cantidad = 1)
        {
            PrecioDescuentoProducto datos = new PrecioDescuentoProducto
            {
                producto = producto,
                cliente = "15191",
                contacto = "0",
                cantidad = cantidad,
                aplicarDescuento = true
            };
            GestorPrecios.calcularDescuentoProducto(datos, db);
            return datos.descuentoCalculado;
        }

        // ----- EL TEST QUE JUSTIFICA EL ORDEN: primero en rojo -----

        /// <summary>
        /// Sin el `SubGrupoProducto == null` en el nivel familia+grupo, la fila "Maystar en COS/OUT"
        /// se leería como "Maystar en COS" y descontaría el 15 % a TODA la cosmética de Maystar. Y con
        /// las dos filas a la vez, BuscarDescuentoUnico reventaría con "descuentos duplicados".
        /// </summary>
        [TestMethod]
        public void LaFilaConSubgrupo_NoContaminaElNivelFamiliaGrupo()
        {
            Descuentos(DeFamiliaYGrupo(0.10m), DeCategoria(0.15m));

            decimal descuento = Calcular(Maystar(), db);

            Assert.AreEqual(0.10m, descuento, "un Maystar de cosmética que NO está en Outlet se lleva el 10 % de familia+grupo, no el 15 % del Outlet");
        }

        // ----- El nivel nuevo -----

        [TestMethod]
        public void CategoriaSecundaria_AplicaElDescuento()
        {
            Descuentos(DeCategoria(0.15m));
            Secundarias("COS/OUT");

            Assert.AreEqual(0.15m, Calcular(Maystar(), db));
        }

        [TestMethod]
        public void CategoriaPrincipal_AplicaElDescuento()
        {
            Descuentos(DeCategoria(0.15m));

            Assert.AreEqual(0.15m, Calcular(Maystar(subGrupoPrincipal: "OUT"), db));
        }

        [TestMethod]
        public void ProductoDeLaMarcaYElGrupoPeroSinLaCategoria_NoAplica()
        {
            Descuentos(DeCategoria(0.15m));
            Secundarias("COS/107");

            Assert.AreEqual(0m, Calcular(Maystar(), db), "es Maystar y es COS, pero no está en Outlet");
        }

        [TestMethod]
        public void OtraMarcaEnLaMismaCategoria_NoAplica()
        {
            Descuentos(DeCategoria(0.15m, familia: "Lisap"));
            Secundarias("COS/OUT");

            Assert.AreEqual(0m, Calcular(Maystar(), db));
        }

        // ----- Precedencia -----

        [TestMethod]
        public void LaCategoriaSobrescribeALaFamiliaYAlGrupoAunqueSeaMenor()
        {
            Descuentos(DeFamilia(0.30m), DeFamiliaYGrupo(0.20m), DeCategoria(0.15m));
            Secundarias("COS/OUT");

            Assert.AreEqual(0.15m, Calcular(Maystar(), db), "los niveles de familia son asignaciones: el último pisa");
        }

        [TestMethod]
        public void ConFilaDeProductoMayor_GanaElProducto()
        {
            Descuentos(DeCategoria(0.15m), DeProducto(0.20m));
            Secundarias("COS/OUT");

            Assert.AreEqual(0.20m, Calcular(Maystar(), db));
        }

        [TestMethod]
        public void ConFilaDeProductoMenor_GanaLaCategoria()
        {
            Descuentos(DeCategoria(0.15m), DeProducto(0.10m));
            Secundarias("COS/OUT");

            Assert.AreEqual(0.15m, Calcular(Maystar(), db), "el nivel de producto lleva '>' en el motor: no pisa si es menor");
        }

        [TestMethod]
        public void DosCategoriasOutletConFila_GanaLaDeMayorCantidadMinimaYAIgualdadLaDeMayorDescuento()
        {
            // Fama Fabre está en COS/OUT y en COS/OUM con filas distintas.
            Descuentos(DeCategoria(0.25m, subGrupo: "OUT", familia: "Fama"), DeCategoria(0.30m, subGrupo: "OUM", familia: "Fama"));
            Secundarias("COS/OUT", "COS/OUM");
            Producto fama = Maystar();
            fama.Familia = "Fama";

            Assert.AreEqual(0.30m, Calcular(fama, db));
        }

        [TestMethod]
        public void LaFilaDeCategoria_RespetaLaCantidadMinima()
        {
            Descuentos(DeCategoria(0.15m, cantidadMinima: 3));
            Secundarias("COS/OUT");

            Assert.AreEqual(0m, Calcular(Maystar(), db, cantidad: 1));
            Assert.AreEqual(0.15m, Calcular(Maystar(), db, cantidad: 3));
        }

        [TestMethod]
        public void ElegirDescuentoDeCategoria_ComparaSinRellenoNiMayusculas()
        {
            DescuentosProducto fila = DeCategoria(0.15m, grupo: "COS", subGrupo: "OUT");
            List<CategoriaProducto> categorias = new List<CategoriaProducto> { new CategoriaProducto("cos ", "out") };

            Assert.AreSame(fila, GestorPrecios.ElegirDescuentoDeCategoria(new[] { fila }, categorias));
            Assert.IsNull(GestorPrecios.ElegirDescuentoDeCategoria(new[] { fila }, new List<CategoriaProducto>()));
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
