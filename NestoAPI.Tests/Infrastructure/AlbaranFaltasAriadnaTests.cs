using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Nesto#508: un albarán no puede salir con productos que el mozo dio por «No está» en Ariadna y que todavía no se
    /// han quitado del pedido (la salida sin terminar). Lo comprueba el núcleo común (ServicioAlbaranesVenta), así que
    /// vale para Facturar rutas, «Facturar al imprimir etiqueta» y los botones de albarán del detalle.
    /// </summary>
    [TestClass]
    public class AlbaranFaltasAriadnaTests
    {
        [TestMethod]
        public async Task CrearAlbaran_ConFaltasSinQuitar_NoLlamaAlProcedimientoYDiceQueFalta()
        {
            bool trasAlbaran = false;
            var faltas = new List<FaltaSinQuitar>
            {
                new FaltaSinQuitar { Picking = 99739, Producto = "22624", Cantidad = 1 }
            };
            var servicio = new ServicioAlbaranesVenta(A.Fake<NVEntities>(),
                (e, p, a, u) => { trasAlbaran = true; return Task.CompletedTask; },
                (e, p) => Task.FromResult(faltas));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.CrearAlbaran("1", 927646, "Carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "927646");
            StringAssert.Contains(ex.Message, "99739");
            StringAssert.Contains(ex.Message, "1 ud. de 22624");
            StringAssert.Contains(ex.Message, "Ariadna");
            Assert.IsFalse(trasAlbaran);
        }

        [TestMethod]
        public void Mensaje_VariasFaltas_LasNombraTodas()
        {
            string mensaje = FaltasSinQuitarSalida.Mensaje(927646, new List<FaltaSinQuitar>
            {
                new FaltaSinQuitar { Picking = 99739, Producto = "22624", Cantidad = 1 },
                new FaltaSinQuitar { Picking = 99739, Producto = "17404", Cantidad = 3 }
            });

            StringAssert.Contains(mensaje, "1 ud. de 22624");
            StringAssert.Contains(mensaje, "3 uds. de 17404");
        }

        [TestMethod]
        public async Task GestorAlbaranes_ErrorDeNegocio_SeRelanzaTalCual()
        {
            var servicio = A.Fake<IServicioAlbaranesVenta>();
            var original = new NestoBusinessException("El picking 99739 del pedido 927646 tiene faltas sin quitar")
            {
                StatusCode = HttpStatusCode.Conflict
            };
            A.CallTo(() => servicio.CrearAlbaran("1", 927646, "Carlos", null)).ThrowsAsync(original);
            var gestor = new GestorAlbaranesVenta(servicio);

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => gestor.CrearAlbaran("1", 927646, "Carlos"));

            Assert.AreSame(original, ex);
        }
    }
}
