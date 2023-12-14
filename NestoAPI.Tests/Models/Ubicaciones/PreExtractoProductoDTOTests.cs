using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Kits;

namespace NestoAPI.Tests.Models.Ubicaciones
{
    [TestClass]
    public class PreExtractoProductoDTOTests
    {
        [TestMethod]
        public void PreExtractoProductoDTO_CantidadPendiente_SiLasUbicacionesEstanVaciasEsIgualALaCantidad()
        {
            var sut = new PreExtractoProductoDTO()
            {
                Cantidad = -5
            };

            Assert.AreEqual(-5, sut.CantidadPendiente);
        }

        [TestMethod]
        public void PreExtractoProductoDTO_CantidadPendiente_SiTieneTieneUnaUbicacionAsignadaCantidadPendienteEsUnoMenos()
        {
            var sut = new PreExtractoProductoDTO()
            {
                Cantidad = -5
            };
            sut.Ubicaciones.Add(new UbicacionProductoDTO()
            {
                Estado = Constantes.Ubicaciones.ESTADO_REGISTRO_MONTAR_KITS,
                Cantidad = -1
            });

            Assert.AreEqual(-4, sut.CantidadPendiente);
        }
    }
}
