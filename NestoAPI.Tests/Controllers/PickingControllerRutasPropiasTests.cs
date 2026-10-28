using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Sugerencia 854: picking de primera hora «Solo nuestra ruta». Debe sacar el picking únicamente con las rutas propias
    /// (AT y 16) de la empresa, y ninguna de agencia (OT, FW, 00...), que se quedan para el picking de las 11.
    /// </summary>
    [TestClass]
    public class PickingControllerRutasPropiasTests
    {
        private static Ruta R(string numero) => new Ruta { Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, Número = numero };

        [TestMethod]
        public async Task SacarPickingRutasPropias_LlamaAlPickingSoloConLasRutasPropias()
        {
            List<Ruta> recibidas = null;
            var pedidos = new List<PedidoPicking>();
            var controller = new PickingController
            {
                LeerRutasEmpresa = () => new List<Ruta> { R("AT "), R("OT "), R("16 "), R("00 "), R("FW "), R("33 ") },
                SacarPickingDeRutas = rutas => { recibidas = rutas; return pedidos; }
            };

            var respuesta = await controller.SacarPickingRutasPropias() as OkNegotiatedContentResult<List<PedidoPicking>>;

            Assert.IsNotNull(respuesta);
            Assert.AreSame(pedidos, respuesta.Content);
            Assert.IsNotNull(recibidas);
            CollectionAssert.AreEquivalent(new[] { "AT", "16" }, recibidas.Select(r => r.Número.Trim()).ToList());
        }

        [TestMethod]
        public void FiltrarRutasPropias_IgnoraEspaciosYMayusculasYNoDejaPasarAgencias()
        {
            var rutas = PickingController.FiltrarRutasPropias(new List<Ruta> { R("at"), R(" 16 "), R("OT"), R(null), null });

            CollectionAssert.AreEquivalent(new[] { "at", " 16 " }, rutas.Select(r => r.Número).ToList());
        }

        [TestMethod]
        public void FiltrarRutasPropias_SonLasMismasQueLasDeLaFacturacionDeRutas()
        {
            var todas = new NestoAPI.Models.Facturas.RutaPropia().RutasContenidas.Select(R).Concat(new[] { R("OT"), R("FW"), R("00") });

            var rutas = PickingController.FiltrarRutasPropias(todas);

            CollectionAssert.AreEquivalent(new NestoAPI.Models.Facturas.RutaPropia().RutasContenidas.ToList(), rutas.Select(r => r.Número).ToList());
        }
    }
}
