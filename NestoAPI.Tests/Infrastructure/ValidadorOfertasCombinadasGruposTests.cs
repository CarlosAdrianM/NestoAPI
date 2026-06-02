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
    /// Grupos de alternativas en ofertas combinadas: una camiseta de regalo que puede ir en
    /// cualquier talla (cada talla una referencia). Las tallas comparten GrupoAlternativa y se
    /// exige EXACTAMENTE 1 (ni ninguna ni varias). Modela las ofertas reales 244/245.
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasGruposTests
    {
        private const string CREMA = "CREMA";
        private const string CAM_S = "CAM_S";
        private const string CAM_M = "CAM_M";
        private const string CAM_L = "CAM_L";

        private IServicioPrecios _servicio;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();
        }

        // Oferta: 1 crema obligatoria (precio mínimo 5) + camiseta de regalo en cualquier talla
        // (grupo 1, cantidad 1, precio 0). Sin importe mínimo: vale el camino de match directo.
        private OfertaCombinada CrearOfertaCamiseta(int cantidadCrema = 1)
        {
            return new OfertaCombinada
            {
                Id = 244,
                Empresa = "1",
                ImporteMinimo = 0,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = CREMA, Precio = 5, Cantidad = (short)cantidadCrema, GrupoAlternativa = null },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = CAM_S, Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = CAM_M, Precio = 0, Cantidad = 1, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 244, Empresa = "1", Producto = CAM_L, Precio = 0, Cantidad = 1, GrupoAlternativa = 1 }
                }
            };
        }

        private void FakeOferta(OfertaCombinada oferta, params string[] productos)
        {
            var lista = new List<OfertaCombinada> { oferta };
            foreach (string p in productos)
            {
                A.CallTo(() => _servicio.BuscarOfertasCombinadas(p)).Returns(lista);
            }
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                AplicarDescuento = true
            };
        }

        private static PedidoVentaDTO CrearPedido(params LineaPedidoVentaDTO[] lineas)
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            foreach (var l in lineas)
            {
                pedido.Lineas.Add(l);
            }
            return pedido;
        }

        [TestMethod]
        public void Camiseta_UnaDeCualquierTalla_EsValido()
        {
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 1, 5), Linea(CAM_M, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            // La talla concreta da igual: con la M puesta, autoriza la M a precio 0.
            Assert.IsTrue(validador.EsPedidoValido(pedido, CAM_M, _servicio).ValidacionSuperada,
                "Una camiseta talla M debe autorizar la oferta");
        }

        [TestMethod]
        public void Camiseta_OtraTalla_TambienEsValido()
        {
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 1, 5), Linea(CAM_S, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, CAM_S, _servicio).ValidacionSuperada,
                "Cualquier talla del grupo debe valer");
        }

        [TestMethod]
        public void Camiseta_SinNingunaCamiseta_NoEsValido()
        {
            // El cliente "olvida" la camiseta: la crema con descuento (precio 5) ya no se autoriza,
            // porque el grupo no está satisfecho. Es justo lo que se quiere evitar.
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 1, 5));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, CREMA, _servicio).ValidacionSuperada,
                "Sin la camiseta del grupo, la oferta no debe autorizarse");
        }

        [TestMethod]
        public void Camiseta_DiezCamisetas_NoEsValido()
        {
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 1, 5), Linea(CAM_M, 10, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, CAM_M, _servicio).ValidacionSuperada,
                "10 camisetas superan el 1 permitido por el grupo");
        }

        [TestMethod]
        public void Camiseta_DosTallasDistintasSumanDos_NoEsValido()
        {
            // S + M = 2 unidades del grupo, pero solo se permite 1 en total.
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 1, 5), Linea(CAM_S, 1, 0), Linea(CAM_M, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, CAM_M, _servicio).ValidacionSuperada,
                "El total del grupo (S+M=2) supera el requerido (1)");
        }

        [TestMethod]
        public void Camiseta_DosInstancias_DosCamisetas_EsValido()
        {
            // 2 cremas (2 instancias) -> se exigen 2 camisetas (cualquier mezcla de tallas).
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 2, 5), Linea(CAM_M, 1, 0), Linea(CAM_L, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, CAM_M, _servicio).ValidacionSuperada,
                "Con 2 instancias, 2 camisetas (M+L) deben valer");
        }

        [TestMethod]
        public void Camiseta_DosInstancias_UnaSolaCamiseta_NoEsValido()
        {
            OfertaCombinada oferta = CrearOfertaCamiseta();
            FakeOferta(oferta, CREMA, CAM_S, CAM_M, CAM_L);
            PedidoVentaDTO pedido = CrearPedido(Linea(CREMA, 2, 5), Linea(CAM_M, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, CAM_M, _servicio).ValidacionSuperada,
                "2 instancias exigen 2 camisetas; con 1 no vale");
        }

        [TestMethod]
        public void OfertaSinGrupos_SigueFuncionandoComoAntes_Regresion()
        {
            // Oferta clásica sin grupos: crema + regalo, ambos obligatorios. No debe romperse.
            var oferta = new OfertaCombinada
            {
                Id = 999,
                Empresa = "1",
                ImporteMinimo = 0,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 999, Empresa = "1", Producto = CREMA, Precio = 5, Cantidad = 1, GrupoAlternativa = null },
                    new OfertaCombinadaDetalle { OfertaId = 999, Empresa = "1", Producto = "REGALO", Precio = 0, Cantidad = 1, GrupoAlternativa = null }
                }
            };
            FakeOferta(oferta, CREMA, "REGALO");

            var validador = new ValidadorOfertasCombinadas();

            PedidoVentaDTO completo = CrearPedido(Linea(CREMA, 1, 5), Linea("REGALO", 1, 0));
            Assert.IsTrue(validador.EsPedidoValido(completo, "REGALO", _servicio).ValidacionSuperada,
                "La oferta sin grupos con todos los productos debe seguir siendo válida");

            PedidoVentaDTO incompleto = CrearPedido(Linea("REGALO", 1, 0));
            Assert.IsFalse(validador.EsPedidoValido(incompleto, "REGALO", _servicio).ValidacionSuperada,
                "Sin la crema obligatoria no debe autorizarse (comportamiento previo)");
        }
    }
}
