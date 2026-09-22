using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Nesto#482 (22/09/26, primer día de CTT): al confirmar en Amazon un pedido enviado por CTT,
    /// Nesto no lo hacía y no dejaba rastro. Nesto tenía un respaldo que reconocía la agencia por el
    /// enlace de seguimiento, con las agencias escritas a mano. Ese respaldo ya no existe: la única
    /// fuente es lo que declara aquí cada estrategia. Esta guardia hace imposible dar de alta una
    /// agencia con perfil sin declarar sus datos de canal.
    /// </summary>
    [TestClass]
    public class CoberturaSeguimientoAgenciasTests
    {
        private static IEnumerable<IPerfilAgencia> PerfilesQueEnvian()
            => RegistroAgencias.PorReflexionSinPuerta().Perfiles
                .Where(p => p is IPerfilConSeguimiento || p is IPerfilConGestionRemota);

        [TestMethod]
        public void TodaAgenciaConPerfilDeSeguimientoOGestionRemota_TieneEstrategiaDeSeguimiento()
        {
            var sinEstrategia = PerfilesQueEnvian()
                .Where(p => !RegistroSeguimientoAgencias.Todas.Any(e => e.AgenciasId.Contains(p.AgenciaId)))
                .Select(p => $"{p.GetType().Name} (agencia {p.AgenciaId})")
                .ToList();

            Assert.AreEqual(0, sinEstrategia.Count,
                "Perfiles sin estrategia en RegistroSeguimientoAgencias: " + string.Join(", ", sinEstrategia) +
                ". Añade su clase SeguimientoXxx con AgenciasId, CarrierNameAmazon, ShippingMethodAmazon y TransportistaPrestashop.");
        }

        [TestMethod]
        public void TodaAgenciaConPerfilDeSeguimientoOGestionRemota_DeclaraSusDatosDeCanal()
        {
            var incompletas = new List<string>();
            foreach (IPerfilAgencia perfil in PerfilesQueEnvian())
            {
                IEstrategiaSeguimientoAgencia estrategia = RegistroSeguimientoAgencias.Todas.FirstOrDefault(e => e.AgenciasId.Contains(perfil.AgenciaId));
                if (estrategia == null)
                {
                    continue; // lo denuncia el otro test
                }
                if (string.IsNullOrWhiteSpace(estrategia.CarrierNameAmazon)
                    || string.IsNullOrWhiteSpace(estrategia.ShippingMethodAmazon)
                    || string.IsNullOrWhiteSpace(estrategia.TransportistaPrestashop))
                {
                    incompletas.Add($"{estrategia.Nombre} (agencia {perfil.AgenciaId})");
                }
                var datos = new DatosSeguimientoEnvio { AgenciaNombre = estrategia.Nombre, CodigoSeguimiento = "0082800082809772536836", CodigoPostal = "28001" };
                Assert.IsFalse(string.IsNullOrWhiteSpace(estrategia.ConstruirUrl(datos)), $"{estrategia.Nombre}: sin enlace de seguimiento con código y CP");
                Assert.IsFalse(string.IsNullOrWhiteSpace(estrategia.TrackingPrestashop(datos)), $"{estrategia.Nombre}: sin tracking para Prestashop");
            }

            Assert.AreEqual(0, incompletas.Count,
                "Estrategias sin datos de canal (CarrierNameAmazon/ShippingMethodAmazon/TransportistaPrestashop): " + string.Join(", ", incompletas));
        }

        [TestMethod]
        public void CTT_DeclaraLosDatosQueNestoUsaParaConfirmar()
        {
            // El caso del 22/09/26: pedido 926717 (Amazon) y 926733 (web) enviados por CTT.
            var dto = new EnvioAgenciaDTO { AgenciaNombre = "CTT", CodigoBarras = "0082800082809772536836", CodigoPostal = "28001" };

            Assert.AreEqual("CTT Express", dto.CarrierNameAmazon);
            Assert.AreEqual("Estándar", dto.ShippingMethodAmazon);
            Assert.AreEqual("0082800082809772536836", dto.NumeroSeguimiento);
            Assert.AreEqual("160", dto.TransportistaPrestashop);
            Assert.AreEqual("www.cttexpress.com/localizador-de-envios?sc=0082800082809772536836", dto.TrackingPrestashop);
        }

        [TestMethod]
        public void NingunIdDeAgenciaEstaEnDosEstrategias()
        {
            var repetidos = RegistroSeguimientoAgencias.Todas
                .SelectMany(e => e.AgenciasId.Select(id => (id, e.Nombre)))
                .GroupBy(x => x.id)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join("/", g.Select(x => x.Nombre))}")
                .ToList();

            Assert.AreEqual(0, repetidos.Count, string.Join(", ", repetidos));
        }
    }
}
