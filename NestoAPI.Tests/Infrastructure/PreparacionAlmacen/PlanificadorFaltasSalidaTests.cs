using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: lo que el mozo da por «No está» al sacar mercancía se quita de lo esperado. Primero de la pieza del
    /// hueco donde lo dio por falta (la parada); después, en un picking, del pedido más nuevo (el reparto del stock va por
    /// antigüedad, así que el que se queda sin nada es el último que llegó).
    /// </summary>
    [TestClass]
    public class PlanificadorFaltasSalidaTests
    {
        private static PiezaSalida Pieza(int linea, int pedido, string producto, string hueco, int cantidad, int? ubicacion = null, bool aMano = false)
            => new PiezaSalida { Linea = linea, Pedido = pedido, Producto = producto, Hueco = hueco, Cantidad = cantidad, Ubicacion = ubicacion, QuitarAMano = aMano };

        private static FaltaSalida Falta(string producto, int cantidad, string hueco = null)
            => new FaltaSalida { Producto = producto, Cantidad = cantidad, Hueco = hueco };

        [TestMethod]
        public void NormalizarHueco_ComoLoEnsenaLaAppOComoLaEtiqueta_NueveCifras()
        {
            Assert.AreEqual("004002012", PlanificadorFaltasSalida.NormalizarHueco("004/002/012"));
            Assert.AreEqual("004002012", PlanificadorFaltasSalida.NormalizarHueco(" 004002012 "));
            Assert.IsNull(PlanificadorFaltasSalida.NormalizarHueco("Recepción 123"));
            Assert.IsNull(PlanificadorFaltasSalida.NormalizarHueco(null));
        }

        [TestMethod]
        public void AgruparFaltas_UnDeshacerConElMismoHuecoLaAnula()
        {
            List<FaltaSalida> faltas = PlanificadorFaltasSalida.AgruparFaltas(new[]
            {
                Falta("A", 2, "004/002/012"),
                Falta("A", -2, "004/002/012"),
                Falta("B", 1, "001/001/001")
            });

            Assert.AreEqual(1, faltas.Count);
            Assert.AreEqual("B", faltas[0].Producto);
            Assert.AreEqual("001001001", faltas[0].Hueco);
        }

        [TestMethod]
        public void AgruparFaltas_SinHuecoConocido_QuedaSinHuecoYNoPasaDelTotal()
        {
            List<FaltaSalida> faltas = PlanificadorFaltasSalida.AgruparFaltas(new[]
            {
                Falta("A", 1, null),
                Falta("A", 2, "002/001/001"),
                Falta("A", -1, null)
            });

            Assert.AreEqual(2, faltas.Sum(f => f.Cantidad), "El total del producto manda: 1 + 2 - 1");
            Assert.AreEqual(2, faltas.Single(f => f.Hueco == "002001001").Cantidad);
        }

        [TestMethod]
        public void Repartir_ConHueco_SeQuitaDeLaPiezaDeEseHueco()
        {
            List<RecorteSalida> recortes = PlanificadorFaltasSalida.Repartir(
                new[] { Falta("A", 1, "002001001") },
                new[]
                {
                    Pieza(10, 900, "A", "001001001", 3, 1),
                    Pieza(11, 800, "A", "002001001", 2, 2)
                });

            Assert.AreEqual(1, recortes.Count);
            Assert.AreEqual(2, recortes[0].Pieza.Ubicacion, "La del hueco de la falta, aunque su pedido sea más antiguo");
            Assert.AreEqual(1, recortes[0].Cantidad);
        }

        [TestMethod]
        public void Repartir_SinHueco_SeQuitaPrimeroDelPedidoMasNuevo()
        {
            List<RecorteSalida> recortes = PlanificadorFaltasSalida.Repartir(
                new[] { Falta("A", 2) },
                new[]
                {
                    Pieza(10, 800, "A", "001001001", 3, 1),
                    Pieza(20, 900, "A", "001001001", 1, 2)
                });

            Assert.AreEqual(2, recortes.Count);
            RecorteSalida nuevo = recortes.Single(r => r.Pieza.Pedido == 900);
            RecorteSalida viejo = recortes.Single(r => r.Pieza.Pedido == 800);
            Assert.AreEqual(1, nuevo.Cantidad, "Al más nuevo, todo lo que tenía");
            Assert.AreEqual(1, viejo.Cantidad, "Y el resto al siguiente");
        }

        [TestMethod]
        public void Repartir_LasLineasQueHayQueQuitarAMano_SeTocanLasUltimas()
        {
            List<RecorteSalida> recortes = PlanificadorFaltasSalida.Repartir(
                new[] { Falta("A", 1) },
                new[]
                {
                    Pieza(10, 800, "A", null, 2),
                    Pieza(20, 900, "A", null, 2, aMano: true)
                });

            Assert.AreEqual(10, recortes.Single().Pieza.Linea);
        }

        [TestMethod]
        public void Repartir_MasFaltaDeLaQueSeEsperaba_Error()
        {
            Assert.ThrowsException<InvalidOperationException>(() => PlanificadorFaltasSalida.Repartir(
                new[] { Falta("A", 3) },
                new[] { Pieza(10, 800, "A", null, 2) }));
        }
    }
}
