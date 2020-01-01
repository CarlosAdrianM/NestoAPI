using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class ServicioComisionesAnualesPeluqueria2020 : IServicioComisionesAnuales
    {
        const string GENERAL = "General";

        private NVEntities db = new NVEntities();

        public ServicioComisionesAnualesPeluqueria2020()
        {
            Etiquetas = NuevasEtiquetas;
        }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
            {
                new EtiquetaGeneral(),
                new EtiquetaLisap()
            };

        // El cálculo de proyecciones de 2019 sigue siendo perfecto para 2020
        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019();

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return ServicioComisionesAnualesComun.LeerResumenAnno(this, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            //vendedor = vendedor.ToUpper();

            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 39600M,
                    Tipo = .00M,
                    TipoExtra = .00M
                },new TramoComision
                {
                    Desde = 39600.01M,
                    Hasta = 54000M,
                    Tipo = .015M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 54000.01M,
                    Hasta = 66000M,
                    Tipo = .022M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 66000.01M,
                    Hasta = 96000M,
                    Tipo = .025M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 96000.01M,
                    Hasta = 101280M,
                    Tipo = .03M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 101280.01M,
                    Hasta = 109200M,
                    Tipo = .04M,
                    TipoExtra = .013M
                },
                new TramoComision
                {
                    Desde = 109200.01M,
                    Hasta = 121440M,
                    Tipo = .05M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 121440.01M,
                    Hasta = 127680M,
                    Tipo = .0621M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 127680.01M,
                    Hasta = 134880M,
                    Tipo = .067M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 134880.01M,
                    Hasta = 176832M,
                    Tipo = .072M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 176832.01M,
                    Hasta = 186624M,
                    Tipo = .075M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 186624.01M,
                    Hasta = 195264M,
                    Tipo = .08M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 195264.01M,
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