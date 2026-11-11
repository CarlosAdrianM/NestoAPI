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

        /// <summary>
        /// NestoAPI#733 (regresión 928217, 29/10/26): al traspasar a la espejo solo viajan las líneas albaraneadas; las
        /// pendientes se quedan en la empresa 1, donde están el stock y las reservas de hueco, para la siguiente entrega.
        /// </summary>
        [TestMethod]
        public void SqlMoverLineas_SoloMueveLasAlbaraneadas()
        {
            string sql = System.Text.RegularExpressions.Regex.Replace(ServicioTraspasoEmpresa.SQL_MOVER_LINEAS_ALBARANEADAS, @"\s+", " ").Trim();

            Assert.AreEqual("UPDATE LinPedidoVta SET Empresa = @EmpresaDestino WHERE Empresa = @EmpresaOrigen AND Número = @NumeroPedido AND Estado >= 2", sql);
        }

        /// <summary>
        /// 11/11/26 (albaranes 732939 y 732940, pedidos 928723 y 921251): el traspaso a la espejo movía líneas y cabecera del
        /// albarán, pero dejaba Ubicaciones.EmpresaAlbaranVta en la empresa 1. Al deshacer el albarán en la 3, la reserva de
        /// picking no se devolvía y el hueco quedaba descuadrado. Rojo antes del arreglo: la sentencia no existía.
        /// </summary>
        [TestMethod]
        public void SqlMoverUbicaciones_PasaEmpresaAlbaranVtaALaEmpresaDestino()
        {
            string sql = System.Text.RegularExpressions.Regex.Replace(ServicioTraspasoEmpresa.SQL_MOVER_UBICACIONES_ALBARANES, @"\s+", " ").Trim();

            Assert.AreEqual(
                "UPDATE u SET EmpresaAlbaranVta = @EmpresaDestino FROM Ubicaciones u " +
                "INNER JOIN LinPedidoVta l ON l.[Nº Orden] = u.NºOrdenVta " +
                "WHERE l.Empresa = @EmpresaDestino AND l.Número = @NumeroPedido AND l.[Nº Albarán] IS NOT NULL " +
                "AND u.AlbaránVta = l.[Nº Albarán] AND u.EmpresaAlbaranVta = @EmpresaOrigen " +
                "AND EXISTS (SELECT 1 FROM CabAlbaránVta c WHERE c.Empresa = @EmpresaDestino AND c.Número = u.AlbaránVta) " +
                "AND NOT EXISTS (SELECT 1 FROM CabAlbaránVta o WHERE o.Empresa = @EmpresaOrigen AND o.Número = u.AlbaránVta)",
                sql);
        }

        [TestMethod]
        public void SqlMoverUbicaciones_NoTocaLaEmpresaDeLaUbicacion_SoloLaDelAlbaran()
        {
            // El stock y los huecos viven en la empresa 1: Ubicaciones.Empresa no se mueve nunca.
            string sql = ServicioTraspasoEmpresa.SQL_MOVER_UBICACIONES_ALBARANES;

            StringAssert.Contains(sql, "SET EmpresaAlbaranVta = @EmpresaDestino");
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(sql, @"SET\s+Empresa\s*="), "No debe cambiar Ubicaciones.Empresa");
        }

        [TestMethod]
        public void OrdenDelTraspasoDelAlbaran_LasUbicacionesVanDespuesDeLaCabecera()
        {
            // La sentencia de Ubicaciones exige que la cabecera YA esté en la empresa destino.
            CollectionAssert.AreEqual(new[] { "LinPedidoVta", "CabAlbaránVta", "Ubicaciones" }, ServicioTraspasoEmpresa.ORDEN_TRASPASO_ALBARAN);

            string fuente = System.IO.File.ReadAllText(RutaDelServicio());
            int cabeceras = fuente.IndexOf("@\"UPDATE CabAlbaránVta", System.StringComparison.Ordinal);
            int ubicaciones = fuente.LastIndexOf("SQL_MOVER_UBICACIONES_ALBARANES,", System.StringComparison.Ordinal);
            Assert.IsTrue(cabeceras > 0 && ubicaciones > cabeceras, "El UPDATE de Ubicaciones tiene que ejecutarse después del de CabAlbaránVta");
        }

        private static string RutaDelServicio()
        {
            // De NestoAPI.Tests\bin\Debug hasta la raíz de la solución.
            string carpeta = System.AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && carpeta != null; i++)
            {
                string candidato = System.IO.Path.Combine(carpeta, "NestoAPI", "Infraestructure", "Traspasos", "ServicioTraspasoEmpresa.cs");
                if (System.IO.File.Exists(candidato))
                {
                    return candidato;
                }
                carpeta = System.IO.Path.GetDirectoryName(carpeta);
            }
            Assert.Inconclusive("No se encuentra ServicioTraspasoEmpresa.cs desde la carpeta de los tests");
            return null;
        }

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
