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
    }
}
