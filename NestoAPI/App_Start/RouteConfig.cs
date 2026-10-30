using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace NestoAPI
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Issue #121: Ignorar rutas de pago para que las maneje Web API (PagoRedirectController)
            routes.IgnoreRoute("pago/{*pathInfo}");

            // NestoAPI#744: la página pública de seguimiento del reparto propio la sirve Web API (SeguimientoController)
            routes.IgnoreRoute("seguimiento/{*pathInfo}");

            // NestoAPI#737: los enlaces de descarga del pedido o la factura (WhatsApp) los sirve Web API (DescargaDocumentoController)
            routes.IgnoreRoute("d/{*pathInfo}");

            // Issue #107: Habilitar attribute routing para DeepLinkController
            routes.MapMvcAttributeRoutes();

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
