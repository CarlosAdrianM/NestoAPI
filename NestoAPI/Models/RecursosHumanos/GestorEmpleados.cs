using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public class GestorEmpleados
    {
        private GestorAcciones gestorAcciones;
        private GestorFestivos gestorFestivos;

        public List<Empleado> listaEmpleados;
        public GestorEmpleados(GestorAcciones gestorAcciones, GestorFestivos gestorFestivos)
        {
            this.gestorAcciones = gestorAcciones;
            this.gestorFestivos = gestorFestivos;
        }

        public void RellenarEmpleados(List<Empleado> listaEmpleados)
        {
            this.listaEmpleados = listaEmpleados;

            foreach(Empleado empleado in listaEmpleados)
            {
                empleado.Acciones = gestorAcciones.Acciones?.Where(a => a.EmpleadoId == empleado.Id).ToList();
                empleado.Festivos = gestorFestivos.ListaFestivos.Where(f => f.TipoFestivo == empleado.FiestasLocales || f.TipoFestivo == TipoFestivo.Nacional).ToList();
            }
        }

        public void Verificar()
        {
            foreach (Empleado empleado in listaEmpleados)
            {
                foreach (Accion accion in empleado.Acciones)
                {
                    if (accion.Duracion.Ticks > 0)
                    {
                        accion.Estado = (int)EstadosVerificacion.Verificado;
                    }
                    else
                    {
                        accion.Estado = (int)EstadosVerificacion.Estimado;
                        accion.HoraFin = accion.HoraInicio + empleado.DuracionEstimada(accion.TipoAccionId);
                    }
                }
            }
        }
        
        public void RellenarJornadas()
        {
            
            if (gestorAcciones.Acciones.Count == 0)
            {
                return;
            }
            
            DateTime fechaInicio = gestorAcciones.Acciones.Min(a => a.Fecha).Date;
            DateTime fechaFinal = gestorAcciones.Acciones.Max(a => a.Fecha).Date;

            Empleado.AjustarFechasSemana(ref fechaInicio, ref fechaFinal);

            foreach (Empleado empleado in listaEmpleados)
            {
                empleado.RellenarJornadas(fechaInicio, fechaFinal);
            }

        }

        public enum EstadosVerificacion {
            Inicial,
            Verificado,
            Estimado,
            Anulado
        }


    }
}