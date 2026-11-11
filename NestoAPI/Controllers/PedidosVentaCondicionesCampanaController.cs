using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using System;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Feria online 12-16/11/26 (Carlos 11/11/26): antes de CREAR un pedido (o presupuesto), el cliente (Nesto, NestoApp) pregunta
    /// si lleva productos de una campaña con condición de contado y no va ya al contado. Solo lectura; no bloquea nada.
    /// Aparte de PedidosVentaController para no engordarlo.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/PedidosVenta")]
    public class PedidosVentaCondicionesCampanaController : ApiController
    {
        private readonly Func<DateTime> hoy;

        public PedidosVentaCondicionesCampanaController() : this(() => DateTime.Today)
        {
        }

        internal PedidosVentaCondicionesCampanaController(Func<DateTime> hoy)
        {
            this.hoy = hoy;
        }

        // POST api/PedidosVenta/AvisoCondicionesCampana
        // { "Empresa": "1", "Productos": ["45383"], "FormaPago": "RCB", "PlazosPago": "1/30", "TienePrepago": false, "DescuentoPP": 0 }
        // Pronto pago (DescuentoPP > 0 o plazos con DtoProntoPago en PlazosPago) cuenta como contado: no avisa (Carlos 11/11/26).
        [HttpPost]
        [Route("AvisoCondicionesCampana")]
        public IHttpActionResult PostAvisoCondicionesCampana([FromBody] AvisoCondicionesCampanaPeticion peticion)
        {
            return Ok(AvisoCondicionesCampana.Decidir(CampanasCondicionContado.Actuales, hoy(), peticion, PlazosProntoPago.Actuales));
        }
    }
}
