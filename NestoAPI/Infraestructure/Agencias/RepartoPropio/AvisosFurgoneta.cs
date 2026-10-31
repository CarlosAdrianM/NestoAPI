using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#772: la campana de Nesto cuando una furgoneta se queda con bultos al acabar el día.</summary>
    public interface IAvisosFurgoneta
    {
        /// <param name="repartidor">El usuario del repartidor.</param>
        /// <param name="pendientes">Lo que lleva encima.</param>
        /// <param name="alTerminar">True: lo ha confirmado él al terminar la jornada. False: lo ve el job de las 22:00.</param>
        /// <returns>A quién se ha avisado («Almacén», «Dirección», «Andre»); vacía si a nadie.</returns>
        Task<List<string>> FurgonetaSinDescargar(string repartidor, IReadOnlyList<MovimientoBultoReparto> pendientes, bool alTerminar);
    }

    /// <summary>
    /// NestoAPI#772 (Carlos 31/10/26): aviso en la campana de Nesto (buzón + SignalR, tipo <see cref="TIPO_NOTIFICACION"/>) a los
    /// miembros de los grupos Almacén y Dirección del dominio (<see cref="MiembrosGrupoDominio"/>, como el aviso de Verifactu) y al
    /// propio repartidor. Cada usuario, una vez aunque esté en los dos grupos.
    /// </summary>
    public class AvisosFurgonetaSinDescargar : IAvisosFurgoneta
    {
        public const string TIPO_NOTIFICACION = "FurgonetaSinDescargar";
        internal const string DOMINIO = "NUEVAVISION\\";
        internal static readonly string[] GRUPOS = { Constantes.GruposSeguridad.ALMACEN, Constantes.GruposSeguridad.DIRECCION };

        private readonly Func<IServicioNotificacionesPush> notificaciones;
        private readonly Func<string, List<string>> miembros;

        public AvisosFurgonetaSinDescargar() : this(() => new ServicioNotificacionesPush(), MiembrosGrupoDominio.Leer)
        {
        }

        internal AvisosFurgonetaSinDescargar(Func<IServicioNotificacionesPush> notificaciones, Func<string, List<string>> miembros)
        {
            this.notificaciones = notificaciones;
            this.miembros = miembros;
        }

        public async Task<List<string>> FurgonetaSinDescargar(string repartidor, IReadOnlyList<MovimientoBultoReparto> pendientes, bool alTerminar)
        {
            var avisados = new List<string>();
            if (string.IsNullOrWhiteSpace(repartidor) || pendientes == null || pendientes.Count == 0)
            {
                return avisados;
            }
            string nombre = ServicioRepartoPropio.NombreRepartidor(repartidor);
            NotificacionPushDTO notificacion = Componer(nombre, pendientes, alTerminar);
            var usuarios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string grupo in GRUPOS)
            {
                List<string> deGrupo;
                try
                {
                    deGrupo = miembros(grupo) ?? new List<string>();
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudieron leer los miembros de {grupo}: {ex.Message}", ex));
                    deGrupo = new List<string>();
                }
                if (deGrupo.Count > 0)
                {
                    avisados.Add(grupo);
                }
                foreach (string usuario in deGrupo)
                {
                    _ = usuarios.Add(ConDominio(usuario));
                }
            }
            _ = usuarios.Add(ConDominio(repartidor));
            avisados.Add(nombre);
            IServicioNotificacionesPush servicio = notificaciones();
            foreach (string usuario in usuarios)
            {
                try
                {
                    await servicio.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo avisar a {usuario} de la furgoneta de {nombre}: {ex.Message}", ex));
                }
            }
            return avisados;
        }

        internal static string ConDominio(string usuario)
        {
            string limpio = usuario?.Trim() ?? string.Empty;
            return limpio.Contains("\\") ? limpio : DOMINIO + limpio;
        }

        internal static NotificacionPushDTO Componer(string repartidor, IReadOnlyList<MovimientoBultoReparto> pendientes, bool alTerminar)
        {
            string bultos = ServicioRepartoPropio.TextoBultos(pendientes.Count);
            string cuerpo = (alTerminar
                    ? $"{repartidor} ha terminado la jornada con {bultos} en la furgoneta"
                    : $"{repartidor} tiene {bultos} en la furgoneta y no ha terminado la jornada")
                + $": {NucleoFurgoneta.ListaCorta(pendientes.Select(p => p.Codigo))}. Compruebe dónde están y descárguelos.";
            return new NotificacionPushDTO
            {
                Titulo = $"Furgoneta sin descargar: {repartidor}",
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["repartidor"] = repartidor,
                    ["bultos"] = pendientes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }
    }

    /// <summary>
    /// NestoAPI#772: red de seguridad de las 22:00 (L-V). Cada repartidor con bultos encima y SIN jornada terminada hoy recibe el
    /// mismo aviso que al terminar con bultos (Almacén, Dirección y él). Sin el script, nada.
    /// </summary>
    public class FurgonetaSinDescargarJobsService
    {
        public const string JOB_ID = "furgoneta-sin-descargar";
        public const string CRON = "0 22 * * 1-5"; // de lunes a viernes a las 22:00

        private readonly IRepositorioFurgoneta repositorio;
        private readonly IAvisosFurgoneta avisos;
        private readonly Func<DateTime> ahora;

        internal FurgonetaSinDescargarJobsService(IRepositorioFurgoneta repositorio, IAvisosFurgoneta avisos, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio;
            this.avisos = avisos;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Avisar()
        {
            using (var db = new NVEntities())
            {
                _ = await new FurgonetaSinDescargarJobsService(new RepositorioFurgoneta(db), new AvisosFurgonetaSinDescargar()).AvisarSinDescargar().ConfigureAwait(false);
            }
        }

        /// <summary>A cuántos repartidores se ha avisado.</summary>
        public async Task<int> AvisarSinDescargar()
        {
            try
            {
                if (repositorio == null || !repositorio.ScriptAplicado())
                {
                    return 0;
                }
                DateTime hoy = ahora().Date;
                List<MovimientoBultoReparto> encima = NucleoFurgoneta.EnFurgoneta(
                    await repositorio.LeerUltimos(hoy.AddDays(-ServicioRepartoPropio.DIAS_FURGONETA)).ConfigureAwait(false));
                List<JornadaReparto> jornadas = await repositorio.LeerJornadas(hoy).ConfigureAwait(false) ?? new List<JornadaReparto>();
                int avisados = 0;
                foreach (IGrouping<string, MovimientoBultoReparto> furgoneta in encima.GroupBy(m => ServicioRepartoPropio.NombreRepartidor(m.Usuario), StringComparer.OrdinalIgnoreCase))
                {
                    if (jornadas.Any(j => ServicioRepartoPropio.MismoUsuario(j.Usuario, furgoneta.Key)))
                    {
                        continue; // ya terminó (y, si fue con bultos, ya avisó él)
                    }
                    List<MovimientoBultoReparto> suyos = furgoneta.ToList();
                    _ = await avisos.FurgonetaSinDescargar(suyos[0].Usuario, suyos, false).ConfigureAwait(false);
                    avisados++;
                }
                return avisados;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] El aviso de las furgonetas sin descargar ha fallado: {ex.Message}", ex));
                return 0;
            }
        }
    }

    /// <summary>
    /// NestoAPI#772: red de seguridad. Si el ALMACÉN lee un bulto que sigue en una furgoneta (la etiqueta R al recibir la REPO, la
    /// NR al hacer el abono de la recogida), cuenta como descargado (Origen = Almacen) si el repartidor no lo había descargado. En
    /// una tienda, al recibir, cuenta como entregado. Nadie escanea dos veces. Nunca lanza.
    /// </summary>
    public static class RedSeguridadFurgoneta
    {
        /// <param name="codigos">Lo leído (R{traspaso}-{N}, NR{pedido}-{N} o NR{pedido}: todas las de esa nota).</param>
        /// <param name="enAlgete">True: vuelve a Algete (Descargado). False: llega a una tienda (Entregado).</param>
        /// <returns>Cuántos movimientos ha apuntado.</returns>
        public static async Task<int> Leidos(IRepositorioFurgoneta repositorio, IEnumerable<string> codigos, string usuario, bool enAlgete, DateTime ahora)
        {
            try
            {
                if (repositorio == null || !repositorio.ScriptAplicado())
                {
                    return 0;
                }
                List<CodigoBultoFurgoneta> leidos = (codigos ?? Enumerable.Empty<string>()).Select(CodigoBultoFurgoneta.Leer).Where(c => c != null).ToList();
                if (leidos.Count == 0)
                {
                    return 0;
                }
                List<int> notasEnteras = leidos.Where(c => c.TipoCodigo == CodigoBultoFurgoneta.NR && c.SinBulto).Select(c => c.Pedido.Value).Distinct().ToList();
                List<MovimientoBultoReparto> historia = await repositorio.LeerHistoria(new ConsultaMovimientosBulto
                {
                    Codigos = leidos.Where(c => !c.SinBulto).Select(c => c.Codigo).Distinct().ToList(),
                    Pedidos = notasEnteras
                }).ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
                var codigosLeidos = new HashSet<string>(leidos.Where(c => !c.SinBulto).Select(c => c.Codigo), StringComparer.OrdinalIgnoreCase);
                string tipo = enAlgete ? TiposMovimientoBulto.DESCARGADO : TiposMovimientoBulto.ENTREGADO;
                List<MovimientoBultoReparto> nuevos = NucleoFurgoneta.Ultimos(historia)
                    .Where(u => codigosLeidos.Contains(u.Codigo)
                        || (u.TipoCodigo == CodigoBultoFurgoneta.NR && u.Pedido.HasValue && notasEnteras.Contains(u.Pedido.Value)))
                    .Where(u => TiposMovimientoBulto.DejaEnFurgoneta(u.Tipo))
                    // La clave sale del último movimiento: leerlo otra vez en el almacén no lo apunta dos veces.
                    .Select(u => ServicioRepartoPropio.Copia(u, tipo, string.IsNullOrWhiteSpace(usuario) ? "Almacén" : usuario.Trim(), ahora, ahora,
                        OrigenesMovimientoBulto.ALMACEN, NucleoFurgoneta.ClaveDerivada(u.ClaveIdempotencia, u.Codigo, tipo + "|" + OrigenesMovimientoBulto.ALMACEN)))
                    .ToList();
                return nuevos.Count == 0 ? 0 : await repositorio.Registrar(nuevos).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo apuntar lo leído en el almacén ({string.Join(", ", codigos ?? Enumerable.Empty<string>())}): {ex.Message}", ex));
                return 0;
            }
        }

        /// <summary>Con su propio contexto (para los sitios que no tienen el de la furgoneta a mano). Nunca lanza.</summary>
        public static async Task LeidosConContextoPropio(IEnumerable<string> codigos, string usuario, bool enAlgete)
        {
            List<string> lista = (codigos ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (lista.Count == 0)
            {
                return;
            }
            try
            {
                using (var db = new NVEntities())
                {
                    _ = await Leidos(new RepositorioFurgoneta(db), lista, usuario, enAlgete, DateTime.Now).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo apuntar lo leído en el almacén: {ex.Message}", ex));
            }
        }
    }
}
