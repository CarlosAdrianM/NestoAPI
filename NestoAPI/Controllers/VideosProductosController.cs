using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    [RoutePrefix("api/VideosProductos")]
    public class VideosProductosController : ApiController
    {
        #region DTOs

        public class ActualizacionVideoProductoDto
        {
            public string Referencia { get; set; }
            public string EnlaceTienda { get; set; }
            public string TiempoAparicion { get; set; }
            public string Observaciones { get; set; }
        }

        public class HistorialCambioDto
        {
            public string CampoModificado { get; set; }
            public string ValorAnterior { get; set; }
            public string ValorNuevo { get; set; }
            public string Usuario { get; set; }
            public string Accion { get; set; }
            public DateTime FechaCambio { get; set; }
            public string Observaciones { get; set; }
        }

        #endregion

        #region PUT: Actualizar VideosProducto

        /// <summary>
        /// Actualiza un VideosProducto (solo empleados). 
        /// Registra cambios en LogVideosProductos.
        /// </summary>
        [HttpPut]
        [Route("{id}")]
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> Actualizar(int id, [FromBody] ActualizacionVideoProductoDto dto)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }

            var identity = User.Identity as ClaimsIdentity;
            var isEmployeeClaim = identity?.FindFirst("IsEmployee");
            if (isEmployeeClaim == null || !bool.TryParse(isEmployeeClaim.Value, out bool isEmployee) || !isEmployee)
            {
                return Content(System.Net.HttpStatusCode.Forbidden, "Acceso denegado. Requiere ser empleado.");
            }

            var usuario = identity.FindFirst(ClaimTypes.Name)?.Value ?? "Desconocido";

            using (var db = new NVEntities())
            {
                var vp = await db.VideosProductos.FindAsync(id);
                if (vp == null)
                {
                    return NotFound();
                }

                var cambios = new List<(string Campo, string ValorAnterior, string ValorNuevo)>();

                // Referencia
                if (dto.Referencia != null && dto.Referencia != vp.Referencia)
                {
                    cambios.Add(("Referencia", vp.Referencia, dto.Referencia));
                    vp.Referencia = dto.Referencia;
                }

                // EnlaceTienda
                if (dto.EnlaceTienda != null && dto.EnlaceTienda != vp.EnlaceTienda)
                {
                    cambios.Add(("EnlaceTienda", vp.EnlaceTienda, dto.EnlaceTienda));
                    vp.EnlaceTienda = dto.EnlaceTienda;
                }

                // TiempoAparicion
                if (dto.TiempoAparicion != null && dto.TiempoAparicion != vp.TiempoAparicion)
                {
                    cambios.Add(("TiempoAparicion", vp.TiempoAparicion, dto.TiempoAparicion));
                    vp.TiempoAparicion = dto.TiempoAparicion;

                    // Regenerar EnlaceVideo si tiene formato YouTube con ?t=
                    if (!string.IsNullOrEmpty(vp.EnlaceVideo))
                    {
                        try
                        {
                            var uri = new Uri(vp.EnlaceVideo);
                            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                            query["t"] = dto.TiempoAparicion;
                            var builder = new UriBuilder(uri)
                            {
                                Query = query.ToString()
                            };
                            vp.EnlaceVideo = builder.ToString();
                            cambios.Add(("EnlaceVideo", vp.EnlaceVideo, vp.EnlaceVideo));
                        }
                        catch { /* Si el enlace no es v�lido, no hacemos nada */ }
                    }
                }

                if (!cambios.Any())
                {
                    return Ok();
                }

                // Registrar cada cambio
                foreach (var (Campo, ValorAnterior, ValorNuevo) in cambios)
                {
                    _ = db.LogVideosProductos.Add(new LogVideoProducto
                    {
                        VideoProductoId = vp.Id,
                        CampoModificado = Campo,
                        ValorAnterior = ValorAnterior,
                        ValorNuevo = ValorNuevo,
                        Usuario = usuario,
                        Accion = "Actualizacion",
                        Observaciones = dto.Observaciones,
                        FechaCambio = DateTime.UtcNow
                    });
                }

                try
                {
                    _ = await db.SaveChangesAsync();
                    return Ok();
                }
                catch (System.Data.Entity.Infrastructure.DbUpdateException ex)
                {
                    // Loguear en ELMAH
                    Elmah.ErrorSignal.FromCurrentContext().Raise(ex);

                    // Extraer mensaje de error interno (ej: permisos SQL)
                    var innerMessage = ex.InnerException?.InnerException?.Message ?? ex.InnerException?.Message ?? ex.Message;
                    return Content(System.Net.HttpStatusCode.InternalServerError,
                        $"Error al guardar los cambios: {innerMessage}");
                }
            }
        }

        #endregion

        #region DELETE: Eliminar VideosProducto

        /// <summary>
        /// Elimina un VideosProducto (solo empleados). 
        /// Registra la eliminaci�n en el log.
        /// </summary>
        [HttpDelete]
        [Route("{id}")]
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> Eliminar(int id, [FromUri] string observaciones = null)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }

            var identity = User.Identity as ClaimsIdentity;
            var isEmployeeClaim = identity?.FindFirst("IsEmployee");
            if (isEmployeeClaim == null || !bool.TryParse(isEmployeeClaim.Value, out bool isEmployee) || !isEmployee)
            {
                return Content(System.Net.HttpStatusCode.Forbidden, "Acceso denegado. Requiere ser empleado.");
            }

            var usuario = identity.FindFirst(ClaimTypes.Name)?.Value ?? "Desconocido";

            using (var db = new NVEntities())
            {
                var vp = await db.VideosProductos.FindAsync(id);
                if (vp == null)
                {
                    return Ok(); // Idempotencia: ya no existe
                }

                // Registrar eliminaci�n (antes de borrar)
                _ = db.LogVideosProductos.Add(new LogVideoProducto
                {
                    VideoProductoId = vp.Id,
                    CampoModificado = "Todos los campos",
                    ValorAnterior = $"Nombre: {vp.NombreProducto}, Referencia: {vp.Referencia}, Tiempo: {vp.TiempoAparicion}, Enlace: {vp.EnlaceTienda}",
                    ValorNuevo = null,
                    Usuario = usuario,
                    Accion = "Eliminacion",
                    Observaciones = observaciones,
                    FechaCambio = DateTime.UtcNow
                });

                _ = db.VideosProductos.Remove(vp);
                _ = await db.SaveChangesAsync();
                return Ok();
            }
        }

        #endregion

        #region GET: Historial de cambios

        /// <summary>
        /// Obtiene el historial de cambios de un VideosProducto
        /// </summary>
        [HttpGet]
        [Route("{id}/historial")]
        [ResponseType(typeof(List<HistorialCambioDto>))]
        public async Task<IHttpActionResult> GetHistorial(int id)
        {
            using (var db = new NVEntities())
            {
                var historial = await db.LogVideosProductos
                    .Where(l => l.VideoProductoId == id)
                    .OrderByDescending(l => l.FechaCambio)
                    .Select(l => new HistorialCambioDto
                    {
                        CampoModificado = l.CampoModificado,
                        ValorAnterior = l.ValorAnterior,
                        ValorNuevo = l.ValorNuevo,
                        Usuario = l.Usuario,
                        Accion = l.Accion,
                        FechaCambio = l.FechaCambio,
                        Observaciones = l.Observaciones
                    })
                    .ToListAsync();

                return Ok(historial);
            }
        }

        #endregion


        #region POST: Deshacer un cambio espec�fico

        /// <summary>
        /// Deshace un cambio espec�fico a partir de un registro de log (solo empleados)
        /// </summary>
        [HttpPost]
        [Route("{id}/deshacer/{logId}")]
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> DeshacerCambio(int id, int logId, [FromUri] string observaciones = null)
        {
            if (!User.Identity.IsAuthenticated)
            {
                return Unauthorized();
            }

            var identity = User.Identity as ClaimsIdentity;
            var isEmployeeClaim = identity?.FindFirst("IsEmployee");
            if (isEmployeeClaim == null ||
                !bool.TryParse(isEmployeeClaim.Value, out bool isEmployee) ||
                !isEmployee)
            {
                return Content(System.Net.HttpStatusCode.Forbidden, "Acceso denegado. Requiere ser empleado.");
            }

            var usuario = identity.FindFirst(ClaimTypes.Name)?.Value ?? "Desconocido";

            using (var db = new NVEntities())
            {
                // Verificar que el VideosProducto existe
                var videoProducto = await db.VideosProductos.FindAsync(id);
                if (videoProducto == null)
                {
                    return NotFound();
                }

                // Obtener el registro de log a deshacer
                var registroLog = await db.LogVideosProductos.FindAsync(logId);
                if (registroLog == null || registroLog.VideoProductoId != id)
                {
                    return BadRequest("No se encontr� el registro de log especificado.");
                }

                // No permitimos deshacer un deshacer (para evitar bucles)
                if (registroLog.Observaciones?.Contains("Deshacer") == true)
                {
                    return Content(System.Net.HttpStatusCode.BadRequest, "No se puede deshacer un cambio que ya fue deshecho.");
                }

                // Aplicar el deshacer: restaurar ValorAnterior
                bool cambioAplicado = false;

                switch (registroLog.CampoModificado)
                {
                    case "Referencia":
                        if (videoProducto.Referencia != registroLog.ValorAnterior)
                        {
                            var valorNuevo = videoProducto.Referencia;
                            videoProducto.Referencia = registroLog.ValorAnterior;

                            _ = db.LogVideosProductos.Add(new LogVideoProducto
                            {
                                VideoProductoId = videoProducto.Id,
                                CampoModificado = "Referencia",
                                ValorAnterior = valorNuevo,
                                ValorNuevo = registroLog.ValorAnterior,
                                Usuario = usuario,
                                Accion = "Actualizacion",
                                Observaciones = $"Deshacer cambio {logId}. {observaciones}",
                                FechaCambio = DateTime.UtcNow
                            });
                            cambioAplicado = true;
                        }
                        break;

                    case "EnlaceTienda":
                        if (videoProducto.EnlaceTienda != registroLog.ValorAnterior)
                        {
                            var valorNuevo = videoProducto.EnlaceTienda;
                            videoProducto.EnlaceTienda = registroLog.ValorAnterior;

                            _ = db.LogVideosProductos.Add(new LogVideoProducto
                            {
                                VideoProductoId = videoProducto.Id,
                                CampoModificado = "EnlaceTienda",
                                ValorAnterior = valorNuevo,
                                ValorNuevo = registroLog.ValorAnterior,
                                Usuario = usuario,
                                Accion = "Actualizacion",
                                Observaciones = $"Deshacer cambio {logId}. {observaciones}",
                                FechaCambio = DateTime.UtcNow
                            });
                            cambioAplicado = true;
                        }
                        break;

                    case "TiempoAparicion":
                    case "EnlaceVideo":
                        if (videoProducto.TiempoAparicion != registroLog.ValorAnterior)
                        {
                            var valorNuevo = videoProducto.TiempoAparicion;
                            videoProducto.TiempoAparicion = registroLog.ValorAnterior;

                            // Regenerar EnlaceVideo
                            if (!string.IsNullOrEmpty(videoProducto.EnlaceVideo))
                            {
                                try
                                {
                                    var uri = new Uri(videoProducto.EnlaceVideo);
                                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                                    query["t"] = registroLog.ValorAnterior;
                                    var builder = new UriBuilder(uri) { Query = query.ToString() };
                                    videoProducto.EnlaceVideo = builder.ToString();
                                }
                                catch { /* Si falla, dejamos el tiempo pero no el enlace */ }
                            }

                            _ = db.LogVideosProductos.Add(new LogVideoProducto
                            {
                                VideoProductoId = videoProducto.Id,
                                CampoModificado = "TiempoAparicion",
                                ValorAnterior = valorNuevo,
                                ValorNuevo = registroLog.ValorAnterior,
                                Usuario = usuario,
                                Accion = "Actualizacion",
                                Observaciones = $"Deshacer cambio {logId}. {observaciones}",
                                FechaCambio = DateTime.UtcNow
                            });
                            cambioAplicado = true;
                        }
                        break;

                    default:
                        return BadRequest($"No se puede deshacer el campo: {registroLog.CampoModificado}");
                }

                if (!cambioAplicado)
                {
                    // Igualmente registramos que se intent� deshacer
                    _ = db.LogVideosProductos.Add(new LogVideoProducto
                    {
                        VideoProductoId = videoProducto.Id,
                        CampoModificado = "Deshacer",
                        ValorAnterior = null,
                        ValorNuevo = $"Intento de deshacer cambio {logId}, pero ya estaba revertido.",
                        Usuario = usuario,
                        Accion = "Actualizacion",
                        Observaciones = observaciones,
                        FechaCambio = DateTime.UtcNow
                    });
                }

                _ = await db.SaveChangesAsync();
                return Ok();
            }
        }

        #endregion
    }
}