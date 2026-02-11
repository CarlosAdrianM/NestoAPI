using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorFacturasTests
    {
        [TestMethod]
        public void GestorFacturas_CargarFactura_SiLosVencimientosPendientesSumanLoMismoQueLaFacturaSeQuitanLosPagados()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 10,
                Producto = "123345",
                Base_Imponible = 8.26M,
                ImporteIVA = 1.74M,
                ImporteRE = 0,
                Total = 10,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                Importe = 10,
                ImportePendiente = 0
            };
            VencimientoFactura vto2 = new VencimientoFactura
            {
                Importe = -10,
                ImportePendiente = 0
            };
            VencimientoFactura vto3 = new VencimientoFactura
            {
                Importe = 10,
                ImportePendiente = 10
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1,
                vto2,
                vto3
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(1, factura.Vencimientos.Count);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiLosVencimientosPagadosHacenCeroLosQuitamos()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                Importe = 20,
                ImportePendiente = 0
            };
            VencimientoFactura vto2 = new VencimientoFactura
            {
                Importe = -20,
                ImportePendiente = 0
            };
            VencimientoFactura vto3 = new VencimientoFactura
            {
                Importe = 10,
                ImportePendiente = 0
            };
            VencimientoFactura vto4 = new VencimientoFactura
            {
                Importe = 10,
                ImportePendiente = 10
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1,
                vto2,
                vto3,
                vto4
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(2, factura.Vencimientos.Count);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiHayUnVencimientoNegativoQueNoEsElPrimeroLoRestamosDelAnterior()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                Importe = 20,
                ImportePendiente = 0
            };
            VencimientoFactura vto2 = new VencimientoFactura
            {
                Importe = -14,
                ImportePendiente = 0
            };
            VencimientoFactura vto3 = new VencimientoFactura
            {
                Importe = 7,
                ImportePendiente = 7
            };
            VencimientoFactura vto4 = new VencimientoFactura
            {
                Importe = 7,
                ImportePendiente = 7
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1,
                vto2,
                vto3,
                vto4
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(3, factura.Vencimientos.Count);
            Assert.AreEqual(6, factura.Vencimientos.First().Importe);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiLosVencimientosNoCuadranConElTotalDeLaFacturaCogemosLosOriginales()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                Importe = 20,
                ImportePendiente = 0
            };
            VencimientoFactura vto2 = new VencimientoFactura
            {
                Importe = -20,
                ImportePendiente = 0
            };
            VencimientoFactura vto3 = new VencimientoFactura
            {
                Importe = 10,
                ImportePendiente = 0
            };
            VencimientoFactura vto4 = new VencimientoFactura
            {
                Importe = 9, // 9 en vez de 10 para forzar que NO cuadre
                ImportePendiente = 9
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1,
                vto2,
                vto3,
                vto4
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            List<VencimientoFactura> vencimientosOriginales = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosOriginales(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientosOriginales);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(1, factura.Vencimientos.Count);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiLaFormaDePagoEsEfectivoElIbanEsNoProcede()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "EFC",
                Importe = 20,
                ImportePendiente = 0
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual("<<< No Procede >>>", factura.Vencimientos.Single().Iban);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiLaFormaDePagoEsTransferenciaElIbanEsElDeLaEmpresa()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "TRN",
                Importe = 20,
                ImportePendiente = 0
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            A.CallTo(() => servicio.CuentaBancoEmpresa(A<string>.Ignored)).Returns("CUENTA EMPRESA");
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual("CUENTA EMPRESA", factura.Vencimientos.Single().Iban);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiTieneCCCElIbanNoSeToca()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                CCC = "1",
                FormaPago = "TRN",
                Importe = 20,
                ImportePendiente = 0,
                Iban = "Iban Cliente"
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            A.CallTo(() => servicio.CuentaBancoEmpresa(A<string>.Ignored)).Returns("CUENTA EMPRESA");
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual("Iban Cliente", factura.Vencimientos.Single().Iban);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiElTotalEsPositivoMostramosTextoFactura()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "EFC",
                Importe = 20,
                ImportePendiente = 0
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(Constantes.Facturas.TiposDocumento.FACTURA, factura.TipoDocumento);
        }

        [TestMethod]
        public void GestorFacturas_CargarFactura_SiElTotalEsNegativoMostramosTextoFacturaRectificativa()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new System.DateTime(2019, 09, 5),
                Cantidad = -1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = -16.52M,
                ImporteIVA = -3.48M,
                ImporteRE = 0,
                Total = -20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);
            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "EFC",
                Importe = -20,
                ImportePendiente = 0
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>()
            {
                vto1
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerFactura("1", "NV11111");

            Assert.AreEqual(Constantes.Facturas.TiposDocumento.FACTURA_RECTIFICATIVA, factura.TipoDocumento);
        }


        [TestMethod]
        public void GestorFacturas_CargarFactura_SiEsPresupuestoMostramosTextoFacturaProforma()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabPedidoVta cab = A.Fake<CabPedidoVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            cab.CCC = "1";
            cab.Forma_Pago = "EFC";
            cab.Fecha = new DateTime(2019, 11, 7);
            cab.Primer_Vencimiento = new DateTime(2019, 11, 7);
            cab.PlazosPago = "UnPlazo";
            PlazoPago plazoPago = new PlazoPago { Número = "UnPlazo", Nº_Plazos = 1 };
            A.CallTo(() => servicio.CargarPlazosPago(A<string>.Ignored, A<string>.Ignored)).Returns(plazoPago);

            LinPedidoVta linea = new LinPedidoVta
            {
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M,
                Estado = Constantes.EstadosLineaVenta.PRESUPUESTO
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabPedido("1", 11111)).Returns(cab);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerPedido("1", 11111);

            Assert.AreEqual(Constantes.Facturas.TiposDocumento.FACTURA_PROFORMA, factura.TipoDocumento);
        }


        [TestMethod]
        public void GestorFacturas_CargarFactura_SiEsNotaEntregaMostramosTextoNotaEntrega()
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabPedidoVta cab = A.Fake<CabPedidoVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            cab.CCC = "1";
            cab.Forma_Pago = "EFC";
            cab.Fecha = new DateTime(2019, 11, 7);
            cab.Primer_Vencimiento = new DateTime(2019, 11, 7);
            cab.PlazosPago = "UnPlazo";
            cab.NotaEntrega = true;
            PlazoPago plazoPago = new PlazoPago { Número = "UnPlazo", Nº_Plazos = 1 };
            A.CallTo(() => servicio.CargarPlazosPago(A<string>.Ignored, A<string>.Ignored)).Returns(plazoPago);

            LinPedidoVta linea = new LinPedidoVta
            {
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M,
                Estado = Constantes.EstadosLineaVenta.EN_CURSO
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabPedido("1", 11111)).Returns(cab);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            Factura factura = gestor.LeerPedido("1", 11111);

            Assert.AreEqual(Constantes.Facturas.TiposDocumento.NOTA_ENTREGA, factura.TipoDocumento);
            Assert.AreEqual(0, factura.ImporteTotal);
        }

        [TestMethod]
        public void GestorFactura_CalcularVencimientos_SiNumeroPlazosEsUnoSoloPoneUnPlazo()
        {
            PlazoPago plazoPago = new PlazoPago { Nº_Plazos = 1, DíasPrimerPlazo = 0 };
            DateTime primerVencimiento = new DateTime(2019, 11, 7);

            var vtos = GestorFacturas.CalcularVencimientos(100, plazoPago, "EFC", "1", primerVencimiento);

            Assert.AreEqual(1, vtos.Count);
            Assert.AreEqual(100, vtos[0].Importe);
            Assert.AreEqual(100, vtos[0].ImportePendiente);
            Assert.AreEqual("EFC", vtos[0].FormaPago);
            Assert.AreEqual("1", vtos[0].CCC);
            Assert.AreEqual(primerVencimiento, vtos[0].Vencimiento);
        }

        [TestMethod]
        public void GestorFactura_CalcularVencimientos_SiTieneVariosPlazosDivideElImporteEntreEllos()
        {
            PlazoPago plazoPago = new PlazoPago { Nº_Plazos = 2, DíasPrimerPlazo = 30, DíasEntrePlazos = 30 };
            DateTime primerVencimiento = new DateTime(2019, 11, 7);

            var vtos = GestorFacturas.CalcularVencimientos(100, plazoPago, "EFC", "1", primerVencimiento);

            Assert.AreEqual(2, vtos.Count);
            Assert.AreEqual(50, vtos[0].Importe);
            Assert.AreEqual(50, vtos[0].ImportePendiente);
            Assert.AreEqual("EFC", vtos[0].FormaPago);
            Assert.AreEqual("1", vtos[0].CCC);
            Assert.AreEqual(primerVencimiento, vtos[0].Vencimiento);
            Assert.AreEqual(50, vtos[1].Importe);
            Assert.AreEqual(50, vtos[1].ImportePendiente);
            Assert.AreEqual("EFC", vtos[1].FormaPago);
            Assert.AreEqual("1", vtos[1].CCC);
            Assert.AreEqual(primerVencimiento.AddDays(plazoPago.DíasEntrePlazos), vtos[1].Vencimiento);
        }

        [TestMethod]
        public void GestorFactura_CalcularVencimientos_SiElImporteNoEsExactoAjustaEnElUltimoVencimiento()
        {
            PlazoPago plazoPago = new PlazoPago { Nº_Plazos = 3, MesesPrimerPlazo = 1, MesesEntrePlazos = 1 };
            DateTime primerVencimiento = new DateTime(2019, 11, 7);

            var vtos = GestorFacturas.CalcularVencimientos(100, plazoPago, "EFC", "1", primerVencimiento);

            Assert.AreEqual(3, vtos.Count);
            Assert.AreEqual(33.33M, vtos[0].Importe);
            Assert.AreEqual(33.33M, vtos[1].Importe);
            Assert.AreEqual(33.34M, vtos[2].Importe);
            Assert.AreEqual(primerVencimiento, vtos[0].Vencimiento);
            Assert.AreEqual(primerVencimiento.AddMonths(plazoPago.MesesEntrePlazos), vtos[1].Vencimiento);
            Assert.AreEqual(primerVencimiento.AddMonths(plazoPago.MesesEntrePlazos*2), vtos[2].Vencimiento);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SiNoHayLineasImporteTotalEsCeroYLanzaExcepcion()
        {
            // Arrange - Este test documenta el bug que ocurría cuando CargarCabFactura
            // no incluía las líneas (LinPedidoVtas) con .Include() y LazyLoadingEnabled = false.
            // El importeTotal quedaba en 0 y no cuadraba con los vencimientos del extracto.
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            // NO añadimos líneas a cab.LinPedidoVtas para simular el bug
            // donde las líneas no se cargaban por falta de Include()

            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            // Los vencimientos sí se cargan correctamente del extracto
            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "EFC",
                Importe = 744.09M,
                ImportePendiente = 744.09M
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>() { vto1 };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            A.CallTo(() => servicio.CargarVencimientosOriginales(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act & Assert - Sin líneas, importeTotal = 0, no cuadra con vencimientos = 744.09
            var ex = Assert.ThrowsException<Exception>(() => gestor.LeerFactura("1", "NV11111"));
            Assert.IsTrue(ex.Message.Contains("No cuadran los vencimientos"));
            Assert.IsTrue(ex.Message.Contains("Total calculado: 0,00€") || ex.Message.Contains("Total calculado: 0.00"));
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConLineasCalculaImporteTotalCorrectamente()
        {
            // Arrange - Este test verifica que cuando las líneas SÍ se cargan,
            // el importeTotal se calcula correctamente y cuadra con los vencimientos.
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";

            // Añadimos una línea que genera un total de 744.09€
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2025, 12, 3),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 614.95M,
                Producto = "12345",
                Base_Imponible = 614.95M,
                ImporteIVA = 129.14M, // 21% de 614.95
                ImporteRE = 0,
                Total = 744.09M,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);

            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            VencimientoFactura vto1 = new VencimientoFactura
            {
                FormaPago = "EFC",
                Importe = 744.09M,
                ImportePendiente = 744.09M
            };
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>() { vto1 };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act - No debe lanzar excepción porque importeTotal = 744.09 cuadra con vencimientos
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert
            Assert.AreEqual(744.09M, factura.ImporteTotal);
            Assert.AreEqual(1, factura.Vencimientos.Count);
            Assert.AreEqual(744.09M, factura.Vencimientos.First().Importe);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void GestorFacturas_LeerFacturas_CuandoVencimientosNoCuadranDebeLanzarExcepcion()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabPedidoVta cab = A.Fake<CabPedidoVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            cab.Número = 12345;
            cab.CCC = "1";
            cab.Forma_Pago = "EFC";
            cab.Fecha = new DateTime(2019, 11, 7);
            cab.Primer_Vencimiento = new DateTime(2019, 11, 7);
            cab.PlazosPago = "UnPlazo";

            PlazoPago plazoPago = new PlazoPago { Número = "UnPlazo", Nº_Plazos = 1 };
            A.CallTo(() => servicio.CargarPlazosPago(A<string>.Ignored, A<string>.Ignored)).Returns(plazoPago);

            LinPedidoVta linea = new LinPedidoVta
            {
                Cantidad = 1,
                Texto = "PRODUCTO ROJO",
                Precio = 20,
                Producto = "123345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M,
                Estado = Constantes.EstadosLineaVenta.PRESUPUESTO
            };
            cab.LinPedidoVtas.Add(linea);

            A.CallTo(() => servicio.CargarCabPedido("1", 12345)).Returns(cab);

            // Los efectos suman 15 en lugar de 20, generando un desbalance
            List<EfectoPedidoVenta> efectos = new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta
                {
                    Importe = 15M, // Mal: debería ser 20
                    FechaVencimiento = new DateTime(2019, 11, 7),
                    FormaPago = "EFC",
                    CCC = "1"
                }
            };
            A.CallTo(() => servicio.CargarEfectosPedido(A<string>.Ignored, A<int>.Ignored)).Returns(efectos);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act - esto debería lanzar una excepción con el mensaje
            // "No cuadran los vencimientos con el total de la factura"
            List<FacturaLookup> facturas = new List<FacturaLookup>
            {
                new FacturaLookup { Empresa = "1", Factura = "12345" }
            };

            // Assert - ExpectedException debería capturar la excepción
            gestor.LeerFacturas(facturas);
        }

        #region Tests para GenerarMensajeModelo347 (Issue #72)

        [TestMethod]
        public void GenerarMensajeModelo347_EnEnero_DevuelveMensajeConContenido()
        {
            // Arrange
            DateTime fechaEnero = new DateTime(2025, 1, 15);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fechaEnero);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(resultado));
            Assert.IsTrue(resultado.Contains("347"));
            Assert.IsTrue(resultado.Contains("certificado"));
        }

        [TestMethod]
        public void GenerarMensajeModelo347_EnFebrero_DevuelveMensajeConContenido()
        {
            // Arrange
            DateTime fechaFebrero = new DateTime(2025, 2, 28);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fechaFebrero);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(resultado));
            Assert.IsTrue(resultado.Contains("347"));
        }

        [TestMethod]
        public void GenerarMensajeModelo347_EnMarzo_DevuelveCadenaVacia()
        {
            // Arrange
            DateTime fechaMarzo = new DateTime(2025, 3, 1);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fechaMarzo);

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        [TestMethod]
        public void GenerarMensajeModelo347_EnDiciembre_DevuelveCadenaVacia()
        {
            // Arrange
            DateTime fechaDiciembre = new DateTime(2025, 12, 31);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fechaDiciembre);

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        [TestMethod]
        public void GenerarMensajeModelo347_EnJulio_DevuelveCadenaVacia()
        {
            // Arrange
            DateTime fechaJulio = new DateTime(2025, 7, 15);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fechaJulio);

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        [TestMethod]
        public void GenerarMensajeModelo347_PrimerDiaEnero_DevuelveMensaje()
        {
            // Arrange - Caso límite: 1 de enero
            DateTime fecha = new DateTime(2025, 1, 1);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fecha);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(resultado));
        }

        [TestMethod]
        public void GenerarMensajeModelo347_UltimoDiaFebrero_DevuelveMensaje()
        {
            // Arrange - Caso límite: último día de febrero (año no bisiesto)
            DateTime fecha = new DateTime(2025, 2, 28);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fecha);

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(resultado));
        }

        [TestMethod]
        public void GenerarMensajeModelo347_PrimerDiaMarzo_DevuelveCadenaVacia()
        {
            // Arrange - Caso límite: 1 de marzo (ya no debe mostrar)
            DateTime fecha = new DateTime(2025, 3, 1);

            // Act
            string resultado = GestorFacturas.GenerarMensajeModelo347(fecha);

            // Assert
            Assert.AreEqual(string.Empty, resultado);
        }

        #endregion

        #region Tests para ObtenerUltimosVencimientos (Issue #96)

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConListaVacia_RetornaNull()
        {
            var resultado = GestorFacturas.ObtenerUltimosVencimientos(new List<VencimientoFactura>(), 100);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConImporteTotalCero_RetornaNull()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 50 },
                new VencimientoFactura { Importe = -50 }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 0);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConUnSoloVencimientoPositivo_RetornaEse()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, Vencimiento = new DateTime(2026, 2, 4) }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 100);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual(100, resultado[0].Importe);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConMultiplesPositivosQueSumanTotal_RetornaTodos()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 50, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 50, Vencimiento = new DateTime(2026, 3, 4) }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 100);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual(50, resultado[0].Importe);
            Assert.AreEqual(50, resultado[1].Importe);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConUnaRondaDeModificacion_RetornaUltimaRonda()
        {
            // Efecto original + anulación + 3 nuevos efectos
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = -1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 362.65M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 362.65M, Vencimiento = new DateTime(2026, 3, 6) },
                new VencimientoFactura { Importe = 362.64M, Vencimiento = new DateTime(2026, 4, 5) }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 1087.94M);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(3, resultado.Count);
            Assert.AreEqual(362.65M, resultado[0].Importe);
            Assert.AreEqual(362.65M, resultado[1].Importe);
            Assert.AreEqual(362.64M, resultado[2].Importe);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConDosRondasDeModificacion_RetornaUltimaRonda()
        {
            // Efecto original + anulación + 3 efectos + reagrupación + anulación + 4 efectos
            var vencimientos = new List<VencimientoFactura>
            {
                // Ronda 1: efecto original
                new VencimientoFactura { Importe = 1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                // Anulación ronda 1
                new VencimientoFactura { Importe = -1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                // Ronda 2: dividido en 3
                new VencimientoFactura { Importe = 362.65M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 362.65M, Vencimiento = new DateTime(2026, 3, 6) },
                new VencimientoFactura { Importe = 362.64M, Vencimiento = new DateTime(2026, 4, 5) },
                // Reagrupación: anulan los 3
                new VencimientoFactura { Importe = -362.65M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = -362.65M, Vencimiento = new DateTime(2026, 3, 6) },
                new VencimientoFactura { Importe = -362.64M, Vencimiento = new DateTime(2026, 4, 5) },
                // Efecto agrupado
                new VencimientoFactura { Importe = 1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                // Anulación del agrupado
                new VencimientoFactura { Importe = -1087.94M, Vencimiento = new DateTime(2026, 2, 4) },
                // Ronda 3: dividido en 4 (CORRECTO)
                new VencimientoFactura { Importe = 271.98M, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 271.98M, Vencimiento = new DateTime(2026, 3, 6) },
                new VencimientoFactura { Importe = 271.98M, Vencimiento = new DateTime(2026, 4, 5) },
                new VencimientoFactura { Importe = 272.00M, Vencimiento = new DateTime(2026, 5, 5) }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 1087.94M);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(4, resultado.Count);
            Assert.AreEqual(271.98M, resultado[0].Importe);
            Assert.AreEqual(new DateTime(2026, 2, 4), resultado[0].Vencimiento);
            Assert.AreEqual(271.98M, resultado[1].Importe);
            Assert.AreEqual(new DateTime(2026, 3, 6), resultado[1].Vencimiento);
            Assert.AreEqual(271.98M, resultado[2].Importe);
            Assert.AreEqual(new DateTime(2026, 4, 5), resultado[2].Vencimiento);
            Assert.AreEqual(272.00M, resultado[3].Importe);
            Assert.AreEqual(new DateTime(2026, 5, 5), resultado[3].Vencimiento);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_CuandoSumaSuperaTotal_RetornaNull()
        {
            // Los últimos positivos suman más que el total
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 20 },
                new VencimientoFactura { Importe = -14 },
                new VencimientoFactura { Importe = 7 },
                new VencimientoFactura { Importe = 7 }
            };

            // Total es 20, pero los últimos positivos desde el final son 7+7=14, luego 14+20=34 > 20
            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 20);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConTotalNegativo_RetornaUltimosNegativos()
        {
            // Factura rectificativa: total negativo
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = -500, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = 500, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = -250, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura { Importe = -250, Vencimiento = new DateTime(2026, 3, 6) }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, -500);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual(-250, resultado[0].Importe);
            Assert.AreEqual(-250, resultado[1].Importe);
        }

        [TestMethod]
        public void ObtenerUltimosVencimientos_ConservaDatosDelVencimiento()
        {
            // Verifica que CCC, FormaPago, ImportePendiente se mantienen
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, Vencimiento = new DateTime(2026, 2, 4), CCC = "OLD", FormaPago = "OLD" },
                new VencimientoFactura { Importe = -100, Vencimiento = new DateTime(2026, 2, 4) },
                new VencimientoFactura
                {
                    Importe = 60, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4),
                    CCC = "10", FormaPago = "RCB", Iban = "ES21 2100 ****"
                },
                new VencimientoFactura
                {
                    Importe = 40, ImportePendiente = 40, Vencimiento = new DateTime(2026, 3, 6),
                    CCC = "10", FormaPago = "RCB", Iban = "ES21 2100 ****"
                }
            };

            var resultado = GestorFacturas.ObtenerUltimosVencimientos(vencimientos, 100);

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual(0, resultado[0].ImportePendiente);
            Assert.AreEqual("10", resultado[0].CCC);
            Assert.AreEqual("RCB", resultado[0].FormaPago);
            Assert.AreEqual(40, resultado[1].ImportePendiente);
        }

        #endregion

        #region Tests para LeerFactura con múltiples rondas de modificación (Issue #96)

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConMultiplesRondasDeModificacion_MuestraSoloUltimaRonda()
        {
            // Arrange - Reproduce el caso real de la factura NV2601836
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "IM";
            cab.Nº_Cliente = "11437";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 2, 4),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 899.12M,
                Producto = "12345",
                Base_Imponible = 899.12M,
                ImporteIVA = 188.82M,
                ImporteRE = 0,
                Total = 1087.94M,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV2601836")).Returns(cab);

            // 14 registros CARTERA simulando múltiples rondas de modificación
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                // Asiento 1171298: Efecto original
                new VencimientoFactura { Importe = 1087.94M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                // Asiento 1171582: Anulación + división en 3
                new VencimientoFactura { Importe = -1087.94M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 362.65M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 362.65M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 3, 6), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 362.64M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 4, 5), FormaPago = "RCB", CCC = "10" },
                // Asiento 1171584: Reagrupación (anulan los 3 + 1 nuevo efecto agrupado)
                new VencimientoFactura { Importe = -362.65M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB" },
                new VencimientoFactura { Importe = -362.65M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 3, 6), FormaPago = "RCB" },
                new VencimientoFactura { Importe = -362.64M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 4, 5), FormaPago = "RCB" },
                new VencimientoFactura { Importe = 1087.94M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                // Asiento 1171585: Anulación + división en 4 (CORRECTO)
                new VencimientoFactura { Importe = -1087.94M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 271.98M, ImportePendiente = 0, Vencimiento = new DateTime(2026, 2, 4), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 271.98M, ImportePendiente = 271.98M, Vencimiento = new DateTime(2026, 3, 6), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 271.98M, ImportePendiente = 271.98M, Vencimiento = new DateTime(2026, 4, 5), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 272.00M, ImportePendiente = 259.66M, Vencimiento = new DateTime(2026, 5, 5), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV2601836");

            // Assert - Solo deben aparecer los 4 vencimientos de la última ronda
            Assert.AreEqual(4, factura.Vencimientos.Count);
            Assert.AreEqual(271.98M, factura.Vencimientos[0].Importe);
            Assert.AreEqual(new DateTime(2026, 2, 4), factura.Vencimientos[0].Vencimiento);
            Assert.AreEqual(0M, factura.Vencimientos[0].ImportePendiente);
            Assert.AreEqual(271.98M, factura.Vencimientos[1].Importe);
            Assert.AreEqual(new DateTime(2026, 3, 6), factura.Vencimientos[1].Vencimiento);
            Assert.AreEqual(271.98M, factura.Vencimientos[2].Importe);
            Assert.AreEqual(new DateTime(2026, 4, 5), factura.Vencimientos[2].Vencimiento);
            Assert.AreEqual(272.00M, factura.Vencimientos[3].Importe);
            Assert.AreEqual(259.66M, factura.Vencimientos[3].ImportePendiente);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConUnaRondaDeModificacion_MuestraNuevosVencimientos()
        {
            // Arrange - Un solo cambio de plazos (caso más habitual)
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 15),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 100,
                Producto = "12345",
                Base_Imponible = 82.64M,
                ImporteIVA = 17.36M,
                ImporteRE = 0,
                Total = 100,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            // Efecto original + anulación + 2 nuevos efectos
            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 0, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = -100, ImportePendiente = 0, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 50, ImportePendiente = 50, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 50, ImportePendiente = 50, Vencimiento = new DateTime(2026, 2, 15), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert
            Assert.AreEqual(2, factura.Vencimientos.Count);
            Assert.AreEqual(50, factura.Vencimientos[0].Importe);
            Assert.AreEqual(50, factura.Vencimientos[1].Importe);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SinModificaciones_MuestraVencimientoOriginal()
        {
            // Arrange - Caso más simple: un solo vencimiento sin cambios
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 15),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 100,
                Producto = "12345",
                Base_Imponible = 82.64M,
                ImporteIVA = 17.36M,
                ImporteRE = 0,
                Total = 100,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 100, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert
            Assert.AreEqual(1, factura.Vencimientos.Count);
            Assert.AreEqual(100, factura.Vencimientos.First().Importe);
            Assert.AreEqual(100, factura.Vencimientos.First().ImportePendiente);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SinModificacionesConTresPlazos_MuestraTresVencimientos()
        {
            // Arrange - Tres plazos originales sin modificación
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 15),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 300,
                Producto = "12345",
                Base_Imponible = 247.93M,
                ImporteIVA = 52.07M,
                ImporteRE = 0,
                Total = 300,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 0, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 100, ImportePendiente = 100, Vencimiento = new DateTime(2026, 2, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 100, ImportePendiente = 100, Vencimiento = new DateTime(2026, 3, 15), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert
            Assert.AreEqual(3, factura.Vencimientos.Count);
            Assert.AreEqual(100, factura.Vencimientos[0].Importe);
            Assert.AreEqual(100, factura.Vencimientos[1].Importe);
            Assert.AreEqual(100, factura.Vencimientos[2].Importe);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConModificacionParcial_ManejaCorrectamente()
        {
            // Arrange - Modificación parcial: [20, -14, 7, 7] (caso del test existente)
            // El while loop existente maneja esto: convierte a [6, 7, 7]
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 15),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 20,
                Producto = "12345",
                Base_Imponible = 16.52M,
                ImporteIVA = 3.48M,
                ImporteRE = 0,
                Total = 20,
                PorcentajeIVA = (byte)0.21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 20, ImportePendiente = 0, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = -14, ImportePendiente = 0, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB" },
                new VencimientoFactura { Importe = 7, ImportePendiente = 7, Vencimiento = new DateTime(2026, 1, 15), FormaPago = "RCB", CCC = "10" },
                new VencimientoFactura { Importe = 7, ImportePendiente = 7, Vencimiento = new DateTime(2026, 2, 15), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert - El while loop maneja esto: [6, 7, 7]
            Assert.AreEqual(3, factura.Vencimientos.Count);
            Assert.AreEqual(6, factura.Vencimientos[0].Importe);
            Assert.AreEqual(7, factura.Vencimientos[1].Importe);
            Assert.AreEqual(7, factura.Vencimientos[2].Importe);
        }

        #endregion

        #region Tests para AplicarImpagados (Issue #96)

        [TestMethod]
        public void AplicarImpagados_ConListaNull_NoFalla()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 0 }
            };

            GestorFacturas.AplicarImpagados(vencimientos, null);

            Assert.AreEqual(100, vencimientos[0].Importe);
            Assert.IsFalse(vencimientos[0].EsImpagado);
        }

        [TestMethod]
        public void AplicarImpagados_SinImpagados_NoModificaVencimientos()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 0, Vencimiento = new DateTime(2025, 10, 19) }
            };

            GestorFacturas.AplicarImpagados(vencimientos, new List<ImpagadoPendiente>());

            Assert.AreEqual(100, vencimientos[0].Importe);
            Assert.IsFalse(vencimientos[0].EsImpagado);
        }

        [TestMethod]
        public void AplicarImpagados_ConImpagadoSinGastos_MarcaComoImpagado()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 11, 19) }
            };
            var impagados = new List<ImpagadoPendiente>
            {
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 223.42M, EsGastos = false }
            };

            GestorFacturas.AplicarImpagados(vencimientos, impagados);

            Assert.IsTrue(vencimientos[0].EsImpagado);
            Assert.AreEqual(223.42M, vencimientos[0].Importe);
            Assert.AreEqual(223.42M, vencimientos[0].ImportePendiente);
            Assert.AreEqual(0, vencimientos[0].GastosImpagado);
            Assert.AreEqual("Impagado", vencimientos[0].TextoPagado);
        }

        [TestMethod]
        public void AplicarImpagados_ConImpagadoYGastos_ActualizaImporteYMarca()
        {
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 11, 19) }
            };
            var impagados = new List<ImpagadoPendiente>
            {
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 223.42M, EsGastos = false },
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 3.00M, EsGastos = true }
            };

            GestorFacturas.AplicarImpagados(vencimientos, impagados);

            Assert.IsTrue(vencimientos[0].EsImpagado);
            Assert.AreEqual(226.42M, vencimientos[0].Importe);
            Assert.AreEqual(226.42M, vencimientos[0].ImportePendiente);
            Assert.AreEqual(3.00M, vencimientos[0].GastosImpagado);
            Assert.IsTrue(vencimientos[0].TextoPagado.Contains("Impagado"));
            Assert.IsTrue(vencimientos[0].TextoPagado.Contains("gastos"));
        }

        [TestMethod]
        public void AplicarImpagados_ConMultiplesVencimientos_SoloMarcaElCorrecto()
        {
            // 4 efectos como el caso real NV2515520
            var vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 223.25M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 9, 19), FormaPago = "TRN" },
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 10, 19), FormaPago = "RCB" },
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 11, 19), FormaPago = "RCB" },
                new VencimientoFactura { Importe = 223.41M, ImportePendiente = 223.41M, Vencimiento = new DateTime(2025, 12, 19), FormaPago = "RCB" }
            };
            // Impagado solo en efecto 3 (19/11)
            var impagados = new List<ImpagadoPendiente>
            {
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 223.42M, EsGastos = false },
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 3.00M, EsGastos = true }
            };

            GestorFacturas.AplicarImpagados(vencimientos, impagados);

            // Efecto 1: pagado, sin cambios
            Assert.IsFalse(vencimientos[0].EsImpagado);
            Assert.AreEqual(223.25M, vencimientos[0].Importe);
            // Efecto 2: pagado, sin cambios
            Assert.IsFalse(vencimientos[1].EsImpagado);
            Assert.AreEqual(223.42M, vencimientos[1].Importe);
            // Efecto 3: impagado con gastos
            Assert.IsTrue(vencimientos[2].EsImpagado);
            Assert.AreEqual(226.42M, vencimientos[2].Importe);
            Assert.AreEqual(3.00M, vencimientos[2].GastosImpagado);
            // Efecto 4: pendiente, sin cambios
            Assert.IsFalse(vencimientos[3].EsImpagado);
            Assert.AreEqual(223.41M, vencimientos[3].Importe);
        }

        #endregion

        #region Tests de TextoPagado con impagados

        [TestMethod]
        public void TextoPagado_CuandoEsImpagadoSinGastos_MuestraImpagado()
        {
            var vencimiento = new VencimientoFactura
            {
                Importe = 223.42M,
                ImportePendiente = 223.42M,
                EsImpagado = true,
                GastosImpagado = 0
            };

            Assert.AreEqual("Impagado", vencimiento.TextoPagado);
        }

        [TestMethod]
        public void TextoPagado_CuandoEsImpagadoConGastos_MuestraDesglose()
        {
            var vencimiento = new VencimientoFactura
            {
                Importe = 226.42M,
                ImportePendiente = 226.42M,
                EsImpagado = true,
                GastosImpagado = 3.00M
            };

            string texto = vencimiento.TextoPagado;
            Assert.IsTrue(texto.StartsWith("Impagado ("));
            Assert.IsTrue(texto.Contains("gastos"));
        }

        [TestMethod]
        public void TextoPagado_CuandoNoEsImpagado_FuncionaComoAntes()
        {
            var pagado = new VencimientoFactura { Importe = 100, ImportePendiente = 0 };
            var pendiente = new VencimientoFactura { Importe = 100, ImportePendiente = 100 };
            var parcial = new VencimientoFactura { Importe = 100, ImportePendiente = 40 };
            var oculto = new VencimientoFactura { Importe = 100, ImportePendiente = 100, OcultarEstadoPago = true };

            Assert.AreEqual("Pagado", pagado.TextoPagado);
            Assert.AreEqual("Pendiente de pago", pendiente.TextoPagado);
            Assert.IsTrue(parcial.TextoPagado.Contains("40"));
            Assert.AreEqual(string.Empty, oculto.TextoPagado);
        }

        #endregion

        #region Tests de integración LeerFactura con impagados (Issue #96)

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConImpagadoPendiente_MuestraEfectosOriginalesConImpagado()
        {
            // Arrange - Reproduce el caso real NV2515520
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "IM";
            cab.Nº_Cliente = "25104";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2025, 9, 30),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 738.43M,
                Producto = "12345",
                Base_Imponible = 738.43M,
                ImporteIVA = 155.07M,
                ImporteRE = 0,
                Total = 893.50M,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV2515520")).Returns(cab);

            // Efectos originales (del asiento de la factura)
            List<VencimientoFactura> vencimientosOriginales = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 223.25M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 9, 19), FormaPago = "TRN", CCC = null },
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 10, 19), FormaPago = "RCB", CCC = "1" },
                new VencimientoFactura { Importe = 223.42M, ImportePendiente = 0, Vencimiento = new DateTime(2025, 11, 19), FormaPago = "RCB", CCC = "2" },
                new VencimientoFactura { Importe = 223.41M, ImportePendiente = 223.41M, Vencimiento = new DateTime(2025, 12, 19), FormaPago = "RCB", CCC = "2" }
            };
            A.CallTo(() => servicio.CargarVencimientosOriginales("1", A<string>.Ignored, "NV2515520")).Returns(vencimientosOriginales);

            // Impagados pendientes: efecto 3 devuelto con gastos
            List<ImpagadoPendiente> impagados = new List<ImpagadoPendiente>
            {
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 223.42M, EsGastos = false },
                new ImpagadoPendiente { FechaVto = new DateTime(2025, 11, 19), ImportePendiente = 3.00M, EsGastos = true }
            };
            A.CallTo(() => servicio.CargarImpagadosPendientes("1", A<string>.Ignored, "NV2515520")).Returns(impagados);

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV2515520");

            // Assert - 4 vencimientos: pagado, pagado, impagado, pendiente
            Assert.AreEqual(4, factura.Vencimientos.Count);

            // Efecto 1: TRN, pagado
            Assert.AreEqual(223.25M, factura.Vencimientos[0].Importe);
            Assert.AreEqual("Pagado", factura.Vencimientos[0].TextoPagado);
            Assert.IsFalse(factura.Vencimientos[0].EsImpagado);

            // Efecto 2: RCB, pagado
            Assert.AreEqual(223.42M, factura.Vencimientos[1].Importe);
            Assert.AreEqual("Pagado", factura.Vencimientos[1].TextoPagado);

            // Efecto 3: RCB, impagado con gastos
            Assert.IsTrue(factura.Vencimientos[2].EsImpagado);
            Assert.AreEqual(226.42M, factura.Vencimientos[2].Importe);
            Assert.AreEqual(3.00M, factura.Vencimientos[2].GastosImpagado);
            Assert.IsTrue(factura.Vencimientos[2].TextoPagado.Contains("Impagado"));
            Assert.IsTrue(factura.Vencimientos[2].TextoPagado.Contains("gastos"));

            // Efecto 4: RCB, pendiente
            Assert.AreEqual(223.41M, factura.Vencimientos[3].Importe);
            Assert.AreEqual(223.41M, factura.Vencimientos[3].ImportePendiente);
            Assert.IsFalse(factura.Vencimientos[3].EsImpagado);
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SinImpagados_FuncionaIgualQueAntes()
        {
            // Arrange - Caso sin impagados, un solo vencimiento
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2025, 1, 15),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 100,
                Producto = "12345",
                Base_Imponible = 82.64M,
                ImporteIVA = 17.36M,
                ImporteRE = 0,
                Total = 100,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV11111")).Returns(cab);

            List<VencimientoFactura> vencimientos = new List<VencimientoFactura>
            {
                new VencimientoFactura { Importe = 100, ImportePendiente = 100, Vencimiento = new DateTime(2025, 1, 15), FormaPago = "RCB", CCC = "10" }
            };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(vencimientos);
            // CargarImpagadosPendientes no configurado → devuelve null (FakeItEasy default)

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV11111");

            // Assert - Funciona como antes
            Assert.AreEqual(1, factura.Vencimientos.Count);
            Assert.AreEqual(100, factura.Vencimientos.First().Importe);
            Assert.AreEqual("Pendiente de pago", factura.Vencimientos.First().TextoPagado);
            Assert.IsFalse(factura.Vencimientos.First().EsImpagado);
        }

        #endregion

        #region Tests para Datos Fiscales Persistidos (Verifactu - Issue #88)

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConDatosFiscalesPersistidos_UsaDatosDeCabFactura()
        {
            // Arrange
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            // Datos fiscales persistidos en CabFacturaVta
            cab.NombreFiscal = "EMPRESA PERSISTIDA S.L.";
            cab.CifNif = "B12345678";
            cab.DireccionFiscal = "Calle Persistida 123";
            cab.CodPostalFiscal = "28001";
            cab.PoblacionFiscal = "Madrid";
            cab.ProvinciaFiscal = "Madrid";

            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 29),
                Cantidad = 1,
                Texto = "PRODUCTO TEST",
                Precio = 100,
                Producto = "12345",
                Base_Imponible = 100M,
                ImporteIVA = 21M,
                ImporteRE = 0,
                Total = 121,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV26/001234")).Returns(cab);

            // Cliente con datos DIFERENTES a los persistidos
            Cliente clientePrincipal = new Cliente
            {
                Nombre = "EMPRESA ACTUAL S.L.",  // Diferente
                CIF_NIF = "B99999999",           // Diferente
                Dirección = "Calle Actual 456",  // Diferente
                CodPostal = "28002",
                Población = "Barcelona",
                Provincia = "Barcelona",
                ClientePrincipal = true
            };
            A.CallTo(() => servicio.CargarCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);
            A.CallTo(() => servicio.CargarClientePrincipal(A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);

            VencimientoFactura vto = new VencimientoFactura { Importe = 121, ImportePendiente = 121 };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<VencimientoFactura> { vto });

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV26/001234");

            // Assert - Debe usar datos de CabFacturaVta, NO de Cliente
            var direccionFiscal = factura.Direcciones.FirstOrDefault(d => d.Tipo == "Fiscal");
            Assert.IsNotNull(direccionFiscal);
            Assert.AreEqual("EMPRESA PERSISTIDA S.L.", direccionFiscal.Nombre, "Debe usar NombreFiscal de CabFacturaVta");
            Assert.AreEqual("Calle Persistida 123", direccionFiscal.Direccion, "Debe usar DireccionFiscal de CabFacturaVta");
            Assert.AreEqual("28001", direccionFiscal.CodigoPostal, "Debe usar CodPostalFiscal de CabFacturaVta");
            Assert.AreEqual("Madrid", direccionFiscal.Poblacion, "Debe usar PoblacionFiscal de CabFacturaVta");
            Assert.AreEqual("B12345678", factura.Nif, "Debe usar CifNif de CabFacturaVta");
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_SinDatosFiscalesPersistidos_UsaDatosDeCliente()
        {
            // Arrange - Factura antigua sin datos fiscales persistidos
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            // Sin datos fiscales (NombreFiscal = null)
            cab.NombreFiscal = null;
            cab.CifNif = null;
            cab.DireccionFiscal = null;

            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2025, 6, 15),
                Cantidad = 1,
                Texto = "PRODUCTO ANTIGUO",
                Precio = 50,
                Producto = "54321",
                Base_Imponible = 50M,
                ImporteIVA = 10.50M,
                ImporteRE = 0,
                Total = 60.50M,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV25/000001")).Returns(cab);

            // Cliente actual
            Cliente clientePrincipal = new Cliente
            {
                Nombre = "CLIENTE ACTUAL S.L.",
                CIF_NIF = "B11111111",
                Dirección = "Calle Cliente 789",
                CodPostal = "28003",
                Población = "Valencia",
                Provincia = "Valencia",
                ClientePrincipal = true
            };
            A.CallTo(() => servicio.CargarCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);
            A.CallTo(() => servicio.CargarClientePrincipal(A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);

            VencimientoFactura vto = new VencimientoFactura { Importe = 60.50M, ImportePendiente = 60.50M };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<VencimientoFactura> { vto });

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV25/000001");

            // Assert - Debe usar datos de Cliente (fallback)
            var direccionFiscal = factura.Direcciones.FirstOrDefault(d => d.Tipo == "Fiscal");
            Assert.IsNotNull(direccionFiscal);
            Assert.AreEqual("CLIENTE ACTUAL S.L.", direccionFiscal.Nombre, "Debe usar nombre de Cliente");
            Assert.AreEqual("Calle Cliente 789", direccionFiscal.Direccion, "Debe usar direccion de Cliente");
            Assert.AreEqual("B11111111", factura.Nif, "Debe usar CIF_NIF de Cliente");
        }

        [TestMethod]
        public void GestorFacturas_LeerFactura_ConNombreFiscalVacio_UsaDatosDeCliente()
        {
            // Arrange - NombreFiscal es cadena vacia (equivalente a null)
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "1111";
            cab.Serie = "NV";
            cab.NombreFiscal = "   "; // Solo espacios

            LinPedidoVta linea = new LinPedidoVta
            {
                Nº_Albarán = 1,
                Fecha_Albarán = new DateTime(2026, 1, 29),
                Cantidad = 1,
                Texto = "PRODUCTO",
                Precio = 100,
                Producto = "11111",
                Base_Imponible = 100M,
                ImporteIVA = 21M,
                ImporteRE = 0,
                Total = 121,
                PorcentajeIVA = 21,
                PorcentajeRE = 0M
            };
            cab.LinPedidoVtas.Add(linea);
            A.CallTo(() => servicio.CargarCabFactura("1", "NV26/000002")).Returns(cab);

            Cliente clientePrincipal = new Cliente
            {
                Nombre = "CLIENTE FALLBACK",
                CIF_NIF = "B22222222",
                Dirección = "Calle Fallback",
                CodPostal = "28004",
                Población = "Sevilla",
                Provincia = "Sevilla",
                ClientePrincipal = true
            };
            A.CallTo(() => servicio.CargarCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);
            A.CallTo(() => servicio.CargarClientePrincipal(A<string>.Ignored, A<string>.Ignored)).Returns(clientePrincipal);

            VencimientoFactura vto = new VencimientoFactura { Importe = 121M, ImportePendiente = 121M };
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<VencimientoFactura> { vto });

            IGestorFacturas gestor = new GestorFacturas(servicio);

            // Act
            Factura factura = gestor.LeerFactura("1", "NV26/000002");

            // Assert - Debe usar fallback a Cliente
            var direccionFiscal = factura.Direcciones.FirstOrDefault(d => d.Tipo == "Fiscal");
            Assert.AreEqual("CLIENTE FALLBACK", direccionFiscal.Nombre);
        }

        #endregion
    }


}
