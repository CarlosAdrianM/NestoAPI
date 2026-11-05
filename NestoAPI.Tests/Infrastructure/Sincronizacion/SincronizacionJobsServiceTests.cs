using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    [TestClass]
    public class SincronizacionJobsServiceTests
    {
        #region Referencias reservadas (las que dejaban registros de Nesto_sync pendientes para siempre)

        [TestMethod]
        public void TieneDatosMinimosParaSincronizar_ProductoCompleto_DevuelveTrue()
        {
            var producto = new Producto { Número = "17404", PVP = 12.5M, Estado = 0 };

            Assert.IsTrue(SincronizacionJobsService.TieneDatosMinimosParaSincronizar(producto));
        }

        [TestMethod]
        public void TieneDatosMinimosParaSincronizar_SinPvp_DevuelveFalse()
        {
            // Caso real: 45464/45465/45466/45476, referencias reservadas ("no usar") sin PVP. El
            // ProductoDTO hace (decimal)PVP y el job reventaba cada 5 minutos desde el 09/06/2026,
            // dejando el registro de Nesto_sync pendiente para siempre.
            var producto = new Producto { Número = "45464", PVP = null, Estado = 1 };

            Assert.IsFalse(SincronizacionJobsService.TieneDatosMinimosParaSincronizar(producto));
        }

        [TestMethod]
        public void TieneDatosMinimosParaSincronizar_SinEstado_DevuelveFalse()
        {
            var producto = new Producto { Número = "45464", PVP = 12.5M, Estado = null };

            Assert.IsFalse(SincronizacionJobsService.TieneDatosMinimosParaSincronizar(producto));
        }

        #endregion Referencias reservadas

        #region Sin solapar pasadas (carga inicial NestoAPI#498, 23/09/26)

        [TestMethod]
        public void IntentarEmpezar_ConLaPasadaAnteriorEnMarcha_NoArrancaOtra()
        {
            // Una carga de miles de clientes dura más que los 5 minutos del cron: la pasada
            // siguiente releía las mismas filas sin marcar y las publicaba otra vez.
            const string job = "test-sin-solapar";
            try
            {
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job));
                Assert.IsFalse(SincronizacionJobsService.IntentarEmpezar(job), "Con la anterior viva no arranca otra");
            }
            finally
            {
                SincronizacionJobsService.Terminar(job);
            }

            Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job), "Terminada la anterior, la siguiente sí arranca");
            SincronizacionJobsService.Terminar(job);
        }

        [TestMethod]
        public void IntentarEmpezar_JobsDistintos_NoSeBloqueanEntreSi()
        {
            try
            {
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar("test-a"));
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar("test-b"));
            }
            finally
            {
                SincronizacionJobsService.Terminar("test-a");
                SincronizacionJobsService.Terminar("test-b");
            }
        }

        #endregion

        #region Candado caducado (NestoAPI#820, incidente 04-05/11/26)

        private static readonly System.DateTime T0 = new System.DateTime(2026, 11, 4, 18, 55, 0, System.DateTimeKind.Utc);
        private static readonly System.TimeSpan CADUCIDAD = System.TimeSpan.FromMinutes(30);

        [TestMethod]
        public void IntentarEmpezar_AnteriorDentroDelPlazo_NoEntra()
        {
            const string job = "test-candado-vivo";
            try
            {
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0, CADUCIDAD, out System.TimeSpan? c1));
                Assert.IsNull(c1);
                Assert.IsFalse(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(30), CADUCIDAD, out System.TimeSpan? c2),
                    "30 minutos justos: la anterior aún se considera viva");
                Assert.IsNull(c2);
            }
            finally
            {
                SincronizacionJobsService.Terminar(job);
            }
        }

        [TestMethod]
        public void IntentarEmpezar_AnteriorCaducada_EntraYDiceCuantoLlevaba()
        {
            // El 04/11 la pasada se quedó colgada en Pub/Sub; sin caducidad el nombre seguía ocupado hasta reciclar el pool.
            const string job = "test-candado-caducado";
            try
            {
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0, CADUCIDAD, out _));
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(45), CADUCIDAD, out System.TimeSpan? caducado));
                Assert.AreEqual(System.TimeSpan.FromMinutes(45), caducado);
                Assert.IsFalse(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(50), CADUCIDAD, out _),
                    "La nueva ha cogido el candado: una tercera no entra");
            }
            finally
            {
                SincronizacionJobsService.Terminar(job);
            }
        }

        [TestMethod]
        public void Terminar_LaPasadaDadaPorMuertaVuelve_NoSueltaElCandadoDeLaNueva()
        {
            const string job = "test-candado-vuelve";
            try
            {
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0, CADUCIDAD, out _));
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(40), CADUCIDAD, out _));

                Assert.IsFalse(SincronizacionJobsService.Terminar(job, T0), "La vieja no suelta el candado de la nueva");
                Assert.IsFalse(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(41), CADUCIDAD, out _));
                Assert.IsTrue(SincronizacionJobsService.Terminar(job, T0.AddMinutes(40)), "La nueva sí lo suelta");
                Assert.IsTrue(SincronizacionJobsService.IntentarEmpezar(job, T0.AddMinutes(42), CADUCIDAD, out _));
            }
            finally
            {
                SincronizacionJobsService.Terminar(job);
            }
        }

        #endregion
    }
}
