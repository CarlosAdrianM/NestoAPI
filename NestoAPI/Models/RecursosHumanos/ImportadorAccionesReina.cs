using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class ImportadorAccionesReina : IImportadorAccionesAdapter
    {
        public void Ejecutar(GestorAcciones gestorAcciones, string datosEntrada)
        {
            const Char delimiter = ',';
            gestorAcciones.Acciones = new List<Accion>();
            string aLine = null;
            StringReader strReader = new StringReader(datosEntrada);
            Accion accion = new Accion();
            string tipoAnterior = "";
            
            
            
            while (true)
            {
                aLine = strReader.ReadLine();
                if (aLine != null)
                {
                    String[] datos = aLine.Split(delimiter);
                    if (tipoAnterior != datos[(int)DatosAcciones.TipoAccion] && accion != null)
                    {
                        accion = new Accion();
                        gestorAcciones.Acciones.Add(accion);
                    } else
                    {
                        DateTime horaFinal = Convert.ToDateTime(datos[(int)DatosAcciones.FechaHora]);
                        accion.HoraFin = horaFinal.TimeOfDay;
                    }
                    
                    tipoAnterior = datos[3];
                }
                else
                {
                    break;
                }
            }
            
        }

        private enum DatosAcciones
        {
            EmpleadoId,
            FechaHora,
            Grupo,
            TipoAccion
        }

        private enum EmpleadosReloj
        {
            Uno = 1,
            Dos,
            Paloma,
            LauraCamacho,
            Pilar,
            Sandra
        }
    }
}