using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#305: el PUT de pedidos llamaba a ActualizarVendedorPedidoGrupoProducto con
    /// pedido.VendedoresGrupoProducto a null (los clientes lo mandan null habitualmente) y
    /// reventaba con NullReferenceException (caso real de Reina, 15/07/26 14:58).
    /// </summary>
    [TestClass]
    public class GestorComisionesActualizarVendedorPedidoTests
    {
        private NVEntities _db;
        private VendedorPedidoGrupoProducto _registroActual;

        private void ConfigurarDb(params VendedorPedidoGrupoProducto[] actuales)
        {
            _db = A.Fake<NVEntities>();
            DbSet<VendedorPedidoGrupoProducto> fakeSet = A.Fake<DbSet<VendedorPedidoGrupoProducto>>(
                opt => opt.Implements<IQueryable<VendedorPedidoGrupoProducto>>());
            IQueryable<VendedorPedidoGrupoProducto> data = actuales.AsQueryable();
            A.CallTo(() => ((IQueryable<VendedorPedidoGrupoProducto>)fakeSet).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<VendedorPedidoGrupoProducto>)fakeSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<VendedorPedidoGrupoProducto>)fakeSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<VendedorPedidoGrupoProducto>)fakeSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
            A.CallTo(() => _db.VendedoresPedidosGruposProductos).Returns(fakeSet);
        }

        private static CabPedidoVta Cabecera() => new CabPedidoVta { Empresa = "1", Número = 922324 };

        [TestMethod]
        public void ActualizarVendedorPedido_ColeccionNullConRegistroActual_NoLanzaNiModifica()
        {
            _registroActual = new VendedorPedidoGrupoProducto { Empresa = "1", Pedido = 922324, GrupoProducto = "PEL", Vendedor = "IF" };
            ConfigurarDb(_registroActual);
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.VendedoresGrupoProducto = null; // los clientes lo mandan null

            GestorComisiones.ActualizarVendedorPedidoGrupoProducto(_db, Cabecera(), pedido);

            Assert.AreEqual("IF", _registroActual.Vendedor, "Sin colección no se debe pisar el vendedor actual");
        }

        [TestMethod]
        public void ActualizarVendedorPedido_ColeccionVaciaConRegistroActual_NoLanzaNiModifica()
        {
            _registroActual = new VendedorPedidoGrupoProducto { Empresa = "1", Pedido = 922324, GrupoProducto = "PEL", Vendedor = "IF" };
            ConfigurarDb(_registroActual);
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO>();

            GestorComisiones.ActualizarVendedorPedidoGrupoProducto(_db, Cabecera(), pedido);

            Assert.AreEqual("IF", _registroActual.Vendedor);
        }

        [TestMethod]
        public void ActualizarVendedorPedido_ConVendedorNuevo_ActualizaElRegistro()
        {
            _registroActual = new VendedorPedidoGrupoProducto { Empresa = "1", Pedido = 922324, GrupoProducto = "PEL", Vendedor = "IF" };
            ConfigurarDb(_registroActual);
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.VendedoresGrupoProducto = new List<VendedorGrupoProductoDTO>
            {
                new VendedorGrupoProductoDTO { grupoProducto = "PEL", vendedor = "JE" }
            };

            GestorComisiones.ActualizarVendedorPedidoGrupoProducto(_db, Cabecera(), pedido);

            Assert.AreEqual("JE", _registroActual.Vendedor);
        }
    }
}
