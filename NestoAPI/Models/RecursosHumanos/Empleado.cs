using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Empleado
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
        public int HorasSemana { get; set; }
        public TipoFestivo FiestasLocales { get; set; }
        public string Usuario { get; set; }
        public List<Accion> Acciones { get; set; }
        public TimeSpan JornadaBruta
        {
            get
            {
                return new TimeSpan(Acciones.Where(a => a.TipoAccionId == 1).Sum(a => a.Duracion.Ticks));
            }
        }

        public TimeSpan JornadaPausada
        {
            get
            {
                List<TipoAccion> listaAcciones = GestorTiposAccion.TiposAccion().Where(t => !t.EsJornadaLaboral).ToList();
                List<int> listaAccionesId = listaAcciones.Select(a => a.Id).ToList();
                return new TimeSpan(Acciones.Where(a => listaAccionesId.Contains(a.TipoAccionId)).Sum(a => a.Duracion.Ticks));
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
                DateTime fechaInicio = Acciones.Min(a => a.Fecha);
                DateTime fechaFinal = Acciones.Max(a => a.Fecha);
                CultureInfo cul = CultureInfo.CurrentCulture;
                Calendar cal = cul.Calendar;
                int semanaInicial = cal.GetWeekOfYear(fechaInicio, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                int semanaFinal = cal.GetWeekOfYear(fechaFinal, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
                int numeroSemanas = semanaFinal - semanaInicial + 1;
                int numeroHorasTotal = numeroSemanas * HorasSemana;

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
                return (decimal)estimados / totales;
            }
        }


    }
}