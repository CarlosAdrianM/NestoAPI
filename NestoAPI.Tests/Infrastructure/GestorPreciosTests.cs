using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using FakeItEasy;
using System.Collections.Generic;
using NestoAPI.Infraestructure.ValidadoresPedido;

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
                Grupo = "COS",
                SubGrupo = "COS"
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
            A.CallTo(() => servicio.BuscarProducto("OTROS_APA")).Returns(new Producto
            {
                Número = "OTROS_APA",
                PVP = 20,
                Grupo = "ACP",
                SubGrupo = "ACP"
            });
            A.CallTo(() => servicio.BuscarProducto("MUESTRA")).Returns(new Producto
            {
                Número = "MUESTRA",
                PVP = 1,
                Grupo = "COS",
                SubGrupo = "MMP"
            });
            A.CallTo(() => servicio.BuscarProducto("MUESTRA_2")).Returns(new Producto
            {
                Número = "MUESTRA_2",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "MMP"
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
            A.CallTo(() => servicio.BuscarOfertasPermitidas("OTROS_APA")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
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
                    CantidadMínima = 1,
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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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
            linea.baseImponible = 20;
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.IsFalse(respuesta.AutorizadaDenegadaExpresamente);
            //asertar el motivo para que no haya errores
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiLaOfertaEsANuestroFavorEsPermitidaPeroNoExpresa()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 11;
            linea.baseImponible = 22;
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.IsFalse(respuesta.AutorizadaDenegadaExpresamente);
            Assert.AreEqual("El producto AA11 no lleva oferta ni descuento", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiLaOfertaTieneClienteNoEsValidaParaTodosLosClientes()
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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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
            linea.precio = 100;
            linea.baseImponible = 400;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "FAMYPROD";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Hay un precio autorizado de 28,00 €", respuesta.Motivo);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoConSuficienteCantidadEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA21";
            linea.aplicarDescuento = true;
            linea.cantidad = 6;
            linea.precio = 5; // el de ficha es 21
            pedido.LineasPedido.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

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

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroNoEsValidaParaTodosLosProductos()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("SIN_FILTRO")).Returns(new Producto
            {
                Número = "SIN_FILTRO",
                Nombre = "ESTO ES UN PINTALABIOS ROJO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("SIN_FILTRO")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("SIN_FILTRO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "SIN_FILTRO";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 2;
            linea.baseImponible = 4;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "SIN_FILTRO";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            linea2.baseImponible = 0;
            pedido.LineasPedido.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorización para la oferta del producto SIN_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeCombinanProductosSigueSiendoValida()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("ROJO")).Returns(new Producto
            {
                Número = "ROJO",
                Nombre = "ESMALTE ROJO MARCA MUY BUENA",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("AZUL")).Returns(new Producto
            {
                Número = "AZUL",
                Nombre = "ESMALTE DE MARCA MUY BUENA AZUL",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("AZUL");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "ROJO";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 2;
            linea.baseImponible = 4;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "AZUL";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            linea2.baseImponible = 0;
            pedido.LineasPedido.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();
            A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> {linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 2+1 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeCombinanEnMultiplosProductosSigueSiendoValida()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("ROJO")).Returns(new Producto
            {
                Número = "ROJO",
                Nombre = "ESMALTE ROJO MARCA MUY BUENA",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("AZUL")).Returns(new Producto
            {
                Número = "AZUL",
                Nombre = "ESMALTE DE MARCA MUY BUENA AZUL",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("AZUL");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "ROJO";
            linea.aplicarDescuento = true;
            linea.cantidad = 5;
            linea.precio = 2;
            linea.baseImponible = 4;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "AZUL";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 2;
            linea2.precio = 0;
            linea2.baseImponible = 0;
            pedido.LineasPedido.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();
            A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 5+2 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYElProductoLoContieneEsValido()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("CON_FILTRO")).Returns(new Producto
            {
                Número = "CON_FILTRO",
                Nombre = "ESMALTE ROJO MUY BONITO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("CON_FILTRO")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("CON_FILTRO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "CON_FILTRO";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 2;
            linea.baseImponible = 4;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "CON_FILTRO";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            linea2.baseImponible = 0;
            pedido.LineasPedido.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Existe una oferta autorizada expresa de 2+1 del producto CON_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneFiltroNoEsValidoParaTodosLosProductos()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("SIN_FILTRO")).Returns(new Producto
            {
                Número = "SIN_FILTRO",
                Nombre = "ESTO ES UN PINTALABIOS ROJO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("SIN_FILTRO", "1", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Familia = "DeMarca",
                    Descuento = .2M,
                    FiltroProducto = "ESMALTE"
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("SIN_FILTRO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "SIN_FILTRO";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 2;
            linea.descuento = .2M;
            linea.baseImponible = 1.6M;
            pedido.LineasPedido.Add(linea);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 20,00 % para el producto SIN_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneCantidadMinimaEsValidoCuandoLaSupera()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("27095")).Returns(new Producto
            {
                Número = "27095",
                Nombre = "M2LASHES",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("27095", "1", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "27095",
                    Precio = 80,
                    CantidadMínima = 6
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("27095");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "27095";
            linea.aplicarDescuento = true;
            linea.cantidad = 7;
            linea.precio = 80;
            linea.baseImponible = 560M;
            pedido.LineasPedido.Add(linea);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Hay un precio autorizado de 80,00 €", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneCantidadMinimaNoEsValidoCuandoNoLaSupera()
        {
            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("27095")).Returns(new Producto
            {
                Número = "27095",
                Nombre = "M2LASHES",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("27095", "1", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "27095",
                    Precio = 80,
                    CantidadMínima = 6
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("27095");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "27095";
            linea.aplicarDescuento = true;
            linea.cantidad = 5;
            linea.precio = 80;
            linea.baseImponible = 400M;
            pedido.LineasPedido.Add(linea);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 20,00 % para el producto 27095", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlevaTodosLosProductosNoEsValido() {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 123,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA11",
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA62",
                            Precio = 31
                        },new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA21",
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLlevaTodosLosProductosEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA62";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 31; // es el de ficha
            pedido.LineasPedido.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 123,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA11",
                            Precio = 10,
                            Cantidad = 1
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA62",
                            Precio = 31,
                            Cantidad = 1
                        },new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA21",
                            Precio = 0,
                            Cantidad = 1
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA21");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 123 permite poner el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiElProductoNoEstaEnNingunaOfertaCombinadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(null);

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLosCobradosVanPorDebajoDeSuPrecioNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 8; // el de ficha es 10 y el de la oferta 9
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 123,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA11",
                            Precio = 9
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA21",
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlegaAlImporteMinimoNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            linea.baseImponible = 10;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.producto = "AA62";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 31; // es el de ficha
            linea2.baseImponible = 31;
            pedido.LineasPedido.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 1; // el de ficha es 21 €
            lineaRegalo.baseImponible = 1;
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 123,
                    Empresa = "NV",
                    ImporteMinimo = 43,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA11",
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA62",
                            Precio = 31
                        },new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA21",
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 123 tiene que tener un importe mínimo de 43,00 € para que sea válida", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlevaLaCantidadNecesariaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 125,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 2,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA21",
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto "+ respuesta.ProductoId +" a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLlevaMasCantidadDeRegaloDeLaPermitidaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 2;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 2;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 125,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 2,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA21",
                            Cantidad = 1,
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Está ofertando más cantidad de la permitida en el producto AA21 para que la oferta 125 sea válida", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLaCantidadDeRegaloEsMultiploDeLaPermitidaEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 4;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 2;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 125,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 2,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA21",
                            Cantidad = 1,
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA21");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 125 permite poner el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiHayMasDeUnaOfertaYlaSegundaLoApruebaEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            linea.baseImponible = 10;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            lineaRegalo.baseImponible = 0;
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 124,
                    Empresa = "NV",
                    ImporteMinimo = 11,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 124,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 1,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 124,
                            Empresa = "NV",
                            Producto = "AA21",
                            Cantidad = 1,
                            Precio = 0
                        }
                    }
                },
                new OfertaCombinada
                {
                    Id = 125,
                    Empresa = "NV",
                    ImporteMinimo = 9,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 1,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 125,
                            Empresa = "NV",
                            Producto = "AA21",
                            Cantidad = 1,
                            Precio = 0
                        }
                    }
                }

            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA21");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 125 permite poner el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraDeMenosValorDelPermitidoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            linea.baseImponible = 10;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra.producto = "MUESTRA";
            lineaMuestra.aplicarDescuento = true;
            lineaMuestra.cantidad = 1;
            lineaMuestra.precio = 1; // es el de ficha
            lineaMuestra.descuento = 1M; // de regalo, 100% dto
            lineaMuestra.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA puede ir a ese precio porque es material promocional y no se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraDeMasValorDelPermitidoNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            linea.baseImponible = 10;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra.producto = "MUESTRA_2";
            lineaMuestra.aplicarDescuento = true;
            lineaMuestra.cantidad = 1;
            lineaMuestra.precio = 2; // es el de ficha
            lineaMuestra.descuento = 1M; // de regalo, 100% dto
            lineaMuestra.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(2);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA_2");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA_2 no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaVariasMuestrasDeMasValorDelPermitidoNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            linea.baseImponible = 10;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra.producto = "MUESTRA";
            lineaMuestra.aplicarDescuento = true;
            lineaMuestra.cantidad = 1;
            lineaMuestra.precio = 1; // es el de ficha
            lineaMuestra.descuento = 1M; // de regalo, 100% dto
            lineaMuestra.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra2.producto = "MUESTRA_2";
            lineaMuestra2.aplicarDescuento = true;
            lineaMuestra2.cantidad = 1;
            lineaMuestra2.precio = 2; // es el de ficha
            lineaMuestra2.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaMuestra2);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(3);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA_2");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA_2 no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaVariasMuestrasDeMenosValorDelPermitidoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 30; 
            linea.baseImponible = 30;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra.producto = "MUESTRA";
            lineaMuestra.aplicarDescuento = true;
            lineaMuestra.cantidad = 1;
            lineaMuestra.precio = 1; // es el de ficha
            lineaMuestra.descuento = 1M; // de regalo, 100% dto
            lineaMuestra.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra2.producto = "MUESTRA_2";
            lineaMuestra2.aplicarDescuento = true;
            lineaMuestra2.cantidad = 1;
            lineaMuestra2.precio = 2; // es el de ficha
            lineaMuestra2.descuento = 1M; // de regalo, 100% dto
            lineaMuestra2.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra2);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA_2");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA_2 puede ir a ese precio porque es material promocional y no se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiNingunaMuestrasIndividualmenteSuperaElValorPermitidoPeroTodasJuntasSiNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 15; 
            linea.baseImponible = 15;
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra.producto = "MUESTRA";
            lineaMuestra.aplicarDescuento = true;
            lineaMuestra.cantidad = 1;
            lineaMuestra.precio = 1; // es el de ficha
            lineaMuestra.descuento = 1M; // de regalo, 100% dto
            lineaMuestra.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = A.Fake<LineaPedidoVentaDTO>();
            lineaMuestra2.producto = "MUESTRA_2";
            lineaMuestra2.aplicarDescuento = true;
            lineaMuestra2.cantidad = 1;
            lineaMuestra2.precio = 1; // el de ficha es 2
            lineaMuestra2.descuento = 1M; // de regalo, 100% dto
            lineaMuestra2.baseImponible = 0;
            pedido.LineasPedido.Add(lineaMuestra2);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(3);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }
        
        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiNoTieneNingunaLineaDeProductoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.numero = 1;
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 2;
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

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El pedido 1 no tiene ninguna línea de productos", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaNoAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
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
            linea.tipoLinea = 1;
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
            linea.tipoLinea = 1;
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
            linea.tipoLinea = 1;
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
        public void GestorPrecios_EsPedidoValido_SiHayUnValidadorDeActivacionEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "AA11";
            linea.aplicarDescuento = true;
            linea.cantidad = 1;
            linea.precio = 10; // es el de ficha
            pedido.LineasPedido.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = A.Fake<LineaPedidoVentaDTO>();
            lineaRegalo.tipoLinea = 1;
            lineaRegalo.producto = "AA21";
            lineaRegalo.aplicarDescuento = true;
            lineaRegalo.cantidad = 1;
            lineaRegalo.precio = 21; // es el de ficha
            lineaRegalo.descuento = 1M; // de regalo, 100% dto
            pedido.LineasPedido.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 143,
                    Empresa = "NV",
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 143,
                            Empresa = "NV",
                            Producto = "AA11",
                            Cantidad = 1,
                            Precio = 10
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 143,
                            Empresa = "NV",
                            Producto = "AA21",
                            Cantidad = 1,
                            Precio = 0
                        }
                    }
                }
            };
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 143 permite poner el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiHayOfertaParaLaFamiliaEsValidaParaElProductoSalvoOtrosAparatos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OTROS_APA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            linea.tipoLinea = 1;
            linea.producto = "OTROS_APA";
            linea.aplicarDescuento = true;
            linea.cantidad = 6;
            linea.precio = 130;
            pedido.LineasPedido.Add(linea);

            LineaPedidoVentaDTO linea2 = A.Fake<LineaPedidoVentaDTO>();
            linea2.tipoLinea = 1;
            linea2.producto = "OTROS_APA";
            linea2.aplicarDescuento = true;
            linea2.cantidad = 1;
            linea2.precio = 0;
            pedido.LineasPedido.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto OTROS_APA no puede llevar ningún descuento ni oferta porque es Otros Aparatos", respuesta.Motivo);
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
            
            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.producto, pedido);

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

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido("AA11", pedido);

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

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.producto, pedido);

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

            PrecioDescuentoProducto oferta = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(producto.Número, pedido);

            Assert.AreEqual(.1M, oferta.descuentoCalculado);
        }
    }
}
