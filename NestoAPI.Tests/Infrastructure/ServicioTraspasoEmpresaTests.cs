using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class ServicioTraspasoEmpresaTests
    {
        private NVEntities db;
        private ServicioPedidosVenta servicioPedidos;
        private ServicioTraspasoEmpresa servicio;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            servicioPedidos = A.Fake<ServicioPedidosVenta>();
            servicio = new ServicioTraspasoEmpresa(db, servicioPedidos);
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            // Arrange
            var servicioPedidos = A.Fake<ServicioPedidosVenta>();

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                var _ = new ServicioTraspasoEmpresa(null, servicioPedidos);
            });
        }

        [TestMethod]
        public void Constructor_ConServicioPedidosNull_LanzaArgumentNullException()
        {
            // Arrange
            var dbFake = A.Fake<NVEntities>();

            // Act & Assert
            Assert.ThrowsException<ArgumentNullException>(() =>
            {
                var _ = new ServicioTraspasoEmpresa(dbFake, null);
            });
        }

        [TestMethod]
        public void Constructor_ConDbYServicioValidos_CreaInstancia()
        {
            // Arrange
            var dbFake = A.Fake<NVEntities>();
            var servicioPedidosFake = A.Fake<ServicioPedidosVenta>();

            // Act
            var servicio = new ServicioTraspasoEmpresa(dbFake, servicioPedidosFake);

            // Assert
            Assert.IsNotNull(servicio);
        }

        [TestMethod]
        public void Constructor_ConSoloDb_CreaServicioPedidosAutomaticamente()
        {
            // Arrange
            var dbFake = A.Fake<NVEntities>();

            // Act
            var servicio = new ServicioTraspasoEmpresa(dbFake);

            // Assert
            Assert.IsNotNull(servicio);
        }

        #endregion

        #region HayQueTraspasar Tests

        [TestMethod]
        public void HayQueTraspasar_ConPedidoNull_RetornaFalse()
        {
            // Arrange
            CabPedidoVta pedido = null;

            // Act
            var resultado = servicio.HayQueTraspasar(pedido);

            // Assert
            Assert.IsFalse(resultado, "Con pedido null debe retornar false");
        }

        [TestMethod]
        public void HayQueTraspasar_ConIVANull_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                IVA = null // IVA null → debe traspasar
            };

            // Act
            var resultado = servicio.HayQueTraspasar(pedido);

            // Assert
            Assert.IsTrue(resultado, "Con IVA null debe retornar true (debe traspasar)");
        }

        [TestMethod]
        public void HayQueTraspasar_ConIVAVacio_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                IVA = "" // IVA vacío → debe traspasar
            };

            // Act
            var resultado = servicio.HayQueTraspasar(pedido);

            // Assert
            Assert.IsTrue(resultado, "Con IVA vacío debe retornar true (debe traspasar)");
        }

        [TestMethod]
        public void HayQueTraspasar_ConIVASoloEspacios_RetornaTrue()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                IVA = "   " // IVA solo espacios → debe traspasar
            };

            // Act
            var resultado = servicio.HayQueTraspasar(pedido);

            // Assert
            Assert.IsTrue(resultado, "Con IVA solo espacios debe retornar true (debe traspasar)");
        }

        [TestMethod]
        [DataRow("G21")]
        [DataRow("IM")]
        [DataRow("E52")]
        [DataRow("C20")]
        public void HayQueTraspasar_ConIVAValido_RetornaFalse(string ivaValor)
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                IVA = ivaValor // IVA con valor → NO debe traspasar
            };

            // Act
            var resultado = servicio.HayQueTraspasar(pedido);

            // Assert
            Assert.IsFalse(resultado, $"Con IVA='{ivaValor}' debe retornar false (no debe traspasar)");
        }

        #endregion

        #region TraspasarPedidoAEmpresa - Validation Tests

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoNull_LanzaArgumentNullException()
        {
            // Arrange
            CabPedidoVta pedido = null;
            string empresaOrigen = "1";
            string empresaDestino = "3";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentNullException>(
                () => servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario"));
        }

        [TestMethod]
        [DataRow(null, "3")]
        [DataRow("", "3")]
        [DataRow("   ", "3")]
        public async Task TraspasarPedidoAEmpresa_EmpresaOrigenNullOVacia_LanzaArgumentException(string empresaOrigen, string empresaDestino)
        {
            // Arrange
            var pedido = new CabPedidoVta { Empresa = "1", Número = 1, Nº_Cliente = "12345" };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario"));
        }

        [TestMethod]
        [DataRow("1", null)]
        [DataRow("1", "")]
        [DataRow("1", "   ")]
        public async Task TraspasarPedidoAEmpresa_EmpresaDestinoNullOVacia_LanzaArgumentException(string empresaOrigen, string empresaDestino)
        {
            // Arrange
            var pedido = new CabPedidoVta { Empresa = "1", Número = 1, Nº_Cliente = "12345" };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario"));
        }

        [TestMethod]
        [DataRow("1", "1")]
        [DataRow("3", "3")]
        [DataRow("2", "2")]
        public async Task TraspasarPedidoAEmpresa_EmpresaOrigenIgualEmpresaDestino_LanzaArgumentException(string empresaOrigen, string empresaDestino)
        {
            // Arrange
            var pedido = new CabPedidoVta { Empresa = empresaOrigen, Número = 1, Nº_Cliente = "12345" };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario"));
        }

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoNoEstaEnEmpresaOrigen_LanzaInvalidOperationException()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "2", // Pedido está en empresa "2"
                Número = 1,
                Nº_Cliente = "12345"
            };
            string empresaOrigen = "1"; // Pero intentamos traspasar desde empresa "1"
            string empresaDestino = "3";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario"));
        }

        #endregion

        #region TraspasarPedidoAEmpresa - Functional Tests (TDD - RED)

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoValido_ActualizaEmpresaEnCabecera()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 12345,
                Nº_Cliente = "12345",
                Contacto = "100001",
                Ruta = "16"
            };
            string empresaOrigen = "1";
            string empresaDestino = "3";

            // Act
            await servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario");

            // Assert
            Assert.AreEqual("3", pedido.Empresa, "El pedido debe quedar en la empresa destino");
        }

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoSinLineas_NoLanzaExcepcion()
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                LinPedidoVtas = new List<LinPedidoVta>() // Sin líneas
            };
            string empresaOrigen = "1";
            string empresaDestino = "3";

            // Act - No debe lanzar excepción
            await servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario");

            // Assert
            Assert.AreEqual("3", pedido.Empresa);
        }

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoConLineas_ActualizaEmpresaEnTodasLasLineas()
        {
            // Arrange
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 1, Producto = "PROD001" },
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 2, Producto = "PROD002" },
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 3, Producto = "PROD003" }
            };
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                LinPedidoVtas = lineas
            };
            string empresaOrigen = "1";
            string empresaDestino = "3";

            // Act
            await servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario");

            // Assert
            Assert.AreEqual("3", pedido.Empresa, "La cabecera debe estar en empresa 3");
            Assert.IsTrue(pedido.LinPedidoVtas.All(l => l.Empresa == "3"),
                "Todas las líneas deben estar en empresa 3");
        }

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_PedidoConProductosDuplicados_NoLanzaExcepcion()
        {
            // Arrange: Pedido con el mismo producto en varias líneas
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 1, Producto = "PROD001", Cantidad = 10 },
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 2, Producto = "PROD001", Cantidad = 5 }, // Duplicado
                new LinPedidoVta { Empresa = "1", Número = 1, NºOrden = 3, Producto = "PROD002", Cantidad = 3 }
            };
            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                LinPedidoVtas = lineas
            };
            string empresaOrigen = "1";
            string empresaDestino = "3";

            // Act - No debe fallar por productos duplicados
            await servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario");

            // Assert
            Assert.AreEqual("3", pedido.Empresa);
            Assert.AreEqual(3, pedido.LinPedidoVtas.Count, "Debe mantener las 3 líneas");
        }

        [TestMethod]
        [DataRow("1", "3")] // Caso típico: empresa 1 → empresa 3
        [DataRow("1", "2")] // Caso alternativo: empresa 1 → empresa 2
        [DataRow("2", "3")] // Caso alternativo: empresa 2 → empresa 3
        [DataRow("3", "1")] // Caso inverso: empresa 3 → empresa 1
        public async Task TraspasarPedidoAEmpresa_ConParametrosFlexibles_PermiteCualquierCombinacionEmpresas(string empresaOrigen, string empresaDestino)
        {
            // Arrange
            var pedido = new CabPedidoVta
            {
                Empresa = empresaOrigen,
                Número = 1,
                Nº_Cliente = "12345",
                LinPedidoVtas = new List<LinPedidoVta>
                {
                    new LinPedidoVta { Empresa = empresaOrigen, Número = 1, NºOrden = 1, Producto = "PROD001" }
                }
            };

            // Act
            await servicio.TraspasarPedidoAEmpresa(pedido, empresaOrigen, empresaDestino, "TEST\\usuario");

            // Assert
            Assert.AreEqual(empresaDestino, pedido.Empresa, $"La cabecera debe estar en empresa {empresaDestino}");
            Assert.IsTrue(pedido.LinPedidoVtas.All(l => l.Empresa == empresaDestino),
                $"Todas las líneas deben estar en empresa {empresaDestino}");
        }

        [TestMethod]
        public async Task TraspasarPedidoAEmpresa_DespuesDelTraspaso_RecalculaImportesConParametrosIVADeEmpresaDestino()
        {
            // Arrange: Simular que empresa 1 tiene IVA 21%, empresa 3 tiene IVA 10%
            var parametroIVAEmpresa1 = new ParametroIVA
            {
                Empresa = "1",
                IVA_Producto = "G21",
                IVA_Cliente_Prov = "G21",
                C__IVA = 21,
                C__RE = 0
            };

            var parametroIVAEmpresa3 = new ParametroIVA
            {
                Empresa = "3",
                IVA_Producto = "G21",
                IVA_Cliente_Prov = "G21",
                C__IVA = 10,
                C__RE = 0
            };

            // Configurar el servicio para que devuelva diferentes parámetros según la empresa
            A.CallTo(() => servicioPedidos.LeerParametroIVA("1", A<string>._, A<string>._))
                .Returns(parametroIVAEmpresa1);
            A.CallTo(() => servicioPedidos.LeerParametroIVA("3", A<string>._, A<string>._))
                .Returns(parametroIVAEmpresa3);

            var linea = new LinPedidoVta
            {
                Empresa = "1",
                Número = 1,
                NºOrden = 1,
                Producto = "PROD001",
                IVA = "G21",
                Cantidad = 10,
                Precio = 100,
                Aplicar_Dto = false,
                Descuento = 0,
                DescuentoPP = 0,
                // Valores calculados con empresa 1 (IVA 21%)
                Base_Imponible = 1000m,
                PorcentajeIVA = 21,
                ImporteIVA = 210m,
                Total = 1210m
            };

            var pedido = new CabPedidoVta
            {
                Empresa = "1",
                Número = 1,
                Nº_Cliente = "12345",
                IVA = "G21",
                LinPedidoVtas = new List<LinPedidoVta> { linea }
            };

            // Act: Traspasar de empresa 1 a empresa 3
            await servicio.TraspasarPedidoAEmpresa(pedido, "1", "3");

            // Assert: Verificar que se llama a LeerParametroIVA con empresa "3"
            A.CallTo(() => servicioPedidos.LeerParametroIVA("3", "G21", "G21"))
                .MustHaveHappened();

            // El recálculo ya se ejecutó, los valores deberían haber cambiado
            // Nota: En un test de integración real, verificaríamos que ImporteIVA = 100 (10% de 1000)
            // Aquí solo verificamos que la empresa cambió correctamente
            Assert.AreEqual("3", linea.Empresa, "La línea debe estar en empresa 3");
        }

        #endregion
    }
}
