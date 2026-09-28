using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.AlbaranesVenta
{
    /// <summary>
    /// 28/09/26 (926037, 926373, 927014, 927146): «No puede cambiar el campo recoger si la linea tiene picking». El
    /// SP del albarán devuelve a pendiente los regalos de base 0 que son lo único «en carpeta» (regla del 07/04/22),
    /// pero si ya tienen picking el trigger lo impide. La API lo hace antes, quitando también el picking.
    /// </summary>
    [TestClass]
    public class RegalosEnCarpetaConPickingTests
    {
        private static LinPedidoVta Linea(int orden, short cantidad, int recoger, decimal baseImponible, int? picking, short estado = 1)
            => new LinPedidoVta { Nº_Orden = orden, Cantidad = cantidad, Recoger = recoger, Base_Imponible = baseImponible, Picking = picking, Estado = estado };

        [TestMethod]
        public void Pedido926037_ElExpositorEnCarpetaConPicking_SeSaca()
        {
            var lineas = new List<LinPedidoVta>
            {
                Linea(327931400, 1, 0, 27.45m, 99681),   // VAPORE, se entrega
                Linea(327935700, 1, 0, 0m, 99681),       // regalo que se entrega
                Linea(327935500, 1, 1, 0m, 99681),       // EXPOSITOR LEVEL VACIO, en carpeta
                Linea(327931500, 1, 0, 10m, 99513, 2)    // ya albaraneada
            };

            CollectionAssert.AreEqual(new[] { 327935500 }, ServicioAlbaranesVenta.RegalosEnCarpetaConPicking(lineas));
        }

        [TestMethod]
        public void SiTambienHayProductosDePagoEnCarpeta_NoSeTocaNada()
        {
            // El SP solo aplica la regla si lo único en carpeta son líneas de base 0 (nota de entrega normal)
            var lineas = new List<LinPedidoVta>
            {
                Linea(1, 1, 1, 0m, 5),
                Linea(2, 2, 2, 30m, 5)
            };

            Assert.AreEqual(0, ServicioAlbaranesVenta.RegalosEnCarpetaConPicking(lineas).Count);
        }

        [TestMethod]
        public void SinPicking_LoHaceElSPComoSiempre()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 1, 0m, null), Linea(2, 1, 0, 30m, 5) };

            Assert.AreEqual(0, ServicioAlbaranesVenta.RegalosEnCarpetaConPicking(lineas).Count);
        }

        [TestMethod]
        public void RecogerParcial_NoSeToca()
        {
            // Si parte de la línea se entrega en este picking, quitarle el picking la sacaría del albarán
            var lineas = new List<LinPedidoVta> { Linea(1, 3, 1, 0m, 5), Linea(2, 1, 0, 30m, 5) };

            Assert.AreEqual(0, ServicioAlbaranesVenta.RegalosEnCarpetaConPicking(lineas).Count);
        }

        [TestMethod]
        public void PedidoSoloDeRegalos_NoAplica()
        {
            // Tercera condición del SP: el pedido tiene que tener algo con base
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 1, 0m, 5), Linea(2, 1, 0, 0m, 5) };

            Assert.AreEqual(0, ServicioAlbaranesVenta.RegalosEnCarpetaConPicking(lineas).Count);
        }
    }
}
