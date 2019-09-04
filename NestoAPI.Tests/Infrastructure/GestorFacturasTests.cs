using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
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
    }


}
