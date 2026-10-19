using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Un usuario y su almacén (ParametrosUsuario «AlmacénPedidoVta»).</summary>
    public class UsuarioAlmacen
    {
        public string Usuario { get; set; }
        public string Almacen { get; set; }
    }

    /// <summary>Lo que se avisa de una reposición: a quién y qué.</summary>
    public class AvisoRecibidaEnParte
    {
        public int Traspaso { get; set; }
        public string Almacen { get; set; }
        public string Texto { get; set; }
        public List<string> Usuarios { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#673 (Carlos, 19/10/26): una reposición que lleva más de 2 días recibida en parte (desde la primera parcial) se
    /// avisa en la campana de Nesto a quienes hicieron sus recepciones parciales y, si la recibe una tienda, también a los
    /// usuarios de esa tienda (AlmacénPedidoVta, sin la fila «(defecto)»; ver <see cref="Destinatarios"/>), una vez al día
    /// mientras siga a medias. Lo mismo que se ve en la lista de recepciones
    /// (<see cref="OrigenRecepcionReposiciones.LeerPendientes"/>), con el mismo plazo. Nunca lanza: lo que falle va a ELMAH.
    /// </summary>
    public class AvisoReposicionesRecibidasEnParte
    {
        public const string TIPO_NOTIFICACION = "ReposicionRecibidaEnParte";
        internal const string CLAVE_ALMACEN_USUARIO = "AlmacénPedidoVta";
        internal const string USUARIO_DEFECTO = "(defecto)";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, Task<List<UsuarioAlmacen>>> usuariosConAlmacen;
        private readonly Func<string, int, Task<List<string>>> usuariosDeLasParciales;

        /// <summary>La hora de ahora (los tests la cambian).</summary>
        internal Func<DateTime> Ahora { get; set; } = () => DateTime.Now;

        /// <param name="usuariosConAlmacen">empresa → los usuarios con su AlmacénPedidoVta (incluida o no la fila «(defecto)»).</param>
        /// <param name="usuariosDeLasParciales">(empresa, traspaso) → quienes hicieron sus recepciones parciales (con o sin dominio).</param>
        public AvisoReposicionesRecibidasEnParte(IServicioRecepcionReposiciones reposiciones, IServicioNotificacionesPush notificaciones,
            Func<string, Task<List<UsuarioAlmacen>>> usuariosConAlmacen, Func<string, int, Task<List<string>>> usuariosDeLasParciales)
        {
            this.reposiciones = reposiciones;
            this.notificaciones = notificaciones;
            this.usuariosConAlmacen = usuariosConAlmacen;
            this.usuariosDeLasParciales = usuariosDeLasParciales;
        }

        /// <summary>
        /// A quién se avisa (Carlos, 19/10/26): siempre a quienes hicieron las parciales; en una tienda (REI, ALC: las mismas que
        /// <see cref="ReglasPerfilAlmacen.TIENDAS"/>), además a todos sus usuarios. En Algete, no: allí tienen AlmacénPedidoVta ALG
        /// comerciales, administración, SA… Sin repetir a nadie, siempre como NUEVAVISION\usuario.
        /// </summary>
        internal static List<string> Destinatarios(string almacen, IEnumerable<string> deLasParciales, IEnumerable<string> delAlmacen)
        {
            IEnumerable<string> candidatos = deLasParciales ?? Enumerable.Empty<string>();
            if (ReglasPerfilAlmacen.TIENDAS.Contains(almacen?.Trim().ToUpperInvariant()))
            {
                candidatos = candidatos.Concat(delAlmacen ?? Enumerable.Empty<string>());
            }
            return candidatos
                .Select(ReglasPerfilAlmacen.SinDominio)
                .Where(u => !string.IsNullOrWhiteSpace(u) && !string.Equals(u, USUARIO_DEFECTO, StringComparison.OrdinalIgnoreCase))
                .Select(u => DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>«La reposición 81029 desde Algete lleva 3 días recibida en parte: quedan 4 líneas (12 ud.) por dar de alta.»</summary>
        internal static string Texto(ReposicionPendienteDTO reposicion, int dias)
        {
            string origen = !string.IsNullOrWhiteSpace(reposicion.NombreOrigen) ? reposicion.NombreOrigen.Trim()
                : !string.IsNullOrWhiteSpace(reposicion.Origen) ? reposicion.Origen.Trim()
                : "su almacén de origen";
            return string.Format(CultureInfo.InvariantCulture,
                "La reposición {0} desde {1} lleva {2} días recibida en parte: quedan {3} ({4} ud.) por dar de alta.",
                reposicion.Traspaso, origen, dias, TextosTipoRecepcion.Contar(reposicion.Lineas, "línea", "líneas"), reposicion.Unidades);
        }

        /// <summary>Si lleva más del plazo recibida en parte, los días enteros desde la primera parcial; si no, null.</summary>
        internal static int? DiasRecibidaEnParte(ReposicionPendienteDTO reposicion, DateTime ahora)
        {
            if (reposicion == null || !reposicion.FechaPrimeraParcial.HasValue
                || OrigenRecepcionReposiciones.TextoRecibidaEnParte(reposicion.LineasRecibidas, reposicion.UnidadesRecibidas) == null)
            {
                return null;
            }
            TimeSpan transcurrido = ahora - reposicion.FechaPrimeraParcial.Value;
            return transcurrido > OrigenRecepcionReposiciones.PLAZO_RECIBIDA_EN_PARTE ? (int?)(int)Math.Floor(transcurrido.TotalDays) : null;
        }

        /// <summary>Busca las reposiciones a medias de cada almacén con usuarios y avisa. Devuelve lo avisado. Nunca lanza.</summary>
        public async Task<List<AvisoRecibidaEnParte>> Avisar(string empresa)
        {
            var avisados = new List<AvisoRecibidaEnParte>();
            List<UsuarioAlmacen> usuarios;
            try
            {
                usuarios = await usuariosConAlmacen(empresa).ConfigureAwait(false) ?? new List<UsuarioAlmacen>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] No se han podido leer los usuarios de los almacenes: {ex.Message}", ex));
                return avisados;
            }

            DateTime ahora = Ahora();
            IEnumerable<IGrouping<string, string>> porAlmacen = usuarios
                .Where(u => !string.IsNullOrWhiteSpace(u?.Usuario) && !string.IsNullOrWhiteSpace(u.Almacen)
                    && !string.Equals(u.Usuario.Trim(), USUARIO_DEFECTO, StringComparison.OrdinalIgnoreCase))
                .GroupBy(u => u.Almacen.Trim().ToUpperInvariant(), u => u.Usuario.Trim());
            foreach (IGrouping<string, string> almacen in porAlmacen)
            {
                try
                {
                    List<ReposicionPendienteDTO> pendientes = await reposiciones.LeerPendientes(empresa, almacen.Key).ConfigureAwait(false)
                        ?? new List<ReposicionPendienteDTO>();
                    foreach (ReposicionPendienteDTO reposicion in pendientes)
                    {
                        int? dias = DiasRecibidaEnParte(reposicion, ahora);
                        if (!dias.HasValue)
                        {
                            continue;
                        }
                        List<string> deLasParciales;
                        try
                        {
                            deLasParciales = await usuariosDeLasParciales(empresa, reposicion.Traspaso).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] No se ha podido leer quién recibió en parte la reposición {reposicion.Traspaso}: {ex.Message}", ex));
                            deLasParciales = null;
                        }
                        List<string> destinatarios = Destinatarios(almacen.Key, deLasParciales, almacen);
                        if (!destinatarios.Any())
                        {
                            ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] No hay a quién avisar de la reposición {reposicion.Traspaso} ({almacen.Key})."));
                            continue;
                        }
                        var aviso = new AvisoRecibidaEnParte { Traspaso = reposicion.Traspaso, Almacen = almacen.Key, Texto = Texto(reposicion, dias.Value) };
                        var notificacion = new NotificacionPushDTO
                        {
                            Titulo = $"Reposición {reposicion.Traspaso} recibida en parte",
                            Cuerpo = aviso.Texto,
                            Tipo = TIPO_NOTIFICACION,
                            Datos = new Dictionary<string, string>
                            {
                                ["tipo"] = TIPO_NOTIFICACION,
                                ["traspaso"] = reposicion.Traspaso.ToString(CultureInfo.InvariantCulture),
                                ["almacen"] = almacen.Key
                            }
                        };
                        foreach (string usuario in destinatarios)
                        {
                            try
                            {
                                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                                aviso.Usuarios.Add(usuario);
                            }
                            catch (Exception ex)
                            {
                                ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] No se ha podido avisar a {usuario} de la reposición {reposicion.Traspaso}: {ex.Message}", ex));
                            }
                        }
                        avisados.Add(aviso);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] Error con el almacén {almacen.Key}: {ex.Message}", ex));
                }
            }
            return avisados;
        }

        /// <summary>Los usuarios con AlmacénPedidoVta de la empresa (la fila «(defecto)» la quita <see cref="Avisar"/>).</summary>
        internal static async Task<List<UsuarioAlmacen>> LeerUsuariosConAlmacen(NVEntities db, string empresa)
        {
            List<ParametroUsuario> filas = await db.ParametrosUsuario
                .Where(p => p.Empresa == empresa && p.Clave == CLAVE_ALMACEN_USUARIO && p.Usuario != USUARIO_DEFECTO)
                .ToListAsync().ConfigureAwait(false);
            return filas.Select(p => new UsuarioAlmacen { Usuario = p.Usuario?.Trim(), Almacen = p.Valor?.Trim() }).ToList();
        }

        internal const string SQL_USUARIOS_PARCIALES = @"
SELECT DISTINCT RTRIM(e.Usuario)
FROM dbo.PreparacionEscaneos e
WHERE e.Empresa = @p0 AND e.TipoOrigen = 'REPO' AND e.NumeroOrigen = @p1 AND e.Fase = '" + EvidenciasRecepcionSql.FASE + @"'
  AND e.Motivo LIKE @p2 AND e.Usuario IS NOT NULL";

        /// <summary>Quienes hicieron las recepciones parciales de la reposición (sus evidencias, como las cuenta la lista).</summary>
        internal static Task<List<string>> LeerUsuariosDeLasParciales(NVEntities db, string empresa, int traspaso)
            => db.Database.SqlQuery<string>(SQL_USUARIOS_PARCIALES, empresa, traspaso, EvidenciasRecepcionSql.MOTIVO_PARCIAL + " %").ToListAsync();
    }

    /// <summary>
    /// NestoAPI#673: el job diario (L-S a las 9:15) del aviso de reposiciones recibidas en parte. Nunca rompe: lo que falle va
    /// a ELMAH y el job acaba bien (no se reintenta: el aviso es de una vez al día).
    /// </summary>
    public static class AvisoReposicionesRecibidasEnParteJobsService
    {
        public const string ID_JOB = "reposiciones-recibidas-en-parte";
        public const string CRON = "15 9 * * 1-6";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Avisar()
        {
            try
            {
                using (var db = new NVEntities())
                using (var reposiciones = new ServicioRecepcionReposiciones())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var aviso = new AvisoReposicionesRecibidasEnParte(reposiciones, new ServicioNotificacionesPush(),
                        empresa => AvisoReposicionesRecibidasEnParte.LeerUsuariosConAlmacen(db, empresa),
                        (empresa, traspaso) => AvisoReposicionesRecibidasEnParte.LeerUsuariosDeLasParciales(db, empresa, traspaso));
                    _ = await aviso.Avisar(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reposiciones recibidas en parte] Error en el job: {ex.Message}", ex), "Sistema (reposiciones recibidas en parte)");
            }
        }
    }
}
