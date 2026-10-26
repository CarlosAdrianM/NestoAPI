using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Avisar de las diferencias de una reposición (buzón y campana de Nesto). Nunca debe romper la entrada.</summary>
    public interface IAvisadorReposiciones
    {
        /// <param name="destinatario">Quien creó el traspaso (con o sin dominio), o null si no se sabe. NestoAPI#634: se avisa además
        /// siempre al grupo Almacén, al buzón de Nesto y al de Ariadna.</param>
        /// <param name="almacenOrigen">Sugerencia 781: si el origen es una tienda, también a su gente, que es quien comprueba la diferencia.</param>
        Task Avisar(string destinatario, string titulo, IEnumerable<string> lineas, string empresa, string almacenOrigen);
    }

    /// <summary>
    /// NestoAPI#553 (Carlos, 04/10/26): cuando lo leído al recibir una reposición no coincide con lo enviado, entra lo leído
    /// y se informa a quien creó el traspaso (o, si no se sabe, al grupo Almacén), al buzón de Nesto: el mismo camino que
    /// los avisos a Compras de las recepciones (<see cref="AvisadorCompras"/>).
    /// </summary>
    public class AvisadorReposiciones : IAvisadorReposiciones
    {
        public const string TIPO_NOTIFICACION = "RecepcionReposicion";
        /// <summary>NestoAPI#634: Datos["vista"] del aviso, para que Nesto y Ariadna abran la lista de diferencias de reposición.</summary>
        public const string VISTA_DIFERENCIAS = "DiferenciasReposicion";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<List<string>> usuariosDeAlmacen;
        private readonly Func<string, string, Task<List<string>>> usuariosDeLaTienda;

        public AvisadorReposiciones(IServicioNotificacionesPush notificaciones) : this(notificaciones, null)
        {
        }

        /// <param name="usuariosDeLaTienda">(empresa, almacén) → la gente de una tienda. Por defecto, el mismo criterio que el aviso
        /// de las reposiciones automáticas (<see cref="Reposiciones.AvisadorReposicionAutomatica.UsuariosDelAlmacen"/>).</param>
        internal AvisadorReposiciones(IServicioNotificacionesPush notificaciones, Func<List<string>> usuariosDeAlmacen,
            Func<string, string, Task<List<string>>> usuariosDeLaTienda = null)
        {
            this.notificaciones = notificaciones;
            this.usuariosDeAlmacen = usuariosDeAlmacen ?? (() => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN));
            this.usuariosDeLaTienda = usuariosDeLaTienda ?? LeerUsuariosDeLaTienda;
        }

        private static async Task<List<string>> LeerUsuariosDeLaTienda(string empresa, string almacen)
        {
            using (var db = new NVEntities())
            {
                return await Reposiciones.AvisadorReposicionAutomatica.UsuariosDelAlmacen(db, empresa, almacen).ConfigureAwait(false);
            }
        }

        public async Task Avisar(string destinatario, string titulo, IEnumerable<string> lineas, string empresa, string almacenOrigen)
        {
            List<string> lista = (lineas ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (!lista.Any())
            {
                return;
            }
            // NestoAPI#634 (Carlos, 14/10/26): a quien la preparó Y al grupo Almacén, que es quien comprueba la diferencia en Algete
            var candidatos = new List<string>();
            if (!string.IsNullOrWhiteSpace(destinatario))
            {
                candidatos.Add(destinatario);
            }
            candidatos.AddRange(usuariosDeAlmacen() ?? new List<string>());
            // Sugerencia 781 (Paloma): en una reposición tienda → Algete la diferencia la comprueba la tienda, y su preparador suele
            // ser «ReposicionAutomatica», que no es nadie: sin esto, a la tienda no le llegaba ningún aviso.
            string origen = almacenOrigen?.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(origen) && origen != Constantes.Almacenes.ALGETE && !string.IsNullOrWhiteSpace(empresa))
            {
                candidatos.AddRange(await usuariosDeLaTienda(empresa.Trim(), origen).ConfigureAwait(false) ?? new List<string>());
            }
            List<string> usuarios = candidatos
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Recepción reposiciones] No hay a quién avisar: {titulo}. {string.Join(" ", lista)}"));
                return;
            }

            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = string.Join(Environment.NewLine, lista),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION, ["vista"] = VISTA_DIFERENCIAS }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                // Ariadna#25: también al buzón de la PDA, donde el usuario va sin dominio
                await notificaciones.GuardarEnBuzonDeUsuario(usuario.Substring(usuario.LastIndexOf('\\') + 1), Constantes.Aplicaciones.ARIADNA, notificacion)
                    .ConfigureAwait(false);
            }
        }
    }
}
