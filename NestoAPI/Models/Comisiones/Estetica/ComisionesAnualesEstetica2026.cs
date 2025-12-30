using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesEstetica2026 : ComisionesAnualesBase, IComisionesAnuales
    {
        private readonly string[] _familiasEspeciales = { "cursos", "eva visnu", "apraise", "faby", "maderas", "max2origin", "mina" };
        private readonly string[] _otrasExclusivas = { "anubismed", "anubis", "belclinic", "cazcarra", "cv", "maystar", "oda" };
        public ComisionesAnualesEstetica2026(IServicioComisionesAnuales servicioComisiones)
            : base(servicioComisiones)
        {

        }

        public override ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(_servicio),
            new EtiquetaUnionLaser(_servicio),
            new EtiquetaFamiliasEspeciales(_servicio, _familiasEspeciales),
            new EtiquetaOtrasExclusivas(_servicio, _otrasExclusivas),
            new EtiquetaOtrosAparatos(_servicio),
            new EtiquetaClientesNuevos(_servicio),
            new EtiquetaClientesTramosMil(_servicio)
        };

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019(this);

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            _ = vendedor.ToUpper();
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {                
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 111169.80M,
                    Tipo = .025M,
                    TipoExtra = .013M
                },
                new TramoComision
                {
                    Desde = 111169.81M,
                    Hasta = 127250M,
                    Tipo = .027M,
                    TipoExtra = .0135M
                },
                new TramoComision
                {
                    Desde = 127250.01M,
                    Hasta = 144520.74M,
                    Tipo = .0275M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 144520.75M,
                    Hasta = 162500M,
                    Tipo = .028M,
                    TipoExtra = .0145M
                },
                new TramoComision
                {
                    Desde = 162500.01M,
                    Hasta = 183430.17M,
                    Tipo = .0285M,
                    TipoExtra = .015M
                },
                new TramoComision
                {
                    Desde = 183430.18M,
                    Hasta = 199000M,
                    Tipo = .029M,
                    TipoExtra = .0155M
                },
                new TramoComision
                {
                    Desde = 199000.01M,
                    Hasta = 215669.41M,
                    Tipo = .0295M,
                    TipoExtra = .016M
                },
                new TramoComision
                {
                    Desde = 215669.42M,
                    Hasta = 227342.24M,
                    Tipo = .03M,
                    TipoExtra = .0165M
                },
                new TramoComision
                {
                    Desde = 227342.25M,
                    Hasta = 239846.06M,
                    Tipo = .035M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 239846.07M,
                    Hasta = 250000M,
                    Tipo = .045M,
                    TipoExtra = .0175M
                },
                new TramoComision
                {
                    Desde = 250000.01M,
                    Hasta = 264853.71M,
                    Tipo = .05M,
                    TipoExtra = .018M
                },
                new TramoComision
                {
                    Desde = 264853.72M,
                    Hasta = 287587.93M,
                    Tipo = .055M,
                    TipoExtra = .0185M
                },
                new TramoComision
                {
                    Desde = 287587.94M,
                    Hasta = 302365.18M,
                    Tipo = .0671M,
                    TipoExtra = .019M
                },
                new TramoComision
                {
                    Desde = 302365.19M,
                    Hasta = 319415.85M,
                    Tipo = .072M,
                    TipoExtra = .0195M
                },
                new TramoComision
                {
                    Desde = 319415.86M,
                    Hasta = 330000M,
                    Tipo = .077M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 330000.01M,
                    Hasta = 348970.34M,
                    Tipo = .0785M,
                    TipoExtra = .021M
                },
                new TramoComision
                {
                    Desde = 348970.35M,
                    Hasta = 368294.43M,
                    Tipo = .08M,
                    TipoExtra = .024M
                },
                new TramoComision
                {
                    Desde = 368294.44M,
                    Hasta = 385345.10M,
                    Tipo = .085M,
                    TipoExtra = .027M
                },
                new TramoComision
                {
                    Desde = 385345.11M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .03M
                }
            };

            return tramosCalle;

        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }

    }
}
