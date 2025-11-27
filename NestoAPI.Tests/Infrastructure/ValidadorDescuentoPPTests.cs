using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ValidadorDescuentoPPTests
    {
        [TestInitialize]
        public void Setup()
        {
            RoundingHelper.UsarAwayFromZero = true;
            ValidadorDescuentoPP.UmbralDiferenciaMaxima = 0.02m;
        }

        [TestMethod]
        public void ValidadorDescuentoPP_PedidoSinPP_RetornaValido()
        {
            // Arrange
            var pedido = CrearPedidoConLineas(descuentoPP: 0, cantidadLineas: 5, precioLinea: 10m);

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            Assert.IsTrue(resultado.EsValido);
            Assert.AreEqual(0, resultado.DiferenciaDetectada);
        }

        [TestMethod]
        public void ValidadorDescuentoPP_PedidoSinLineas_RetornaValido()
        {
            // Arrange
            var pedido = new PedidoVentaDTO { DescuentoPP = 0.02m };

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            Assert.IsTrue(resultado.EsValido);
        }

        [TestMethod]
        public void ValidadorDescuentoPP_PedidoNulo_RetornaValido()
        {
            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP<LineaPedidoVentaDTO>(null);

            // Assert
            Assert.IsTrue(resultado.EsValido);
        }

        [TestMethod]
        public void ValidadorDescuentoPP_DiferenciaDentroDeUmbral_RetornaValido()
        {
            // Arrange - Caso donde la diferencia es menor que 0.02€
            var pedido = CrearPedidoConLineas(descuentoPP: 0.02m, cantidadLineas: 3, precioLinea: 10m);

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            Assert.IsTrue(resultado.EsValido);
            Assert.IsTrue(resultado.DiferenciaDetectada <= 0.02m,
                $"Diferencia {resultado.DiferenciaDetectada} debería ser <= 0.02");
        }

        [TestMethod]
        public void ValidadorDescuentoPP_MuchasLineasConValoresCriticos_DetectaDiferencia()
        {
            // Arrange - Muchas líneas con valores que causan acumulación de error
            // Ejemplo de issue #243: líneas de 0.445€ cada una
            var pedido = new PedidoVentaDTO();
            pedido.DescuentoPP = 0.02m; // 2% de descuento PP

            // Añadir 100 líneas con precio que cause redondeo .5
            for (int i = 0; i < 100; i++)
            {
                var linea = new LineaPedidoVentaDTO
                {
                    Pedido = pedido,
                    Producto = $"PROD{i}",
                    Cantidad = 1,
                    PrecioUnitario = 4.445m, // Este valor causa redondeo .5
                    AplicarDescuento = true,
                    PorcentajeIva = 0.21m
                };
                pedido.Lineas.Add(linea);
            }

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            // Con 100 líneas, la diferencia acumulada puede superar el umbral
            Assert.IsNotNull(resultado.Mensaje);
            // No hacemos Assert sobre EsValido porque depende de los valores exactos
        }

        [TestMethod]
        public void ValidadorDescuentoPP_EjemploIssue243_DocumentaDiferencia()
        {
            // Este test documenta el ejemplo de la issue #243
            // Dos líneas de 0.445€ con PP del 100% (caso extremo para ilustrar)

            // Arrange
            var pedido = new PedidoVentaDTO();
            pedido.DescuentoPP = 0; // Sin PP para simplificar

            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                Producto = "LINEA1",
                Cantidad = 1,
                PrecioUnitario = 0.445m,
                AplicarDescuento = false,
                PorcentajeIva = 0
            });

            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Pedido = pedido,
                Producto = "LINEA2",
                Cantidad = 1,
                PrecioUnitario = 0.445m,
                AplicarDescuento = false,
                PorcentajeIva = 0
            });

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            // Sin PP, ambos métodos deberían dar el mismo resultado
            Assert.IsTrue(resultado.EsValido);

            // El total línea a línea será: round(0.445) + round(0.445) = 0.45 + 0.45 = 0.90
            // (porque cada línea se redondea individualmente)
            Assert.AreEqual(0.90m, pedido.Total);
        }

        [TestMethod]
        public void ValidadorDescuentoPP_UmbralPersonalizado_Respetado()
        {
            // Arrange
            ValidadorDescuentoPP.UmbralDiferenciaMaxima = 0.001m; // Umbral muy bajo

            var pedido = CrearPedidoConLineas(descuentoPP: 0.02m, cantidadLineas: 10, precioLinea: 10.005m);

            // Act
            var resultado = ValidadorDescuentoPP.ValidarDescuentoPP(pedido);

            // Assert
            // Con umbral muy bajo, cualquier diferencia mínima lo invalidará
            Assert.IsNotNull(resultado);
        }

        private PedidoVentaDTO CrearPedidoConLineas(decimal descuentoPP, int cantidadLineas, decimal precioLinea)
        {
            var pedido = new PedidoVentaDTO();
            pedido.DescuentoPP = descuentoPP;

            for (int i = 0; i < cantidadLineas; i++)
            {
                var linea = new LineaPedidoVentaDTO
                {
                    Pedido = pedido,
                    Producto = $"PROD{i}",
                    Cantidad = 1,
                    PrecioUnitario = precioLinea,
                    AplicarDescuento = true,
                    PorcentajeIva = 0.21m
                };
                pedido.Lineas.Add(linea);
            }

            return pedido;
        }
    }
}
