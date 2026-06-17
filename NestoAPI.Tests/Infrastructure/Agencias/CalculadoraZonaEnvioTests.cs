using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// CalculadoraZonaEnvio: porte fiel del CP -> zona de Nesto (AgenciasViewModel.CalcularZonaEnvio).
    /// </summary>
    [TestClass]
    public class CalculadoraZonaEnvioTests
    {
        [DataTestMethod]
        [DataRow("28001", ZonasEnvioAgencia.Provincial, DisplayName = "Madrid (28) = Provincial")]
        [DataRow("28999", ZonasEnvioAgencia.Provincial)]
        [DataRow("08001", ZonasEnvioAgencia.Peninsular, DisplayName = "Barcelona = Peninsular")]
        [DataRow("46001", ZonasEnvioAgencia.Peninsular)]
        [DataRow("07001", ZonasEnvioAgencia.BalearesMayores, DisplayName = "Palma (en lista mayores)")]
        [DataRow("07500", ZonasEnvioAgencia.BalearesMenores, DisplayName = "Baleares no-mayor")]
        [DataRow("35001", ZonasEnvioAgencia.CanariasMayores, DisplayName = "Las Palmas (en lista mayores)")]
        [DataRow("38001", ZonasEnvioAgencia.CanariasMayores, DisplayName = "Tenerife (en lista mayores)")]
        [DataRow("35500", ZonasEnvioAgencia.CanariasMenores, DisplayName = "Canarias no-mayor")]
        [DataRow("38500", ZonasEnvioAgencia.CanariasMenores)]
        public void CalcularZona_PorCodigoPostal(string cp, ZonasEnvioAgencia esperada)
        {
            Assert.AreEqual(esperada, CalculadoraZonaEnvio.CalcularZona(cp));
        }

        [DataTestMethod]
        [DataRow("1000-001", DisplayName = "Portugal con guion")]
        [DataRow("4000 100", DisplayName = "Portugal con espacio")]
        public void CalcularZona_Portugal(string cp)
        {
            Assert.AreEqual(ZonasEnvioAgencia.Portugal, CalculadoraZonaEnvio.CalcularZona(cp));
        }

        [DataTestMethod]
        [DataRow("EXTER", DisplayName = "Centinela EXTER")]
        [DataRow("1234", DisplayName = "Longitud != 5")]
        [DataRow("123456", DisplayName = "Longitud != 5 (largo)")]
        [DataRow(null, DisplayName = "Null")]
        public void CalcularZona_Extranjero(string cp)
        {
            Assert.AreEqual(ZonasEnvioAgencia.Extranjero, CalculadoraZonaEnvio.CalcularZona(cp));
        }

        [TestMethod]
        public void CalcularZona_RecortaEspacios()
        {
            Assert.AreEqual(ZonasEnvioAgencia.Provincial, CalculadoraZonaEnvio.CalcularZona("  28001  "));
        }
    }
}
