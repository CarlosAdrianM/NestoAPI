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
    /// Issue #282: ofertas combinadas con detalle por FILTRO (Familia + prefijo de nombre) además de
    /// por producto concreto. Caso real Lisap: comprar 36+36 tintes OPC (cualquier referencia de la
    /// familia cuyo nombre empiece por "LK OPC ") y 3+3 aguas oxigenadas → regalo 45473 + 45472.
    /// El "36+36" se configura como cantidad total (72) + importe mínimo por instancia (el precio de
    /// las 36 de pago a tarifa), sin distinguir filas de pago y de regalo.
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasFiltrosTests
    {
        private const string TINTE_1 = "31001";
        private const string TINTE_2 = "31002";
        private const string AGUA = "32001";
        private const string REGALO_1 = "45473";
        private const string REGALO_2 = "45472";
        private const string FILTRO_TINTES = "LK OPC ";
        private const string FILTRO_AGUAS = "AGUA OXIGENADA DEVELOPER ";
        private static readonly string[] PRODUCTOS_TINTE = { TINTE_1, TINTE_2 };
        private static readonly string[] PRODUCTOS_AGUA = { AGUA };

        private IServicioPrecios _servicio;
        private OfertaCombinada _oferta;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();

            // Tarifa: tinte 5 €, agua 4 € → 36 tintes + 3 aguas de pago = 192 € de importe mínimo.
            _oferta = new OfertaCombinada
            {
                Id = 300,
                Empresa = "1",
                ImporteMinimo = 192m,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = null, Familia = "Lisap", FiltroProducto = FILTRO_TINTES, Cantidad = 72, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = null, Familia = "Lisap", FiltroProducto = FILTRO_AGUAS, Cantidad = 6, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = REGALO_1, Cantidad = 1, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = REGALO_2, Cantidad = 1, Precio = 0 }
                }
            };

            foreach (string producto in PRODUCTOS_TINTE.Concat(PRODUCTOS_AGUA).Concat(new[] { REGALO_1, REGALO_2 }))
            {
                A.CallTo(() => _servicio.BuscarOfertasCombinadas(producto)).Returns(new List<OfertaCombinada> { _oferta });
            }

            // El matching real (familia + nombre empieza por el prefijo) lo hace FiltrarLineas contra
            // la BD; aquí lo simulamos: cada filtro casa con sus referencias del test.
            A.CallTo(() => _servicio.FiltrarLineas(A<PedidoVentaDTO>._, FILTRO_TINTES, A<string>._))
                .ReturnsLazily((PedidoVentaDTO p, string filtro, string familia) =>
                    p.Lineas.Where(l => PRODUCTOS_TINTE.Contains(l.Producto)).ToList());
            A.CallTo(() => _servicio.FiltrarLineas(A<PedidoVentaDTO>._, FILTRO_AGUAS, A<string>._))
                .ReturnsLazily((PedidoVentaDTO p, string filtro, string familia) =>
                    p.Lineas.Where(l => PRODUCTOS_AGUA.Contains(l.Producto)).ToList());
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precioUnitario, decimal descuentoLinea)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precioUnitario,
                DescuentoLinea = descuentoLinea,
                AplicarDescuento = true
            };
        }

        // Pedido que cumple la oferta: 36 tintes pagados (repartidos en 2 referencias) + 36 gratis,
        // 3 aguas pagadas + 3 gratis, y los dos regalos al 100 %.
        private PedidoVentaDTO CrearPedidoLisap()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(Linea(TINTE_1, 20, 5m, 0m));   // 100 €
            pedido.Lineas.Add(Linea(TINTE_2, 16, 5m, 0m));   // 80 €
            pedido.Lineas.Add(Linea(TINTE_1, 36, 5m, 1m));   // gratis
            pedido.Lineas.Add(Linea(AGUA, 3, 4m, 0m));       // 12 €
            pedido.Lineas.Add(Linea(AGUA, 3, 4m, 1m));       // gratis
            pedido.Lineas.Add(Linea(REGALO_1, 1, 2m, 1m));   // regalo
            pedido.Lineas.Add(Linea(REGALO_2, 1, 2m, 1m));   // regalo
            return pedido;
        }

        [TestMethod]
        public void OfertaPorFiltro_PedidoCompleto_AutorizaElRegalo()
        {
            PedidoVentaDTO pedido = CrearPedidoLisap();
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REGALO_1, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "Con 72 OPC + 6 aguas y el importe mínimo cubierto, el regalo debe autorizarse. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void OfertaPorFiltro_PedidoCompleto_AutorizaLosTintesGratis()
        {
            PedidoVentaDTO pedido = CrearPedidoLisap();
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, TINTE_1, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "Las unidades gratis del filtro las cubre la oferta. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void OfertaPorFiltro_SinLasAguas_NoAutorizaElRegalo()
        {
            PedidoVentaDTO pedido = CrearPedidoLisap();
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => l.Producto == AGUA).ToList())
            {
                pedido.Lineas.Remove(linea);
            }
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REGALO_1, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Sin las 6 aguas la condición del segundo filtro no se cumple");
        }

        [TestMethod]
        public void OfertaPorFiltro_ImporteInsuficiente_NoAutorizaElRegalo()
        {
            // Los 36 tintes de pago van al 50 % → 90 + 12 = 102 € < 192 € de importe mínimo.
            PedidoVentaDTO pedido = CrearPedidoLisap();
            foreach (LineaPedidoVentaDTO linea in pedido.Lineas.Where(l => PRODUCTOS_TINTE.Contains(l.Producto) && l.DescuentoLinea == 0m))
            {
                linea.DescuentoLinea = 0.5m;
            }
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REGALO_1, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Sin llegar al importe mínimo la oferta no debe validar");
        }

        [TestMethod]
        public void OfertaPorFiltro_TintesGratisDeMas_NoAutorizaElTinte()
        {
            // 82 unidades OPC cuando la oferta cubre 72: las 10 extra gratis son sobresurtido del filtro.
            PedidoVentaDTO pedido = CrearPedidoLisap();
            pedido.Lineas.Add(Linea(TINTE_2, 10, 5m, 1m));
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, TINTE_2, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Las unidades del filtro por encima de la cantidad de la oferta no las cubre la oferta");
        }

        [TestMethod]
        public void OfertaPorFiltro_TintesGratisDeMas_ElRegaloSigueAutorizado()
        {
            // El sobresurtido del filtro afecta a los productos del filtro, no tira la oferta entera.
            PedidoVentaDTO pedido = CrearPedidoLisap();
            pedido.Lineas.Add(Linea(TINTE_2, 10, 5m, 1m));
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REGALO_1, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "El regalo concreto sigue cubierto aunque el filtro vaya sobresurtido. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void OfertaPorFiltro_DobleInstancia_ExigeDobleImporteMinimo()
        {
            // 144 OPC + 12 aguas + 2 regalos de cada = 2 instancias, pero con el importe de una sola
            // (los segundos 36+3 de pago van gratis): el suelo es 2 × 192 € y no se llega.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(Linea(TINTE_1, 36, 5m, 0m));   // 180 €
            pedido.Lineas.Add(Linea(TINTE_1, 108, 5m, 1m));  // gratis
            pedido.Lineas.Add(Linea(AGUA, 3, 4m, 0m));       // 12 €
            pedido.Lineas.Add(Linea(AGUA, 9, 4m, 1m));       // gratis
            pedido.Lineas.Add(Linea(REGALO_1, 2, 2m, 1m));
            pedido.Lineas.Add(Linea(REGALO_2, 2, 2m, 1m));
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, REGALO_1, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Dos instancias de la oferta exigen dos veces el importe mínimo");
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_FamiliaYPrefijo_Casa()
        {
            OfertaCombinadaDetalle detalle = new OfertaCombinadaDetalle { Familia = "Lisap", FiltroProducto = "LK OPC " };
            Producto producto = new Producto { Familia = "Lisap     ", Nombre = "LK OPC 4/0 CASTAÑO MEDIO" };

            Assert.IsTrue(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_OtraFamilia_NoCasa()
        {
            OfertaCombinadaDetalle detalle = new OfertaCombinadaDetalle { Familia = "Lisap", FiltroProducto = "LK OPC " };
            Producto producto = new Producto { Familia = "Allure", Nombre = "LK OPC 4/0 CASTAÑO MEDIO" };

            Assert.IsFalse(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_FilaDeProductoConcreto_NoCasa()
        {
            OfertaCombinadaDetalle detalle = new OfertaCombinadaDetalle { Producto = REGALO_1, Familia = "Lisap" };
            Producto producto = new Producto { Familia = "Lisap", Nombre = "LK OPC 4/0" };

            Assert.IsFalse(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }
    }
}
