﻿using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class ComisionesAnualesPeluqueria2020 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesPeluqueria2020()
            : base(new ServicioComisionesAnualesComun())
        {
            
        }
        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
            {
                new EtiquetaGeneral(new ServicioComisionesAnualesComun()),
                new EtiquetaLisap(new ServicioComisionesAnualesComun()),
                new EtiquetaKach(new ServicioComisionesAnualesComun())
            };

        // El cálculo de proyecciones de 2019 sigue siendo perfecto para 2020
        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            string etiqueta;

            if (linea.Familia != null && linea.Familia.ToLower().Trim() == "lisap")
            {
                etiqueta = "Lisap";
            }
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "kach")
            {
                etiqueta = "Kach";
            }
            else
            {
                etiqueta = "General";
            }
            return etiqueta;
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            //vendedor = vendedor.ToUpper();

            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 54450M,
                    Tipo = .00M,
                    TipoExtra = .00M
                },new TramoComision
                {
                    Desde = 54450.01M,
                    Hasta = 74250M,
                    Tipo = .015M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 74250.01M,
                    Hasta = 90750M,
                    Tipo = .022M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 90750.01M,
                    Hasta = 132000M,
                    Tipo = .025M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 132000.01M,
                    Hasta = 139260M,
                    Tipo = .03M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 139260.01M,
                    Hasta = 150150M,
                    Tipo = .04M,
                    TipoExtra = .013M
                },
                new TramoComision
                {
                    Desde = 150150.01M,
                    Hasta = 166980M,
                    Tipo = .05M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 166980.01M,
                    Hasta = 175560M,
                    Tipo = .0621M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 175560.01M,
                    Hasta = 185460M,
                    Tipo = .067M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 185460.01M,
                    Hasta = 243144M,
                    Tipo = .072M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 243144.01M,
                    Hasta = 256608M,
                    Tipo = .075M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 256608.01M,
                    Hasta = 268488M,
                    Tipo = .08M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 268488.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0875M,
                    TipoExtra = .03M
                }
            };
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }
    }
}