using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class ImportadorAccionesAlgete : IImportadorAccionesAdapter
    {
        Dictionary<string, int> diccionarioAcciones;

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
            diccionarioAcciones = new Dictionary<string, int>();
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

            for(int i = 0; i < accionesReloj.Count; i++)
            {
                AccionReloj accionImportando = accionesReloj[i];
                DateTime fechaAccion = Convert.ToDateTime(accionImportando.Fecha);
                int tipoAccion = diccionarioAcciones[accionImportando.TipoAccion];
                int empleadoAccion = this.calculaEmpleadoId(accionImportando.Empleado);

                // Si la accion se anula inmediatamente no apuntamos ninguna de las dos
                if (!accionImportando.Importar)
                {
                    continue;
                }

                if (!(tipoAccion == diccionarioAcciones[tipoJornadaReloj] && existeRegistro(gestorAcciones, empleadoAccion, fechaAccion.Date, diccionarioAcciones[tipoJornadaReloj])) && (fechaAnterior != fechaAccion.Date || tipoAnterior != tipoAccion || empleadoAnterior != empleadoAccion))
                {
                    if (seAnulaDeInmediato(accionesReloj, i, accionImportando))
                    {
                        // Precondicion: hay al menos un movimiento más porque en cc !seAnulaDeInmediadto
                        accionesReloj[i + 1].Importar = false;
                        continue;
                    }
                    
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

        private bool seAnulaDeInmediato(List<AccionReloj> accionesReloj, int i, AccionReloj accionImportando)
        {
            // Si es el último movimiento no puede anularlo otro
            if (accionesReloj.Count <= i+1)
            {
                return false;
            }
            AccionReloj accionSiguiente = accionesReloj[i + 1];
            if (accionSiguiente.TipoAccion != accionImportando.TipoAccion)
            {
                return false;
            }
            DateTime fechaRegistroActual = Convert.ToDateTime(accionImportando.Fecha);
            DateTime fechaRegistroSiguiente = Convert.ToDateTime(accionSiguiente.Fecha);

            // Si el siguiente movimiento es más de 30 segundos después, no es que haya marcado dos veces sin querer
            if ((fechaRegistroSiguiente - fechaRegistroActual).TotalSeconds > 30) {
                return false;
            }
            
            // Si el numero de movimientos es impar, borramos uno de los dos que está seguido
            // Si es par, borramos los dos
            var accionesDelMismoTipo = accionesReloj.Where(a => a.Empleado == accionImportando.Empleado && Convert.ToDateTime(a.Fecha).Date == Convert.ToDateTime(accionImportando.Fecha).Date && a.TipoAccion == accionImportando.TipoAccion);
            int numeroAccionesDelMismoTipo = accionesDelMismoTipo.Count();
            if (numeroAccionesDelMismoTipo % 2 != 0 || accionImportando.TipoAccion == "I" || accionImportando.TipoAccion == "O")
            {
                accionSiguiente.Importar = false;
                return false;
            }

            return true;
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
            Santiago,
            Alfredo,
            Inaki,
            Andreii,
            LauraAlonso,
            LauraVillacieros,
            Carolina,
            Aida,
            Silvia,
            Maria,
            Trece,
            Antonio,
            Quince,
            Marta,
            Carmen,
            Pedro,
            Christian
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
            public bool Importar { get; set; } = true;
        }
    }
}