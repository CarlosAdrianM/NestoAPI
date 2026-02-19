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
            // TODO: Reemplazar sha256_cert_fingerprints con el fingerprint real del APK firmado.
            // Obtener con: keytool -list -v -keystore mi-keystore.jks | grep SHA256
            var json = @"[{
  ""relation"": [""delegate_permission/common.handle_all_urls""],
  ""target"": {
    ""namespace"": ""android_app"",
    ""package_name"": ""com.ionicframework.nestoapp958858"",
    ""sha256_cert_fingerprints"": [""TODO:REEMPLAZAR:CON:EL:FINGERPRINT:REAL:DEL:CERTIFICADO:DE:FIRMA""]
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
