using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Notificaciones
{
    /// <summary>
    /// NestoAPI#775 (Carlos 31/10/26): las notificaciones URGENTES de la campana de Nesto. La urgencia va POR TIPO y se decide aquí, en
    /// código (lista blanca), no por quien la manda. Una urgente, además de ir al buzón como todas:
    /// <list type="bullet">
    /// <item>avisa por SignalR con «hayNotificacionUrgente» (Nesto pide GET api/Notificaciones/Urgentes y abre la ventana roja con pitido);</item>
    /// <item>lleva en Datos la «referencia» de lo que hay que contestar (para la autorización de reembolso, su Id) y, al contestarse, se manda
    /// «notificacionResuelta» {Tipo, Referencia, Respondio, Resultado} para que se cierre en todos los Nesto;</item>
    /// <item>mientras siga pendiente vuelve a salir al abrir Nesto o al reconectar (sin sondeo): cada tipo dice qué referencias siguen
    /// pendientes (<see cref="IComprobadorUrgentesPendientes"/>).</item>
    /// </list>
    /// Hoy solo <see cref="TextosAutorizacionReembolso.TIPO_NOTIFICACION"/>. «FurgonetaSinDescargar» (#772) sigue siendo normal: no tiene nada
    /// que contestar ni un estado «pendiente» que cerrar.
    /// </summary>
    public static class NotificacionesUrgentes
    {
        public const string DATO_TIPO = "tipo";
        public const string DATO_REFERENCIA = "referencia";
        /// <summary>«1» en el aviso del escalado a Dirección (nadie de Administración contestó a tiempo).</summary>
        public const string DATO_ESCALADA = "escalada";

        /// <summary>Lo que se mira hacia atrás en el buzón: una urgente de ayer ya no es urgente.</summary>
        public static readonly TimeSpan ANTIGUEDAD_MAXIMA = TimeSpan.FromHours(24);

        private static readonly HashSet<string> TIPOS = new HashSet<string>(StringComparer.Ordinal)
        {
            TextosAutorizacionReembolso.TIPO_NOTIFICACION
        };

        public static IReadOnlyCollection<string> Tipos => TIPOS;

        public static bool EsUrgente(string tipo) => !string.IsNullOrWhiteSpace(tipo) && TIPOS.Contains(tipo.Trim());

        /// <summary>Por su Tipo o por Datos["tipo"] (las del buzón solo guardan los Datos).</summary>
        public static bool EsUrgente(NotificacionPushDTO notificacion)
            => notificacion != null && (EsUrgente(notificacion.Tipo) || EsUrgente(Dato(notificacion.Datos, DATO_TIPO)));

        public static string Dato(IDictionary<string, string> datos, string clave)
        {
            if (datos == null || clave == null)
            {
                return null;
            }
            foreach (KeyValuePair<string, string> par in datos)
            {
                if (string.Equals(par.Key, clave, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(par.Value) ? null : par.Value.Trim();
                }
            }
            return null;
        }
    }

    /// <summary>GET api/Notificaciones/Urgentes: una urgente del buzón del usuario que sigue pendiente.</summary>
    public class NotificacionUrgenteDTO
    {
        /// <summary>La fila del buzón (la más reciente de esa referencia), para marcarla leída.</summary>
        public int IdBuzon { get; set; }
        public string Tipo { get; set; }
        public string Referencia { get; set; }
        public string Titulo { get; set; }
        public string Cuerpo { get; set; }
        public Dictionary<string, string> Datos { get; set; }
        public DateTime FechaCreacion { get; set; }
        /// <summary>Le ha llegado por el escalado (nadie la contestó a tiempo).</summary>
        public bool Escalada { get; set; }
    }

    /// <summary>El evento «notificacionResuelta» de AvisosHub: se cierra la ventana urgente en todos los Nesto.</summary>
    public class NotificacionResueltaDTO
    {
        public string Tipo { get; set; }
        public string Referencia { get; set; }
        /// <summary>Quién la ha contestado, sin dominio (en una telefónica, lo que escribió el repartidor).</summary>
        public string Respondio { get; set; }
        /// <summary>Para la autorización de reembolso: «Autorizada», «Denegada» o «Telefonica».</summary>
        public string Resultado { get; set; }
    }

    /// <summary>NestoAPI#775: cada tipo urgente dice cuáles de sus referencias siguen esperando respuesta.</summary>
    public interface IComprobadorUrgentesPendientes
    {
        string Tipo { get; }
        Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias);
    }

    public interface IRepositorioUrgentesBuzon
    {
        /// <summary>Las notificaciones de Nesto del usuario (sin borrar) desde esa fecha.</summary>
        Task<List<NotificacionBuzonDTO>> Leer(string usuario, DateTime desde);
    }

    /// <summary>NestoAPI#775: las urgentes pendientes de un usuario (lo que Nesto vuelve a enseñar al abrir o al reconectar).</summary>
    public class ServicioNotificacionesUrgentes
    {
        private readonly IRepositorioUrgentesBuzon buzon;
        private readonly IReadOnlyList<IComprobadorUrgentesPendientes> comprobadores;
        private readonly Func<DateTime> ahora;

        public ServicioNotificacionesUrgentes(IRepositorioUrgentesBuzon buzon, IEnumerable<IComprobadorUrgentesPendientes> comprobadores,
            Func<DateTime> ahora = null)
        {
            this.buzon = buzon ?? throw new ArgumentNullException(nameof(buzon));
            this.comprobadores = (comprobadores ?? Enumerable.Empty<IComprobadorUrgentesPendientes>()).ToList();
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>El de producción.</summary>
        public static async Task<List<NotificacionUrgenteDTO>> PendientesDe(string usuario)
        {
            using (var db = new NVEntities())
            {
                var servicio = new ServicioNotificacionesUrgentes(new RepositorioUrgentesBuzon(db),
                    new IComprobadorUrgentesPendientes[] { new ComprobadorAutorizacionesPendientes(new RepositorioAutorizacionesReembolso(db)) });
                return await servicio.Pendientes(usuario).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Las urgentes del buzón del usuario (últimas <see cref="NotificacionesUrgentes.ANTIGUEDAD_MAXIMA"/>) cuya referencia sigue
        /// pendiente, una por referencia (la más reciente), de la más antigua a la más nueva. Un tipo sin comprobador no sale: sin saber si
        /// sigue pendiente, mejor no hacer sonar nada.
        /// </summary>
        public async Task<List<NotificacionUrgenteDTO>> Pendientes(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return new List<NotificacionUrgenteDTO>();
            }
            List<NotificacionBuzonDTO> filas = await buzon.Leer(usuario.Trim(), ahora() - NotificacionesUrgentes.ANTIGUEDAD_MAXIMA).ConfigureAwait(false)
                ?? new List<NotificacionBuzonDTO>();
            List<NotificacionUrgenteDTO> candidatas = filas
                .Where(f => f != null)
                .Select(f => new
                {
                    Fila = f,
                    Tipo = NotificacionesUrgentes.Dato(f.Datos, NotificacionesUrgentes.DATO_TIPO),
                    Referencia = NotificacionesUrgentes.Dato(f.Datos, NotificacionesUrgentes.DATO_REFERENCIA)
                })
                .Where(x => NotificacionesUrgentes.EsUrgente(x.Tipo) && x.Referencia != null)
                .GroupBy(x => new { x.Tipo, x.Referencia })
                .Select(g =>
                {
                    var ultima = g.OrderByDescending(x => x.Fila.FechaCreacion).ThenByDescending(x => x.Fila.Id).First();
                    return new NotificacionUrgenteDTO
                    {
                        IdBuzon = ultima.Fila.Id,
                        Tipo = g.Key.Tipo,
                        Referencia = g.Key.Referencia,
                        Titulo = ultima.Fila.Titulo,
                        Cuerpo = ultima.Fila.Cuerpo,
                        Datos = ultima.Fila.Datos,
                        FechaCreacion = ultima.Fila.FechaCreacion,
                        Escalada = g.Any(x => NotificacionesUrgentes.Dato(x.Fila.Datos, NotificacionesUrgentes.DATO_ESCALADA) == "1")
                    };
                })
                .ToList();

            var resultado = new List<NotificacionUrgenteDTO>();
            foreach (IGrouping<string, NotificacionUrgenteDTO> delTipo in candidatas.GroupBy(c => c.Tipo))
            {
                IComprobadorUrgentesPendientes comprobador = comprobadores.FirstOrDefault(c => c.Tipo == delTipo.Key);
                if (comprobador == null)
                {
                    continue;
                }
                ISet<string> pendientes = await comprobador.Pendientes(delTipo.Select(c => c.Referencia).ToList()).ConfigureAwait(false)
                    ?? new HashSet<string>();
                resultado.AddRange(delTipo.Where(c => pendientes.Contains(c.Referencia)));
            }
            return resultado.OrderBy(r => r.FechaCreacion).ThenBy(r => r.IdBuzon).ToList();
        }
    }

    /// <summary>NestoAPI#775: las autorizaciones de reembolso (#773) que siguen «Pendiente».</summary>
    public class ComprobadorAutorizacionesPendientes : IComprobadorUrgentesPendientes
    {
        private readonly IRepositorioAutorizacionesReembolso repositorio;

        public ComprobadorAutorizacionesPendientes(IRepositorioAutorizacionesReembolso repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public string Tipo => TextosAutorizacionReembolso.TIPO_NOTIFICACION;

        public async Task<ISet<string>> Pendientes(IReadOnlyCollection<string> referencias)
        {
            var pendientes = new HashSet<string>(StringComparer.Ordinal);
            if (referencias == null || referencias.Count == 0 || !repositorio.ScriptAplicado())
            {
                return pendientes;
            }
            foreach (string referencia in referencias.Distinct())
            {
                if (int.TryParse(referencia, out int id))
                {
                    AutorizacionReembolsoReparto autorizacion = await repositorio.Leer(id).ConfigureAwait(false);
                    if (autorizacion?.Estado?.Trim() == EstadosAutorizacionReembolso.PENDIENTE)
                    {
                        _ = pendientes.Add(referencia);
                    }
                }
            }
            return pendientes;
        }
    }

    public class RepositorioUrgentesBuzon : IRepositorioUrgentesBuzon
    {
        private readonly NVEntities db;

        public RepositorioUrgentesBuzon(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<NotificacionBuzonDTO>> Leer(string usuario, DateTime desde)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO;
            List<NotificacionBuzon> filas = await db.NotificacionesBuzon
                .Where(n => n.Usuario == usuario && n.Aplicacion == aplicacion && n.FechaEliminada == null && n.FechaCreacion >= desde && n.Datos != null)
                .ToListAsync()
                .ConfigureAwait(false);
            return filas.Select(f => new NotificacionBuzonDTO
            {
                Id = f.Id,
                Titulo = f.Titulo,
                Cuerpo = f.Cuerpo,
                Datos = LeerDatos(f.Datos),
                FechaCreacion = f.FechaCreacion,
                Leida = f.FechaLeida != null
            }).ToList();
        }

        private static Dictionary<string, string> LeerDatos(string json)
        {
            try
            {
                return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }
    }
}
