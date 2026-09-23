using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
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

        #region VaAEmpresaEspejo (regla compartida con la validación de NIF #327)

        [TestMethod]
        public void VaAEmpresaEspejo_IvaVacioNullOEspacios_EsTrue()
        {
            // Caso cliente 9093 (22/07/26): un pedido sin IVA acabará en la espejo (serie GB,
            // sin Verifactu) y NO debe validar el NIF ni marcar la ficha.
            Assert.IsTrue(NestoAPI.Infraestructure.Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo(null));
            Assert.IsTrue(NestoAPI.Infraestructure.Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo(""));
            Assert.IsTrue(NestoAPI.Infraestructure.Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo("   "));
        }

        [TestMethod]
        public void VaAEmpresaEspejo_ConIva_EsFalse()
        {
            Assert.IsFalse(NestoAPI.Infraestructure.Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo("G21"));
            Assert.IsFalse(NestoAPI.Infraestructure.Traspasos.ServicioTraspasoEmpresa.VaAEmpresaEspejo("I22"));
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

        #region Recálculo de importes tras el traspaso

        // NestoAPI#313 (23/09/26): los tests «funcionales» de TraspasarPedidoAEmpresa (pedido válido, sin
        // líneas, con líneas, productos duplicados, combinaciones de empresas) se han retirado: ejecutan
        // prdCopiarCliente/prdCopiarProducto y UPDATE/DELETE por SqlCommand sobre la conexión y la
        // transacción REALES del contexto (db.Database, no virtual), así que con un NVEntities falso nunca
        // pudieron pasar ("No connection string named 'NVEntities'"). Son de integración y necesitan BD.
        // Lo que sí es lógica propia y comprobable sin BD es el paso 12: recalcular los importes de las
        // líneas con los ParámetrosIVA de la empresa DESTINO (GestorPedidosVenta.RecalcularImportesLineasPedido).

        [TestMethod]
        public void RecalcularImportesLineasPedido_TrasTraspasar_UsaLosParametrosIVADeLaEmpresaDestino()
        {
            // Empresa 1 con IVA 21 %, empresa 3 con IVA 10 %: la línea ya está en la empresa 3.
            var servicioFake = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicioFake.LeerParametroIVA("1", A<string>._, A<string>._))
                .Returns(new ParametroIVA { Empresa = "1", IVA_Producto = "G21", IVA_Cliente_Prov = "G21", C__IVA = 21, C__RE = 0 });
            A.CallTo(() => servicioFake.LeerParametroIVA("3", A<string>._, A<string>._))
                .Returns(new ParametroIVA { Empresa = "3", IVA_Producto = "G21", IVA_Cliente_Prov = "G21", C__IVA = 10, C__RE = 0 });
            var linea = new LinPedidoVta
            {
                Empresa = "3", Número = 1, Nº_Orden = 1, Producto = "PROD001", IVA = "G21",
                Cantidad = 10, Precio = 100, Aplicar_Dto = false, Descuento = 0, DescuentoPP = 0,
                // Importes calculados en la empresa de ORIGEN (21 %)
                Base_Imponible = 1000m, PorcentajeIVA = 21, ImporteIVA = 210m, Total = 1210m
            };
            var pedido = new CabPedidoVta { Empresa = "3", Número = 1, Nº_Cliente = "12345", IVA = "G21", LinPedidoVtas = new List<LinPedidoVta> { linea } };

            new GestorPedidosVenta(servicioFake).RecalcularImportesLineasPedido(pedido);

            Assert.AreEqual(10, linea.PorcentajeIVA);
            Assert.AreEqual(100m, linea.ImporteIVA, "10 % de 1.000, no el 21 % de la empresa de origen");
            Assert.AreEqual(1100m, linea.Total);
            A.CallTo(() => servicioFake.LeerParametroIVA("3", "G21", "G21")).MustHaveHappened();
            A.CallTo(() => servicioFake.LeerParametroIVA("1", A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void RecalcularImportesLineasPedido_PedidoSinLineas_NoHaceNada()
        {
            var servicioFake = A.Fake<IServicioPedidosVenta>();
            var pedido = new CabPedidoVta { Empresa = "3", Número = 1, IVA = "G21", LinPedidoVtas = new List<LinPedidoVta>() };

            new GestorPedidosVenta(servicioFake).RecalcularImportesLineasPedido(pedido);

            A.CallTo(() => servicioFake.LeerParametroIVA(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        #endregion
    }
}
