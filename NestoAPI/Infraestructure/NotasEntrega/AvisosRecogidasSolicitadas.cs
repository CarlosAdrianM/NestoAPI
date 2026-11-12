using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>
    /// NestoAPI#855: los avisos de las recogidas solicitadas, con los mecanismos de siempre: la campana del grupo Almacén (Nesto y
    /// Ariadna, <see cref="AvisadorRetencionAlmacen"/>), la push de NestoApp a quien la pidió (que también la guarda en su buzón), la
    /// campana de Nesto si la pidió desde Nesto y «haCambiado» al grupo <see cref="AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS"/> del
    /// AlmacenHub para que la pantalla de Nesto se refresque sola (sin sondeo). En segundo plano y sin lanzar nunca (ELMAH).
    /// </summary>
    public class AvisosRecogidasSolicitadas : IAvisosRecogidasSolicitadas
    {
        public const string TIPO_SOLICITADA = "RecogidaSolicitada";
        public const string TIPO_RESUELTA = "RecogidaSolicitadaResuelta";
        public const string TIPO_SIN_RESPUESTA = "RecogidaSolicitadaSinRespuesta";
        public const string TIPO_ANULADA = "RecogidaSolicitadaAnulada";
        internal const string DOMINIO = "NUEVAVISION\\";

        private readonly Func<IServicioNotificacionesPush> crearNotificaciones;
        private readonly Func<NotificacionPushDTO, Task> avisarAlmacen;
        private readonly Action<string, string> haCambiado;
        private readonly Func<Func<Task>, Task> enSegundoPlano;

        public AvisosRecogidasSolicitadas()
            : this(() => new ServicioNotificacionesPush(), n => new AvisadorRetencionAlmacen().Avisar(n),
                  (grupo, usuario) => new AvisosAlmacenSignalR().HaCambiado(grupo, usuario, null, null), trabajo => Task.Run(trabajo))
        {
        }

        internal AvisosRecogidasSolicitadas(Func<IServicioNotificacionesPush> crearNotificaciones, Func<NotificacionPushDTO, Task> avisarAlmacen,
            Action<string, string> haCambiado, Func<Func<Task>, Task> enSegundoPlano)
        {
            this.crearNotificaciones = crearNotificaciones;
            this.avisarAlmacen = avisarAlmacen;
            this.haCambiado = haCambiado;
            this.enSegundoPlano = enSegundoPlano ?? (trabajo => trabajo());
        }

        public void Solicitada(RecogidaSolicitadaDTO recogida, string usuario)
        {
            if (recogida == null)
            {
                return;
            }
            Refrescar(usuario);
            Lanzar(() => avisarAlmacen(Componer(TIPO_SOLICITADA, recogida,
                $"Recogida pedida: {Cliente(recogida)}",
                $"{QuienPide(recogida)} pide recoger en {Cliente(recogida)}: {recogida.Texto} ({Bultos(recogida.Bultos)}" +
                (recogida.QuienRecoge == QuienRecogeRecogida.VENDEDOR ? ", la recoge el vendedor" : string.Empty) + "). Confírmela o rechácela.")),
                "a Almacén de la solicitud");
        }

        public void Resuelta(RecogidaSolicitadaDTO recogida, string usuario)
        {
            if (recogida == null)
            {
                return;
            }
            Refrescar(usuario);
            bool rechazada = recogida.Estado == EstadosRecogidaSolicitada.RECHAZADA;
            string titulo = rechazada ? $"Recogida rechazada: {Cliente(recogida)}" : $"Recogida confirmada: {Cliente(recogida)}";
            string cuerpo = rechazada
                ? $"El almacén ha rechazado la recogida en {Cliente(recogida)}: {recogida.MotivoRechazo}"
                : $"El almacén ha confirmado la recogida en {Cliente(recogida)} (nota {recogida.CodigoDocumento}, {Bultos(recogida.Bultos)})." +
                  (recogida.QuienRecoge == QuienRecogeRecogida.VENDEDOR
                      ? " Al llegar a Algete enseñe el código de la nota para cerrarla."
                      : " Irá en el reparto.");
            NotificacionPushDTO notificacion = Componer(TIPO_RESUELTA, recogida, titulo, cuerpo);
            notificacion.Datos["estado"] = recogida.Estado;
            if (recogida.CodigoDocumento != null)
            {
                notificacion.Datos["codigoDocumento"] = recogida.CodigoDocumento;
            }
            Lanzar(() => AQuienLaPidio(recogida, notificacion), "a quien la pidió de la respuesta");
        }

        public void Anulada(RecogidaSolicitadaDTO recogida, string usuario, bool avisarAlmacen)
        {
            if (recogida == null)
            {
                return;
            }
            Refrescar(usuario);
            NotificacionPushDTO notificacion = Componer(TIPO_ANULADA, recogida, $"Recogida anulada: {Cliente(recogida)}",
                $"{usuario} ha anulado la recogida en {Cliente(recogida)}" +
                (recogida.CodigoDocumento != null ? $" (nota {recogida.CodigoDocumento})" : string.Empty) + "." +
                (string.IsNullOrWhiteSpace(recogida.AvisoAnulacion) ? string.Empty : " " + recogida.AvisoAnulacion));
            notificacion.Datos["estado"] = recogida.Estado;
            if (recogida.CodigoDocumento != null)
            {
                notificacion.Datos["codigoDocumento"] = recogida.CodigoDocumento;
            }
            if (avisarAlmacen)
            {
                Lanzar(() => this.avisarAlmacen(notificacion), "a Almacén de la anulación");
            }
            if (!ServicioRecogidasSolicitadas.MismoUsuario(recogida.UsuarioSolicita, usuario))
            {
                Lanzar(() => AQuienLaPidio(recogida, notificacion), "a quien la pidió de la anulación");
            }
        }

        public async Task SinRespuesta(RecogidaSolicitadaDTO recogida)
        {
            if (recogida == null)
            {
                return;
            }
            NotificacionPushDTO notificacion = Componer(TIPO_SIN_RESPUESTA, recogida, $"Recogida sin contestar: {Cliente(recogida)}",
                $"La recogida que pidió {QuienPide(recogida)} el {recogida.FechaSolicitud:dd/MM} a las {recogida.FechaSolicitud:H:mm} en " +
                $"{Cliente(recogida)} lleva un día laborable sin respuesta del almacén: {recogida.Texto}");
            await Seguro(() => avisarAlmacen(notificacion), "a Almacén de la recogida sin respuesta").ConfigureAwait(false);
            await Seguro(() => AQuienLaPidio(recogida, notificacion), "a quien la pidió de la recogida sin respuesta").ConfigureAwait(false);
        }

        /// <summary>
        /// Push de NestoApp a su usuario (sin dominio; se guarda en su buzón) y, si la pidió desde Nesto (usuario con dominio), la campana
        /// de Nesto.
        /// </summary>
        internal async Task AQuienLaPidio(RecogidaSolicitadaDTO recogida, NotificacionPushDTO notificacion)
        {
            string usuario = recogida.UsuarioSolicita?.Trim();
            string sinDominio = ServicioRecogidasSolicitadas.SinDominio(usuario);
            if (string.IsNullOrEmpty(sinDominio))
            {
                return;
            }
            IServicioNotificacionesPush notificaciones = crearNotificaciones();
            if (usuario.Contains("\\"))
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            _ = await notificaciones.EnviarAUsuario(sinDominio, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
        }

        internal static NotificacionPushDTO Componer(string tipo, RecogidaSolicitadaDTO recogida, string titulo, string cuerpo)
            => new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = tipo,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = tipo,
                    ["id"] = ServicioRecogidasSolicitadas.Entero(recogida.Id),
                    ["empresa"] = recogida.Empresa,
                    ["cliente"] = recogida.Cliente,
                    ["contacto"] = recogida.Contacto
                }
            };

        private static string Cliente(RecogidaSolicitadaDTO r)
            => $"{r.Cliente}/{r.Contacto}" + (string.IsNullOrWhiteSpace(r.NombreCliente) ? string.Empty : $" {r.NombreCliente}");

        private static string QuienPide(RecogidaSolicitadaDTO r)
            => ServicioRecogidasSolicitadas.SinDominio(r.UsuarioSolicita) ?? "Un vendedor";

        private static string Bultos(short bultos) => bultos == 1 ? "1 bulto" : $"{bultos} bultos";

        private void Refrescar(string usuario)
        {
            try
            {
                haCambiado?.Invoke(AvisosAlmacen.GRUPO_RECOGIDAS_SOLICITADAS, usuario);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido avisar al AlmacenHub: {ex.Message}", ex));
            }
        }

        private void Lanzar(Func<Task> trabajo, string que)
        {
            try
            {
                _ = enSegundoPlano(() => Seguro(trabajo, que));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido avisar {que}: {ex.Message}", ex));
            }
        }

        private static async Task Seguro(Func<Task> trabajo, string que)
        {
            try
            {
                await trabajo().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] No se ha podido avisar {que}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>
    /// NestoAPI#855: job «recogidas-solicitadas-sin-respuesta». De lunes a viernes, cada hora de 8:00 a 20:00: las solicitudes que llevan
    /// un día laborable sin contestar, campana a Almacén y a quien la pidió (una sola vez por solicitud). Sin el script, nada.
    /// </summary>
    public static class RecogidasSolicitadasJobsService
    {
        public const string ID_JOB = "recogidas-solicitadas-sin-respuesta";
        /// <summary>De lunes a viernes, a en punto de 8:00 a 20:00.</summary>
        public const string CRON = "0 8-20 * * 1-5";

        /// <summary>Punto de entrada de Hangfire. Nunca lanza.</summary>
        public static async Task ComprobarSinRespuesta()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var servicio = new ServicioRecogidasSolicitadas(new RepositorioRecogidasSolicitadas(db), new AvisosRecogidasSolicitadas());
                    _ = await servicio.AvisarSinRespuesta().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Recogidas solicitadas #855] Job {ID_JOB}: {ex.Message}", ex));
            }
        }
    }
}
