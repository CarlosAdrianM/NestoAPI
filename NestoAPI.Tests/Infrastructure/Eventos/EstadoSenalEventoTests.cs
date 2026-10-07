using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Eventos;
using System;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>NestoAPI#591: estado de una señal según la fecha del evento y el pendiente del apunte del extracto.</summary>
    [TestClass]
    public class EstadoSenalEventoTests
    {
        private static readonly DateTime EVENTO = new DateTime(2026, 10, 13);

        [TestMethod]
        public void Calcular_AntesDelEvento_Pendiente()
        {
            Assert.AreEqual(EstadoSenalEvento.Pendiente, CalculadoraEstadoSenalEvento.Calcular(EVENTO, -50m, new DateTime(2026, 10, 12, 23, 59, 0)));
        }

        [TestMethod]
        public void Calcular_ElMismoDiaDelEvento_Liberada()
        {
            Assert.AreEqual(EstadoSenalEvento.Liberada, CalculadoraEstadoSenalEvento.Calcular(EVENTO.AddHours(18), -50m, new DateTime(2026, 10, 13, 8, 0, 0)));
        }

        [TestMethod]
        public void Calcular_CatorceDiasDespues_SigueLiberada()
        {
            Assert.AreEqual(EstadoSenalEvento.Liberada, CalculadoraEstadoSenalEvento.Calcular(EVENTO, -50m, EVENTO.AddDays(14)));
        }

        [TestMethod]
        public void Calcular_QuinceDiasDespuesSinConsumir_SinCompra()
        {
            Assert.AreEqual(15, CalculadoraEstadoSenalEvento.DIAS_SIN_COMPRA);
            Assert.AreEqual(EstadoSenalEvento.SinCompra, CalculadoraEstadoSenalEvento.Calcular(EVENTO, -50m, EVENTO.AddDays(15)));
            Assert.AreEqual(EVENTO.AddDays(15), CalculadoraEstadoSenalEvento.FechaSinCompra(EVENTO));
        }

        [TestMethod]
        public void Calcular_ConsumidaParcialmente_SigueLiberadaConElPendiente()
        {
            Assert.AreEqual(EstadoSenalEvento.Liberada, CalculadoraEstadoSenalEvento.Calcular(EVENTO, -12.5m, EVENTO.AddDays(1)));
        }

        [TestMethod]
        public void Calcular_ApunteSinPendiente_ConsumidaEnCualquierFecha()
        {
            Assert.AreEqual(EstadoSenalEvento.Consumida, CalculadoraEstadoSenalEvento.Calcular(EVENTO, 0m, EVENTO.AddDays(-5)));
            Assert.AreEqual(EstadoSenalEvento.Consumida, CalculadoraEstadoSenalEvento.Calcular(EVENTO, 0m, EVENTO.AddDays(1)));
            Assert.AreEqual(EstadoSenalEvento.Consumida, CalculadoraEstadoSenalEvento.Calcular(EVENTO, 0m, EVENTO.AddDays(40)));
        }

        [TestMethod]
        public void Interpretar_AceptaLasVariantesYVacioEsSinFiltro()
        {
            Assert.AreEqual(EstadoSenalEvento.SinCompra, CalculadoraEstadoSenalEvento.Interpretar("Sin compra"));
            Assert.AreEqual(EstadoSenalEvento.SinCompra, CalculadoraEstadoSenalEvento.Interpretar("sincompra"));
            Assert.AreEqual(EstadoSenalEvento.SinCompra, CalculadoraEstadoSenalEvento.Interpretar("SIN_COMPRA"));
            Assert.AreEqual(EstadoSenalEvento.Liberada, CalculadoraEstadoSenalEvento.Interpretar(" liberada "));
            Assert.IsNull(CalculadoraEstadoSenalEvento.Interpretar(null));
            Assert.IsNull(CalculadoraEstadoSenalEvento.Interpretar("Todas"));
            Assert.IsNull(CalculadoraEstadoSenalEvento.Interpretar("9"));
            Assert.AreEqual("Sin compra", CalculadoraEstadoSenalEvento.Texto(EstadoSenalEvento.SinCompra));
        }
    }
}
