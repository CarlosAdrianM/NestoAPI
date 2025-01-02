using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class ComisionesAnualesPeluqueria2025 : ComisionesAnualesBase, IComisionesAnuales
    {
        public ComisionesAnualesPeluqueria2025(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
            
        }
        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaLisap(_servicio),
            new EtiquetaBeox(_servicio),
            new EtiquetaClientesNuevos(_servicio),
            new EtiquetaClientesTramosMil(_servicio)
        };

        // El cálculo de proyecciones de 2019 sigue siendo perfecto para 2020
        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            return new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 56519M,
                    Tipo = .006M,
                    TipoExtra = .01M
                },new TramoComision
                {
                    Desde = 56519.01M,
                    Hasta = 77071M,
                    Tipo = .015M,
                    TipoExtra = .0105M
                },
                new TramoComision
                {
                    Desde = 77071.01M,
                    Hasta = 94198M,
                    Tipo = .022M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 94198.01M,
                    Hasta = 137016M,
                    Tipo = .025M,
                    TipoExtra = .0115M
                },
                new TramoComision
                {
                    Desde = 137016.01M,
                    Hasta = 139092M,
                    Tipo = .03M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 139092.01M,
                    Hasta = 155000M,
                    Tipo = .05M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 150000.01M,
                    Hasta = 173325M,
                    Tipo = .055M,
                    TipoExtra = .0145M
                },
                new TramoComision
                {
                    Desde = 173325.01M,
                    Hasta = 182231M,
                    Tipo = .0621M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 182231.01M,
                    Hasta = 192507M,
                    Tipo = .067M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 192507.01M,
                    Hasta = 252383M,
                    Tipo = .072M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 252383.01M,
                    Hasta = 266359M,
                    Tipo = .075M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 266359.01M,
                    Hasta = 278690M,
                    Tipo = .08M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 278690.01M,
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