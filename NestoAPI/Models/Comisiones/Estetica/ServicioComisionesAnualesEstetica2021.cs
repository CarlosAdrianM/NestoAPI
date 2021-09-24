using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ServicioComisionesAnualesEstetica2021 : IServicioComisionesAnuales
    {
        const string GENERAL = "General";
        
        private NVEntities db = new NVEntities();

        public ServicioComisionesAnualesEstetica2021()
        {
            Etiquetas = NuevasEtiquetas;
        }
        
        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
            {
                new EtiquetaGeneral(),
                new EtiquetaUnionLaser(),
                new EtiquetaEvaVisnu(),
                new EtiquetaOtrosAparatos()
            };

        // El cálculo de proyecciones de 2019 está perfecto para 2021
        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019();

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return ServicioComisionesAnualesComun.LeerResumenAnno(this, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 30600M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 30600.01M,
                    Hasta = 67320M,
                    Tipo = .02M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 67320.01M,
                    Hasta = 86700M,
                    Tipo = .023M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 86700.01M,
                    Hasta = 102000M,
                    Tipo = .025M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 102000.01M,
                    Hasta = 132600M,
                    Tipo = .027M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 132600.01M,
                    Hasta = 168300M,
                    Tipo = .028M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 168300.01M,
                    Hasta = 197880M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 197880.01M,
                    Hasta = 208590M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 208590.01M,
                    Hasta = 220062.45M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 220062.46M,
                    Hasta = 243007.35M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 243007.36M,
                    Hasta = 263866.35M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 263866.36M,
                    Hasta = 277424.70M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 277424.71M,
                    Hasta = 293068.95M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 293068.96M,
                    Hasta = 320185.65M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 320185.66M,
                    Hasta = 337915.80M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 337915.81M,
                    Hasta = 353560.05M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 353060.06M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosTelefono = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 45000M,
                    Tipo = .0M,
                    TipoExtra = .0M
                },new TramoComision
                {
                    Desde = 45000.01M,
                    Hasta = 96874.18M,
                    Tipo = .006M,
                    TipoExtra = .0005M
                },new TramoComision
                {
                    Desde = 96874.19M,
                    Hasta = 102000.01M,
                    Tipo = .012M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 102000.02M,
                    Hasta = 121325.01M,
                    Tipo = .0145M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 121325.02M,
                    Hasta = 133400.01M,
                    Tipo = .0195M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 133400.02M,
                    Hasta = 145475.01M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 145475.02M,
                    Hasta = 152950.01M,
                    Tipo = .0306M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 152950.02M,
                    Hasta = 161575.01M,
                    Tipo = .033M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 161575.02M,
                    Hasta = 176525.01M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 176525.02M,
                    Hasta = 186300.01M,
                    Tipo = .0370M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 186300.02M,
                    Hasta = 194925.01M,
                    Tipo = .0395M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 194925.02M,
                    Hasta = 220062.45M,
                    Tipo = .042M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 220062.46M,
                    Hasta = 243007.35M,
                    Tipo = .045M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 243007.36M,
                    Hasta = decimal.MaxValue,
                    Tipo = .055M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosMinivendedores = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 5000M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 5000.01M,
                    Hasta = 10000M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 10000.01M,
                    Hasta = 15000M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 15000.01M,
                    Hasta = 20000M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 20000.01M,
                    Hasta = 35000M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 35000.01M,
                    Hasta = 45000M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 45000.01M,
                    Hasta = 55000M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 55000.01M,
                    Hasta = 65000M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 65000.01M,
                    Hasta = 75000M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 75000.01M,
                    Hasta = 85000M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 85000.01M,
                    Hasta = 95000M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 95000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };


            // Aquí usamos cadenas mágicas porque son los que YA han firmado las condiciones
            if (vendedor == "DV" || vendedor == "JE" || vendedor == "ASH" || vendedor == "RFG" || vendedor == "JGP" || vendedor == "MRM")
            {
                return tramosCalle;
            }
            else if (vendedor == "PA" || vendedor == "LA" || vendedor == "MPP" || vendedor == "AGR")
            {
                return tramosTelefono;
            }
            else if (vendedor == "AL" || vendedor == "CAM" || vendedor == "MR" || vendedor == "PI" || vendedor == "SC") 
            {
                return tramosMinivendedores;
            }

            throw new Exception("El vendedor " + vendedor + " no comisiona por este esquema");
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            //Este año no hay tramos mensuales
            return new Collection<TramoComision>();
        }
        
    }
}