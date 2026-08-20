using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// Tests para Issue #245 - Selector de series de facturación.
    /// Carlos 09/12/25
    /// </summary>
    [TestClass]
    public class SelectorSerieTests
    {
        #region Tests de constantes de series

        [TestMethod]
        public void Series_SerieCursosEsCV()
        {
            // Assert
            Assert.AreEqual("CV", Constantes.Series.SERIE_CURSOS);
        }

        [TestMethod]
        public void Series_SerieNuevaVisionEsNV()
        {
            // Verificar que existe la serie por defecto
            // Assert
            Assert.AreEqual("NV", Constantes.Series.SERIE_POR_DEFECTO);
        }

        #endregion

        #region Tests de EsSerieCursos

        [TestMethod]
        public void EsSerieCursos_SerieCV_DevuelveTrue()
        {
            // Arrange
            string serie = "CV";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsTrue(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieCVConEspacios_DevuelveTrue()
        {
            // Arrange - Serie con espacios (como viene de la BD)
            string serie = "CV ";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsTrue(esSerieCursos, "La comparación debe funcionar aunque la serie tenga espacios");
        }

        [TestMethod]
        public void EsSerieCursos_SerieNV_DevuelveFalse()
        {
            // Arrange
            string serie = "NV";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieNull_DevuelveFalse()
        {
            // Arrange
            string serie = null;

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieVacia_DevuelveFalse()
        {
            // Arrange
            string serie = "";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieUL_DevuelveFalse()
        {
            // Arrange - Unión Láser
            string serie = "UL";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieVC_DevuelveFalse()
        {
            // Arrange - Visnú Cosméticos
            string serie = "VC";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        [TestMethod]
        public void EsSerieCursos_SerieDV_DevuelveFalse()
        {
            // Arrange - Deuda Vencida
            string serie = "DV";

            // Act
            bool esSerieCursos = !string.IsNullOrEmpty(serie) &&
                serie.Trim().Equals(Constantes.Series.SERIE_CURSOS, System.StringComparison.OrdinalIgnoreCase);

            // Assert
            Assert.IsFalse(esSerieCursos);
        }

        #endregion

        #region Tests de inicialización de serie en pedido

        [TestMethod]
        public void InicializarSerie_PedidoConSerieNV_MantieneSerie()
        {
            // Arrange
            var pedido = new PedidoVentaDTO
            {
                serie = "NV"
            };

            // Act - No se modifica la serie
            string serieActual = pedido.serie?.Trim();

            // Assert
            Assert.AreEqual("NV", serieActual);
        }

        [TestMethod]
        public void InicializarSerie_PedidoConSerieConEspacios_TrimFunciona()
        {
            // Arrange - Serie con espacios de la BD
            var pedido = new PedidoVentaDTO
            {
                serie = "NV "
            };

            // Act
            string serieActual = pedido.serie?.Trim();

            // Assert
            Assert.AreEqual("NV", serieActual);
            Assert.AreEqual(2, serieActual.Length, "Después del trim, la serie debe tener 2 caracteres");
        }

        #endregion

        #region Tests de comparación de series (importante para el binding)

        [TestMethod]
        public void ComparacionSeries_MismaSerie_SonIguales()
        {
            // Arrange
            string serie1 = "NV";
            string serie2 = "NV";

            // Act
            bool sonIguales = serie1.Trim() == serie2.Trim();

            // Assert
            Assert.IsTrue(sonIguales);
        }

        [TestMethod]
        public void ComparacionSeries_SerieConYSinEspacios_SonIgualesConTrim()
        {
            // Arrange - Simula el valor de la BD (con espacios) vs el valor del ComboBox
            string serieBD = "NV "; // 3 caracteres
            string serieCombo = "NV"; // 2 caracteres

            // Act
            bool sinTrim = serieBD == serieCombo;
            bool conTrim = serieBD.Trim() == serieCombo.Trim();

            // Assert
            Assert.IsFalse(sinTrim, "Sin trim, las series son diferentes");
            Assert.IsTrue(conTrim, "Con trim, las series son iguales");
        }

        [TestMethod]
        public void ComparacionSeries_DiferentesSeries_SonDiferentes()
        {
            // Arrange
            string serie1 = "NV";
            string serie2 = "CV";

            // Act
            bool sonIguales = serie1.Trim() == serie2.Trim();

            // Assert
            Assert.IsFalse(sonIguales);
        }

        #endregion

        #region Tests del endpoint de series (contra el controller REAL, no una copia local)

        private static List<SerieDTO> SeriesDelEndpoint()
        {
            var resultado = new NestoAPI.Controllers.PedidosVentaController().GetSeries()
                as System.Web.Http.Results.OkNegotiatedContentResult<List<SerieDTO>>;
            Assert.IsNotNull(resultado, "GetSeries debe devolver Ok con la lista");
            return resultado.Content;
        }

        [TestMethod]
        public void GetSeries_OfreceSoloLasSeriesVivas()
        {
            // Verifactu #39 (20/08/26): VC nunca existió en la BD y DV deja de usarse (sus
            // abonos van por RV). El combo de series para pedidos NUEVOS no debe ofrecerlas;
            // los pedidos/facturas DV históricos no se ven afectados (solo gobierna la elección).
            List<SerieDTO> series = SeriesDelEndpoint();

            CollectionAssert.AreEquivalent(new[] { "NV", "CV", "UL" }, series.Select(s => s.Codigo).ToArray());
            Assert.IsFalse(series.Any(s => s.Codigo == "VC" || s.Codigo == "DV"),
                "VC y DV no se ofrecen para pedidos nuevos");
        }

        [TestMethod]
        public void GetSeries_TodasTienenCodigoYNombre()
        {
            foreach (SerieDTO serie in SeriesDelEndpoint())
            {
                Assert.IsFalse(string.IsNullOrEmpty(serie.Codigo), "Código no debe estar vacío");
                Assert.IsFalse(string.IsNullOrEmpty(serie.Nombre), $"Nombre no debe estar vacío para {serie.Codigo}");
            }
        }

        #endregion

        #region LeerSerie: series históricas sin clase propia

        [TestMethod]
        public void LeerSerie_SerieHistoricaSinClase_UsaLaPlantillaPorDefecto()
        {
            // Verifactu #39 (20/08/26): reimprimir una factura DV (serie retirada, nunca tuvo
            // clase SerieDV) reventaba con NullReference en Activator.CreateInstance(null).
            // El duplicado de una serie sin clase sale con la plantilla por defecto (NV): la
            // clase de serie solo gobierna presentación (logo, notas, ruta del informe).
            var serie = NestoAPI.Infraestructure.Facturas.GestorFacturas.LeerSerie("DV");

            Assert.IsInstanceOfType(serie, typeof(NestoAPI.Models.Facturas.SeriesFactura.SerieNV));
        }

        [TestMethod]
        public void LeerSerie_SerieConClase_DevuelveSuClase()
        {
            Assert.IsInstanceOfType(NestoAPI.Infraestructure.Facturas.GestorFacturas.LeerSerie("CV "),
                typeof(NestoAPI.Models.Facturas.SeriesFactura.SerieCV), "Con padding de la BD incluido");
            Assert.IsInstanceOfType(NestoAPI.Infraestructure.Facturas.GestorFacturas.LeerSerie("RV"),
                typeof(NestoAPI.Models.Facturas.SeriesFactura.SerieRV));
        }

        #endregion

        #region Tests de inicialización de valores diferentes (VARIOS)

        [TestMethod]
        public void InicializarFormaVenta_TodasIguales_DevuelveLaFormaVenta()
        {
            // Arrange
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { formaVenta = "01" },
                new LineaPedidoVentaDTO { formaVenta = "01" },
                new LineaPedidoVentaDTO { formaVenta = "01" }
            };

            // Act
            var formasDistintas = lineas
                .Where(l => !string.IsNullOrWhiteSpace(l.formaVenta))
                .Select(l => l.formaVenta.Trim())
                .Distinct()
                .ToList();

            string resultado = formasDistintas.Count == 1 ? formasDistintas.First() : "VARIOS";

            // Assert
            Assert.AreEqual("01", resultado);
        }

        [TestMethod]
        public void InicializarFormaVenta_DiferentesFormas_DevuelveVARIOS()
        {
            // Arrange
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { formaVenta = "01" },
                new LineaPedidoVentaDTO { formaVenta = "02" },
                new LineaPedidoVentaDTO { formaVenta = "01" }
            };

            // Act
            var formasDistintas = lineas
                .Where(l => !string.IsNullOrWhiteSpace(l.formaVenta))
                .Select(l => l.formaVenta.Trim())
                .Distinct()
                .ToList();

            string resultado = formasDistintas.Count == 1 ? formasDistintas.First() : "VARIOS";

            // Assert
            Assert.AreEqual("VARIOS", resultado);
        }

        [TestMethod]
        public void InicializarAlmacen_TodosIguales_DevuelveElAlmacen()
        {
            // Arrange
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true },
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true },
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true }
            };

            // Act
            var almacenesDistintos = lineas
                .Where(l => l.EsFicticio && !string.IsNullOrWhiteSpace(l.almacen))
                .Select(l => l.almacen.Trim())
                .Distinct()
                .ToList();

            string resultado = almacenesDistintos.Count == 1 ? almacenesDistintos.First() : "VARIOS";

            // Assert
            Assert.AreEqual("ALG", resultado);
        }

        [TestMethod]
        public void InicializarAlmacen_DiferentesAlmacenes_DevuelveVARIOS()
        {
            // Arrange
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true },
                new LineaPedidoVentaDTO { almacen = "REI", EsFicticio = true },
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true }
            };

            // Act
            var almacenesDistintos = lineas
                .Where(l => l.EsFicticio && !string.IsNullOrWhiteSpace(l.almacen))
                .Select(l => l.almacen.Trim())
                .Distinct()
                .ToList();

            string resultado = almacenesDistintos.Count == 1 ? almacenesDistintos.First() : "VARIOS";

            // Assert
            Assert.AreEqual("VARIOS", resultado);
        }

        [TestMethod]
        public void InicializarAlmacen_SoloConsideraLineasFicticias()
        {
            // Arrange - Mezcla de líneas ficticias y no ficticias
            var lineas = new List<LineaPedidoVentaDTO>
            {
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true },
                new LineaPedidoVentaDTO { almacen = "REI", EsFicticio = false }, // Esta no cuenta
                new LineaPedidoVentaDTO { almacen = "ALG", EsFicticio = true }
            };

            // Act
            var almacenesDistintos = lineas
                .Where(l => l.EsFicticio && !string.IsNullOrWhiteSpace(l.almacen))
                .Select(l => l.almacen.Trim())
                .Distinct()
                .ToList();

            string resultado = almacenesDistintos.Count == 1 ? almacenesDistintos.First() : "VARIOS";

            // Assert - Solo hay un almacén entre las líneas ficticias
            Assert.AreEqual("ALG", resultado);
        }

        #endregion
    }
}
