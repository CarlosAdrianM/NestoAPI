﻿using NestoAPI.Infraestructure.Ventas;
using NestoAPI.Models;
using System;
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
                var resultado = _gestor.ObtenerComparativaVentas(
                    clienteId,
                    modoComparativa.ToLower(),
                    agruparPor.ToLower());

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
