//// Comento toda la clase, porque ahora mismo no trabajamos con Glovo

//using FakeItEasy;
//using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NestoAPI.Infraestructure;
//using NestoAPI.Models.PedidosVenta;
//using System;

//namespace NestoAPI.Tests.Infrastructure
//{
//    [TestClass]
//    public class GestorAgenciasGlovoTests
//    {
//        private IServicioAgencias servicio;
//        private IGestorAgencias gestor;
//        private PedidoVentaDTO pedido;
//        IGestorStocks gestorStocks;
        
//        public GestorAgenciasGlovoTests()
//        {
//            servicio = A.Fake<IServicioAgencias>();
//            gestor = new GestorAgenciasGlovo();
//            pedido = new PedidoVentaDTO
//            {
//                ccc = "1",
//                plazosPago = "Recibo"
//            };
//            gestorStocks = A.Fake<IGestorStocks>();
            
//            A.CallTo(() => servicio.LeerCodigoPostal(pedido)).Returns("28004");
//            A.CallTo(() => servicio.HoraActual()).Returns(new DateTime(2019, 2, 5, 10, 0, 0));
//            A.CallTo(() => gestorStocks.HayStockDisponibleDeTodo(pedido)).Returns(true);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiElCodigoPostalEstaAutorizadoSiSePuedeServir()
//        {
//            //Dejamos el pedido original que está todo autorizado y lo comprobamos

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNotNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiElCodigoPostalNoEstaAutorizadoNoSePuedeServir()
//        {
//            A.CallTo(() => servicio.LeerCodigoPostal(pedido)).Returns("28110");

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiElPedidoNoTieneCCCNoSePuedeServir()
//        {
//            pedido.ccc = null;

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiEsPrepagoSePuedeServirAunqueNoTengaCCC()
//        {
//            pedido.ccc = null;
//            pedido.plazosPago = "PRE";

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNotNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiNoHayStockDeTodoNoSePuedeServir()
//        {
//            IGestorStocks gestorStocks = A.Fake<IGestorStocks>();
//            A.CallTo(() => gestorStocks.HayStockDisponibleDeTodo(pedido)).Returns(false);

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNull(resultado);
//        }

//        //[TestMethod]
//        //public void GestorAgenciasGlovo_SePuedeServirPedido_SiElAlmacenNoEstaAutorizadoNoSePuedeServir()
//        //{
//        //    LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
//        //    {
//        //        producto = "producto",
//        //        almacen = "almacen"
//        //    };
//        //    pedido.LineasPedido.Add(linea);

//        //    RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//        //    Assert.IsNull(resultado);
//        //}

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiElAlmacenEstaAutorizadoSiSePuedeServir()
//        {
//            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
//            {
//                Producto = "producto",
//                almacen = "REI"
//            };
//            pedido.Lineas.Add(linea);

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNotNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiNoEsElHorarioPermitidoNoSePuedeServir()
//        {
//            A.CallTo(() => servicio.HoraActual()).Returns(new DateTime(2019, 2, 5, 22, 0, 0));

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiElHorarioEsPermitidoSiSePuedeServir()
//        {
//            A.CallTo(() => servicio.HoraActual()).Returns(new DateTime(2019, 2, 5, 10, 0, 0));

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNotNull(resultado);
//        }

//        [TestMethod]
//        public void GestorAgenciasGlovo_SePuedeServirPedido_SiNoEsElDiaLaborableNoSePuedeServir()
//        {
//            A.CallTo(() => servicio.HoraActual()).Returns(new DateTime(2019, 2, 3, 10, 0, 0));

//            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

//            Assert.IsNull(resultado);
//        }
//    }
//}

using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models.PedidosVenta;
using System;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#232: GestorAgenciasGlovo.SePuedeServirPedido crasheaba con "La secuencia no
    /// contiene elementos" cuando el pedido llegaba con Lineas vacía: el '?.' protege contra
    /// null pero no contra colección vacía, y First() (línea 31) se ejecutaba antes de la
    /// comprobación de líneas vacías que hay más abajo. Un pedido sin líneas no es servible.
    /// </summary>
    [TestClass]
    public class GestorAgenciasGlovoLineasVaciasTests
    {
        [TestMethod]
        public void SePuedeServirPedido_PedidoConLineasVacias_DevuelveNullSinLanzar()
        {
            var servicio = A.Fake<IServicioAgencias>();
            var gestorStocks = A.Fake<IGestorStocks>();
            // Lineas vacía por defecto (HashSet vacío), ccc + PRE para pasar las otras guardas.
            var pedido = new PedidoVentaDTO { ccc = "1", plazosPago = "PRE" };

            // Martes a las 10:00 y CP 28xxx: el flujo llega hasta la línea del First().
            A.CallTo(() => servicio.HoraActual()).Returns(new DateTime(2019, 2, 5, 10, 0, 0));
            A.CallTo(() => servicio.LeerCodigoPostal(pedido)).Returns("28004");

            var gestor = new GestorAgenciasGlovo();

            RespuestaAgencia resultado = gestor.SePuedeServirPedido(pedido, servicio, gestorStocks).Result;

            Assert.IsNull(resultado, "Un pedido sin líneas no se puede servir por agencia y no debe lanzar");
        }
    }
}
