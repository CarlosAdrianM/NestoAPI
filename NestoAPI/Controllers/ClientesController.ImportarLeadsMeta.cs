using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public partial class ClientesController
    {
        public const string MENSAJE_LEADS_META_SIN_PERMISO = "Solo Dirección o Informática pueden leer los leads de Meta por la API.";

        /// <summary>NestoAPI#623 corte 5c: la fuente Graph (sustituible en los tests); null = la de producción.</summary>
        internal ImportadorLeadsMetaGraph ImportadorLeadsGraph { get; set; }

        internal static bool PuedeLeerLeadsMeta(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        private ImportadorLeadsMetaGraph CrearImportadorLeadsGraph() =>
            ImportadorLeadsGraph ?? ImportadorLeadsMetaGraph.DeProduccion(AltaLeads ?? new AltaClientesLeadsGestor(_gestorClientes));

        // POST api/Clientes/Origenes/ImportarLeadsMeta?ensayo=true[&origen=META-HARMONIA-0926][&desde=2026-11-01]
        /// <summary>
        /// NestoAPI#623 corte 5c: lo mismo que hace el job <c>meta-leads</c> cada 15 minutos, a mano y con ensayo (por defecto): lee por
        /// la API Graph los leads de los formularios de los orígenes con MetaFormId y los pasa al importador del fichero. Funciona
        /// aunque el job esté apagado (MetaLeads:Activo), para probar antes de encenderlo. <paramref name="desde"/> (hora de España)
        /// sustituye a la marca de lectura. 400 sin token o sin formularios; 502 si Meta no contesta bien; 403 sin permiso.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/Origenes/ImportarLeadsMeta")]
        [ResponseType(typeof(ResultadoImportacionLeadsDTO))]
        public async Task<IHttpActionResult> PostImportarLeadsMeta(bool ensayo = true, string origen = null, DateTime? desde = null)
        {
            if (!PuedeLeerLeadsMeta(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_LEADS_META_SIN_PERMISO);
            }
            ImportadorLeadsMetaGraph importador = CrearImportadorLeadsGraph();
            if (string.IsNullOrWhiteSpace(importador.Configuracion.Token))
            {
                return BadRequest($"Falta el token de Meta ({ConfiguracionMetaLeads.CLAVE_TOKEN}) en secretos.config.");
            }
            DateTime? desdeUtc = null;
            if (desde.HasValue)
            {
                TimeZoneInfo zona = Startup.ZonaHorariaPeninsular();
                desdeUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(desde.Value, DateTimeKind.Unspecified), zona);
            }
            ResultadoImportacionLeadsMetaGraph resultado = await importador.Importar(ensayo, User?.Identity?.Name,
                string.IsNullOrWhiteSpace(origen) ? null : origen.Trim(), desdeUtc).ConfigureAwait(false);
            if (resultado.Formularios == 0)
            {
                return BadRequest(string.IsNullOrWhiteSpace(origen)
                    ? "Ningún origen activo de campaña tiene formulario de Meta (OrigenesCliente.MetaFormId)."
                    : $"El origen «{origen.Trim()}» no está activo, no es de campaña o no tiene formulario de Meta (MetaFormId).");
            }
            if (resultado.Errores.Count > 0)
            {
                return Content(HttpStatusCode.BadGateway, string.Join(" ", resultado.Errores));
            }
            return Ok(resultado.Resultado);
        }

        // GET api/Clientes/Origenes/MetaFormularios
        /// <summary>
        /// NestoAPI#623 corte 5c: los formularios de clientes potenciales de las páginas que ve el token de Meta (id, nombre, estado, nº de
        /// leads y el origen que lo tiene en MetaFormId). Para saber qué id poner en OrigenesCliente.MetaFormId. 502 si Meta falla.
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/Origenes/MetaFormularios")]
        [ResponseType(typeof(List<FormularioMetaDTO>))]
        public async Task<IHttpActionResult> GetMetaFormularios()
        {
            if (!PuedeLeerLeadsMeta(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_LEADS_META_SIN_PERMISO);
            }
            ImportadorLeadsMetaGraph importador = CrearImportadorLeadsGraph();
            if (string.IsNullOrWhiteSpace(importador.Configuracion.Token))
            {
                return BadRequest($"Falta el token de Meta ({ConfiguracionMetaLeads.CLAVE_TOKEN}) en secretos.config.");
            }
            try
            {
                return Ok(await importador.LeerFormulariosDeMeta().ConfigureAwait(false));
            }
            catch (ErrorGraphMetaException ex)
            {
                return Content(HttpStatusCode.BadGateway, ex.Message);
            }
        }
    }
}
