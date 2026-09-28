using Hangfire;
using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Infraestructure.PreciosMedios;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Issue #547, corte (b): diagnóstico de la sombra de precios medios. SOLO LECTURA: calcula con la calculadora
    /// en C# y compara con lo que dejó el SP; no escribe en Productos, LinPedidoCmp ni LinPedidoVta. Solo Dirección
    /// e Informática.
    /// </summary>
    [RoutePrefix("api/PreciosMedios")]
    public class PreciosMediosController : ApiController
    {
        /// <summary>Máximo de productos en una sombra parcial (la pasada completa va por Hangfire).</summary>
        internal const int MAXIMO_PRODUCTOS_PARCIAL = 50;

        private readonly Func<ServicioSombraPreciosMedios> crearServicio;
        private readonly Func<string> encolarPasadaCompleta;

        public PreciosMediosController()
            : this(PreciosMediosJobsService.CrearServicio,
                  () => BackgroundJob.Enqueue(() => PreciosMediosJobsService.ProcesarSombraManual()))
        {
        }

        internal PreciosMediosController(Func<ServicioSombraPreciosMedios> crearServicio, Func<string> encolarPasadaCompleta)
        {
            this.crearServicio = crearServicio;
            this.encolarPasadaCompleta = encolarPasadaCompleta;
        }

        /// <summary>
        /// GET api/PreciosMedios/1/41281?ventas=true — tramos, costes por línea y avisos que calcula el C# para un
        /// producto, y su diferencia con lo que hay en la BD (con <c>ventas=true</c>, también las líneas de venta).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("{empresa}/{producto}")]
        public IHttpActionResult GetSombraProducto(string empresa, string producto, bool ventas = false)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(producto))
            {
                return BadRequest("Faltan la empresa o el producto");
            }
            ServicioSombraPreciosMedios servicio = crearServicio();
            DateTime corte = servicio.CorteSP(PreciosMediosJobsService.CorteSP(DateTime.Now));
            return Ok(servicio.CompararProducto(empresa.Trim(), producto.Trim(), corte, ventas));
        }

        /// <summary>
        /// POST api/PreciosMedios/Sombra?empresa=1&amp;productos=41281,44904&amp;ventas=true — sombra de unos pocos
        /// productos (máximo 50), en el momento, sin registrar nada.
        /// POST api/PreciosMedios/Sombra?completa=true — encola en Hangfire la pasada completa (empresas 1, 4 y 5),
        /// que registra en PreciosMediosSombra aunque el interruptor del job semanal esté apagado.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("Sombra")]
        public IHttpActionResult PostSombra(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, string productos = null,
            bool ventas = false, bool completa = false)
        {
            if (!EsDireccionOInformatica())
            {
                return StatusCode(HttpStatusCode.Forbidden);
            }
            if (completa)
            {
                string idTrabajo = encolarPasadaCompleta();
                return Ok(new { Encolada = true, IdTrabajo = idTrabajo });
            }

            List<string> lista = (productos ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (lista.Count == 0)
            {
                return BadRequest("Indica los productos (productos=41281,44904) o completa=true para la pasada completa");
            }
            if (lista.Count > MAXIMO_PRODUCTOS_PARCIAL)
            {
                return BadRequest($"Como mucho {MAXIMO_PRODUCTOS_PARCIAL} productos; para más, completa=true");
            }

            ServicioSombraPreciosMedios servicio = crearServicio();
            DateTime corte = servicio.CorteSP(PreciosMediosJobsService.CorteSP(DateTime.Now));
            List<ResultadoSombraPrecioMedio> resultados = lista
                .Select(p => servicio.CompararProducto(empresa.Trim(), p, corte, ventas))
                .ToList();
            return Ok(new
            {
                CorteSP = corte,
                Resumen = resultados.GroupBy(r => r.Clasificacion.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                Productos = resultados
            });
        }

        private bool EsDireccionOInformatica()
        {
            return User != null && (User.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION) ||
                User.IsInRoleSinDominio(NovedadesController.GRUPO_INFORMATICA));
        }
    }
}
