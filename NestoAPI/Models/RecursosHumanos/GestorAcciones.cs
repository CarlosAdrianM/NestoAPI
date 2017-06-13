using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorAcciones
    {
        public List<Accion> Acciones { get; set; }
        public void Importar(string datosEntrada)
        {
            IImportadorAccionesAdapter importador = new ImportadorAccionesAlgete();
            importador.Ejecutar(this, datosEntrada);
        }
    }
}