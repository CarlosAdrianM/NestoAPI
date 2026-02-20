using System.Web.Mvc;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Controller para deep linking (Android App Links) y landing page de pedidos.
    /// Issue #107: Enlace a pedido en correos + landing page
    ///
    /// Sirve:
    /// - /.well-known/assetlinks.json (Android App Links verification)
    /// - /pedido/{empresa}/{numero} (Landing page de fallback)
    /// </summary>
    public class DeepLinkController : Controller
    {
        /// <summary>
        /// Android App Links: Digital Asset Links verification file.
        /// GET /.well-known/assetlinks.json
        /// </summary>
        [AllowAnonymous]
        [Route(".well-known/assetlinks.json")]
        public ActionResult AssetLinks()
        {
            var json = @"[{
  ""relation"": [""delegate_permission/common.handle_all_urls""],
  ""target"": {
    ""namespace"": ""android_app"",
    ""package_name"": ""com.ionicframework.nestoapp958858"",
    ""sha256_cert_fingerprints"": [""4D:40:F3:B0:C7:16:2A:7C:E2:70:6C:8A:BE:7B:9E:62:49:70:F3:34:3E:D0:1E:01:14:86:0A:FF:29:62:97:EF""]
  }
}]";
            return Content(json, "application/json");
        }

        /// <summary>
        /// Landing page de pedido para navegadores de escritorio.
        /// En Android con NestoApp instalada, el App Link intercepta la URL y abre la app directamente.
        /// GET /pedido/{empresa}/{numero}
        /// </summary>
        [AllowAnonymous]
        [Route("pedido/{empresa}/{numero:int}")]
        public ActionResult Pedido(string empresa, int numero)
        {
            ViewBag.Empresa = empresa;
            ViewBag.NumeroPedido = numero;
            return View();
        }
    }
}
