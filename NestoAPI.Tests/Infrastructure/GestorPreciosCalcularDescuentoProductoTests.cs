using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Regresión Issue #229: en DescuentosProducto solo puede haber una fila aplicable por
    /// filtro (cliente/familia/grupo/producto). Cuando hay duplicados (p.ej. dos familias que
    /// solo difieren en mayúsculas, iguales para SQL Server), el error debe decir claramente
    /// qué está duplicado para que el usuario lo corrija, no un 500 genérico de
    /// "La secuencia contiene más de un elemento".
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

        // Caso real del error 500 en producción: el cliente 2414 tenía dos filas de descuento
        // por familia que SQL Server considera iguales ('lisap'/'Lisap').
        [TestMethod]
        public void CalcularDescuentoProducto_DescuentosDeFamiliaDuplicados_LanzaErrorQueIndicaElDuplicado()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.66m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.66m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 1);

            var excepcion = Assert.ThrowsException<DescuentosDuplicadosException>(() => GestorPrecios.calcularDescuentoProducto(datos, db));

            StringAssert.Contains(excepcion.Message, "Lisap");
            StringAssert.Contains(excepcion.Message, "2414");
            StringAssert.Contains(excepcion.Message, "duplicados");
        }

        // Dos filas con distinta CantidadMínima también son un error de datos: aunque solo
        // una "ganaría", el descuento del cliente debe estar definido sin ambigüedad.
        [TestMethod]
        public void CalcularDescuentoProducto_DosFilasDeFamiliaAplicablesPorCantidad_LanzaErrorQueIndicaElDuplicado()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.10m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 4, Descuento = 0.15m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 4);

            var excepcion = Assert.ThrowsException<DescuentosDuplicadosException>(() => GestorPrecios.calcularDescuentoProducto(datos, db));

            StringAssert.Contains(excepcion.Message, "Lisap");
            StringAssert.Contains(excepcion.Message, "2414");
        }

        // Si por cantidad solo aplica una de las filas, no hay ambigüedad y se calcula normal.
        [TestMethod]
        public void CalcularDescuentoProducto_SoloUnaFilaAplicaPorCantidad_CalculaElDescuento()
        {
            var producto = CrearProducto();
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.10m, Producto = new Producto { Número = "OTRO" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Familia = "Lisap", CantidadMínima = 4, Descuento = 0.15m, Producto = new Producto { Número = "OTRO" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 2);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.10m, datos.descuentoCalculado);
        }

        [TestMethod]
        public void CalcularDescuentoProducto_PreciosEspecialesDuplicados_LanzaErrorQueIndicaElProducto()
        {
            var producto = CrearProducto();
            producto.PVP = 100;
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 0, Precio = 90, Producto = new Producto { Número = "44166" } },
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 0, Precio = 80, Producto = new Producto { Número = "44166" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 1);

            var excepcion = Assert.ThrowsException<DescuentosDuplicadosException>(() => GestorPrecios.calcularDescuentoProducto(datos, db));

            StringAssert.Contains(excepcion.Message, "44166");
            StringAssert.Contains(excepcion.Message, "2414");
        }

        [TestMethod]
        public void CalcularDescuentoProducto_SinDuplicados_CalculaPrecioYDescuento()
        {
            var producto = CrearProducto();
            producto.PVP = 100;
            var descuentos = new List<DescuentosProducto>
            {
                new DescuentosProducto { Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 0, Precio = 80, Producto = new Producto { Número = "44166" } }
            };
            ConfigurarFakeDbSet(fakeDescuentos, descuentos.AsQueryable());
            var datos = CrearDatos(producto, 1);

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
