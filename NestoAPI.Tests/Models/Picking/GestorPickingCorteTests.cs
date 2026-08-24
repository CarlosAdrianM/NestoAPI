using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
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
        private static DateTime Hoy(int hora, int minuto, int segundo)
        {
            return new DateTime(2026, 8, 24, hora, minuto, segundo);
        }

        // ===== El límite exacto, que antes vivía enterrado en una comparación con DateTime.Now =====

        [TestMethod]
        public void CorteDelDiaSuperado_UnSegundoAntesDeLasOnce_TodaviaNo()
        {
            Assert.IsFalse(GestorPicking.CorteDelDiaSuperado(Hoy(10, 59, 59)));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_LasOnceEnPunto_YaSi()
        {
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 0)));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_UnSegundoDespues_YaSi()
        {
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 1)));
        }

        [TestMethod]
        public void CorteDelDiaSuperado_ElCorteSonLasOnce_NoUnNumeroSuelto()
        {
            // Si algún día se mueve la hora de corte, este test lo sigue cubriendo.
            int corte = Constantes.Picking.HORA_MAXIMA_AMPLIAR_PEDIDOS;

            Assert.IsFalse(GestorPicking.CorteDelDiaSuperado(Hoy(corte - 1, 59, 59)));
            Assert.IsTrue(GestorPicking.CorteDelDiaSuperado(Hoy(corte, 0, 0)));
        }

        // ===== El horizonte deducido del reloj (picking interactivo) =====

        [TestMethod]
        public void CalcularFechaPicking_AntesDelCorte_SirveHoy()
        {
            Assert.AreEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(10, 59, 59)));
            Assert.AreEqual(new DateTime(2026, 8, 24), GestorPicking.CalcularFechaPicking(Hoy(8, 0, 0)));
        }

        [TestMethod]
        public void CalcularFechaPicking_AntesDelCorte_QuitaLaHora()
        {
            // El horizonte se compara contra FechaEntrega, que no lleva hora.
            DateTime resultado = GestorPicking.CalcularFechaPicking(Hoy(10, 30, 45));

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
                GestorPicking.CorteDelDiaSuperado(Hoy(10, 59, 59)),
                GestorPicking.CorteDelDiaSuperado(Hoy(11, 0, 1)),
                "Este es el salto que hacía frágil al picking automático: dos segundos cambiaban el horizonte");

            // El horizonte que declara el picking de cierre es siempre el mismo, sin reloj de por medio.
            Assert.AreEqual(DateTime.Today.Date, DateTime.Today,
                "El picking de cierre pasa DateTime.Today: una fecha sin hora, estable todo el día");
        }
    }
}
