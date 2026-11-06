using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#831 (Carlos 06/11/26): el día de reparto del envío del reparto propio va en FechaEntrega. Si el picking es de hoy
    /// o de antes, hoy; si es futuro (un aplazado), ese día con el día de la ruta y los festivos. Fecha sigue siendo el de la etiqueta.
    /// </summary>
    [TestClass]
    public class FechaRepartoPropioTests
    {
        private static readonly DateTime JUEVES = new DateTime(2026, 11, 5);
        private static readonly DateTime VIERNES = new DateTime(2026, 11, 6);
        private static readonly DateTime LUNES = new DateTime(2026, 11, 9);
        private static readonly DateTime MARTES = new DateTime(2026, 11, 10);

        private static bool FinDeSemana(DateTime d) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

        [TestMethod]
        public void Calcular_SinPickingOPickingDeHoyODeAntes_Hoy()
        {
            Assert.AreEqual(VIERNES, FechaRepartoPropio.Calcular(null, VIERNES, (DayOfWeek?)null, FinDeSemana));
            Assert.AreEqual(VIERNES, FechaRepartoPropio.Calcular(VIERNES, VIERNES.AddHours(17), (DayOfWeek?)null, FinDeSemana));
            Assert.AreEqual(VIERNES, FechaRepartoPropio.Calcular(JUEVES, VIERNES, DayOfWeek.Tuesday, FinDeSemana),
                "lo atrasado no se manda a la semana siguiente: sale hoy");
        }

        [TestMethod]
        public void Calcular_PickingFuturo_EseDia_SaltandoFestivos()
        {
            Assert.AreEqual(LUNES, FechaRepartoPropio.Calcular(LUNES, VIERNES, (DayOfWeek?)null, FinDeSemana));
            Assert.AreEqual(LUNES, FechaRepartoPropio.Calcular(VIERNES.AddDays(1), VIERNES, (DayOfWeek?)null, FinDeSemana), "un sábado pasa al lunes");
            Assert.AreEqual(MARTES, FechaRepartoPropio.Calcular(LUNES, VIERNES, (DayOfWeek?)null, d => FinDeSemana(d) || d == LUNES), "festivo el lunes");
        }

        [TestMethod]
        public void Calcular_PickingFuturo_ConDiaDeRuta_ElPrimerDiaDeSuRuta()
        {
            Assert.AreEqual(MARTES, FechaRepartoPropio.Calcular(LUNES, VIERNES, DayOfWeek.Tuesday, FinDeSemana));
            Assert.AreEqual(MARTES, FechaRepartoPropio.Calcular(MARTES, VIERNES, DayOfWeek.Tuesday, FinDeSemana));
        }

        [TestMethod]
        public void DeEnvio_FechaEntregaOSinEllaFecha_YEsDeOtroDiaSoloEnLa14()
        {
            var rp = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = VIERNES.AddHours(10), FechaEntrega = LUNES };
            Assert.AreEqual(LUNES, FechaRepartoPropio.DeEnvio(rp));
            Assert.IsTrue(FechaRepartoPropio.EsDeOtroDia(rp, VIERNES));
            Assert.IsFalse(FechaRepartoPropio.EsDeOtroDia(rp, LUNES));
            Assert.AreEqual("Este bulto es del reparto del 09/11: no se carga hoy.", FechaRepartoPropio.MensajeNoSeCargaHoy(rp));

            rp.FechaEntrega = null;
            Assert.AreEqual(VIERNES, FechaRepartoPropio.DeEnvio(rp));

            var ctt = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_CTT, Fecha = VIERNES, FechaEntrega = LUNES };
            Assert.IsFalse(FechaRepartoPropio.EsDeOtroDia(ctt, VIERNES), "solo el reparto propio");
        }

        [TestMethod]
        public void FechaPagoReembolso_DelRepartoPropio_EsElDiaDeReparto()
        {
            var envio = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = VIERNES, FechaEntrega = LUNES, Reembolso = 85.40m };

            ReembolsoRepartoPropio.AplicarFechaPago(envio);

            Assert.AreEqual(LUNES, envio.FechaPagoReembolso);
        }

        [TestMethod]
        public void Sombra_RepartoPropio_NoComparaLaFechaDeEntrega_LasDemasSi()
        {
            var propuesta = new PropuestaEnvioDTO { Origen = PropuestaEnvioDTO.ORIGEN_NUEVO, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = VIERNES, FechaEntrega = LUNES };
            var envio = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = VIERNES, FechaEntrega = VIERNES.AddDays(1) };

            Assert.IsFalse(ComparadorPropuestaEnvio.Diferencias(envio, propuesta).Exists(d => d.StartsWith("fecha de entrega")));

            envio.Agencia = propuesta.Agencia = Constantes.Agencias.AGENCIA_CTT;
            Assert.IsTrue(ComparadorPropuestaEnvio.Diferencias(envio, propuesta).Exists(d => d.StartsWith("fecha de entrega")));
        }

        [TestMethod]
        public void Tramitar_RepartoPropio_NoPisaElDiaDeReparto_LasAgenciasMananaComoSiempre()
        {
            var rp = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = JUEVES, FechaEntrega = LUNES };
            TramitacionEnviosService.FechasAlTramitar(rp, VIERNES);
            Assert.AreEqual(VIERNES, rp.Fecha, "Fecha = el día de la etiqueta");
            Assert.AreEqual(LUNES, rp.FechaEntrega);

            var rpPasado = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, Fecha = JUEVES, FechaEntrega = JUEVES };
            TramitacionEnviosService.FechasAlTramitar(rpPasado, VIERNES);
            Assert.AreEqual(VIERNES, rpPasado.FechaEntrega, "de un día pasado (o sin ella), hoy");

            var ctt = new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_CTT, Fecha = JUEVES, FechaEntrega = MARTES };
            TramitacionEnviosService.FechasAlTramitar(ctt, VIERNES);
            Assert.AreEqual(VIERNES.AddDays(1), ctt.FechaEntrega);
        }
    }
}
