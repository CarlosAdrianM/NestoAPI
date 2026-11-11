using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Sugerencia 1184: «Retenido» en la lista de pedidos de Nesto (GET api/PedidosVenta?vendedor=).</summary>
    [TestClass]
    public class RetenidosListaPedidosTests
    {
        [TestMethod]
        public void Marcar_SoloLosPedidosConRetencionViva_QuedanRetenidos()
        {
            IRepositorioPedidosRetenidos repositorio = A.Fake<IRepositorioPedidosRetenidos>();
            A.CallTo(() => repositorio.LeerActivasSincrono("1", A<IEnumerable<int>>._))
                .Returns(new List<RetencionPedidoDTO> { new RetencionPedidoDTO { Empresa = "1", Pedido = 200 } });
            List<ResumenPedidoVentaDTO> lista = new List<ResumenPedidoVentaDTO>
            {
                new ResumenPedidoVentaDTO { empresa = "1", numero = 100 },
                new ResumenPedidoVentaDTO { empresa = "1", numero = 200 }
            };

            RetenidosListaPedidos.Marcar(lista, repositorio);

            Assert.IsFalse(lista[0].estaRetenido);
            Assert.IsTrue(lista[1].estaRetenido);
        }

        [TestMethod]
        public void Marcar_UnaSolaConsultaPorEmpresa_ConTodosLosNumeros()
        {
            IRepositorioPedidosRetenidos repositorio = A.Fake<IRepositorioPedidosRetenidos>();
            A.CallTo(() => repositorio.LeerActivasSincrono(A<string>._, A<IEnumerable<int>>._)).Returns(new List<RetencionPedidoDTO>());
            List<ResumenPedidoVentaDTO> lista = new List<ResumenPedidoVentaDTO>
            {
                new ResumenPedidoVentaDTO { empresa = "1", numero = 100 },
                new ResumenPedidoVentaDTO { empresa = "1", numero = 101 },
                new ResumenPedidoVentaDTO { empresa = "1", numero = 102 }
            };

            RetenidosListaPedidos.Marcar(lista, repositorio);

            A.CallTo(() => repositorio.LeerActivasSincrono("1", A<IEnumerable<int>>.That.Matches(n => n.OrderBy(x => x).SequenceEqual(new[] { 100, 101, 102 }))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.LeerActivasSincrono(A<string>._, A<IEnumerable<int>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Marcar_ElMismoNumeroEnOtraEmpresa_NoSeConfunde()
        {
            IRepositorioPedidosRetenidos repositorio = A.Fake<IRepositorioPedidosRetenidos>();
            A.CallTo(() => repositorio.LeerActivasSincrono("1", A<IEnumerable<int>>._))
                .Returns(new List<RetencionPedidoDTO> { new RetencionPedidoDTO { Empresa = "1", Pedido = 100 } });
            A.CallTo(() => repositorio.LeerActivasSincrono("3", A<IEnumerable<int>>._)).Returns(new List<RetencionPedidoDTO>());
            List<ResumenPedidoVentaDTO> lista = new List<ResumenPedidoVentaDTO>
            {
                new ResumenPedidoVentaDTO { empresa = "1", numero = 100 },
                new ResumenPedidoVentaDTO { empresa = "3", numero = 100 }
            };

            RetenidosListaPedidos.Marcar(lista, repositorio);

            Assert.IsTrue(lista[0].estaRetenido);
            Assert.IsFalse(lista[1].estaRetenido);
        }

        [TestMethod]
        public void Marcar_SiFallaLaConsulta_LaListaSaleSinMarcarYNoRevienta()
        {
            IRepositorioPedidosRetenidos repositorio = A.Fake<IRepositorioPedidosRetenidos>();
            A.CallTo(() => repositorio.LeerActivasSincrono(A<string>._, A<IEnumerable<int>>._)).Throws(new InvalidOperationException("sin BD"));
            List<ResumenPedidoVentaDTO> lista = new List<ResumenPedidoVentaDTO> { new ResumenPedidoVentaDTO { empresa = "1", numero = 100 } };

            RetenidosListaPedidos.Marcar(lista, repositorio);

            Assert.IsFalse(lista[0].estaRetenido);
        }

        [TestMethod]
        public void Marcar_ListaVacia_NoConsulta()
        {
            IRepositorioPedidosRetenidos repositorio = A.Fake<IRepositorioPedidosRetenidos>();

            RetenidosListaPedidos.Marcar(new List<ResumenPedidoVentaDTO>(), repositorio);

            A.CallTo(() => repositorio.LeerActivasSincrono(A<string>._, A<IEnumerable<int>>._)).MustNotHaveHappened();
        }
    }
}
