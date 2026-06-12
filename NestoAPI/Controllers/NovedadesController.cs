using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Issue Nesto#372: changelog de novedades en lenguaje de usuario. Nesto lo consulta al
    /// arrancar tras una actualización y desde el menú Ayuda → Novedades. En el futuro lo
    /// podrán consumir también NestoApp y TiendasNuevaVision.
    /// </summary>
    public class NovedadesController : ApiController
    {
        private readonly IServicioNovedades servicio;

        public NovedadesController() : this(new ServicioNovedades()) { }

        public NovedadesController(IServicioNovedades servicio)
        {
            this.servicio = servicio;
        }

        // GET api/Novedades
        // GET api/Novedades?desdeVersion=1.10.5.3 (solo novedades de versiones POSTERIORES a la indicada)
        [ResponseType(typeof(List<NovedadDTO>))]
        public IHttpActionResult GetNovedades(string desdeVersion = null)
        {
            List<NovedadDTO> novedades = servicio.LeerNovedadesPublicadas();

            if (Version.TryParse(desdeVersion, out Version versionVista))
            {
                // Las entradas con versión no parseable no se filtran: mejor enseñarlas de más
                // que perder una novedad por un dato mal grabado.
                novedades = novedades
                    .Where(n => !Version.TryParse(n.Version, out Version v) || v > versionVista)
                    .ToList();
            }

            List<NovedadDTO> ordenadas = novedades
                .OrderByDescending(n => Version.TryParse(n.Version, out Version v) ? v : new Version(0, 0))
                .ThenBy(n => n.Id)
                .ToList();

            return Ok(ordenadas);
        }
    }
}
