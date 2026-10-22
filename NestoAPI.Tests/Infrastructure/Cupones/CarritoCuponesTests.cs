using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosBase;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#614 (TNV#82): el cupón desde la app (CodigoCupon en la cabecera de api/Pedidos/Cliente, Portes y Ganavisiones),
    /// con el MISMO ServicioCanjeCupones que POST api/PedidosVenta: aplicado, mínimo sin cursos ni portes, de otro cliente, que no
    /// existe; y la respuesta Cupon {Aplicado, CodigoCupon, Importe, BaseImponible, ErrorCode, Mensaje, Falta}.
    /// </summary>
    [TestClass]
    public class CarritoCuponesTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 22, 10, 0, 0);
        private const string CODIGO = "EV-2610-7K3Q";

        private IRepositorioCupones repositorio;
        private CuponLeido cupon;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioCupones>();
            cupon = new CuponLeido
            {
                Id = 1,
                Codigo = CODIGO,
                Empresa = "1",
                Cliente = "15191",
                Contacto = "0",
                Importe = 50,
                Estado = EstadosCupon.ACTIVO,
                ValidoDesde = HOY.Date.AddDays(-1),
                Caducidad = HOY.Date.AddMonths(6),
                Evento = "Curso de cejas"
            };
            A.CallTo(() => repositorio.LeerPorCodigo(A<string>._)).Returns(Task.FromResult<CuponLeido>(null));
            A.CallTo(() => repositorio.LeerPorCodigo(CODIGO)).ReturnsLazily(() => Task.FromResult(cupon));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).Returns(Task.FromResult(
                new Dictionary<string, ProductoParaCupon>
                {
                    ["CUPONEVENTO"] = new ProductoParaCupon { Numero = "CUPONEVENTO", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
                    ["12345"] = new ProductoParaCupon { Numero = "12345", Grupo = "COS", Ficticio = false, IvaRepercutido = "G21" },
                    ["90077"] = new ProductoParaCupon { Numero = "90077", Grupo = "CUR", Ficticio = true, IvaRepercutido = "EX" }
                }));
            A.CallTo(() => repositorio.LeerPorcentajesIva(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new PorcentajesIvaCupon { Iva = 21, RecargoEquivalencia = 0 }));
        }

        private CarritoCupones Carrito() => new CarritoCupones(new ServicioCanjeCupones(repositorio, () => HOY, "CUPONEVENTO", () => false));

        private static PedidoVentaDTO Pedido(params (string producto, decimal precio, string iva)[] lineas)
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                cliente = "15191",
                contacto = "0",
                iva = "G21",
                Usuario = "luna@example.com",
                ParametrosIva = new List<ParametrosIvaBase>
                {
                    new ParametrosIvaBase { CodigoIvaProducto = "G21", PorcentajeIvaProducto = 0.21M },
                    new ParametrosIvaBase { CodigoIvaProducto = "EX", PorcentajeIvaProducto = 0M }
                }
            };
            foreach ((string producto, decimal precio, string iva) in lineas)
            {
                pedido.Lineas.Add(new LineaPedidoVentaDTO
                {
                    tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Producto = producto,
                    Cantidad = 1,
                    PrecioUnitario = precio,
                    iva = iva,
                    almacen = "ALG",
                    formaVenta = Constantes.FormasVenta.APP,
                    estado = Constantes.EstadosLineaVenta.EN_CURSO
                });
            }
            return pedido;
        }

        [TestMethod]
        public async Task Aplicado_LaLineaDescuentaElImporteExactoYLaRespuestaLoDice()
        {
            PedidoVentaDTO pedido = Pedido(("12345", 200M, "G21"));

            ResultadoCuponCarrito resultado = await Carrito().Preparar(pedido, " ev-2610-7k3q ", PedidosClienteController.RellenarPorcentajesIva);
            CuponCarritoDTO dto = resultado.ParaElCarrito();

            Assert.IsTrue(dto.Aplicado);
            Assert.AreEqual(CODIGO, dto.CodigoCupon);
            Assert.AreEqual(50M, dto.Importe);
            Assert.AreEqual(41.32M, dto.BaseImponible);
            Assert.IsNull(dto.ErrorCode);
            StringAssert.Contains(dto.Mensaje, "Su cupón de 50,00 €");
            LineaPedidoVentaDTO linea = pedido.Lineas.Single(l => l.EsCupon);
            Assert.AreEqual(-1, linea.Cantidad);
            Assert.AreEqual(CODIGO, linea.CodigoCupon, "PostPedidoVenta lo vuelve a comprobar y lo marca Usado");
            Assert.AreEqual(242M - 50M, Math.Round(pedido.Lineas.Sum(l => l.Total), 2), "el total ya lo lleva descontado");
        }

        [TestMethod]
        public async Task PedidoMinimo_SinContarLosCursos_ErrorConLoQueFalta()
        {
            // 60 € de productos (72,60 con IVA) + 150 € de curso: el curso no cuenta para el mínimo (100 €)
            PedidoVentaDTO pedido = Pedido(("12345", 60M, "G21"), ("90077", 150M, "EX"));

            ResultadoCuponCarrito resultado = await Carrito().Preparar(pedido, CODIGO, PedidosClienteController.RellenarPorcentajesIva);
            CuponCarritoDTO dto = resultado.ParaElCarrito();

            Assert.IsFalse(dto.Aplicado);
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, dto.ErrorCode);
            Assert.AreEqual(27.40M, dto.Falta);
            Assert.AreEqual(0M, dto.Importe);
            StringAssert.Contains(dto.Mensaje, "sin contar cursos ni portes");

            new CarritoCupones(new ServicioCanjeCupones(repositorio, () => HOY, "CUPONEVENTO", () => false)).QuitarLineasCupon(pedido);
            Assert.IsFalse(pedido.Lineas.Any(l => l.EsCupon), "el carrito se calcula sin él");
        }

        [TestMethod]
        public async Task DeOtroCliente_Rechazado()
        {
            PedidoVentaDTO pedido = Pedido(("12345", 200M, "G21"));
            pedido.cliente = "20000";

            ResultadoCuponCarrito resultado = await Carrito().Preparar(pedido, CODIGO, PedidosClienteController.RellenarPorcentajesIva);

            Assert.AreEqual(ReglasCupon.ERROR_OTRO_CLIENTE, resultado.ParaElCarrito().ErrorCode);
        }

        [TestMethod]
        public async Task NoActivo_Rechazado()
        {
            cupon.Estado = EstadosCupon.EMITIDO;
            cupon.ValidoDesde = HOY.Date.AddDays(5);

            ResultadoCuponCarrito resultado = await Carrito().Preparar(Pedido(("12345", 200M, "G21")), CODIGO, PedidosClienteController.RellenarPorcentajesIva);

            Assert.AreEqual(ReglasCupon.ERROR_TODAVIA_NO, resultado.ParaElCarrito().ErrorCode);
        }

        [TestMethod]
        public async Task QueNoExiste_Rechazado()
        {
            ResultadoCuponCarrito resultado = await Carrito().Preparar(Pedido(("12345", 200M, "G21")), "EV-2610-XXXX", PedidosClienteController.RellenarPorcentajesIva);

            Assert.AreEqual(ReglasCupon.ERROR_NO_EXISTE, resultado.ParaElCarrito().ErrorCode);
        }

        [TestMethod]
        public async Task SinCodigo_NadaQueHacer()
        {
            PedidoVentaDTO pedido = Pedido(("12345", 200M, "G21"));

            ResultadoCuponCarrito resultado = await Carrito().Preparar(pedido, null, PedidosClienteController.RellenarPorcentajesIva);

            Assert.IsNull(resultado.ParaElCarrito());
            Assert.AreEqual(1, pedido.Lineas.Count);
            A.CallTo(() => repositorio.LeerPorCodigo(A<string>._)).MustNotHaveHappened();
        }
    }
}
