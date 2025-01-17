﻿using NestoAPI.Models.Comisiones.Estetica;
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
        public decimal GeneralInicioTramo { get; set; }
        public decimal GeneralFinalTramo { get; set; }
        public bool GeneralBajaSaltoMesSiguiente { get; set; }

        public decimal GeneralProyeccion { get; set; }
        public decimal GeneralVentaAcumulada { get; set; }
        public decimal GeneralComisionAcumulada { get; set; }
        public decimal GeneralTipoConseguido { get; set; }
        public decimal GeneralTipoReal {
            get 
            {
                if (GeneralVentaAcumulada == 0)
                {
                    return 0;
                }
                return Math.Round(GeneralComisionAcumulada / GeneralVentaAcumulada, 4, MidpointRounding.AwayFromZero);
            }
        }

        public decimal TotalComisiones {
            get
            {
                return Math.Round(Etiquetas.Sum(e => e.Comision), 2);
            }
        }

        public decimal TotalVentaAcumulada { get; set; }
        public decimal TotalComisionAcumulada { get; set; }
        public decimal TotalTipoAcumulado => TotalVentaAcumulada == 0 ? 0 : Math.Round(TotalComisionAcumulada / TotalVentaAcumulada, 4, MidpointRounding.AwayFromZero);
    }
}