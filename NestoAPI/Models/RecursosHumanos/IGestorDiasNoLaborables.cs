using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public interface IGestorDiasNoLaborables
    {
        void Rellenar(DateTime fechaDesde, DateTime fechaHasta);
    }
}