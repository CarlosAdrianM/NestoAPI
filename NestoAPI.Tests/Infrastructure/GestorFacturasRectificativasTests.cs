using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using CompraOriginal = NestoAPI.Infraestructure.Rectificativas.GestorFacturasRectificativas.CompraOriginal;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Verifactu #37: búsqueda LIFO de las facturas originales de una rectificativa. El núcleo
    /// del reparto es puro (RepartirEntreCompras); BuscarFacturasOriginales añade el acceso a
    /// datos (líneas facturadas + lo ya rectificado en LinFacturaVtaRectificacion).
    /// </summary>
    [TestClass]
    public class GestorFacturasRectificativasTests
    {
        // ---- Núcleo puro ----

        [TestMethod]
        public void RepartirEntreCompras_LaUltimaCompraCubreTodo_UnaSolaVinculacion()
        {
            var compras = new List<CompraOriginal>
            {
                new CompraOriginal { Factura = "NV25/001234", Linea = 10, Cantidad = 12 },
                new CompraOriginal { Factura = "NV25/001100", Linea = 20, Cantidad = 8 }
            };

            List<VinculacionRectificativa> vinculaciones =
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 10, compras);

            Assert.AreEqual(1, vinculaciones.Count);
            Assert.AreEqual("NV25/001234", vinculaciones.Single().FacturaOriginalNumero);
            Assert.AreEqual(10, vinculaciones.Single().FacturaOriginalLinea);
            Assert.AreEqual(10m, vinculaciones.Single().CantidadRectificada);
        }

        [TestMethod]
        public void RepartirEntreCompras_ejemploDeLaIssue_RepartoLifoEntreDosFacturas()
        {
            // Rectificativa de 10 uds: última compra 5 uds, penúltima 8 → 5 y 5
            var compras = new List<CompraOriginal>
            {
                new CompraOriginal { Factura = "NV25/001234", Linea = 10, Cantidad = 5 },
                new CompraOriginal { Factura = "NV25/001100", Linea = 20, Cantidad = 8 }
            };

            List<VinculacionRectificativa> vinculaciones =
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 10, compras);

            Assert.AreEqual(2, vinculaciones.Count);
            Assert.AreEqual(5m, vinculaciones[0].CantidadRectificada);
            Assert.AreEqual("NV25/001234", vinculaciones[0].FacturaOriginalNumero);
            Assert.AreEqual(5m, vinculaciones[1].CantidadRectificada);
            Assert.AreEqual("NV25/001100", vinculaciones[1].FacturaOriginalNumero);
        }

        [TestMethod]
        public void RepartirEntreCompras_LoYaRectificadoNoSePuedeVolverARectificar()
        {
            // La última compra (5 uds) ya tiene 3 rectificadas: solo quedan 2 disponibles
            var compras = new List<CompraOriginal>
            {
                new CompraOriginal { Factura = "NV25/001234", Linea = 10, Cantidad = 5, YaRectificada = 3 },
                new CompraOriginal { Factura = "NV25/001100", Linea = 20, Cantidad = 8 }
            };

            List<VinculacionRectificativa> vinculaciones =
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 6, compras);

            Assert.AreEqual(2, vinculaciones.Count);
            Assert.AreEqual(2m, vinculaciones[0].CantidadRectificada, "Solo lo disponible de la última");
            Assert.AreEqual(4m, vinculaciones[1].CantidadRectificada);
        }

        [TestMethod]
        public void RepartirEntreCompras_CompraAgotada_SeSaltaSinVincularCero()
        {
            var compras = new List<CompraOriginal>
            {
                new CompraOriginal { Factura = "NV25/001234", Linea = 10, Cantidad = 5, YaRectificada = 5 },
                new CompraOriginal { Factura = "NV25/001100", Linea = 20, Cantidad = 8 }
            };

            List<VinculacionRectificativa> vinculaciones =
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 4, compras);

            Assert.AreEqual(1, vinculaciones.Count, "La agotada no genera vinculación de 0 unidades");
            Assert.AreEqual("NV25/001100", vinculaciones.Single().FacturaOriginalNumero);
        }

        [TestMethod]
        public void RepartirEntreCompras_ComprasInsuficientes_LanzaConLoQueFalta()
        {
            var compras = new List<CompraOriginal>
            {
                new CompraOriginal { Factura = "NV25/001234", Linea = 10, Cantidad = 5 }
            };

            var ex = Assert.ThrowsException<InvalidOperationException>(() =>
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 7, compras));

            StringAssert.Contains(ex.Message, "Faltan 2");
            StringAssert.Contains(ex.Message, "ABC");
        }

        [TestMethod]
        public void RepartirEntreCompras_CantidadNoPositiva_Lanza()
        {
            _ = Assert.ThrowsException<ArgumentException>(() =>
                GestorFacturasRectificativas.RepartirEntreCompras("ABC", 0, new List<CompraOriginal>()));
        }

        // ---- Acceso a datos (fakes async) ----

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

        private static NVEntities DbCon(List<LinPedidoVta> lineas, List<LinFacturaVtaRectificacion> rectificaciones = null)
        {
            NVEntities db = A.Fake<NVEntities>();
            var fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            var fakeRect = A.Fake<DbSet<LinFacturaVtaRectificacion>>(o => o.Implements<IQueryable<LinFacturaVtaRectificacion>>().Implements<IDbAsyncEnumerable<LinFacturaVtaRectificacion>>());
            ConfigurarFakeDbSet(fakeLineas, lineas.AsQueryable());
            ConfigurarFakeDbSet(fakeRect, (rectificaciones ?? new List<LinFacturaVtaRectificacion>()).AsQueryable());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.LinFacturaVtaRectificaciones).Returns(fakeRect);
            return db;
        }

        private static LinPedidoVta LineaFacturada(string factura, int orden, short cantidad, DateTime fechaFactura,
            string producto = "12345", short estado = 4)
        {
            return new LinPedidoVta
            {
                Empresa = "1",
                Nº_Cliente = "15191",
                Producto = producto,
                Cantidad = cantidad,
                Estado = estado,
                Nº_Factura = factura,
                Nº_Orden = orden,
                Fecha_Factura = fechaFactura
            };
        }

        [TestMethod]
        public async Task BuscarFacturasOriginales_OrdenaPorFechaFacturaDescendente_Lifo()
        {
            NVEntities db = DbCon(new List<LinPedidoVta>
            {
                LineaFacturada("NV25/001100", 20, 8, new DateTime(2026, 6, 1)),   // penúltima
                LineaFacturada("NV25/001234", 10, 5, new DateTime(2026, 7, 15))   // última
            });
            var gestor = new GestorFacturasRectificativas(db);

            List<VinculacionRectificativa> vinculaciones =
                await gestor.BuscarFacturasOriginales("1", "15191", "12345", 10);

            Assert.AreEqual(2, vinculaciones.Count);
            Assert.AreEqual("NV25/001234", vinculaciones[0].FacturaOriginalNumero, "La última compra primero (LIFO)");
            Assert.AreEqual(5m, vinculaciones[0].CantidadRectificada);
            Assert.AreEqual("NV25/001100", vinculaciones[1].FacturaOriginalNumero);
            Assert.AreEqual(5m, vinculaciones[1].CantidadRectificada);
        }

        [TestMethod]
        public async Task BuscarFacturasOriginales_ExcluyeNoFacturadasNegativasYOtrosProductos()
        {
            NVEntities db = DbCon(new List<LinPedidoVta>
            {
                LineaFacturada("NV25/001234", 10, 5, new DateTime(2026, 7, 15)),
                LineaFacturada("NV25/001235", 11, 5, new DateTime(2026, 7, 16), estado: 2),        // albarán, no factura
                LineaFacturada("NV25/001236", 12, -5, new DateTime(2026, 7, 17)),                  // otra rectificativa
                LineaFacturada("NV25/001237", 13, 5, new DateTime(2026, 7, 18), producto: "99999") // otro producto
            });
            var gestor = new GestorFacturasRectificativas(db);

            List<VinculacionRectificativa> vinculaciones =
                await gestor.BuscarFacturasOriginales("1", "15191", "12345", 5);

            Assert.AreEqual(1, vinculaciones.Count);
            Assert.AreEqual("NV25/001234", vinculaciones.Single().FacturaOriginalNumero);
        }

        [TestMethod]
        public async Task BuscarFacturasOriginales_RestaLoYaRectificadoEnLinFacturaVtaRectificacion()
        {
            NVEntities db = DbCon(
                new List<LinPedidoVta>
                {
                    LineaFacturada("NV25/001234", 10, 5, new DateTime(2026, 7, 15)),
                    LineaFacturada("NV25/001100", 20, 8, new DateTime(2026, 6, 1))
                },
                new List<LinFacturaVtaRectificacion>
                {
                    // Una rectificativa anterior ya se llevó 3 uds de la última compra
                    new LinFacturaVtaRectificacion { Empresa = "1", NumeroFactura = "RV26/000001",
                        FacturaOriginalNumero = "NV25/001234", FacturaOriginalLinea = 10, CantidadRectificada = 3 }
                });
            var gestor = new GestorFacturasRectificativas(db);

            List<VinculacionRectificativa> vinculaciones =
                await gestor.BuscarFacturasOriginales("1", "15191", "12345", 6);

            Assert.AreEqual(2, vinculaciones.Count);
            Assert.AreEqual(2m, vinculaciones[0].CantidadRectificada, "De la última solo quedan 5-3=2");
            Assert.AreEqual(4m, vinculaciones[1].CantidadRectificada);
        }

        [TestMethod]
        public async Task BuscarFacturasOriginales_SinComprasSuficientes_Lanza()
        {
            NVEntities db = DbCon(new List<LinPedidoVta>
            {
                LineaFacturada("NV25/001234", 10, 5, new DateTime(2026, 7, 15))
            });
            var gestor = new GestorFacturasRectificativas(db);

            var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                gestor.BuscarFacturasOriginales("1", "15191", "12345", 8));

            StringAssert.Contains(ex.Message, "Faltan 3");
        }
    }
}
