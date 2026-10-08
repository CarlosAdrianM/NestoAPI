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
        /// <summary>Día y hora de cierre: a esa hora se rellena sola la reposición y la tienda la prepara.</summary>
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
        /// <summary>Día + HoraCierre: a esa hora se rellena sola (solo cuentan los pedidos anteriores).</summary>
        public DateTime Corte { get; set; }
        /// <summary>Día + HoraLlegadaHabitual.</summary>
        public DateTime Llegada { get; set; }
    }

    /// <summary>
    /// NestoAPI#577 (corte 1): clase pura (sin BD) que predice la próxima reposición de una ruta a partir del calendario
    /// (tabla ReposicionesCalendario), los festivos de cada almacén y la hora de corte del picking del destino.
    ///
    /// <para>Reglas: la próxima reposición es la primera fila activa de la ruta cuyo día + HoraCierre sea posterior a
    /// <c>ahora</c>, saltando sábados, domingos y los días festivos del origen (no hay recogida) y del destino (no hay
    /// recepción). Llega ese mismo día a HoraLlegadaHabitual. El pedido sale ese día si la mercancía llega ANTES del corte
    /// del picking; si no (o justo a la hora del corte), el siguiente día laborable del destino.</para>
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

                ReposicionCalendario fila = filasRuta
                    .Where(f => f.DiaSemana == DiaSemana(dia) && dia + f.HoraCierre > ahora)
                    .OrderBy(f => f.HoraCierre)
                    .FirstOrDefault();
                if (fila == null)
                {
                    continue;
                }

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
                    CierraEl = dia + fila.HoraCierre,
                    LlegaEl = llegaEl,
                    PedidoSaleEl = pedidoSaleEl,
                    DiasHastaSalida = (pedidoSaleEl - ahora.Date).Days
                };
            }
            return null;
        }

        /// <summary>
        /// NestoAPI#577 (corte 3b): las reposiciones que tocan el día de <paramref name="dia"/> (la hora no cuenta), con su
        /// instante de corte (día + HoraCierre) y de llegada. Las mismas reglas que <see cref="Calcular"/>: ni sábados ni
        /// domingos ni festivos del origen (no hay recogida) o del destino (no hay recepción).
        /// </summary>
        public List<CorteReposicion> CortesDelDia(IEnumerable<ReposicionCalendario> calendario, DateTime dia)
        {
            DateTime fecha = dia.Date;
            return (calendario ?? Enumerable.Empty<ReposicionCalendario>())
                .Where(f => f != null && f.Activo && f.DiaSemana == DiaSemana(fecha))
                .Select(f => new CorteReposicion
                {
                    Empresa = Limpiar(f.Empresa) ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Origen = Limpiar(f.AlmacenOrigen),
                    Destino = Limpiar(f.AlmacenDestino),
                    Corte = fecha + f.HoraCierre,
                    Llegada = fecha + f.HoraLlegadaHabitual
                })
                .Where(c => c.Origen != null && c.Destino != null && EsLaborable(fecha, c.Origen) && EsLaborable(fecha, c.Destino))
                .OrderBy(c => c.Corte).ThenBy(c => c.Origen).ThenBy(c => c.Destino)
                .ToList();
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
