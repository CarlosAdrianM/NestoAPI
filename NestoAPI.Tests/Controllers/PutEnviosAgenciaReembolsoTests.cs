using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#512 (22/09/26, envío 248984 de CTT): el botón «Modificar Envío» de la pestaña En curso
    /// manda el envío entero por PUT y cambiaba el reembolso solo en nuestra BD (sin historial, sin
    /// contabilidad), mientras CTT seguía con los 500 € del manifiesto. Regla pura del bloqueo.
    /// </summary>
    [TestClass]
    public class PutEnviosAgenciaReembolsoTests
    {
        private const string ALBARAN_CTT = "0082800082809772552452";

        [TestMethod]
        public void RegistradoEnAgenciaRemota_CambiarReembolso_SeBloquea()
        {
            Assert.IsTrue(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 0M, ALBARAN_CTT, (short)Constantes.Agencias.ESTADO_EN_CURSO, agenciaConGestionRemota: true));
            Assert.IsTrue(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 0M, ALBARAN_CTT, (short)Constantes.Agencias.ESTADO_TRAMITADO, agenciaConGestionRemota: true));
        }

        [TestMethod]
        public void RegistradoEnAgenciaRemota_MismoReembolso_Pasa()
        {
            // Cambiar otra cosa del envío (observaciones, teléfono) sin tocar el reembolso sigue permitido.
            Assert.IsFalse(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 500M, ALBARAN_CTT, (short)Constantes.Agencias.ESTADO_EN_CURSO, agenciaConGestionRemota: true));
        }

        [TestMethod]
        public void PendienteSinAlbaran_CambiarReembolso_Pasa()
        {
            // Antes de registrar en la agencia el reembolso se puede corregir libremente.
            Assert.IsFalse(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 0M, null, (short)Constantes.Agencias.ESTADO_PENDIENTE, agenciaConGestionRemota: true));
            Assert.IsFalse(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 0M, "  ", (short)Constantes.Agencias.ESTADO_EN_CURSO, agenciaConGestionRemota: true));
        }

        [TestMethod]
        public void AgenciaClasica_CambiarReembolso_Pasa()
        {
            // GLS manda el reembolso al cierre del día: cambiarlo antes es lo normal.
            Assert.IsFalse(EnviosAgenciasController.CambioDeReembolsoBloqueado(500M, 0M, "61197140248079", (short)Constantes.Agencias.ESTADO_EN_CURSO, agenciaConGestionRemota: false));
        }
    }
}
