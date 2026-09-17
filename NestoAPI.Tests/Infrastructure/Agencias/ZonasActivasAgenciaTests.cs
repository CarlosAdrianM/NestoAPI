using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#493: freno por zonas para arrancar CTT "de menos a más" (parámetro CTTZonasActivas).
    /// Fuera de las zonas activas la tarifa de CTT no cubre y el comparador elige la siguiente; una
    /// etiqueta elegida a mano se rechaza con un mensaje claro. Vacío = todas las zonas.
    /// </summary>
    [TestClass]
    public class ZonasActivasAgenciaTests
    {
        private const string CP_MADRID = "28001";
        private const string CP_VALENCIA = "46001";
        private const string CP_PORTUGAL = "1000-001";

        [TestMethod]
        public void Parsear_VacioONulo_EsSinFreno_YNombresDesconocidosSeIgnoran()
        {
            Assert.IsNull(ZonasActivasAgencia.Parsear(null));
            Assert.IsNull(ZonasActivasAgencia.Parsear("   "));
            ISet<ZonasEnvioAgencia> zonas = ZonasActivasAgencia.Parsear(" provincial , Peninsular, Marte ");
            CollectionAssert.AreEquivalent(new[] { ZonasEnvioAgencia.Provincial, ZonasEnvioAgencia.Peninsular }, zonas.ToList());
            Assert.IsTrue(ZonasActivasAgencia.EstaActiva(null, ZonasEnvioAgencia.Portugal), "Sin freno, todo activo");
            Assert.IsFalse(ZonasActivasAgencia.EstaActiva(zonas, ZonasEnvioAgencia.Portugal));
        }

        [TestMethod]
        public void TarifaConZonasActivas_FueraDeZona_NoCubre_YDentroCuestaLoMismo()
        {
            var ctt = new TarifaCTT48h();
            var frenada = new TarifaConZonasActivas(ctt, ZonasActivasAgencia.Parsear("Provincial"));

            Assert.AreEqual(ctt.CalcularCoste(CP_MADRID, "ES", 1m, 0m, 0m), frenada.CalcularCoste(CP_MADRID, "ES", 1m, 0m, 0m), "Madrid es provincial: mismo coste");
            Assert.AreEqual(decimal.MaxValue, frenada.CalcularCoste(CP_VALENCIA, "ES", 1m, 0m, 0m), "Peninsular no está activa");
            Assert.AreEqual(decimal.MaxValue, frenada.CalcularCoste(CP_PORTUGAL, "PT", 1m, 0m, 0m), "Portugal no está activa");
            Assert.AreEqual(ctt.AgenciaId, frenada.AgenciaId);
            Assert.AreEqual(ctt.ServicioId, frenada.ServicioId);
        }

        [TestMethod]
        public void RegistroConZonasActivas_SoloEnvuelveLaAgenciaFrenada()
        {
            IRegistroTarifas registro = new RegistroTarifasConZonasActivas(new RegistroTarifas(), Constantes.Agencias.AGENCIA_CTT, ZonasActivasAgencia.Parsear("Provincial"));

            List<ITarifaAgencia> tarifas = registro.Todas().ToList();
            Assert.IsTrue(tarifas.Where(t => t.AgenciaId == Constantes.Agencias.AGENCIA_CTT).All(t => t is TarifaConZonasActivas));
            Assert.IsTrue(tarifas.Where(t => t.AgenciaId != Constantes.Agencias.AGENCIA_CTT).All(t => !(t is TarifaConZonasActivas)), "GLS e Innovatrans no se tocan");
            Assert.AreEqual(new RegistroTarifas().Todas().Count(), tarifas.Count, "Mismo número de tarifas");

            IRegistroTarifas sinFreno = new RegistroTarifasConZonasActivas(new RegistroTarifas(), Constantes.Agencias.AGENCIA_CTT, null);
            Assert.IsTrue(sinFreno.Todas().All(t => !(t is TarifaConZonasActivas)), "Parámetro vacío = sin envolver");
        }

        [TestMethod]
        public void Comparador_ConFrenoProvincial_CTTGanaEnMadridPeroNoEnValencia()
        {
            // CTT (sin sombra en este test) es la más barata en ambas zonas; con el freno solo puede
            // ganar en la provincial. El fuel es 0 para no depender de la BD.
            IRegistroTarifas registro = new RegistroTarifasConZonasActivas(new RegistroTarifas(), Constantes.Agencias.AGENCIA_CTT, ZonasActivasAgencia.Parsear("Provincial"));
            var comparador = new ComparadorAgencias(registro, new FuelCero());

            Assert.AreEqual(Constantes.Agencias.AGENCIA_CTT, comparador.MasEconomica("1", CP_MADRID, 1m, 0m).AgenciaId);
            Assert.AreNotEqual(Constantes.Agencias.AGENCIA_CTT, comparador.MasEconomica("1", CP_VALENCIA, 1m, 0m).AgenciaId);
            Assert.IsNull(comparador.CosteDeAgencia("1", CP_VALENCIA, 1m, 0m, Constantes.Agencias.AGENCIA_CTT), "Sin cobertura en la zona frenada");
        }

        [TestMethod]
        public void PerfilCTT_ValidarDestino_RechazaFueraDeZona_YAceptaDentroOSinFreno()
        {
            Assert.IsNull(PerfilAgenciaCTT.MensajeZonaInactiva(CP_MADRID, "Provincial"));
            Assert.IsNull(PerfilAgenciaCTT.MensajeZonaInactiva(CP_VALENCIA, ""), "Sin freno, todo vale");
            Assert.IsNull(PerfilAgenciaCTT.MensajeZonaInactiva(CP_VALENCIA, null));

            string mensaje = PerfilAgenciaCTT.MensajeZonaInactiva(CP_VALENCIA, "Provincial");
            StringAssert.Contains(mensaje, "CTT todavía no está activa");
            StringAssert.Contains(mensaje, "Peninsular");
            StringAssert.Contains(mensaje, "CTTZonasActivas");
            Assert.IsInstanceOfType(new PerfilAgenciaCTT(), typeof(IPerfilConReglasDestino));
        }

        private class FuelCero : IProveedorRecargoCombustible
        {
            public decimal RecargoCombustible(string empresa, int agenciaId) => 0m;
        }
    }
}
