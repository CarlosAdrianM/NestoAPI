using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Empleado
    {
        public static void AjustarFechasSemana(ref DateTime fechaInicio, ref DateTime fechaFinal)
        {
            if (fechaInicio.DayOfWeek != DayOfWeek.Monday)
            {
                int diasRestar = fechaInicio.DayOfWeek == DayOfWeek.Sunday ? 6 : fechaInicio.DayOfWeek - DayOfWeek.Monday;
                fechaInicio = fechaInicio.AddDays(-diasRestar);
            }

            if (fechaFinal.DayOfWeek != DayOfWeek.Sunday)
            {
                int diasSumar = 7 - (int)fechaFinal.DayOfWeek;
                fechaFinal = fechaFinal.AddDays(diasSumar);
            }
        }

        public Empleado()
        {
            Acciones = new List<Accion>();
            Festivos = new List<Festivo>();
            Jornadas = new List<Jornada>();
            DiasNoLaborables = new List<IDiaNoLaborable>();
        }
        public int Id { get; set; }
        public string Nombre { get; set; }
        public int HorasSemana { get; set; }
        public TipoFestivo FiestasLocales { get; set; }
        public string Usuario { get; set; }
        public List<Jornada> Jornadas { get; set; }
        public List<Accion> Acciones { get; set; }
        public List<Festivo> Festivos { get; set; }
        //public List<Vacaciones> Vacaciones { get; set; }
        public List<IDiaNoLaborable> DiasNoLaborables { get; set; }
        public TimeSpan JornadaBruta
        {
            get
            {
                return new TimeSpan(Jornadas.Sum(j => j.JornadaBruta.Ticks));
            }
        }
        public TimeSpan JornadaPausada
        {
            get
            {
                return new TimeSpan(Jornadas.Sum(j => j.JornadaPausada.Ticks));
            }
        }
        public TimeSpan JornadaNeta
        {
            get
            {
                return JornadaBruta - JornadaPausada;
            }
        }
        public TimeSpan JornadaLaboral
        {
            get
            {
                if (Acciones.Count == 0)
                {
                    return TimeSpan.Zero;
                }
                DateTime fechaInicio = Acciones.Min(a => a.Fecha).Date;
                DateTime fechaFinal = Acciones.Max(a => a.Fecha).Date;
                CultureInfo cul = CultureInfo.CurrentCulture;
                Calendar cal = cul.Calendar;
                int semanaInicial = cal.GetWeekOfYear(fechaInicio, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                int semanaFinal = cal.GetWeekOfYear(fechaFinal, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                int numeroSemanas = semanaFinal - semanaInicial + 1;
                int numeroHorasTotal = numeroSemanas * HorasSemana;

                Empleado.AjustarFechasSemana(ref fechaInicio, ref fechaFinal);
                
                int numeroFestivos = Festivos.Where(f => f.Fecha >= fechaInicio && f.Fecha <= fechaFinal).Count();
                int numeroNoLaborables = DiasNoLaborables.Where(n => !n.SumaEnJornadaTrabajada && n.Fecha >= fechaInicio && n.Fecha <= fechaFinal).Count();
                numeroHorasTotal -= (numeroFestivos+numeroNoLaborables) * HorasDia;

                return new TimeSpan(numeroHorasTotal, 0, 0);
            }
        }
        public TimeSpan SaldoJornada
        {
            get
            {   
                return JornadaNeta - JornadaLaboral;
            }
        }
        public TimeSpan DuracionEstimada(int tipoAccionId)
        {
            List<Accion> listaPrevias = Acciones.Where(a => a.TipoAccionId == tipoAccionId && a.Duracion.Ticks > 0).ToList();
            if (listaPrevias.Count == 0)
            {
                return DateTime.MinValue.TimeOfDay;
            }
            double media = listaPrevias.Average(a => a.Duracion.Ticks);
            long mediaTicks = (long)media;
            return new TimeSpan(mediaTicks);
        }
        public decimal RatioEstimacion
        {
            get
            {
                int estimados = Acciones.Where(a => a.Estado == 2).Count();
                int totales = Acciones.Count();
                return totales == 0 ? 0 : (decimal)estimados / totales;
            }
        }
        public int HorasDia
        {
            // Esto quizá debiera contemplar que los viernes el horario es diferente en algunos empleados
            get
            {
                return HorasSemana / 5;
            }
        }
        public void RellenarJornadas(DateTime fechaInicial, DateTime fechaFinal)
        {
            for (DateTime fecha = fechaInicial; fecha <= fechaFinal; fecha = fecha.AddDays(1))
            {
                Jornada jornada = new Jornada()
                {
                    Fecha = fecha.Date,
                    Acciones = this.Acciones.Where(a => a.Fecha == fecha).ToList(),
                    DiasNoLaborables = this.DiasNoLaborables.Where(d => d.Fecha == fecha).ToList()
                };
                if (jornada.Acciones.Count != 0)
                {
                    jornada.TipoJornada = TipoJornada.Laboral;
                } else if (jornada.Fecha.DayOfWeek == DayOfWeek.Saturday || jornada.Fecha.DayOfWeek == DayOfWeek.Sunday)
                {
                    jornada.TipoJornada = TipoJornada.FinDeSemana;
                } else if (Festivos.Where(f => f.Fecha == jornada.Fecha).SingleOrDefault() != null)
                {
                    jornada.TipoJornada = TipoJornada.Festivo;
                }

                Jornadas.Add(jornada);
            }
        }
    }
}