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
    /// Escalado de descuento por cantidad modelado como varias ofertas combinadas, una por tramo
    /// (ofertas reales "Allure by StarMask junio 2026" 247-250): las 4 referencias en un grupo de
    /// alternativas con Cantidad = tramo y Precio = 0, y el descuento del tramo controlado por el
    /// ImporteMinimo de la cabecera (1 und. 10 % = 40,45; 2 und. 15 % = 76,41; 3 und. 20 % = 107,88;
    /// 4 und. 25 % = 134,85; PVP 44,95).
    ///
    /// Bug (pedido real 919497, 11/06/26): con 3 referencias distintas a 1 unidad cada una al 20 %,
    /// el validador recorría las ofertas por orden de Id y elegía la del tramo 1 (también
    /// "satisfecha", porque los grupos exigen AL MENOS su cantidad y su importe mínimo también se
    /// cumplía de sobra). Después ProductoEnGrupoSobresurtido veía 3 unidades donde la oferta
    /// elegida solo cubre 1 y rechazaba el producto. Hay que elegir el tramo que mejor encaja
    /// (el más exigente que el pedido satisface), no el primero por Id.
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasTramosTests
    {
        private const string REF_45299 = "45299";
        private const string REF_45300 = "45300";
        private const string REF_45301 = "45301";
        private const string REF_45302 = "45302";
        private const decimal PVP = 44.95m;

        private IServicioPrecios _servicio;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();

            var ofertas = new List<OfertaCombinada>
            {
                CrearOfertaTramo(247, 1, 40.45m),
                CrearOfertaTramo(248, 2, 76.41m),
                CrearOfertaTramo(249, 3, 107.88m),
                CrearOfertaTramo(250, 4, 134.85m)
            };

            foreach (string referencia in new[] { REF_45299, REF_45300, REF_45301, REF_45302 })
            {
                A.CallTo(() => _servicio.BuscarOfertasCombinadas(referencia)).Returns(ofertas);
            }
        }

        private static OfertaCombinada CrearOfertaTramo(int id, int cantidad, decimal importeMinimo)
        {
            return new OfertaCombinada
            {
                Id = id,
                Empresa = "1",
                Nombre = $"Allure by StarMask junio 2026 ({cantidad} und.)",
                ImporteMinimo = importeMinimo,
                OfertasCombinadasDetalles = new[] { REF_45299, REF_45300, REF_45301, REF_45302 }
                    .Select(p => new OfertaCombinadaDetalle
                    {
                        OfertaId = id,
                        Empresa = "1",
                        Producto = p,
                        Precio = 0,
                        Cantidad = (short)cantidad,
                        GrupoAlternativa = 1
                    }).ToList()
            };
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal descuentoLinea)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = PVP,
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
        public void Tramos_TresReferenciasMezcladasAl20_SeAutoriza()
        {
            // El pedido real: 45299 + 45300 + 45302, 1 unidad de cada al 20 % (suma 107,88 = tramo 3).
            PedidoVentaDTO pedido = CrearPedido(
                Linea(REF_45299, 1, 0.2m),
                Linea(REF_45300, 1, 0.2m),
                Linea(REF_45302, 1, 0.2m));

            var validador = new ValidadorOfertasCombinadas();

            foreach (string referencia in new[] { REF_45299, REF_45300, REF_45302 })
            {
                RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, referencia, _servicio);
                Assert.IsTrue(respuesta.ValidacionSuperada,
                    $"3 unidades mezcladas al 20 % cumplen el tramo 3 y la referencia {referencia} debe autorizarse. Motivo: {respuesta.Motivo}");
            }
        }

        [TestMethod]
        public void Tramos_CuatroUnidadesAl25_SeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedido(
                Linea(REF_45299, 2, 0.25m),
                Linea(REF_45301, 1, 0.25m),
                Linea(REF_45302, 1, 0.25m));

            var validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REF_45301, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "4 unidades al 25 % cumplen el tramo 4 (importe 134,85). Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Tramos_UnaUnidadAl10_SeAutoriza()
        {
            // El caso que ya funcionaba antes del fix (el tramo 1 era el primero por Id) debe seguir igual.
            PedidoVentaDTO pedido = CrearPedido(Linea(REF_45300, 1, 0.1m));

            var validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REF_45300, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "1 unidad al 10 % cumple el tramo 1. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Tramos_DosUnidadesAl20_NoSeAutoriza()
        {
            // 2 unidades solo dan derecho al 15 %: al 20 % la suma (71,92) no llega al mínimo
            // de su tramo (76,41) y el tramo 3 exige 3 unidades.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(REF_45299, 1, 0.2m),
                Linea(REF_45300, 1, 0.2m));

            var validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REF_45299, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "2 unidades al 20 % no cumplen ningún tramo y no deben autorizarse");
        }

        [TestMethod]
        public void Tramos_TresUnidadesAl50_NoSeAutoriza()
        {
            // Un descuento por encima de cualquier tramo (50 %) no debe colarse por el tramo 1
            // (su importe mínimo, 40,45, sí se cumpliría con 67,43 €).
            PedidoVentaDTO pedido = CrearPedido(
                Linea(REF_45299, 1, 0.5m),
                Linea(REF_45300, 1, 0.5m),
                Linea(REF_45302, 1, 0.5m));

            var validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REF_45299, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "3 unidades al 50 % no llegan al importe mínimo del tramo 3 y no deben autorizarse");
        }
    }
}
