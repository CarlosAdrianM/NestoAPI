using NestoAPI.Models.Comisiones.Estetica;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class ServicioComisionesAnualesPeluqueria2019 : IServicioComisionesAnuales
    {
        const string GENERAL = "General";

        private NVEntities db = new NVEntities();

        public ServicioComisionesAnualesPeluqueria2019()
        {
            Etiquetas = NuevasEtiquetas;
        }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
            {
                new EtiquetaGeneral(),
                new EtiquetaLisap()
            };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019();

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return ServicioComisionesAnualesComun.LeerResumenAnno(this, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();

            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 33000M,
                    Tipo = .00M,
                    TipoExtra = .00M
                },new TramoComision
                {
                    Desde = 33000.01M,
                    Hasta = 45000M,
                    Tipo = .015M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 45000.01M,
                    Hasta = 55000M,
                    Tipo = .022M,
                    TipoExtra = .01M
                },
                new TramoComision
                {
                    Desde = 55000.01M,
                    Hasta = 80000M,
                    Tipo = .025M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 80000.01M,
                    Hasta = 84400M,
                    Tipo = .03M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 84400.01M,
                    Hasta = 91000M,
                    Tipo = .04M,
                    TipoExtra = .013M
                },
                new TramoComision
                {
                    Desde = 91000.01M,
                    Hasta = 101200M,
                    Tipo = .05M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 101200.01M,
                    Hasta = 106400M,
                    Tipo = .0621M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 106400.01M,
                    Hasta = 112400M,
                    Tipo = .067M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 112400.01M,
                    Hasta = 147360M,
                    Tipo = .072M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 147360.01M,
                    Hasta = 155520M,
                    Tipo = .075M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 155520.01M,
                    Hasta = 162720M,
                    Tipo = .08M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 162720.01M,
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