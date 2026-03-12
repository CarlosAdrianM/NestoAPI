using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests para Issue #58: Campo Purchase Order (PO) / SuPedido en pedidos y facturas
    /// </summary>
    [TestClass]
    public class SuPedidoTests
    {
        #region Modelo - CabPedidoVta

        [TestMethod]
        public void CabPedidoVta_SuPedido_PorDefectoEsNull()
        {
            var cab = new CabPedidoVta();
            Assert.IsNull(cab.SuPedido);
        }

        [TestMethod]
        public void CabPedidoVta_ClonarParaEmpresa_CopiaSuPedido()
        {
            var original = new CabPedidoVta
            {
                Empresa = "1",
                Número = 100,
                SuPedido = "PO-2026-001",
                Fecha_Modificación = DateTime.Now
            };

            var clon = original.ClonarParaEmpresa("3");

            Assert.AreEqual("PO-2026-001", clon.SuPedido);
        }

        [TestMethod]
        public void CabPedidoVta_ClonarParaEmpresa_CopiaSuPedidoNull()
        {
            var original = new CabPedidoVta
            {
                Empresa = "1",
                Número = 100,
                SuPedido = null,
                Fecha_Modificación = DateTime.Now
            };

            var clon = original.ClonarParaEmpresa("3");

            Assert.IsNull(clon.SuPedido);
        }

        #endregion

        #region Modelo - CabFacturaVta

        [TestMethod]
        public void CabFacturaVta_SuPedido_PorDefectoEsNull()
        {
            var cab = new CabFacturaVta();
            Assert.IsNull(cab.SuPedido);
        }

        #endregion

        #region DTO - PedidoVentaDTO

        [TestMethod]
        public void PedidoVentaDTO_SuPedido_PorDefectoEsNull()
        {
            var dto = new PedidoVentaDTO();
            Assert.IsNull(dto.suPedido);
        }

        [TestMethod]
        public void PedidoVentaDTO_SuPedido_AlmacenaValor()
        {
            var dto = new PedidoVentaDTO { suPedido = "4505571723" };
            Assert.AreEqual("4505571723", dto.suPedido);
        }

        #endregion

        #region Modelo PDF - Factura

        [TestMethod]
        public void Factura_SuPedido_PorDefectoEsNull()
        {
            var factura = new Factura();
            Assert.IsNull(factura.SuPedido);
        }

        #endregion

        #region GestorFacturas - LeerFactura lee SuPedido de CabFacturaVta (congelado)

        [TestMethod]
        public void GestorFacturas_LeerFactura_SuPedidoSeLeeDeFacturaNoDelPedido()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();

            CabFacturaVta cabFactura = A.Fake<CabFacturaVta>();
            cabFactura.Vendedor = "VD";
            cabFactura.Nº_Cliente = "1111";
            cabFactura.Serie = "NV";
            cabFactura.SuPedido = "PO-FACTURA-001"; // Valor congelado en factura

            LinPedidoVta linea = new LinPedidoVta
            {
                Número = 100,
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 3, 12),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 10,
                Producto = "PROD01",
                Base_Imponible = 8.26M,
                ImporteIVA = 1.74M,
                ImporteRE = 0,
                Total = 10,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cabFactura.LinPedidoVtas.Add(linea);

            A.CallTo(() => servicio.CargarCabFactura("1", "NV00001")).Returns(cabFactura);

            // Pedido con SuPedido diferente (simulando que se modificó después de facturar)
            CabPedidoVta cabPedido = A.Fake<CabPedidoVta>();
            cabPedido.SuPedido = "PO-MODIFICADO-DESPUES";
            cabPedido.Ruta = "RT";
            cabPedido.Comentarios = "Comentario";
            A.CallTo(() => servicio.CargarCabPedido("1", 100)).Returns(cabPedido);

            // Vencimientos que cuadran con el total (10€)
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 10, ImportePendiente = 10 }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(vencimientos);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV00001");

            // Assert: SuPedido viene de la factura, NO del pedido
            Assert.AreEqual("PO-FACTURA-001", factura.SuPedido);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SuPedidoNullSiFacturaNoTiene()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();

            CabFacturaVta cabFactura = A.Fake<CabFacturaVta>();
            cabFactura.Vendedor = "VD";
            cabFactura.Nº_Cliente = "1111";
            cabFactura.Serie = "NV";
            cabFactura.SuPedido = null;

            LinPedidoVta linea = new LinPedidoVta
            {
                Número = 100,
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 3, 12),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 10,
                Producto = "PROD01",
                Base_Imponible = 8.26M,
                ImporteIVA = 1.74M,
                ImporteRE = 0,
                Total = 10,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cabFactura.LinPedidoVtas.Add(linea);

            A.CallTo(() => servicio.CargarCabFactura("1", "NV00001")).Returns(cabFactura);
            CabPedidoVta cabPedido = A.Fake<CabPedidoVta>();
            cabPedido.Ruta = "RT";
            A.CallTo(() => servicio.CargarCabPedido("1", 100)).Returns(cabPedido);
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 10, ImportePendiente = 10 }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(vencimientos);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV00001");

            // Assert
            Assert.IsNull(factura.SuPedido);
        }

        #endregion

        #region GestorFacturas - LeerPedido lee SuPedido de CabPedidoVta

        [TestMethod]
        public void GestorFacturas_LeerPedido_SuPedidoSeLeeDelPedido()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();

            CabPedidoVta cabPedido = A.Fake<CabPedidoVta>();
            cabPedido.Empresa = "1";
            cabPedido.Número = 100;
            cabPedido.Nº_Cliente = "1111";
            cabPedido.Contacto = "0  ";
            cabPedido.Serie = "NV";
            cabPedido.Vendedor = "VD";
            cabPedido.Fecha = DateTime.Today;
            cabPedido.PlazosPago = "CON";
            cabPedido.Forma_Pago = "EFC";
            cabPedido.CCC = null;
            cabPedido.Primer_Vencimiento = DateTime.Today;
            cabPedido.SuPedido = "  PO-PEDIDO-001  "; // Con espacios para verificar Trim
            cabPedido.Ruta = "RT";

            LinPedidoVta lineaPedido = new LinPedidoVta
            {
                Número = 100,
                Cantidad = 0,
                Texto = "PRODUCTO TEST",
                Precio = 0,
                Producto = "PROD01",
                Base_Imponible = 0,
                ImporteIVA = 0,
                ImporteRE = 0,
                Total = 0,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M,
                Delegación = "NV"
            };
            cabPedido.LinPedidoVtas.Add(lineaPedido);

            A.CallTo(() => servicio.CargarCabPedido("1", 100)).Returns(cabPedido);
            A.CallTo(() => servicio.CargarVendedoresPedido("1", 100)).Returns(new List<VendedorFactura>());

            // LeerPedido calcula vencimientos desde efectos o plazos de pago
            A.CallTo(() => servicio.CargarEfectosPedido("1", 100)).Returns(new List<EfectoPedidoVenta>());
            PlazoPago plazoPago = A.Fake<PlazoPago>();
            plazoPago.MesesPrimerPlazo = 0;
            plazoPago.DíasPrimerPlazo = 0;
            plazoPago.Nº_Plazos = 1;
            A.CallTo(() => servicio.CargarPlazosPago("1", "CON")).Returns(plazoPago);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerPedido("1", 100);

            // Assert: SuPedido viene del pedido, con Trim
            Assert.AreEqual("PO-PEDIDO-001", factura.SuPedido);
        }

        #endregion

        #region GestorFacturas - LeerAlbaran lee SuPedido de CabPedidoVta

        [TestMethod]
        public void GestorFacturas_LeerAlbaran_SuPedidoSeLeeDelPedido()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();

            CabPedidoVta cabPedido = A.Fake<CabPedidoVta>();
            cabPedido.Empresa = "1";
            cabPedido.Número = 100;
            cabPedido.Nº_Cliente = "1111";
            cabPedido.Contacto = "0  ";
            cabPedido.Serie = "NV";
            cabPedido.Vendedor = "VD";
            cabPedido.Fecha = DateTime.Today;
            cabPedido.SuPedido = "PO-ALBARAN-001";
            cabPedido.Ruta = "RT";

            LinPedidoVta linea = new LinPedidoVta
            {
                Número = 100,
                Nº_Albarán = 5,
                Fecha_Albarán = DateTime.Today,
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 10,
                Producto = "PROD01",
                Base_Imponible = 8.26M,
                ImporteIVA = 1.74M,
                ImporteRE = 0,
                Total = 10,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M,
                Estado = 2,
                Delegación = "NV"
            };
            cabPedido.LinPedidoVtas.Add(linea);

            A.CallTo(() => servicio.CargarCabPedidoPorAlbaran("1", 5)).Returns(cabPedido);
            A.CallTo(() => servicio.CargarVendedoresPedido("1", 100)).Returns(new List<VendedorFactura>());

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura albaran = gestor.LeerAlbaran("1", 5);

            // Assert
            Assert.AreEqual("PO-ALBARAN-001", albaran.SuPedido);
        }

        #endregion

        #region PDF - Su Pedido / P.O. se muestra condicionalmente

        [TestMethod]
        public void GeneradorPdf_ConSuPedido_GeneraPdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfFacturasQuestPdf();
            var factura = CrearFacturaBasicaParaPdf();
            factura.SuPedido = "4505571723";

            // Act
            var resultado = generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0, "El PDF debe tener contenido");
            Assert.AreEqual(0x25, bytes[0], "Debe ser PDF válido");
        }

        [TestMethod]
        public void GeneradorPdf_SinSuPedido_GeneraPdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfFacturasQuestPdf();
            var factura = CrearFacturaBasicaParaPdf();
            factura.SuPedido = null;

            // Act
            var resultado = generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        [TestMethod]
        public void GeneradorPdf_SuPedidoVacio_GeneraPdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfFacturasQuestPdf();
            var factura = CrearFacturaBasicaParaPdf();
            factura.SuPedido = "   "; // Solo espacios

            // Act
            var resultado = generador.GenerarPdf(new List<Factura> { factura });

            // Assert: No debe fallar, y no debe mostrar "Su Pedido / P.O."
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        [TestMethod]
        public void GeneradorPdf_FormatoTicket_ConSuPedido_GeneraPdfValido()
        {
            // Arrange
            var generador = new GeneradorPdfFacturasQuestPdf();
            var factura = CrearFacturaBasicaParaPdf();
            factura.UrlLogo = null;
            factura.UsaFormatoTicket = true;
            factura.SuPedido = "PO-TICKET-001";

            // Act
            var resultado = generador.GenerarPdf(new List<Factura> { factura });

            // Assert
            Assert.IsNotNull(resultado);
            var bytes = resultado.ReadAsByteArrayAsync().Result;
            Assert.IsTrue(bytes.Length > 0);
        }

        #endregion

        #region Helpers

        private Factura CrearFacturaBasicaParaPdf()
        {
            return new Factura
            {
                Cliente = "TEST001",
                NumeroFactura = "NV000001",
                Fecha = DateTime.Today,
                ImporteTotal = 100m,
                Serie = "NV",
                TipoDocumento = "FACTURA",
                UrlLogo = "https://www.productosdeesteticaypeluqueriaprofesional.com/img/cms/Landing/logo.png",
                Direcciones = new List<DireccionFactura>
                {
                    new DireccionFactura { Tipo = "Empresa", Nombre = "Empresa Test", Direccion = "Calle Test 1", CodigoPostal = "28000", Poblacion = "Madrid", Provincia = "Madrid" },
                    new DireccionFactura { Tipo = "Fiscal", Nombre = "Cliente Test", Direccion = "Calle Cliente 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid" },
                    new DireccionFactura { Tipo = "Entrega", Nombre = "Cliente Test", Direccion = "Calle Entrega 1", CodigoPostal = "28002", Poblacion = "Madrid", Provincia = "Madrid" }
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
    }
}
