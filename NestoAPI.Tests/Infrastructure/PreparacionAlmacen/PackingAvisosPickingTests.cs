using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#95 (envío 929911, Alfredo, Novedades 1014): en el picking 100002 Andre dio por falta 2 × 43498; al terminar, la línea
    /// salió del picking y Alfredo le dio a mano el 100003. El packing tiene que avisar de la falta y del otro picking pendiente.
    /// </summary>
    [TestClass]
    public class PackingAvisosPickingTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 100002;
        private const int PEDIDO = 929911;
        private const int OTRO_PEDIDO = 929913;

        private static FilaPackingAlmacen Fila(int pedido, string cliente, string producto, int cantidad = 1)
            => new FilaPackingAlmacen
            {
                Pedido = pedido, Cliente = cliente, Contacto = "0", Nombre = "CLIENTE " + cliente, LineaPedido = pedido * 10 + cantidad,
                Producto = producto, Descripcion = "PRODUCTO " + producto, CodigoBarras = "8437020512176", Cantidad = cantidad
            };

        private static FaltaPickingAlmacen Falta(string producto, int cantidad) => new FaltaPickingAlmacen
        {
            Producto = producto, Descripcion = "ILUMINA SERUM", Cantidad = cantidad, Usuario = "NUEVAVISION\\Andre",
            Fecha = new DateTime(2026, 11, 4, 11, 9, 10)
        };

        private static LineaFueraDelPicking Fuera(int pedido, string producto, int cantidad, int picking, int estado) => new LineaFueraDelPicking
        {
            Pedido = pedido, Producto = producto + "   ", Descripcion = "ILUMINA SERUM  ", Cantidad = cantidad, Picking = picking, Estado = estado
        };

        private static PackingAlmacenDTO DosEntregas() => ServicioPreparacionAlmacen.MontarPacking(EMPRESA, PICKING, new[]
        {
            Fila(PEDIDO, "15191", "17404"),
            Fila(OTRO_PEDIDO, "20000", "43498", 3)
        });

        [TestMethod]
        public void RepartirAvisos_FaltaConLaLineaEnOtroPicking_VaASuEntregaConOtroPickingPendiente()
        {
            PackingAlmacenDTO packing = DosEntregas();

            ServicioPreparacionAlmacen.RepartirAvisosDelPicking(packing,
                new[] { Falta("43498", 2) },
                new[] { Fuera(PEDIDO, "43498", 2, 100003, 1) });

            EntregaPackingAlmacenDTO suya = packing.Entregas.Single(e => e.Cliente == "15191");
            FaltaPickingPackingDTO falta = suya.FaltasDelPicking.Single();
            Assert.AreEqual("43498", falta.Producto);
            Assert.AreEqual("ILUMINA SERUM", falta.Descripcion);
            Assert.AreEqual(2, falta.Cantidad);
            Assert.AreEqual("Andre", falta.Usuario, "Sin el dominio");
            Assert.AreEqual(new DateTime(2026, 11, 4, 11, 9, 10), falta.Fecha);

            OtroPickingPendienteDTO otro = suya.OtrosPickingsPendientes.Single();
            Assert.AreEqual(100003, otro.Picking);
            LineaOtroPickingDTO linea = otro.Lineas.Single();
            Assert.AreEqual(PEDIDO, linea.Pedido);
            Assert.AreEqual("43498", linea.Producto);
            Assert.AreEqual("ILUMINA SERUM", linea.Descripcion);
            Assert.AreEqual(2, linea.Cantidad);

            EntregaPackingAlmacenDTO otra = packing.Entregas.Single(e => e.Cliente == "20000");
            Assert.AreEqual(0, otra.FaltasDelPicking.Count, "Lleva el 43498 en este picking, pero la falta no era suya");
            Assert.AreEqual(0, otra.OtrosPickingsPendientes.Count);
        }

        [TestMethod]
        public void RepartirAvisos_FaltaConLaLineaPendiente_SinOtroPicking()
        {
            PackingAlmacenDTO packing = DosEntregas();

            ServicioPreparacionAlmacen.RepartirAvisosDelPicking(packing,
                new[] { Falta("43498", 2) },
                new[] { Fuera(PEDIDO, "43498", 2, 0, -1) });

            EntregaPackingAlmacenDTO suya = packing.Entregas.Single(e => e.Cliente == "15191");
            Assert.AreEqual(2, suya.FaltasDelPicking.Single().Cantidad);
            Assert.AreEqual(0, suya.OtrosPickingsPendientes.Count, "Una línea pendiente (-1, sin picking) no es otro picking");
        }

        [TestMethod]
        public void RepartirAvisos_PickingSinTerminar_LaFaltaVaALaEntregaQueLlevaElProducto()
        {
            PackingAlmacenDTO packing = DosEntregas();

            ServicioPreparacionAlmacen.RepartirAvisosDelPicking(packing, new[] { Falta("43498", 1) }, new LineaFueraDelPicking[0]);

            Assert.AreEqual(1, packing.Entregas.Single(e => e.Cliente == "20000").FaltasDelPicking.Single().Cantidad);
            Assert.AreEqual(0, packing.Entregas.Single(e => e.Cliente == "15191").FaltasDelPicking.Count);
        }

        [TestMethod]
        public void RepartirAvisos_SinFaltasNiOtrosPickings_ListasVacias()
        {
            PackingAlmacenDTO packing = DosEntregas();

            ServicioPreparacionAlmacen.RepartirAvisosDelPicking(packing, null, null);

            Assert.IsTrue(packing.Entregas.All(e => e.FaltasDelPicking.Count == 0 && e.OtrosPickingsPendientes.Count == 0));
        }

        [TestMethod]
        public void RepartirAvisos_OtroPickingDeOtroPedido_NoSaleEnEstaEntrega()
        {
            PackingAlmacenDTO packing = DosEntregas();

            ServicioPreparacionAlmacen.RepartirAvisosDelPicking(packing, null, new[]
            {
                Fuera(OTRO_PEDIDO, "11111", 1, 100005, 1),
                Fuera(OTRO_PEDIDO, "22222", 4, 100005, 1),
                Fuera(OTRO_PEDIDO, "33333", 1, 100004, 1)
            });

            Assert.AreEqual(0, packing.Entregas.Single(e => e.Cliente == "15191").OtrosPickingsPendientes.Count);
            List<OtroPickingPendienteDTO> otros = packing.Entregas.Single(e => e.Cliente == "20000").OtrosPickingsPendientes;
            CollectionAssert.AreEqual(new[] { 100004, 100005 }, otros.Select(o => o.Picking).ToArray(), "Por número de picking");
            Assert.AreEqual(2, otros[1].Lineas.Count);
        }

        [TestMethod]
        public async Task LeerPacking_PideLosAvisosUnaVezPorPacking()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, null))
                .Returns(new List<FilaPackingAlmacen> { Fila(PEDIDO, "15191", "17404"), Fila(OTRO_PEDIDO, "20000", "43498", 3) });
            A.CallTo(() => repositorio.LeerFaltasDelPicking(EMPRESA, PICKING)).Returns(new List<FaltaPickingAlmacen> { Falta("43498", 2) });
            A.CallTo(() => repositorio.LeerLineasFueraDelPicking(EMPRESA, PICKING, null))
                .Returns(new List<LineaFueraDelPicking> { Fuera(PEDIDO, "43498", 2, 100003, 1) });
            var servicio = new ServicioPreparacionAlmacen(repositorio, A.Fake<IAlmacenFotosBultos>());

            PackingAlmacenDTO packing = await servicio.LeerPacking(EMPRESA, PICKING);

            EntregaPackingAlmacenDTO suya = packing.Entregas.Single(e => e.Cliente == "15191");
            Assert.AreEqual(1, suya.FaltasDelPicking.Count);
            Assert.AreEqual(100003, suya.OtrosPickingsPendientes.Single().Picking);
            A.CallTo(() => repositorio.LeerFaltasDelPicking(EMPRESA, PICKING)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerLineasFueraDelPicking(EMPRESA, PICKING, null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LeerPackingDePedido_FiltraLasLineasFueraPorEsePedido()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.PickingEnCursoDelPedido(EMPRESA, PEDIDO)).Returns((int?)PICKING);
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, PEDIDO))
                .Returns(new List<FilaPackingAlmacen> { Fila(PEDIDO, "15191", "17404") });
            A.CallTo(() => repositorio.LeerLineasFueraDelPicking(EMPRESA, PICKING, PEDIDO))
                .Returns(new List<LineaFueraDelPicking> { Fuera(PEDIDO, "43498", 2, 100003, 1) });
            var servicio = new ServicioPreparacionAlmacen(repositorio, A.Fake<IAlmacenFotosBultos>());

            PackingAlmacenDTO packing = await servicio.LeerPackingDePedido(EMPRESA, PEDIDO);

            Assert.AreEqual(100003, packing.Entregas.Single().OtrosPickingsPendientes.Single().Picking);
        }

        [TestMethod]
        public async Task LeerPacking_SiFallanLosAvisos_ElPackingSaleIgual()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPacking(EMPRESA, PICKING, null))
                .Returns(new List<FilaPackingAlmacen> { Fila(PEDIDO, "15191", "17404") });
            A.CallTo(() => repositorio.LeerFaltasDelPicking(EMPRESA, PICKING)).Throws(new InvalidOperationException("sin conexión"));
            var servicio = new ServicioPreparacionAlmacen(repositorio, A.Fake<IAlmacenFotosBultos>());

            PackingAlmacenDTO packing = await servicio.LeerPacking(EMPRESA, PICKING);

            Assert.AreEqual(1, packing.Entregas.Count);
            Assert.AreEqual(0, packing.Entregas.Single().FaltasDelPicking.Count);
        }
    }
}
