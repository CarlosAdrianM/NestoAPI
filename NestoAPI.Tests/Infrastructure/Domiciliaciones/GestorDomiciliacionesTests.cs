using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Domiciliaciones;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Domiciliaciones;
using NestoAPI.Models.Facturas;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Mail;

namespace NestoAPI.Tests.Infrastructure.Domiciliaciones
{
    [TestClass]
    public class GestorDomiciliacionesTests
    {
        private IGestorFacturas gestorFacturas;
        private IServicioCorreoElectronico servicioCorreo;
        private IServicioDomiciliaciones servicioDomiciliaciones;
        private GestorDomiciliaciones gestor;

        [TestInitialize]
        public void Setup()
        {
            gestorFacturas = A.Fake<IGestorFacturas>();
            servicioCorreo = A.Fake<IServicioCorreoElectronico>();
            servicioDomiciliaciones = A.Fake<IServicioDomiciliaciones>();
            gestor = new GestorDomiciliaciones(servicioDomiciliaciones, servicioCorreo, gestorFacturas);
        }

        #region Tests existentes de AdjuntarFacturas

        [TestMethod]
        public void AdjuntarFacturas_ConNumeroDocumento_AdjuntaPDF()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(1, correo.Attachments.Count);
            Assert.AreEqual("NV001234.pdf", correo.Attachments[0].Name);
        }

