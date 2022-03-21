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
    }


}
