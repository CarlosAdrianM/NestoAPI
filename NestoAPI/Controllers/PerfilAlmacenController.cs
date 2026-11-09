using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Ariadna para tiendas (Carlos 09/10/26): GET api/Almacen/Perfil dice a la app en qué almacén trabaja quien ha entrado
    /// y si es una tienda. Va aparte de AlmacenController para no tocar su constructor (inyección de Startup).
    /// </summary>
    [Authorize]
    [RoutePrefix("api/Almacen")]
    public class PerfilAlmacenController : ApiController
    {
        private const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";

        private readonly Func<string, string, string> almacenDelUsuario;
        private readonly Func<string, string, Task<string>> nombreAlmacen;

        // AddControllersAsServices elige el constructor público que puede construir entero: este.
        public PerfilAlmacenController() : this(
            (empresa, usuario) => ParametrosUsuarioController.LeerParametro(empresa, usuario, CLAVE_ALMACEN_USUARIO),
            LeerNombreAlmacen)
        {
        }

        /// <param name="almacenDelUsuario">(empresa, usuario sin dominio) → su AlmacénPedidoVta.</param>
        /// <param name="nombreAlmacen">(empresa, almacén) → su nombre («Reina»).</param>
        internal PerfilAlmacenController(Func<string, string, string> almacenDelUsuario, Func<string, string, Task<string>> nombreAlmacen)
        {
            this.almacenDelUsuario = almacenDelUsuario ?? throw new ArgumentNullException(nameof(almacenDelUsuario));
            this.nombreAlmacen = nombreAlmacen ?? ((e, a) => Task.FromResult<string>(null));
        }

        // GET api/Almacen/Perfil?empresa=1
        /// <summary>
        /// El almacén del usuario (AlmacénPedidoVta; ALG si no tiene) y su perfil en Ariadna: «Tienda» en Reina y Alcobendas
        /// (solo Salidas y Entradas, con las reposiciones de su tienda), «Almacen» en el resto.
        /// </summary>
        [HttpGet]
        [Route("Perfil")]
        [ResponseType(typeof(PerfilAlmacenDTO))]
        public async Task<IHttpActionResult> GetPerfil(string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string usuario = ReglasPerfilAlmacen.SinDominio(User?.Identity?.Name);
            string almacen = string.IsNullOrWhiteSpace(usuario) ? null : almacenDelUsuario(empresa, usuario);
            PerfilAlmacenDTO perfil = ReglasPerfilAlmacen.Deducir(usuario, almacen);
            perfil.NombreAlmacen = await nombreAlmacen(empresa, perfil.Almacen).ConfigureAwait(false);
            return Ok(perfil);
        }

        // GET api/Almacen/VersionesAriadna
        /// <summary>
        /// NestoAPI#844: la última versión de Ariadna con la que ha llamado cada usuario (X-Ariadna-Version, desde el último arranque
        /// de la API), para distinguir «no ha actualizado» de «no lo ha hecho». Dirección, Informática, Administración y Almacén.
        /// </summary>
        [HttpGet]
        [Route("VersionesAriadna")]
        [ResponseType(typeof(System.Collections.Generic.List<Infraestructure.Ariadna.VersionAriadnaUsuarioDTO>))]
        public IHttpActionResult GetVersionesAriadna()
        {
            if (User == null || !(User.IsInRole(Constantes.GruposSeguridad.DIRECCION) || User.IsInRole(Constantes.GruposSeguridad.INFORMATICA)
                || User.IsInRole(Constantes.GruposSeguridad.ADMINISTRACION) || User.IsInRole(Constantes.GruposSeguridad.ALMACEN)))
            {
                return Content(System.Net.HttpStatusCode.Forbidden, new { Message = "Su usuario no puede ver las versiones de Ariadna." });
            }
            return Ok(Infraestructure.Ariadna.VersionesAriadna.Todas());
        }

        private static async Task<string> LeerNombreAlmacen(string empresa, string almacen)
        {
            using (var db = new NVEntities())
            {
                return await RepositorioPreparacionAlmacen.LeerNombreAlmacen(db.Database, empresa, almacen).ConfigureAwait(false);
            }
        }
    }
}
