using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    [TestClass]
    public class ServicioRecomendacionesPostCompraTests
    {
        #region FiltrarClientesValidos

        [TestMethod]
        public void FiltrarClientesValidos_ClienteConEmailYEstadoActivo_LoIncluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Cliente Activo", "cliente@test.com", estado: 0, codPostal: "28001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(1, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_ClienteEstado8_LoExcluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Cliente Baja", "cliente@test.com", estado: 8, codPostal: "28001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_ClienteSinEmail_LoExcluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Sin Email", null, estado: 0, codPostal: "28001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_ClienteEmailVacio_LoExcluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Email Vacío", "  ", estado: 0, codPostal: "28001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_CPEmpieza28_LoIncluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Madrid", "c@test.com", estado: 0, codPostal: "28100")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(1, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_CPEmpieza45_LoIncluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Toledo", "c@test.com", estado: 0, codPostal: "45001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(1, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_CPEmpieza19_LoIncluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Guadalajara", "c@test.com", estado: 0, codPostal: "19001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(1, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_CPDeBarcelona_LoExcluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Barcelona", "c@test.com", estado: 0, codPostal: "08001")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(0, resultado.Count);
        }

        [TestMethod]
        public void FiltrarClientesValidos_CPConEspacios_LoIncluye()
        {
            var clientes = new List<DatosClienteCorreo>
            {
                CrearCliente("00001", "Madrid espacios", "c@test.com", estado: 0, codPostal: "  28001  ")
            };

            var resultado = ServicioRecomendacionesPostCompra.FiltrarClientesValidos(clientes);

            Assert.AreEqual(1, resultado.Count);
        }

        #endregion

        #region SeleccionarTopProductos

        [TestMethod]
        public void SeleccionarTopProductos_DevuelveMaximo3Productos()
        {
            var lineas = new List<LineaAlbaranConVideo>
            {
                CrearLineaConVideo("PROD1", 100m),
                CrearLineaConVideo("PROD2", 90m),
                CrearLineaConVideo("PROD3", 80m),
                CrearLineaConVideo("PROD4", 70m)
            };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarTopProductos(lineas);

            Assert.AreEqual(3, resultado.Count);
        }

        [TestMethod]
        public void SeleccionarTopProductos_OrdenaPorBaseImponibleDescendente()
        {
            var lineas = new List<LineaAlbaranConVideo>
            {
                CrearLineaConVideo("PROD_BARATO", 10m),
                CrearLineaConVideo("PROD_CARO", 100m),
                CrearLineaConVideo("PROD_MEDIO", 50m)
            };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarTopProductos(lineas);

            Assert.AreEqual("PROD_CARO", resultado[0].ProductoId);
            Assert.AreEqual("PROD_MEDIO", resultado[1].ProductoId);
            Assert.AreEqual("PROD_BARATO", resultado[2].ProductoId);
        }

        [TestMethod]
        public void SeleccionarTopProductos_AgrupaLineasDelMismoProducto()
        {
            var lineas = new List<LineaAlbaranConVideo>
            {
                CrearLineaConVideo("PROD1", 50m),
                CrearLineaConVideo("PROD1", 30m),
                CrearLineaConVideo("PROD2", 70m)
            };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarTopProductos(lineas);

            Assert.AreEqual(2, resultado.Count);
            Assert.AreEqual("PROD1", resultado[0].ProductoId);
            Assert.AreEqual(80m, resultado[0].BaseImponibleTotal);
        }

        [TestMethod]
        public void SeleccionarTopProductos_SinLineas_DevuelveListaVacia()
        {
            var lineas = new List<LineaAlbaranConVideo>();

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarTopProductos(lineas);

            Assert.AreEqual(0, resultado.Count);
        }

        #endregion

        #region SeleccionarVideoMasReciente

        [TestMethod]
        public void SeleccionarVideoMasReciente_DevuelveElMasRecientePorFechaPublicacion()
        {
            var videos = new List<DatosVideoProducto>
            {
                new DatosVideoProducto { VideoYoutubeId = "viejo", VideoTitulo = "Video Viejo", FechaPublicacion = new DateTime(2025, 1, 1), EnlaceVideo = "link1" },
                new DatosVideoProducto { VideoYoutubeId = "nuevo", VideoTitulo = "Video Nuevo", FechaPublicacion = new DateTime(2026, 3, 1), EnlaceVideo = "link2" },
                new DatosVideoProducto { VideoYoutubeId = "medio", VideoTitulo = "Video Medio", FechaPublicacion = new DateTime(2025, 6, 1), EnlaceVideo = "link3" }
            };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarVideoMasReciente(videos);

            Assert.AreEqual("nuevo", resultado.VideoYoutubeId);
            Assert.AreEqual("link2", resultado.EnlaceVideo);
        }

        [TestMethod]
        public void SeleccionarVideoMasReciente_SinVideos_DevuelveNull()
        {
            var videos = new List<DatosVideoProducto>();

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarVideoMasReciente(videos);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void SeleccionarVideoMasReciente_VideoSinFecha_LoConsideraUltimo()
        {
            var videos = new List<DatosVideoProducto>
            {
                new DatosVideoProducto { VideoYoutubeId = "sinFecha", VideoTitulo = "Sin Fecha", FechaPublicacion = null, EnlaceVideo = "link1" },
                new DatosVideoProducto { VideoYoutubeId = "conFecha", VideoTitulo = "Con Fecha", FechaPublicacion = new DateTime(2025, 1, 1), EnlaceVideo = "link2" }
            };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarVideoMasReciente(videos);

            Assert.AreEqual("conFecha", resultado.VideoYoutubeId);
        }

        #endregion

        #region SeleccionarProductosRecomendados

        [TestMethod]
        public void SeleccionarProductosRecomendados_ExcluyeProductosYaComprados()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = "COMPRADO1", NombreProducto = "Ya Comprado", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link1" },
                new DatosProductoEnVideo { ProductoId = "NUEVO1", NombreProducto = "Producto Nuevo", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link2" }
            };
            var productosCompradosHistorico = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "COMPRADO1" };
            var productosPrincipales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, productosCompradosHistorico, productosPrincipales,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("NUEVO1", resultado[0].ProductoId);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_ExcluyeProductosPrincipales()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = "PRINCIPAL1", NombreProducto = "Principal", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link1" },
                new DatosProductoEnVideo { ProductoId = "OTRO1", NombreProducto = "Otro", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link2" }
            };
            var productosCompradosHistorico = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var productosPrincipales = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PRINCIPAL1" };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, productosCompradosHistorico, productosPrincipales,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("OTRO1", resultado[0].ProductoId);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_DevuelveMaximo4()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>();
            for (int i = 1; i <= 6; i++)
            {
                productosEnVideos.Add(new DatosProductoEnVideo
                {
                    ProductoId = $"PROD{i}",
                    NombreProducto = $"Producto {i}",
                    VideoYoutubeId = "v1",
                    VideoTitulo = "V1",
                    EnlaceVideo = $"link{i}"
                });
            }
            var productosCompradosHistorico = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var productosPrincipales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, productosCompradosHistorico, productosPrincipales,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(4, resultado.Count);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_NoDuplicaProductosDeVariosVideos()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = "PROD1", NombreProducto = "Producto 1", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link1" },
                new DatosProductoEnVideo { ProductoId = "PROD1", NombreProducto = "Producto 1", VideoYoutubeId = "v2", VideoTitulo = "V2", EnlaceVideo = "link2" },
                new DatosProductoEnVideo { ProductoId = "PROD2", NombreProducto = "Producto 2", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link3" }
            };
            var productosCompradosHistorico = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var productosPrincipales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, productosCompradosHistorico, productosPrincipales,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(2, resultado.Count);
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_ProductoIdNullOVacio_LoExcluye()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = null, NombreProducto = "Sin ID", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link1" },
                new DatosProductoEnVideo { ProductoId = "  ", NombreProducto = "ID Vacío", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link2" },
                new DatosProductoEnVideo { ProductoId = "VALIDO", NombreProducto = "Válido", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link3" }
            };
            var productosCompradosHistorico = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var productosPrincipales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, productosCompradosHistorico, productosPrincipales,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("VALIDO", resultado[0].ProductoId);
        }

        #endregion

        #region EsProductoRecomendable

        [TestMethod]
        public void EsProductoRecomendable_MismaFamiliaQueComprado_SiEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "ANUBIS", TipoExclusiva = "MAD" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsTrue(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_FamiliaPRP_SiEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "EVA_VISNU", TipoExclusiva = "PRP" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsTrue(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_FamiliaNAC_SiEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "MAX2", TipoExclusiva = "NAC" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsTrue(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_FamiliaMADDistinta_NoEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "MAYSTAR", TipoExclusiva = "MAD" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsFalse(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_FamiliaNIG_NoEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "FAMAFAHRE", TipoExclusiva = "NIG" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsFalse(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_SinFamiliasCompradas_SiEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = "MAYSTAR", TipoExclusiva = "MAD" };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void EsProductoRecomendable_SinTipoExclusivaNiFamilia_NoEsRecomendable()
        {
            var producto = new DatosProductoEnVideo { Familia = null, TipoExclusiva = null };
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            Assert.IsFalse(ServicioRecomendacionesPostCompra.EsProductoRecomendable(producto, familiasCompradas));
        }

        [TestMethod]
        public void SeleccionarProductosRecomendados_ConFiltroExclusiva_SoloIncluyePermitidos()
        {
            var productosEnVideos = new List<DatosProductoEnVideo>
            {
                new DatosProductoEnVideo { ProductoId = "P1", NombreProducto = "Misma Familia", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link1", Familia = "ANUBIS", TipoExclusiva = "MAD" },
                new DatosProductoEnVideo { ProductoId = "P2", NombreProducto = "Propia", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link2", Familia = "EVA", TipoExclusiva = "PRP" },
                new DatosProductoEnVideo { ProductoId = "P3", NombreProducto = "Nacional", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link3", Familia = "MAX2", TipoExclusiva = "NAC" },
                new DatosProductoEnVideo { ProductoId = "P4", NombreProducto = "Exclusiva MAD", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link4", Familia = "MAYSTAR", TipoExclusiva = "MAD" },
                new DatosProductoEnVideo { ProductoId = "P5", NombreProducto = "Exclusiva NIG", VideoYoutubeId = "v1", VideoTitulo = "V1", EnlaceVideo = "link5", Familia = "FAMA", TipoExclusiva = "NIG" }
            };
            var comprados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var principales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var familiasCompradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ANUBIS" };

            var resultado = ServicioRecomendacionesPostCompra.SeleccionarProductosRecomendados(
                productosEnVideos, comprados, principales, familiasCompradas);

            Assert.AreEqual(3, resultado.Count);
            Assert.IsTrue(resultado.Any(r => r.ProductoId == "P1")); // Misma familia
            Assert.IsTrue(resultado.Any(r => r.ProductoId == "P2")); // PRP
            Assert.IsTrue(resultado.Any(r => r.ProductoId == "P3")); // NAC
        }

        #endregion

        #region LimpiarParametrosUtm

        [TestMethod]
        public void LimpiarParametrosUtm_UrlConUtm_LosElimina()
        {
            var url = "https://tienda.com/producto.html?utm_source=nuevavision&utm_medium=email&utm_campaign=postcompra";

            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(url);

            Assert.AreEqual("https://tienda.com/producto.html", resultado);
        }

        [TestMethod]
        public void LimpiarParametrosUtm_UrlConUtmYOtrosParametros_SoloEliminaUtm()
        {
            var url = "https://tienda.com/producto.html?id=123&utm_source=nuevavision&color=rojo&utm_medium=email";

            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(url);

            Assert.AreEqual("https://tienda.com/producto.html?id=123&color=rojo", resultado);
        }

        [TestMethod]
        public void LimpiarParametrosUtm_UrlSinUtm_DevuelveIgual()
        {
            var url = "https://tienda.com/producto.html?id=123";

            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(url);

            Assert.AreEqual("https://tienda.com/producto.html?id=123", resultado);
        }

        [TestMethod]
        public void LimpiarParametrosUtm_UrlSinQueryString_DevuelveIgual()
        {
            var url = "https://tienda.com/producto.html";

            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(url);

            Assert.AreEqual("https://tienda.com/producto.html", resultado);
        }

        [TestMethod]
        public void LimpiarParametrosUtm_UrlNull_DevuelveNull()
        {
            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void LimpiarParametrosUtm_UrlVacia_DevuelveVacia()
        {
            var resultado = ServicioRecomendacionesPostCompra.LimpiarParametrosUtm("");

            Assert.AreEqual("", resultado);
        }

        #endregion

        #region GenerarUrlBusquedaTienda

        [TestMethod]
        public void GenerarUrlBusquedaTienda_NombreProducto_DevuelveUrlBusqueda()
        {
            var resultado = ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda("Crema Hidratante Anubis");

            Assert.IsTrue(resultado.StartsWith("https://www.productosdeesteticaypeluqueriaprofesional.com/buscar?controller=search&s="));
            Assert.IsTrue(resultado.Contains("Crema"));
        }

        [TestMethod]
        public void GenerarUrlBusquedaTienda_NombreNull_DevuelveNull()
        {
            var resultado = ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda(null);

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void GenerarUrlBusquedaTienda_NombreVacio_DevuelveNull()
        {
            var resultado = ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda("   ");

            Assert.IsNull(resultado);
        }

        [TestMethod]
        public void GenerarUrlBusquedaTienda_NombreConEspacios_LosCodea()
        {
            var resultado = ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda("Crema Facial");

            Assert.IsTrue(resultado.Contains("Crema%20Facial"));
        }

        #endregion

        #region Helpers

        private DatosClienteCorreo CrearCliente(string clienteId, string nombre, string email, int estado, string codPostal)
        {
            return new DatosClienteCorreo
            {
                ClienteId = clienteId,
                Nombre = nombre,
                Email = email,
                Estado = estado,
                CodPostal = codPostal
            };
        }

        private LineaAlbaranConVideo CrearLineaConVideo(string productoId, decimal baseImponible)
        {
            return new LineaAlbaranConVideo
            {
                ProductoId = productoId,
                NombreProducto = $"Producto {productoId}",
                BaseImponible = baseImponible
            };
        }

        #endregion
    }
}
