using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Sugerencia 541 (Marta, NestoApp): el detalle de comisiones salía del más antiguo al más reciente y
    /// había que arrastrar hasta el final del mes para ver lo de hoy.
    /// </summary>
    [TestClass]
    public class ComisionAnualDetallesControllerTests
    {
        private static vstLinPedidoVtaComisionesDetalle Linea(int pedido, DateTime? fecha)
        {
            return new vstLinPedidoVtaComisionesDetalle
            {
                Vendedor = "JE", Anno = 2025, Mes = 3, Etiqueta = "General",
                Empresa = "1", Pedido = pedido, Fecha_Factura = fecha
            };
        }

        [TestMethod]
        public void GetComisionesAnualesDetalles_MesCerrado_LoMasRecienteArriba()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<vstLinPedidoVtaComisionesDetalle> fake = A.Fake<DbSet<vstLinPedidoVtaComisionesDetalle>>(
                o => o.Implements<IQueryable<vstLinPedidoVtaComisionesDetalle>>());
            IQueryable<vstLinPedidoVtaComisionesDetalle> datos = new List<vstLinPedidoVtaComisionesDetalle>
            {
                Linea(100, new DateTime(2025, 3, 3)),
                Linea(300, new DateTime(2025, 3, 28)),
                Linea(200, new DateTime(2025, 3, 15)),
                Linea(201, new DateTime(2025, 3, 15)),
                new vstLinPedidoVtaComisionesDetalle { Vendedor = "OTRO", Anno = 2025, Mes = 3, Etiqueta = "General", Pedido = 999, Fecha_Factura = new DateTime(2025, 3, 30) }
            }.AsQueryable();
            A.CallTo(() => ((IQueryable<vstLinPedidoVtaComisionesDetalle>)fake).Provider).Returns(datos.Provider);
            A.CallTo(() => ((IQueryable<vstLinPedidoVtaComisionesDetalle>)fake).Expression).Returns(datos.Expression);
            A.CallTo(() => ((IQueryable<vstLinPedidoVtaComisionesDetalle>)fake).ElementType).Returns(datos.ElementType);
            A.CallTo(() => ((IQueryable<vstLinPedidoVtaComisionesDetalle>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => db.vstLinPedidoVtaComisionesDetalles).Returns(fake);
            ComisionAnualDetallesController controller = new ComisionAnualDetallesController(db);

            List<int> pedidos = controller.GetComisionesAnualesDetalles("JE", 2025, 3, false, "General")
                .Select(l => l.Pedido).ToList();

            CollectionAssert.AreEqual(new List<int> { 300, 201, 200, 100 }, pedidos);
        }

        [TestMethod]
        public void MasRecientesPrimero_SinFechaDeFactura_VanLasPrimeras()
        {
            IQueryable<vstLinPedidoVtaComisionesDetalle> datos = new List<vstLinPedidoVtaComisionesDetalle>
            {
                Linea(100, new DateTime(2025, 3, 3)),
                Linea(500, null),
                Linea(300, new DateTime(2025, 3, 28))
            }.AsQueryable();

            List<int> pedidos = ComisionAnualDetallesController.MasRecientesPrimero(datos).Select(l => l.Pedido).ToList();

            CollectionAssert.AreEqual(new List<int> { 500, 300, 100 }, pedidos);
        }

        // Sugerencia 566 (Marta): lo servido sin facturar ya no sale con la fecha vacía

        [TestMethod]
        public void Detalle_ServidoSinFacturar_DiceServidoElDiaPendienteDeFacturar()
        {
            var linea = new vstLinPedidoVtaComisione
            {
                Empresa = "1", Número = 927595, Nombre = "A.M.A BY 11", Dirección = "C/ X", Base_Imponible = 10m,
                Estado = 2, Fecha_Albarán = new DateTime(2026, 10, 2, 11, 29, 34), Fecha_Factura = null
            };

            vstLinPedidoVtaComisionesDetalle detalle = ComisionAnualDetallesController.Detalle(linea, "MRM", 2026, 10);

            Assert.AreEqual(927595, detalle.Pedido);
            Assert.AreEqual(new DateTime(2026, 10, 2, 11, 29, 34), detalle.FechaAlbaran);
            Assert.IsNull(detalle.Fecha_Factura);
            Assert.AreEqual("Servido el 02/10, pendiente de facturar", detalle.EstadoFacturacion);
        }

        [TestMethod]
        public void Detalle_Facturado_SinTextoYConSuFecha()
        {
            var linea = new vstLinPedidoVtaComisione
            {
                Número = 1, Estado = 4, Fecha_Albarán = new DateTime(2026, 10, 2), Fecha_Factura = new DateTime(2026, 10, 3)
            };

            vstLinPedidoVtaComisionesDetalle detalle = ComisionAnualDetallesController.Detalle(linea, "MRM", 2026, 10);

            Assert.AreEqual(new DateTime(2026, 10, 3), detalle.Fecha_Factura);
            Assert.IsNull(detalle.EstadoFacturacion, "Facturada: NestoApp pinta la fecha de factura, como siempre");
        }

        [TestMethod]
        public void Detalle_EnPickingSinAlbaran_PendienteDeServir()
        {
            var linea = new vstLinPedidoVtaComisione { Número = 1, Estado = 1, Picking = 99748 };

            Assert.AreEqual("Pendiente de servir", ComisionAnualDetallesController.Detalle(linea, "MRM", 2026, 10).EstadoFacturacion);
        }

        [TestMethod]
        public void EstadoFacturacion_MesCerradoSinFactura_PendienteDeFacturar()
        {
            // La vista de meses cerrados no trae la fecha del albarán
            Assert.AreEqual("Pendiente de facturar", Linea(500, null).EstadoFacturacion);
            Assert.IsNull(Linea(100, new DateTime(2025, 3, 3)).EstadoFacturacion);
        }

        [TestMethod]
        public void EstadoFacturacion_SeSerializaEnElJsonComoCampoNuevo()
        {
            vstLinPedidoVtaComisionesDetalle detalle = ComisionAnualDetallesController.Detalle(new vstLinPedidoVtaComisione
            {
                Número = 927595, Estado = 2, Fecha_Albarán = new DateTime(2026, 10, 2)
            }, "MRM", 2026, 10);

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(detalle);

            StringAssert.Contains(json, "\"EstadoFacturacion\":\"Servido el 02/10, pendiente de facturar\"");
            StringAssert.Contains(json, "\"FechaAlbaran\":\"2026-10-02T00:00:00\"");
            StringAssert.Contains(json, "\"Fecha_Factura\":null");
            Assert.IsFalse(json.Contains("EstaServida"), "Lo interno no sale");
        }
    }
}
