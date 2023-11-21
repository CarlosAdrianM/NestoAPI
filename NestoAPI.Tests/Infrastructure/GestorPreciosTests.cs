using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using FakeItEasy;
using System.Collections.Generic;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models.PedidosVenta;

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
                SubGrupo = "001",
                Familia = "DeMarca"
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
            A.CallTo(() => servicio.BuscarProducto("FAM_DTO")).Returns(new Producto
            {
                Número = "FAM_DTO",
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
            A.CallTo(() => servicio.BuscarDescuentosPermitidos("FAM_DTO", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "FAM_DTO",
                    CantidadMínima = 2,
                    Descuento = .25M
                },
                new DescuentosProducto
                {
                    Familia = "DeMarca",
                    CantidadMínima = 0,
                    Descuento = .15M
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10;
            linea.DescuentoLinea = .1M;
            //linea.BaseImponible = 18; --> SI FALLA MIRAR ESTA LÍNEA
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA11";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Oferta no puede llevar descuento en el producto " + linea.Producto, respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnDescuentoAutorizadoEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 11; // el de ficha es 21
            //linea.BaseImponible = 11; --> SI FALLA MIRAR ESTA LÍNEA
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoTieneOfertaEsPermitidaPeroNoExpresa()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.IsFalse(respuesta.AutorizadaDenegadaExpresamente);
            //asertar el motivo para que no haya errores
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoEstaRedondeadoPeroEsSuPrecioEsPermitida()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10M;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 9.9999M;
            ////linea.BaseImponible = linea.Cantidad * linea.precio;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 11;
            ////linea.BaseImponible = 22;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_CLI1";
            linea.AplicarDescuento = true;
            linea.Cantidad = 4; //5+1 permitido para todos, 4+1 solo para el cliente 1
            linea.PrecioUnitario= 13;
            ////linea.BaseImponible = 4 * 13;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "OF_CLI1";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            ////linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_CLI1";
            linea.AplicarDescuento = true;
            linea.Cantidad = 3; //5+1 permitido para todos, 4+1 solo para el cliente 1, 3+1 solo contacto 2
            linea.PrecioUnitario = 13;
            //linea.BaseImponible = 3 * 13;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "OF_CLI1";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaEsValidaParaElProducto()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_FAMILIA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 6; 
            linea.PrecioUnitario = 130;
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "OF_FAMILIA";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaEsValidaParaProductosDelMismoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");

            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
            {
                Número = "MISMO_PRECIO",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("MISMO_PRECIO")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
                }
            });
            Producto mismoPrecio = GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO");

            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_FAMILIA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 6;
            linea.PrecioUnitario = 130;
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "MISMO_PRECIO";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(mismoPrecio, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaNoEsValidaParaProductosDeDistintoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");

            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
            {
                Número = "MISMO_PRECIO",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            Producto mismoPrecio = GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO");

            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_FAMILIA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 6;
            linea.PrecioUnitario = 130.01M ;
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "MISMO_PRECIO";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(mismoPrecio, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaYParaElProductoLaDelProductoPrevalece()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("FAMYPROD");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "FAMYPROD";
            linea.AplicarDescuento = true;
            linea.Cantidad = 4;
            linea.PrecioUnitario = 100;
            //linea.BaseImponible = 400;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "FAMYPROD";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "FAMYPROD";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 30;
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 70,00 % para el producto FAMYPROD", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoHayUnDescuentoValidoYLlevaDescuentoNoEsPermitida()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 5; // el de ficha es 21
            //linea.BaseImponible = 5;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 76,19 % para el producto AA21", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayDosDescuentosAutorizadosYCumpleUnoEsValido()
        {
            // El descuento por familia tiene cantidad mínima que sí se cumple y el del producto no
            Producto producto = GestorPrecios.servicio.BuscarProducto("FAM_DTO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "FAM_DTO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 100; // el de ficha es 100
            //linea.BaseImponible = 85;
            linea.DescuentoLinea = .15M;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA62";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 28; // el de ficha es 31
            //linea.BaseImponible = 28;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Hay un precio autorizado de 28,00 €", respuesta.Motivo);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoRedondeadoEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA62");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA62";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 27.9951M; // el de ficha es 31 y el autorizado 28
            //linea.BaseImponible = 27.9951M;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 6;
            linea.PrecioUnitario = 5; // el de ficha es 21
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "OF_FAMILIA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 30;
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoNoAfectaAProductosDelMismoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA62");

            A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
            {
                Número = "MISMO_PRECIO",
                PVP = 31,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });

            Producto mismoPrecio = GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO");

            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA62";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 28;
            //linea.BaseImponible = 28;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "MISMO_PRECIO";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 31;
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(mismoPrecio, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = false;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 9;
            //linea.BaseImponible = 18;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Oferta a precio inferior al de ficha en el producto " + linea.Producto, respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFichaSalvoQueSeaUnRegaloPermitido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("REGALO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "REGALO";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaPuedeIrAPrecioSuperiorAlDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = false;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 30;
            //linea.BaseImponible = 60;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferiores()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = false;
            linea.Cantidad = 3; // la oferta permitida es 2+1
            linea.PrecioUnitario = 30;
            //linea.BaseImponible = 90;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasYDescuentosPermitidos.EsOfertaPermitida(producto, pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferioresAunqueSeanMultiplos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = false;
            linea.Cantidad = 9; // la oferta permitida es 2+1
            linea.PrecioUnitario = 30;
            //linea.BaseImponible = 270;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 3;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 5; // se permite a partir de 6 unidades
            //linea.BaseImponible = 5;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 21; // es el de ficha
            linea.DescuentoLinea = .6M; // se permite a partir de 6 unidades
            //linea.BaseImponible = 8.4M;
            pedido.Lineas.Add(linea);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "SIN_FILTRO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 2;
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "SIN_FILTRO";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
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
                PVP = 2.1M,
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "ROJO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 2;
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "AZUL";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
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
                PVP = 1.9M,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("ROJO")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            }); A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "ROJO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 5;
            linea.PrecioUnitario = 2;
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "AZUL";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 2;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();
            A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 5+2 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeMeteUnaOfertaMenorSigueSiendoValida()
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
                PVP = 1,
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
                },
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 3,
                    CantidadRegalo = 1
                }
            });
            Producto producto = GestorPrecios.servicio.BuscarProducto("AZUL");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "ROJO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 3;
            linea.PrecioUnitario = 2;
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "AZUL";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 2;
            linea2.DescuentoLinea = 1.0M;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasYDescuentosPermitidos validador = new ValidadorOfertasYDescuentosPermitidos();
            A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 3+1 para el filtro de producto ESMALTE", respuesta.Motivo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "CON_FILTRO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 2;
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "CON_FILTRO";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "SIN_FILTRO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 2;
            linea.DescuentoLinea = .2M;
            //linea.BaseImponible = 1.6M;
            pedido.Lineas.Add(linea);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "27095";
            linea.AplicarDescuento = true;
            linea.Cantidad = 7;
            linea.PrecioUnitario = 80;
            //linea.BaseImponible = 560M;
            pedido.Lineas.Add(linea);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "27095";
            linea.AplicarDescuento = true;
            linea.Cantidad = 5;
            linea.PrecioUnitario = 80;
            //linea.BaseImponible = 400M;
            pedido.Lineas.Add(linea);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.Producto = "AA62";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 31; // es el de ficha
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            ////lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 8; // el de ficha es 10 y el de la oferta 9
            //linea.BaseImponible = 8;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
                            Precio = 9,
                            Cantidad = 1
                        },
                        new OfertaCombinadaDetalle
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.Producto = "AA62";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 31; // es el de ficha
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 1; // el de ficha es 21 €
            //lineaRegalo.BaseImponible = 1;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //linea.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 2;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 4;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 40;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 2;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlevaUnProductoDeCantidadCeroSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
                            Cantidad = 0
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

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 123 permite poner el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlevaNingunProductoDeLaOfertaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            //LineaPedidoVentaDTO linea = A.Fake<LineaPedidoVentaDTO>();
            //linea.Producto = "AA11";
            //linea.AplicarDescuento = true;
            //linea.Cantidad = 1;
            //linea.PrecioUnitario = 10; // es el de ficha
            //pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
                            Cantidad = 0
                        },
                        new OfertaCombinadaDetalle
                        {
                            OfertaId = 123,
                            Empresa = "NV",
                            Producto = "AA62",
                            Precio = 31,
                            Cantidad = 0
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

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto AA21 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraDeMenosValorDelPermitidoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.Producto = "MUESTRA";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 1;
            lineaMuestra.PrecioUnitario = 1; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.Producto = "MUESTRA_2";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 1;
            lineaMuestra.PrecioUnitario = 2; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(2);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA_2");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            //Assert.AreEqual("El producto MUESTRA_2 no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaVariasMuestrasDeMasValorDelPermitidoNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.Producto = "MUESTRA";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 1;
            lineaMuestra.PrecioUnitario = 1; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO();
            lineaMuestra2.Producto = "MUESTRA_2";
            lineaMuestra2.AplicarDescuento = true;
            lineaMuestra2.Cantidad = 1;
            lineaMuestra2.PrecioUnitario = 2; // es el de ficha
            lineaMuestra2.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra2.baseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(3);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA_2");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            //Assert.AreEqual("El producto MUESTRA_2 no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaVariasMuestrasDeMenosValorDelPermitidoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 30; 
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.Producto = "MUESTRA";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 1;
            lineaMuestra.PrecioUnitario = 1; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO();
            lineaMuestra2.Producto = "MUESTRA_2";
            lineaMuestra2.AplicarDescuento = true;
            lineaMuestra2.Cantidad = 1;
            lineaMuestra2.PrecioUnitario = 2; // es el de ficha
            lineaMuestra2.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra2.baseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 15; 
            //linea.BaseImponible = 15;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.Producto = "MUESTRA";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 1;
            lineaMuestra.PrecioUnitario = 1; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO();
            lineaMuestra2.Producto = "MUESTRA_2";
            lineaMuestra2.AplicarDescuento = true;
            lineaMuestra2.Cantidad = 1;
            lineaMuestra2.PrecioUnitario = 1; // el de ficha es 2
            lineaMuestra2.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra2.baseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);

            A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido,"COS","MMP")).Returns(3);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            //Assert.AreEqual("El producto MUESTRA no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }
        
        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraConMasUnidesDeLasPermitidasNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO();
            lineaMuestra.tipoLinea = 1;
            lineaMuestra.Producto = "MUESTRA";
            lineaMuestra.AplicarDescuento = true;
            lineaMuestra.Cantidad = 11;
            lineaMuestra.PrecioUnitario = .1M; // es el de ficha
            lineaMuestra.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(respuesta.Motivo, "No se encuentra autorizado el descuento del 100,00 % para el producto MUESTRA");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorRegaloPorImportePedido_SiLlegaAlImporteLoPuedeRegalar()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "COBRADO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "REGALO";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 1; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
            {
                new RegaloImportePedido
                {
                    Empresa = "1",
                    Cantidad = 1,
                    ImportePedido = 10M,
                    Producto = "REGALO"
                }
            });

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "REGALO");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto REGALO puede ir a ese precio porque es un regalo autorizado para pedidos de este importe", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorRegaloPorImportePedido_SiNoLlegaAlImporteNoLoPuedeRegalar()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "COBRADO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 9; // es el de ficha
            //linea.BaseImponible = 9;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "REGALO";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 1; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
            {
                new RegaloImportePedido
                {
                    Empresa = "1",
                    Cantidad = 1,
                    ImportePedido = 10M,
                    Producto = "REGALO"
                }
            });

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "REGALO");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto REGALO no puede ir a ese precio porque no es un regalo autorizado para pedidos de este importe", respuesta.Motivo);
        }


        [TestMethod]
        public void GestorPrecios_ValidadorRegaloPorImportePedido_SiLlegaAlImporteSoloPuedeRegalarLaCantidadPermitida()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "COBRADO";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.Producto = "REGALO";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 2;
            lineaRegalo.PrecioUnitario = 1; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
            {
                new RegaloImportePedido
                {
                    Empresa = "1",
                    Cantidad = 1,
                    ImportePedido = 10M,
                    Producto = "REGALO"
                }
            });

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "REGALO");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto REGALO no puede ir a ese precio porque no es un regalo autorizado para pedidos de este importe", respuesta.Motivo);
        }


        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiNoTieneNingunaLineaDeProductoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.numero = 1;
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 2;
            linea.Producto = "123144AAAAA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.DescuentoLinea = .05M;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 9.5M;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El pedido 1 no tiene ninguna línea de productos", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaNoAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "123144AAAAA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.DescuentoLinea = .05M;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 9.5M;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaExpresamenteAutorizadaSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 21;
            //linea.BaseImponible = 42;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaEsMultiploDeUnaExpresamenteAutorizadaSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 10;
            linea.PrecioUnitario = 21;
            //linea.BaseImponible = 210;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 5;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }
        
        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaQueEsMultiploInferiorDeUnaExpresamenteAutorizadaNoEsValido()
        {
            // Lo que queremos probar es que si está autorizado el 6+2, no tiene por qué estarlo el 3+1
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA62";
            linea.AplicarDescuento = true;
            linea.Cantidad = 3;
            linea.PrecioUnitario = 31;
            //linea.BaseImponible = 93;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.Producto = "AA62";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaPeroNoEsMultiploDeUnaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA21";
            linea.AplicarDescuento = true;
            linea.Cantidad = 11;
            linea.PrecioUnitario = 15;
            //linea.BaseImponible = 11 * 15;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.Producto = "AA21";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 5;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiNoCumpleLaNormaYNoLlevaUnaOfertaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 15;
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.Producto = "AA11";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiTodasLasOfertasEstanAutorizadasEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "123144AAAAA";
            linea.AplicarDescuento = false;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            linea.tipoLinea = 1;
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; // es el de ficha
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO();
            lineaRegalo.tipoLinea = 1;
            lineaRegalo.Producto = "AA21";
            lineaRegalo.AplicarDescuento = true;
            lineaRegalo.Cantidad = 1;
            lineaRegalo.PrecioUnitario = 21; // es el de ficha
            lineaRegalo.DescuentoLinea = 1M; // de regalo, 100% dto
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = 1;
            linea.Producto = "OTROS_APA";
            linea.AplicarDescuento = true;
            linea.Cantidad = 6;
            linea.PrecioUnitario = 130;
            //linea.BaseImponible = 780;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = 1;
            linea2.Producto = "OTROS_APA";
            linea2.AplicarDescuento = true;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto OTROS_APA no puede llevar ningún descuento ni oferta porque es Otros Aparatos", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiSoloHayUnaLineaDevuelveSusDatos()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = false;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 6; //El precio de ficha son 10
            //linea.BaseImponible = 6;
            pedido.Lineas.Add(linea);
            
            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(1, precioDescuentoProducto.cantidad);
            Assert.AreEqual(.4M, precioDescuentoProducto.descuentoReal);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayDosDescuentosLosSumaEncadenados()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = false;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10; //El precio de ficha son 10
            linea.DescuentoLinea = .5M;
            linea.DescuentoProducto = .5M;
            //linea.BaseImponible = linea.PrecioUnitario * linea.Cantidad; //para el test vale
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(1, precioDescuentoProducto.cantidad);
            Assert.AreEqual(.75M, precioDescuentoProducto.descuentoReal);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiNoHayNingunaLineaDeEseProductoDevuelveNull()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.Producto = "AA12";
            linea.AplicarDescuento = false;
            linea.Cantidad = 1;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido("AA11", pedido);

            Assert.IsNull(precioDescuentoProducto);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayUnaLineaDeRegaloLaPoneEnCantidadOferta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = false;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "AA11";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(1, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(2, precioDescuentoProducto.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiLaLineaLlevaDescuentoLaOfertaTambien()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA11");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "AA11";
            linea.AplicarDescuento = true;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10;
            linea.DescuentoLinea = .1M;
            //linea.BaseImponible = 18;
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto oferta = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(producto.Número, pedido);

            Assert.AreEqual(.1M, oferta.descuentoCalculado);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayUnaLineaDelMismoPrecioLaPoneEnLaOferta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO();
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = "FAMYPROD";
            linea.AplicarDescuento = false;
            linea.Cantidad = 2;
            linea.PrecioUnitario = 10;
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO();
            linea2.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea2.Producto = "REGALO";
            linea2.AplicarDescuento = false;
            linea2.Cantidad = 1;
            linea2.PrecioUnitario = 0;
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            PrecioDescuentoProducto precioDescuentoProducto = ValidadorOfertasYDescuentosPermitidos.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(1, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(2, precioDescuentoProducto.cantidad);
        }

    }
}
