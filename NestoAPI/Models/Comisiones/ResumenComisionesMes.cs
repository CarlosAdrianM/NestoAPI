using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ResumenComisionesMes
    {
        public ResumenComisionesMes()
        {
            Etiquetas = new List<IEtiquetaComision>();
        }

        public string Vendedor { get; set; }
        
        public int Anno { get; set; }
        public int Mes { get; set; }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public decimal GeneralFaltaParaSalto { get; set; }
        public decimal GeneralProyeccion { get; set; }

        public decimal TotalComisiones {
            get
            {
                return Math.Round(Etiquetas.Sum(e => e.Comision), 2);
            }
        }
    }
}