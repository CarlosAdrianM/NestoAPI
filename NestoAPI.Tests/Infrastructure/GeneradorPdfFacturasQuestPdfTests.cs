using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests para GeneradorPdfFacturasQuestPdf (Issue #83)
    /// Verifica la correcta selección de formato según las propiedades de la factura
    /// </summary>
    [TestClass]
    public class GeneradorPdfFacturasQuestPdfTests
    {
        private GeneradorPdfFacturasQuestPdf _generador;

        [TestInitialize]
        public void Setup()
        {
            _generador = new GeneradorPdfFacturasQuestPdf();
        }

        #region Tests de validación de configuración

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void GenerarPdf_SinUrlLogo_YSinFormatoTicket_LanzaExcepcion()
        {
            // Arrange: Factura sin UrlLogo y sin UsaFormatoTicket
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = false;
            factura.Serie = "XX"; // Serie ficticia

            // Act & Assert: Debe lanzar excepción
            _generador.GenerarPdf(new List<Factura> { factura });
        }

        [TestMethod]
        public void GenerarPdf_SinUrlLogo_PeroConFormatoTicket_NoLanzaExcepcion()
        {
            // Arrange: Factura sin UrlLogo pero con UsaFormatoTicket=true (como GB)
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = true;
            factura.Serie = "GB";

            // Act: No debe lanzar excepción
            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado, "Debe generar PDF para formato ticket sin logo");
        }

        [TestMethod]
        public void GenerarPdf_ConUrlLogo_YSinFormatoTicket_NoLanzaExcepcion()
        {
            // Arrange: Factura con UrlLogo (formato estándar)
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "https://example.com/logo.png";
            factura.UsaFormatoTicket = false;
            factura.Serie = "NV";

            // Act: No debe lanzar excepción (aunque el logo no exista, la validación pasa)
            // Nota: El logo fallará al descargar pero no debería lanzar excepción
            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado, "Debe generar PDF para formato estándar con logo");
        }

        [TestMethod]
        public void GenerarPdf_ConPapelMembrete_NoRequiereUrlLogo()
        {
            // Arrange: Factura sin UrlLogo pero con papel membrete
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = false;
            factura.Serie = "NV";

            // Act: Con papelConMembrete=true, no debe requerir logo
            var resultado = _generador.GenerarPdf(new List<Factura> { factura }, papelConMembrete: true);

            // Assert
            Assert.IsNotNull(resultado, "Con papel membrete no debe requerir logo");
        }

        #endregion

        #region Tests de contenido generado

        [TestMethod]
        public void GenerarPdf_FormatoTicket_GeneraPdfValido()
        {
            // Arrange
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = true;
            factura.Serie = "GB";
            factura.TipoDocumento = "TICKET";

            // Act
            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            // Verificar cabecera PDF
            Assert.AreEqual(0x25, bytes[0], "Debe empezar con %");
            Assert.AreEqual(0x50, bytes[1], "Segundo byte debe ser P");
            Assert.AreEqual(0x44, bytes[2], "Tercer byte debe ser D");
            Assert.AreEqual(0x46, bytes[3], "Cuarto byte debe ser F");
        }

        [TestMethod]
        public void GenerarPdf_FormatoEstandar_GeneraPdfValido()
        {
            // Arrange
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png";
            factura.UsaFormatoTicket = false;
            factura.Serie = "NV";
            factura.TipoDocumento = "FACTURA";

            // Act
            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            // Verificar cabecera PDF
            Assert.AreEqual(0x25, bytes[0], "Debe empezar con %PDF");
        }

        #endregion

        #region Tests de regresión - evitar hardcodeo de series

        [TestMethod]
        public void GenerarPdf_NoDepende_DeNombreDeSerie()
        {
            // Este test verifica que la lógica NO depende del nombre de la serie (ej: "GB")
            // sino de la propiedad UsaFormatoTicket

            // Arrange: Factura con serie "XX" pero UsaFormatoTicket=true
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = true;
            factura.Serie = "XX"; // Serie ficticia, no "GB"

            // Act: Debe funcionar porque UsaFormatoTicket=true
            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado,
                "El formato ticket debe funcionar con cualquier serie si UsaFormatoTicket=true");
        }

        [TestMethod]
        public void GenerarPdf_SerieGB_SinUsaFormatoTicket_Falla()
        {
            // Este test verifica que tener serie="GB" NO es suficiente
            // La propiedad UsaFormatoTicket es la que determina el formato

            // Arrange: Factura con serie "GB" pero UsaFormatoTicket=false y sin logo
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = false; // Aunque sea GB, si esto es false, debe fallar
            factura.Serie = "GB";

            // Act & Assert: Debe fallar porque UsaFormatoTicket=false y no hay logo
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                _generador.GenerarPdf(new List<Factura> { factura });
            }, "Debe fallar si UsaFormatoTicket=false aunque la serie sea GB");
        }

        #endregion

        #region Helpers

        private Factura CrearFacturaBasica()
        {
            return new Factura
            {
                Cliente = "TEST001",
                NumeroFactura = "NV000001",
                Fecha = DateTime.Today,
                ImporteTotal = 100m,
                Serie = "NV",
                TipoDocumento = "FACTURA",
                Direcciones = new List<DireccionFactura>
                {
                    new DireccionFactura
                    {
                        Tipo = "Empresa",
                        Nombre = "Empresa Test",
                        Direccion = "Calle Test 1",
                        PoblacionCompleta = "28000 Madrid"
                    },
                    new DireccionFactura
                    {
                        Tipo = "Fiscal",
                        Nombre = "Cliente Test",
                        Direccion = "Calle Cliente 1",
                        PoblacionCompleta = "28001 Madrid"
                    },
                    new DireccionFactura
                    {
                        Tipo = "Entrega",
                        Nombre = "Cliente Test",
                        Direccion = "Calle Entrega 1",
                        PoblacionCompleta = "28002 Madrid"
                    }
                },
                Lineas = new List<LineaFactura>
                {
                    new LineaFactura
                    {
                        Producto = "PROD01",
                        DescripcionCompleta = "Producto de prueba",
                        Cantidad = 1,
                        PrecioUnitario = 100m,
                        Descuento = 0,
                        Importe = 100m,
                        TextoAlbaran = "Albarán 1"
                    }
                },
                Totales = new List<TotalFactura>
                {
                    new TotalFactura
                    {
                        BaseImponible = 100m,
                        PorcentajeIVA = 0.21m,
                        ImporteIVA = 21m,
                        PorcentajeRecargoEquivalencia = 0,
                        ImporteRecargoEquivalencia = 0
                    }
                },
                Vencimientos = new List<VencimientoFactura>(),
                Vendedores = new List<VendedorFactura>(),
                NotasAlPie = new List<NotaFactura>()
            };
        }

        #endregion
    }
}
