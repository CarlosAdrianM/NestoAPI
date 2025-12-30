using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Comisiones;
using NestoAPI.Models.Comisiones.Estetica;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class ReescalarTramosTests
    {
        #region Tests de validación de estructura de tramos

        [TestMethod]
        public void ReescalarTramos_PrimerTramo_DesdeEsCero()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M);

            // Assert
            Assert.AreEqual(0M, resultado.First().Desde);
        }

        [TestMethod]
        public void ReescalarTramos_UltimoTramo_HastaEsDecimalMaxValue()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M);

            // Assert
            Assert.AreEqual(decimal.MaxValue, resultado.Last().Hasta);
        }

        [TestMethod]
        public void ReescalarTramos_Continuidad_DesdeCadaTramoEsHastaAnteriorMasUnCentimo()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Assert
            for (int i = 1; i < resultado.Count; i++)
            {
                var esperado = resultado[i - 1].Hasta + 0.01M;
                Assert.AreEqual(esperado, resultado[i].Desde,
                    $"Tramo {i}: Desde ({resultado[i].Desde}) debería ser {esperado}");
            }
        }

        [TestMethod]
        public void ReescalarTramos_SinSolapamiento_NingunValorPerteneceADosTramos()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Act & Assert - Verificar que los límites no se solapan
            for (int i = 0; i < resultado.Count - 1; i++)
            {
                Assert.IsTrue(resultado[i].Hasta < resultado[i + 1].Desde,
                    $"Tramo {i} (Hasta={resultado[i].Hasta}) se solapa con Tramo {i + 1} (Desde={resultado[i + 1].Desde})");
            }
        }

        [TestMethod]
        public void ReescalarTramos_SinHuecos_DiferenciaEntreHastaYSiguienteDesdeEsUnCentimo()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Act & Assert
            for (int i = 0; i < resultado.Count - 1; i++)
            {
                var diferencia = resultado[i + 1].Desde - resultado[i].Hasta;
                Assert.AreEqual(0.01M, diferencia,
                    $"Hueco entre tramo {i} y {i + 1}: diferencia es {diferencia}, debería ser 0.01");
            }
        }

        #endregion

        #region Tests de reescalado

        [TestMethod]
        public void ReescalarTramos_FactorUno_HastaNoCambia()
        {
            // Arrange
            var tramosBase = CrearTramosEjemplo();
            var hastaOriginal = tramosBase.First().Hasta;

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.0M);

            // Assert
            Assert.AreEqual(hastaOriginal, resultado.First().Hasta);
        }

        [TestMethod]
        public void ReescalarTramos_Factor1027_HastaSeMultiplicaCorrectamente()
        {
            // Arrange
            var tramosBase = new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 100M, Tipo = 0.01M, TipoExtra = 0.001M },
                new TramoComision { Desde = 100.01M, Hasta = decimal.MaxValue, Tipo = 0.02M, TipoExtra = 0.002M }
            };

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Assert
            // 100 * 1.027 = 102.70
            Assert.AreEqual(102.70M, resultado[0].Hasta);
        }

        [TestMethod]
        public void ReescalarTramos_Redondeo_HastaSeRedondeaADosDecimales()
        {
            // Arrange
            var tramosBase = new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 33.33M, Tipo = 0.01M, TipoExtra = 0.001M },
                new TramoComision { Desde = 33.34M, Hasta = decimal.MaxValue, Tipo = 0.02M, TipoExtra = 0.002M }
            };

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Assert
            // 33.33 * 1.027 = 34.22991 -> redondeado a 34.23
            Assert.AreEqual(34.23M, resultado[0].Hasta);
        }

        [TestMethod]
        public void ReescalarTramos_DecimalMaxValue_NoSeMultiplica()
        {
            // Arrange
            var tramosBase = new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 100M, Tipo = 0.01M, TipoExtra = 0.001M },
                new TramoComision { Desde = 100.01M, Hasta = decimal.MaxValue, Tipo = 0.02M, TipoExtra = 0.002M }
            };

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M);

            // Assert
            Assert.AreEqual(decimal.MaxValue, resultado.Last().Hasta);
        }

        #endregion

        #region Tests de preservación de Tipo y TipoExtra

        [TestMethod]
        public void ReescalarTramos_Tipo_SePreserva()
        {
            // Arrange
            var tramosBase = new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 100M, Tipo = 0.015M, TipoExtra = 0.001M },
                new TramoComision { Desde = 100.01M, Hasta = decimal.MaxValue, Tipo = 0.025M, TipoExtra = 0.002M }
            };

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Assert
            Assert.AreEqual(0.015M, resultado[0].Tipo);
            Assert.AreEqual(0.025M, resultado[1].Tipo);
        }

        [TestMethod]
        public void ReescalarTramos_TipoExtra_SePreserva()
        {
            // Arrange
            var tramosBase = new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 100M, Tipo = 0.01M, TipoExtra = 0.0015M },
                new TramoComision { Desde = 100.01M, Hasta = decimal.MaxValue, Tipo = 0.02M, TipoExtra = 0.0025M }
            };

            // Act
            var resultado = ComisionesAnualesBase.ReescalarTramos(tramosBase, 1.027M).ToList();

            // Assert
            Assert.AreEqual(0.0015M, resultado[0].TipoExtra);
            Assert.AreEqual(0.0025M, resultado[1].TipoExtra);
        }

        #endregion

        #region Tests de ComisionesAnualesTelefono2025 (tramos base)

        [TestMethod]
        public void ComisionesAnualesTelefono2025_LeerTramosBase_PrimerTramoDesdeEsCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2025(servicio);

            // Act
            var tramos = sut.LeerTramosBase();

            // Assert
            Assert.AreEqual(0M, tramos.First().Desde);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2025_LeerTramosBase_UltimoTramoHastaEsMaxValue()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2025(servicio);

            // Act
            var tramos = sut.LeerTramosBase();

            // Assert
            Assert.AreEqual(decimal.MaxValue, tramos.Last().Hasta);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2025_LeerTramosBase_TramosContiguosSinHuecos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2025(servicio);

            // Act
            var tramos = sut.LeerTramosBase().ToList();

            // Assert
            for (int i = 1; i < tramos.Count; i++)
            {
                var diferencia = tramos[i].Desde - tramos[i - 1].Hasta;
                Assert.AreEqual(0.01M, diferencia,
                    $"Hueco entre tramo {i - 1} (Hasta={tramos[i - 1].Hasta}) y tramo {i} (Desde={tramos[i].Desde})");
            }
        }

        #endregion

        #region Tests de ComisionesAnualesTelefono2026

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_PrimerTramoDesdeEsCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD");

            // Assert
            Assert.AreEqual(0M, tramos.First().Desde);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_UltimoTramoHastaEsMaxValue()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD");

            // Assert
            Assert.AreEqual(decimal.MaxValue, tramos.Last().Hasta);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_TramosContiguosSinHuecos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD").ToList();

            // Assert
            for (int i = 1; i < tramos.Count; i++)
            {
                var diferencia = tramos[i].Desde - tramos[i - 1].Hasta;
                Assert.AreEqual(0.01M, diferencia,
                    $"Hueco entre tramo {i - 1} (Hasta={tramos[i - 1].Hasta}) y tramo {i} (Desde={tramos[i].Desde})");
            }
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_SinSolapamientoEntreTramos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesTelefono2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD").ToList();

            // Assert
            for (int i = 0; i < tramos.Count - 1; i++)
            {
                Assert.IsTrue(tramos[i].Hasta < tramos[i + 1].Desde,
                    $"Tramo {i} (Hasta={tramos[i].Hasta}) >= Tramo {i + 1} (Desde={tramos[i + 1].Desde})");
            }
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_PrimerTramoHastaIncrementado27PorCiento()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesTelefono2025(servicio);
            var sut2026 = new ComisionesAnualesTelefono2026(servicio);

            var hasta2025 = sut2025.LeerTramosComisionAnno("VD").First().Hasta;
            var esperado = decimal.Round(hasta2025 * 1.027M, 2);

            // Act
            var hasta2026 = sut2026.LeerTramosComisionAnno("VD").First().Hasta;

            // Assert
            Assert.AreEqual(esperado, hasta2026);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_MismoNumeroDeTramos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesTelefono2025(servicio);
            var sut2026 = new ComisionesAnualesTelefono2026(servicio);

            // Act
            var count2025 = sut2025.LeerTramosComisionAnno("VD").Count;
            var count2026 = sut2026.LeerTramosComisionAnno("VD").Count;

            // Assert
            Assert.AreEqual(count2025, count2026);
        }

        [TestMethod]
        public void ComisionesAnualesTelefono2026_LeerTramosComisionAnno_TiposPreservados()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesTelefono2025(servicio);
            var sut2026 = new ComisionesAnualesTelefono2026(servicio);

            var tramos2025 = sut2025.LeerTramosComisionAnno("VD").ToList();
            var tramos2026 = sut2026.LeerTramosComisionAnno("VD").ToList();

            // Act & Assert
            for (int i = 0; i < tramos2025.Count; i++)
            {
                Assert.AreEqual(tramos2025[i].Tipo, tramos2026[i].Tipo,
                    $"Tramo {i}: Tipo difiere");
                Assert.AreEqual(tramos2025[i].TipoExtra, tramos2026[i].TipoExtra,
                    $"Tramo {i}: TipoExtra difiere");
            }
        }

        #endregion

        #region Tests de ComisionesAnualesMinivendedores2026

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_PrimerTramoDesdeEsCero()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesMinivendedores2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD");

            // Assert
            Assert.AreEqual(0M, tramos.First().Desde);
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_UltimoTramoHastaEsMaxValue()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesMinivendedores2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD");

            // Assert
            Assert.AreEqual(decimal.MaxValue, tramos.Last().Hasta);
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_TramosContiguosSinHuecos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesMinivendedores2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD").ToList();

            // Assert
            for (int i = 1; i < tramos.Count; i++)
            {
                var diferencia = tramos[i].Desde - tramos[i - 1].Hasta;
                Assert.AreEqual(0.01M, diferencia,
                    $"Hueco entre tramo {i - 1} (Hasta={tramos[i - 1].Hasta}) y tramo {i} (Desde={tramos[i].Desde})");
            }
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_SinSolapamientoEntreTramos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut = new ComisionesAnualesMinivendedores2026(servicio);

            // Act
            var tramos = sut.LeerTramosComisionAnno("VD").ToList();

            // Assert
            for (int i = 0; i < tramos.Count - 1; i++)
            {
                Assert.IsTrue(tramos[i].Hasta < tramos[i + 1].Desde,
                    $"Tramo {i} (Hasta={tramos[i].Hasta}) >= Tramo {i + 1} (Desde={tramos[i + 1].Desde})");
            }
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_PrimerTramoHastaIncrementado27PorCiento()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesMinivendedores2025(servicio);
            var sut2026 = new ComisionesAnualesMinivendedores2026(servicio);

            var hasta2025 = sut2025.LeerTramosComisionAnno("VD").First().Hasta;
            var esperado = decimal.Round(hasta2025 * 1.027M, 2);

            // Act
            var hasta2026 = sut2026.LeerTramosComisionAnno("VD").First().Hasta;

            // Assert
            Assert.AreEqual(esperado, hasta2026);
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_MismoNumeroDeTramos()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesMinivendedores2025(servicio);
            var sut2026 = new ComisionesAnualesMinivendedores2026(servicio);

            // Act
            var count2025 = sut2025.LeerTramosComisionAnno("VD").Count;
            var count2026 = sut2026.LeerTramosComisionAnno("VD").Count;

            // Assert
            Assert.AreEqual(count2025, count2026);
        }

        [TestMethod]
        public void ComisionesAnualesMinivendedores2026_LeerTramosComisionAnno_TiposPreservados()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var sut2025 = new ComisionesAnualesMinivendedores2025(servicio);
            var sut2026 = new ComisionesAnualesMinivendedores2026(servicio);

            var tramos2025 = sut2025.LeerTramosComisionAnno("VD").ToList();
            var tramos2026 = sut2026.LeerTramosComisionAnno("VD").ToList();

            // Act & Assert
            for (int i = 0; i < tramos2025.Count; i++)
            {
                Assert.AreEqual(tramos2025[i].Tipo, tramos2026[i].Tipo,
                    $"Tramo {i}: Tipo difiere");
                Assert.AreEqual(tramos2025[i].TipoExtra, tramos2026[i].TipoExtra,
                    $"Tramo {i}: TipoExtra difiere");
            }
        }

        #endregion

        #region Helpers

        private ICollection<TramoComision> CrearTramosEjemplo()
        {
            return new List<TramoComision>
            {
                new TramoComision { Desde = 0M, Hasta = 1000M, Tipo = 0.01M, TipoExtra = 0.001M },
                new TramoComision { Desde = 1000.01M, Hasta = 5000M, Tipo = 0.02M, TipoExtra = 0.002M },
                new TramoComision { Desde = 5000.01M, Hasta = 10000M, Tipo = 0.03M, TipoExtra = 0.003M },
                new TramoComision { Desde = 10000.01M, Hasta = decimal.MaxValue, Tipo = 0.04M, TipoExtra = 0.004M }
            };
        }

        #endregion
    }
}
