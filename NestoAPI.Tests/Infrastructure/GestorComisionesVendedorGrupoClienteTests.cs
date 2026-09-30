using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// ELMAH 29/09/26 (Sancho, tres intentos de guardar una ficha de cliente): al guardar se
    /// grababa en VendedoresClienteGrupoProducto el usuario que venía en el DTO, que llegaba
    /// vacío, y el guardado entero reventaba con «El campo Usuario es obligatorio».
    /// </summary>
    [TestClass]
    public class GestorComisionesVendedorGrupoClienteTests
    {
        private static VendedorClienteGrupoProducto Fila(string vendedor, string usuario)
        {
            return new VendedorClienteGrupoProducto { Vendedor = vendedor, Usuario = usuario, Estado = 0 };
        }

        [TestMethod]
        public void AplicarVendedorGrupo_MismoVendedorYDtoSinUsuario_ConservaElUsuarioQueHabia()
        {
            // El caso del error: no cambia nada y el DTO no trae usuario
            VendedorClienteGrupoProducto actual = Fila("IF ", @"NUEVAVISION\Manuel");
            var nuevo = new VendedorGrupoProductoDTO { vendedor = "IF", estado = 0, usuario = null };

            GestorComisiones.AplicarVendedorGrupo(actual, nuevo, @"NUEVAVISION\Sancho");

            Assert.AreEqual(@"NUEVAVISION\Manuel", actual.Usuario);
        }

        [TestMethod]
        public void AplicarVendedorGrupo_CambiaElVendedor_GrabaQuienLoCambia()
        {
            VendedorClienteGrupoProducto actual = Fila("IF ", @"NUEVAVISION\Manuel");
            var nuevo = new VendedorGrupoProductoDTO { vendedor = "NV", estado = 0 };

            GestorComisiones.AplicarVendedorGrupo(actual, nuevo, @"NUEVAVISION\Sancho");

            Assert.AreEqual("NV", actual.Vendedor);
            Assert.AreEqual(@"NUEVAVISION\Sancho", actual.Usuario);
        }

        [TestMethod]
        public void AplicarVendedorGrupo_CambiaElVendedorYNoSeSabeQuien_NuncaDejaElUsuarioVacio()
        {
            VendedorClienteGrupoProducto actual = Fila("IF ", @"NUEVAVISION\Manuel");
            var nuevo = new VendedorGrupoProductoDTO { vendedor = "NV", estado = 0 };

            GestorComisiones.AplicarVendedorGrupo(actual, nuevo, null);

            Assert.AreEqual(UsuarioAuditoriaHelper.DESCONOCIDO, actual.Usuario);
        }

        [TestMethod]
        public void AplicarVendedorGrupo_LaFilaNoTeniaUsuario_SeRellena()
        {
            VendedorClienteGrupoProducto actual = Fila("IF ", null);
            var nuevo = new VendedorGrupoProductoDTO { vendedor = "IF", estado = 0 };

            GestorComisiones.AplicarVendedorGrupo(actual, nuevo, @"NUEVAVISION\Sancho");

            Assert.AreEqual(@"NUEVAVISION\Sancho", actual.Usuario);
        }

        [TestMethod]
        public void AplicarVendedorGrupo_ElClienteNoMandaVendedoresPorGrupo_NoTocaNada()
        {
            // Antes daba NullReferenceException
            VendedorClienteGrupoProducto actual = Fila("IF ", @"NUEVAVISION\Manuel");

            GestorComisiones.AplicarVendedorGrupo(actual, null, @"NUEVAVISION\Sancho");

            Assert.AreEqual("IF ", actual.Vendedor);
            Assert.AreEqual(@"NUEVAVISION\Manuel", actual.Usuario);
        }

        [TestMethod]
        public void AplicarVendedorGrupo_CambiaSoloElEstado_LoGrabaSinCambiarElUsuario()
        {
            VendedorClienteGrupoProducto actual = Fila("IF ", @"NUEVAVISION\Manuel");
            var nuevo = new VendedorGrupoProductoDTO { vendedor = "IF", estado = 9 };

            GestorComisiones.AplicarVendedorGrupo(actual, nuevo, @"NUEVAVISION\Sancho");

            Assert.AreEqual((short)9, actual.Estado);
            Assert.AreEqual(@"NUEVAVISION\Manuel", actual.Usuario);
        }
    }
}
