using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using FakeItEasy;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    [TestClass]
    public class GestorPreciosTests
    {
        public GestorPreciosTests()
        {
            // Configuramos el fake del servicio para que nos devuelva datos
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto("AA11")).Returns(new Producto {
                Número = "AA11",
                PVP = 10,
                Grupo = "ACP",
                SubGrupo = "ACP"
            });
            A.CallTo(() => servicio.BuscarProducto("AA21")).Returns(new Producto
            {
                Número = "AA21",
                PVP = 21,
                Grupo = "ACP",
                SubGrupo = "ACP"
            });
            A.CallTo(() => servicio.BuscarProducto("AA62")).Returns(new Producto
            {
                Número = "AA62",
                PVP = 31,
                Grupo = "COS",
                SubGrupo = "001"
            });
            A.CallTo(() => servicio.BuscarProducto("OF_CLI1")).Returns(new Producto
            {
                Número = "OF_CLI1",
                PVP = 13,
                Grupo = "COS",
                SubGrupo = "001"
            });
            A.CallTo(() => servicio.BuscarProducto("OF_FAMILIA")).Returns(new Producto
            {
                Número = "OF_FAMILIA",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            A.CallTo(() => servicio.BuscarProducto("FAMYPROD")).Returns(new Producto
            {
                Número = "FAMYPROD",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });
            A.CallTo(() => servicio.BuscarProducto("REGALO")).Returns(new Producto
            {
                Número = "REGALO",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });

            A.CallTo(() => servicio.BuscarOfertasPermitidas("AA21")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Número= "AA21",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1
                }
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("AA62")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Número= "AA62",
                    CantidadConPrecio = 6,
                    CantidadRegalo = 2
                }
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("OF_CLI1")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Número= "OF_CLI1",
                    CantidadConPrecio = 5,
                    CantidadRegalo = 1
                },
                new OfertaPermitida
                {
                    Número = "OF_CLI1",
                    CantidadConPrecio = 4,
                    CantidadRegalo = 1,
                    Cliente = "1"
                },
                new OfertaPermitida
                {
                    Número = "OF_CLI1",
                    CantidadConPrecio = 3,
                    CantidadRegalo = 1,
                    Cliente = "1",
                    Contacto = "2"
                }

            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("OF_FAMILIA")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
                }
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("FAMYPROD")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 4,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
                }, 
                new OfertaPermitida
                {
                    Número = "FAMYPROD",
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                }
            });

            A.CallTo(() => servicio.BuscarDescuentosPermitidos("AA21","5","0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "AA21",
                    CantidadMínima = 0,
                    Descuento = .5M
                },
                new DescuentosProducto
                {
                    Nº_Producto = "AA21",
                    CantidadMínima = 6,
                    Precio = 5
                },
                new DescuentosProducto
                {
                    Nº_Producto = "AA21",
                    CantidadMínima = 6,
                    Descuento = .6M
                }
            });
            A.CallTo(() => servicio.BuscarDescuentosPermitidos("AA62", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "AA62",
                    CantidadMínima = 0,
                    Precio = 28
                }
            });
            A.CallTo(() => servicio.BuscarDescuentosPermitidos("OF_FAMILIA", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Familia = "DeMarca",
                    CantidadMínima = 0,
                    Precio = 25
                }
            });
            A.CallTo(() => servicio.BuscarDescuentosPermitidos("FAMYPROD", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "FAMYPROD",
                    CantidadMínima = 0,
                    Precio = 50
                },
                new DescuentosProducto
                {
                    Familia = "DeMarca",
                    CantidadMínima = 0,
                    Precio = 25
                }
            });
            A.CallTo(() => servicio.BuscarDescuentosPermitidos("REGALO", null, null)).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "REGALO",
                    CantidadMínima = 0,
                    Descuento = 1
                }
            });

            GestorPrecios.servicio = servicio;
        }

        [TestMethod]
        public void GestorPrecios_ComprobarCondiciones_OtrosAparatosNoPuedeLlevarDescuento()
        {
            // Arrange
            Producto producto = A.Fake<Producto>();
            producto.Número = "111AAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 100;
            producto.Familia = "Carlos";
            PrecioDescuentoProducto precioDescuentoProducto = A.Fake<PrecioDescuentoProducto>();
            precioDescuentoProducto.producto = producto;
            precioDescuentoProducto.cantidad = 1;
            precioDescuentoProducto.descuentosRellenos = true;
            precioDescuentoProducto.aplicarDescuento = true;
            precioDescuentoProducto.descuentoCalculado = 0.01M;// M = double
            precioDescuentoProducto.precioCalculado = 100M; 

            // Act
            Boolean sePuedeHacer = GestorPrecios.comprobarCondiciones(precioDescuentoProducto);

            // Assert
            Assert.IsFalse(sePuedeHacer);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaYDescuentoDaError()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 10;
            linea.descuento = .1M;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA11";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Oferta no puede llevar descuento en el producto " + linea.producto, respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoTieneOfertaEsPermitidaPeroNoExpresa()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 10;
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.IsFalse(respuesta.OfertaAutorizadaExpresamente);
            //asertar el motivo para que no haya errores
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiLaOfertaTieneClienteNoEsValidaParaTodos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_CLI1");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "2"; // la oferta solo es válida para el 1
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "OF_CLI1";
            linea.aplicarDescuento = true;
            linea.cantidad = 4; //5+1 permitido para todos, 4+1 solo para el cliente 1
            linea.precio = 13;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "OF_CLI1";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiLaOfertaTieneContactoNoEsValidaParaTodos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_CLI1");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0"; // la oferta solo es válida para el 2
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "OF_CLI1";
            linea.aplicarDescuento = true;
            linea.cantidad = 3; //5+1 permitido para todos, 4+1 solo para el cliente 1, 3+1 solo contacto 2
            linea.precio = 13;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "OF_CLI1";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaEsValidaParaElProducto()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "OF_FAMILIA";
            linea.aplicarDescuento = true;
            linea.cantidad = 6; 
            linea.precio = 130;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "OF_FAMILIA";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaYParaElProductoLaDelProductoPrevalece()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("FAMYPROD");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "FAMYPROD";
            linea.aplicarDescuento = true;
            linea.cantidad = 4;
            linea.precio = 130;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "FAMYPROD";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta máxima para el producto FAMYPROD es el 6+1", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayDescuentoParaLaFamiliaYParaElProductoElDelProductoPrevalece()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("FAMYPROD");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "FAMYPROD";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 30;
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 70,00 % para el producto FAMYPROD", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoHayUnDescuentoValidoYLlevaDescuentoNoEsPermitida()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 5; // el de ficha es 21
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 76,19 % para el producto AA21", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnDescuentoAutorizadoEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 11; // el de ficha es 21
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA62");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA62";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 28; // el de ficha es 31
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Hay un precio autorizado de 28,00 €", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoParaLaFamiliaEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "OF_FAMILIA";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 30;
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = false;
            linea.cantidad = 2;
            linea.precio = 9;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA21";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Oferta a precio inferior al de ficha en el producto " + linea.producto, respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFichaSalvoQueSeaUnRegaloPermitido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("REGALO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "REGALO";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaPuedeIrAPrecioSuperiorAlDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = false;
            linea.cantidad = 2;
            linea.precio = 30;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA21";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferiores()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = false;
            linea.cantidad = 3; // la oferta permitida es 2+1
            linea.precio = 30;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA21";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferioresAunqueSeanMultiplos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = false;
            linea.cantidad = 9; // la oferta permitida es 2+1
            linea.precio = 30;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA21";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 3;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneCantidadMinimaYPrecioFijoNoSiempreEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 5; // se permite a partir de 6 unidades
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneCantidadMinimaYDescuentoNoSiempreEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 21; // es el de ficha
            linea.descuento = .6M; // se permite a partir de 6 unidades
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaNoAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "123144AAAAA";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.descuento = .05M;
            linea.precio = 10;
            pedido.LineasPedido.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            A.CallTo(() => servicio.BuscarProducto(linea.producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaExpresamenteAutorizadaSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 21;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea2.producto = "AA21";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaEsMultiploDeUnaExpresamenteAutorizadaSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 10;
            linea.precio = 21;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "AA21";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 5;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }
        
        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaQueEsMultiploInferiorDeUnaExpresamenteAutorizadaNoEsValido()
        {
            // Lo que queremos probar es que si está autorizado el 6+2, no tiene por qué estarlo el 3+1
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA62";
            linea.aplicarDescuento = true;
            linea.cantidad = 3;
            linea.precio = 31;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA62";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaPeroNoEsMultiploDeUnaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 11;
            linea.precio = 15;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA21";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 5;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiNoCumpleLaNormaYNoLlevaUnaOfertaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 15;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA11";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiTodasLasOfertasEstanAutorizadasEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "123144AAAAA";
            linea.aplicarDescuento = false;
            linea.cantidad = 1;
            linea.precio = 10;
            pedido.LineasPedido.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            linea.tipoLinea = 1;
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            A.CallTo(() => servicio.BuscarProducto(linea.producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiSoloHayUnaLineaDevuelveSusDatos()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = false;
            linea.cantidad = 1;
            linea.precio = 6; //El precio de ficha son 10
            pedido.LineasPedido.Add(linea);
            
            PrecioDescuentoProducto precioDescuentoProducto = GestorPrecios.MontarOfertaPedido(linea.producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(1, precioDescuentoProducto.cantidad);
            Assert.AreEqual(.4M, precioDescuentoProducto.descuentoReal);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiNoHayNingunaLineaDeEseProductoDevuelveNull()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA12";
            linea.aplicarDescuento = false;
            linea.cantidad = 1;
            linea.precio = 10;
            pedido.LineasPedido.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = GestorPrecios.MontarOfertaPedido("AA11", pedido);

            Assert.IsNull(precioDescuentoProducto);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayUnaLineaDeRegaloLaPoneEnCantidadOferta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = false;
            linea.cantidad = 2;
            linea.precio = 10;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA11";
            linea2.aplicarDescuento = false;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            PrecioDescuentoProducto precioDescuentoProducto = GestorPrecios.MontarOfertaPedido(linea.producto, pedido);

            Assert.AreEqual(1, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(2, precioDescuentoProducto.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiLaLineaLlevaDescuentoLaOfertaTambien()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA11");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 10;
            linea.descuento = .1M;
            pedido.LineasPedido.Add(linea);

            PrecioDescuentoProducto oferta = GestorPrecios.MontarOfertaPedido(producto.Número, pedido);

            Assert.AreEqual(.1M, oferta.descuentoCalculado);
        }
    }
}
