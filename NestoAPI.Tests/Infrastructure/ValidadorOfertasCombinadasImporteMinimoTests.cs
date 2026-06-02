using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Ofertas "Modellare Home Care" (239-242): cada una lleva una crema + el masajeador 45381
    /// (compartido por las 4) con un importe mínimo que es el precio de la crema al 25 % de dto,
    /// guardado redondeado a 2 decimales (10,84 / 13,09). Al comprar al descuento exacto, el
    /// importe real de la línea (redondeado una vez) se queda 1 céntimo por debajo de
    /// instancias × ImporteMinimo, y el pedido se rechazaba. Regresión de ese redondeo.
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasImporteMinimoTests
    {
        private const string C_22268 = "22268";
        private const string C_23130 = "23130";
        private const string C_23128 = "23128";
        private const string C_23132 = "23132";
        private const string MASAJEADOR = "45381";

        private IServicioPrecios _servicio;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();

            OfertaCombinada o239 = CrearOferta(239, C_22268, 13.09m);
            OfertaCombinada o240 = CrearOferta(240, C_23130, 10.84m);
            OfertaCombinada o241 = CrearOferta(241, C_23128, 10.84m);
            OfertaCombinada o242 = CrearOferta(242, C_23132, 10.84m);

            // Cada crema solo está en su oferta; el masajeador, en las cuatro.
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(C_22268)).Returns(new List<OfertaCombinada> { o239 });
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(C_23130)).Returns(new List<OfertaCombinada> { o240 });
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(C_23128)).Returns(new List<OfertaCombinada> { o241 });
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(C_23132)).Returns(new List<OfertaCombinada> { o242 });
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(MASAJEADOR)).Returns(new List<OfertaCombinada> { o239, o240, o241, o242 });
        }

        private static OfertaCombinada CrearOferta(int id, string crema, decimal importeMinimo)
        {
            return new OfertaCombinada
            {
                Id = id,
                Empresa = "1",
                ImporteMinimo = importeMinimo,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = id, Empresa = "1", Producto = crema, Precio = 0, Cantidad = 1 },
                    new OfertaCombinadaDetalle { OfertaId = id, Empresa = "1", Producto = MASAJEADOR, Precio = 0, Cantidad = 1 }
                }
            };
        }

        // BaseImponible se calcula: Round(PrecioUnitario × Cantidad × (1 - DescuentoLinea), 2).
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

        // El pedido de Sancho: 4 de cada crema al 25 % (PVP real) + 16 masajeadores de regalo (100 %).
        private static PedidoVentaDTO CrearPedidoSancho()
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            // 4 × 17,45 × 0,75 = 52,35 ; 4 × 14,45 × 0,75 = 43,35
            pedido.Lineas.Add(Linea(C_22268, 4, 17.45m, 0.25m));
            pedido.Lineas.Add(Linea(C_23130, 4, 14.45m, 0.25m));
            pedido.Lineas.Add(Linea(C_23128, 4, 14.45m, 0.25m));
            pedido.Lineas.Add(Linea(C_23132, 4, 14.45m, 0.25m));
            pedido.Lineas.Add(Linea(MASAJEADOR, 16, 3.95m, 1m));
            return pedido;
        }

        [TestMethod]
        public void Modellare_CuatroDeCadaCremaY16Masajeadores_LaCremaSeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedidoSancho();
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, C_23132, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "Comprar 4 cremas al 25 % (su descuento de oferta) debe autorizarse; falla por 1 céntimo de redondeo. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Modellare_CuatroDeCadaCremaY16Masajeadores_ElMasajeadorSeAutoriza()
        {
            PedidoVentaDTO pedido = CrearPedidoSancho();
            var validador = new ValidadorOfertasCombinadas();

            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, MASAJEADOR, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada,
                "El masajeador de regalo (100 % dto) debe autorizarse. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void Modellare_CremaConImporteClaramenteInsuficiente_NoSeAutoriza()
        {
            // Si la base imponible se queda MUY por debajo del mínimo (no es redondeo), debe rechazar.
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            pedido.Lineas.Add(Linea(C_23132, 4, 5m, 0m)); // 4 × 5 = 20 € << 43,36
            pedido.Lineas.Add(Linea(MASAJEADOR, 4, 3.95m, 1m));

            var validador = new ValidadorOfertasCombinadas();
            RespuestaValidacion respuesta = validador.EsPedidoValido(pedido, C_23132, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada,
                "Un importe muy por debajo del mínimo no debe pasar (la tolerancia es solo de redondeo)");
        }
    }
}
