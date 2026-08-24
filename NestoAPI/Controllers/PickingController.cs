using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public class PickingController : ApiController
    {
        private GestorPicking gestorPicking;
        // GET: api/Picking/1/654321
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking(string empresa, int numeroPedido)
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking(empresa, numeroPedido));

            return Ok(gestorPicking.PedidosEnPicking());
        }

        // GET: api/Picking
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking()
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking());

            return Ok(gestorPicking.PedidosEnPicking());
        }

        /// <summary>
        /// NestoAPI#361: el picking AUTOMÁTICO de las 11h, el que lanza la tarea del Task
        /// Scheduler. Hace exactamente lo mismo que <see cref="SacarPicking()"/>, pero además
        /// AVISA POR CORREO al almacén del resultado, porque ahí no hay nadie mirando la pantalla.
        ///
        /// Es un endpoint aparte y no un parámetro del de siempre por dos razones: la diferencia
        /// no es una preferencia configurable sino QUIÉN llama (un parámetro global podría acabar
        /// mandando correos en los pickings manuales), y añadir un bool opcional a SacarPicking()
        /// crearía ambigüedad de ruta con la sobrecarga que recibe el cliente.
        ///
        /// La excepción se relanza igual que antes: así el Task Scheduler ve que ha fallado y los
        /// errores técnicos siguen llegando a ELMAH. Lo único que se añade es el aviso.
        ///
        /// Y, sobre todo, DECLARA su horizonte de entrega (hoy) en vez de deducirlo del reloj.
        /// Antes el horizonte salía de la hora exacta de arranque, así que a las 10:59:59 servía
        /// hoy y a las 11:00:01 servía también lo de MAÑANA, adelantando un día las entregas sin
        /// que se notase. Se toreaba programando la tarea a las 10:59:40, a costa de dejar fuera
        /// los pedidos metidos en esos últimos 20 segundos. Ahora da igual el segundo en que
        /// arranque: la tarea se puede programar a las 11:00 en punto (o más tarde) y el
        /// resultado es el mismo.
        /// </summary>
        [HttpGet]
        [Route("api/Picking/Automatico")]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPickingAutomatico()
        {
            crearModulos();
            try
            {
                await Task.Run(() => gestorPicking.SacarPicking(DateTime.Today));
            }
            catch (Exception ex)
            {
                CrearAvisador().Avisar(ex, DateTime.Now);
                throw;
            }

            return Ok(gestorPicking.PedidosEnPicking());
        }

        internal virtual AvisadorPickingAutomatico CrearAvisador()
        {
            return new AvisadorPickingAutomatico(
                new Infraestructure.ServicioCorreoElectronico(),
                new Infraestructure.LectorParametrosUsuario());
        }

        // GET: api/Picking/15191
        [HttpGet]
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> SacarPicking(string cliente)
        {
            crearModulos();
            await Task.Run(() => gestorPicking.SacarPicking(cliente));

            return Ok(gestorPicking.PedidosEnPicking());
        }

        private void crearModulos()
        {
            ModulosPicking modulos = new ModulosPicking();
            modulos.rellenadorPicking = new RellenadorPickingService();
            modulos.rellenadorStocks = new RellenadorStocksService();
            modulos.rellenadorUbicaciones = new RellenadorUbicacionesService();
            modulos.finalizador = new FinalizadorPicking();

            gestorPicking = new GestorPicking(modulos);
        }
    }

}