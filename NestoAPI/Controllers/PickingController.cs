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