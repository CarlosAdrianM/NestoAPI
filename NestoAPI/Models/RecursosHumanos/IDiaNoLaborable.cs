using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public interface IDiaNoLaborable
    {
        DateTime Fecha { get; set; }

        // Si es true sumará como más tiempo trabajado, si es false sumará como menos jornada laboral
        bool SumaEnJornadaTrabajada { get; set; }
    }
}