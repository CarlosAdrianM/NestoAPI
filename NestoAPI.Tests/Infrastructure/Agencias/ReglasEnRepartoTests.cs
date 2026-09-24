using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>NestoAPI#516: qué envíos tramitados están ya en reparto.</summary>
    [TestClass]
    public class ReglasEnRepartoTests
    {
        [DataTestMethod]
        [DataRow("EN REPARTO", DisplayName = "GLS y CTT")]
        [DataRow("REPARTO", DisplayName = "Innovatrans")]
        [DataRow("  en reparto ", DisplayName = "Con relleno y minúsculas")]
        public void Tramitado_ConTextoDeReparto_EstaEnReparto(string detalle)
        {
            Assert.IsTrue(ReglasEnReparto.EstaEnReparto(1, detalle));
        }

        [DataTestMethod]
        [DataRow("REPARTO FALLIDO")]
        [DataRow("EN DELEGACION DESTINO")]
        [DataRow("MANIFESTADO")]
        [DataRow(null)]
        public void Tramitado_ConOtroTexto_NoEstaEnReparto(string detalle)
        {
            Assert.IsFalse(ReglasEnReparto.EstaEnReparto(1, detalle));
        }

        [TestMethod]
        public void NoTramitado_AunqueDigaEnReparto_NoCuenta()
        {
            Assert.IsFalse(ReglasEnReparto.EstaEnReparto(2, "EN REPARTO"));
            Assert.IsFalse(ReglasEnReparto.EstaEnReparto(0, "EN REPARTO"));
        }

        [TestMethod]
        public void ElListadoLoExpone()
        {
            var dto = new EnvioAgenciaListadoDTO { Estado = 1, DetalleEstado = "EN REPARTO" };

            Assert.IsTrue(dto.EnReparto);
        }
    }
}
