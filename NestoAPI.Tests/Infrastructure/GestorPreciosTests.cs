using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

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

            _ = A.CallTo(() => servicio.BuscarProducto("COBRADO")).Returns(new Producto
            {
                Número = "COBRADO",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
            });
            _ = A.CallTo(() => servicio.BuscarProducto("40133")).Returns(new Producto
            {
                Número = "40133",
                PVP = 100,
                Grupo = "COS",
                SubGrupo = "CER",
                Familia = "DeMarca"
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
        public void GestorPrecios_EsOfertaPermitida_RegaloConProductoMismoPrecioPasaValidacionOfertas()
        {
            // Un producto regalo (PrecioUnitario=0) con otro producto cobrado del mismo PVP/Familia
            // NO debe ser tratado como oferta. Pasa la validación de ofertas (ValidacionSuperada=true)
            // y será validado por otros validadores (ej: ValidadorDescuentosPermitidos, ValidadorGanavisiones).
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
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "OF_FAMILIA",
                AplicarDescuento = true,
                Cantidad = 6,
                PrecioUnitario = 130.01M
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "MISMO_PRECIO",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0
            });

            RespuestaValidacion respuesta = ValidadorOfertasPermitidas.EsOfertaPermitida(mismoPrecio, pedido, GestorPrecios.servicio);

            // El regalo no tiene líneas cobradas propias → cantidad=0 → no es una oferta
            Assert.IsTrue(respuesta.ValidacionSuperada);
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
        public void ValidadorDescuentosPermitidos_LineaIntacta_NoRevalidaElDescuento_Issue237()
        {
            // Línea preexistente intacta (NoRevalidarDescuento) con un descuento que normalmente NO
            // estaría autorizado (p. ej. la tarifa subió tras meter el pedido): no debe bloquear la unión.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 30,            // 70 % de dto, normalmente NO autorizado
                NoRevalidarDescuento = true
            });

            RespuestaValidacion respuesta = new ValidadorDescuentosPermitidos().EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "Una línea intacta no debe re-bloquearse por una subida de tarifa posterior");
        }

        [TestMethod]
        public void ValidadorDescuentosPermitidos_LineaNoIntacta_SiRevalidaElDescuento_Issue237()
        {
            // La misma línea SIN marcar como intacta debe seguir fallando (no relajar de más).
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "FAMYPROD",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 30,
                NoRevalidarDescuento = false
            });

            RespuestaValidacion respuesta = new ValidadorDescuentosPermitidos().EsPedidoValido(pedido, GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Una línea no intacta debe seguir validándose");
        }

        private static LineaPedidoVentaDTO LineaPreexistente(int cantidad, decimal precio, int? oferta = null)
            => new LineaPedidoVentaDTO { id = 100, Producto = "45363", Cantidad = cantidad, PrecioUnitario = precio, oferta = oferta };

        [TestMethod]
        public void EsIntactaParaDescuento_MismaCantidadYPrecioSinOferta_True()
        {
            Assert.IsTrue(LineaPreexistente(1, 165m).EsIntactaParaDescuento(1, 165m));
        }

        [TestMethod]
        public void EsIntactaParaDescuento_LineaNueva_False()
        {
            // id == 0 → línea nueva, debe validarse.
            Assert.IsFalse(new LineaPedidoVentaDTO { id = 0, Producto = "45363", Cantidad = 1, PrecioUnitario = 165m }.EsIntactaParaDescuento(1, 165m));
        }

        [TestMethod]
        public void EsIntactaParaDescuento_CantidadDistinta_False()
        {
            Assert.IsFalse(LineaPreexistente(2, 165m).EsIntactaParaDescuento(1, 165m));
        }

        [TestMethod]
        public void EsIntactaParaDescuento_PrecioDistinto_False()
        {
            Assert.IsFalse(LineaPreexistente(1, 160m).EsIntactaParaDescuento(1, 165m));
        }

        [TestMethod]
        public void EsIntactaParaDescuento_ConOferta_False()
        {
            // Línea intacta pero con oferta (combinada): debe seguir validándose.
            Assert.IsFalse(LineaPreexistente(1, 165m, oferta: 7).EsIntactaParaDescuento(1, 165m));
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

        // El importe mínimo es POR INSTANCIA: si el pedido lleva 2 veces las cantidades de la oferta,
        // hay que cumplirlo 2 veces. Oferta {AA11:1, AA21:1} con mínimo 10; el pedido lleva 2 de cada
        // pero su base imponible (10) solo alcanza el suelo de UNA instancia (haría falta 20) → NO válido.
        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_ImporteMinimoPorInstancia_DosInstanciasConUnSoloSueloNoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "AA11", AplicarDescuento = true, Cantidad = 2, PrecioUnitario = 5 });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "AA21", AplicarDescuento = true, Cantidad = 2, PrecioUnitario = 21, DescuentoLinea = 1M });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 130,
                    Empresa = "NV",
                    ImporteMinimo = 10,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 130, Empresa = "NV", Producto = "AA11", Cantidad = 1, Precio = 0 },
                        new OfertaCombinadaDetalle { OfertaId = 130, Empresa = "NV", Producto = "AA21", Cantidad = 1, Precio = 0 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        // Espejo del anterior: 2 instancias que SÍ alcanzan el doble del suelo (20) → válido.
        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_ImporteMinimoPorInstancia_DosInstanciasConElDobleDelSueloEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "AA11", AplicarDescuento = true, Cantidad = 2, PrecioUnitario = 10 });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "AA21", AplicarDescuento = true, Cantidad = 2, PrecioUnitario = 21, DescuentoLinea = 1M });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 130,
                    Empresa = "NV",
                    ImporteMinimo = 10,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 130, Empresa = "NV", Producto = "AA11", Cantidad = 1, Precio = 0 },
                        new OfertaCombinadaDetalle { OfertaId = 130, Empresa = "NV", Producto = "AA21", Cantidad = 1, Precio = 0 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA21")).Returns(listaOfertas);

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA21", GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
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

        // Documenta las 4 ofertas combinadas "Modellare Home Care" (Id 239-242, creadas el
        // 28/05/26). Cada una vincula 1 ud de su pareja (239->22268, 240->23130, 241->23128,
        // 242->23132) con 1 ud de 45381; el ImporteMinimo (75% de la tarifa de la pareja) es el
        // suelo del conjunto. No hay "regalo" como tal: ambas líneas van vinculadas 1:1.
        // Caso: 1 ud de cada pareja + 1 ud de 45381. Como hay 4 parejas, harían falta 4 uds de
        // 45381 (1 por pareja); con 1 sola, el reparto en instancias enteras no cuadra → NO válido.
        [TestMethod]
        public void GestorPrecios_OfertasCombinadasModellare_UnaUnidadDeCadaParejaYUnaSolaDelRegalo_NoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "22268", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 13.09M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23130", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23128", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23132", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "45381", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 0M });

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            GestorPrecios.servicio = servicio;
            List<OfertaCombinada> ofertas = CrearOfertasCombinadasModellare();
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("45381")).Returns(ofertas);
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("22268")).Returns(new List<OfertaCombinada> { ofertas[0] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23130")).Returns(new List<OfertaCombinada> { ofertas[1] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23128")).Returns(new List<OfertaCombinada> { ofertas[2] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23132")).Returns(new List<OfertaCombinada> { ofertas[3] });

            RespuestaValidacion respuesta = new ValidadorOfertasCombinadas().EsPedidoValido(pedido, "45381", servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
        }

        // Mismo caso pero con UNA sola línea de 45381 con cantidad 4 (un regalo por cada pareja).
        // También se permite, que es lo que se quiere (4 parejas -> 4 regalos). OJO: el validador
        // razona oferta a oferta y NO agrega las 4 ofertas; el chequeo de "más cantidad de regalo
        // de la permitida" (45381 x4 frente a 1 ud de la pareja de UNA oferta) intentaría anular la
        // oferta, pero como estas ofertas tienen ImporteMinimo > 0 la validación ya se marcó como
        // superada antes y ese rechazo no surte efecto. Es decir: el resultado deseado se obtiene,
        // pero por un camino frágil. Si se "arregla" el chequeo de múltiplos, este test avisará.
        [TestMethod]
        public void GestorPrecios_OfertasCombinadasModellare_UnaLineaDeRegaloConCantidadCuatroYUnaParejaDeCadaUna_EsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "22268", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 13.09M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23130", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23128", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23132", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "45381", AplicarDescuento = true, Cantidad = 4, PrecioUnitario = 0M });

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            GestorPrecios.servicio = servicio;
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("45381")).Returns(CrearOfertasCombinadasModellare());

            RespuestaValidacion respuesta = new ValidadorOfertasCombinadas().EsPedidoValido(pedido, "45381", servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        // PRUEBA PRINCIPAL: 1 ud de cada una de las 5 refs (las 4 parejas al 25% + 1 ud de 45381).
        // Con 4 parejas harían falta 4 uds de 45381 (1 por pareja), pero el pedido solo lleva 1.
        // Como el producto 45381 es compartido por las 4 ofertas, validar cualquier pareja debe
        // RECHAZAR: el reparto en instancias enteras no cuadra (la única unidad de 45381 no puede
        // respaldar a las 4 parejas a la vez). Es el mismo problema que 1 pareja + N regalos.
        [TestMethod]
        public void GestorPrecios_OfertasCombinadasModellare_UnaUnidadDeCadaUnaDeLas5Refs_NoEsValido()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "22268", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 13.09M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23130", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23128", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "23132", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 10.84M });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { Producto = "45381", AplicarDescuento = true, Cantidad = 1, PrecioUnitario = 0M });

            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            GestorPrecios.servicio = servicio;
            // CrearOfertasCombinadasModellare devuelve [0]=239(22268), [1]=240(23130), [2]=241(23128), [3]=242(23132)
            List<OfertaCombinada> ofertas = CrearOfertasCombinadasModellare();
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("45381")).Returns(ofertas);
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("22268")).Returns(new List<OfertaCombinada> { ofertas[0] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23130")).Returns(new List<OfertaCombinada> { ofertas[1] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23128")).Returns(new List<OfertaCombinada> { ofertas[2] });
            _ = A.CallTo(() => servicio.BuscarOfertasCombinadas("23132")).Returns(new List<OfertaCombinada> { ofertas[3] });

            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, "22268", servicio).ValidacionSuperada);
            Assert.IsFalse(validador.EsPedidoValido(pedido, "23130", servicio).ValidacionSuperada);
            Assert.IsFalse(validador.EsPedidoValido(pedido, "23128", servicio).ValidacionSuperada);
            Assert.IsFalse(validador.EsPedidoValido(pedido, "23132", servicio).ValidacionSuperada);
        }

        private static List<OfertaCombinada> CrearOfertasCombinadasModellare()
        {
            OfertaCombinada Crear(int id, string pareja, decimal importeMinimo) => new OfertaCombinada
            {
                Id = id,
                Empresa = "1",
                ImporteMinimo = importeMinimo,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = id, Empresa = "1", Producto = pareja, Cantidad = 1, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = id, Empresa = "1", Producto = "45381", Cantidad = 1, Precio = 0 }
                }
            };

            return new List<OfertaCombinada>
            {
                Crear(239, "22268", 13.09M),
                Crear(240, "23130", 10.84M),
                Crear(241, "23128", 10.84M),
                Crear(242, "23132", 10.84M)
            };
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
        public void GestorPrecios_ValidadorOfertasCombinadas_OfertaUnSoloProductoEnVariasLineasConPrecioEsValida()
        {
            // Oferta de un solo producto repartida en dos líneas con precio:
            // p. ej. 2ª unidad al 50 %, una unidad a 20,40 € y otra a 10,20 €.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 20.40M
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10.20M
            });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 555,
                    Empresa = "NV",
                    ImporteMinimo = 0,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 20.40M, Cantidad = 1 },
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 10.20M, Cantidad = 1 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA1")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA1", GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 555 permite poner el producto AA1 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_OfertaUnSoloProductoConImporteMinimoEsValida()
        {
            // Oferta de un solo producto con el precio total en el importe mínimo:
            // 2 unidades de AA1 cuyo importe (30,60 €) llega al mínimo de la oferta.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 15.30M
            });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 555,
                    Empresa = "NV",
                    ImporteMinimo = 30.60M,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 0, Cantidad = 2 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA1")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA1", GestorPrecios.servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 555 permite poner el producto AA1 a ese precio", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SegundaUnidadAl50EnDosLineasEsValida()
        {
            // Nesto#371: "2ª unidad al 50 %" repartida en DOS líneas (1 ud a precio completo + 1 ud al
            // 50 %) debe validar contra una oferta de un solo producto con Cantidad 2 e ImporteMinimo 30,60.
            // Antes fallaba: ninguna línea (de cantidad 1) cumplía por sí sola Cantidad >= 2.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 20.40M // 1ª unidad a precio completo (base 20,40)
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 20.40M,
                DescuentoLinea = 0.5M // 2ª unidad al 50 % (base 10,20)
            });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 238,
                    Empresa = "NV",
                    ImporteMinimo = 30.60M,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 238, Empresa = "NV", Producto = "AA1", Precio = 0, Cantidad = 2 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA1")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA1", GestorPrecios.servicio);

            // 20,40 + 10,20 = 30,60 = importe mínimo (1 instancia) -> la oferta autoriza el 50 %.
            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_OfertaUnSoloProductoSiNoLlegaAlImporteMinimoNoEsValida()
        {
            // Oferta de un solo producto con importe mínimo: 2 unidades a 12 € (24 €)
            // no llegan al mínimo de 30,60 €, así que la oferta no autoriza el precio.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 12M
            });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 555,
                    Empresa = "NV",
                    ImporteMinimo = 30.60M,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 0, Cantidad = 2 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA1")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA1", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("La oferta 555 tiene que tener un importe mínimo de 30,60 € para que sea válida", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_OfertaUnSoloProductoEnVariasLineasSiFaltaElPrecioCompletoNoEsValida()
        {
            // Oferta "por líneas con precio": exige una unidad a precio completo (20,40 €)
            // y otra a mitad (10,20 €). Si las dos van a mitad de precio, no se autoriza.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10.20M
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                Producto = "AA1",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10.20M
            });
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 555,
                    Empresa = "NV",
                    ImporteMinimo = 0,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 20.40M, Cantidad = 1 },
                        new OfertaCombinadaDetalle { OfertaId = 555, Empresa = "NV", Producto = "AA1", Precio = 10.20M, Cantidad = 1 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("AA1")).Returns(listaOfertas);
            ValidadorOfertasCombinadas validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "AA1", GestorPrecios.servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual("No hay ninguna oferta combinada que autorice a vender el producto AA1 a ese precio", respuesta.Motivo);
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
            // Tras el follow-up del mensaje, el pipeline surfacea el motivo concreto del validador de
            // muestras (supera las 10 uds sueltas) en vez del genérico "no autorizado el 100 %".
            Assert.AreEqual("El producto MUESTRA no se puede regalar: se superan las 10 unidades de muestra suelta permitidas", respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorMuestrasYMaterialPromocional_LasMuestrasDeUnaOfertaCombinadaNoCuentanParaEl5Porciento()
        {
            // Regresión (pedido 918775): una muestra suelta que por sí sola cabe en el 5 % se rechazaba
            // porque las muestras regaladas por una oferta combinada se sumaban al importe de muestras.
            // Las muestras justificadas por la oferta NO deben gastar el 5 % de las muestras sueltas.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";

            // Producto cobrado que da base imponible (20 €). 5 % = 1 €.
            LineaPedidoVentaDTO lineaCobrada = new LineaPedidoVentaDTO
            {
                Producto = "COSMAIN",
                AplicarDescuento = true,
                Cantidad = 2,
                PrecioUnitario = 10,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            };
            pedido.Lineas.Add(lineaCobrada);

            // Muestra regalada por la oferta combinada (PVP 5 € → 5 € de importe de muestras).
            LineaPedidoVentaDTO lineaMuestraOferta = new LineaPedidoVentaDTO
            {
                Producto = "MOFERTA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 5,
                DescuentoLinea = 1M,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS
            };
            pedido.Lineas.Add(lineaMuestraOferta);

            // Muestra suelta (PVP 0,5 € → 0,5 €, cabe en el 5 % = 1 €).
            LineaPedidoVentaDTO lineaMuestraSuelta = new LineaPedidoVentaDTO
            {
                Producto = "MSUELTA",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 0.5M,
                DescuentoLinea = 1M,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS
            };
            pedido.Lineas.Add(lineaMuestraSuelta);

            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("COSMAIN")).Returns(new Producto
            {
                Número = "COSMAIN",
                PVP = 10,
                Grupo = "COS",
                SubGrupo = "025"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MOFERTA")).Returns(new Producto
            {
                Número = "MOFERTA",
                PVP = 5,
                Grupo = "COS",
                SubGrupo = "MMP"
            });
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto("MSUELTA")).Returns(new Producto
            {
                Número = "MSUELTA",
                PVP = 0.5M,
                Grupo = "COS",
                SubGrupo = "MMP"
            });

            // La oferta combinada (COSMAIN + MOFERTA de regalo) justifica la muestra MOFERTA.
            List<OfertaCombinada> listaOfertas = new List<OfertaCombinada>
            {
                new OfertaCombinada
                {
                    Id = 900,
                    Empresa = "1",
                    ImporteMinimo = 0,
                    OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                    {
                        new OfertaCombinadaDetalle { OfertaId = 900, Empresa = "1", Producto = "COSMAIN", Precio = 10, Cantidad = 1 },
                        new OfertaCombinadaDetalle { OfertaId = 900, Empresa = "1", Producto = "MOFERTA", Precio = 0, Cantidad = 1 }
                    }
                }
            };
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("MOFERTA")).Returns(listaOfertas);

            // CalcularImporteGrupo devuelve TODAS las muestras cosméticas: oferta (5) + suelta (0,5) = 5,5 €.
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(5.5M);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "MSUELTA");

            // Descontando la muestra de la oferta (5 €) quedan 0,5 € ≤ 1 € (5 %) → la suelta es válida.
            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_UnaCamisetaDeMasNoDebeTirarTodaLaOfertaNiSusMuestras()
        {
            // Regresión real (ELMAH 04/06/26, pedido borrador de CAROLINA FERREIRA BERMEJO 29382):
            // se rechazó el descuento del 100 % de TODAS las muestras (45440, 45442, 45461, 45444,
            // 45445, 45446, 45449) de la oferta combinada 244 "NEO-TECH Ainhoa opcion exclusiva beauty".
            //
            // Causa: el pedido cumple EXACTAMENTE la oferta 244 (importe mínimo 642,56 € y todas las
            // cantidades), salvo que lleva 2 camisetas (45449) cuando el grupo de alternativas de la
            // oferta da 1. GruposSatisfechos exige cantidad exacta (pedidas != requerido => false), así
            // que UNA camiseta de más invalida la oferta entera y arrastra a todas las muestras.
            //
            // Comportamiento deseado (confirmado por Carlos): la oferta se cumple consumiendo 1 camiseta
            // y SOBRA 1 camiseta (PVP 17 €). Esa camiseta sobrante es material promocional suelto: como
            // 17 € < 5 % de la base (642,56 € → 32,13 €), el pedido debe ACEPTARSE.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "29382";
            pedido.contacto = "0";

            // Productos cobrados de la oferta (dan la base imponible: 642,56 €, justo el importe mínimo).
            AnadirLineaCobrada(pedido, "45437", 2, 38.63M);
            AnadirLineaCobrada(pedido, "45438", 5, 26.42M);
            AnadirLineaCobrada(pedido, "45439", 20, 4.56M);
            AnadirLineaCobrada(pedido, "45441", 10, 34.20M);

            // Muestras de la oferta (100 % dto). 45449 es la camiseta: la oferta da 1, el pedido lleva 2.
            AnadirLineaMuestra(pedido, "45440", 1, 10M);
            AnadirLineaMuestra(pedido, "45442", 50, 0.40M);
            AnadirLineaMuestra(pedido, "45461", 30, 4M);
            AnadirLineaMuestra(pedido, "45444", 30, 0.80M);
            AnadirLineaMuestra(pedido, "45445", 1, 19M);
            AnadirLineaMuestra(pedido, "45446", 1, 10M);
            AnadirLineaMuestra(pedido, "45449", 2, 17M);

            ConfigurarProductoCobrado("45437", 38.63M);
            ConfigurarProductoCobrado("45438", 26.42M);
            ConfigurarProductoCobrado("45439", 4.56M);
            ConfigurarProductoCobrado("45441", 34.20M);
            ConfigurarProductoMuestra("45440", 10M);
            ConfigurarProductoMuestra("45442", 0.40M);
            ConfigurarProductoMuestra("45461", 4M);
            ConfigurarProductoMuestra("45444", 0.80M);
            ConfigurarProductoMuestra("45445", 19M);
            ConfigurarProductoMuestra("45446", 10M);
            ConfigurarProductoMuestra("45449", 17M);

            OfertaCombinada oferta244 = CrearOfertaNeoTech244();
            OfertaCombinada oferta245 = CrearOfertaNeoTech245();
            List<OfertaCombinada> ambas = new List<OfertaCombinada> { oferta244, oferta245 };
            List<OfertaCombinada> solo244 = new List<OfertaCombinada> { oferta244 };
            // 45445 solo está en la 244; el resto, en las dos.
            foreach (string p in new[] { "45437", "45438", "45439", "45440", "45441", "45442", "45444", "45446", "45449", "45461" })
            {
                string producto = p;
                _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas(producto)).Returns(ambas);
            }
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("45445")).Returns(solo244);

            // Importe total de muestras cosméticas (PVP × cantidad): 10+20+120+24+19+10+34 = 237 €.
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(237M);

            // Las 7 referencias que ELMAH rechazó deben quedar autorizadas (por la oferta las que la
            // oferta consume, y por el 5 % de material promocional la camiseta sobrante).
            string[] productosRechazadosEnElmah = { "45440", "45442", "45461", "45444", "45445", "45446", "45449" };
            List<string> sigueRechazado = new List<string>();
            foreach (string producto in productosRechazadosEnElmah)
            {
                RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, producto);
                if (!respuesta.ValidacionSuperada)
                {
                    sigueRechazado.Add(producto);
                }
            }

            Assert.AreEqual(0, sigueRechazado.Count,
                "Estas referencias siguen rechazadas y no deberían: " + string.Join(", ", sigueRechazado));
        }

        [TestMethod]
        public void GestorPrecios_ValidadorOfertasCombinadas_SiLasCamisetasSobrantesSuperanEl5PorcientoSeRechazan()
        {
            // Misma oferta 244, pero ahora la comercial mete 5 camisetas (45449). La oferta cubre 1;
            // sobran 4 (4 × 17 = 68 €), que como material promocional suelto superan el 5 % de la base
            // (642,56 € → 32,13 €). Debe rechazarse SOLO la camiseta, no las demás muestras de la oferta.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "29382";
            pedido.contacto = "0";

            AnadirLineaCobrada(pedido, "45437", 2, 38.63M);
            AnadirLineaCobrada(pedido, "45438", 5, 26.42M);
            AnadirLineaCobrada(pedido, "45439", 20, 4.56M);
            AnadirLineaCobrada(pedido, "45441", 10, 34.20M);

            AnadirLineaMuestra(pedido, "45440", 1, 10M);
            AnadirLineaMuestra(pedido, "45442", 50, 0.40M);
            AnadirLineaMuestra(pedido, "45461", 30, 4M);
            AnadirLineaMuestra(pedido, "45444", 30, 0.80M);
            AnadirLineaMuestra(pedido, "45445", 1, 19M);
            AnadirLineaMuestra(pedido, "45446", 1, 10M);
            AnadirLineaMuestra(pedido, "45449", 5, 17M); // 5 camisetas: la oferta cubre 1, sobran 4

            ConfigurarProductoCobrado("45437", 38.63M);
            ConfigurarProductoCobrado("45438", 26.42M);
            ConfigurarProductoCobrado("45439", 4.56M);
            ConfigurarProductoCobrado("45441", 34.20M);
            ConfigurarProductoMuestra("45440", 10M);
            ConfigurarProductoMuestra("45442", 0.40M);
            ConfigurarProductoMuestra("45461", 4M);
            ConfigurarProductoMuestra("45444", 0.80M);
            ConfigurarProductoMuestra("45445", 19M);
            ConfigurarProductoMuestra("45446", 10M);
            ConfigurarProductoMuestra("45449", 17M);

            OfertaCombinada oferta244 = CrearOfertaNeoTech244();
            OfertaCombinada oferta245 = CrearOfertaNeoTech245();
            List<OfertaCombinada> ambas = new List<OfertaCombinada> { oferta244, oferta245 };
            List<OfertaCombinada> solo244 = new List<OfertaCombinada> { oferta244 };
            foreach (string p in new[] { "45437", "45438", "45439", "45440", "45441", "45442", "45444", "45446", "45449", "45461" })
            {
                string producto = p;
                _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas(producto)).Returns(ambas);
            }
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("45445")).Returns(solo244);

            // Importe total de muestras: 10+20+120+24+19+10 + 5×17 = 288 €.
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(288M);

            // Las demás muestras siguen autorizadas por la oferta...
            RespuestaValidacion respuestaMuestra = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "45442");
            Assert.IsTrue(respuestaMuestra.ValidacionSuperada, "Las muestras de la oferta deben seguir autorizadas");

            // ...pero la camiseta sobrante (4 uds = 68 € > 32,13 €) NO.
            RespuestaValidacion respuestaCamiseta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "45449");
            Assert.IsFalse(respuestaCamiseta.ValidacionSuperada,
                "Las 4 camisetas sobrantes superan el 5 % y no deben autorizarse");
            Assert.IsTrue(respuestaCamiseta.Motivo != null && respuestaCamiseta.Motivo.Contains("supera el 5 %"),
                "El motivo del rechazo debe explicar que supera el 5 %, no el genérico del 100 %. Motivo: " + respuestaCamiseta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_LaCamisetaQueSuperaEl5PorcientoDaMensajeClaro()
        {
            // End-to-end: el pipeline completo (denegación -> aceptación) debe surfacear el motivo
            // CONCRETO del validador de muestras ("supera el 5 %") en vez del genérico de denegación
            // ("No se encuentra autorizado el descuento del 100 %"), que no le dice al usuario qué pasa.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "29382";
            pedido.contacto = "0";

            AnadirLineaCobrada(pedido, "45437", 2, 38.63M);
            AnadirLineaCobrada(pedido, "45438", 5, 26.42M);
            AnadirLineaCobrada(pedido, "45439", 20, 4.56M);
            AnadirLineaCobrada(pedido, "45441", 10, 34.20M);

            AnadirLineaMuestra(pedido, "45440", 1, 10M);
            AnadirLineaMuestra(pedido, "45442", 50, 0.40M);
            AnadirLineaMuestra(pedido, "45461", 30, 4M);
            AnadirLineaMuestra(pedido, "45444", 30, 0.80M);
            AnadirLineaMuestra(pedido, "45445", 1, 19M);
            AnadirLineaMuestra(pedido, "45446", 1, 10M);
            AnadirLineaMuestra(pedido, "45449", 5, 17M);

            ConfigurarProductoCobrado("45437", 38.63M);
            ConfigurarProductoCobrado("45438", 26.42M);
            ConfigurarProductoCobrado("45439", 4.56M);
            ConfigurarProductoCobrado("45441", 34.20M);
            ConfigurarProductoMuestra("45440", 10M);
            ConfigurarProductoMuestra("45442", 0.40M);
            ConfigurarProductoMuestra("45461", 4M);
            ConfigurarProductoMuestra("45444", 0.80M);
            ConfigurarProductoMuestra("45445", 19M);
            ConfigurarProductoMuestra("45446", 10M);
            ConfigurarProductoMuestra("45449", 17M);

            OfertaCombinada oferta244 = CrearOfertaNeoTech244();
            OfertaCombinada oferta245 = CrearOfertaNeoTech245();
            List<OfertaCombinada> ambas = new List<OfertaCombinada> { oferta244, oferta245 };
            List<OfertaCombinada> solo244 = new List<OfertaCombinada> { oferta244 };
            foreach (string p in new[] { "45437", "45438", "45439", "45440", "45441", "45442", "45444", "45446", "45449", "45461" })
            {
                string producto = p;
                _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas(producto)).Returns(ambas);
            }
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas("45445")).Returns(solo244);
            _ = A.CallTo(() => GestorPrecios.servicio.CalcularImporteGrupo(pedido, "COS", "MMP")).Returns(288M);

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada, "El pedido no es válido (la camiseta sobrante supera el 5 %)");
            Assert.IsTrue(respuesta.Motivos.Any(m => m.Contains("supera el 5 %")),
                "El motivo final debe explicar que supera el 5 %. Motivos: " + string.Join(" | ", respuesta.Motivos));
            Assert.IsFalse(respuesta.Motivos.Any(m => m.Contains("descuento del 100")),
                "No debe quedar el mensaje genérico del 100 % para la camiseta. Motivos: " + string.Join(" | ", respuesta.Motivos));
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_ElRechazoDelGateDeOfertaCombinadaDaMensajeClaro()
        {
            // 15/07/26 (pedidos 922350/922324): el usuario solo veía "No se encuentra autorización
            // para la oferta del producto X" aunque el gate de RegalarMenorImporte sabía exactamente
            // por qué rechazaba. El pipeline debe surfacear el motivo del gate (vía MotivoEspecifico),
            // igual que ya hace con el del 5 % de muestras.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "31522";
            pedido.contacto = "1";

            // 2 instancias del 2+1 mezclable (6 unidades justas), pero regalando las 2 unidades
            // CARAS: no hay partición válida y el gate rechaza con su motivo concreto.
            AnadirLineaCobrada(pedido, "GATE_A", 2, 10M);
            AnadirLineaCobrada(pedido, "GATE_B", 1, 12M);
            AnadirLineaCobrada(pedido, "GATE_C", 1, 15M);
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = 1,
                Producto = "GATE_C",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 0, // las dos gratis
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            });

            ConfigurarProductoCobrado("GATE_A", 10M);
            ConfigurarProductoCobrado("GATE_B", 12M);
            ConfigurarProductoCobrado("GATE_C", 15M);

            OfertaCombinada oferta = new OfertaCombinada
            {
                Id = 300,
                Empresa = "1",
                ImporteMinimo = 0,
                RegalarMenorImporte = true,
                UnidadesRegaladas = 1,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = "GATE_A", Precio = 0, Cantidad = 3, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = "GATE_B", Precio = 0, Cantidad = 3, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = "GATE_C", Precio = 0, Cantidad = 3, GrupoAlternativa = 1 }
                }
            };
            foreach (string p in new[] { "GATE_A", "GATE_B", "GATE_C" })
            {
                string producto = p;
                _ = A.CallTo(() => GestorPrecios.servicio.BuscarOfertasCombinadas(producto)).Returns(new List<OfertaCombinada> { oferta });
            }

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.IsTrue(respuesta.Motivos.Any(m => m.Contains("menor importe")),
                "El motivo final debe ser el del gate (regalar la de menor importe). Motivos: " + string.Join(" | ", respuesta.Motivos));
            Assert.IsFalse(respuesta.Motivos.Any(m => m.Contains("No se encuentra autorización")),
                "No debe quedar el genérico de denegación. Motivos: " + string.Join(" | ", respuesta.Motivos));
        }

        [TestMethod]
        public void GestorPrecios_EsPedidoValido_ConsolidaAutorizadaDenegadaExpresamente()
        {
            // H1 (deuda detectada 04/06/26, visible en los X-Context de ELMAH que siempre dicen
            // false): cuando un error de denegación viene marcado como denegado EXPRESAMENTE, el
            // flag debe llegar consolidado a la respuesta final del pipeline.
            List<IValidadorDenegacion> denegacionOriginal = GestorPrecios.listaValidadoresDenegacion;
            List<IValidadorAceptacion> aceptacionOriginal = GestorPrecios.listaValidadoresAceptacion;
            try
            {
                GestorPrecios.listaValidadoresDenegacion = new List<IValidadorDenegacion> { new ValidadorDenegacionExpresaStub() };
                GestorPrecios.listaValidadoresAceptacion = new List<IValidadorAceptacion>();
                PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
                pedido.cliente = "5";

                RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido);

                Assert.IsFalse(respuesta.ValidacionSuperada);
                Assert.IsTrue(respuesta.AutorizadaDenegadaExpresamente,
                    "La denegación expresa debe llegar consolidada a la respuesta final (H1)");
            }
            finally
            {
                GestorPrecios.listaValidadoresDenegacion = denegacionOriginal;
                GestorPrecios.listaValidadoresAceptacion = aceptacionOriginal;
            }
        }

        private class ValidadorDenegacionExpresaStub : IValidadorDenegacion
        {
            public RespuestaValidacion EsPedidoValido(PedidoVentaDTO pedido, IServicioPrecios servicio)
            {
                return new RespuestaValidacion
                {
                    ValidacionSuperada = false,
                    Errores = new List<ErrorValidacion>
                    {
                        new ErrorValidacion
                        {
                            Motivo = "Oferta denegada expresamente para el producto X",
                            ProductoId = "X",
                            AutorizadaDenegadaExpresamente = true
                        }
                    }
                };
            }
        }

        private static void AnadirLineaCobrada(PedidoVentaDTO pedido, string producto, int cantidad, decimal precio)
        {
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = 1, // línea de venta: los validadores de denegación solo procesan tipoLinea == 1
                Producto = producto,
                AplicarDescuento = true,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA
            });
        }

        private static void AnadirLineaMuestra(PedidoVentaDTO pedido, string producto, int cantidad, decimal precio)
        {
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = 1, // línea de venta: los validadores de denegación solo procesan tipoLinea == 1
                Producto = producto,
                AplicarDescuento = true,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = 1M, // de regalo, 100 % dto
                GrupoProducto = Constantes.Productos.GRUPO_COSMETICA,
                SubgrupoProducto = Constantes.Productos.SUBGRUPO_MUESTRAS
            });
        }

        private static void ConfigurarProductoCobrado(string producto, decimal pvp)
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto(producto)).Returns(new Producto
            {
                Número = producto,
                PVP = pvp,
                Grupo = Constantes.Productos.GRUPO_COSMETICA,
                SubGrupo = "COS"
            });
        }

        private static void ConfigurarProductoMuestra(string producto, decimal pvp)
        {
            _ = A.CallTo(() => GestorPrecios.servicio.BuscarProducto(producto)).Returns(new Producto
            {
                Número = producto,
                PVP = pvp,
                Grupo = Constantes.Productos.GRUPO_COSMETICA,
                SubGrupo = Constantes.Productos.SUBGRUPO_MUESTRAS
            });
        }

        private static OfertaCombinada CrearOfertaNeoTech244()
        {
            return new OfertaCombinada
            {
                Id = 244,
                Empresa = "1",
                Nombre = "Oferta NEO-TECH Ainhoa opcion exclusiva beauty",
                ImporteMinimo = 642.56M,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45437", Precio = 0, Cantidad = 2 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45438", Precio = 0, Cantidad = 5 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45439", Precio = 0, Cantidad = 20 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45440", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45441", Precio = 0, Cantidad = 10 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45442", Precio = 0, Cantidad = 50 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45444", Precio = 0, Cantidad = 30 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45445", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45446", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45461", Precio = 0, Cantidad = 30 },
                    // Grupo de alternativas (elige 1 camiseta): 45447 / 45448 / 45449
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45447", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45448", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = "45449", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 }
                }
            };
        }

        private static OfertaCombinada CrearOfertaNeoTech245()
        {
            return new OfertaCombinada
            {
                Id = 245,
                Empresa = "1",
                Nombre = "Oferta NEO-TECH Ainhoa opcion exclusiva simple",
                ImporteMinimo = 383.71M,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45437", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45438", Precio = 0, Cantidad = 4 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45439", Precio = 0, Cantidad = 15 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45440", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45441", Precio = 0, Cantidad = 5 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45442", Precio = 0, Cantidad = 30 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45444", Precio = 0, Cantidad = 15 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45446", Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45461", Precio = 0, Cantidad = 10 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45447", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45448", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 245, Empresa = "1", Producto = "45449", Precio = 0, Cantidad = 1, GrupoAlternativa = 1 }
                }
            };
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

            // REGALO no debe contaminarse con la cantidad cobrada de FAMYPROD
            Assert.AreEqual(1, ofertaRegalo.cantidadOferta);
            Assert.AreEqual(0, ofertaRegalo.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_RegaloConProductoMismoPrecioNoCogeCantidadDelOtro()
        {
            // Escenario: producto regalo (Ganavision) con BaseImponible=0 comparte PVP y Familia
            // con otro producto cobrado. MontarOfertaPedido del regalo no debe incluir la cantidad
            // cobrada del otro producto, para evitar que ValidadorOfertasPermitidas lo trate como oferta.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();

            // Producto cobrado (genera Ganavisiones)
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "COBRADO",
                AplicarDescuento = false,
                Cantidad = 6,
                PrecioUnitario = 10
            });

            // Producto regalo (Ganavision) - mismo PVP y Familia
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "40133",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            });

            PrecioDescuentoProducto ofertaRegalo = GestorOfertasPedido.MontarOfertaPedido("40133", pedido);

            // El regalo debe tener cantidadOferta=1 y cantidad=0 (no contaminar con COBRADO)
            Assert.AreEqual(1, ofertaRegalo.cantidadOferta);
            Assert.AreEqual(0, ofertaRegalo.cantidad);
        }

        [TestMethod]
        public void GestorPrecios_MontarOfertaProducto_OfertaMismoProductoCantidadesCorrectas()
        {
            // Escenario: oferta 2+1 del mismo producto (2 cobradas, 1 gratis)
            // La cantidad de la oferta debe ser correcta incluso con la corrección
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();

            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD1",
                AplicarDescuento = false,
                Cantidad = 2,
                PrecioUnitario = 10
            });
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = "PROD1",
                AplicarDescuento = false,
                Cantidad = 1,
                PrecioUnitario = 0
            });

            PrecioDescuentoProducto oferta = GestorOfertasPedido.MontarOfertaPedido("PROD1", pedido);

            // Oferta 2+1 del mismo producto
            Assert.AreEqual(1, oferta.cantidadOferta);
            Assert.AreEqual(2, oferta.cantidad);
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

        // NestoAPI#203: ValidadorDescuentoTiendaOnline acepta el descuento del voucher
        // de Prestashop (5% o 15% exactos) cuando TODO el pedido viene de la tienda online.

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_DescuentoCincoPorCientoEnPedidoTodoWeb_LoAcepta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.05M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "Un descuento del 5% en un pedido WEB (voucher de Prestashop) debe estar autorizado. Motivo recibido: " + respuesta.Motivo);
            Assert.IsTrue(respuesta.Motivo.Contains("voucher"),
                "El motivo debe explicar que el descuento se autoriza por venir de tienda online. Recibido: " + respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_DescuentoQuincePorCientoEnPedidoTodoWeb_LoAcepta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.15M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "Un descuento del 15% en un pedido WEB (voucher de Prestashop) debe estar autorizado. Motivo recibido: " + respuesta.Motivo);
        }

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_DescuentoMenorACincoEnPedidoTodoWeb_NoLoAcepta()
        {
            // Solo el 5% y el 15% EXACTOS están autorizados: un 3% (u otro valor intermedio
            // por debajo del 5%) debe pasar por revisión manual.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.03M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Un 3% no es 5% ni 15%: no debe autorizarse automáticamente.");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_DescuentoEntreCincoYQuinceEnPedidoTodoWeb_NoLoAcepta()
        {
            // No es un tramo: un 10% (entre el 5% y el 15%) debe ir a revisión manual.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.10M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Un 10% no es 5% ni 15%: no debe autorizarse automáticamente.");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_DescuentoSuperiorACincoEnPedidoTodoWeb_NoLoAcepta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO linea = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.07M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(linea);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Un descuento superior al 5% en pedido WEB no debe autorizarse automáticamente: queremos que pase por revisión manual.");
        }

        [TestMethod]
        public void GestorPrecios_ValidadorDescuentoTiendaOnline_PedidoMixtoWebYOtroCanal_NoLoAcepta()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.empresa = "1";
            pedido.cliente = "5";
            pedido.contacto = "0";
            LineaPedidoVentaDTO lineaWeb = new LineaPedidoVentaDTO
            {
                Producto = "AA11",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoLinea = 0.05M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = Constantes.FormasVenta.TIENDA_ONLINE
            };
            pedido.Lineas.Add(lineaWeb);
            LineaPedidoVentaDTO lineaNoWeb = new LineaPedidoVentaDTO
            {
                Producto = "AA21",
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 21M,
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                formaVenta = "DIR"
            };
            pedido.Lineas.Add(lineaNoWeb);

            RespuestaValidacion respuesta = GestorPrecios.ComprobarValidadoresDeAceptacion(pedido, "AA11");

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Si alguna línea no es WEB, el descuento no viene de Prestashop y no se justifica automáticamente.");
        }
    }
}
