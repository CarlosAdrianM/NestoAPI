using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesEstetica2024 : IComisionesAnuales
    {
        public ComisionesAnualesEstetica2024(ICalculadorProyecciones _calculadorProyecciones)
        {
            Etiquetas = NuevasEtiquetas;
            CalculadorProyecciones = _calculadorProyecciones;
        }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }

        public ICollection<IEtiquetaComision> NuevasEtiquetas => new Collection<IEtiquetaComision>
        {
            new EtiquetaGeneral(),
            new EtiquetaUnionLaser(),
            new EtiquetaFamiliasEspeciales(),
            new EtiquetaOtrosAparatos()
        };

        public ICalculadorProyecciones CalculadorProyecciones { get; set; }
        public IServicioComisionesAnuales ServicioComisionesAnuales { get; set; }

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            string etiqueta;

            if (linea.Grupo != null && linea.Grupo.ToLower().Trim() == "otros aparatos")
            {
                etiqueta = "Otros Aparatos";
            }
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "uniónláser")
            {
                etiqueta = "Unión Láser";
            }
            else if (linea.Familia != null && EtiquetaFamiliasEspeciales.FamiliasEspeciales.Contains(linea.Familia.ToLower().Trim()))
            {
                etiqueta = "Especiales";
            }
            else
            {
                etiqueta = "General";
            }
            return etiqueta;
        }
        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return ServicioComisionesAnuales.LeerResumenAnno(this, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 32130M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 32130.01M,
                    Hasta = 70686M,
                    Tipo = .02M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 70686.01M,
                    Hasta = 91035M,
                    Tipo = .023M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 91035.01M,
                    Hasta = 107100M,
                    Tipo = .025M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 107100.01M,
                    Hasta = 139230M,
                    Tipo = .027M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 139230.01M,
                    Hasta = 176715M,
                    Tipo = .028M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 176715,
                    Hasta = 207774M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 207774.01M,
                    Hasta = 219019.50M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 219019.51M,
                    Hasta = 231065.57M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 231065.58M,
                    Hasta = 255157.72M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 255157.73M,
                    Hasta = 277059.67M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 277059.68M,
                    Hasta = 291295.94M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 291295.95M,
                    Hasta = 307722.40M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 307722.41M,
                    Hasta = 336194.93M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 336194.94M,
                    Hasta = 354811.59M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 354811.60M,
                    Hasta = 371238.05M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 371238.06M,
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
                    Hasta = 47250M,
                    Tipo = .0M,
                    TipoExtra = .0M
                },new TramoComision
                {
                    Desde = 47250.01M,
                    Hasta = 101717.89M,
                    Tipo = .006M,
                    TipoExtra = .0005M
                },new TramoComision
                {
                    Desde = 101717.90M,
                    Hasta = 107100.01M,
                    Tipo = .012M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 107100.02M,
                    Hasta = 127391.26M,
                    Tipo = .0145M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 127391.27M,
                    Hasta = 140070.01M,
                    Tipo = .0195M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 140070.02M,
                    Hasta = 152748.76M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 152748.77M,
                    Hasta = 160597.51M,
                    Tipo = .0306M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 160597.52M,
                    Hasta = 169653.76M,
                    Tipo = .033M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 169653.77M,
                    Hasta = 185351.26M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 185351.27M,
                    Hasta = 195615.01M,
                    Tipo = .0370M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 195615.02M,
                    Hasta = 204671.26M,
                    Tipo = .0395M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 204671.27M,
                    Hasta = 231065.57M,
                    Tipo = .042M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 231065.58M,
                    Hasta = 255157.72M,
                    Tipo = .045M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 255157.73M,
                    Hasta = 277059.67M,
                    Tipo = .055M,
                    TipoExtra = .02M
                },
                new TramoComision
                {
                    Desde = 277059.68M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0671M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosMinivendedores = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 5250M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 5250.01M,
                    Hasta = 10500M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 10500.01M,
                    Hasta = 15750M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 15750.01M,
                    Hasta = 21000M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 21000.01M,
                    Hasta = 36750M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 36750.01M,
                    Hasta = 47250M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 47250.01M,
                    Hasta = 57750M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 57750.01M,
                    Hasta = 68250M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 68250.01M,
                    Hasta = 78750M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 78750.01M,
                    Hasta = 89250M,
                    Tipo = .08M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 89250.01M,
                    Hasta = 99750M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 99750.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosJefeDeVentas = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 600000M,
                    Tipo = .003M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 600000.01M,
                    Hasta = 700000M,
                    Tipo = .004M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 700000.01M,
                    Hasta = 800000M,
                    Tipo = .005M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 800000.01M,
                    Hasta = 900000M,
                    Tipo = .006M,
                    TipoExtra = .0015M
                },
                new TramoComision
                {
                    Desde = 900000.01M,
                    Hasta = 1000000M,
                    Tipo = .007M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 1000000.01M,
                    Hasta = 1100000M,
                    Tipo = .008M,
                    TipoExtra = .0025M
                },
                new TramoComision
                {
                    Desde = 1100000.01M,
                    Hasta = 1200000M,
                    Tipo = .009M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 1200000.01M,
                    Hasta = 1300000M,
                    Tipo = .01M,
                    TipoExtra = .0035M
                },
                new TramoComision
                {
                    Desde = 1300000.01M,
                    Hasta = 1400000M,
                    Tipo = .011M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 1400000.01M,
                    Hasta = 1500000M,
                    Tipo = .012M,
                    TipoExtra = .0045M
                },
                new TramoComision
                {
                    Desde = 1500000.01M,
                    Hasta = 1600000M,
                    Tipo = .013M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 1600000.01M,
                    Hasta = 1700000M,
                    Tipo = .014M,
                    TipoExtra = .0055M
                },
                new TramoComision
                {
                    Desde = 1700000.01M,
                    Hasta = 1800000M,
                    Tipo = .015M,
                    TipoExtra = .006M
                },
                new TramoComision
                {
                    Desde = 1800000.01M,
                    Hasta = 1900000M,
                    Tipo = .016M,
                    TipoExtra = .0065M
                },
                new TramoComision
                {
                    Desde = 1900000.01M,
                    Hasta = 2000000M,
                    Tipo = .017M,
                    TipoExtra = .007M
                },
                new TramoComision
                {
                    Desde = 2000000.01M,
                    Hasta = 2100000M,
                    Tipo = .018M,
                    TipoExtra = .0075M
                },
                new TramoComision
                {
                    Desde = 2100000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .019M,
                    TipoExtra = .008M
                }
            };


            // Aquí usamos cadenas mágicas porque son los que YA han firmado las condiciones 
            if (vendedor == "DV" || vendedor == "JE" || vendedor == "RFG" || vendedor == "IM" || vendedor == "JGP" || vendedor == "MRM" || vendedor == "RAS")
            {
                return tramosCalle;
            }
            else if (vendedor == "AGR" || vendedor == "MPP" || vendedor == "PA" || vendedor == "KCP") 
            {
                return tramosTelefono;
            }
            else if (vendedor == "AL" || vendedor == "CAM" || vendedor == "MR" || vendedor == "PI" || vendedor == "SC")
            {
                return tramosMinivendedores;
            }
            else if (vendedor == "ASH")
            {
                return tramosJefeDeVentas;
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