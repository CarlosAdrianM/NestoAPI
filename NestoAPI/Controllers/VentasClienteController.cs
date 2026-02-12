using Microsoft.ApplicationInsights;
using NestoAPI.Infraestructure.Ventas;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/ventascliente")]
    public class VentasClienteController : ApiController
    {
        private readonly GestorVentasCliente _gestor;

        public VentasClienteController()
        {
            _gestor = new GestorVentasCliente(new NVEntities());
        }

        /// <summary>
        /// Devuelve la comparativa de ventas por agrupación.
        /// </summary>
        /// <param name="clienteId">Código del cliente</param>
        /// <param name="modoComparativa">"anual" o "ultimos12meses"</param>
        /// <param name="agruparPor">"grupo", "subgrupo", "familia"</param>
        /// <returns></returns>
        [HttpGet]
        [Route("resumen")]
        public IHttpActionResult GetComparativaVentas(
            string clienteId,
            string modoComparativa = "anual",
            string agruparPor = "grupo")
        {
            if (string.IsNullOrWhiteSpace(clienteId))
            {
                return BadRequest("Debe proporcionar el identificador del cliente.");
            }

            if (!new[] { "anual", "ultimos12meses" }.Contains(modoComparativa.ToLower()))
            {
                return BadRequest("El modo de comparativa debe ser 'anual' o 'ultimos12meses'.");
            }

            if (!new[] { "grupo", "subgrupo", "familia" }.Contains(agruparPor.ToLower()))
            {
                return BadRequest("El campo de agrupación debe ser 'grupo', 'subgrupo' o 'familia'.");
            }

            try
            {
                var telemetry = new TelemetryClient();

                // Puedes crear un diccionario con los datos relevantes
                var propiedades = new Dictionary<string, string>
                {
                    { "ClienteId", clienteId },
                    { "ModoComparativa", modoComparativa },
                    { "AgruparPor", agruparPor },
                    { "Usuario", User?.Identity?.Name ?? "Anonimo" },
                    { "Controller", nameof(VentasClienteController) },
                    { "Accion", nameof(GetComparativaVentas) },
                    { "Timestamp", DateTime.Now.ToString("o") } // formato ISO 8601
                };

                telemetry.TrackEvent("ConsultarVentasAnterioresCliente", propiedades);

                var resultado = _gestor.ObtenerComparativaVentas(
                    clienteId,
                    modoComparativa.ToLower(),
                    agruparPor.ToLower());

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                var telemetry = new TelemetryClient();
                telemetry.TrackException(ex, new Dictionary<string, string>
                {
                    { "ClienteId", clienteId },
                    { "ModoComparativa", modoComparativa },
                    { "AgruparPor", agruparPor }
                });

                return InternalServerError(ex);
            }

        }

        [HttpGet]
        [Route("resumen/detalle")]
        public IHttpActionResult GetDetalleVentasProducto(
            string clienteId,
            string filtro,
            string modoComparativa = "anual",
            string agruparPor = "grupo")
        {
            if (string.IsNullOrWhiteSpace(clienteId))
            {
                return BadRequest("Debe proporcionar el identificador del cliente.");
            }

            if (string.IsNullOrWhiteSpace(filtro))
            {
                return BadRequest("Debe proporcionar el filtro de agrupación.");
            }

            if (!new[] { "anual", "ultimos12meses" }.Contains(modoComparativa.ToLower()))
            {
                return BadRequest("El modo de comparativa debe ser 'anual' o 'ultimos12meses'.");
            }

            if (!new[] { "grupo", "subgrupo", "familia" }.Contains(agruparPor.ToLower()))
            {
                return BadRequest("El campo de agrupación debe ser 'grupo', 'subgrupo' o 'familia'.");
            }

            try
            {
                var resultado = _gestor.ObtenerDetalleVentasProducto(
                    clienteId,
                    filtro,
                    modoComparativa.ToLower(),
                    agruparPor.ToLower());

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                var telemetry = new TelemetryClient();
                telemetry.TrackException(ex, new Dictionary<string, string>
                {
                    { "ClienteId", clienteId },
                    { "Filtro", filtro },
                    { "ModoComparativa", modoComparativa },
                    { "AgruparPor", agruparPor }
                });

                return InternalServerError(ex);
            }
        }
    }
}
