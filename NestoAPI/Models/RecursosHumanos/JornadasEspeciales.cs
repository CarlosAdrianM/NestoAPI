using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class JornadasEspeciales: IDiaNoLaborable
    {
        public DateTime Fecha { get; set; }
        public string Motivo { get; set; }
        public TimeSpan Duracion { get; set; }
        public bool SumaEnJornadaTrabajada { get; set; } = true;
    }
}