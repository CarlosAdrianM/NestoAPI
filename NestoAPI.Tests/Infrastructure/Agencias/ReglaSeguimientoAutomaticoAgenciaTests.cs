using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Sugerencia 1329 (18/11/26): qué agencias tienen seguimiento automático (su estado avanza solo) y, por tanto, pueden salir
    /// en Retrasados. Canteras (sin integración) no pasa nunca a Entregado y llenaba la lista 60 días.
    /// </summary>
    [TestClass]
    public class ReglaSeguimientoAutomaticoAgenciaTests
    {
        private class Puerta : IGateAgenciasActivas
        {
            private readonly ISet<int> inactivas;
            public Puerta(params int[] inactivas) { this.inactivas = new HashSet<int>(inactivas); }
            public bool EstaActiva(int agenciaId) => !inactivas.Contains(agenciaId);
        }

        private static RegistroAgencias Registro(params int[] inactivas)
            => new RegistroAgencias(RegistroAgencias.DescubrirPerfiles(typeof(RegistroAgencias).Assembly), new Puerta(inactivas));

        [TestMethod]
        public void ConLosPerfilesReales_LasQueConsultaElJobYElRepartoPropio()
        {
            ISet<int> conSeguimiento = ReglaSeguimientoAutomaticoAgencia.AgenciasConSeguimientoAutomatico(Registro());

            Assert.IsTrue(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_GLS));
            Assert.IsTrue(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_INNOVATRANS));
            Assert.IsTrue(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_CTT));
            Assert.IsTrue(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_REPARTO_PROPIO),
                "el repartidor cierra sus envíos desde Ariadna: un tramitado de días es un retraso de verdad");
        }

        [TestMethod]
        public void ConLosPerfilesReales_CanterasYLasDemasSinIntegracionNo()
        {
            ISet<int> conSeguimiento = ReglaSeguimientoAutomaticoAgencia.AgenciasConSeguimientoAutomatico(Registro());

            Assert.IsFalse(ReglaSeguimientoAutomaticoAgencia.TieneSeguimientoAutomatico(Constantes.Agencias.AGENCIA_CANTERAS, conSeguimiento),
                "Canteras va por correo: nunca pasa a Entregado");
            Assert.IsFalse(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO), "su perfil no existe: el job no la consulta");
            Assert.IsFalse(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_SENDING));
            Assert.IsFalse(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS));
            Assert.IsFalse(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_GLOVO));
        }

        [TestMethod]
        public void UnaAgenciaEnCuarentena_DejaDeTenerSeguimientoComoEnElJob()
        {
            ISet<int> conSeguimiento = ReglaSeguimientoAutomaticoAgencia.AgenciasConSeguimientoAutomatico(Registro(Constantes.Agencias.AGENCIA_GLS));

            Assert.IsFalse(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_GLS), "la puerta de activas manda, igual que en el job");
            Assert.IsTrue(conSeguimiento.Contains(Constantes.Agencias.AGENCIA_CTT));
        }

        [TestMethod]
        public void EstadosPropios_SoloElRepartoPropio()
        {
            CollectionAssert.AreEquivalent(new[] { Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                ReglaSeguimientoAutomaticoAgencia.AgenciasConEstadosPropios.ToArray());
        }

        [TestMethod]
        public void Union_LasDelJobMasLasDeEstadosPropios_SinRepetir()
        {
            ISet<int> conSeguimiento = ReglaSeguimientoAutomaticoAgencia.AgenciasConSeguimientoAutomatico(new[] { 1, 12, 13 }, new[] { 14, 13 });

            CollectionAssert.AreEquivalent(new[] { 1, 12, 13, 14 }, conSeguimiento.ToArray());
        }

        [TestMethod]
        public void SinListas_NingunaTieneSeguimiento()
        {
            ISet<int> vacio = ReglaSeguimientoAutomaticoAgencia.AgenciasConSeguimientoAutomatico((IEnumerable<int>)null, null);

            Assert.AreEqual(0, vacio.Count);
            Assert.IsFalse(ReglaSeguimientoAutomaticoAgencia.TieneSeguimientoAutomatico(1, null));
        }
    }
}
