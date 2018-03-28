﻿using NestoAPI.Models.Comisiones.Estetica;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ServicioComisionesAnualesEstetica : IServicioComisionesAnuales
    {
        const string GENERAL = "General";
        
        private NVEntities db = new NVEntities();

        public ServicioComisionesAnualesEstetica()
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

        //public decimal LeerOtrosAparatosVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        //{
        //    DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
        //    DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

        //    IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
        //        .Where(l =>
        //            l.Vendedor == vendedor &&
        //            l.Grupo.ToLower() == "otros aparatos" &&
        //            l.EstadoFamilia == 0
        //        );

        //    return ServicioComisionesAnualesComun.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        //}

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
                    Hasta = 189949.36M,
                    Tipo = .02M,
                    TipoExtra = .00M
                },
                new TramoComision
                {
                    Desde = 189949.37M,
                    Hasta = 200000M,
                    Tipo = .03M,
                    TipoExtra = .001M
                },
                new TramoComision
                {
                    Desde = 200000.01M,
                    Hasta = 211000M,
                    Tipo = .035M,
                    TipoExtra = .002M
                },
                new TramoComision
                {
                    Desde = 211000.01M,
                    Hasta = 253000M,
                    Tipo = .045M,
                    TipoExtra = .003M
                },
                new TramoComision
                {
                    Desde = 253000.01M,
                    Hasta = 266000M,
                    Tipo = .0671M,
                    TipoExtra = .005M
                },
                new TramoComision
                {
                    Desde = 266000.01M,
                    Hasta = 281000M,
                    Tipo = .072M,
                    TipoExtra = .008M
                },
                new TramoComision
                {
                    Desde = 281000.01M,
                    Hasta = 307000M,
                    Tipo = .077M,
                    TipoExtra = .011M
                },
                new TramoComision
                {
                    Desde = 307000.01M,
                    Hasta = 324000M,
                    Tipo = .08M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 324000.01M,
                    Hasta = 339000M,
                    Tipo = .085M,
                    TipoExtra = .017M
                },
                new TramoComision
                {
                    Desde = 339000.01M,
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
                    Tipo = .0067M,
                    TipoExtra = .0M
                },new TramoComision
                {
                    Desde = 94974.69M,
                    Hasta = 100000M,
                    Tipo = .012M,
                    TipoExtra = .0M
                },
                new TramoComision
                {
                    Desde = 100000.01M,
                    Hasta = 105500M,
                    Tipo = .0145M,
                    TipoExtra = .0M
                },
                new TramoComision
                {
                    Desde = 105500.01M,
                    Hasta = 126500M,
                    Tipo = .0195M,
                    TipoExtra = .0M
                },
                new TramoComision
                {
                    Desde = 126500.01M,
                    Hasta = 133000M,
                    Tipo = .0306M,
                    TipoExtra = .0025M
                },
                new TramoComision
                {
                    Desde = 133000.01M,
                    Hasta = 140500M,
                    Tipo = .033M,
                    TipoExtra = .004M
                },
                new TramoComision
                {
                    Desde = 140500.01M,
                    Hasta = 153500M,
                    Tipo = .0355M,
                    TipoExtra = .0055M
                },
                new TramoComision
                {
                    Desde = 153500.01M,
                    Hasta = 162000M,
                    Tipo = .0370M,
                    TipoExtra = .007M
                },
                new TramoComision
                {
                    Desde = 162000.01M,
                    Hasta = 169500M,
                    Tipo = .0395M,
                    TipoExtra = .0085M
                },
                new TramoComision
                {
                    Desde = 169500.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = .0462M,
                    TipoExtra = .01M
                }
            };

            if (vendedor == "ASH" || vendedor == "DV" || vendedor == "JE" || vendedor == "JM")
            {
                return tramosCalle;
            }
            else if (vendedor == "CL" || vendedor == "LA" || vendedor == "MRM" || vendedor == "PA" || vendedor == "SH")
            {
                return tramosTelefono;
            }

            throw new Exception("El vendedor " + vendedor + " no comisiona por este esquema");
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            Collection <TramoComision> tramosCalle = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = decimal.MinValue,
                    Hasta = 12000,
                    Tipo = 0,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 12000.01M,
                    Hasta = 15829.11M,
                    Tipo = .02M,
                    TipoExtra = 0
                }
            };

            Collection<TramoComision> tramosTelefono = new Collection<TramoComision>
            {
                new TramoComision
                {
                    Desde = decimal.MinValue,
                    Hasta = 6000,
                    Tipo = 0,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 6000.01M,
                    Hasta = 7914.56M,
                    Tipo = .0067M,
                    TipoExtra = 0
                }
            };
            if (vendedor == "ASH" || vendedor == "DV" || vendedor == "JE" || vendedor == "JM")
            {
                return tramosCalle;
            } else if (vendedor == "CL" || vendedor == "LA" || vendedor == "MRM" || vendedor == "PA" || vendedor == "SH")
            {
                return tramosTelefono;
            }

            throw new Exception("El vendedor " + vendedor + " no comisiona por este esquema");
            
        }
        
    }
}