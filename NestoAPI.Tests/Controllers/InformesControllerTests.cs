using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;

namespace NestoAPI.Tests.Controllers
{
    [TestClass]
    public class InformesControllerTests
    {
        private IInformesService _servicio;
        private InformesController _controller;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IInformesService>();
            _controller = new InformesController(_servicio);
            _controller.User = new GenericPrincipal(new GenericIdentity("testuser"), null);
        }

        [TestMethod]
        public async Task GetResumenVentas_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 1, 1);
            DateTime hasta = new DateTime(2026, 3, 31);
            bool soloFacturas = true;

            A.CallTo(() => _servicio.LeerResumenVentasAsync(desde, hasta, soloFacturas))
                .Returns(new List<ResumenVentasDTO>());

            await _controller.GetResumenVentas(desde, hasta, soloFacturas);

            A.CallTo(() => _servicio.LeerResumenVentasAsync(desde, hasta, soloFacturas))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetResumenVentas_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ResumenVentasDTO>
            {
                new ResumenVentasDTO
                {
                    Grupo = "NV",
                    Vendedor = "AM",
                    NombreVendedor = "Ana Martínez",
                    VtaNV = 1500m,
                    VtaCV = 200m,
                    VtaVC = 50m,
                    VtaUL = 0m,
                    VtaTotal = 1750m
                },
                new ResumenVentasDTO
                {
                    Grupo = "CV",
                    Vendedor = "JG",
                    NombreVendedor = "Juan García",
                    VtaNV = 0m,
                    VtaCV = 800m,
                    VtaVC = 100m,
                    VtaUL = 50m,
                    VtaTotal = 950m
                }
            };

            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31), false);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ResumenVentasDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ResumenVentasDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual("Ana Martínez", okResult.Content[0].NombreVendedor);
            Assert.AreEqual(1750m, okResult.Content[0].VtaTotal);
            Assert.AreEqual("Juan García", okResult.Content[1].NombreVendedor);
        }

        [TestMethod]
        public async Task GetResumenVentas_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Returns(new List<ResumenVentasDTO>());

            var resultado = await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), true);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ResumenVentasDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ResumenVentasDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetResumenVentas_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerResumenVentasAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<bool>.Ignored))
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetResumenVentas(new DateTime(2026, 1, 1), new DateTime(2026, 3, 31), false);
        }

        [TestMethod]
        public void InformesController_TieneAuthorizeAttribute()
        {
            var authorizeAttributes = typeof(InformesController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);

            Assert.IsTrue(authorizeAttributes.Length > 0,
                "InformesController debe tener [Authorize] a nivel de clase");
        }

        // ----- ControlPedidos (1A.2) -----

        [TestMethod]
        public async Task GetControlPedidos_LlamaAlServicio()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(new List<ControlPedidosDTO>());

            await _controller.GetControlPedidos();

            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetControlPedidos_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ControlPedidosDTO>
            {
                new ControlPedidosDTO
                {
                    Pedido = 12345,
                    Producto = "38697",
                    Ruta = "MAD",
                    Cliente = "15191",
                    Vendedor = "AM",
                    Nombre = "Crema Hidratante",
                    Familia = "Eva Visnú",
                    CantidadPedido = 2,
                    CantidadTotal = 5
                },
                new ControlPedidosDTO
                {
                    Pedido = 12346,
                    Producto = "12345",
                    Ruta = "BCN",
                    Cliente = "20001",
                    Vendedor = "JG",
                    Nombre = "Champú",
                    Familia = "Lisap",
                    CantidadPedido = 1,
                    CantidadTotal = 1
                }
            };

            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(lista);

            var resultado = await _controller.GetControlPedidos();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ControlPedidosDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ControlPedidosDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual(12345, okResult.Content[0].Pedido);
            Assert.AreEqual("Crema Hidratante", okResult.Content[0].Nombre);
            Assert.AreEqual(5, okResult.Content[0].CantidadTotal);
            Assert.AreEqual("Champú", okResult.Content[1].Nombre);
        }

        [TestMethod]
        public async Task GetControlPedidos_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Returns(new List<ControlPedidosDTO>());

            var resultado = await _controller.GetControlPedidos();

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ControlPedidosDTO>>));
            var okResult = (OkNegotiatedContentResult<List<ControlPedidosDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetControlPedidos_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerControlPedidosAsync())
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetControlPedidos();
        }

        // ----- DetalleRapports (1A.3) -----

        [TestMethod]
        public async Task GetDetalleRapports_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 4, 1);
            DateTime hasta = new DateTime(2026, 4, 10);
            string listaVendedores = "AM,JG,MR";

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(desde, hasta, listaVendedores))
                .Returns(new List<DetalleRapportsDTO>());

            await _controller.GetDetalleRapports(desde, hasta, listaVendedores);

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(desde, hasta, listaVendedores))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetDetalleRapports_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<DetalleRapportsDTO>
            {
                new DetalleRapportsDTO
                {
                    Usuario = "AM",
                    Empresa = "1",
                    NombreEmpresa = "Nueva Visión",
                    Cliente = "15191",
                    Direccion = "Calle Mayor 1",
                    Comentarios = "Llamada para reposición",
                    HoraLlamada = new DateTime(2026, 4, 10, 10, 30, 0),
                    EstadoCliente = 0,
                    AcumuladoMes = 1500,
                    Tipo = "Llamada",
                    Pedido = true,
                    Vendedor = "AM",
                    CodigoPostal = "28001",
                    Poblacion = "Madrid",
                    EstadoRapport = 9
                },
                new DetalleRapportsDTO
                {
                    Usuario = "JG",
                    Vendedor = "JG",
                    Cliente = "20001",
                    Pedido = false,
                    EstadoRapport = 9
                }
            };

            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "AM,JG");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<DetalleRapportsDTO>>));
            var okResult = (OkNegotiatedContentResult<List<DetalleRapportsDTO>>)resultado;
            Assert.AreEqual(2, okResult.Content.Count);
            Assert.AreEqual("Nueva Visión", okResult.Content[0].NombreEmpresa);
            Assert.AreEqual(1500, okResult.Content[0].AcumuladoMes);
            Assert.IsTrue(okResult.Content[0].Pedido.Value);
            Assert.AreEqual("JG", okResult.Content[1].Vendedor);
            Assert.IsFalse(okResult.Content[1].Pedido.Value);
        }

        [TestMethod]
        public async Task GetDetalleRapports_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Returns(new List<DetalleRapportsDTO>());

            var resultado = await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<DetalleRapportsDTO>>));
            var okResult = (OkNegotiatedContentResult<List<DetalleRapportsDTO>>)resultado;
            Assert.AreEqual(0, okResult.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetDetalleRapports_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerDetalleRapportsAsync(A<DateTime>.Ignored, A<DateTime>.Ignored, A<string>.Ignored))
                .Throws(new InvalidOperationException("Error en el SP"));

            await _controller.GetDetalleRapports(new DateTime(2026, 4, 1), new DateTime(2026, 4, 10), "AM");
        }

        // ----- ExtractoContable (1A.7) -----

        [TestMethod]
        public async Task GetExtractoContable_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 1, 1);
            DateTime hasta = new DateTime(2026, 3, 31);

            A.CallTo(() => _servicio.LeerExtractoContableAsync("1", "43000000", desde, hasta))
                .Returns(new List<ExtractoContableDTO>());

            await _controller.GetExtractoContable("1", "43000000", desde, hasta);

            A.CallTo(() => _servicio.LeerExtractoContableAsync("1", "43000000", desde, hasta))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetExtractoContable_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ExtractoContableDTO>
            {
                new ExtractoContableDTO
                {
                    Id = 1, Empresa = "1", Fecha = new DateTime(2026, 1, 15),
                    Documento = "FRA/100", Concepto = "Compra material",
                    Debe = 100m, Haber = 0m, Saldo = 100m,
                    Delegacion = "ALG", FormaVenta = "VAR"
                }
            };
            A.CallTo(() => _servicio.LeerExtractoContableAsync(A<string>.Ignored, A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetExtractoContable("1", "43000000", new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ExtractoContableDTO>>));
            var ok = (OkNegotiatedContentResult<List<ExtractoContableDTO>>)resultado;
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual(100m, ok.Content[0].Saldo);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetExtractoContable_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerExtractoContableAsync(A<string>.Ignored, A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored))
                .Throws(new InvalidOperationException("Error en la query"));

            await _controller.GetExtractoContable("1", "43000000", new DateTime(2026, 1, 1), new DateTime(2026, 3, 31));
        }

        // ----- UbicacionesInventario (1A.9) -----

        [TestMethod]
        public async Task GetUbicacionesInventario_PasaLaEmpresaAlServicio()
        {
            A.CallTo(() => _servicio.LeerUbicacionesInventarioAsync("1"))
                .Returns(new List<UbicacionesInventarioDTO>());

            await _controller.GetUbicacionesInventario("1");

            A.CallTo(() => _servicio.LeerUbicacionesInventarioAsync("1"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetUbicacionesInventario_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<UbicacionesInventarioDTO>
            {
                new UbicacionesInventarioDTO
                {
                    Pasillo = "A", Fila = "01", Columna = "03",
                    Producto = "12345", CodigoBarras = "8400000000001",
                    Nombre = "Crema manos", Tamanno = 100, UnidadMedida = "ml",
                    Familia = "Eva Visnu"
                }
            };
            A.CallTo(() => _servicio.LeerUbicacionesInventarioAsync(A<string>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetUbicacionesInventario("1");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<UbicacionesInventarioDTO>>));
            var ok = (OkNegotiatedContentResult<List<UbicacionesInventarioDTO>>)resultado;
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual("12345", ok.Content[0].Producto);
        }

        // ----- KitsQueSePuedenMontar (1A.10) -----

        [TestMethod]
        public async Task GetKitsQueSePuedenMontar_PasaLosParametrosCorrectosAlServicio()
        {
            A.CallTo(() => _servicio.LeerKitsQueSePuedenMontarAsync("1", "20/04/26", "ALG", "(ruta='AT ')"))
                .Returns(new List<KitsQueSePuedenMontarDTO>());

            await _controller.GetKitsQueSePuedenMontar("1", "20/04/26", "ALG", "(ruta='AT ')");

            A.CallTo(() => _servicio.LeerKitsQueSePuedenMontarAsync("1", "20/04/26", "ALG", "(ruta='AT ')"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetKitsQueSePuedenMontar_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<KitsQueSePuedenMontarDTO>
            {
                new KitsQueSePuedenMontarDTO
                {
                    Tipo = "NORMAL", Kit = "K-001", Nombre = "Pack promoción",
                    CantidadAMontar = 5, CodigoBarras = "8400000000002"
                }
            };
            A.CallTo(() => _servicio.LeerKitsQueSePuedenMontarAsync(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetKitsQueSePuedenMontar("1", "20/04/26", "ALG", "(ruta='AT ')");

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<KitsQueSePuedenMontarDTO>>));
            var ok = (OkNegotiatedContentResult<List<KitsQueSePuedenMontarDTO>>)resultado;
            Assert.AreEqual(5, ok.Content[0].CantidadAMontar);
        }

        // ----- MontarKitProductos (1A.11) -----

        [TestMethod]
        public async Task GetMontarKitProductos_PasaElTraspasoAlServicio()
        {
            A.CallTo(() => _servicio.LeerMontarKitProductosAsync(12345))
                .Returns(new List<MontarKitProductosDTO>());

            await _controller.GetMontarKitProductos(12345);

            A.CallTo(() => _servicio.LeerMontarKitProductosAsync(12345))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetMontarKitProductos_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<MontarKitProductosDTO>
            {
                new MontarKitProductosDTO
                {
                    Producto = "12345", Nombre = "Componente", Tamanno = 50,
                    UnidadMedida = "ml", Familia = "Lisap", Cantidad = 2,
                    Pasillo = "A", Fila = "01", Columna = "03",
                    CodigoBarras = "8400000000003"
                }
            };
            A.CallTo(() => _servicio.LeerMontarKitProductosAsync(A<int>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetMontarKitProductos(12345);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<MontarKitProductosDTO>>));
            var ok = (OkNegotiatedContentResult<List<MontarKitProductosDTO>>)resultado;
            Assert.AreEqual(2, ok.Content[0].Cantidad);
        }

        // ----- ManifiestoAgencia (1A.8) -----

        [TestMethod]
        public async Task GetManifiestoAgencia_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime fecha = new DateTime(2026, 4, 16);
            A.CallTo(() => _servicio.LeerManifiestoAgenciaAsync("1", 7, fecha))
                .Returns(new List<ManifiestoAgenciaDTO>());

            await _controller.GetManifiestoAgencia("1", 7, fecha);

            A.CallTo(() => _servicio.LeerManifiestoAgenciaAsync("1", 7, fecha))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetManifiestoAgencia_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ManifiestoAgenciaDTO>
            {
                new ManifiestoAgenciaDTO
                {
                    Cliente = "15191", Contacto = "0", Nombre = "Peluquería ABC",
                    Direccion = "Calle Mayor 1", CodigoPostal = "28001",
                    Poblacion = "Madrid", Provincia = "Madrid", Bultos = 2,
                    Reembolso = 0m, TelefonoFijo = "915551234",
                    TelefonoMovil = "600000001", Observaciones = ""
                }
            };
            A.CallTo(() => _servicio.LeerManifiestoAgenciaAsync(A<string>.Ignored, A<int>.Ignored, A<DateTime>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetManifiestoAgencia("1", 7, new DateTime(2026, 4, 16));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ManifiestoAgenciaDTO>>));
            var ok = (OkNegotiatedContentResult<List<ManifiestoAgenciaDTO>>)resultado;
            Assert.AreEqual("15191", ok.Content[0].Cliente);
            Assert.AreEqual(2, ok.Content[0].Bultos);
        }

        // ----- PedidoCompra (1A.6) -----

        [TestMethod]
        public async Task GetPedidoCompra_PasaLosParametrosCorrectosAlServicio()
        {
            A.CallTo(() => _servicio.LeerPedidoCompraAsync("1", 123456))
                .Returns(new PedidoCompraInformeDTO { Id = 123456 });

            await _controller.GetPedidoCompra("1", 123456);

            A.CallTo(() => _servicio.LeerPedidoCompraAsync("1", 123456))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetPedidoCompra_CuandoExiste_DevuelveOkConElPedido()
        {
            var dto = new PedidoCompraInformeDTO
            {
                Id = 123456, Proveedor = "999", Nombre = "Amazon EU",
                Fecha = new DateTime(2026, 3, 31), PedidoValorado = true,
                Lineas = new List<LineaPedidoCompraInformeDTO>
                {
                    new LineaPedidoCompraInformeDTO
                    {
                        NuestraReferencia = "60000100", Descripcion = "Operaciones",
                        Cantidad = 1, PrecioUnitario = 100m, BaseImponible = 100m
                    }
                }
            };
            A.CallTo(() => _servicio.LeerPedidoCompraAsync(A<string>.Ignored, A<int>.Ignored))
                .Returns(dto);

            var resultado = await _controller.GetPedidoCompra("1", 123456);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<PedidoCompraInformeDTO>));
            var ok = (OkNegotiatedContentResult<PedidoCompraInformeDTO>)resultado;
            Assert.AreEqual(123456, ok.Content.Id);
            Assert.AreEqual(1, ok.Content.Lineas.Count);
            Assert.AreEqual(100m, ok.Content.Lineas[0].PrecioUnitario);
        }

        [TestMethod]
        public async Task GetPedidoCompra_CuandoNoExiste_DevuelveNotFound()
        {
            A.CallTo(() => _servicio.LeerPedidoCompraAsync(A<string>.Ignored, A<int>.Ignored))
                .Returns((PedidoCompraInformeDTO)null);

            var resultado = await _controller.GetPedidoCompra("1", 999999);

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        // ----- ExtractoProveedor (Nesto#349 Fase 2a) -----

        [TestMethod]
        public async Task GetExtractoProveedor_PasaLosParametrosCorrectosAlServicio()
        {
            DateTime desde = new DateTime(2026, 2, 1);
            DateTime hasta = new DateTime(2026, 2, 28);
            A.CallTo(() => _servicio.LeerExtractoProveedorAsync("1", "999", desde, hasta))
                .Returns(new List<ExtractoProveedorDTO>());

            await _controller.GetExtractoProveedor("1", "999", desde, hasta);

            A.CallTo(() => _servicio.LeerExtractoProveedorAsync("1", "999", desde, hasta))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetExtractoProveedor_DevuelveOkConLaListaDelServicio()
        {
            var lista = new List<ExtractoProveedorDTO>
            {
                new ExtractoProveedorDTO
                {
                    Id = 1, Fecha = new DateTime(2026, 2, 15),
                    Documento = "FRA100", DocumentoProveedor = "AMZ-INV-0001",
                    Concepto = "Factura Amazon",
                    Importe = 123.45M, ImportePendiente = 0M,
                    TipoApunte = "1", FormaPago = "TRN",
                    Delegacion = "ALG"
                }
            };
            A.CallTo(() => _servicio.LeerExtractoProveedorAsync(
                    A<string>.Ignored, A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored))
                .Returns(lista);

            var resultado = await _controller.GetExtractoProveedor(
                "1", "999", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<List<ExtractoProveedorDTO>>));
            var ok = (OkNegotiatedContentResult<List<ExtractoProveedorDTO>>)resultado;
            Assert.AreEqual(1, ok.Content.Count);
            Assert.AreEqual("AMZ-INV-0001", ok.Content[0].DocumentoProveedor);
            Assert.AreEqual(123.45M, ok.Content[0].Importe);
        }

        [TestMethod]
        public async Task GetExtractoProveedor_CuandoServicioDevuelveListaVacia_DevuelveOkVacia()
        {
            A.CallTo(() => _servicio.LeerExtractoProveedorAsync(
                    A<string>.Ignored, A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored))
                .Returns(new List<ExtractoProveedorDTO>());

            var resultado = await _controller.GetExtractoProveedor(
                "1", "999", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

            var ok = resultado as OkNegotiatedContentResult<List<ExtractoProveedorDTO>>;
            Assert.IsNotNull(ok);
            Assert.AreEqual(0, ok.Content.Count);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public async Task GetExtractoProveedor_CuandoServicioLanzaExcepcion_LaPropaga()
        {
            A.CallTo(() => _servicio.LeerExtractoProveedorAsync(
                    A<string>.Ignored, A<string>.Ignored, A<DateTime>.Ignored, A<DateTime>.Ignored))
                .Throws(new InvalidOperationException("Error de BD"));

            await _controller.GetExtractoProveedor(
                "1", "999", new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));
        }
    }
}
