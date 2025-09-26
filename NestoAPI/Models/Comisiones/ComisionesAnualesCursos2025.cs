using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesCursos2025 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesCursos2025(IServicioComisionesAnuales servicioComisionesVentas)
            : base(servicioComisionesVentas)
        {

        }

        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneralCursos(_servicio),
        };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            Collection<TramoComision> tramosTelefono = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 55000,
                    Tipo = .016M,
                    TipoExtra = 0
                },new TramoComision
                {
                    Desde = 55000.01M,
                    Hasta = 65000,
                    Tipo = .018M,
                    TipoExtra = 0
                },new TramoComision
                {
                    Desde = 65000.01M,
                    Hasta = 75380.49M,
                    Tipo = .02M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 75380.5M,
                    Hasta = 85380.49M,
                    Tipo = .022M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 85380.5M,
                    Hasta = 99888.55M,
                    Tipo = .024M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 99888.56M,
                    Hasta = 109040.21M,
                    Tipo = .026M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 109040.22M,
                    Hasta = 120080.62M,
                    Tipo = .028M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 120080.63M,
                    Hasta = 134838.81M,
                    Tipo = .03M,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 134838.82M,
                    Hasta = decimal.MaxValue,
                    Tipo = .032M,
                    TipoExtra = 0
                }
            };
            return tramosTelefono;
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }
    }
}
