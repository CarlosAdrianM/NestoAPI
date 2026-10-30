using System;
using System.Globalization;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#764 (Carlos 30/10/26): el calendario de reparto de NUESTRA ruta (ruta interna) por código postal. Almacén
    /// (Alfredo) decide en última instancia qué pedidos van por ruta interna (las rutas de <c>RutasPropiasConfiguradas</c>,
    /// hoy AT y 16; la sugerencia 575 se lo propone). De los que van, el código postal de entrega dice qué día se sirven
    /// (<see cref="DiasRepartoCodigosPostales"/>: tabla <c>dbo.CodigosPostalesDiaReparto</c>, 01 = lunes … 05 = viernes).
    /// <list type="bullet">
    /// <item>Si ese día es festivo de Algete, el siguiente laborable (lunes y martes festivos → miércoles; viernes festivo →
    /// lunes).</item>
    /// <item>El picking se hace EL MISMO DÍA del reparto: la fecha de entrega de la línea es el día de reparto.</item>
    /// <item>El día de su ruta, antes de la hora de corte (<see cref="HORA_CORTE"/>, la del picking automático de las rutas
    /// internas) sale ese día; a partir de esa hora, el siguiente día de su ruta. Otro día, el próximo día de su ruta.</item>
    /// <item>Lo que se queda pendiente sin stock espera al siguiente día de su ruta (el picking solo saca estos pedidos el día
    /// que les toca, <see cref="EsDiaDeReparto"/>).</item>
    /// </list>
    /// Pura: los festivos llegan como función. Es el ÚNICO sitio con estas fechas.
    /// </summary>
    public static class CalendarioRutasDia
    {
        private static readonly CultureInfo ES = new CultureInfo("es-ES");

        /// <summary>
        /// La hora de corte de los pedidos de ruta interna con día de reparto: la del picking automático de rutas internas
        /// (<c>PickingRutasInternasJobsService</c>, cron <see cref="CRON_PICKING"/>), cuando sale la furgoneta. Es EN PUNTO: a
        /// esa hora ya no sale hoy. Distinta de <c>HoraCortePicking</c> (11:00), que es la de la agencia.
        /// </summary>
        public static readonly TimeSpan HORA_CORTE = new TimeSpan(9, 0, 0);

        /// <summary>El cron del picking automático de rutas internas: de lunes a viernes a la <see cref="HORA_CORTE"/>.</summary>
        public static string CRON_PICKING => $"{HORA_CORTE.Minutes} {HORA_CORTE.Hours} * * 1-5";

        /// <summary>Tope de seguridad al saltar festivos (un calendario roto que diga que todo es festivo no cuelga).</summary>
        private const int MAXIMO_DIAS_SALTO = 366;

        /// <summary>«01» → lunes … «05» → viernes (la columna Ruta de CodigosPostalesDiaReparto); cualquier otra cosa, null.</summary>
        public static DayOfWeek? DiaDeLaSemana(string codigoDia)
        {
            switch (codigoDia?.Trim())
            {
                case "01": return DayOfWeek.Monday;
                case "02": return DayOfWeek.Tuesday;
                case "03": return DayOfWeek.Wednesday;
                case "04": return DayOfWeek.Thursday;
                case "05": return DayOfWeek.Friday;
                default: return null;
            }
        }

        /// <summary>
        /// El día de reparto en la semana (de lunes a domingo) que contiene <paramref name="fechaEnLaSemana"/>: su día nominal y,
        /// si es festivo, el siguiente laborable (que puede caer ya en la semana siguiente).
        /// </summary>
        public static DateTime DiaRepartoDeLaSemana(DayOfWeek dia, DateTime fechaEnLaSemana, Func<DateTime, bool> esFestivo)
        {
            if (esFestivo == null)
            {
                throw new ArgumentNullException(nameof(esFestivo));
            }
            DateTime nominal = Lunes(fechaEnLaSemana).AddDays(((int)dia + 6) % 7);
            return SiguienteLaborableDesde(nominal, esFestivo);
        }

        /// <summary>El primer día de reparto igual o posterior a <paramref name="desde"/> (sin hora).</summary>
        public static DateTime ProximoDiaReparto(DayOfWeek dia, DateTime desde, Func<DateTime, bool> esFestivo)
        {
            DateTime inicio = desde.Date;
            // Desde una semana antes: el reparto de la semana pasada pudo desplazarse por festivos hasta esta.
            for (DateTime semana = Lunes(inicio).AddDays(-7); ; semana = semana.AddDays(7))
            {
                DateTime reparto = DiaRepartoDeLaSemana(dia, semana, esFestivo);
                if (reparto >= inicio)
                {
                    return reparto;
                }
            }
        }

        /// <summary>
        /// La fecha de entrega (= día de picking) de una línea: el primer día de reparto que no sea antes de la fecha pedida ni
        /// de hoy; hoy solo si aún no ha llegado la hora de corte.
        /// </summary>
        /// <param name="fecha">La fecha de entrega que pide la línea (hoy o una futura).</param>
        /// <param name="ahora">El instante en que se calcula (con hora).</param>
        /// <param name="horaCorte">Normalmente <see cref="HORA_CORTE"/>. EN PUNTO: a esa hora ya no sale hoy.</param>
        public static DateTime ProximoDiaReparto(DayOfWeek dia, DateTime fecha, DateTime ahora, TimeSpan horaCorte, Func<DateTime, bool> esFestivo)
        {
            DateTime minimo = ahora.TimeOfDay < horaCorte ? ahora.Date : ahora.Date.AddDays(1);
            DateTime desde = fecha.Date > minimo ? fecha.Date : minimo;
            return ProximoDiaReparto(dia, desde, esFestivo);
        }

        /// <summary>¿<paramref name="fecha"/> es un día de reparto (su día, o al que se ha movido por festivo)?</summary>
        public static bool EsDiaDeReparto(DayOfWeek dia, DateTime fecha, Func<DateTime, bool> esFestivo)
        {
            return ProximoDiaReparto(dia, fecha, esFestivo) == fecha.Date;
        }

        /// <summary>«hoy, martes 03/11», «mañana, martes 03/11» o «el martes 03/11».</summary>
        public static string Texto(DateTime dia, DateTime hoy)
        {
            string fecha = $"{dia.ToString("dddd", ES)} {dia:dd/MM}";
            if (dia.Date == hoy.Date)
            {
                return $"hoy, {fecha}";
            }
            if (dia.Date == hoy.Date.AddDays(1))
            {
                return $"mañana, {fecha}";
            }
            return $"el {fecha}";
        }

        /// <summary>«los martes».</summary>
        public static string NombrePlural(DayOfWeek dia)
        {
            string nombre = ES.DateTimeFormat.GetDayName(dia);
            return "los " + (nombre.EndsWith("s", StringComparison.Ordinal) ? nombre : nombre + "s");
        }

        /// <summary>«martes».</summary>
        public static string Nombre(DayOfWeek dia) => ES.DateTimeFormat.GetDayName(dia);

        private static DateTime Lunes(DateTime fecha)
        {
            DateTime dia = fecha.Date;
            return dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7));
        }

        private static DateTime SiguienteLaborableDesde(DateTime dia, Func<DateTime, bool> esFestivo)
        {
            DateTime resultado = dia;
            for (int i = 0; i < MAXIMO_DIAS_SALTO && esFestivo(resultado); i++)
            {
                resultado = resultado.AddDays(1);
            }
            return resultado;
        }
    }
}
