using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class ComisionesAnualesTelefono2025 : ComisionesAnualesBase, IComisionesAnuales
    {
        private string[] _familiasEspeciales = { "cursos", "eva visnu", "apraise", "diagmyskin", "maderas", "max2origin", "mina" };
        public ComisionesAnualesTelefono2025(IServicioComisionesAnuales servicioComisionesVentas) 
            : base(servicioComisionesVentas)
        {
            
        }

        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaser(_servicio),
            new EtiquetaFamiliasEspeciales(_servicio, _familiasEspeciales),
            new EtiquetaOtrosAparatos(_servicio)
        };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public virtual ICollection<TramoComision> LeerTramosBase()
        {
            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 50614.96M,
                    Tipo = .003M,
                    TipoExtra = .0002M
                },new TramoComision
                {
                    Desde = 50614.97M,
                    Hasta = 108961.83M,
                    Tipo = .006M,
                    TipoExtra = .0005M
                },new TramoComision
                {
                    Desde = 108961.84M,
                    Hasta = 114727.25M,
                    Tipo = .012M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 114727.26M,
                    Hasta = 136463.55M,
                    Tipo = .0145M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 136463.56M,
                    Hasta = 150045.24M,
                    Tipo = .0195M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 150045.25M,
                    Hasta = 163626.91M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 163626.92M,
                    Hasta = 172034.62M,
                    Tipo = .0306M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 172034.63M,
                    Hasta = 181735.82M,
                    Tipo = .033M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 181735.83M,
                    Hasta = 198551.23M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 198551.24M,
                    Hasta = 209545.92M,
                    Tipo = .0370M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 209545.93M,
                    Hasta = 219247.12M,
                    Tipo = .0395M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 219247.13M,
                    Hasta = 247521.14M,
                    Tipo = .042M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 247521.15M,
                    Hasta = 273329.03M,
                    Tipo = .045M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 273329.04M,
                    Hasta = 296790.75M,
                    Tipo = .055M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 296790.76M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0671M,
                    TipoExtra = .02M
                }
            };
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            return LeerTramosBase();
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }

    }
}