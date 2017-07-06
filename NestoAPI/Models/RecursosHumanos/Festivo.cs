using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Festivo :IDiaNoLaborable
    {
        public DateTime Fecha { get; set; }
        public TipoFestivo TipoFestivo { get; set; }
        public string Fiesta { get; set; }
        public bool SumaEnJornadaTrabajada { get; set; } = false;
    }
}