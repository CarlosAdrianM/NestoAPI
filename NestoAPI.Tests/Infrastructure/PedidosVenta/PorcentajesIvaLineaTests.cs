using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models.PedidosBase;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>Novedades 1314: una línea de texto sin tipo de IVA no rompe el alta del pedido.</summary>
    [TestClass]
    public class PorcentajesIvaLineaTests
    {
        private static readonly List<ParametrosIvaBase> Parametros = new List<ParametrosIvaBase>
        {
            new ParametrosIvaBase { CodigoIvaProducto = "G21", PorcentajeIvaProducto = 0.21M, PorcentajeRecargoEquivalencia = 0.052M },
            new ParametrosIvaBase { CodigoIvaProducto = "R10", PorcentajeIvaProducto = 0.10M, PorcentajeRecargoEquivalencia = 0.014M }
        };

        [TestMethod]
        public void Buscar_LineaDeTextoSinTipoDeIva_DevuelveNullYNoLanza()
        {
            Assert.IsNull(PorcentajesIvaLinea.Buscar(Parametros, null));
            Assert.IsNull(PorcentajesIvaLinea.Buscar(Parametros, ""));
            Assert.IsNull(PorcentajesIvaLinea.Buscar(Parametros, "   "));
        }

        [TestMethod]
        public void Buscar_TipoQueNoEstaEntreLosDelPedido_DevuelveNull()
        {
            Assert.IsNull(PorcentajesIvaLinea.Buscar(Parametros, "XXX"));
        }

        [TestMethod]
        public void Buscar_TipoConRelleno_DevuelveSuParametro()
        {
            ParametrosIvaBase parametro = PorcentajesIvaLinea.Buscar(Parametros, "G21  ");
            Assert.AreEqual(0.21M, parametro.PorcentajeIvaProducto);
            Assert.AreEqual(0.052M, parametro.PorcentajeRecargoEquivalencia);
        }

        [TestMethod]
        public void Buscar_SinParametros_DevuelveNull()
        {
            Assert.IsNull(PorcentajesIvaLinea.Buscar<ParametrosIvaBase>(null, "G21"));
        }
    }
}
