using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Tarifa GLS EuroBusinessParcel (oferta GLS_EU.pdf 2026). Se zonifica por PAÍS (ISO2 → zona A–E),
    /// no por CP. Sobre el porte se aplica SIEMPRE el 1,50% Climate Protect (y el fuel si lo hay).
    /// </summary>
    [TestClass]
    public class TarifasGLSEuroTests
    {
        private readonly TarifaGLSEuroBusinessParcel _euro = new TarifaGLSEuroBusinessParcel();
        private const decimal Climate = 1.015m;

        // El CP es irrelevante en esta tarifa (zonifica por país); se pasa uno cualquiera.
        private decimal Coste(string pais, decimal peso, decimal reembolso = 0m, decimal fuel = 0m)
            => _euro.CalcularCoste("00000", pais, peso, reembolso, fuel);

        [TestMethod]
        public void Identidad_EsGlsServicio74()
        {
            Assert.AreEqual(1, _euro.AgenciaId);
            Assert.AreEqual((byte)74, _euro.ServicioId);
            Assert.AreEqual("EuroBusinessParcel", _euro.NombreServicio);
        }

        [TestMethod]
        public void PreciosClavePorZona_ConClimateProtect()
        {
            Assert.AreEqual(14.88m * Climate, Coste("DE", 1m));   // Zona A, 1 kg
            Assert.AreEqual(34.26m * Climate, Coste("FR", 40m));  // Zona A, tope 40 kg = precio 30 kg
            Assert.AreEqual(15.16m * Climate, Coste("IT", 1m));   // Zona B, 1 kg
            Assert.AreEqual(27.38m * Climate, Coste("GB", 5m));   // Zona B, 5 kg
            Assert.AreEqual(37.35m * Climate, Coste("PL", 10m));  // Zona C, 10 kg
            Assert.AreEqual(63.60m * Climate, Coste("IE", 20m));  // Zona D, 20 kg
            Assert.AreEqual(421.50m * Climate, Coste("CY", 30m)); // Zona E, 30 kg
        }

        [TestMethod]
        public void PaisCaseInsensitive()
        {
            Assert.AreEqual(14.88m * Climate, Coste("de", 1m));
        }

        [TestMethod]
        public void Fuel_SeAplicaAlPorteJuntoAlClimate()
        {
            Assert.AreEqual(14.88m * 1.10m * Climate, Coste("DE", 1m, fuel: 0.10m));
        }

        [TestMethod]
        public void NoCubre_EspanaPortugalNiPaisFueraDeLaTarifa()
        {
            Assert.AreEqual(decimal.MaxValue, Coste("ES", 1m)); // España es nacional, no EU
            Assert.AreEqual(decimal.MaxValue, Coste("PT", 1m)); // Portugal va por GLS nacional / Innovatrans
            Assert.AreEqual(decimal.MaxValue, Coste("US", 1m)); // fuera de la tarifa europea
        }

        [TestMethod]
        public void NoCubre_PesoPorEncimaDe40kg()
        {
            Assert.AreEqual(decimal.MaxValue, Coste("DE", 41m));
        }

        [TestMethod]
        public void Reembolso_SoloItalia()
        {
            // CashService (+6 €) disponible solo para Italia.
            Assert.AreEqual((15.16m * Climate) + 6.00m, Coste("IT", 1m, reembolso: 50m));
            // Para el resto de países, contra reembolso no está disponible -> no cubre.
            Assert.AreEqual(decimal.MaxValue, Coste("DE", 1m, reembolso: 50m));
        }

        [TestMethod]
        public void SuplementosPorPais_NoruegaSerbiaGrecia()
        {
            Assert.AreEqual((29.77m + 8m) * Climate, Coste("NO", 1m));   // Noruega +8 € (zona D 1 kg)
            Assert.AreEqual((29.77m + 10m) * Climate, Coste("RS", 1m));  // Serbia +10 € (zona D 1 kg)
            Assert.AreEqual((29.77m + 1m) * Climate, Coste("GR", 1m));   // Grecia hasta 3 kg +1 €
            Assert.AreEqual((57.57m + 16m) * Climate, Coste("GR", 5m));  // Grecia hasta 10 kg +16 €
        }
    }
}
