using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    // Esto tendría que ser una Factory con todos los IGestorDiasNoLaborables
    // De momento lo usaremos solo para las vacaciones
    // Aunque ahora mismo no se usa para nada.
    public class GestorDiasNoLaborables
    {
        public List<Festivo> ListaGestores { get; set; }
        public void Rellenar()
        {

        }
        public List<IDiaNoLaborable> DiasNoLaborables(Empleado empleado)
        {
            return new List<IDiaNoLaborable>();
        }
    }
    
}