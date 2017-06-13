using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Accion
    {
        public int Id { get; set; }
        public int EmpleadoId { get; set; }
        public int TipoAccionId { get; set; }
        public DateTime Fecha { get; set; } //solo fecha sin hora: Fecha.Date()
        public TimeSpan HoraInicio { get; set; }
        public TimeSpan HoraFin { get; set; }
        public int Estado { get; set; }

        public TimeSpan Duracion {
            get
            {
                return HoraFin - HoraInicio;
            }
        }
    }
    
}