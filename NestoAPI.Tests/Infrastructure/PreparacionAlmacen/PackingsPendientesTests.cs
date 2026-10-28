using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#720 (Andre): en la lista de salidas solo lo pendiente; una entrega etiquetada desde Nesto cuenta como hecha.</summary>
    [TestClass]
    public class PackingsPendientesTests
    {
        private static EntregaPorEmpaquetar Entrega(int picking, string cliente, int pedidos, int conEtiqueta, int bultosConFoto = 0, int bultosSinFoto = 0, bool terminado = true)
            => new EntregaPorEmpaquetar
            {
                Picking = picking, Cliente = cliente, Contacto = "0", Nombre = $"Cliente {cliente}", PrimerPedido = 900000 + picking, Pedidos = pedidos, Unidades = 10,
                PedidosConEtiqueta = conEtiqueta, BultosConFoto = bultosConFoto, BultosSinFoto = bultosSinFoto, Terminado = terminado
            };

        [TestMethod]
        public void EntregaEmpaquetada_ConFotoDeTodosLosBultos_OConTodosLosPedidosEtiquetados()
        {
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(99958, "1", pedidos: 1, conEtiqueta: 0, bultosConFoto: 2)));
            Assert.IsFalse(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(99958, "1", pedidos: 1, conEtiqueta: 0, bultosConFoto: 2, bultosSinFoto: 1)), "un bulto sin foto");
            Assert.IsTrue(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(99958, "1", pedidos: 2, conEtiqueta: 2)), "etiquetas hechas desde Nesto");
            Assert.IsFalse(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(99958, "1", pedidos: 2, conEtiqueta: 1)), "falta un pedido por etiquetar");
            Assert.IsFalse(ServicioPreparacionAlmacen.EntregaEmpaquetada(Entrega(99958, "1", pedidos: 1, conEtiqueta: 0)));
        }

        [TestMethod]
        public void MontarPackingsPendientes_PickingConTodoEtiquetado_YaNoSale_YElMixtoCuentaLoQueFalta()
        {
            List<PackingPendienteDTO> pendientes = ServicioPreparacionAlmacen.MontarPackingsPendientes(new[]
            {
                Entrega(99958, "1", 1, conEtiqueta: 1),
                Entrega(99958, "2", 1, conEtiqueta: 1),
                Entrega(99957, "3", 1, conEtiqueta: 1),
                Entrega(99957, "4", 2, conEtiqueta: 1),
                Entrega(99956, "5", 1, conEtiqueta: 0, terminado: false)
            });

            CollectionAssert.AreEqual(new[] { 99957, 99956 }, pendientes.Select(p => p.Picking).ToList(), "el 99958 está entero; el más reciente primero");
            PackingPendienteDTO mixto = pendientes.Single(p => p.Picking == 99957);
            Assert.AreEqual(2, mixto.Entregas);
            Assert.AreEqual(1, mixto.EntregasSinEmpaquetar);
            Assert.AreEqual("Cliente 4", mixto.Nombre, "la primera sin empaquetar");
            Assert.IsTrue(mixto.RecogidoEnAriadna);
            Assert.IsFalse(pendientes.Single(p => p.Picking == 99956).RecogidoEnAriadna);
        }
    }
}
