using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class Vacaciones: IDiaNoLaborable
    {
        public DateTime Fecha { get; set; }
        public bool SumaEnJornadaTrabajada { get; set; } = false;
    }
}