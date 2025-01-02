using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesMinivendedores2025 : ComisionesAnualesBase, IComisionesAnuales
    {
        private string[] _familiasEspeciales = { "cursos", "eva visnu", "apraise", "maderas", "max2origin", "mina" };
        
        public ComisionesAnualesMinivendedores2025(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {
            
        }
                
        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaser(_servicio),
            new EtiquetaFamiliasEspeciales(_servicio, _familiasEspeciales),
            new EtiquetaOtrosAparatos(_servicio),
            new EtiquetaClientesNuevos(_servicio),
            new EtiquetaClientesTramosMil(_servicio)
        };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();            

            Collection<TramoComision> tramosMinivendedores = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 5418M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 5418.01M,
                    Hasta = 10836M,
                    Tipo = .0295M,
                    TipoExtra = .016M
                },
                new TramoComision
                {
                    Desde = 10836.01M,
                    Hasta = 16254M,
                    Tipo = .03M,
                    TipoExtra = .0165M
                },
                new TramoComision
                {
                    Desde = 16254.01M,
                    Hasta = 21672M,
                    Tipo = .035M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 21672.01M,
                    Hasta = 37926M,
                    Tipo = .05M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 37926.01M,
                    Hasta = 48762M,
                    Tipo = .055M,
                    TipoExtra = .0185M
                },
                new TramoComision
                {
                    Desde = 48762.01M,
                    Hasta = 59598M,
                    Tipo = .0671M,
                    TipoExtra = .019M
                },
                new TramoComision
                {
                    Desde = 59598.01M,
                    Hasta = 70434M,
                    Tipo = .072M,
                    TipoExtra = .0195M
                },
                new TramoComision
                {
                    Desde = 70434.01M,
                    Hasta = 81270M,
                    Tipo = .0785M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 81270.01M,
                    Hasta = 92106M,
                    Tipo = .08M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 92106.01M,
                    Hasta = 102942M,
                    Tipo = .085M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 102942.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .03M
                }
            };            

            return tramosMinivendedores;
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }

    }
}