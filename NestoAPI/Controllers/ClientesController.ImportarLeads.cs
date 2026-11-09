using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    public partial class ClientesController
    {
        public const string MENSAJE_IMPORTAR_LEADS_SIN_PERMISO = "Solo Dirección, Administración, Vendedores o Informática pueden importar leads.";
        public const int TAMANO_MAXIMO_FICHERO_LEADS = 5 * 1024 * 1024;

        /// <summary>NestoAPI#623 corte 4: el SQL del importador (sustituible en los tests).</summary>
        internal IRepositorioLeadsMeta RepositorioLeads { get; set; } = new RepositorioLeadsMetaSql();
        /// <summary>NestoAPI#623 corte 4: el alta de clientes (sustituible en los tests); null = la de GestorClientes.</summary>
        internal IAltaClientesLeads AltaLeads { get; set; }

        internal static bool PuedeImportarLeads(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.VENDEDORES)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // POST api/Clientes/Origenes/ImportarLeads?origen=META-HARMONIA-0926&ensayo=true   (cuerpo: el fichero, multipart o tal cual)
        /// <summary>
        /// NestoAPI#623 corte 4: importa el fichero de leads que exporta Meta Ads (Excel XML o CSV). Cada lead se da de alta como
        /// cliente con el origen (FechaOrigen = cuando llegó el lead) o, si ya hay un cliente con su teléfono o su correo, se le pone
        /// el origen si no tenía ninguno. Con ensayo=true (por defecto) no escribe nada y dice qué haría. 400 si el origen no
        /// existe o no está activo o el fichero no se puede leer; 403 sin permiso.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/Origenes/ImportarLeads")]
        [ResponseType(typeof(ResultadoImportacionLeadsDTO))]
        public async Task<IHttpActionResult> PostImportarLeads(string origen, bool ensayo = true)
        {
            if (!PuedeImportarLeads(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_IMPORTAR_LEADS_SIN_PERMISO);
            }
            string codigo = string.IsNullOrWhiteSpace(origen) ? null : origen.Trim().ToUpperInvariant();
            if (codigo == null)
            {
                return BadRequest("Falta el origen.");
            }
            string errorOrigen = await ValidarOrigen(codigo).ConfigureAwait(false);
            if (errorOrigen != null)
            {
                return BadRequest(errorOrigen);
            }

            byte[] contenido = await LeerFicheroPeticion().ConfigureAwait(false);
            if (contenido == null || contenido.Length == 0)
            {
                return BadRequest("No ha llegado el fichero.");
            }
            if (contenido.Length > TAMANO_MAXIMO_FICHERO_LEADS)
            {
                return BadRequest("El fichero es demasiado grande (5 MB como mucho).");
            }

            List<LeadMeta> leads;
            try
            {
                leads = LectorLeadsMeta.Leer(contenido);
            }
            catch (FormatException ex)
            {
                return BadRequest(ex.Message);
            }

            var importador = new ImportadorLeadsMeta(RepositorioLeads, AltaLeads ?? new AltaClientesLeadsGestor(_gestorClientes),
                RegistrarErrorOrigen, RelojOrigenes);
            ResultadoImportacionLeadsDTO resultado = await importador.Importar(leads, codigo, ensayo, User?.Identity?.Name).ConfigureAwait(false);
            return Ok(resultado);
        }

        private async Task<byte[]> LeerFicheroPeticion()
        {
            if (Request?.Content == null)
            {
                return null;
            }
            if (Request.Content.IsMimeMultipartContent())
            {
                MultipartMemoryStreamProvider partes = await Request.Content.ReadAsMultipartAsync().ConfigureAwait(false);
                HttpContent fichero = partes.Contents.FirstOrDefault(c => !string.IsNullOrEmpty(c.Headers.ContentDisposition?.FileName))
                    ?? partes.Contents.FirstOrDefault();
                return fichero == null ? null : await fichero.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
            return await Request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    }
}
