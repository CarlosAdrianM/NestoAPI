using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Jornada
    {
        public Jornada()
        {
            Acciones = new List<Accion>();
            DiasNoLaborables = new List<IDiaNoLaborable>();
        }
        public DateTime Fecha { get; set; }
        public TipoJornada TipoJornada { get; set; }
        public List<Accion> Acciones { get; set; }
        public List<IDiaNoLaborable> DiasNoLaborables { get; set; }
        public TimeSpan JornadaBruta
        {
            get
            {
                TimeSpan jornadaEnAcciones = new TimeSpan(Acciones.Where(a => a.TipoAccionId == 1).Sum(a => a.Duracion.Ticks));
                TimeSpan jornadaEspecial = new TimeSpan(DiasNoLaborables.OfType<JornadasEspeciales>().Where(n => n.SumaEnJornadaTrabajada).Sum(n => n.Duracion.Ticks));
                return jornadaEnAcciones + jornadaEspecial;
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
    }

    public enum TipoJornada
    {
        Laboral,
        Vacaciones,
        Festivo,
        FinDeSemana,
        Baja
    }
}