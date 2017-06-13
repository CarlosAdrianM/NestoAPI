using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorEmpleados
    {
        private GestorAcciones gestorAcciones;
        public List<Empleado> listaEmpleados;
        public GestorEmpleados(GestorAcciones gestorAcciones)
        {
            this.gestorAcciones = gestorAcciones;
        }

        public void RellenarEmpleados(List<Empleado> listaEmpleados)
        {
            this.listaEmpleados = listaEmpleados;

            foreach(Empleado empleado in listaEmpleados)
            {
                empleado.Acciones = gestorAcciones.Acciones?.Where(a => a.EmpleadoId == empleado.Id).ToList();
            }
        }

        public void Verificar()
        {
            foreach (Empleado empleado in listaEmpleados)
            {
                foreach(Accion accion in empleado.Acciones) {
                    if (accion.Duracion.Ticks > 0)
                    {
                        accion.Estado = (int)EstadosVerificacion.Verificado; 
                    } else
                    {
                        accion.Estado = (int)EstadosVerificacion.Estimado;
                        accion.HoraFin = accion.HoraInicio + empleado.DuracionEstimada(accion.TipoAccionId);
                    }
                    
                }

            }
        }

        private enum EstadosVerificacion {
            Inicial,
            Verificado,
            Estimado
        }


    }
}