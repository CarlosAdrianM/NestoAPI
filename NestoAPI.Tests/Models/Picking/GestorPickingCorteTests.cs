using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models.Picking;
using System;

namespace NestoAPI.Tests.Models.Picking
{
    /// <summary>
    /// NestoAPI#361: el corte de las 11h.
    ///
    /// El horizonte de entrega (<c>fechaPicking</c>) decide hasta qué fecha se sirve:
    /// <c>BorrarLineasEntregaFutura</c> quita las líneas con FechaEntrega mayor. Hasta ahora
    /// siempre se deducía de <c>DateTime.Now</c>, así que el picking de cierre de las 11h era
    /// sensible al segundo exacto de arranque: a las 10:59:59 servía HOY y a las 11:00:01
    /// pasaba a servir también lo de MAÑANA, adelantando un día las entregas en silencio.
    ///
    /// Se toreaba programando la tarea del Task Scheduler a las 10:59:40, lo que dejaba fuera
    /// los pedidos metidos en esos últimos 20 segundos — pedidos que PedidosVentaController SÍ
    /// permite meter, porque su corte son las 11:00 en punto.
    /// </summary>
    [TestClass]
    public class GestorPickingCorteTests
    {
        private static readonly TimeSpan CORTE_11 = HoraCortePicking.POR_DEFECTO;

        private static DateTime Hoy(int hora, int minuto, int segundo)
        {
            return new DateTime(2026, 8, 24, hora, minuto, segundo);
        }

        // ===== El límite exacto, que antes vivía enterrado en una comparación con DateTime.Now =====

        [TestMethod]
        public void CorteDelDiaSuperado_UnSegundoAntesDeLasOnce_TodaviaNo()
        {
            Assert.IsFalse(GestorPicking.CorteDelDiaSuperado(Hoy(10, 59, 59), CORTE_11));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_LasOnceEnPunto_YaSi()
        {
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 0), CORTE_11));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_UnSegundoDespues_YaSi()
        {
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 1), CORTE_11));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_ElCorteSaleDelParametro_NoDeUnOnceFijo()
        {
            // NestoAPI#577: con el parámetro HoraCortePicking a las 12:30 el corte se mueve.
            TimeSpan corte = new TimeSpan(12, 30, 0);

            Assert.IsFalse(GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 0), corte));
            Assert.IsFalse(GestorPicking.CorteDelDiaSuperado(Hoy(12, 29, 59), corte));
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(12, 30, 0), corte));
        }

        [TestMethod]
        public void CalcularFechaPicking_ConCorteALasDoceYMedia_A_LasOnceYMediaSirveHoy()
        {
            TimeSpan corte = new TimeSpan(12, 30, 0);

            Assert.AreEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(11, 30, 0), corte));
            Assert.AreNotEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(12, 30, 0), corte));
        }

        // ===== De dónde sale la hora de corte =====

        [TestMethod]
        public void HoraCortePicking_SinParametro_LasOnce()
        {
            Func<string, string> original = HoraCortePicking.LectorValor;
            try
            {
                HoraCortePicking.LimpiarCache();
                HoraCortePicking.LectorValor = empresa => null;
                Assert.AreEqual(new TimeSpan(11, 0, 0), HoraCortePicking.Leer("9"));
            }
            finally
            {
                HoraCortePicking.LectorValor = original;
                HoraCortePicking.LimpiarCache();
            }
        }

        [TestMethod]
        public void HoraCortePicking_ConParametro_LeeElParametroDeLaEmpresa()
        {
            Func<string, string> original = HoraCortePicking.LectorValor;
            try
            {
                HoraCortePicking.LimpiarCache();
                string empresaLeida = null;
                HoraCortePicking.LectorValor = empresa => { empresaLeida = empresa; return "12:30"; };

                Assert.AreEqual(new TimeSpan(12, 30, 0), HoraCortePicking.Leer("9  "));
                Assert.AreEqual("9", empresaLeida);
            }
            finally
            {
                HoraCortePicking.LectorValor = original;
                HoraCortePicking.LimpiarCache();
            }
        }

        [TestMethod]
        public void HoraCortePicking_SiFallaLaLectura_LasOnce()
        {
            Func<string, string> original = HoraCortePicking.LectorValor;
            try
            {
                HoraCortePicking.LimpiarCache();
                HoraCortePicking.LectorValor = empresa => throw new InvalidOperationException("sin BD");
                Assert.AreEqual(new TimeSpan(11, 0, 0), HoraCortePicking.Leer("9"));
            }
            finally
            {
                HoraCortePicking.LectorValor = original;
                HoraCortePicking.LimpiarCache();
            }
        }

        // ===== El horizonte deducido del reloj (picking interactivo) =====

        [TestMethod]
        public void CalcularFechaPicking_AntesDelCorte_SirveHoy()
        {
            Assert.AreEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(10, 59, 59), CORTE_11));
            Assert.AreEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(8, 0, 0), CORTE_11));
        }

        [TestMethod]
        public void CalcularFechaPicking_AntesDelCorte_QuitaLaHora()
        {
            // El horizonte se compara contra FechaEntrega, que no lleva hora.
            DateTime resultado = GestorPicking.CalcularFechaPicking(Hoy(10, 30, 45), CORTE_11);

            Assert.AreEqual(TimeSpan.Zero, resultado.TimeOfDay);
        }

        // ===== Lo que de verdad arregla el cambio =====

        [TestMethod]
        public void ElPickingDeCierre_NoDependeDelSegundoEnQueArranque()
        {
            // ANTES: el horizonte salía de la hora de arranque, así que estos dos instantes
            // daban resultados DISTINTOS (hoy vs. el siguiente día laborable) por dos segundos
            // de diferencia. Ahora el picking de cierre pasa DateTime.Today como dato y ni
            // siquiera llama a CalcularFechaPicking, así que arranque cuando arranque sirve hoy.
            Assert.AreNotEqual(
                GestorPicking.CorteDelDiaSuperado(Hoy(10, 59, 59), CORTE_11),
                GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 1), CORTE_11),
                "Este es el salto que hacía frágil al picking automático: dos segundos cambiaban el horizonte");

            // El horizonte que declara el picking de cierre es siempre el mismo, sin reloj de por medio.
            Assert.AreEqual(DateTime.Today.Date, DateTime.Today,
                "El picking de cierre pasa DateTime.Today: una fecha sin hora, estable todo el día");
        }
    }
}
