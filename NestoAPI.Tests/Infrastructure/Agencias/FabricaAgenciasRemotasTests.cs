using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Factory de agencias remotas: solo Innovatrans (Numero=12) tiene gestión remota; el resto de
    /// agencias devuelve null (sin integración server-side) para que el llamante haga solo el flujo BD.
    /// </summary>
    [TestClass]
    public class FabricaAgenciasRemotasTests
    {
        private readonly IFabricaAgenciasRemotas _fabrica = new FabricaAgenciasRemotas();

        [TestMethod]
        public void Crear_Innovatrans_DevuelveLaEstrategia()
        {
            IAgenciaRemota agencia = _fabrica.Crear(Constantes.Agencias.AGENCIA_INNOVATRANS);

            Assert.IsNotNull(agencia);
            Assert.IsInstanceOfType(agencia, typeof(AgenciaRemotaInnovatrans));
        }

        [TestMethod]
        public void Crear_AgenciaSinIntegracion_DevuelveNull()
        {
            Assert.IsNull(_fabrica.Crear(Constantes.Agencias.AGENCIA_GLS));
            Assert.IsNull(_fabrica.Crear(Constantes.Agencias.AGENCIA_CANTERAS));
            Assert.IsNull(_fabrica.Crear(0));
        }
    }
}
