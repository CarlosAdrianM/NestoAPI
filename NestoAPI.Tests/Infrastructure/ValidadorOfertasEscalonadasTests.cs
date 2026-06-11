using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Ofertas escalonadas (Issue #226): una lista de referencias combinables entre sí con un
    /// escalado de descuento por cantidad total. Caso real que lo motiva: 11 referencias
    /// (44707-44714, 44951-44953) con tramos 2 und -> 5 %, 3 -> 10 %, 4 -> 15 %, 5 -> 20 %,
    /// 6 o más -> 25 %. Los tramos son cantidad MÍNIMA: 7 unidades llevan el 25 % en las 7
    /// (con el patrón antiguo de ofertas combinadas duplicadas esto era imposible).
    /// </summary>
    [TestClass]
    public class ValidadorOfertasEscalonadasTests
    {
        private static readonly string[] Referencias =
        {
            "44707", "44708", "44709", "44710", "44711", "44712", "44713", "44714", "44951", "44952", "44953"
        };
        private const decimal PRECIO_BASE = 18.50m;
        private const decimal PRECIO_BASE_44951 = 24.95m;

        private IServicioPrecios _servicio;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();

            var oferta = new OfertaEscalonada
            {
                Id = 300,
                Empresa = "1",
                Nombre = "Escalado StarPil junio 2026",
                OfertasEscalonadasProductos = Referencias.Select(r => new OfertaEscalonadaProducto
                {
                    OfertaId = 300,
                    Empresa = "1",
                    Producto = r,
                    PrecioBase = r == "44951" ? PRECIO_BASE_44951 : PRECIO_BASE
                }).ToList(),
                OfertasEscalonadasTramos = new List<OfertaEscalonadaTramo>
                {
                    new OfertaEscalonadaTramo { OfertaId = 300, CantidadMinima = 2, Descuento = 0.05m },
                    new OfertaEscalonadaTramo { OfertaId = 300, CantidadMinima = 3, Descuento = 0.10m },
                    new OfertaEscalonadaTramo { OfertaId = 300, CantidadMinima = 4, Descuento = 0.15m },
                    new OfertaEscalonadaTramo { OfertaId = 300, CantidadMinima = 5, Descuento = 0.20m },
                    new OfertaEscalonadaTramo { OfertaId = 300, CantidadMinima = 6, Descuento = 0.25m }
                }
            };

            var lista = new List<OfertaEscalonada> { oferta };
            foreach (string referencia in Referencias)
            {
                A.CallTo(() => _servicio.BuscarOfertasEscalonadas(referencia)).Returns(lista);
            }
            A.CallTo(() => _servicio.BuscarOfertasEscalonadas("99999")).Returns(new List<OfertaEscalonada>());
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precioUnitario, decimal descuentoLinea)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                DescuentoLinea = descuentoLinea,
                AplicarDescuento = false
            };
        }

        private static PedidoVentaDTO CrearPedido(params LineaPedidoVentaDTO[] lineas)
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "9130";
            pedido.contacto = "0";
            foreach (var l in lineas)
            {
                pedido.Lineas.Add(l);
            }
            return pedido;
        }

        [TestMethod]
        public void Escalonada_DosReferenciasDistintasAl5_SeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 1, PRECIO_BASE, 0.05m),
                Linea("44708", 1, PRECIO_BASE, 0.05m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "2 unidades mezcladas alcanzan el tramo 2 (5 %). Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Escalonada_SeisUnidadesMezcladasAl25_SeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 2, PRECIO_BASE, 0.25m),
                Linea("44710", 2, PRECIO_BASE, 0.25m),
                Linea("44953", 2, PRECIO_BASE, 0.25m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44710", _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "6 unidades alcanzan el tramo 6 (25 %). Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Escalonada_SieteUnidadesAl25_SeAutorizaPorqueElTramoEsCantidadMinima()
        {
            // La razón de ser del modelo: el tramo superior es "6 o más", no exactamente 6.
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 3, PRECIO_BASE, 0.25m),
                Linea("44710", 2, PRECIO_BASE, 0.25m),
                Linea("44953", 2, PRECIO_BASE, 0.25m));

            var validador = new ValidadorOfertasEscalonadas();
            foreach (string referencia in new[] { "44707", "44710", "44953" })
            {
                RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, referencia, _servicio);
                Assert.IsTrue(respuesta.ValidacionSuperada,
                    $"7 unidades superan el tramo 6 y el 25 % debe aplicar a las 7 ({referencia}). Motivo: {respuesta.Motivo}");
            }
        }

        [TestMethod]
        public void Escalonada_ReferenciasConPreciosBaseDistintos_CadaUnaConSuSuelo_SeAutoriza()
        {
            // 5 unidades al 20 %: la 44951 tiene un precio base distinto y su suelo es el suyo.
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 2, PRECIO_BASE, 0.20m),
                Linea("44708", 2, PRECIO_BASE, 0.20m),
                Linea("44951", 1, PRECIO_BASE_44951, 0.20m));

            var validador = new ValidadorOfertasEscalonadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, "44951", _servicio).ValidacionSuperada,
                "La 44951 al 20 % sobre SU precio base debe autorizarse");
            Assert.IsTrue(validador.EsPedidoValido(pedido, "44707", _servicio).ValidacionSuperada,
                "La 44707 al 20 % sobre su precio base debe autorizarse");
        }

        [TestMethod]
        public void Escalonada_PrecioYaRebajadoSinDescuentoLinea_SeAutoriza()
        {
            // El vendedor puede teclear directamente el precio neto (18,50 × 0,95 = 17,575 ≈ 17,58).
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 1, 17.58m, 0m),
                Linea("44708", 1, 17.58m, 0m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "El precio neto tecleado directamente debe valer igual que el descuento de línea. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Escalonada_UnaSolaUnidad_NoSeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedido(Linea("44707", 1, PRECIO_BASE, 0.05m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "1 unidad no llega al primer tramo (2) y no debe autorizarse");
        }

        [TestMethod]
        public void Escalonada_TresUnidadesAl25_NoSeAutoriza()
        {
            // 3 unidades solo dan derecho al 10 %.
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 2, PRECIO_BASE, 0.25m),
                Linea("44708", 1, PRECIO_BASE, 0.25m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "3 unidades al 25 % exceden el descuento del tramo 3 (10 %) y no deben autorizarse");
        }

        [TestMethod]
        public void Escalonada_UnRegaloNoCuentaParaAlcanzarElTramo_NoSeAutoriza()
        {
            // 1 unidad al 5 % + 1 unidad regalada (100 %): el regalo no puede contar para
            // desbloquear el tramo 2, porque esa unidad no se está pagando al precio del tramo.
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 1, PRECIO_BASE, 0.05m),
                Linea("44708", 1, PRECIO_BASE, 1m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "La unidad regalada no debe contar para el tramo: solo hay 1 unidad pagada");
        }

        [TestMethod]
        public void Escalonada_ProductoSinOfertaEscalonada_NoSeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedido(
                Linea("99999", 2, PRECIO_BASE, 0.05m),
                Linea("44707", 2, PRECIO_BASE, 0.05m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "99999", _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Un producto que no está en ninguna oferta escalonada no debe autorizarse");
        }

        [TestMethod]
        public void Escalonada_DescuentoDePedidoEnVariasLineasDelMismoProducto_SeAutoriza()
        {
            // La cantidad se agrega por producto aunque venga repartida en varias líneas.
            PedidoVentaDTO pedido = CrearPedido(
                Linea("44707", 1, PRECIO_BASE, 0.05m),
                Linea("44707", 1, PRECIO_BASE, 0.05m));

            var validador = new ValidadorOfertasEscalonadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, "44707", _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "2 líneas de 1 unidad del mismo producto suman el tramo 2. Motivo: " + respuesta.Motivo);
        }
    }
}
