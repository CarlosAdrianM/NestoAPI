using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.RecursosHumanos
{
    interface IImportadorAccionesAdapter
    {
        void Ejecutar(GestorAcciones gestorAcciones, string datosEntrada);
    }
}
