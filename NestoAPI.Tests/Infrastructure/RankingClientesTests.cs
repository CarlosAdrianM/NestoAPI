using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Buscador;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#455: el orden del buscador de clientes sale de lo que ha comprado cada uno en el
    /// último año. Se guarda la posición y no el importe, porque con importes un solo cliente
    /// puede desbaratar el ranking entero.
    /// </summary>
    [TestClass]
    public class RankingClientesTests
    {
        private static VentaCliente Venta(string cliente, decimal importe)
        {
            return new VentaCliente { Cliente = cliente, Venta = importe };
        }

        [TestMethod]
        public void AsignarPosiciones_ElQueMasCompraEsElPrimero()
        {
            var ventas = new List<VentaCliente>
            {
                Venta("9471", 29148.93M),
                Venta("41648", 800000M),
                Venta("32624", 140940.89M)
            };

            Dictionary<string, int> posiciones = RankingClientes.AsignarPosiciones(ventas);

            Assert.AreEqual(1, posiciones["41648"]);
            Assert.AreEqual(2, posiciones["32624"]);
            Assert.AreEqual(3, posiciones["9471"]);
        }

        [TestMethod]
        public void AsignarPosiciones_UnClienteEnorme_NoAplastaAlResto()
        {
            // El caso real: 800.000 EUR de una sola factura frente a 29.000 del cuarto. Con
            // importes, ese cliente se comería el ranking; con posiciones es solo el primero.
            var ventas = new List<VentaCliente>
            {
                Venta("41648", 800000M),
                Venta("32624", 140940.89M),
                Venta("9471", 29148.93M)
            };

            Dictionary<string, int> posiciones = RankingClientes.AsignarPosiciones(ventas);

            Assert.AreEqual(1, posiciones["41648"]);
            Assert.AreEqual(2, posiciones["32624"], "la distancia en euros no debe abrir hueco de puestos");
            Assert.AreEqual(3, posiciones["9471"]);
        }

        [TestMethod]
        public void AsignarPosiciones_MismaVenta_OrdenEstableEntreNoches()
        {
            // Sin desempate, dos clientes con la misma venta podrían intercambiarse en cada
            // reindexado y el usuario vería la lista bailar sin motivo
            var ventas = new List<VentaCliente> { Venta("30722", 500M), Venta("14181", 500M) };
            var mismasAlReves = new List<VentaCliente> { Venta("14181", 500M), Venta("30722", 500M) };

            Dictionary<string, int> unas = RankingClientes.AsignarPosiciones(ventas);
            Dictionary<string, int> otras = RankingClientes.AsignarPosiciones(mismasAlReves);

            Assert.AreEqual(unas["14181"], otras["14181"]);
            Assert.AreEqual(unas["30722"], otras["30722"]);
        }

        [TestMethod]
        public void AsignarPosiciones_RecortaElNumeroDeCliente()
        {
            // Los char de la base de datos vienen con relleno; si no se recorta, luego no casa
            // con el número que manda el buscador
            Dictionary<string, int> posiciones = RankingClientes.AsignarPosiciones(
                new List<VentaCliente> { Venta("15191     ", 100M) });

            Assert.IsTrue(posiciones.ContainsKey("15191"));
        }

        [TestMethod]
        public void AsignarPosiciones_IgnoraClientesVaciosYNoFalla()
        {
            Dictionary<string, int> posiciones = RankingClientes.AsignarPosiciones(
                new List<VentaCliente> { Venta(null, 10M), Venta("   ", 20M), Venta("15191", 30M) });

            Assert.AreEqual(1, posiciones.Count);
            Assert.AreEqual(1, posiciones["15191"]);
        }

        [TestMethod]
        public void AsignarPosiciones_SinVentas_DevuelveVacioSinFallar()
        {
            Assert.AreEqual(0, RankingClientes.AsignarPosiciones(null).Count);
            Assert.AreEqual(0, RankingClientes.AsignarPosiciones(new List<VentaCliente>()).Count);
        }

        [TestMethod]
        public void AsignarPosiciones_ClienteRepetido_SeQuedaConElMejorPuesto()
        {
            Dictionary<string, int> posiciones = RankingClientes.AsignarPosiciones(
                new List<VentaCliente> { Venta("15191", 100M), Venta("15191  ", 50M) });

            Assert.AreEqual(1, posiciones.Count);
            Assert.AreEqual(1, posiciones["15191"]);
        }
    }
}
