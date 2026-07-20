using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models.Facturas;
using QuestPDF.Helpers;
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

        #region Sello Madrid Excelente (NestoAPI#244)

        [TestMethod]
        public void SelloMadridExcelente_RecursoEmbebido_ExisteYEsPng()
        {
            // Guarda el recurso embebido y su LogicalName: si alguien renombra/quita el PNG o cambia el
            // nombre lógico, el sello dejaría de salir SILENCIOSAMENTE en producción. Este test lo caza.
            var assembly = typeof(GeneradorPdfFacturasQuestPdf).Assembly;
            using (var stream = assembly.GetManifestResourceStream("NestoAPI.Resources.SelloMadridExcelenteReducido.png"))
            {
                Assert.IsNotNull(stream, "El sello Madrid Excelente debe estar embebido con ese LogicalName.");
                var firma = new byte[8];
                int leidos = stream.Read(firma, 0, 8);
                Assert.AreEqual(8, leidos, "El recurso del sello está vacío o truncado.");
                // Firma de fichero PNG: 89 'P' 'N' 'G' 0D 0A 1A 0A
                Assert.AreEqual(0x89, firma[0]);
                Assert.AreEqual((byte)'P', firma[1]);
                Assert.AreEqual((byte)'N', firma[2]);
                Assert.AreEqual((byte)'G', firma[3]);
            }
        }

        #endregion

        #region QR Verifactu (#35)

        // PNG válido de 1x1 px (para probar el QR sin depender de Verifacti)
        private const string PNG_1X1_BASE64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        [TestMethod]
        public void DecodificarQrVerifactu_Base64Valido_DevuelveLosBytes()
        {
            byte[] bytes = GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu(PNG_1X1_BASE64);

            Assert.IsNotNull(bytes);
            Assert.AreEqual(0x89, bytes[0]); // firma PNG
        }

        [TestMethod]
        public void DecodificarQrVerifactu_DataUri_DevuelveLosBytes()
        {
            byte[] bytes = GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu("data:image/png;base64," + PNG_1X1_BASE64);

            Assert.IsNotNull(bytes);
        }

        [TestMethod]
        public void DecodificarQrVerifactu_DatoCorrupto_DevuelveNullSinLanzar()
        {
            // Un dato corrupto en BD nunca puede tumbar la impresión de la factura
            Assert.IsNull(GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu(null));
            Assert.IsNull(GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu("   "));
            Assert.IsNull(GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu("esto-no-es-base64!!"));
            // Base64 válido pero que NO es una imagen
            Assert.IsNull(GeneradorPdfFacturasQuestPdf.DecodificarQrVerifactu(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })));
        }

        [TestMethod]
        public void GenerarPdf_ConQrVerifactu_GeneraSinErrores()
        {
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "http://localhost/logo-inexistente.png"; // la descarga falla en silencio
            factura.VerifactuQrBase64 = PNG_1X1_BASE64;

            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            Assert.IsNotNull(resultado, "La factura con QR Verifactu debe generarse sin errores");
        }

        [TestMethod]
        public void GenerarPdf_ConQrVerifactuCorrupto_GeneraSinQrYSinErrores()
        {
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "http://localhost/logo-inexistente.png"; // la descarga falla en silencio
            factura.VerifactuQrBase64 = "dato-corrupto-en-bd";

            var resultado = _generador.GenerarPdf(new List<Factura> { factura });

            Assert.IsNotNull(resultado, "Un QR corrupto no puede tumbar la impresión: se omite el QR");
        }

        #endregion

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
        public void GenerarPdf_TicketGB_ConComentariosPedido_LosIncluye()
        {
            // Issue #212: el ticket GB debe mostrar los comentarios (ruta) del pedido. Sin extractor
            // de texto PDF, verificamos que la rama nueva genera un PDF válido y que añadir un
            // comentario largo aumenta el tamaño del PDF (la cadena se incrusta en el contenido).
            const string comentarioLargo =
                "RUTA: REPARTO MADRID CENTRO - ZONA 3 - LLAMAR ANTES DE ENTREGAR - HORARIO DE MAÑANA - " +
                "PUERTA TRASERA - PREGUNTAR POR JUAN - DEJAR EN CONSERJERÍA SI NO HAY NADIE - GRACIAS";

            var sinComentario = CrearFacturaBasica();
            sinComentario.UrlLogo = null;
            sinComentario.UsaFormatoTicket = true;
            sinComentario.Serie = "GB";
            sinComentario.TipoDocumento = "TICKET";
            sinComentario.Comentarios = null;

            var conComentario = CrearFacturaBasica();
            conComentario.UrlLogo = null;
            conComentario.UsaFormatoTicket = true;
            conComentario.Serie = "GB";
            conComentario.TipoDocumento = "TICKET";
            conComentario.Comentarios = comentarioLargo;

            var pdfSin = _generador.GenerarPdf(new List<Factura> { sinComentario }).ReadAsByteArrayAsync().Result;
            var pdfCon = _generador.GenerarPdf(new List<Factura> { conComentario }).ReadAsByteArrayAsync().Result;

            Assert.AreEqual(0x25, pdfCon[0], "El PDF con comentarios debe ser válido (%PDF)");
            Assert.IsTrue(pdfCon.Length > pdfSin.Length,
                "El ticket GB con comentarios del pedido debe ocupar más que sin ellos (el texto se incrusta en el PDF)");
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
                        CodigoPostal = "28000",
                        Poblacion = "Madrid",
                        Provincia = "Madrid"
                    },
                    new DireccionFactura
                    {
                        Tipo = "Fiscal",
                        Nombre = "Cliente Test",
                        Direccion = "Calle Cliente 1",
                        CodigoPostal = "28001",
                        Poblacion = "Madrid",
                        Provincia = "Madrid"
                    },
                    new DireccionFactura
                    {
                        Tipo = "Entrega",
                        Nombre = "Cliente Test",
                        Direccion = "Calle Entrega 1",
                        CodigoPostal = "28002",
                        Poblacion = "Madrid",
                        Provincia = "Madrid"
                    }
                },
                Lineas = new List<LineaFactura>
                {
                    new LineaFactura
                    {
                        Producto = "PROD01",
                        Descripcion = "Producto de prueba",
                        Cantidad = 1,
                        PrecioUnitario = 100m,
                        Descuento = 0,
                        Importe = 100m,
                        Albaran = 1,
                        FechaAlbaran = DateTime.Today,
                        Pedido = 100
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

        #region Issue #111 - Mostrar imágenes de productos

        [TestMethod]
        public void GenerarPdf_MostrarImagenesFalse_GeneraPdfSinImagenes()
        {
            // Arrange
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "https://example.com/logo.png";
            factura.UsaFormatoTicket = false;

            // Act: Sin imágenes (comportamiento por defecto)
            var resultado = _generador.GenerarPdf(new List<Factura> { factura }, mostrarImagenes: false);

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        [TestMethod]
        public void GenerarPdf_MostrarImagenesTrue_SinUrlsImagen_GeneraPdfConColumnaVacia()
        {
            // Arrange: Imágenes activadas pero las líneas no tienen UrlImagen
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "https://example.com/logo.png";
            factura.UsaFormatoTicket = false;
            factura.MostrarImagenes = true;

            // Act
            var resultado = _generador.GenerarPdf(new List<Factura> { factura }, mostrarImagenes: true);

            // Assert: Debe generar PDF válido aunque no haya imágenes de productos
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
            Assert.AreEqual(0x25, bytes[0], "Debe ser PDF válido");
        }

        [TestMethod]
        public void GenerarPdf_MostrarImagenesTrue_ConDescuentos_GeneraPdfValido()
        {
            // Arrange: Imágenes activadas Y descuentos visibles
            var factura = CrearFacturaBasica();
            factura.UrlLogo = "https://example.com/logo.png";
            factura.UsaFormatoTicket = false;
            factura.MostrarImagenes = true;
            factura.Lineas[0].Descuento = 0.10m; // 10% para activar columna descuento

            // Act
            var resultado = _generador.GenerarPdf(new List<Factura> { factura }, mostrarImagenes: true);

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        [TestMethod]
        public void GenerarPdf_FormatoTicket_IgnoraMostrarImagenes()
        {
            // Arrange: Tickets no deben mostrar imágenes
            var factura = CrearFacturaBasica();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = true;
            factura.MostrarImagenes = true; // Se ignora en tickets

            // Act
            var resultado = _generador.GenerarPdf(new List<Factura> { factura }, mostrarImagenes: true);

            // Assert: PDF válido, sin error
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        [TestMethod]
        public void LineaFactura_UrlImagen_PuedeSerNull()
        {
            // Arrange & Act
            var linea = new LineaFactura
            {
                Producto = "PROD01",
                Descripcion = "Test",
                Cantidad = 1,
                PrecioUnitario = 10m,
                Importe = 10m,
                UrlImagen = null
            };

            // Assert
            Assert.IsNull(linea.UrlImagen);
        }

        [TestMethod]
        public void Factura_MostrarImagenes_DefaultFalse()
        {
            // Arrange & Act
            var factura = new Factura();

            // Assert
            Assert.IsFalse(factura.MostrarImagenes);
        }

        #endregion

        #region Tests de color de línea según estado/stock (Issue #222)

        // Replica la matriz del informe legacy Factura.rdlc:
        // | Estado >= 2 (albarán/factura) o Estado < -1 (presupuesto) | Negro |
        // | Estado = -1 (PENDIENTE)                                   | Rojo  |
        // | Estado = 1 (EN_CURSO) y Picking = 0                       | Azul  |
        // | Estado = 1 (EN_CURSO) y Picking <> 0                      | Verde |

        [DataTestMethod]
        [DataRow(2, 0)]    // ALBARAN
        [DataRow(2, 123)]  // ALBARAN con picking asignado
        [DataRow(4, 0)]    // FACTURA
        [DataRow(-3, 0)]   // PRESUPUESTO (Estado < -1)
        public void ColorLinea_AlbaranFacturaOPresupuesto_DevuelveNegro(int estado, int picking)
        {
            Assert.AreEqual(Colors.Black, GeneradorPdfFacturasQuestPdf.ColorLinea(estado, picking));
        }

        [TestMethod]
        public void ColorLinea_Pendiente_DevuelveRojo()
        {
            // Estado -1 (PENDIENTE): sin stock
            Assert.AreEqual(Colors.Red.Medium, GeneradorPdfFacturasQuestPdf.ColorLinea(-1, 0));
        }

        [TestMethod]
        public void ColorLinea_EnCursoSinPicking_DevuelveAzul()
        {
            // Estado 1 (EN_CURSO) y Picking 0: stock sin comprobar
            Assert.AreEqual(Colors.Blue.Medium, GeneradorPdfFacturasQuestPdf.ColorLinea(1, 0));
        }

        [TestMethod]
        public void ColorLinea_EnCursoConPicking_DevuelveVerde()
        {
            // Estado 1 (EN_CURSO) y Picking <> 0: línea servible (hay stock)
            Assert.AreEqual(Colors.Green.Medium, GeneradorPdfFacturasQuestPdf.ColorLinea(1, 5));
        }

        #endregion

        #region ComponerTextoDireccionEntrega (NestoAPI#196)

        [TestMethod]
        public void ComponerTextoDireccionEntrega_DireccionCompleta_DevuelveTextoConNombreDireccionYPoblacion()
        {
            var direccion = new DireccionFactura
            {
                Tipo = "Entrega",
                Nombre = "ACME S.L.",
                Direccion = "Calle X 12",
                CodigoPostal = "28001",
                Poblacion = "Madrid",
                Provincia = "Madrid"
            };

            string texto = GeneradorPdfFacturasQuestPdf.ComponerTextoDireccionEntrega(direccion);

            Assert.AreEqual("Entrega: ACME S.L. - Calle X 12 - 28001 Madrid (Madrid)", texto);
        }

        [TestMethod]
        public void ComponerTextoDireccionEntrega_Null_DevuelveVacio()
        {
            Assert.AreEqual(string.Empty, GeneradorPdfFacturasQuestPdf.ComponerTextoDireccionEntrega(null));
        }

        // NestoAPI#243: separación del descuento comercial y el pronto pago para el PDF.

        [TestMethod]
        public void DescuentoComercial_QuitaElProntoPago_DelDescuentoTotal()
        {
            // 15 % comercial + 3 % PP => total 17,55 %. La línea debe mostrar el 15 % comercial.
            decimal comercial = GeneradorPdfFacturasQuestPdf.DescuentoComercial(0.1755m, 0.03m);
            Assert.AreEqual(0.15m, Math.Round(comercial, 4));
        }

        [TestMethod]
        public void DescuentoComercial_SinProntoPago_DevuelveElTotal()
        {
            Assert.AreEqual(0.1755m, GeneradorPdfFacturasQuestPdf.DescuentoComercial(0.1755m, 0m));
        }

        [TestMethod]
        public void ImporteAntesProntoPago_DeshaceElProntoPago()
        {
            // Base imponible 82,45 (= 85,00 con un 3 % de PP). El subtotal comercial es 85,00.
            Assert.AreEqual(85.00m, GeneradorPdfFacturasQuestPdf.ImporteAntesProntoPago(82.45m, 0.03m));
        }

        [TestMethod]
        public void ImporteAntesProntoPago_SinProntoPago_DevuelveElImporte()
        {
            Assert.AreEqual(82.45m, GeneradorPdfFacturasQuestPdf.ImporteAntesProntoPago(82.45m, 0m));
        }

        [TestMethod]
        public void SeparacionProntoPago_Cuadra_SubtotalMenosProntoPagoEsLaBase()
        {
            // El importe del pronto pago del pie = subtotal - base imponible, y coincide con base*PP/(1-PP).
            decimal baseImponible = 82.45m;
            decimal pp = 0.03m;
            decimal subtotal = GeneradorPdfFacturasQuestPdf.ImporteAntesProntoPago(baseImponible, pp);
            decimal importePP = subtotal - baseImponible;
            Assert.AreEqual(85.00m, subtotal);
            Assert.AreEqual(2.55m, importePP);          // 85,00 * 3 %
            Assert.AreEqual(baseImponible, subtotal - importePP); // cuadra con la base imponible
        }

        [TestMethod]
        public void ComponerTextoDireccionEntrega_SoloNombre_NoIncluyeSeparadoresVacios()
        {
            var direccion = new DireccionFactura { Tipo = "Entrega", Nombre = "ACME S.L." };

            string texto = GeneradorPdfFacturasQuestPdf.ComponerTextoDireccionEntrega(direccion);

            // Sin CP/población/provincia no debe colar "()" ni separadores vacíos.
            Assert.AreEqual("Entrega: ACME S.L.", texto);
        }

        #endregion
    }
}