        [TestMethod]
        public void AdjuntarFacturas_SinNumeroDocumento_NoAdjuntaNada()
        {
            // Arrange
            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = null } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(0, correo.Attachments.Count);
            A.CallTo(() => gestorFacturas.LeerFactura(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void AdjuntarFacturas_DocumentoDuplicado_AdjuntaSoloUnaVez()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos =
                {
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Efecto = "1", Importe = 100M },
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Efecto = "2", Importe = 100M }
                }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(1, correo.Attachments.Count);
        }

        [TestMethod]
        public void AdjuntarFacturas_VariasFacturasDistintas_AdjuntaTodas()
        {
            // Arrange
            var factura1 = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            var factura2 = new Factura { NumeroFactura = "NV005678", ImporteTotal = 200M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura1);
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV005678")).Returns(factura2);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos =
                {
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M },
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV005678", Importe = 200M }
                }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(2, correo.Attachments.Count);
        }

        [TestMethod]
        public void AdjuntarFacturas_FacturaNoExiste_ContinuaSinError()
        {
            // Arrange
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV999999")).Returns((Factura)null);

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV999999", Importe = 100M } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(0, correo.Attachments.Count);
        }

        [TestMethod]
        public void AdjuntarFacturas_ErrorEnPDF_ContinuaSinError()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .Throws(new System.Exception("Error generando PDF"));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(0, correo.Attachments.Count);
        }

        [TestMethod]
        public void AdjuntarFacturas_MezclaValidosEInvalidos_AdjuntaSoloValidos()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV999999")).Returns((Factura)null);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos =
                {
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M },
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV999999", Importe = 50M },
                    new EfectoDomiciliado { Empresa = "1", NumeroDocumento = null }
                }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(1, correo.Attachments.Count);
            Assert.AreEqual("NV001234.pdf", correo.Attachments[0].Name);
        }

        #endregion

        #region Tests de ObtenerDocumentosAdjuntar

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_EfectoSinNumeroDocumento_DevuelveListaVacia()
        {
            // Arrange
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = null, Importe = 100M };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_FacturaSimpleCuadra_DevuelveUnDocumento()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("NV001234", resultado[0].NumeroDocumento);
            Assert.AreEqual(100M, resultado[0].Importe);
            Assert.IsTrue(resultado[0].Descripcion.Contains("Factura NV001234"));
            Assert.IsTrue(resultado[0].Descripcion.Contains("100"));
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_FacturaSimpleNoCuadra_IntentaVencimientos()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV001234",
                ImporteTotal = 300M,
                Vencimientos = new List<VencimientoFactura>
                {
                    new VencimientoFactura { Importe = 100M },
                    new VencimientoFactura { Importe = 100M },
                    new VencimientoFactura { Importe = 100M }
                }
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.IsTrue(resultado[0].Descripcion.Contains("Vencimiento 1 de 3"));
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_VencimientoSegundoDeTres_DescripcionCorrecta()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV001234",
                ImporteTotal = 300M,
                Vencimientos = new List<VencimientoFactura>
                {
                    new VencimientoFactura { Importe = 100M },
                    new VencimientoFactura { Importe = 120M },
                    new VencimientoFactura { Importe = 80M }
                }
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 120M };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(1, resultado.Count);
            Assert.IsTrue(resultado[0].Descripcion.Contains("Vencimiento 2 de 3"));
            Assert.AreEqual(120M, resultado[0].Importe);
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_VencimientoNoCuadra_IntentaLiquidaciones()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV001234",
                ImporteTotal = 300M,
                Vencimientos = new List<VencimientoFactura>
                {
                    new VencimientoFactura { Importe = 150M },
                    new VencimientoFactura { Importe = 150M }
                }
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => servicioDomiciliaciones.BuscarDocumentosRelacionados("1", 10))
                .Returns(new List<DocumentoRelacionado>
                {
                    new DocumentoRelacionado { NumeroDocumento = "NV001234", Importe = 100M, Descripcion = "Factura NV001234 (100,00 €)" },
                    new DocumentoRelacionado { NumeroDocumento = "RC000001", Importe = -30M, Descripcion = "Abono RC000001 (-30,00 €)" }
                });
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 70M, NOrden = 10 };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual("NV001234", resultado[0].NumeroDocumento);
            Assert.AreEqual("RC000001", resultado[1].NumeroDocumento);
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_LiquidacionSimple_FacturaMenosAbono()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV005678",
                ImporteTotal = 100M,
                Vencimientos = new List<VencimientoFactura>()
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV005678")).Returns(factura);
            A.CallTo(() => servicioDomiciliaciones.BuscarDocumentosRelacionados("1", 20))
                .Returns(new List<DocumentoRelacionado>
                {
                    new DocumentoRelacionado { NumeroDocumento = "NV005678", Importe = 100M, Descripcion = "Factura NV005678 (100,00 €)" },
                    new DocumentoRelacionado { NumeroDocumento = "RC001234", Importe = -30M, Descripcion = "Abono RC001234 (-30,00 €)" }
                });
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV005678", Importe = 70M, NOrden = 20 };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(2, resultado.Count);
            decimal suma = 0;
            foreach (var doc in resultado)
            {
                suma += doc.Importe;
            }
            Assert.AreEqual(70M, suma);
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_LiquidacionNoCuadra_DevuelveListaVacia()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV005678",
                ImporteTotal = 100M,
                Vencimientos = new List<VencimientoFactura>()
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV005678")).Returns(factura);
            A.CallTo(() => servicioDomiciliaciones.BuscarDocumentosRelacionados("1", 20))
                .Returns(new List<DocumentoRelacionado>
                {
                    new DocumentoRelacionado { NumeroDocumento = "NV005678", Importe = 100M },
                    new DocumentoRelacionado { NumeroDocumento = "RC001234", Importe = -20M }
                });
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV005678", Importe = 70M, NOrden = 20 };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void ObtenerDocumentosAdjuntar_ErrorAlLeerFactura_DevuelveListaVacia()
        {
            // Arrange
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234"))
                .Throws(new System.Exception("Error de base de datos"));
            var efecto = new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M };

            // Act
            var resultado = gestor.ObtenerDocumentosAdjuntar(efecto);

            // Assert
            Assert.AreEqual(0, resultado.Count);
        }

        #endregion

        #region Tests de AdjuntarFacturas con validacion de importes

        [TestMethod]
        public void AdjuntarFacturas_ConDocumentosValidos_GeneraPDFsYAdjunta()
        {
            // Arrange
            var factura = new Factura { NumeroFactura = "NV001234", ImporteTotal = 100M };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => gestorFacturas.FacturasEnPDF(A<List<Factura>>._, false, null, false))
                .ReturnsLazily(() => new ByteArrayContent(new byte[] { 1, 2, 3 }));

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(1, correo.Attachments.Count);
        }

        [TestMethod]
        public void AdjuntarFacturas_SinDocumentos_NoAdjuntaNadaNiModificaBody()
        {
            // Arrange
            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = null } }
            };
            var correo = new MailMessage();
            correo.Body = "<body>contenido</body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(0, correo.Attachments.Count);
            Assert.IsFalse(correo.Body.Contains("Documentos adjuntos"));
        }

        [TestMethod]
        public void AdjuntarFacturas_ImporteNoCuadra_NoAdjunta()
        {
            // Arrange
            var factura = new Factura
            {
                NumeroFactura = "NV001234",
                ImporteTotal = 200M,
                Vencimientos = new List<VencimientoFactura>()
            };
            A.CallTo(() => gestorFacturas.LeerFactura("1", "NV001234")).Returns(factura);
            A.CallTo(() => servicioDomiciliaciones.BuscarDocumentosRelacionados("1", 10))
                .Returns(new List<DocumentoRelacionado>());

            var cliente = new DomiciliacionesCliente
            {
                Correo = "test@test.com",
                ListaEfectos = { new EfectoDomiciliado { Empresa = "1", NumeroDocumento = "NV001234", Importe = 100M, NOrden = 10 } }
            };
            var correo = new MailMessage();
            correo.Body = "<body></body>";

            // Act
            gestor.AdjuntarFacturas(correo, cliente);

            // Assert
            Assert.AreEqual(0, correo.Attachments.Count);
        }

        #endregion

        #region Tests de GenerarTextoDocumentosAdjuntos

        [TestMethod]
        public void GenerarTextoDocumentosAdjuntos_FacturaSimple_TextoCorrecto()
        {
            // Arrange
            var documentos = new List<DocumentoRelacionado>
            {
                new DocumentoRelacionado
                {
                    NumeroDocumento = "NV001234",
                    Importe = 100M,
                    Descripcion = "Factura NV001234 por 100,00 €"
                }
            };

            // Act
            string resultado = gestor.GenerarTextoDocumentosAdjuntos(documentos);

            // Assert
            Assert.IsTrue(resultado.Contains("Documentos adjuntos"));
            Assert.IsTrue(resultado.Contains("Factura NV001234 por 100,00"));
        }

        [TestMethod]
        public void GenerarTextoDocumentosAdjuntos_Vencimiento_TextoConNumeroVencimiento()
        {
            // Arrange
            var documentos = new List<DocumentoRelacionado>
            {
                new DocumentoRelacionado
                {
                    NumeroDocumento = "NV001234",
                    Importe = 33.33M,
                    Descripcion = "Factura NV001234 - Vencimiento 2 de 3 por 33,33 €"
                }
            };

            // Act
            string resultado = gestor.GenerarTextoDocumentosAdjuntos(documentos);

            // Assert
            Assert.IsTrue(resultado.Contains("Vencimiento 2 de 3"));
        }

        [TestMethod]
        public void GenerarTextoDocumentosAdjuntos_Liquidacion_TextoConFacturaYAbono()
        {
            // Arrange
            var documentos = new List<DocumentoRelacionado>
            {
                new DocumentoRelacionado { NumeroDocumento = "NV005678", Importe = 100M, Descripcion = "Factura NV005678 (100,00 €)" },
                new DocumentoRelacionado { NumeroDocumento = "RC001234", Importe = -30M, Descripcion = "Abono RC001234 (-30,00 €)" }
            };

            // Act
            string resultado = gestor.GenerarTextoDocumentosAdjuntos(documentos);

            // Assert
            Assert.IsTrue(resultado.Contains("Factura NV005678"));
            Assert.IsTrue(resultado.Contains("Abono RC001234"));
        }

        [TestMethod]
        public void GenerarTextoDocumentosAdjuntos_SinDocumentos_DevuelveVacio()
        {
            // Act
            string resultado = gestor.GenerarTextoDocumentosAdjuntos(new List<DocumentoRelacionado>());

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        [TestMethod]
        public void GenerarTextoDocumentosAdjuntos_ListaNula_DevuelveVacio()
        {
            // Act
            string resultado = gestor.GenerarTextoDocumentosAdjuntos(null);

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        #endregion
    }
}
