using NestoAPI.Models.Comisiones.Estetica;
using NestoAPI.Models.Comisiones.Estetica.Etiquetas;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesEstetica2025 : ComisionesAnualesBase, IComisionesAnuales
    {
        private readonly string[] _familiasEspeciales = { "cursos", "eva visnu", "apraise", "faby", "maderas", "max2origin", "mina" };
        private readonly string[] _otrasExclusivas = { "anubismed", "anubis", "belclinic", "cazcarra", "cv", "maystar", "oda" };
        public ComisionesAnualesEstetica2025(IServicioComisionesAnuales servicioComisiones)
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
                    Hasta = 33350.94M,
                    Tipo = .006M,
                    TipoExtra = .0105M
                },
                new TramoComision
                {
                    Desde = 33350.95M,
                    Hasta = 45000M,
                    Tipo = .02M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 45000.01M,
                    Hasta = 58000M,
                    Tipo = .0205M,
                    TipoExtra = .0115M
                },
                new TramoComision
                {
                    Desde = 58000.01M,
                    Hasta = 73372.07M,
                    Tipo = .021M,
                    TipoExtra = .012M
                },
                new TramoComision
                {
                    Desde = 73372.08M,
                    Hasta = 94494.33M,
                    Tipo = .023M,
                    TipoExtra = .0125M
                },
                new TramoComision
                {
                    Desde = 94494.34M,
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
            /*
            // Crear los tramos trimestrales con la lógica de Desde = Hasta anterior + 0.01
            Collection<TramoComision> tramosCalleTrimestral = new Collection<TramoComision>();

            decimal hastaAnterior = 0M; // Inicializamos el primer valor de "Hasta" como 0

            foreach (var tramo in tramosCalle)
            {
                // Calculamos el nuevo tramo
                var nuevoTramo = new TramoComision
                {
                    Desde = hastaAnterior == 0M ? 0M : hastaAnterior + 0.01M, // Desde es Hasta anterior + 0.01 (excepto el primer registro)
                    Hasta = tramo.Hasta == decimal.MaxValue
                        ? decimal.MaxValue // Si es decimal.MaxValue, lo dejamos tal cual
                        : Math.Round(tramo.Hasta / 4, 2),  // Si no, lo dividimos por 4
                    Tipo = tramo.Tipo, // No cambiamos el tipo
                    TipoExtra = tramo.TipoExtra // No cambiamos el tipo extra
                };

                // Añadimos el nuevo tramo a la colección
                tramosCalleTrimestral.Add(nuevoTramo);

                // Actualizamos "hastaAnterior" con el valor "Hasta" del nuevo tramo
                hastaAnterior = nuevoTramo.Hasta;
            }
            */

            return tramosCalle;

        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }

    }
}