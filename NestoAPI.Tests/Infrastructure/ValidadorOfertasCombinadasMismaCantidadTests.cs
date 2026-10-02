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
    /// Oferta 263 «Skin Primers Ainhoa» (pedido 927595, 02/10/26): desmaquillante 45687 + limpiador 45686 + diadema
    /// 45688, una unidad de cada (o seis de cada: la misma cantidad), a cualquier precio o descuento mientras sumen
    /// 22,22 € por juego. Los productos del detalle llegan de la BD con el relleno del char(15) («45687          »).
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasMismaCantidadTests
    {
        private const string DESMAQUILLANTE = "45687";
        private const string LIMPIADOR = "45686";
        private const string DIADEMA = "45688";

        private IServicioPrecios servicio;

        private void ConOferta(bool productosConRelleno, short diademasPorJuego = 1)
        {
            servicio = A.Fake<IServicioPrecios>();
            string P(string producto) => productosConRelleno ? producto.PadRight(15) : producto;
            var oferta = new OfertaCombinada
            {
                Id = 263, Empresa = "1", ImporteMinimo = 22.22M, RegalarMenorImporte = false, UnidadesRegaladas = 1,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 263, Empresa = "1", Producto = P(DESMAQUILLANTE), Cantidad = 1, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = 263, Empresa = "1", Producto = P(DIADEMA), Cantidad = diademasPorJuego, Precio = 0 },
                    new OfertaCombinadaDetalle { OfertaId = 263, Empresa = "1", Producto = P(LIMPIADOR), Cantidad = 1, Precio = 0 }
                }
            };
            A.CallTo(() => servicio.BuscarOfertasCombinadas(A<string>._)).Returns(new List<OfertaCombinada> { oferta });
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, decimal descuento = 0) => new LineaPedidoVentaDTO
        {
            tipoLinea = 1, Producto = producto, Cantidad = cantidad, PrecioUnitario = precio, DescuentoLinea = descuento, AplicarDescuento = true
        };

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "38628", contacto = "0" };
            foreach (var l in lineas) pedido.Lineas.Add(l);
            return pedido;
        }

        private bool Valida(PedidoVentaDTO pedido, string producto)
            => new ValidadorOfertasCombinadas().EsPedidoValido(pedido, producto, servicio).ValidacionSuperada;

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UnJuego_ConLaDiademaDeRegalo_Vale(bool conRelleno)
        {
            ConOferta(conRelleno);
            Assert.IsTrue(Valida(Pedido(Linea(DESMAQUILLANTE, 1, 13.69M), Linea(LIMPIADOR, 1, 11.00M), Linea(DIADEMA, 1, 4.20M, 1)), DIADEMA));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SeisJuegos_ACualquierPrecioQueSume_Vale(bool conRelleno)
        {
            ConOferta(conRelleno);
            // 6 × 13 + 6 × 9,50 = 135 ≥ 6 × 22,22
            Assert.IsTrue(Valida(Pedido(Linea(DESMAQUILLANTE, 6, 13.00M), Linea(LIMPIADOR, 6, 9.50M), Linea(DIADEMA, 6, 4.20M, 1)), DIADEMA));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void SeisJuegosQueNoSuman_NoVale(bool conRelleno)
        {
            ConOferta(conRelleno);
            // 6 × 10 + 6 × 10 = 120 < 133,32
            Assert.IsFalse(Valida(Pedido(Linea(DESMAQUILLANTE, 6, 10M), Linea(LIMPIADOR, 6, 10M), Linea(DIADEMA, 6, 4.20M, 1)), DIADEMA));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UnJuegoYSeisDiademas_NoVale(bool conRelleno)
        {
            ConOferta(conRelleno);
            Assert.IsFalse(Valida(Pedido(Linea(DESMAQUILLANTE, 1, 13.69M), Linea(LIMPIADOR, 1, 11.00M), Linea(DIADEMA, 6, 4.20M, 1)), DIADEMA));
        }
    
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DosDiademasPorJuego_SeRegalanEnProporcion(bool conRelleno)
        {
            ConOferta(conRelleno, diademasPorJuego: 2);
            Assert.IsTrue(Valida(Pedido(Linea(DESMAQUILLANTE, 1, 13.69M), Linea(LIMPIADOR, 1, 11.00M), Linea(DIADEMA, 2, 4.20M, 1)), DIADEMA), "1 juego, 2 diademas");
            Assert.IsTrue(Valida(Pedido(Linea(DESMAQUILLANTE, 3, 13.69M), Linea(LIMPIADOR, 3, 11.00M), Linea(DIADEMA, 6, 4.20M, 1)), DIADEMA), "3 juegos, 6 diademas");
            Assert.IsFalse(Valida(Pedido(Linea(DESMAQUILLANTE, 1, 13.69M), Linea(LIMPIADOR, 1, 11.00M), Linea(DIADEMA, 3, 4.20M, 1)), DIADEMA), "1 juego, 3 diademas");
        }
    }
}
