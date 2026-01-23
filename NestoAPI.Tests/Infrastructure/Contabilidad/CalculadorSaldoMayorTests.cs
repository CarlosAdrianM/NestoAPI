using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models.Mayor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    [TestClass]
    public class CalculadorSaldoMayorTests
    {
        #region ConvertirSaldoAnteriorProveedor

        [TestMethod]
        public void ConvertirSaldoAnteriorProveedor_SaldoPositivo_DevuelveNegativo()
        {
            // Arrange - Proveedor con facturas pendientes (importe positivo en extracto = haber)
            // Si tenemos facturas por 63775.08, el saldo en formato Debe-Haber debe ser -63775.08
            decimal sumatoriaImportes = 63775.08m;

            // Act
            decimal resultado = CalculadorSaldoMayor.ConvertirSaldoAnteriorProveedor(sumatoriaImportes);

            // Assert - Saldo negativo indica que debemos al proveedor (saldo acreedor)
            Assert.AreEqual(-63775.08m, resultado);
        }

        [TestMethod]
        public void ConvertirSaldoAnteriorProveedor_SaldoNegativo_DevuelvePositivo()
        {
            // Arrange - Proveedor con pagos superiores a facturas (anticipo)
            decimal sumatoriaImportes = -5000m;

            // Act
            decimal resultado = CalculadorSaldoMayor.ConvertirSaldoAnteriorProveedor(sumatoriaImportes);

            // Assert - Saldo positivo indica que el proveedor nos debe (poco comun)
            Assert.AreEqual(5000m, resultado);
        }

        [TestMethod]
        public void ConvertirSaldoAnteriorProveedor_SaldoCero_DevuelveCero()
        {
            // Arrange
            decimal sumatoriaImportes = 0m;

            // Act
            decimal resultado = CalculadorSaldoMayor.ConvertirSaldoAnteriorProveedor(sumatoriaImportes);

            // Assert
            Assert.AreEqual(0m, resultado);
        }

        #endregion

        #region CalcularSaldos

        [TestMethod]
        public void CalcularSaldos_ClienteConMovimientos_CalculaSaldoCorrectamente()
        {
            // Arrange - Cliente con factura 1000 y cobro 400
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO { Debe = 1000m, Haber = 0m },  // Factura
                new MovimientoMayorDTO { Debe = 0m, Haber = 400m }    // Cobro
            };
            decimal saldoAnterior = 0m;

            // Act
            var (totalDebe, totalHaber, saldoFinal) = CalculadorSaldoMayor.CalcularSaldos(movimientos, saldoAnterior);

            // Assert
            Assert.AreEqual(1000m, totalDebe);
            Assert.AreEqual(400m, totalHaber);
            Assert.AreEqual(600m, saldoFinal);  // 1000 - 400 = 600 (nos debe 600)
            Assert.AreEqual(1000m, movimientos[0].Saldo);  // Saldo tras factura
            Assert.AreEqual(600m, movimientos[1].Saldo);   // Saldo tras cobro
        }

        [TestMethod]
        public void CalcularSaldos_ProveedorConMovimientos_CalculaSaldoCorrectamente()
        {
            // Arrange - Proveedor con factura 5000 (haber) y pago 2000 (debe)
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO { Debe = 0m, Haber = 5000m },  // Factura proveedor
                new MovimientoMayorDTO { Debe = 2000m, Haber = 0m }   // Pago a proveedor
            };
            decimal saldoAnterior = 0m;

            // Act
            var (totalDebe, totalHaber, saldoFinal) = CalculadorSaldoMayor.CalcularSaldos(movimientos, saldoAnterior);

            // Assert
            Assert.AreEqual(2000m, totalDebe);
            Assert.AreEqual(5000m, totalHaber);
            Assert.AreEqual(-3000m, saldoFinal);  // 2000 - 5000 = -3000 (debemos 3000)
            Assert.AreEqual(-5000m, movimientos[0].Saldo);  // Saldo tras factura
            Assert.AreEqual(-3000m, movimientos[1].Saldo);  // Saldo tras pago
        }

        [TestMethod]
        public void CalcularSaldos_ProveedorConSaldoAnterior_SumaCorrectamente()
        {
            // Arrange - Proveedor con saldo anterior de 63775.08 al haber
            // El saldo anterior ya debe venir convertido a formato Debe-Haber
            decimal saldoAnteriorConvertido = CalculadorSaldoMayor.ConvertirSaldoAnteriorProveedor(63775.08m);

            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO { Debe = 0m, Haber = 1000m },   // Nueva factura
                new MovimientoMayorDTO { Debe = 10000m, Haber = 0m }   // Pago
            };

            // Act
            var (totalDebe, totalHaber, saldoFinal) = CalculadorSaldoMayor.CalcularSaldos(movimientos, saldoAnteriorConvertido);

            // Assert
            Assert.AreEqual(10000m, totalDebe);
            Assert.AreEqual(1000m, totalHaber);
            // Saldo = -63775.08 + (10000 - 1000) = -63775.08 + 9000 = -54775.08
            Assert.AreEqual(-54775.08m, saldoFinal);
            // Primer movimiento: -63775.08 + (0 - 1000) = -64775.08
            Assert.AreEqual(-64775.08m, movimientos[0].Saldo);
            // Segundo movimiento: -64775.08 + (10000 - 0) = -54775.08
            Assert.AreEqual(-54775.08m, movimientos[1].Saldo);
        }

        [TestMethod]
        public void CalcularSaldos_ClienteConSaldoAnterior_SumaCorrectamente()
        {
            // Arrange - Cliente con saldo anterior de 5000 (nos debe 5000)
            decimal saldoAnterior = 5000m;  // Para clientes no hay conversion

            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO { Debe = 1000m, Haber = 0m },  // Factura
                new MovimientoMayorDTO { Debe = 0m, Haber = 3000m }   // Cobro
            };

            // Act
            var (totalDebe, totalHaber, saldoFinal) = CalculadorSaldoMayor.CalcularSaldos(movimientos, saldoAnterior);

            // Assert
            Assert.AreEqual(1000m, totalDebe);
            Assert.AreEqual(3000m, totalHaber);
            // Saldo = 5000 + 1000 - 3000 = 3000
            Assert.AreEqual(3000m, saldoFinal);
        }

        #endregion

        #region EliminarPasoACartera

        [TestMethod]
        public void EliminarPasoACartera_ClienteConPasoCartera_EliminaParCorrectamente()
        {
            // Arrange - Factura y su paso a cartera (cliente)
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "1",
                    Debe = 500m,
                    Haber = 0m,
                    Concepto = "Factura"
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "0",
                    Debe = 0m,
                    Haber = 500m,
                    Concepto = "Paso a cartera"
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "2",
                    Debe = 500m,
                    Haber = 0m,
                    Concepto = "Cartera"
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Solo debe quedar la cartera
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("2", resultado[0].TipoApunte);
        }

        [TestMethod]
        public void EliminarPasoACartera_ProveedorConPasoCartera_EliminaParCorrectamente()
        {
            // Arrange - Factura y su paso a cartera (proveedor)
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "FP001",
                    TipoApunte = "1",
                    Debe = 0m,
                    Haber = 1000m,  // Factura proveedor va al Haber
                    Concepto = "Factura proveedor"
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "FP001",
                    TipoApunte = "0",
                    Debe = 1000m,   // Paso a cartera va al Debe (inverso)
                    Haber = 0m,
                    Concepto = "Paso a cartera"
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "FP001",
                    TipoApunte = "2",
                    Debe = 0m,
                    Haber = 1000m,
                    Concepto = "Cartera"
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Solo debe quedar la cartera
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("2", resultado[0].TipoApunte);
        }

        [TestMethod]
        public void EliminarPasoACartera_SinPasoCartera_MantieneMovimientos()
        {
            // Arrange - Movimientos sin paso a cartera
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "2",
                    Debe = 500m,
                    Haber = 0m,
                    Concepto = "Cartera"
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 20),
                    NumeroDocumento = "C001",
                    TipoApunte = "3",
                    Debe = 0m,
                    Haber = 500m,
                    Concepto = "Cobro"
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Mantiene todos los movimientos
            Assert.AreEqual(2, resultado.Count);
        }

        [TestMethod]
        public void EliminarPasoACartera_DocumentosDiferentes_NoElimina()
        {
            // Arrange - Factura y paso a cartera con documentos diferentes
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "1",
                    Debe = 500m,
                    Haber = 0m
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F002",  // Documento diferente
                    TipoApunte = "0",
                    Debe = 0m,
                    Haber = 500m
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Mantiene ambos (no son pareja)
            Assert.AreEqual(2, resultado.Count);
        }

        [TestMethod]
        public void EliminarPasoACartera_FechasDiferentes_NoElimina()
        {
            // Arrange - Factura y paso a cartera con fechas diferentes
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "1",
                    Debe = 500m,
                    Haber = 0m
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 16),  // Fecha diferente
                    NumeroDocumento = "F001",
                    TipoApunte = "0",
                    Debe = 0m,
                    Haber = 500m
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Mantiene ambos
            Assert.AreEqual(2, resultado.Count);
        }

        [TestMethod]
        public void EliminarPasoACartera_ImportesDiferentes_NoElimina()
        {
            // Arrange - Factura y paso a cartera con importes diferentes
            var movimientos = new List<MovimientoMayorDTO>
            {
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "1",
                    Debe = 500m,
                    Haber = 0m
                },
                new MovimientoMayorDTO
                {
                    Fecha = new DateTime(2025, 1, 15),
                    NumeroDocumento = "F001",
                    TipoApunte = "0",
                    Debe = 0m,
                    Haber = 400m  // Importe diferente
                }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Mantiene ambos
            Assert.AreEqual(2, resultado.Count);
        }

        [TestMethod]
        public void EliminarPasoACartera_MultiplesParesYOtros_EliminaSoloLosPares()
        {
            // Arrange
            var movimientos = new List<MovimientoMayorDTO>
            {
                // Par 1 - Factura + Paso a cartera
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 10), NumeroDocumento = "F001", TipoApunte = "1", Debe = 100m, Haber = 0m },
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 10), NumeroDocumento = "F001", TipoApunte = "0", Debe = 0m, Haber = 100m },
                // Cartera (se mantiene)
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 10), NumeroDocumento = "F001", TipoApunte = "2", Debe = 100m, Haber = 0m },
                // Par 2 - Factura + Paso a cartera
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 15), NumeroDocumento = "F002", TipoApunte = "1", Debe = 200m, Haber = 0m },
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 15), NumeroDocumento = "F002", TipoApunte = "0", Debe = 0m, Haber = 200m },
                // Cartera (se mantiene)
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 15), NumeroDocumento = "F002", TipoApunte = "2", Debe = 200m, Haber = 0m },
                // Cobro (se mantiene)
                new MovimientoMayorDTO { Fecha = new DateTime(2025, 1, 20), NumeroDocumento = "C001", TipoApunte = "3", Debe = 0m, Haber = 150m }
            };

            // Act
            var resultado = CalculadorSaldoMayor.EliminarPasoACartera(movimientos);

            // Assert - Quedan las 2 carteras + 1 cobro = 3 movimientos
            Assert.AreEqual(3, resultado.Count);
            Assert.IsTrue(resultado.All(m => m.TipoApunte == "2" || m.TipoApunte == "3"));
        }

        #endregion
    }
}
