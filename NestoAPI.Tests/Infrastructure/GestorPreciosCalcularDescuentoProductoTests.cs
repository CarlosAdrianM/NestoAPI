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
    /// Regresión Issue #229: GestorPrecios.calcularDescuentoProducto lanzaba
    /// "La secuencia contiene más de un elemento" cuando DescuentosProducto tenía
    /// varias filas válidas para el mismo filtro (tramos por CantidadMínima o
    /// duplicados que SQL Server considera iguales por collation). Debe elegir
    /// el tramo más exigente (mayor CantidadMínima &lt;= cantidad) sin lanzar.
    /// </summary>
    [TestClass]
    public class GestorPreciosCalcularDescuentoProductoTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeDescuentos = A.Fake<DbSet<DescuentosProducto>>(o => o.Implements<IQueryable<DescuentosProducto>>().Implements<IDbAsyncEnumerable<DescuentosProducto>>());
            A.CallTo(() => db.DescuentosProductoes).Returns(fakeDescuentos);
        }

        private Producto CrearProducto()
        {
            return new Producto
            {
                Empresa = "1",
                Número = "44166",
                Nombre = "GEL FRIO EFECTO C + D",
                PVP = 14.45m,
                Familia = "Lisap",
                Grupo = "PEL",
                SubGrupo = "ACB",
                Aplicar_Dto = true
            };
        }

        private PrecioDescuentoProducto CrearDatos(Producto producto, short cantidad)
        {
            return new PrecioDescuentoProducto
            {
                producto = producto,
                cliente = "2414",
                contacto = "0",
                cantidad = cantidad,
                aplicarDescuento = true
            };
        }

        // Caso real del error 500 en producción: el cliente 2414 tiene dos filas de descuento
        // por familia que SQL Server considera iguales ('lisap'/'Lisap'). En memoria las
        // representamos con la misma familia y FiltroProducto vacío (StartsWith(null) no es
        // evaluable en LINQ to Objects).
        [TestMethod]
        public void CalcularDescuentoProducto_FilasDuplicadasDeFamilia_NoLanzaYAplicaElDescuento()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 0, Descuento = 0.66m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 0, Descuento = 0.66m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 1);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.66m, datos.descuentoCalculado);
        }

        [TestMethod]
        public void CalcularDescuentoProducto_TramosDeDescuentoPorFamilia_CogeElTramoMasExigente()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 0, Descuento = 0.10m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 4, Descuento = 0.15m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 4);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.15m, datos.descuentoCalculado);
        }

        [TestMethod]
        public void CalcularDescuentoProducto_TramosDeDescuentoPorFamilia_CantidadInferiorAlSegundoTramo_CogeElPrimero()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 0, Descuento = 0.10m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", FiltroProducto = "", CantidadMínima = 4, Descuento = 0.15m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 2);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.10m, datos.descuentoCalculado);
        }

        [TestMethod]
        public void CalcularDescuentoProducto_TramosDePrecioEspecialPorCliente_CogeElTramoMasExigente()
        {
            var producto = CrearProducto();
            producto.PVP = 100;
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 0, Precio = 90, Producto = new Producto { Número = "44166" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 4, Precio = 80, Producto = new Producto { Número = "44166" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 4);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(80, datos.precioCalculado);
            Assert.AreEqual(0, datos.descuentoCalculado);
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
