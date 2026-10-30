using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#763: el horario preferible de los clientes de las paradas, por <see cref="RepositorioHorarioPreferible.Clave"/>.</summary>
    public interface IHorariosParadas
    {
        /// <summary>Nunca lanza: sin script o con error, vacío.</summary>
        Task<Dictionary<string, List<FranjaHorario>>> HorariosDe(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos);
    }

    public class HorariosParadas : IHorariosParadas
    {
        private readonly ServicioHorarioPreferible servicio;

        public HorariosParadas(ServicioHorarioPreferible servicio)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
        }

        public Task<Dictionary<string, List<FranjaHorario>>> HorariosDe(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos)
            => servicio.HorariosDe(contactos);
    }

    /// <summary>
    /// NestoAPI#763: el «horario preferible» en las paradas. Solo lo respeta nuestra ruta (las agencias no). Cada parada enseña
    /// su horario (entero y el del día de la lista) y el orden sugerido estima a qué hora se llega a cada una y avisa de las que
    /// caerían fuera de su horario. NO cambia el orden por el horario (eso sería un problema con ventanas de tiempo): solo avisa,
    /// y el repartidor decide.
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Lo que se calcula que se tarda en cada parada (aparcar, descargar, firmar).</summary>
        public const int MINUTOS_POR_PARADA = 5;
        /// <summary>Velocidad media en ciudad para estimar con la línea recta cuando el motor no da los tramos.</summary>
        public const double VELOCIDAD_MEDIA_KMH = 30;
        /// <summary>Si se pide el orden de un día futuro, se supone que sale a esta hora.</summary>
        public static readonly TimeSpan HORA_SALIDA_POR_DEFECTO = new TimeSpan(9, 0, 0);

        /// <summary>Pone <see cref="EnvioRepartoDTO.HorarioPreferible"/> y <see cref="EnvioRepartoDTO.HorarioPreferibleDia"/> (el de <paramref name="dia"/>).</summary>
        private async Task CompletarHorarios(List<EnvioRepartoDTO> lista, DateTime dia)
        {
            if (paradas.Horarios == null || lista.Count == 0)
            {
                return;
            }
            List<EnvioRepartoDTO> conCliente = lista.Where(p => !NucleoParadasReparto.EsReposicion(p.EnvioId) && !string.IsNullOrWhiteSpace(p.Cliente)).ToList();
            if (conCliente.Count == 0)
            {
                return;
            }
            Dictionary<string, List<FranjaHorario>> horarios;
            try
            {
                horarios = await paradas.Horarios.HorariosDe(conCliente.Select(p => (p.Empresa, p.Cliente, p.Contacto)).ToList()).ConfigureAwait(false)
                    ?? new Dictionary<string, List<FranjaHorario>>();
            }
            catch (Exception ex)
            {
                // El horario es un añadido: sin él la lista sale igual.
                ElmahHelper.Log(new Exception($"[Reparto propio #763] No se pudieron leer los horarios preferibles: {ex.Message}", ex));
                return;
            }
            int diaSemana = FormatoHorarioPreferible.DiaSemana(dia.DayOfWeek);
            foreach (EnvioRepartoDTO parada in conCliente)
            {
                if (horarios.TryGetValue(RepositorioHorarioPreferible.Clave(parada.Empresa, parada.Cliente, parada.Contacto), out List<FranjaHorario> franjas)
                    && franjas.Count > 0)
                {
                    parada.HorarioPreferible = FormatoHorarioPreferible.Texto(franjas);
                    parada.HorarioPreferibleDia = FormatoHorarioPreferible.TextoDia(franjas, diaSemana);
                }
            }
        }

        /// <summary>
        /// NestoAPI#763: la hora de llegada a cada parada del recorrido (en su orden) y si cae fuera de su horario. Sale de
        /// <paramref name="salida"/> a la hora <paramref name="horaSalida"/>; los tramos son los del motor (OSRM) o, si no los da,
        /// la línea recta a <see cref="VELOCIDAD_MEDIA_KMH"/>, más <see cref="MINUTOS_POR_PARADA"/> en cada parada.
        /// Devuelve cuántas caen fuera.
        /// </summary>
        internal static int EstimarLlegadas(IReadOnlyList<EnvioRepartoDTO> recorrido, CoordenadasGeo salida, DateTime horaSalida,
            IReadOnlyList<double> minutosTramos)
        {
            bool tramosDelMotor = minutosTramos != null && minutosTramos.Count >= recorrido.Count;
            DateTime reloj = horaSalida;
            CoordenadasGeo anterior = salida;
            int fuera = 0;
            for (int i = 0; i < recorrido.Count; i++)
            {
                EnvioRepartoDTO parada = recorrido[i];
                if (!CoordenadasGeo.Validas(parada.Latitud, parada.Longitud))
                {
                    continue;
                }
                var aqui = new CoordenadasGeo(parada.Latitud.Value, parada.Longitud.Value);
                double minutos = tramosDelMotor
                    ? minutosTramos[i]
                    : HeuristicaRutas.DistanciaKm(anterior, aqui) * HeuristicaRutas.FACTOR_CARRETERA / VELOCIDAD_MEDIA_KMH * 60d;
                reloj = reloj.AddMinutes(Math.Max(0, minutos));
                parada.LlegadaEstimada = new DateTime(reloj.Year, reloj.Month, reloj.Day, reloj.Hour, reloj.Minute, 0);
                List<FranjaHorario> franjas = string.IsNullOrWhiteSpace(parada.HorarioPreferible) ? null : FormatoHorarioPreferible.Leer(parada.HorarioPreferible, out _);
                if (franjas != null
                    && franjas.Count > 0)
                {
                    int dia = FormatoHorarioPreferible.DiaSemana(reloj.DayOfWeek);
                    TimeSpan hora = parada.LlegadaEstimada.Value.TimeOfDay;
                    parada.FueraDeHorario = !franjas.Any(f => f.DiaSemana == dia && f.Contiene(hora));
                    if (parada.FueraDeHorario)
                    {
                        fuera++;
                    }
                }
                reloj = reloj.AddMinutes(MINUTOS_POR_PARADA);
                anterior = aqui;
            }
            return fuera;
        }

        /// <summary>El aviso de <see cref="OrdenSugeridoRepartoDTO.AvisoHorario"/>, o null.</summary>
        internal static string AvisoHorario(IEnumerable<EnvioRepartoDTO> fuera)
        {
            List<EnvioRepartoDTO> lista = fuera.ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            string nombres = string.Join(", ", lista.Take(3).Select(p => p.Nombre?.Trim()).Where(n => !string.IsNullOrEmpty(n)));
            string mas = lista.Count > 3 ? $" y {lista.Count - 3} más" : string.Empty;
            string cuantas = lista.Count == 1 ? "Una parada llegaría" : $"{lista.Count} paradas llegarían";
            return $"{cuantas} fuera de su horario preferible ({nombres}{mas}). Revise el orden si puede; el horario solo es una preferencia.";
        }

        /// <summary>A qué hora sale el recorrido: la de la PDA; para un día futuro, <see cref="HORA_SALIDA_POR_DEFECTO"/>; null si el día ya pasó.</summary>
        internal static DateTime? HoraSalida(DateTime dia, DateTime ahoraPda)
        {
            if (dia.Date < ahoraPda.Date)
            {
                return null;
            }
            return dia.Date > ahoraPda.Date ? dia.Date + HORA_SALIDA_POR_DEFECTO : ahoraPda;
        }
    }
}
