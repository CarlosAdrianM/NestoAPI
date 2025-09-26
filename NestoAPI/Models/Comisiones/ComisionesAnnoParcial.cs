using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnnoParcial : IComisionesAnuales
    {
        private readonly IComisionesAnuales _comisionesBase;
        private readonly int _mesesTrabajados;

        public ComisionesAnnoParcial(IComisionesAnuales comisionesBase, int mesesTrabajados)
        {
            _comisionesBase = comisionesBase;
            _mesesTrabajados = mesesTrabajados;
        }

        // Delegamos todo a la implementación base excepto los tramos
        public ICollection<IEtiquetaComision> NuevasEtiquetas => _comisionesBase.NuevasEtiquetas;
        public ICollection<IEtiquetaComision> Etiquetas => _comisionesBase.Etiquetas;
        public ICalculadorProyecciones CalculadorProyecciones => _comisionesBase.CalculadorProyecciones;

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            return _comisionesBase.EtiquetaLinea(linea);
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return _comisionesBase.LeerResumenAnno(vendedor, anno);
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno, bool todoElEquipo = false)
        {
            return _comisionesBase.LeerResumenAnno(vendedor, anno, todoElEquipo);
        }

        // Aquí está la magia: recalculamos los tramos según los meses trabajados
        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            var tramosOriginales = _comisionesBase.LeerTramosComisionAnno(vendedor);
            return RecalcularTramosPorMeses(tramosOriginales, _mesesTrabajados);
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            return _comisionesBase.LeerTramosComisionMes(vendedor);
        }

        private ICollection<TramoComision> RecalcularTramosPorMeses(ICollection<TramoComision> tramosOriginales, int meses)
        {
            Collection<TramoComision> tramosParciales = new Collection<TramoComision>();
            decimal hastaAnterior = 0M;

            foreach (var tramo in tramosOriginales)
            {
                var nuevoTramo = new TramoComision
                {
                    Desde = hastaAnterior == 0M ? 0M : hastaAnterior + 0.01M,
                    Hasta = tramo.Hasta == decimal.MaxValue
                        ? decimal.MaxValue
                        : Math.Round(tramo.Hasta * meses / 12, 2), // Proporcional a los meses
                    Tipo = tramo.Tipo, // Los porcentajes se mantienen
                    TipoExtra = tramo.TipoExtra
                };

                tramosParciales.Add(nuevoTramo);
                hastaAnterior = nuevoTramo.Hasta;
            }

            return tramosParciales;
        }
    }
}
