using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Ubicaciones;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Ubicaciones
{
    /// <summary>
    /// NestoAPI#594: el reparto de prdUbicarReposicion (tipo 1) sin base de datos. Un hueco si alguno tiene bastante
    /// (FIFO: el más antiguo; LIFO: el más reciente); si no, de varios en ese orden; si no hay nada, sin reserva.
    /// </summary>
    [TestClass]
    public class PlanificadorReservaHuecosTests
    {
        private static readonly DateTime LUNES = new DateTime(2026, 10, 5, 8, 0, 0);

        private static FilaUbicacionLibre Libre(int orden, int cantidad, int diasDespues, string pasillo = "002", int estado = 0)
        {
            return new FilaUbicacionLibre
            {
                NumeroOrden = orden, Empresa = "1", Almacen = "ALG", Producto = "12291", Cantidad = cantidad, Estado = estado,
                Pasillo = pasillo, Fila = pasillo == null ? null : "002", Columna = pasillo == null ? null : "00" + orden % 10,
                FechaCreacion = LUNES.AddDays(diasDespues)
            };
        }

        [TestMethod]
        public void UnHuecoSuficiente_ConFifo_CogeElMasAntiguoAunqueHayaOtroMasPequeno()
        {
            var libres = new[] { Libre(3, 10, 2), Libre(1, 2, 0), Libre(2, 6, 1) };

            List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(libres, 4, fifo: true);

            Assert.AreEqual(1, tomas.Count);
            Assert.AreEqual(2, tomas[0].Libre.NumeroOrden, "El 1 no tiene bastante; de los que tienen, el más antiguo");
            Assert.AreEqual(4, tomas[0].Cantidad);
            Assert.IsFalse(tomas[0].Entera, "Sobra: se resta y la reserva va aparte");
        }

        [TestMethod]
        public void UnHuecoSuficiente_ConLifo_CogeElMasReciente()
        {
            var libres = new[] { Libre(3, 10, 2), Libre(1, 5, 0), Libre(2, 6, 1) };

            List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(libres, 4, fifo: false);

            Assert.AreEqual(3, tomas.Single().Libre.NumeroOrden);
        }

        [TestMethod]
        public void MismaFecha_DesempataPorNumeroDeOrden()
        {
            var libres = new[] { Libre(9, 5, 0), Libre(4, 5, 0) };

            Assert.AreEqual(4, PlanificadorReservaHuecos.Repartir(libres, 5, fifo: true).Single().Libre.NumeroOrden);
            Assert.AreEqual(9, PlanificadorReservaHuecos.Repartir(libres, 5, fifo: false).Single().Libre.NumeroOrden);
        }

        [TestMethod]
        public void JustoLaCantidad_SeCogeLaFilaEntera()
        {
            TomaDeHueco toma = PlanificadorReservaHuecos.Repartir(new[] { Libre(1, 4, 0) }, 4, fifo: true).Single();

            Assert.IsTrue(toma.Entera);
        }

        [TestMethod]
        public void NingunoTieneBastante_CogeDeVariosEnOrdenFifoHastaCubrir()
        {
            var libres = new[] { Libre(3, 3, 2), Libre(1, 2, 0), Libre(2, 3, 1) };

            List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(libres, 7, fifo: true);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, tomas.Select(t => t.Libre.NumeroOrden).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 3, 2 }, tomas.Select(t => t.Cantidad).ToArray());
            CollectionAssert.AreEqual(new[] { true, true, false }, tomas.Select(t => t.Entera).ToArray());
        }

        [TestMethod]
        public void NingunoTieneBastante_ConLifo_EmpiezaPorElMasReciente()
        {
            var libres = new[] { Libre(3, 3, 2), Libre(1, 2, 0), Libre(2, 3, 1) };

            CollectionAssert.AreEqual(new[] { 3, 2 }, PlanificadorReservaHuecos.Repartir(libres, 5, fifo: false).Select(t => t.Libre.NumeroOrden).ToArray());
        }

        [TestMethod]
        public void NoHayBastanteEnTotal_ReservaLoQueHayYElRestoQuedaSinHueco()
        {
            List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(new[] { Libre(1, 2, 0), Libre(2, 1, 1) }, 10, fifo: true);

            Assert.AreEqual(3, tomas.Sum(t => t.Cantidad));
        }

        [TestMethod]
        public void SinStockLibre_NoReservaNada()
        {
            Assert.AreEqual(0, PlanificadorReservaHuecos.Repartir(new FilaUbicacionLibre[0], 3, fifo: true).Count);
            Assert.AreEqual(0, PlanificadorReservaHuecos.Repartir(null, 3, fifo: true).Count);
            Assert.AreEqual(0, PlanificadorReservaHuecos.Repartir(new[] { Libre(1, 0, 0), Libre(2, -1, 0) }, 3, fifo: true).Count,
                "Las filas a 0 o en negativo no cuentan");
        }

        [TestMethod]
        public void PendienteDeUbicar_Estado2SinHueco_TambienSeReserva()
        {
            FilaUbicacionLibre pendiente = Libre(1, 5, 0, pasillo: null, estado: 2);

            TomaDeHueco toma = PlanificadorReservaHuecos.Repartir(new[] { pendiente }, 3, fifo: true).Single();

            Assert.AreEqual(3, toma.Cantidad);
            Assert.IsFalse(toma.Libre.Hueco.TieneHueco, "Sale «sin hueco» en el recorrido");
            Assert.IsNull(toma.Libre.Hueco.ToString());
        }

        [TestMethod]
        public void OtrosEstados_NoSeReservan()
        {
            Assert.AreEqual(0, PlanificadorReservaHuecos.Repartir(new[] { Libre(1, 5, 0, estado: 3), Libre(2, 5, 0, estado: 4) }, 3, fifo: true).Count);
        }

        [TestMethod]
        public void Descontar_DejaParaLaSiguienteLineaSoloLoQueSobra()
        {
            var libres = new List<FilaUbicacionLibre> { Libre(1, 2, 0), Libre(2, 5, 1) };
            List<TomaDeHueco> tomas = PlanificadorReservaHuecos.Repartir(libres, 4, fifo: true);

            List<FilaUbicacionLibre> quedan = PlanificadorReservaHuecos.Descontar(libres, tomas);

            Assert.AreEqual(2, quedan.Count);
            Assert.AreEqual(2, quedan.Single(l => l.NumeroOrden == 1).Cantidad);
            Assert.AreEqual(1, quedan.Single(l => l.NumeroOrden == 2).Cantidad);
            Assert.AreEqual(5, libres[1].Cantidad, "No cambia la lista original");
        }
    }
}
