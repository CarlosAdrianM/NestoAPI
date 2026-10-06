using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>Nesto#510: el PUT del pedido solo deja cambiar el almacén sin picking, albarán, nota de entrega ni etiqueta viva.</summary>
    [TestClass]
    public class CambioAlmacenPedidoTests
    {
        private static LinPedidoVta Linea(int orden, short estado = Constantes.EstadosLineaVenta.PENDIENTE, int picking = 0,
            byte tipo = Constantes.TiposLineaVenta.PRODUCTO, string almacen = "ALC")
        {
            return new LinPedidoVta { Nº_Orden = orden, Producto = "45146", Estado = estado, Picking = picking, TipoLinea = tipo, Almacén = almacen + " " };
        }

        private static string Motivo(params LinPedidoVta[] lineas)
        {
            return CambioAlmacenPedido.Motivo(lineas, false, new List<EnviosAgencia>(), null);
        }

        [TestMethod]
        public void Motivo_PendientesPresupuestoYEnCursoSinPicking_SePuede()
        {
            Assert.IsNull(Motivo(Linea(1), Linea(2, Constantes.EstadosLineaVenta.EN_CURSO)));
            Assert.IsNull(Motivo(Linea(1, Constantes.EstadosLineaVenta.PRESUPUESTO)));
        }

        [TestMethod]
        public void Motivo_LineaConPicking_NoSePuede()
        {
            StringAssert.Contains(Motivo(Linea(1), Linea(2, Constantes.EstadosLineaVenta.EN_CURSO, picking: 12345)), "picking");
        }

        [TestMethod]
        public void Motivo_LineaEnAlbaran_NoSePuede()
        {
            StringAssert.Contains(Motivo(Linea(1), Linea(2, Constantes.EstadosLineaVenta.ALBARAN)), "albarán");
        }

        [TestMethod]
        public void Motivo_LineaFacturada_NoSePuede()
        {
            StringAssert.Contains(Motivo(Linea(1, Constantes.EstadosLineaVenta.FACTURA)), "factura");
        }

        [TestMethod]
        public void Motivo_NotaDeEntrega_NoSePuede()
        {
            StringAssert.Contains(CambioAlmacenPedido.Motivo(new[] { Linea(1) }, true, null, null), "nota de entrega");
            StringAssert.Contains(Motivo(Linea(1, Constantes.EstadosLineaVenta.NOTA_ENTREGA)), "nota de entrega");
        }

        [TestMethod]
        public void Motivo_PickingEnCurso_NoSePuede()
        {
            StringAssert.Contains(CambioAlmacenPedido.Motivo(new[] { Linea(1) }, false, null, 98765), "picking 98765");
        }

        [TestMethod]
        public void Motivo_EtiquetaVivaSinEntregar_NoSePuede()
        {
            foreach (short estado in new[] { (short)Constantes.Agencias.ESTADO_EN_CURSO, Constantes.Agencias.ESTADO_TRAMITADO, Constantes.Agencias.ESTADO_INCIDENTADO })
            {
                var envios = new List<EnviosAgencia> { new EnviosAgencia { Estado = estado, CodigoBarras = "ABC123 " } };
                StringAssert.Contains(CambioAlmacenPedido.Motivo(new[] { Linea(1) }, false, envios, null), "etiqueta", $"Estado {estado}");
            }
        }

        [TestMethod]
        public void Motivo_EtiquetaPendienteSinCodigoOEntregada_SePuede()
        {
            var envios = new List<EnviosAgencia>
            {
                new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_PENDIENTE, CodigoBarras = null },
                new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_EN_CURSO, CodigoBarras = " " },
                new EnviosAgencia { Estado = Constantes.Agencias.ESTADO_ENTREGADO, CodigoBarras = "ABC123" }
            };
            Assert.IsNull(CambioAlmacenPedido.Motivo(new[] { Linea(1) }, false, envios, null));
        }

        [TestMethod]
        public void Motivo_LineaDePortesConAlbaran_NoCuentaPorqueNoEsDeProducto()
        {
            Assert.IsNull(Motivo(Linea(1), Linea(2, Constantes.EstadosLineaVenta.ALBARAN, tipo: Constantes.TiposLineaVenta.CUENTA_CONTABLE)));
        }

        [TestMethod]
        public void CambiaAlmacen_SoloSiUnaLineaGuardadaVieneConOtroAlmacen()
        {
            var guardadas = new[] { Linea(1), Linea(2) };

            Assert.IsFalse(CambioAlmacenPedido.CambiaAlmacen(guardadas, new[]
            {
                new LineaPedidoVentaDTO { id = 1, almacen = "ALC" }, new LineaPedidoVentaDTO { id = 0, almacen = "ALG" }
            }), "Mismo almacén (con relleno) y una línea nueva");
            Assert.IsTrue(CambioAlmacenPedido.CambiaAlmacen(guardadas, new[]
            {
                new LineaPedidoVentaDTO { id = 1, almacen = "ALG" }, new LineaPedidoVentaDTO { id = 2, almacen = "ALG" }
            }));
        }
    }
}
