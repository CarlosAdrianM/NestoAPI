using Hangfire;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#693: los tres trabajos de Hangfire de los pedidos con la fecha prometida en riesgo.
    /// <list type="bullet">
    /// <item><see cref="ProcesarEvento"/>: lo programa <see cref="EventosPromesaRiesgo.Anotar"/> un minuto después de cada
    /// evento. Uno a la vez (DisableConcurrentExecution): ni dos pasadas registran el mismo pedido dos veces ni se juntan
    /// ráfagas de recálculos en horario.</item>
    /// <item><see cref="PasadaNocturna"/> (L-S a las 5:40, fuera de horario): todos los pedidos con promesa de hoy o futura,
    /// para lo que no pasa por la API (Nesto viejo). En oct/26 son unas pocas decenas.</item>
    /// <item><see cref="EnviarCorreoResumen"/> (L-V a las 9:00 y a las 15:00): un correo por destinatario con lo nuevo (y lo
    /// que ha empeorado) desde el anterior. En Sombra, uno solo a Informática.</item>
    /// </list>
    /// Con el interruptor Apagado no hacen nada. Nunca rompen: lo que falle va a ELMAH y no se reintenta.
    /// </summary>
    public static class PromesaRiesgoJobsService
    {
        public const string ID_JOB_NOCTURNO = "promesa-riesgo-nocturna";
        public const string CRON_NOCTURNO = "40 5 * * 1-6";
        public const string ID_JOB_CORREO = "promesa-riesgo-correo";
        public const string CRON_CORREO = "0 9,15 * * 1-5";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task ProcesarEvento(EventoPromesaRiesgo evento)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await CrearServicio(db).ProcesarEvento(evento).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Promesa en riesgo #693] Error al procesar el evento «{evento?.Descripcion}»: {ex.Message}", ex),
                    "Sistema (promesa en riesgo)");
            }
        }

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static async Task PasadaNocturna()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await CrearServicio(db).PasadaNocturna(Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Promesa en riesgo #693] Error en la pasada nocturna: {ex.Message}", ex), "Sistema (promesa en riesgo)");
            }
        }

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task EnviarCorreoResumen()
        {
            try
            {
                if (GestorFestivos.EsFestivo(DateTime.Today, Constantes.Almacenes.ALGETE))
                {
                    return; // lo de hoy sale el siguiente laborable
                }
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await CorreoResumen(new RepositorioPromesaRiesgo(), CrearAvisador(db), () => DateTime.Now,
                        () => InterruptorAvisoPromesaRiesgo.Modo, Constantes.Empresas.EMPRESA_POR_DEFECTO).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Promesa en riesgo #693] Error en el correo resumen: {ex.Message}", ex), "Sistema (promesa en riesgo)");
            }
        }

        /// <summary>
        /// Los riesgos sin resolver que aún no han salido en un correo, agrupados por destinatario. Se marcan como enviados
        /// aunque algún correo falle (lo dice ELMAH): mejor perder un correo que mandar el mismo cada vez. Devuelve cuántos
        /// riesgos han ido.
        /// </summary>
        internal static async Task<int> CorreoResumen(IRepositorioPromesaRiesgo repositorio, IAvisadorPromesaRiesgo avisador,
            Func<DateTime> reloj, Func<ModoAvisoPromesaRiesgo> modo, string empresa)
        {
            ModoAvisoPromesaRiesgo modoActual = modo();
            if (modoActual == ModoAvisoPromesaRiesgo.Apagado || !repositorio.TablaExiste())
            {
                return 0;
            }
            List<RiesgoPromesa> nuevos = await repositorio.LeerSinCorreo(empresa).ConfigureAwait(false) ?? new List<RiesgoPromesa>();
            if (!nuevos.Any())
            {
                return 0;
            }
            List<AvisoPromesaRiesgo> avisos = nuevos.Select(r => avisador.Preparar(r, false)).ToList();
            _ = avisador.EnviarCorreos(avisos, modoActual == ModoAvisoPromesaRiesgo.Sombra);
            await repositorio.MarcarAvisadoCorreo(nuevos.Select(r => r.Id), reloj()).ConfigureAwait(false);
            return nuevos.Count;
        }

        internal static ServicioPromesaRiesgo CrearServicio(NVEntities db)
        {
            // Sin el aviso a Compras de proveedor vencido: ya lo da el cálculo cuando alguien mira el pedido, y esta pasada no
            // es «alguien esperando».
            var fechas = new ServicioFechaEntregaAgencia(new RepositorioFechaEntregaAgencia(db), () => DateTime.Now, GestorFestivos.EsFestivo,
                ex => ElmahHelper.Log(ex), avisadorProveedorVencido: null);
            return new ServicioPromesaRiesgo(new RepositorioPromesaRiesgo(), fechas, CrearAvisador(db));
        }

        internal static AvisadorPromesaRiesgo CrearAvisador(NVEntities db)
        {
            return new AvisadorPromesaRiesgo(new LectorComercialesPedido(db), new ServicioVendedores(), new ServicioNotificacionesPush(),
                new ServicioCorreoElectronico(), InterruptorAvisoPromesaRiesgo.UsuariosDireccion, usuario => CorreoDeUsuario(db, usuario),
                preferenciaDeUsuario: usuario => PreferenciaAvisosPromesaRiesgo.Leer(db, usuario));
        }

        /// <summary>El CorreoDefecto de ParametrosUsuario (Dirección: Carlos y Manuel lo tienen).</summary>
        private static string CorreoDeUsuario(NVEntities db, string usuario)
        {
            string sinDominio = ComercialesPedido.SinDominio(usuario);
            if (string.IsNullOrWhiteSpace(sinDominio))
            {
                return null;
            }
            string correo = db.ParametrosUsuario
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == sinDominio && p.Clave == Parametros.Claves.CorreoDefecto)
                .Select(p => p.Valor).FirstOrDefault();
            return string.IsNullOrWhiteSpace(correo) ? null : correo.Trim();
        }
    }
}
