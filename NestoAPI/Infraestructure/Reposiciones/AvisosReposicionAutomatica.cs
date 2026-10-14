using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>NestoAPI#622: avisar a la gente de un almacén de lo que ha hecho el job con su reposición (campana de Nesto).</summary>
    public interface IAvisadorReposicionAutomatica
    {
        Task Avisar(string empresa, string origen, string titulo, string cuerpo);
    }

    /// <summary>
    /// NestoAPI#622: el aviso del job de las reposiciones automáticas va al buzón de Nesto (la campana, por SignalR) de la gente
    /// del almacén de ORIGEN, que es quien la envía. Quién es de un almacén: el mismo criterio que da permiso para preparar y
    /// terminar su reposición (<see cref="ServicioPreparacionReposicion.PuedeEscribir"/>), quien lo tiene como AlmacénPedidoVta
    /// en esa empresa (ParámetrosUsuario; sin la fila «(defecto)»). En Algete no vale: es el almacén por defecto de todo el
    /// mundo, así que allí se avisa al grupo Almacén del dominio, que es quien prepara las suyas. Mismo camino que
    /// <see cref="PreparacionAlmacen.AvisadorReposiciones"/>.
    /// </summary>
    public class AvisadorReposicionAutomatica : IAvisadorReposicionAutomatica
    {
        public const string TIPO_NOTIFICACION = "ReposicionAutomatica";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, string, Task<List<string>>> usuariosDelAlmacen;
        private readonly Func<List<string>> grupoAlmacen;

        public AvisadorReposicionAutomatica(NVEntities db)
            : this(new ServicioNotificacionesPush(), (empresa, almacen) => UsuariosDelAlmacen(db, empresa, almacen),
                  () => MiembrosGrupoDominio.Leer(Constantes.GruposSeguridad.ALMACEN))
        {
        }

        /// <param name="usuariosDelAlmacen">(empresa, almacén) → los usuarios con ese AlmacénPedidoVta.</param>
        /// <param name="grupoAlmacen">Los miembros del grupo Almacén (para Algete).</param>
        internal AvisadorReposicionAutomatica(IServicioNotificacionesPush notificaciones, Func<string, string, Task<List<string>>> usuariosDelAlmacen,
            Func<List<string>> grupoAlmacen)
        {
            this.notificaciones = notificaciones ?? throw new ArgumentNullException(nameof(notificaciones));
            this.usuariosDelAlmacen = usuariosDelAlmacen ?? throw new ArgumentNullException(nameof(usuariosDelAlmacen));
            this.grupoAlmacen = grupoAlmacen ?? (() => new List<string>());
        }

        public async Task Avisar(string empresa, string origen, string titulo, string cuerpo)
        {
            string almacen = origen?.Trim().ToUpperInvariant();
            List<string> deAlmacen = almacen == Constantes.Almacenes.ALGETE
                ? grupoAlmacen()
                : await usuariosDelAlmacen(empresa?.Trim(), almacen).ConfigureAwait(false);
            List<string> usuarios = (deAlmacen ?? new List<string>())
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Reposición automática #622] No hay a quién avisar en {almacen}: {titulo}. {cuerpo}"));
                return;
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION, ["origen"] = almacen }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
        }

        /// <summary>Quien tiene <paramref name="almacen"/> como AlmacénPedidoVta en <paramref name="empresa"/> (sin la fila «(defecto)»).</summary>
        internal static async Task<List<string>> UsuariosDelAlmacen(NVEntities db, string empresa, string almacen)
        {
            string clave = ServicioPreparacionReposicion.CLAVE_ALMACEN_USUARIO;
            List<string> usuarios = await db.ParametrosUsuario.AsNoTracking()
                .Where(p => p.Empresa == empresa && p.Clave == clave && p.Valor == almacen && !p.Usuario.StartsWith("("))
                .Select(p => p.Usuario)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return usuarios.Select(u => u?.Trim()).Where(u => !string.IsNullOrEmpty(u)).ToList();
        }
    }
}
