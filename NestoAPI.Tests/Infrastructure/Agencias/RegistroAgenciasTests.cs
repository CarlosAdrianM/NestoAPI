using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#258 (Fase 1): el registro descubre los perfiles de agencia por reflexión y se queda
    /// solo con los activos (según la puerta). El resto del sistema consultará por AgenciaId/capacidad.
    /// </summary>
    [TestClass]
    public class RegistroAgenciasTests
    {
        // Perfiles de prueba (no dependen de la BBDD; viven en el ensamblado de tests, así que la
        // reflexión sobre el ensamblado de NestoAPI NO los recoge).
        private class PerfilRemotoConSeguimiento : IPerfilConGestionRemota, IPerfilConSeguimiento
        {
            public int AgenciaId => 12;
        }
        private class PerfilSoloSeguimiento : IPerfilConSeguimiento { public int AgenciaId => 1; }
        private class PerfilSuelto : IPerfilAgencia { public int AgenciaId => 8; }

        private class GateFake : IGateAgenciasActivas
        {
            private readonly HashSet<int> _activas;
            public GateFake(params int[] activas) { _activas = new HashSet<int>(activas); }
            public bool EstaActiva(int agenciaId) => _activas.Contains(agenciaId);
        }

        [TestMethod]
        public void Registro_SoloDejaLosPerfilesQueLaPuertaMarcaActivos()
        {
            var registro = new RegistroAgencias(
                new IPerfilAgencia[] { new PerfilRemotoConSeguimiento(), new PerfilSoloSeguimiento(), new PerfilSuelto() },
                new GateFake(12, 1)); // 8 NO activa

            Assert.AreEqual(2, registro.Perfiles.Count);
            Assert.IsNotNull(registro.Perfil(12));
            Assert.IsNotNull(registro.Perfil(1));
            Assert.IsNull(registro.Perfil(8), "La agencia inactiva no aparece aunque su clase de perfil exista");
        }

        [TestMethod]
        public void ConCapacidad_DevuelveSoloLasAgenciasQueTienenEsaCapacidad()
        {
            var registro = new RegistroAgencias(
                new IPerfilAgencia[] { new PerfilRemotoConSeguimiento(), new PerfilSoloSeguimiento() },
                new GateFake(12, 1));

            CollectionAssert.AreEquivalent(new[] { 12 },
                registro.ConCapacidad<IPerfilConGestionRemota>().ToList());
            CollectionAssert.AreEquivalent(new[] { 12, 1 },
                registro.ConCapacidad<IPerfilConSeguimiento>().ToList());
        }

        [TestMethod]
        public void ConCapacidad_ExcluyeLasInactivasAunqueTenganLaCapacidad()
        {
            var registro = new RegistroAgencias(
                new IPerfilAgencia[] { new PerfilRemotoConSeguimiento(), new PerfilSoloSeguimiento() },
                new GateFake(1)); // solo la 1 activa; la 12 (con gestión remota) queda fuera

            Assert.AreEqual(0, registro.ConCapacidad<IPerfilConGestionRemota>().Count);
            CollectionAssert.AreEquivalent(new[] { 1 }, registro.ConCapacidad<IPerfilConSeguimiento>().ToList());
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void Registro_DosPerfilesParaLaMismaAgencia_Lanza()
        {
            _ = new RegistroAgencias(
                new IPerfilAgencia[] { new PerfilRemotoConSeguimiento(), new PerfilRemotoConSeguimiento() },
                new GateFake(12));
        }

        [TestMethod]
        public void Reflexion_DescubreLosPerfilesRealesDelEnsamblado()
        {
            List<int> ids = RegistroAgencias
                .DescubrirPerfiles(typeof(RegistroAgencias).Assembly)
                .Select(p => p.AgenciaId)
                .ToList();

            CollectionAssert.Contains(ids, Constantes.Agencias.AGENCIA_INNOVATRANS);
            CollectionAssert.Contains(ids, Constantes.Agencias.AGENCIA_GLS);
            CollectionAssert.Contains(ids, Constantes.Agencias.AGENCIA_CANTERAS);
        }

        [TestMethod]
        public void PorReflexion_ConPuertaQueActivaLasReales_LasIncluye()
        {
            RegistroAgencias registro = RegistroAgencias.PorReflexion(new GateFake(
                Constantes.Agencias.AGENCIA_INNOVATRANS,
                Constantes.Agencias.AGENCIA_GLS,
                Constantes.Agencias.AGENCIA_CANTERAS));

            Assert.IsNotNull(registro.Perfil(Constantes.Agencias.AGENCIA_INNOVATRANS));
            CollectionAssert.Contains(
                registro.ConCapacidad<IPerfilConGestionRemota>().ToList(),
                Constantes.Agencias.AGENCIA_INNOVATRANS);
            CollectionAssert.Contains(
                registro.ConCapacidad<IPerfilConSeguimiento>().ToList(),
                Constantes.Agencias.AGENCIA_GLS);
        }

        [TestMethod]
        public void PorReflexion_ConPuertaQueDesactivaUna_NoLaIncluye()
        {
            // La clase de Innovatrans existe, pero si la puerta no la marca activa NO entra.
            RegistroAgencias registro = RegistroAgencias.PorReflexion(new GateFake(
                Constantes.Agencias.AGENCIA_GLS));

            Assert.IsNull(registro.Perfil(Constantes.Agencias.AGENCIA_INNOVATRANS),
                "Aunque exista PerfilAgenciaInnovatrans, la puerta puede dejarla fuera");
        }
    }
}
