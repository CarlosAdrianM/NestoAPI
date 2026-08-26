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
    }
}
