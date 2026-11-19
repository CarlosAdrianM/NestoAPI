using System;
using System.Data.Entity;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881 (fleco): lo que importa de un envío para saber si cambia el conjunto de paradas del reparto propio
    /// (Ariadna recalcula con ello el fin previsto de la ruta).
    /// </summary>
    public class FotoEnvioParada
    {
        public int Agencia { get; set; }
        public short Estado { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public decimal Reembolso { get; set; }
        public short Bultos { get; set; }

        public static FotoEnvioParada De(EnviosAgencia envio) => envio == null ? null : new FotoEnvioParada
        {
            Agencia = envio.Agencia,
            Estado = envio.Estado,
            FechaEntrega = envio.FechaEntrega,
            Reembolso = envio.Reembolso,
            Bultos = envio.Bultos
        };
    }

    /// <summary>
    /// NestoAPI#881 (fleco), LA REGLA (pura): ¿este cambio de un envío cambia las paradas del reparto propio por algo que NO hace el
    /// repartidor? Parada = agencia 14 en Tramitado o Incidentado (lo que sale en GET api/RepartoPropio/Envios por entregar).
    /// Sí: nace una parada (alta ya tramitada o paso a Tramitado), deja de serlo (borrado, devuelto a pendiente o en curso, cerrado a
    /// mano, cambiado de agencia) o, siéndolo, cambia su día de reparto, su reembolso o sus bultos. No: entregar o dar una incidencia
    /// (paso a Entregado o Incidentado), que hace el repartidor y ya avisa con Motivo «Entrega»/«Incidencia»; de Incidentado a Tramitado
    /// sigue siendo la misma parada (solo cuenta si además cambia el día, el reembolso o los bultos).
    /// </summary>
    public static class CambiosParadasReparto
    {
        public static bool EsParada(FotoEnvioParada envio)
            => envio != null && envio.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                && (envio.Estado == Constantes.Agencias.ESTADO_TRAMITADO || envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO);

        public static bool AfectaAlReparto(EntityState estado, FotoEnvioParada antes, FotoEnvioParada despues)
        {
            switch (estado)
            {
                case EntityState.Added:
                    return EsParada(despues);
                case EntityState.Deleted:
                    return EsParada(antes);
                case EntityState.Modified:
                    bool eraParada = EsParada(antes);
                    bool esParada = EsParada(despues);
                    if (!eraParada && !esParada)
                    {
                        return false;
                    }
                    if (eraParada && despues != null && despues.Agencia == antes.Agencia && despues.Estado != antes.Estado
                        && (despues.Estado == Constantes.Agencias.ESTADO_ENTREGADO || despues.Estado == Constantes.Agencias.ESTADO_INCIDENTADO))
                    {
                        return false; // entregar o incidencia: lo hace el repartidor (y avisa él)
                    }
                    if (eraParada != esParada)
                    {
                        return true;
                    }
                    return antes.FechaEntrega?.Date != despues.FechaEntrega?.Date
                        || antes.Reembolso != despues.Reembolso
                        || antes.Bultos != despues.Bultos;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// NestoAPI#881 (fleco): junta en UN aviso «repartoCambiado» (Motivo «Parada») todas las peticiones seguidas: se manda cuando pasan
    /// <see cref="SILENCIO"/> sin otra (o, como mucho, <see cref="MAXIMO"/> después de la primera). Así Facturar rutas, TramitarTodos o un
    /// PUT con varios guardados avisan una vez, y el aviso sale ya con la transacción confirmada. En segundo plano; nunca lanza.
    /// </summary>
    public sealed class AgrupadorAvisosParadas
    {
        public static readonly TimeSpan SILENCIO = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan MAXIMO = TimeSpan.FromSeconds(20);

        private readonly object cerrojo = new object();
        private readonly Action<string> emitir;
        private readonly Func<DateTime> ahora;
        private readonly Func<TimeSpan, Task> esperar;
        private readonly Action<Exception> registrar;
        private bool enMarcha;
        private DateTime primera;
        private DateTime ultima;
        private string usuario;

        public AgrupadorAvisosParadas(Action<string> emitir, Func<DateTime> ahora = null, Func<TimeSpan, Task> esperar = null,
            Action<Exception> registrar = null)
        {
            this.emitir = emitir ?? throw new ArgumentNullException(nameof(emitir));
            this.ahora = ahora ?? (() => DateTime.UtcNow);
            this.esperar = esperar ?? (t => Task.Delay(t));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>La pasada en curso (o la última): para los tests.</summary>
        internal Task Pasada { get; private set; } = Task.CompletedTask;

        public void Pedir(string usuario)
        {
            try
            {
                bool arrancar;
                lock (cerrojo)
                {
                    DateTime momento = ahora();
                    if (!string.IsNullOrWhiteSpace(usuario))
                    {
                        this.usuario = usuario.Trim();
                    }
                    ultima = momento;
                    arrancar = !enMarcha;
                    if (arrancar)
                    {
                        enMarcha = true;
                        primera = momento;
                    }
                }
                if (arrancar)
                {
                    Pasada = Task.Run(Esperar);
                }
            }
            catch (Exception ex)
            {
                Registrar(ex);
            }
        }

        private async Task Esperar()
        {
            try
            {
                string quien;
                while (true)
                {
                    TimeSpan falta;
                    lock (cerrojo)
                    {
                        DateTime limite = ultima + SILENCIO < primera + MAXIMO ? ultima + SILENCIO : primera + MAXIMO;
                        falta = limite - ahora();
                        if (falta <= TimeSpan.Zero)
                        {
                            enMarcha = false;
                            quien = usuario;
                            usuario = null;
                            break;
                        }
                    }
                    await esperar(falta).ConfigureAwait(false);
                }
                emitir(quien);
            }
            catch (Exception ex)
            {
                lock (cerrojo)
                {
                    enMarcha = false;
                }
                Registrar(ex);
            }
        }

        private void Registrar(Exception ex)
        {
            try
            {
                registrar(new Exception("[Reparto propio #881] No se ha podido avisar a las PDA de un cambio de paradas: " + ex.Message, ex));
            }
            catch
            {
                // nada: el aviso es un extra
            }
        }
    }

    /// <summary>
    /// NestoAPI#881 (fleco): el punto único del aviso «repartoCambiado» con Motivo «Parada» cuando las paradas cambian por algo que no
    /// hace el repartidor. Lo llaman el guardado de NVEntities (<see cref="CambiosParadasReparto"/>, todo lo que pasa por EF: tramitar,
    /// TramitarTodos, DesdePedido/packing, Facturar rutas, notas de cobro, aplazar, anular…) y los caminos que no tocan EnviosAgencia
    /// (servicio técnico de una nota). Sin exclusiones de PDA: el cambio no lo ha hecho ninguna. Nunca lanza.
    /// </summary>
    public static class AvisoParadasReparto
    {
        /// <summary>El «dispositivo» del aviso: no es ninguna PDA, así que no se excluye a nadie del grupo.</summary>
        public const string DISPOSITIVO = "NestoAPI";

        private static AgrupadorAvisosParadas agrupador;

        /// <summary>Sustituible en los tests.</summary>
        internal static AgrupadorAvisosParadas Agrupador
        {
            get => agrupador ?? (agrupador = new AgrupadorAvisosParadas(Emitir));
            set => agrupador = value;
        }

        public static void Avisar(string usuario)
        {
            try
            {
                Agrupador.Pedir(usuario);
            }
            catch
            {
                // nunca rompe la operación
            }
        }

        private static void Emitir(string usuario)
            => new global::NestoAPI.Infraestructure.Notificaciones.AvisosAlmacenSignalR().RepartoCambiado(usuario,
                global::NestoAPI.Models.Agencias.RepartoCambiadoDTO.MOTIVO_PARADA, DateTime.Now,
                new OrigenPeticionReparto { Dispositivo = DISPOSITIVO });
    }
}
