using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
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
            _ = A.CallTo(() => servicio.BuscarProducto("AA11")).Returns(new Producto
            {
                Número = "AA11",
                PVP = 10,
                Grupo = "ACP",
                SubGrupo = "ACP"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("AA21")).Returns(new Producto
            {
                Número = "AA21",
                PVP = 21,
                Grupo = "COS",
                SubGrupo = "COS"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("AA62")).Returns(new Producto
            {
                Número = "AA62",
                PVP = 31,
                Grupo = "COS",
                SubGrupo = "001",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("OF_CLI1")).Returns(new Producto
            {
                Número = "OF_CLI1",
                PVP = 13,
                Grupo = "COS",
                SubGrupo = "001"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("OF_FAMILIA")).Returns(new Producto
            {
                Número = "OF_FAMILIA",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("FAMYPROD")).Returns(new Producto
            {
                Número = "FAMYPROD",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("FAM_DTO")).Returns(new Producto
            {
                Número = "FAM_DTO",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("REGALO")).Returns(new Producto
            {
                Número = "REGALO",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("OTROS_APA")).Returns(new Producto
            {
                Número = "OTROS_APA",
                PVP = 20,
                Grupo = "ACP",
                SubGrupo = "ACP"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("MUESTRA")).Returns(new Producto
            {
                Número = "MUESTRA",
                PVP = 1,
                Grupo = "COS",
                SubGrupo = "MMP"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("MUESTRA_2")).Returns(new Producto
            {
                Número = "MUESTRA_2",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "MMP"
            });

            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("AA21")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Número= "AA21",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1
                }
            });
            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("AA62")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Número= "AA62",
                    CantidadConPrecio = 6,
                    CantidadRegalo = 2
                }
            });
            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("OF_CLI1")).Returns(new List<OfertaPermitida>
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
            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("OF_FAMILIA")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
                }
            });
            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("FAMYPROD")).Returns(new List<OfertaPermitida>
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
            _ = A.CallTo(() => servicio.BuscarOfertasPermitidas("OTROS_APA")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    CantidadConPrecio = 6,
                    CantidadRegalo = 1,
                    Familia = "DeMarca"
                }
            });
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("AA21", "5", "0")).Returns(new List<DescuentosProducto>
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
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("AA62", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Nº_Producto = "AA62",
                    CantidadMínima = 0,
                    Precio = 28
                }
            });
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("OF_FAMILIA", "5", "0")).Returns(new List<DescuentosProducto>
            {
                new DescuentosProducto
                {
                    Familia = "DeMarca",
                    CantidadMínima = 0,
                    Precio = 25
                }
            });
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("FAMYPROD", "5", "0")).Returns(new List<DescuentosProducto>
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
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("FAM_DTO", "5", "0")).Returns(new List<DescuentosProducto>
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
            _ = A.CallTo(() => servicio.BuscarDescuentosPermitidos("REGALO", null, null)).Returns(new List<DescuentosProducto>
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
            bool sePuedeHacer = GestorPrecios.comprobarCondiciones(precioDescuentoProducto);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10,
                DescuentoLinea = .1M
            };
            //linea.BaseImponible = 18; --> SI FALLA MIRAR ESTA LÍNEA
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 11 // el de ficha es 21
            };
            //linea.BaseImponible = 11; --> SI FALLA MIRAR ESTA LÍNEA
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoTieneOfertaEsPermitidaPeroNoExpresa()
        {
            Producto producto = A.Fake<Producto>();
            producto.Número = "AA11";
            producto.PVP = 10;
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 9.9999M
            };
            ////linea.BaseImponible = linea.Cantidad * linea.precio;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 11
            };
            ////linea.BaseImponible = 22;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_CLI1",
                AplicarDescuento = true,
                Cantidad = 4, //5+1 permitido para todos, 4+1 solo para el cliente 1
                PrecioUnitario = 13
            };
            ////linea.BaseImponible = 4 * 13;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_CLI1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            ////linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiLaOfertaTieneContactoNoEsValidaParaTodos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_CLI1");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0"; // la oferta solo es válida para el 2
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_CLI1",
                AplicarDescuento = true,
                Cantidad = 3, //5+1 permitido para todos, 4+1 solo para el cliente 1, 3+1 solo contacto 2
                PrecioUnitario = 13
            };
            //linea.BaseImponible = 3 * 13;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_CLI1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaEsValidaParaElProducto()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 130
            };
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaEsValidaParaProductosDelMismoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
            {
                Número = "MISMO_PRECIO",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("MISMO_PRECIO")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 130
            };
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "MISMO_PRECIO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(mismoPrecio, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaNoEsValidaParaProductosDeDistintoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
            {
                Número = "MISMO_PRECIO",
                PVP = 130,
                Grupo = "APA",
                SubGrupo = "APA",
                Familia = "DeMarca"
            });
            Producto mismoPrecio = GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO");

            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 130.01M
            };
            //linea.BaseImponible = 960;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "MISMO_PRECIO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(mismoPrecio, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayOfertaParaLaFamiliaYParaElProductoLaDelProductoPrevalece()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("FAMYPROD");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = true,
                Cantidad = 4,
                PrecioUnitario = 100
            };
            //linea.BaseImponible = 400;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 30
            };
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 70,00 % para el producto FAMYPROD", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiNoHayUnDescuentoValidoYLlevaDescuentoNoEsPermitida()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 5 // el de ficha es 21
            };
            //linea.BaseImponible = 5;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAM_DTO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 100, // el de ficha es 100
                                      //linea.BaseImponible = 85;
                DescuentoLinea = .15M
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA62");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 28 // el de ficha es 31
            };
            //linea.BaseImponible = 28;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 27.9951M // el de ficha es 31 y el autorizado 28
            };
            //linea.BaseImponible = 27.9951M;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 5 // el de ficha es 21
            };
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoParaLaFamiliaEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("OF_FAMILIA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 30
            };
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiHayUnPrecioFijoAutorizadoNoAfectaAProductosDelMismoPrecio()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA62");

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MISMO_PRECIO")).Returns(new Producto
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 28
            };
            //linea.BaseImponible = 28;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "MISMO_PRECIO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 31
            };
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(mismoPrecio, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }


        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 9
            };
            //linea.BaseImponible = 18;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("Oferta a precio inferior al de ficha en el producto " + linea.Producto, respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaTieneQueIrAlPrecioDeFichaSalvoQueSeaUnRegaloPermitido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("REGALO");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "REGALO",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaPuedeIrAPrecioSuperiorAlDeFicha()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 30
            };
            //linea.BaseImponible = 60;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferiores()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 3, // la oferta permitida es 2+1
                PrecioUnitario = 30
            };
            //linea.BaseImponible = 90;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneOfertaSonValidasTodasLasOfertasInferioresAunqueSeanMultiplos()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 9, // la oferta permitida es 2+1
                PrecioUnitario = 30
            };
            //linea.BaseImponible = 270;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = false,
                Cantidad = 3,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(producto, pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneCantidadMinimaYPrecioFijoNoSiempreEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 5 // se permite a partir de 6 unidades
            };
            //linea.BaseImponible = 5;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsOfertaPermitida_SiTieneCantidadMinimaYDescuentoNoSiempreEsValido()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = .6M // se permite a partir de 6 unidades
            };
            //linea.BaseImponible = 8.4M;
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroNoEsValidaParaTodosLosProductos()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("SIN_FILTRO")).Returns(new Producto
            {
                Número = "SIN_FILTRO",
                Nombre = "ESTO ES UN PINTALABIOS ROJO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("SIN_FILTRO")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "SIN_FILTRO",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 2
            };
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "SIN_FILTRO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorización para la oferta del producto SIN_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeCombinanProductosSigueSiendoValida()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("ROJO")).Returns(new Producto
            {
                Número = "ROJO",
                Nombre = "ESMALTE ROJO MARCA MUY BUENA",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("AZUL")).Returns(new Producto
            {
                Número = "AZUL",
                Nombre = "ESMALTE DE MARCA MUY BUENA AZUL",
                PVP = 2.1M,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "ROJO",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 2
            };
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AZUL",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();
            _ = A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 2+1 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeCombinanEnMultiplosProductosSigueSiendoValida()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("ROJO")).Returns(new Producto
            {
                Número = "ROJO",
                Nombre = "ESMALTE ROJO MARCA MUY BUENA",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("AZUL")).Returns(new Producto
            {
                Número = "AZUL",
                Nombre = "ESMALTE DE MARCA MUY BUENA AZUL",
                PVP = 1.9M,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("ROJO")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida
                {
                    Familia = "DeMarca",
                    CantidadConPrecio = 2,
                    CantidadRegalo = 1,
                    FiltroProducto = "ESMALTE"
                }
            }); _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "ROJO",
                AplicarDescuento = true,
                Cantidad = 5,
                PrecioUnitario = 2
            };
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AZUL",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();
            _ = A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 5+2 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYSeMeteUnaOfertaMenorSigueSiendoValida()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("ROJO")).Returns(new Producto
            {
                Número = "ROJO",
                Nombre = "ESMALTE ROJO MARCA MUY BUENA",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("AZUL")).Returns(new Producto
            {
                Número = "AZUL",
                Nombre = "ESMALTE DE MARCA MUY BUENA AZUL",
                PVP = 1,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("AZUL")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "ROJO",
                AplicarDescuento = true,
                Cantidad = 3,
                PrecioUnitario = 2
            };
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AZUL",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 2,
                DescuentoLinea = 1.0M
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();
            _ = A.CallTo(() => GestorPrecios.servicio.FiltrarLineas(pedido, "ESMALTE", "DeMarca")).Returns(new List<LineaPedidoVentaDTO> { linea, linea2 });

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Se permite el 3+1 para el filtro de producto ESMALTE", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiLaOfertaTieneFiltroYElProductoLoContieneEsValido()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("CON_FILTRO")).Returns(new Producto
            {
                Número = "CON_FILTRO",
                Nombre = "ESMALTE ROJO MUY BONITO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("CON_FILTRO")).Returns(new List<OfertaPermitida>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "CON_FILTRO",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 2
            };
            //linea.BaseImponible = 4;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "CON_FILTRO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Existe una oferta autorizada expresa de 2+1 del producto CON_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneFiltroNoEsValidoParaTodosLosProductos()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("SIN_FILTRO")).Returns(new Producto
            {
                Número = "SIN_FILTRO",
                Nombre = "ESTO ES UN PINTALABIOS ROJO",
                PVP = 2,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("SIN_FILTRO", "1", "0")).Returns(new List<DescuentosProducto>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "SIN_FILTRO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 2,
                DescuentoLinea = .2M
            };
            //linea.BaseImponible = 1.6M;
            pedido.Lineas.Add(linea);
            ValidadorDescuentosPermitidos validador = new ValidadorDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 20,00 % para el producto SIN_FILTRO", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneCantidadMinimaEsValidoCuandoLaSupera()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("27095")).Returns(new Producto
            {
                Número = "27095",
                Nombre = "M2LASHES",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("27095", "1", "0")).Returns(new List<DescuentosProducto>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "27095",
                AplicarDescuento = true,
                Cantidad = 7,
                PrecioUnitario = 80
            };
            //linea.BaseImponible = 560M;
            pedido.Lineas.Add(linea);
            ValidadorDescuentosPermitidos validador = new ValidadorDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("Hay un precio autorizado de 80,00 €", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_SiElDescuentoTieneCantidadMinimaNoEsValidoCuandoNoLaSupera()
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("27095")).Returns(new Producto
            {
                Número = "27095",
                Nombre = "M2LASHES",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "COS",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("27095", "1", "0")).Returns(new List<DescuentosProducto>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "27095",
                AplicarDescuento = true,
                Cantidad = 5,
                PrecioUnitario = 80
            };
            //linea.BaseImponible = 400M;
            pedido.Lineas.Add(linea);
            ValidadorDescuentosPermitidos validador = new ValidadorDescuentosPermitidos();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 20,00 % para el producto 27095", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiNoLlevaTodosLosProductosNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 31 // es el de ficha
            };
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            ////lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA21");
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(null);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 8 // el de ficha es 10 y el de la oferta 9
            };
            //linea.BaseImponible = 8;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 31 // es el de ficha
            };
            //linea2.BaseImponible = 31;
            pedido.Lineas.Add(linea2);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1 // el de ficha es 21 €
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto " + respuesta.ProductoId + " a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLlevaMasCantidadDeRegaloDeLaPermitidaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 4,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 40;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10, // es el de ficha
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El producto MUESTRA puede ir a ese precio porque es material promocional y no se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLosProductosSonAparatosNoEsValido()
        {
            // Este test es igual a GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraDeMenosValorDelPermitidoEsValido pero con aparatos en vez de cosmética
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10, // es el de ficha
                GrupoProducto = Constantes.Productos.GRUPO_APARATOS
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraDeMasValorDelPermitidoNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10, // es el de ficha
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA_2",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 2, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO
            {
                Producto = "MUESTRA_2",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 2, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra2.baseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto + lineaMuestra2.Bruto);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            //Assert.AreEqual("El producto MUESTRA no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaUnaMuestraConMasUnidesDeLasPermitidasNoEsValido()
        {
            // Estamos regalando 11 muestras, cuando el máximo es 10.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 100,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 11, // el máximo permitido es 10
                PrecioUnitario = 1,
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No se encuentra autorizado el descuento del 100,00 % para el producto MUESTRA", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorRegaloPorImportePedido_SiLlegaAlImporteLoPuedeRegalar()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "COBRADO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "REGALO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "COBRADO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 9 // es el de ficha
            };
            //linea.BaseImponible = 9;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "REGALO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "COBRADO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                Producto = "REGALO",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaRegalo.BaseImponible = 0;
            pedido.Lineas.Add(lineaRegalo);
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarRegaloPorImportePedido("REGALO")).Returns(new List<RegaloImportePedido>
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 2,
                Producto = "123144AAAAA",
                AplicarDescuento = true,
                Cantidad = 1,
                DescuentoLinea = .05M,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 9.5M;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            _ = A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("El pedido 1 no tiene ninguna línea de productos", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaNoAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "123144AAAAA",
                AplicarDescuento = true,
                Cantidad = 1,
                DescuentoLinea = .05M,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 9.5M;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            _ = A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
            GestorPrecios.servicio = servicio;

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaExpresamenteAutorizadaSiEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 21
            };
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 10,
                PrecioUnitario = 21
            };
            //linea.BaseImponible = 210;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 5,
                PrecioUnitario = 0
            };
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 3,
                PrecioUnitario = 31
            };
            //linea.BaseImponible = 93;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                Producto = "AA62",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiLlevaUnaOfertaPeroNoEsMultiploDeUnaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 11,
                PrecioUnitario = 15
            };
            //linea.BaseImponible = 11 * 15;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 5,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiNoCumpleLaNormaYNoLlevaUnaOfertaExpresamenteAutorizadaNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 15
            };
            //linea.BaseImponible = 30;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiTodasLasOfertasEstanAutorizadasEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "123144AAAAA",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            Producto producto = A.Fake<Producto>();
            linea.tipoLinea = 1;
            producto.Número = "123144AAAAA";
            producto.Grupo = "ACP";
            producto.SubGrupo = "ACP";
            producto.PVP = 10;

            _ = A.CallTo(() => servicio.BuscarProducto(linea.Producto)).Returns(producto);
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10 // es el de ficha
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);


            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 143 permite poner el producto AA21 a ese precio", respuesta.Motivo);
            //Assert.AreEqual("El producto AA11 no lleva oferta ni descuento", respuesta.Motivos[0]);
            //Assert.AreEqual("La oferta 143 permite poner el producto AA21 a ese precio", respuesta.Motivos[1]);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiHayOfertaParaLaFamiliaEsValidaParaElProductoSalvoOtrosAparatos()
        {
            _ = GestorPrecios.servicio.BuscarProducto("OTROS_APA");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "OTROS_APA",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 130
            };
            //linea.BaseImponible = 780;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "OTROS_APA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 6 //El precio de ficha son 10
            };
            //linea.BaseImponible = 6;
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = GestorOfertasPedido.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(1, precioDescuentoProducto.cantidad);
            Assert.AreEqual(.4M, precioDescuentoProducto.descuentoReal);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayDosDescuentosLosSumaEncadenados()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 10, //El precio de ficha son 10
                DescuentoLinea = .5M,
                DescuentoProducto = .5M
            };
            //linea.BaseImponible = linea.PrecioUnitario * linea.Cantidad; //para el test vale
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = GestorOfertasPedido.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(1, precioDescuentoProducto.cantidad);
            Assert.AreEqual(.75M, precioDescuentoProducto.descuentoReal);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiNoHayNingunaLineaDeEseProductoDevuelveNull()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA12",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 10;
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto precioDescuentoProducto = GestorOfertasPedido.MontarOfertaPedido("AA11", pedido);

            Assert.IsNull(precioDescuentoProducto);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayUnaLineaDeRegaloLaPoneEnCantidadOferta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            PrecioDescuentoProducto precioDescuentoProducto = GestorOfertasPedido.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(1, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(2, precioDescuentoProducto.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiLaLineaLlevaDescuentoLaOfertaTambien()
        {
            Producto producto = GestorPrecios.servicio.BuscarProducto("AA11");
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10,
                DescuentoLinea = .1M
            };
            //linea.BaseImponible = 18;
            pedido.Lineas.Add(linea);

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido(producto.Número, pedido);

            Assert.AreEqual(.1M, oferta.descuentoCalculado);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiHayUnaLineaDelMismoPrecioLaPoneEnLaOferta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 10
            };
            //linea.BaseImponible = 20;
            pedido.Lineas.Add(linea);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "REGALO",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            //linea2.BaseImponible = 0;
            pedido.Lineas.Add(linea2);

            // FAMYPROD no tiene líneas gratis propias, así que cantidadOferta = 0
            // La oferta se valida en REGALO (el producto con líneas gratis)
            PrecioDescuentoProducto precioDescuentoProducto = GestorOfertasPedido.MontarOfertaPedido(linea.Producto, pedido);

            Assert.AreEqual(0, precioDescuentoProducto.cantidadOferta);
            Assert.AreEqual(2, precioDescuentoProducto.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_SiOtroProductoDelMismoPrecioTieneOfertaNoContaminaAlProductoSinOferta()
        {
            // FAMYPROD y REGALO tienen mismo PVP (100) y Familia ("DeMarca")
            // FAMYPROD solo tiene líneas pagadas, REGALO tiene una línea gratis
            // MontarOfertaPedido("FAMYPROD") NO debe devolver cantidadOferta > 0
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 10
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "REGALO",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            });

            PrecioDescuentoProducto ofertaFamyprod = GestorOfertasPedido.MontarOfertaPedido("FAMYPROD", pedido);
            PrecioDescuentoProducto ofertaRegalo = GestorOfertasPedido.MontarOfertaPedido("REGALO", pedido);

            // FAMYPROD no debe contaminarse con la oferta de REGALO
            Assert.AreEqual(0, ofertaFamyprod.cantidadOferta);
            Assert.AreEqual(1, ofertaFamyprod.cantidad);

            // REGALO sí debe tener la oferta agregada
            Assert.AreEqual(1, ofertaRegalo.cantidadOferta);
            Assert.AreEqual(1, ofertaRegalo.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiHayMultiplesProductosConErrorDevuelveTodosLosMotivos()
        {
            // Arrange
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";
            pedido.numero = 123;

            // Producto 1 con oferta no autorizada
            LineaPedidoVentaDTO linea1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 3,
                PrecioUnitario = 10
            };
            pedido.Lineas.Add(linea1);

            LineaPedidoVentaDTO lineaRegalo1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 0 // Regalo
            };
            pedido.Lineas.Add(lineaRegalo1);

            // Producto 2 con descuento no autorizado
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD2",
                AplicarDescuento = true,
                Cantidad = 5,
                PrecioUnitario = 8, // Precio de ficha es 10
                DescuentoLinea = 0.20M // 20% descuento
            };
            pedido.Lineas.Add(linea2);

            // Producto 3 con oferta diferente no autorizada
            LineaPedidoVentaDTO linea3 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD3",
                AplicarDescuento = true,
                Cantidad = 4,
                PrecioUnitario = 15
            };
            pedido.Lineas.Add(linea3);

            LineaPedidoVentaDTO lineaRegalo3 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD3",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            };
            pedido.Lineas.Add(lineaRegalo3);

            // Mock productos
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("PROD1")).Returns(new Producto
            {
                Número = "PROD1",
                Nombre = "Producto 1",
                PVP = 10,
                Grupo = "GRP",
                SubGrupo = "SUB",
                Familia = "FAM1"
            });

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("PROD2")).Returns(new Producto
            {
                Número = "PROD2",
                Nombre = "Producto 2",
                PVP = 10,
                Grupo = "GRP",
                SubGrupo = "SUB",
                Familia = "FAM2"
            });

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("PROD3")).Returns(new Producto
            {
                Número = "PROD3",
                Nombre = "Producto 3",
                PVP = 15,
                Grupo = "GRP",
                SubGrupo = "SUB",
                Familia = "FAM3"
            });

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas(A<string>._)).Returns(new List<OfertaPermitida>());
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos(A<string>._, A<string>._, A<string>._)).Returns(new List<DescuentosProducto>());

            // Act
            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            // Assert
            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.IsTrue(respuesta.Motivos.Count >= 2, "Debería haber al menos 2 errores");
            Assert.IsTrue(respuesta.Motivo.Contains("•"), "El motivo consolidado debería tener viñetas");
            Assert.IsTrue(respuesta.Motivo.Contains("PROD1") || respuesta.Motivo.Contains("PROD2") || respuesta.Motivo.Contains("PROD3"));
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_SiHayDosProductosConErrorYUnoConValidadorDeAceptacionSoloDevuelveUnMotivo()
        {
            // Arrange
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";

            // Producto 1 con oferta no autorizada (sin validador de aceptación)
            LineaPedidoVentaDTO linea1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 3,
                PrecioUnitario = 10
            };
            pedido.Lineas.Add(linea1);

            LineaPedidoVentaDTO lineaRegalo1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 0
            };
            pedido.Lineas.Add(lineaRegalo1);

            // Producto 2 con oferta combinada válida (CON validador de aceptación)
            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10
            };
            pedido.Lineas.Add(linea2);

            LineaPedidoVentaDTO lineaRegalo2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21,
                DescuentoLinea = 1M
            };
            pedido.Lineas.Add(lineaRegalo2);

            // Mock productos
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("PROD1")).Returns(new Producto
            {
                Número = "PROD1",
                Nombre = "Producto 1",
                PVP = 10,
                Grupo = "GRP",
                SubGrupo = "SUB",
                Familia = "FAM1"
            });

            Producto productoAA21 = GestorPrecios.servicio.BuscarProducto("AA21");

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("PROD1")).Returns(new List<OfertaPermitida>());
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("PROD1", "1", "0")).Returns(new List<DescuentosProducto>());

            // Oferta combinada válida para AA21
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
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            // Act
            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            // Assert
            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(1, respuesta.Motivos.Count, "Solo debería haber 1 error (PROD1), AA21 está validado");
            Assert.IsFalse(respuesta.Motivo.Contains("•"), "Con un solo error no debería tener viñetas");
            Assert.IsTrue(respuesta.Motivo.Contains("PROD1"));
            Assert.IsFalse(respuesta.Motivo.Contains("AA21"));
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_MotivoConsolidadoTieneFormatoConVinetas()
        {
            // Arrange
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";

            // Tres productos con errores diferentes
            for (int i = 1; i <= 3; i++)
            {
                LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
                {
                    tipoLinea = 1,
                    Producto = $"PROD{i}",
                    AplicarDescuento = true,
                    Cantidad = 2,
                    PrecioUnitario = 10
                };
                pedido.Lineas.Add(linea);

                LineaPedidoVentaDTO lineaRegalo = new LineaPedidoVentaDTO
                {
                    tipoLinea = 1,
                    Producto = $"PROD{i}",
                    AplicarDescuento = true,
                    Cantidad = 1,
                    PrecioUnitario = 0
                };
                pedido.Lineas.Add(lineaRegalo);

                _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto($"PROD{i}")).Returns(new Producto
                {
                    Número = $"PROD{i}",
                    Nombre = $"Producto {i}",
                    PVP = 10,
                    Grupo = "GRP",
                    SubGrupo = "SUB",
                    Familia = $"FAM{i}"
                });

                _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas($"PROD{i}")).Returns(new List<OfertaPermitida>());
                _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos($"PROD{i}", "1", "0")).Returns(new List<DescuentosProducto>());
            }

            // Act
            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            // Assert
            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(3, respuesta.Motivos.Count);

            // Verificar formato con viñetas
            string[] lineas = respuesta.Motivo.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.AreEqual(3, lineas.Length, "Debería haber 3 líneas de error");

            foreach (string linea in lineas)
            {
                Assert.IsTrue(linea.StartsWith("• "), $"Cada línea debería empezar con '• ', pero era: {linea}");
            }
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasYDescuentosPermitidos_NoValidaElMismoProductoDosveces()
        {
            // Arrange
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";

            // Mismo producto en DOS líneas diferentes (no debería validarse dos veces)
            LineaPedidoVentaDTO linea1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10
            };
            pedido.Lineas.Add(linea1);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "PROD1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0 // Regalo
            };
            pedido.Lineas.Add(linea2);

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("PROD1")).Returns(new Producto
            {
                Número = "PROD1",
                Nombre = "Producto 1",
                PVP = 10,
                Grupo = "GRP",
                SubGrupo = "SUB",
                Familia = "FAM1"
            });

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasPermitidas("PROD1")).Returns(new List<OfertaPermitida>());
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarDescuentosPermitidos("PROD1", "1", "0")).Returns(new List<DescuentosProducto>());

            // Act
            ValidadorOfertasPermitidas validador = new ValidadorOfertasPermitidas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            // Assert
            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(1, respuesta.Motivos.Count, "Solo debería haber UN error aunque el producto aparezca en dos líneas");
            Assert.IsFalse(respuesta.Motivo.Contains("•"), "Con un solo error no debería tener viñetas");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOtrosAparatosSiempreSinDescuento_AcumulaMultiplesErrores()
        {
            // Arrange
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "1";
            pedido.contacto = "0";

            // Dos productos de "Otros Aparatos" (SubGrupo = "ACP") con descuento (no permitido)
            LineaPedidoVentaDTO linea1 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "APAR1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 90,
                DescuentoLinea = 0.10M // 10% descuento (no permitido)
            };
            pedido.Lineas.Add(linea1);

            LineaPedidoVentaDTO linea2 = new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "APAR2",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 80,
                DescuentoLinea = 0.20M // 20% descuento (no permitido)
            };
            pedido.Lineas.Add(linea2);

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("APAR1")).Returns(new Producto
            {
                Número = "APAR1",
                Nombre = "Aparato 1",
                PVP = 100,
                Grupo = "ACP",
                SubGrupo = "ACP", // Este es el SubGrupo que identifica "Otros Aparatos"
                Familia = "OtrosAparatos"
            });

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("APAR2")).Returns(new Producto
            {
                Número = "APAR2",
                Nombre = "Aparato 2",
                PVP = 100,
                Grupo = "ACP",
                SubGrupo = "ACP", // Este es el SubGrupo que identifica "Otros Aparatos"
                Familia = "OtrosAparatos"
            });

            // Act
            ValidadorOtrosAparatosSiempreSinDescuento validador = new ValidadorOtrosAparatosSiempreSinDescuento();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, GestorPrecios.servicio);

            // Assert
            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(2, respuesta.Motivos.Count, "Debería haber 2 errores");
            Assert.IsTrue(respuesta.Motivo.Contains("APAR1"));
            Assert.IsTrue(respuesta.Motivo.Contains("APAR2"));
            Assert.IsTrue(respuesta.Motivo.Contains("•"), "Con múltiples errores debería tener viñetas");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_SiLlevaVariasMuestrasDeMenosValorDelPermitidoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 80,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            };
            //linea.BaseImponible = 80;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA_2",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 2, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra2.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto + lineaMuestra2.Bruto);

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
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 15,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            };
            //linea.BaseImponible = 15;
            pedido.Lineas.Add(linea);
            LineaPedidoVentaDTO lineaMuestra = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // es el de ficha
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra);
            LineaPedidoVentaDTO lineaMuestra2 = new LineaPedidoVentaDTO
            {
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS,
                Producto = "MUESTRA_2",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 1, // el de ficha es 2
                DescuentoLinea = 1M // de regalo, 100% dto
            };
            //lineaMuestra2.BaseImponible = 0;
            pedido.Lineas.Add(lineaMuestra2);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(lineaMuestra.Bruto + lineaMuestra2.Bruto);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MUESTRA");

            Assert.IsFalse(respuesta.ValidacionSuperada);
            //Assert.AreEqual("El producto MUESTRA no puede ir a ese precio porque no es material promocional o se supera el importe autorizado", respuesta.Motivo);
        }
    }
}
