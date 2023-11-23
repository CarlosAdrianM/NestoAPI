using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace NestoAPI.Models.Comisiones
{
    public class ComisionesAnualesEstetica2019 : IComisionesAnuales
    {
        const string GENERAL = "General";
        
        private NVEntities db = new NVEntities();

        public ComisionesAnualesEstetica2019()
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

        public ICalculadorProyecciones CalculadorProyecciones => new CalculadorProyecciones2019();

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
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "eva visnu")
            {
                etiqueta = "Eva Visnú";
            }
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "lisap")
            {
                etiqueta = "Lisap";
            }
            else if (linea.Familia != null && linea.Familia.ToLower().Trim() == "kach")
            {
                etiqueta = "Kach";
            }
            else
            {
                etiqueta = "General";
            }
            return etiqueta;
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return (new ServicioComisionesAnualesComun()).LeerResumenAnno(this, vendedor, anno);
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            vendedor = vendedor.ToUpper();
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 30000M,
                    Tipo = .0M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 30000.01M,
                    Hasta = 66000M,
                    Tipo = .02M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 66000.01M,
                    Hasta = 85000M,
                    Tipo = .023M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 85000.01M,
                    Hasta = 100000M,
                    Tipo = .025M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 100000.01M,
                    Hasta = 130000M,
                    Tipo = .027M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 130000.01M,
                    Hasta = 165000M,
                    Tipo = .028M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 165000.01M,
                    Hasta = 194000M,
                    Tipo = .029M,
                    TipoExtra = .0005M
                },
                new TramoComision
                {
                    Desde = 194000.01M,
                    Hasta = 204500M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 204500.01M,
                    Hasta = 215747.5M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 215747.51M,
                    Hasta = 238242.5M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 238242.51M,
                    Hasta = 258692.5M,
                    Tipo = .055M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 258692.51M,
                    Hasta = 271985M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 271985.01M,
                    Hasta = 287322.5M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 287322.51M,
                    Hasta = 313907.5M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 313907.51M,
                    Hasta = 331290M,
                    Tipo = .08M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 331290.01M,
                    Hasta = 346627.5M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 346627.51M,
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
                    Hasta = 94974.68M,
                    Tipo = .0M,
                    TipoExtra = .0M
                },new TramoComision
                {
                    Desde = 94974.69M,
                    Hasta = 100000M,
                    Tipo = .012M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 100000.01M,
                    Hasta = 105500M,
                    Tipo = .0145M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 105500.01M,
                    Hasta = 116000M,
                    Tipo = .0195M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 116000.01M,
                    Hasta = 126500M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 126500.01M,
                    Hasta = 133000M,
                    Tipo = .0306M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 133000.01M,
                    Hasta = 140500M,
                    Tipo = .033M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 140500.01M,
                    Hasta = 153500M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 153500.01M,
                    Hasta = 162000M,
                    Tipo = .0370M,
                    TipoExtra = .014M
                },
                new TramoComision
                {
                    Desde = 162000.01M,
                    Hasta = 169500M,
                    Tipo = .0395M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 169500.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0462M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosTelefonoParcial = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 8000M,
                    Tipo = .0M,
                    TipoExtra = .0M
                },new TramoComision
                {
                    Desde = 8000.01M,
                    Hasta = 16000M,
                    Tipo = .045M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 16000.01M,
                    Hasta = 24000M,
                    Tipo = .0225M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 24000.01M,
                    Hasta = 32000M,
                    Tipo = .0355M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 32000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0395M,
                    TipoExtra = .017M
                }
            };

            if (vendedor == "ASH" || vendedor == "DV" || vendedor == "JE" || vendedor == "MRM" || vendedor == "RFG" || vendedor == "JGP")
            {
                return tramosCalle;
            }
            else if (vendedor == "LA" || vendedor == "PA" || vendedor == "SH")
            {
                return tramosTelefono;
            }
            else if (vendedor == "CAR")
            {
                return tramosTelefonoParcial;
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