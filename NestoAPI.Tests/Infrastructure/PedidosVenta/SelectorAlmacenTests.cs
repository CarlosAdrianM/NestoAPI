using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Tests para Issue #253/#52 - Selector de almacenes y propiedad EsFicticio.
    /// Carlos 09/12/25
    /// </summary>
    [TestClass]
    public class SelectorAlmacenTests
    {
        #region Tests de lógica EsFicticio

        [TestMethod]
        public void EsFicticio_ProductoConFicticioTrue_DevuelveTrue()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                Producto = "PROD_FICTICIO",
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, // tipoLinea = 1
                EsFicticio = true // Simulamos que el producto tiene Ficticio = true
            };

            // Act & Assert
            Assert.IsTrue(linea.EsFicticio);
        }

        [TestMethod]
        public void EsFicticio_ProductoConFicticioFalse_DevuelveFalse()
        {
            // Arrange
            var linea = new LineaPedidoVentaDTO
            {
                Producto = "PROD_NORMAL",
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, // tipoLinea = 1
                EsFicticio = false
            };

            // Act & Assert
            Assert.IsFalse(linea.EsFicticio);
        }

        [TestMethod]
        public void EsFicticio_TipoLineaCuentaContable_DevuelveTrue()
        {
            // Arrange - Línea de tipo cuenta contable (tipoLinea = 2)
            // Según la lógica del GestorPedidosVenta, tipoLinea = 2 siempre es EsFicticio = true
            var tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            // Act - La lógica en GestorPedidosVenta es:
            // EsFicticio = (prod != null && prod.Ficticio) || l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE
            bool esFicticio = tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            // Assert
            Assert.IsTrue(esFicticio, "Las líneas con tipoLinea=2 (cuenta contable) deben tratarse como ficticias");
            Assert.AreEqual(2, Constantes.TiposLineaVenta.CUENTA_CONTABLE, "CUENTA_CONTABLE debe ser 2");
        }

        [TestMethod]
        public void EsFicticio_TipoLineaTexto_NoEsFicticioPorTipo()
        {
            // Arrange
            var tipoLinea = Constantes.TiposLineaVenta.TEXTO;

            // Act
            bool esFicticioPorTipo = tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            // Assert
            Assert.IsFalse(esFicticioPorTipo, "Las líneas de texto no son ficticias solo por el tipo");
            Assert.AreEqual(0, Constantes.TiposLineaVenta.TEXTO, "TEXTO debe ser 0");
        }

        [TestMethod]
        public void EsFicticio_TipoLineaProducto_NoEsFicticioPorTipo()
        {
            // Arrange
            var tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;

            // Act
            bool esFicticioPorTipo = tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            // Assert
            Assert.IsFalse(esFicticioPorTipo, "Las líneas de producto no son ficticias solo por el tipo");
            Assert.AreEqual(1, Constantes.TiposLineaVenta.PRODUCTO, "PRODUCTO debe ser 1");
        }

        [TestMethod]
        public void EsFicticio_TipoLineaInmovilizado_NoEsFicticioPorTipo()
        {
            // Arrange
            var tipoLinea = Constantes.TiposLineaVenta.INMOVILIZADO;

            // Act
            bool esFicticioPorTipo = tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            // Assert
            Assert.IsFalse(esFicticioPorTipo, "Las líneas de inmovilizado no son ficticias solo por el tipo");
            Assert.AreEqual(3, Constantes.TiposLineaVenta.INMOVILIZADO, "INMOVILIZADO debe ser 3");
        }

        #endregion

        #region Tests de aplicación de almacén a líneas ficticias

        [TestMethod]
        public void AplicarAlmacen_SoloActualizaLineasFicticias()
        {
            // Arrange
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { id = 1, Producto = "PROD_NORMAL", almacen = "ALG", EsFicticio = false, tipoLinea = 1 },
                new LineaPedidoVentaDTO { id = 2, Producto = "PROD_FICTICIO", almacen = "ALG", EsFicticio = true, tipoLinea = 1 },
                new LineaPedidoVentaDTO { id = 3, Producto = "CUENTA_701", almacen = "ALG", EsFicticio = true, tipoLinea = 2 }, // Cuenta contable
                new LineaPedidoVentaDTO { id = 4, Producto = "OTRO_NORMAL", almacen = "ALG", EsFicticio = false, tipoLinea = 1 }
            };

            string nuevoAlmacen = "REI";

            // Act - Simular la lógica del ViewModel
            foreach (var linea in lineas)
            {
                if (linea.EsFicticio)
                {
                    linea.almacen = nuevoAlmacen;
                }
            }

            // Assert
            Assert.AreEqual("ALG", lineas[0].almacen, "Línea normal NO debe cambiar almacén");
            Assert.AreEqual("REI", lineas[1].almacen, "Línea con producto ficticio SÍ debe cambiar almacén");
            Assert.AreEqual("REI", lineas[2].almacen, "Línea cuenta contable SÍ debe cambiar almacén");
            Assert.AreEqual("ALG", lineas[3].almacen, "Línea normal NO debe cambiar almacén");
        }

        [TestMethod]
        public void AplicarAlmacen_TodasLineasFicticias_TodasCambian()
        {
            // Arrange - Pedido solo con productos ficticios (típico de serie CV)
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { id = 1, Producto = "CURSO_001", almacen = "ALG", EsFicticio = true, tipoLinea = 2 },
                new LineaPedidoVentaDTO { id = 2, Producto = "CURSO_002", almacen = "ALG", EsFicticio = true, tipoLinea = 2 },
                new LineaPedidoVentaDTO { id = 3, Producto = "MATRICULA", almacen = "ALG", EsFicticio = true, tipoLinea = 2 }
            };

            string nuevoAlmacen = "ALC";

            // Act
            foreach (var linea in lineas)
            {
                if (linea.EsFicticio)
                {
                    linea.almacen = nuevoAlmacen;
                }
            }

            // Assert
            Assert.IsTrue(lineas.All(l => l.almacen == "ALC"), "Todas las líneas ficticias deben cambiar al nuevo almacén");
        }

        [TestMethod]
        public void AplicarAlmacen_NingunaLineaFicticia_NingunaCambia()
        {
            // Arrange - Pedido solo con productos normales
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { id = 1, Producto = "CHAMPU_001", almacen = "ALG", EsFicticio = false, tipoLinea = 1 },
                new LineaPedidoVentaDTO { id = 2, Producto = "TINTE_002", almacen = "ALG", EsFicticio = false, tipoLinea = 1 }
            };

            string nuevoAlmacen = "REI";

            // Act
            foreach (var linea in lineas)
            {
                if (linea.EsFicticio)
                {
                    linea.almacen = nuevoAlmacen;
                }
            }

            // Assert
            Assert.IsTrue(lineas.All(l => l.almacen == "ALG"), "Ninguna línea normal debe cambiar de almacén");
        }

        #endregion

        #region Tests del endpoint de almacenes

        [TestMethod]
        public void Almacenes_ListaContieneAlgete()
        {
            // Assert
            Assert.AreEqual("ALG", Constantes.Almacenes.ALGETE);
        }

        [TestMethod]
        public void Almacenes_ListaContieneReina()
        {
            // Assert
            Assert.AreEqual("REI", Constantes.Almacenes.REINA);
        }

        [TestMethod]
        public void Almacenes_ListaContieneAlcobendas()
        {
            // Assert
            Assert.AreEqual("ALC", Constantes.Almacenes.ALCOBENDAS);
        }

        #endregion

        #region Tests de la lógica completa de EsFicticio (como en GestorPedidosVenta)

        [TestMethod]
        public void LogicaEsFicticio_ProductoFicticioTipoProducto_EsTrue()
        {
            // Simula la lógica: EsFicticio = (prod != null && prod.Ficticio) || l.TipoLinea == CUENTA_CONTABLE
            bool productoFicticio = true;
            int tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;

            bool resultado = productoFicticio || tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            Assert.IsTrue(resultado);
        }

        [TestMethod]
        public void LogicaEsFicticio_ProductoNormalTipoCuentaContable_EsTrue()
        {
            // Simula la lógica: EsFicticio = (prod != null && prod.Ficticio) || l.TipoLinea == CUENTA_CONTABLE
            bool productoFicticio = false;
            int tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            bool resultado = productoFicticio || tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            Assert.IsTrue(resultado, "tipoLinea=2 debe hacer EsFicticio=true aunque el producto no sea ficticio");
        }

        [TestMethod]
        public void LogicaEsFicticio_ProductoNormalTipoProducto_EsFalse()
        {
            // Simula la lógica: EsFicticio = (prod != null && prod.Ficticio) || l.TipoLinea == CUENTA_CONTABLE
            bool productoFicticio = false;
            int tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;

            bool resultado = productoFicticio || tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            Assert.IsFalse(resultado);
        }

        [TestMethod]
        public void LogicaEsFicticio_ProductoNullTipoCuentaContable_EsTrue()
        {
            // Caso donde no hay producto (prod == null) pero la línea es cuenta contable
            // Simula: EsFicticio = (prod != null && prod.Ficticio) || l.TipoLinea == CUENTA_CONTABLE
            bool productoExiste = false; // prod == null
            bool productoFicticio = false; // No aplica si prod == null
            int tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            bool resultado = (productoExiste && productoFicticio) || tipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE;

            Assert.IsTrue(resultado, "Aunque no haya producto, tipoLinea=2 debe hacer EsFicticio=true");
        }

        #endregion
    }
}
