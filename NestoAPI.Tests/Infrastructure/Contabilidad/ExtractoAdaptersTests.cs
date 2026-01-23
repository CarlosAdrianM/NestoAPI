using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    [TestClass]
    public class ExtractoAdaptersTests
    {
        #region ExtractoClienteAdapter

        [TestMethod]
        public void ExtractoClienteAdapter_ImportePositivo_EsDebe()
        {
            // Arrange
            var adapter = new ExtractoClienteAdapter();
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Fecha = new DateTime(2025, 1, 15),
                    Concepto = "Factura 001",
                    Nº_Documento = "F001",
                    Importe = 1210.00m,
                    TipoApunte = "1"
                }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(1210.00m, resultado[0].Debe);
            Assert.AreEqual(0m, resultado[0].Haber);
        }

        [TestMethod]
        public void ExtractoClienteAdapter_ImporteNegativo_EsHaber()
        {
            // Arrange
            var adapter = new ExtractoClienteAdapter();
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Fecha = new DateTime(2025, 1, 20),
                    Concepto = "Cobro transferencia",
                    Nº_Documento = "T001",
                    Importe = -1210.00m,
                    TipoApunte = "3"
                }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(0m, resultado[0].Debe);
            Assert.AreEqual(1210.00m, resultado[0].Haber);
        }

        [TestMethod]
        public void ExtractoClienteAdapter_MultiplesMovimientos_AdaptaTodos()
        {
            // Arrange
            var adapter = new ExtractoClienteAdapter();
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente { Fecha = new DateTime(2025, 1, 1), Concepto = "Factura 1", Importe = 100m, TipoApunte = "1" },
                new ExtractoCliente { Fecha = new DateTime(2025, 1, 15), Concepto = "Cobro 1", Importe = -50m, TipoApunte = "3" },
                new ExtractoCliente { Fecha = new DateTime(2025, 1, 20), Concepto = "Factura 2", Importe = 200m, TipoApunte = "1" }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(3, resultado.Count);
            Assert.AreEqual(100m, resultado[0].Debe);
            Assert.AreEqual(50m, resultado[1].Haber);
            Assert.AreEqual(200m, resultado[2].Debe);
        }

        [TestMethod]
        public void ExtractoClienteAdapter_ConservaFechaYConcepto()
        {
            // Arrange
            var adapter = new ExtractoClienteAdapter();
            var fecha = new DateTime(2025, 3, 15);
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente
                {
                    Fecha = fecha,
                    Concepto = "  Factura con espacios  ",
                    Nº_Documento = " DOC123 ",
                    Importe = 500m,
                    TipoApunte = "1"
                }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).First();

            // Assert
            Assert.AreEqual(fecha, resultado.Fecha);
            Assert.AreEqual("Factura con espacios", resultado.Concepto);
            Assert.AreEqual("DOC123", resultado.NumeroDocumento);
            Assert.AreEqual("1", resultado.TipoApunte);
        }

        [TestMethod]
        public void ExtractoClienteAdapter_ImporteCero_NoGeneraDebeNiHaber()
        {
            // Arrange
            var adapter = new ExtractoClienteAdapter();
            var extractos = new List<ExtractoCliente>
            {
                new ExtractoCliente { Fecha = DateTime.Now, Concepto = "Ajuste", Importe = 0m }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).First();

            // Assert
            Assert.AreEqual(0m, resultado.Debe);
            Assert.AreEqual(0m, resultado.Haber);
        }

        #endregion

        #region ExtractoProveedorAdapter

        [TestMethod]
        public void ExtractoProveedorAdapter_ImportePositivo_EsHaber()
        {
            // Arrange - Para proveedores: positivo = haber (factura de proveedor a pagar)
            var adapter = new ExtractoProveedorAdapter();
            var extractos = new List<ExtractoProveedor>
            {
                new ExtractoProveedor
                {
                    Fecha = new DateTime(2025, 2, 10),
                    Concepto = "Factura proveedor",
                    NºDocumento = "FP001",
                    Importe = 5000.00m,
                    TipoApunte = "1"
                }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(0m, resultado[0].Debe);
            Assert.AreEqual(5000.00m, resultado[0].Haber);
        }

        [TestMethod]
        public void ExtractoProveedorAdapter_ImporteNegativo_EsDebe()
        {
            // Arrange - Para proveedores: negativo = debe (pago a proveedor)
            var adapter = new ExtractoProveedorAdapter();
            var extractos = new List<ExtractoProveedor>
            {
                new ExtractoProveedor
                {
                    Fecha = new DateTime(2025, 2, 25),
                    Concepto = "Pago a proveedor",
                    NºDocumento = "PAG001",
                    Importe = -5000.00m,
                    TipoApunte = "3"
                }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(5000.00m, resultado[0].Debe);
            Assert.AreEqual(0m, resultado[0].Haber);
        }

        [TestMethod]
        public void ExtractoProveedorAdapter_MultiplesMovimientos_AdaptaTodos()
        {
            // Arrange
            var adapter = new ExtractoProveedorAdapter();
            var extractos = new List<ExtractoProveedor>
            {
                new ExtractoProveedor { Fecha = new DateTime(2025, 1, 1), Concepto = "Factura 1", Importe = 1000m, TipoApunte = "1" },
                new ExtractoProveedor { Fecha = new DateTime(2025, 1, 15), Concepto = "Pago 1", Importe = -500m, TipoApunte = "3" },
                new ExtractoProveedor { Fecha = new DateTime(2025, 1, 20), Concepto = "Factura 2", Importe = 2000m, TipoApunte = "1" }
            };

            // Act
            var resultado = adapter.Adaptar(extractos).ToList();

            // Assert
            Assert.AreEqual(3, resultado.Count);
            Assert.AreEqual(1000m, resultado[0].Haber);  // Factura proveedor = haber
            Assert.AreEqual(500m, resultado[1].Debe);    // Pago = debe
            Assert.AreEqual(2000m, resultado[2].Haber);  // Factura proveedor = haber
        }

        [TestMethod]
        public void ExtractoProveedorAdapter_SignoInversoAClientes()
        {
            // Arrange - Verificar que el comportamiento es inverso a clientes
            var adapterCliente = new ExtractoClienteAdapter();
            var adapterProveedor = new ExtractoProveedorAdapter();

            var extractoCliente = new List<ExtractoCliente>
            {
                new ExtractoCliente { Fecha = DateTime.Now, Concepto = "Test", Importe = 100m }
            };
            var extractoProveedor = new List<ExtractoProveedor>
            {
                new ExtractoProveedor { Fecha = DateTime.Now, Concepto = "Test", Importe = 100m }
            };

            // Act
            var resultadoCliente = adapterCliente.Adaptar(extractoCliente).First();
            var resultadoProveedor = adapterProveedor.Adaptar(extractoProveedor).First();

            // Assert - Mismo importe positivo: cliente = debe, proveedor = haber
            Assert.AreEqual(100m, resultadoCliente.Debe);
            Assert.AreEqual(0m, resultadoCliente.Haber);
            Assert.AreEqual(0m, resultadoProveedor.Debe);
            Assert.AreEqual(100m, resultadoProveedor.Haber);
        }

        #endregion
    }
}
