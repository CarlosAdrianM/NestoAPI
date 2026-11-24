using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
        private readonly ILectorDisponibleCampanaStock lectorStock;
        private readonly IEnumerable<ProductoCampanaStock> filasStock;
        private readonly ICalculadorPrecioSinCampana calculadorSinCampana;

        public PedidosVentaCondicionesCampanaController() : this(() => DateTime.Today)
        {
        }

        internal PedidosVentaCondicionesCampanaController(Func<DateTime> hoy, ILectorDisponibleCampanaStock lectorStock = null,
            IEnumerable<ProductoCampanaStock> filasStock = null, ICalculadorPrecioSinCampana calculadorSinCampana = null)
        {
            this.hoy = hoy;
            this.lectorStock = lectorStock;
            this.filasStock = filasStock;
            this.calculadorSinCampana = calculadorSinCampana;
        }

        // POST api/PedidosVenta/AvisoCampanaSoloEnStock (NestoAPI#889, Carlos 24/11/26): el pedido tal como se va a guardar (numero 0 si es
        // nuevo). Solo lectura, siempre 200: si alguna línea de una campaña «solo en stock» no llega al stock, la pregunta (Mensaje, de
        // usted, sin la pregunta final). El cliente guarda después con CampanaSoloEnStock = «QuitarDescuento» o «MantenerDescuento».
        [HttpPost]
        [Route("AvisoCampanaSoloEnStock")]
        public async Task<IHttpActionResult> PostAvisoCampanaSoloEnStock([FromBody] PedidoVentaDTO pedido)
        {
            return Ok(await ServicioCampanaSoloEnStock.Consultar(pedido, hoy(), lectorStock, filasStock, calculadorSinCampana).ConfigureAwait(true));
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
