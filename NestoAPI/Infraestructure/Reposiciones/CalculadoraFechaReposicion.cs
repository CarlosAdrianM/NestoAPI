using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#577: cuándo llega a <see cref="Destino"/> lo que sale en la próxima reposición desde <see cref="Origen"/>
    /// y cuándo saldría de allí el pedido que lo espera.
    /// </summary>
    public class ProximaReposicionDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>
        /// Día y hora de cierre: a esa hora se rellena sola la reposición y la tienda la prepara. Con antelación
        /// (ReposicionesCalendario.LaborablesAntelacionCierre), N laborables del origen ANTES del día de llegada.
        /// </summary>
        public DateTime CierraEl { get; set; }
        /// <summary>Día y hora a la que suele entrar en el destino.</summary>
        public DateTime LlegaEl { get; set; }
        /// <summary>Día (sin hora) en que saldría del destino el pedido que espera la mercancía.</summary>
        public DateTime PedidoSaleEl { get; set; }
        /// <summary>Días naturales desde hoy hasta <see cref="PedidoSaleEl"/> (0 = hoy).</summary>
        public int DiasHastaSalida { get; set; }
    }

    /// <summary>NestoAPI#577 (corte 3b): una reposición del calendario en un día concreto, con su instante de corte.</summary>
    public class CorteReposicion
    {
        public string Empresa { get; set; }
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>
        /// Día de cierre + HoraCierre: a esa hora se rellena sola (solo cuentan los pedidos anteriores). El día de cierre es
        /// el de llegada menos LaborablesAntelacionCierre laborables del origen (0 = el mismo día).
        /// </summary>
        public DateTime Corte { get; set; }
        /// <summary>Día de llegada (el DiaSemana de la fila) + HoraLlegadaHabitual.</summary>
        public DateTime Llegada { get; set; }
    }

    /// <summary>
    /// NestoAPI#577 (corte 1): clase pura (sin BD) que predice la próxima reposición de una ruta a partir del calendario
    /// (tabla ReposicionesCalendario), los festivos de cada almacén y la hora de corte del picking del destino.
    ///
    /// <para>Reglas: DiaSemana es el día de LLEGADA (el de la ruta), que tiene que ser laborable en el origen (no hay
    /// recogida) y en el destino (no hay recepción): ni sábados ni domingos ni festivos. Llega ese día a
    /// HoraLlegadaHabitual. Se CIERRA (se rellena sola) LaborablesAntelacionCierre laborables del origen antes, a
    /// HoraCierre (0 = el mismo día; 1 = el laborable anterior: la del lunes, el viernes). La próxima reposición es la
    /// primera llegada cuyo cierre sea posterior a <c>ahora</c>. El pedido sale el día de llegada si la mercancía llega
    /// ANTES del corte del picking; si no (o justo a la hora del corte), el siguiente día laborable del destino.</para>
    /// </summary>
    public class CalculadoraFechaReposicion
    {
        /// <summary>Hasta dónde se busca una reposición (un calendario con todo festivo no debe colgar la llamada).</summary>
        internal const int DIAS_MAXIMOS_BUSQUEDA = 60;

        private readonly Func<DateTime, string, bool> esFestivo;

        /// <summary>Con los festivos de <see cref="GestorFestivos.EsFestivo"/> (delegación = código de almacén).</summary>
        public CalculadoraFechaReposicion() : this(GestorFestivos.EsFestivo)
        {
        }

        /// <param name="esFestivo">(día, almacén) → true si ese almacén no trabaja ese día.</param>
        public CalculadoraFechaReposicion(Func<DateTime, string, bool> esFestivo)
        {
            this.esFestivo = esFestivo ?? throw new ArgumentNullException(nameof(esFestivo));
        }

        /// <summary>1 lunes … 7 domingo, como la columna DiaSemana.</summary>
        public static byte DiaSemana(DateTime fecha)
        {
            return fecha.DayOfWeek == DayOfWeek.Sunday ? (byte)7 : (byte)fecha.DayOfWeek;
        }

        /// <summary>La próxima reposición de <paramref name="origen"/> a <paramref name="destino"/>; null si la ruta no tiene calendario activo.</summary>
        public ProximaReposicionDTO Calcular(IEnumerable<ReposicionCalendario> calendario, string origen, string destino,
            TimeSpan horaCortePicking, DateTime ahora, string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO)
        {
            string origenLimpio = Limpiar(origen);
            string destinoLimpio = Limpiar(destino);
            string empresaLimpia = Limpiar(empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;

            List<ReposicionCalendario> filasRuta = (calendario ?? Enumerable.Empty<ReposicionCalendario>())
                .Where(f => f != null && f.Activo
                    && Limpiar(f.Empresa) == empresaLimpia
                    && Limpiar(f.AlmacenOrigen) == origenLimpio
                    && Limpiar(f.AlmacenDestino) == destinoLimpio)
                .ToList();
            if (!filasRuta.Any())
            {
                return null;
            }

            for (int i = 0; i <= DIAS_MAXIMOS_BUSQUEDA; i++)
            {
                DateTime dia = ahora.Date.AddDays(i);
                if (!EsLaborable(dia, origenLimpio) || !EsLaborable(dia, destinoLimpio))
                {
                    continue;
                }

                var candidata = filasRuta
                    .Where(f => f.DiaSemana == DiaSemana(dia))
                    .Select(f => new { Fila = f, Cierre = DiaCierre(dia, f.LaborablesAntelacionCierre, origenLimpio) + f.HoraCierre })
                    .Where(c => c.Cierre > ahora)
                    .OrderBy(c => c.Cierre)
                    .FirstOrDefault();
                if (candidata == null)
                {
                    continue;
                }
                ReposicionCalendario fila = candidata.Fila;

                // NestoAPI#606 (decisión de Carlos, 08/10): la hora de llegada es la de ESTA fila (cada reposición la suya), y el
                // umbral es la hora de corte del picking (parámetro HoraCortePicking, 11:00, única fuente): lo que llega antes
                // sale en el picking de ese día; a esa hora o después, en el del laborable siguiente.
                DateTime llegaEl = dia + fila.HoraLlegadaHabitual;
                DateTime pedidoSaleEl = fila.HoraLlegadaHabitual < horaCortePicking
                    ? dia
                    : SiguienteLaborable(dia, destinoLimpio);
                return new ProximaReposicionDTO
                {
                    Origen = origenLimpio,
                    Destino = destinoLimpio,
                    CierraEl = candidata.Cierre,
                    LlegaEl = llegaEl,
                    PedidoSaleEl = pedidoSaleEl,
                    DiasHastaSalida = (pedidoSaleEl - ahora.Date).Days
                };
            }
            return null;
        }

        /// <summary>
        /// NestoAPI#577 (corte 3b, 3d): las reposiciones que CIERRAN el día de <paramref name="dia"/> (la hora no cuenta), con
        /// su instante de corte (día de cierre + HoraCierre) y de llegada (día de llegada + HoraLlegadaHabitual). Con
        /// antelación, la del lunes cierra el viernes: CortesDelDia(viernes) la devuelve con Llegada el lunes. Las mismas
        /// reglas que <see cref="Calcular"/>: el día de llegada, laborable en origen y destino; el de cierre, N laborables
        /// del origen antes.
        /// </summary>
        public List<CorteReposicion> CortesDelDia(IEnumerable<ReposicionCalendario> calendario, DateTime dia)
        {
            DateTime fecha = dia.Date;
            var cortes = new List<CorteReposicion>();
            foreach (ReposicionCalendario fila in FilasValidas(calendario))
            {
                string origen = Limpiar(fila.AlmacenOrigen);
                // El día de cierre avanza con el de llegada: se recorren llegadas desde hoy hasta que su cierre pase de hoy.
                for (int i = 0; i <= DIAS_MAXIMOS_BUSQUEDA; i++)
                {
                    DateTime llegada = fecha.AddDays(i);
                    DateTime cierre = DiaCierre(llegada, fila.LaborablesAntelacionCierre, origen);
                    if (cierre > fecha)
                    {
                        break;
                    }
                    if (cierre == fecha && EsDiaDeLlegada(fila, llegada))
                    {
                        cortes.Add(Corte(fila, llegada));
                    }
                }
            }
            return Ordenar(cortes);
        }

        /// <summary>
        /// NestoAPI#577 (corte 3d): las reposiciones que LLEGAN el día de <paramref name="dia"/>, con su corte (que, con
        /// antelación, es de un día anterior).
        /// </summary>
        public List<CorteReposicion> CortesQueLleganElDia(IEnumerable<ReposicionCalendario> calendario, DateTime dia)
        {
            DateTime fecha = dia.Date;
            return Ordenar(FilasValidas(calendario)
                .Where(f => EsDiaDeLlegada(f, fecha))
                .Select(f => Corte(f, fecha)));
        }

        /// <summary>
        /// NestoAPI#577 (corte 3d): lo que mira el job (y el relanzamiento manual) el día <paramref name="dia"/>: las que
        /// cierran ese día (<see cref="CortesDelDia"/>) y las que llegan ese día (<see cref="CortesQueLleganElDia"/>), sin
        /// repetir (con antelación 0 son las mismas). Las segundas, para no perder la del lunes si el viernes no se rellenó
        /// (la API estaba caída): el lunes por la mañana aún da tiempo, hasta su hora de llegada.
        /// </summary>
        public List<CorteReposicion> CortesDeHoy(IEnumerable<ReposicionCalendario> calendario, DateTime dia)
        {
            List<ReposicionCalendario> filas = (calendario ?? Enumerable.Empty<ReposicionCalendario>()).ToList();
            return Ordenar(CortesDelDia(filas, dia).Concat(CortesQueLleganElDia(filas, dia))
                .GroupBy(c => new { c.Empresa, c.Origen, c.Destino, c.Corte })
                .Select(g => g.First()));
        }

        /// <summary>
        /// El día de cierre de la reposición que llega el día <paramref name="llegada"/>: <paramref name="antelacion"/>
        /// laborables del <paramref name="origen"/> antes (0 = el mismo día). Con festivos, el laborable anterior.
        /// </summary>
        internal DateTime DiaCierre(DateTime llegada, byte antelacion, string origen)
        {
            DateTime dia = llegada.Date;
            int pendientes = antelacion;
            for (int i = 0; pendientes > 0 && i < DIAS_MAXIMOS_BUSQUEDA; i++)
            {
                dia = dia.AddDays(-1);
                if (EsLaborable(dia, origen))
                {
                    pendientes--;
                }
            }
            return dia;
        }

        private static IEnumerable<ReposicionCalendario> FilasValidas(IEnumerable<ReposicionCalendario> calendario)
        {
            return (calendario ?? Enumerable.Empty<ReposicionCalendario>())
                .Where(f => f != null && f.Activo && Limpiar(f.AlmacenOrigen) != null && Limpiar(f.AlmacenDestino) != null);
        }

        private bool EsDiaDeLlegada(ReposicionCalendario fila, DateTime dia)
        {
            return fila.DiaSemana == DiaSemana(dia)
                && EsLaborable(dia, Limpiar(fila.AlmacenOrigen))
                && EsLaborable(dia, Limpiar(fila.AlmacenDestino));
        }

        private CorteReposicion Corte(ReposicionCalendario fila, DateTime diaLlegada)
        {
            string origen = Limpiar(fila.AlmacenOrigen);
            return new CorteReposicion
            {
                Empresa = Limpiar(fila.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                Origen = origen,
                Destino = Limpiar(fila.AlmacenDestino),
                Corte = DiaCierre(diaLlegada, fila.LaborablesAntelacionCierre, origen) + fila.HoraCierre,
                Llegada = diaLlegada.Date + fila.HoraLlegadaHabitual
            };
        }

        private static List<CorteReposicion> Ordenar(IEnumerable<CorteReposicion> cortes)
        {
            return cortes.OrderBy(c => c.Corte).ThenBy(c => c.Origen).ThenBy(c => c.Destino).ToList();
        }

        /// <summary>
        /// Con líneas que vienen de tiendas distintas manda la que deja salir el pedido más tarde (y, a igualdad, la que
        /// llega más tarde). Ignora los null; null si no queda ninguna.
        /// </summary>
        public static ProximaReposicionDTO MasTardia(IEnumerable<ProximaReposicionDTO> reposiciones)
        {
            return (reposiciones ?? Enumerable.Empty<ProximaReposicionDTO>())
                .Where(r => r != null)
                .OrderByDescending(r => r.PedidoSaleEl)
                .ThenByDescending(r => r.LlegaEl)
                .FirstOrDefault();
        }

        private DateTime SiguienteLaborable(DateTime dia, string almacen)
        {
            DateTime siguiente = dia.AddDays(1);
            for (int i = 0; i < DIAS_MAXIMOS_BUSQUEDA && !EsLaborable(siguiente, almacen); i++)
            {
                siguiente = siguiente.AddDays(1);
            }
            return siguiente;
        }

        private bool EsLaborable(DateTime dia, string almacen)
        {
            return dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia, almacen);
        }

        private static string Limpiar(string valor)
        {
            string limpio = valor?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }
    }
}
