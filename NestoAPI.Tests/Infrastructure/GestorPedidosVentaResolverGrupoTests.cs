using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#319 (remate de #249, caso real pedido 922572 / producto 25720): la conversión del
    /// grupo comisionable arrastraba el SUBGRUPO de la ficha, que es independiente por grupo
    /// (COS/107 y PEL/107 no tienen nada que ver) y podía no existir bajo el grupo convertido:
    /// FK_LinPedidoVta_SubGruposProducto reventaba y el pedido no se podía guardar.
    /// Regla (Carlos 17/07/26): la línea convertida lleva un subgrupo YA EXISTENTE del grupo
    /// convertido (jamás se crean): el por defecto por convención (código = grupo) o el primero.
    /// </summary>
    [TestClass]
    public class GestorPedidosVentaResolverGrupoTests
    {
        private IServicioPedidosVenta servicio;
        private GestorPedidosVenta gestor;
        private Producto producto;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioPedidosVenta>();
            gestor = new GestorPedidosVenta(servicio);
            // El caso real: bobina de aluminio, ficha PEL/DES, marcada para comisionar por ACC
            producto = new Producto { Número = "25720", Grupo = "PEL", SubGrupo = "DES" };
            A.CallTo(() => servicio.LeerGruposComisionablesAlternativos("1", "25720"))
                .Returns(new List<string> { "ACC" });
            A.CallTo(() => servicio.LeerVendedorDeUsuario("1", A<string>.Ignored)).Returns("DLS");
            // El cliente comisiona ACC por DLS (quien mete el pedido) y PEL no tiene registro:
            // la regla de #249 convierte PEL -> ACC.
            A.CallTo(() => servicio.LeerVendedoresClienteGrupo("1", "28512", "0"))
                .Returns(new List<VendedorGrupoProductoDTO>
                {
                    new VendedorGrupoProductoDTO { grupoProducto = "ACC", vendedor = "DLS" }
                });
        }

        [TestMethod]
        public void ResolverGrupoProducto_ConversionDeGrupo_UsaUnSubgrupoExistenteDelGrupoConvertido()
        {
            // El servicio garantiza que el subgrupo devuelto EXISTE (por defecto ACC/ACC o el primero)
            A.CallTo(() => servicio.LeerSubGrupoParaGrupo("1", "ACC")).Returns("ACC");

            GestorPedidosVenta.GrupoSubgrupoLinea resultado =
                gestor.ResolverGrupoProducto(producto, "1", "28512", "0", "JGP", @"NUEVAVISION\dlopez");

            Assert.AreEqual("ACC", resultado.Grupo);
            Assert.AreEqual("ACC", resultado.SubGrupo,
                "El subgrupo de la ficha (DES) es independiente por grupo: la línea convertida lleva uno existente de ACC.");
        }

        [TestMethod]
        public void ResolverGrupoProducto_GrupoSinSubgrupos_LanzaErrorClaroParaNoComisionarMal()
        {
            // Caso extremo (no debería pasar): el grupo convertido no tiene ningún subgrupo.
            // Regla de Carlos: la comisión toca el bolsillo de un compañero — mejor error claro y
            // bloqueante que comisionar por la ficha en silencio (o que la FK críptica).
            A.CallTo(() => servicio.LeerSubGrupoParaGrupo("1", "ACC")).Returns(null);

            System.Exception ex = Assert.ThrowsException<System.Exception>(() =>
                gestor.ResolverGrupoProducto(producto, "1", "28512", "0", "JGP", @"NUEVAVISION\dlopez"));

            StringAssert.Contains(ex.Message, "25720");
            StringAssert.Contains(ex.Message, "ACC");
            StringAssert.Contains(ex.Message, "subgrupo");
        }

        [TestMethod]
        public void ResolverGrupoProducto_ProductoSinMarcar_FichaIntactaYSinConsultasExtra()
        {
            // Lo normal (producto no marcado): grupo y subgrupo de la ficha, sin tocar SubGruposProducto
            A.CallTo(() => servicio.LeerGruposComisionablesAlternativos("1", "25720"))
                .Returns(new List<string>());

            GestorPedidosVenta.GrupoSubgrupoLinea resultado =
                gestor.ResolverGrupoProducto(producto, "1", "28512", "0", "JGP", @"NUEVAVISION\dlopez");

            Assert.AreEqual("PEL", resultado.Grupo);
            Assert.AreEqual("DES", resultado.SubGrupo);
            A.CallTo(() => servicio.LeerSubGrupoParaGrupo(A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }
    }
}
