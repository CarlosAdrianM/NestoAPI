using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#423 (Slice 1): la regla de vigencia en sí misma. NULL = sin límite por ese lado,
    /// y las dos fechas son INCLUSIVAS (una campaña que acaba el 31/08 vale todo el día 31).
    /// </summary>
    [TestClass]
    public class VigenciaDescuentosReglaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 8, 31);

        private static DescuentosProducto Fila(DateTime? desde, DateTime? hasta)
        {
            return new DescuentosProducto { FechaDesde = desde, FechaHasta = hasta };
        }

        // Lo más importante del slice: las 48.870 filas que ya existen no llevan fechas, y tienen
        // que seguir comportándose exactamente igual que antes. La vigencia es opt-in.
        [TestMethod]
        public void EsVigente_SinNingunaFecha_EsSiempreVigente()
        {
            Assert.IsTrue(Vigencia.EsVigente(Fila(null, null), HOY));
        }

        [TestMethod]
        public void EsVigente_FechaHastaAyer_YaNoEsVigente()
        {
            Assert.IsFalse(Vigencia.EsVigente(Fila(null, HOY.AddDays(-1)), HOY));
        }

        // El caso de las rebajas: FechaHasta = 31/08 tiene que valer TODO el día 31.
        [TestMethod]
        public void EsVigente_FechaHastaHoy_TodaviaEsVigente()
        {
            Assert.IsTrue(Vigencia.EsVigente(Fila(null, HOY), HOY));
        }

        [TestMethod]
        public void EsVigente_FechaDesdeManana_TodaviaNoEsVigente()
        {
            Assert.IsFalse(Vigencia.EsVigente(Fila(HOY.AddDays(1), null), HOY));
        }

        [TestMethod]
        public void EsVigente_FechaDesdeHoy_YaEsVigente()
        {
            Assert.IsTrue(Vigencia.EsVigente(Fila(HOY, null), HOY));
        }

        [TestMethod]
        public void EsVigente_RangoQueContieneHoy_EsVigente()
        {
            Assert.IsTrue(Vigencia.EsVigente(Fila(HOY.AddDays(-10), HOY.AddDays(10)), HOY));
        }

        [TestMethod]
        public void EsVigente_RangoYaPasado_NoEsVigente()
        {
            Assert.IsFalse(Vigencia.EsVigente(Fila(HOY.AddDays(-30), HOY.AddDays(-1)), HOY));
        }
    }

    /// <summary>
    /// NestoAPI#423 (Slice 1): que el MOTOR DE PRECIOS respete la vigencia. Es lo que hace que la
    /// decisión de Carlos del 31/08/2026 sea coherente: la vigencia es una propiedad de la fila,
    /// no del nivel, así que caduca igual un descuento de producto que uno de familia que un
    /// precio especial.
    ///
    /// Los tests usan fechas RELATIVAS a hoy porque las consultas de producción llaman a
    /// DateTime.Today; la sobrecarga con día explícito se prueba arriba.
    /// </summary>
    [TestClass]
    public class VigenciaDescuentosGestorPreciosTests
    {
        private NVEntities db;
        private DbSet<DescuentosProducto> fakeDescuentos;

        private static DateTime Ayer => DateTime.Today.AddDays(-1);
        private static DateTime Manana => DateTime.Today.AddDays(1);

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
                PVP = 100m,
                Familia = "Lisap",
                Grupo = "PEL",
                SubGrupo = "ACB",
                Aplicar_Dto = true
            };
        }

        private PrecioDescuentoProducto CrearDatos(Producto producto)
        {
            return new PrecioDescuentoProducto
            {
                producto = producto,
                cliente = "2414",
                contacto = "0",
                cantidad = 1,
                aplicarDescuento = true
            };
        }

        private static DescuentosProducto DescuentoDeTarifaDelProducto(decimal descuento, DateTime? desde, DateTime? hasta)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Nº_Producto = "44166",
                CantidadMínima = 0,
                Descuento = descuento,
                Producto = new Producto { Número = "44166" },
                FechaDesde = desde,
                FechaHasta = hasta
            };
        }

        [TestMethod]
        public void CalcularDescuentoProducto_DescuentoDeTarifaVigente_SeAplica()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                DescuentoDeTarifaDelProducto(0.20m, Ayer, Manana)
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.20m, datos.descuentoCalculado);
        }

        // El caso de las rebajas de verano: la campaña acabó ayer y hoy el pedido ya no la lleva.
        // Sin el filtro de vigencia este test es ROJO: devuelve 0,20.
        [TestMethod]
        public void CalcularDescuentoProducto_DescuentoDeTarifaCaducado_NoSeAplica()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                DescuentoDeTarifaDelProducto(0.20m, Ayer.AddDays(-30), Ayer)
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0m, datos.descuentoCalculado);
        }

        // Una campaña programada para el mes que viene no puede cobrarse hoy.
        [TestMethod]
        public void CalcularDescuentoProducto_DescuentoDeTarifaQueEmpiezaManana_TodaviaNoSeAplica()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                DescuentoDeTarifaDelProducto(0.20m, Manana, Manana.AddDays(30))
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0m, datos.descuentoCalculado);
        }

        // Nivel distinto (familia, que va por el embudo BuscarDescuentoUnico): misma regla.
        [TestMethod]
        public void CalcularDescuentoProducto_DescuentoDeFamiliaCaducado_NoSeAplica()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30m,
                    Producto = new Producto { Número = "OTRO" },
                    FechaHasta = Ayer
                }
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0m, datos.descuentoCalculado);
        }

        // Y los PRECIOS especiales también caducan, no solo los porcentajes.
        [TestMethod]
        public void CalcularDescuentoProducto_PrecioEspecialCaducado_SeCobraElPvp()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Nº_Cliente = "2414", Nº_Producto = "44166", CantidadMínima = 0,
                    Precio = 80m, Producto = new Producto { Número = "44166" },
                    FechaHasta = Ayer
                }
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(100m, datos.precioCalculado);
        }

        /// <summary>
        /// Efecto colateral bueno del embudo: dos campañas CONSECUTIVAS sobre la misma familia
        /// dejan de ser un duplicado de los de #229, porque la caducada ni siquiera entra en la
        /// consulta. Sin esto, encadenar campañas obligaría a borrar la anterior a mano — justo
        /// lo que el slice viene a evitar.
        /// </summary>
        [TestMethod]
        public void CalcularDescuentoProducto_DosCampanasDeFamiliaConsecutivas_NoEsDuplicadoYGanaLaVigente()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30m,
                    Producto = new Producto { Número = "OTRO" },
                    FechaDesde = Ayer.AddDays(-60), FechaHasta = Ayer
                },
                new DescuentosProducto
                {
                    Empresa = "1", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.15m,
                    Producto = new Producto { Número = "OTRO" },
                    FechaDesde = DateTime.Today, FechaHasta = Manana.AddDays(30)
                }
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            GestorPrecios.calcularDescuentoProducto(datos, db);

            Assert.AreEqual(0.15m, datos.descuentoCalculado);
        }

        // Si las dos están vigentes a la vez sigue siendo un error de datos, como en #229: la
        // vigencia no es una excusa para dejar el descuento ambiguo.
        [TestMethod]
        public void CalcularDescuentoProducto_DosCampanasDeFamiliaSolapadas_SigueSiendoDuplicado()
        {
            Producto producto = CrearProducto();
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Empresa = "1", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.30m,
                    Producto = new Producto { Número = "OTRO" },
                    FechaDesde = Ayer, FechaHasta = Manana
                },
                new DescuentosProducto
                {
                    Empresa = "1", Familia = "Lisap", CantidadMínima = 0, Descuento = 0.15m,
                    Producto = new Producto { Número = "OTRO" },
                    FechaDesde = Ayer, FechaHasta = Manana
                }
            }.AsQueryable());
            PrecioDescuentoProducto datos = CrearDatos(producto);

            _ = Assert.ThrowsException<DescuentosDuplicadosException>(() => GestorPrecios.calcularDescuentoProducto(datos, db));
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

    /// <summary>
    /// NestoAPI#423 (Slice 1): que lo que VIAJA A LA TIENDA respete la misma vigencia que el motor
    /// de precios. Si no, la tienda anunciaría un porcentaje que Nesto ya no cobra.
    /// </summary>
    [TestClass]
    public class VigenciaDescuentosMensajeTiendaTests
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

        private static DescuentosProducto OfertaDeCampana(DateTime? desde, DateTime? hasta)
        {
            return new DescuentosProducto
            {
                Empresa = "1",
                Nº_Producto = "44166",
                CantidadMínima = 1,
                Descuento = 0.20m,
                AudienciaOferta = 2,   // profesional y público
                FechaDesde = desde,
                FechaHasta = hasta
            };
        }

        [TestMethod]
        public async Task CargarDescuentosPorAudiencia_CampanaVigente_Viaja()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                OfertaDeCampana(DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1))
            }.AsQueryable());
            var dto = new ProductoDTO { Producto = "44166" };

            await ProductoDTO.CargarDescuentosPorAudiencia(dto, db, 100m);

            Assert.AreEqual(20m, dto.DescuentoPorcentajeProfesional);
            Assert.AreEqual(20m, dto.DescuentoPorcentajePublico);
        }

        // Sin este filtro la tienda se queda anunciando el 20 % de una campaña acabada.
        [TestMethod]
        public async Task CargarDescuentosPorAudiencia_CampanaCaducada_NoViaja()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                OfertaDeCampana(DateTime.Today.AddDays(-30), DateTime.Today.AddDays(-1))
            }.AsQueryable());
            var dto = new ProductoDTO { Producto = "44166" };

            await ProductoDTO.CargarDescuentosPorAudiencia(dto, db, 100m);

            Assert.IsNull(dto.DescuentoPorcentajeProfesional);
            Assert.IsNull(dto.DescuentoPorcentajePublico);
        }

        [TestMethod]
        public async Task CargarDescuentosPorAudiencia_SinFechas_ViajaComoSiempre()
        {
            ConfigurarFakeDbSet(fakeDescuentos, new List<DescuentosProducto>
            {
                OfertaDeCampana(null, null)
            }.AsQueryable());
            var dto = new ProductoDTO { Producto = "44166" };

            await ProductoDTO.CargarDescuentosPorAudiencia(dto, db, 100m);

            Assert.AreEqual(20m, dto.DescuentoPorcentajeProfesional);
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
