using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Comisiones
{
    public class ServicioComisionesAnualesEstetica : IServicioComisionesAnuales
    {
        private NVEntities db = new NVEntities();

        public decimal LeerEvaVisnuVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Familia == "Eva Visnu" && 
                    l.Grupo.ToLower() != "otros aparatos" &&
                    l.Vendedor == vendedor
                );

            return CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta); ;
        }

        public decimal LeerGeneralVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {

            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.EstadoFamilia == 0 &&
                    l.Familia.ToLower() != "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );

            return CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta); 
        }

        public decimal LeerOtrosAparatosVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);

            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.Grupo.ToLower() == "otros aparatos"
                );

            return CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return new Collection<ResumenComisionesMes>();
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            Collection<TramoComision> tramosCalle = new Collection<TramoComision>
            {
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
                    Hasta = Decimal.MaxValue,
                    Tipo = .0925M,
                    TipoExtra = .02M
                }
            };

            Collection<TramoComision> tramosTelefono = new Collection<TramoComision>
            {
                new TramoComision
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
                    TipoExtra = .025M
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
                    TipoExtra = .055M
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
                    Hasta = Decimal.MaxValue,
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
                    Desde = 0,
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
                    Desde = 0,
                    Hasta = 6000,
                    Tipo = 0,
                    TipoExtra = 0
                },
                new TramoComision
                {
                    Desde = 6000.01M,
                    Hasta = 7914.56M,
                    Tipo = .067M,
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

        public decimal LeerUnionLaserVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            // OJO AQUÍ FALTAN LOS RENTING

            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.Familia == "UnionLaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );
            decimal venta = CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);

            return venta;
        }

        private static decimal CalcularVentaFiltrada(bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta, ref IQueryable<vstLinPedidoVtaComisione> consulta)
        {
            if (incluirAlbaranes)
            {
                consulta = consulta.Where(l => l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else
            {
                consulta = consulta.Where(l => l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4);
            }
            decimal venta = consulta.Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();
            return venta;
        }

        private DateTime FechaDesde(int anno, int mes)
        {
            return new DateTime(anno, mes, 1);
        }

        private DateTime FechaHasta(int anno, int mes)
        {
            return (new DateTime(anno, mes+1, 1)).AddDays(-1);
        }
    }
}