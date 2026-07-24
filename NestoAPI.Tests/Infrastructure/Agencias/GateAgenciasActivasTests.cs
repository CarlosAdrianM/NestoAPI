using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#258 (Fase 1): la puerta de agencias activas por el parámetro AgenciasEnCuarentena
    /// (lista de nombres). Se testea el núcleo puro (sin BBDD) con la sobrecarga interna.
    /// </summary>
    [TestClass]
    public class GateAgenciasActivasTests
    {
        private static List<AgenciaTransporte> Agencias() => new List<AgenciaTransporte>
        {
            new AgenciaTransporte { Numero = 1, Nombre = "ASM" },
            new AgenciaTransporte { Numero = 8, Nombre = "Correos Express" },
            new AgenciaTransporte { Numero = 10, Nombre = "Sending" },
            new AgenciaTransporte { Numero = 12, Nombre = "Innovatrans" }
        };

        [TestMethod]
        public void LasEnCuarentenaQuedanInactivas_ElRestoActivas()
        {
            var gate = new GateAgenciasActivasPorCuarentena(Agencias(), "Sending, Correos Express");

            Assert.IsTrue(gate.EstaActiva(1), "ASM no está en cuarentena");
            Assert.IsTrue(gate.EstaActiva(12), "Innovatrans no está en cuarentena");
            Assert.IsFalse(gate.EstaActiva(8), "Correos Express está en cuarentena");
            Assert.IsFalse(gate.EstaActiva(10), "Sending está en cuarentena");
        }

        [TestMethod]
        public void AgenciaSinFilaEnAgenciasTransporte_EsInactiva()
        {
            var gate = new GateAgenciasActivasPorCuarentena(Agencias(), "");
            Assert.IsFalse(gate.EstaActiva(999), "Sin fila en AgenciasTransporte no puede estar activa");
        }

        [TestMethod]
        public void Cuarentena_IgnoraEspaciosYMayusculasEnLosNombres()
        {
            var gate = new GateAgenciasActivasPorCuarentena(Agencias(), "  sending ,  CORREOS EXPRESS ");
            Assert.IsFalse(gate.EstaActiva(10));
            Assert.IsFalse(gate.EstaActiva(8));
            Assert.IsTrue(gate.EstaActiva(12));
        }

        [TestMethod]
        public void CuarentenaVaciaONula_TodasLasExistentesQuedanActivas()
        {
            Assert.IsTrue(new GateAgenciasActivasPorCuarentena(Agencias(), null).EstaActiva(8));
            Assert.IsTrue(new GateAgenciasActivasPorCuarentena(Agencias(), "").EstaActiva(10));
        }
    }
}
