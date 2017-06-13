using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class ImportadorAccionesAlgete : IImportadorAccionesAdapter
    {
        public void Ejecutar(GestorAcciones gestorAcciones, string datosEntrada)
        {
            const Char delimiter = ',';
            gestorAcciones.Acciones = new List<Accion>();
            string aLine = null;
            StringReader strReader = new StringReader(datosEntrada);
            Accion accion = new Accion();
            int tipoAnterior = int.MinValue;
            DateTime fechaAnterior = DateTime.MinValue;
            int empleadoAnterior = int.MinValue;
            Dictionary<string, int> diccionarioAcciones = new Dictionary<string, int>();
            cargarDiccionario(diccionarioAcciones);
            string tipoJornadaReloj = "I";

            List<AccionReloj> accionesReloj = new List<AccionReloj>();

            while (true)
            {
                aLine = strReader.ReadLine();
                if (aLine != null)
                {
                    String[] datos = aLine.Split(delimiter);
                    if (datos.Length != 4)
                    {
                        throw new Exception("No aparece alguno de los campos en el registro del reloj: " + datos.Length.ToString());
                    }

                    AccionReloj accionReloj = new AccionReloj()
                    {
                        Empleado = datos[0].Trim(),
                        Fecha = datos[1].Trim(),
                        Grupo = datos[2].Trim(),
                        TipoAccion = datos[3].Trim()
                    };
                    accionesReloj.Add(accionReloj);
                }
                else
                {
                    break;
                }
            }

            // Ordenamos para recorrerlo
            accionesReloj = accionesReloj.OrderBy(a => a.Empleado).ThenBy(a => a.Fecha).ToList();

            foreach(AccionReloj accionImportando in accionesReloj)
            {
                DateTime fechaAccion = Convert.ToDateTime(accionImportando.Fecha);
                int tipoAccion = diccionarioAcciones[accionImportando.TipoAccion];
                int empleadoAccion = this.calculaEmpleadoId(accionImportando.Empleado);
                
                if (!(tipoAccion == diccionarioAcciones[tipoJornadaReloj] && existeRegistro(gestorAcciones, empleadoAccion, fechaAccion.Date, diccionarioAcciones[tipoJornadaReloj])) && (fechaAnterior != fechaAccion.Date || tipoAnterior != tipoAccion || empleadoAnterior != empleadoAccion))
                {
                    if (tipoAccion != diccionarioAcciones[tipoJornadaReloj])
                    {
                        if (!existeRegistro(gestorAcciones, empleadoAccion, fechaAccion.Date, diccionarioAcciones[tipoJornadaReloj]))
                        {
                            accion = new Accion();
                            accion.EmpleadoId = empleadoAccion;
                            accion.Fecha = fechaAccion.Date;
                            accion.HoraInicio = fechaAccion.TimeOfDay;
                            accion.TipoAccionId = diccionarioAcciones[tipoJornadaReloj];
                            gestorAcciones.Acciones.Add(accion);
                        }
                    }

                    accion = new Accion();
                    accion.EmpleadoId = empleadoAccion;
                    accion.Fecha = fechaAccion.Date;
                    accion.HoraInicio = fechaAccion.TimeOfDay;
                    accion.TipoAccionId = tipoAccion;
                    gestorAcciones.Acciones.Add(accion);

                    tipoAnterior = tipoAccion;
                    fechaAnterior = fechaAccion.Date;
                    empleadoAnterior = empleadoAccion;
                }
                else if (tipoAccion == diccionarioAcciones[tipoJornadaReloj] && existeRegistro(gestorAcciones, empleadoAccion, fechaAccion.Date, diccionarioAcciones[tipoJornadaReloj]))
                {
                    accion = gestorAcciones.Acciones.Where(a => a.EmpleadoId == empleadoAccion && a.Fecha == fechaAccion.Date && a.TipoAccionId == diccionarioAcciones[tipoJornadaReloj]).SingleOrDefault();
                    accion.HoraFin = fechaAccion.TimeOfDay;
                }
                else
                {
                    accion.HoraFin = fechaAccion.TimeOfDay;
                    tipoAnterior = int.MinValue;
                    empleadoAnterior = int.MinValue;
                }
            }
        }
        
        private bool existeRegistro(GestorAcciones gestor, int empleadoId, DateTime fecha, int tipoJornada)
        {
            return gestor.Acciones.Where(a =>a.EmpleadoId == empleadoId && a.Fecha == fecha && a.TipoAccionId == tipoJornada).FirstOrDefault() != null;
        }
        
        private enum AccionesReloj
        {
            Entrada = 0,        // I
            Salida,             // O
            Comida,             // 0
            Medico,             // 3
            Tabaco,             // 4
            Cafe,               // 5
            GestionesLaborales, // 6
            AsuntosPersonales   // 7
        }
        private void cargarDiccionario(Dictionary<string, int> diccionario)
        {
            diccionario.Add("I", 1); // Jornada
            diccionario.Add("O", 1); // Jornada
            diccionario.Add("0", 2); // Comida
            diccionario.Add("3", 6); // Medico
            diccionario.Add("4", 3); // Tabaco Cafe
            diccionario.Add("5", 3); // Tabaco Cafe
            diccionario.Add("6", 5); // Gestiones Laborales
            diccionario.Add("7", 4); // Asuntos Personales
        }

        private enum DatosAcciones
        {
            Empleado,
            FechaHora,
            Grupo,
            TipoAccion
        }
        private enum EmpleadosReloj
        {
            Manuel = 1,
            Carlos,
            Tres,
            Alfredo,
            Inaki,
            Andreii,
            Siete,
            LauraVillacieros,
            Carolina,
            Diez,
            Silvia,
            Maria,
            Trece,
            Antonio,
            Quince,
            Marta,
            Carmen
        }
        private int calculaEmpleadoId(string empleadoReloj)
        {
            // Esto hay que desarrollarlo para que lea el empleado de la tabla Empleados
            // No tiene por qué coincidir el Id del reloj, con el de la tabla
            // Un dicionario entre el empleado del reloj y el de Nesto sería lo perfecto
            return Int32.Parse(empleadoReloj);
        }

        private class AccionReloj
        {
            public string Empleado { get; set; }
            public string Fecha { get; set; }
            public string Grupo { get; set; }
            public string TipoAccion { get; set; }
        }
    }
}