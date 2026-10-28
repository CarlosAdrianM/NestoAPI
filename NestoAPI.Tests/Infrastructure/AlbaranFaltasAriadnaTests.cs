using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
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
        public async Task CrearAlbaran_PedidoRetenido_NoLlamaAlProcedimiento_698()
        {
            bool trasAlbaran = false;
            var servicio = new ServicioAlbaranesVenta(A.Fake<NVEntities>(),
                (e, p, a, u) => { trasAlbaran = true; return Task.CompletedTask; },
                (e, p) => Task.FromResult(new List<FaltaSinQuitar>()),
                (e, p) => Task.FromResult(p == 929057 ? "El pedido 929057 está retenido por Alfredo desde el 28/10/2026 hasta nuevo aviso: Cliente de viaje. No se puede enviar ni albaranear" : null));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.CrearAlbaran("1", 929057, "Carlos"));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.Contains(ex.Message, "retenido");
            StringAssert.Contains(ex.Message, "Cliente de viaje");
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

        // Carlos 13/10/26: «No hay líneas para albaranear» (error del SP) pasa a decir por qué.
        private static ServicioAlbaranesVenta.LineaParaAlbaranear L(short estado, DateTime? fecha = null)
            => new ServicioAlbaranesVenta.LineaParaAlbaranear { Estado = estado, FechaEntrega = fecha };

        [TestMethod]
        public void SinLineasParaAlbaranear_YaFacturado_LoDice()
        {
            string motivo = ServicioAlbaranesVenta.MotivoSinLineasParaAlbaranear(928317, new[] { L(4), L(4) }, new DateTime(2026, 10, 13));

            Assert.AreEqual("El pedido 928317 ya está albaranado o facturado: no le queda nada que albaranear.", motivo);
        }

        [TestMethod]
        public void SinLineasParaAlbaranear_LoQueQuedaEsDeMasAdelante_DiceDesdeCuando()
        {
            string motivo = ServicioAlbaranesVenta.MotivoSinLineasParaAlbaranear(922520,
                new[] { L(4, new DateTime(2026, 7, 21)), L(1, new DateTime(2026, 12, 15)), L(1, new DateTime(2026, 11, 15)) }, new DateTime(2026, 10, 13));

            Assert.AreEqual("Lo que le queda al pedido 922520 tiene fecha de entrega a partir del 15/11/2026: hasta entonces no hay nada que albaranear.", motivo);
        }

        [TestMethod]
        public void SinLineasParaAlbaranear_PendienteSinPicking_DiceQueNoEstaListo()
        {
            string motivo = ServicioAlbaranesVenta.MotivoSinLineasParaAlbaranear(928400, new[] { L(-1, new DateTime(2026, 10, 13)) }, new DateTime(2026, 10, 13));

            StringAssert.Contains(motivo, "todavía no está listo para albaranear");
        }
    }
}
