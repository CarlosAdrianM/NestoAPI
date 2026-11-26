using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ParametrosUsuario;
using NestoAPI.Models;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using System.Web.Http;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>Auditoría de seguridad (26/11/26): endpoints que no llevaban [Authorize] y el PUT de ParametrosUsuario.</summary>
    [TestClass]
    public class SeguridadEndpointsSinAuthorizeTests
    {
        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var identidad = new ClaimsIdentity("Bearer");
            identidad.AddClaim(new Claim(ClaimTypes.Name, nombre));
            foreach (string g in grupos)
            {
                identidad.AddClaim(new Claim(ClaimTypes.Role, g));
            }
            return new ClaimsPrincipal(identidad);
        }

        private static IPrincipal Anonimo()
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        private static bool TieneAuthorize(System.Type tipo, string metodo)
        {
            MethodInfo m = tipo.GetMethods().Single(x => x.Name == metodo);
            return m.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any()
                || tipo.GetCustomAttributes(typeof(AuthorizeAttribute), true).Any();
        }

        [TestMethod]
        public void DeudaVencida_LlevaAuthorize()
        {
            Assert.IsTrue(TieneAuthorize(typeof(ExtractosClienteController), nameof(ExtractosClienteController.GetDeudaVencida)));
        }

        [TestMethod]
        public void GetAlbaran_LlevaAuthorize()
        {
            Assert.IsTrue(TieneAuthorize(typeof(AlbaranesVentaController), nameof(AlbaranesVentaController.GetAlbaran)));
        }

        [TestMethod]
        public void EnviarCorreoDomiciliacionDia_SigueSinAuthorize_PorqueLaLlamaUnaTareaExterna()
        {
            // Si se cierra, que sea a sabiendas: hoy no hay ningún llamante con token conocido.
            Assert.IsFalse(TieneAuthorize(typeof(ExtractosClienteController), nameof(ExtractosClienteController.EnviarCorreoDomiciliacionDia)));
        }

        [TestMethod]
        public void PutParametroUsuario_SigueSinAuthorize_PorqueNestoGuardaSinToken()
        {
            Assert.IsFalse(TieneAuthorize(typeof(ParametrosUsuarioController), nameof(ParametrosUsuarioController.PutParametroUsuario)));
        }

        [TestMethod]
        public void EsClaveDePermiso_ReconoceLasClavesYLosPrefijos()
        {
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso("PermitirCrearPedidoConErroresValidacion"));
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso(" permitirVerTodosLosPedidos "));
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso("Cupones.PuedeAdelantar"));
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso("NestoViejo_Algo"));
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso("Validacion.DescuentosCriteriosEstrictos"));
            Assert.IsTrue(ReglaCambioParametroUsuario.EsClaveDePermiso("InventarioBorrarFilas"));
            Assert.IsFalse(ReglaCambioParametroUsuario.EsClaveDePermiso("AlmacenesPlantillaVenta"));
            Assert.IsFalse(ReglaCambioParametroUsuario.EsClaveDePermiso("DelegaciónDefecto"));
            Assert.IsFalse(ReglaCambioParametroUsuario.EsClaveDePermiso(null));
        }

        [TestMethod]
        public void MotivoNoPermitido_NestoSinToken_SusPreferencias_SePuede()
        {
            // Lo de siempre: Nesto manda AgenciasEnCuarentena, AlmacenPedidoVta… de su usuario sin token.
            Assert.IsNull(ReglaCambioParametroUsuario.MotivoNoPermitido(Anonimo(), "Carlos", "AgenciasEnCuarentena"));
        }

        [TestMethod]
        public void MotivoNoPermitido_SinToken_ClaveDePermiso_NoSePuede()
        {
            string motivo = ReglaCambioParametroUsuario.MotivoNoPermitido(Anonimo(), "Carlos", "PermitirCrearPedidoConErroresValidacion");
            Assert.IsNotNull(motivo);
            StringAssert.Contains(motivo, "Dirección o Informática");
        }

        [TestMethod]
        public void MotivoNoPermitido_SinToken_FilaDefecto_NoSePuede()
        {
            Assert.AreEqual(ReglaCambioParametroUsuario.MOTIVO_FILA_DEFECTO,
                ReglaCambioParametroUsuario.MotivoNoPermitido(Anonimo(), "(defecto)", "AlmacenPedidoVta"));
        }

        [TestMethod]
        public void MotivoNoPermitido_Vendedor_SusParametros_SePuede_LosDeOtro_No()
        {
            IPrincipal vendedor = Usuario("Pilar", "Vendedores");
            Assert.IsNull(ReglaCambioParametroUsuario.MotivoNoPermitido(vendedor, "pilar ", "AlmacenesPlantillaVenta"));
            string motivo = ReglaCambioParametroUsuario.MotivoNoPermitido(vendedor, "Laura", "AlmacenesPlantillaVenta");
            Assert.AreEqual(string.Format(ReglaCambioParametroUsuario.MOTIVO_OTRO_USUARIO, "Laura"), motivo);
        }

        [TestMethod]
        public void MotivoNoPermitido_EmpleadoConDominio_SusParametros_SePuede()
        {
            Assert.IsNull(ReglaCambioParametroUsuario.MotivoNoPermitido(Usuario(@"NUEVAVISION\Carlos", @"NUEVAVISION\Almacén"), "Carlos", "PlantillaMantenerAbierta"));
        }

        [TestMethod]
        public void MotivoNoPermitido_UsuarioConToken_SuPropioPermiso_NoSePuede()
        {
            Assert.IsNotNull(ReglaCambioParametroUsuario.MotivoNoPermitido(Usuario("Pilar"), "Pilar", "PermitirCrearPedidoConErroresValidacion"));
        }

        [TestMethod]
        public void MotivoNoPermitido_DireccionEInformatica_Todo()
        {
            Assert.IsNull(ReglaCambioParametroUsuario.MotivoNoPermitido(Usuario(@"NUEVAVISION\Carlos", @"NUEVAVISION\" + Constantes.GruposSeguridad.DIRECCION), "Pilar", "PermitirCrearPedidoConErroresValidacion"));
            Assert.IsNull(ReglaCambioParametroUsuario.MotivoNoPermitido(Usuario("Informatico", Constantes.GruposSeguridad.INFORMATICA), "(defecto)", "Cupones.PuedeAdelantar"));
        }
    }
}
