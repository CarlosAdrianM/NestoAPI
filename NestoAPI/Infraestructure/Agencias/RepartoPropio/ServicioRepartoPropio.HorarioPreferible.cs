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
                // Sin espera: aquí solo se dice qué pasaría con este orden (la espera es cosa de la segunda pasada, #769).
                LlegadaParada llegada = VentanaHorario.Llegar(reloj, FranjasDe(parada), 0);
                parada.LlegadaEstimada = VentanaHorario.Minuto(llegada.Hora);
                if (llegada.ConHorario)
                {
                    parada.FueraDeHorario = llegada.Fuera;
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
            string cuantas = lista.Count == 1 ? "Una parada llegaría" : $"{lista.Count} paradas llegarían";
            return $"{cuantas} fuera de su horario preferible ({Nombres(lista)}). Revise el orden si puede; el horario solo es una preferencia.";
        }

        /// <summary>«C1, C2, C3 y 2 más».</summary>
        private static string Nombres(List<EnvioRepartoDTO> lista)
        {
            string nombres = string.Join(", ", lista.Take(3).Select(p => p.Nombre?.Trim()).Where(n => !string.IsNullOrEmpty(n)));
            string mas = lista.Count > 3 ? $" y {lista.Count - 3} más" : string.Empty;
            return nombres + mas;
        }

        /// <summary>Las franjas del horario preferible de la parada (de <see cref="EnvioRepartoDTO.HorarioPreferible"/>), o null si no tiene.</summary>
        private static List<FranjaHorario> FranjasDe(EnvioRepartoDTO parada)
        {
            List<FranjaHorario> franjas = string.IsNullOrWhiteSpace(parada.HorarioPreferible) ? null : FormatoHorarioPreferible.Leer(parada.HorarioPreferible, out _);
            return franjas != null && franjas.Count > 0 ? franjas : null;
        }

        /// <summary>
        /// NestoAPI#769: el aviso cuando se ha cambiado el orden por el horario. Nunca dice que se respete: solo que se intenta.
        /// </summary>
        internal static string AvisoReordenado(IEnumerable<EnvioRepartoDTO> siguenFuera, int arregladas, int minutosExtra)
        {
            List<EnvioRepartoDTO> lista = siguenFuera.ToList();
            if (lista.Count == 0)
            {
                string clientes = arregladas == 1 ? "1 cliente" : $"{arregladas} clientes";
                string extra = minutosExtra > 0 ? $" ({minutosExtra} min más)" : string.Empty;
                return $"Orden cambiado para intentar llegar dentro del horario preferible de {clientes}{extra}.";
            }
            string llegarian = lista.Count == 1 ? "1 llegaría" : $"{lista.Count} llegarían";
            return $"Orden cambiado para intentar respetar los horarios preferibles; aun así {llegarian} fuera ({Nombres(lista)}). El horario es solo una preferencia.";
        }

        /// <summary>
        /// NestoAPI#769: la segunda pasada. Si alguna parada del recorrido más corto llega fuera de su horario y alguna tiene
        /// franjas ese día, busca otro orden con la matriz de tiempos (OSRM /table o, si falla, la línea recta) y
        /// <see cref="OrdenConHorarios"/>. Solo lo acepta si deja MENOS paradas fuera; entonces actualiza la respuesta (llegadas,
        /// fuera, kilómetros, minutos y el aviso). Cualquier fallo: el recorrido de antes, tal cual.
        /// </summary>
        private async Task<List<EnvioRepartoDTO>> ReordenarPorHorario(List<EnvioRepartoDTO> recorrido, CoordenadasGeo salida, CoordenadasGeo final,
            DateTime horaSalida, OrdenSugeridoRepartoDTO respuesta)
        {
            if (paradas.Tiempos == null || respuesta.FueraDeHorario == 0 || recorrido.Count < 2)
            {
                return recorrido;
            }
            try
            {
                List<List<FranjaHorario>> franjas = recorrido.Select(FranjasDe).ToList();
                int dia = FormatoHorarioPreferible.DiaSemana(horaSalida.DayOfWeek);
                if (!franjas.Any(f => f != null && f.Any(x => x.DiaSemana == dia)))
                {
                    return recorrido; // Las que tienen horario no lo tienen hoy: ningún orden lo arregla.
                }
                var puntos = new List<CoordenadasGeo> { salida };
                puntos.AddRange(recorrido.Select(p => new CoordenadasGeo(p.Latitud.Value, p.Longitud.Value)));
                puntos.Add(final);

                MatrizTiempos matriz = null;
                try
                {
                    matriz = await paradas.Tiempos.Calcular(puntos).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Reparto propio #769] La matriz de tiempos ha fallado; se usa la línea recta. {ex.Message}", ex));
                }
                if (matriz == null || matriz.Puntos != puntos.Count)
                {
                    matriz = MatrizTiempos.Heuristica(puntos);
                }

                List<int> masCorto = Enumerable.Range(0, recorrido.Count).ToList();
                EvaluacionOrden antes = OrdenConHorarios.Evaluar(matriz, franjas, horaSalida, masCorto);
                EvaluacionOrden mejor = OrdenConHorarios.Mejorar(matriz, franjas, horaSalida, masCorto);
                if (mejor.Fuera >= antes.Fuera || mejor.Orden.SequenceEqual(masCorto))
                {
                    return recorrido;
                }

                List<EnvioRepartoDTO> nuevo = mejor.Orden.Select(i => recorrido[i]).ToList();
                for (int i = 0; i < nuevo.Count; i++)
                {
                    nuevo[i].LlegadaEstimada = mejor.Llegadas[i];
                    nuevo[i].FueraDeHorario = mejor.FueraPorParada[i];
                }
                int minutosExtra = Math.Max(0, (int)Math.Round(mejor.MinutosTotales - antes.MinutosTotales, MidpointRounding.AwayFromZero));
                respuesta.ReordenadoPorHorario = true;
                respuesta.MinutosExtra = minutosExtra;
                respuesta.FueraDeHorario = mejor.Fuera;
                respuesta.Minutos = matriz.Motor == OrdenSugeridoRepartoDTO.MOTOR_OSRM || respuesta.Minutos.HasValue
                    ? (int)Math.Round(mejor.MinutosConduccion, MidpointRounding.AwayFromZero)
                    : (int?)null;
                respuesta.Kilometros = Kilometros(matriz, puntos, mejor.Orden);
                respuesta.AvisoHorario = AvisoReordenado(nuevo.Where(p => p.FueraDeHorario), antes.Fuera - mejor.Fuera, minutosExtra);
                return nuevo;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto propio #769] No se pudo reordenar por el horario preferible; se deja el recorrido más corto. {ex.Message}", ex));
                return recorrido;
            }
        }

        /// <summary>Del origen, por las paradas en <paramref name="orden"/>, al destino: los de la matriz o, si no los trae, línea recta × 1,3.</summary>
        private static decimal Kilometros(MatrizTiempos matriz, IReadOnlyList<CoordenadasGeo> puntos, IReadOnlyList<int> orden)
        {
            var camino = new List<int> { 0 };
            camino.AddRange(orden.Select(i => i + 1));
            camino.Add(puntos.Count - 1);
            double km = 0;
            for (int i = 0; i < camino.Count - 1; i++)
            {
                km += matriz.Kilometros != null
                    ? matriz.Kilometros[camino[i], camino[i + 1]]
                    : HeuristicaRutas.DistanciaKm(puntos[camino[i]], puntos[camino[i + 1]]) * HeuristicaRutas.FACTOR_CARRETERA;
            }
            return Math.Round((decimal)km, 1);
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
