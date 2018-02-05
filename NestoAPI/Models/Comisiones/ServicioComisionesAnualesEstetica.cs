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
            decimal venta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta &&
                    l.Familia == "Eva Visnu" && 
                    l.Grupo.ToLower() != "otros aparatos" &&
                    l.Vendedor == vendedor &&
                    (l.Estado == 2 || l.Estado == 4)
                ).Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();

            return venta;
        }

        public decimal LeerGeneralVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {

            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            decimal venta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta &&
                    l.Vendedor == vendedor &&
                    (
                    l.Familia.ToLower() == "anubis" ||
                    l.Familia.ToLower() == "ardell" ||
                    l.Familia.ToLower() == "belclinic" ||
                    l.Familia.ToLower() == "blancabarb" ||
                    l.Familia.ToLower() == "cursos" ||
                    l.Familia.ToLower() == "cv" ||
                    l.Familia.ToLower() == "du" ||
                    l.Familia.ToLower() == "estelca" ||
                    l.Familia.ToLower() == "eva visnu" ||
                    l.Familia.ToLower() == "faby" ||
                    l.Familia.ToLower() == "fama" ||
                    l.Familia.ToLower() == "gena" ||
                    l.Familia.ToLower() == "genéricos" ||
                    l.Familia.ToLower() == "ibd" ||
                    l.Familia.ToLower() == "ibp uniuso" ||
                    l.Familia.ToLower() == "irene ríos" ||
                    l.Familia.ToLower() == "m2lashes" ||
                    l.Familia.ToLower() == "masglo" ||
                    l.Familia.ToLower() == "maystar" ||
                    l.Familia.ToLower() == "nk" ||
                    l.Familia.ToLower() == "paraiso" ||
                    l.Familia.ToLower() == "pbserum" ||
                    l.Familia.ToLower() == "pure" ||
                    l.Familia.ToLower() == "tessiline" ||
                    l.Familia.ToLower() == "tevian" ||
                    l.Familia.ToLower() == "uso prof." ||
                    l.Familia.ToLower() == "silverfox" ||
                    l.Familia.ToLower() == "wear&tear" ||
                    l.Familia.ToLower() == "agv" ||
                    l.Familia.ToLower() == "dr. santé" ||
                    l.Familia.ToLower() == "iberfilm" ||
                    l.Familia.ToLower() == "k. cure" ||
                    l.Familia.ToLower() == "jorgegarza" ||
                    l.Familia.ToLower() == "lisap") &&
                    l.Grupo.ToLower() != "otros aparatos" &&
                    (l.Estado == 2 || l.Estado == 4)
                ).Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();

            return venta;
        }

        public decimal LeerOtrosAparatosVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            
            decimal venta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta &&
                    l.Vendedor == vendedor &&
                    l.Grupo.ToLower() == "otros aparatos" &&
                    (l.Estado == 2 || l.Estado == 4)
                ).Select(l=>l.Base_Imponible).DefaultIfEmpty().Sum();

            return venta;
        }

        public ICollection<ResumenComisionesMes> LeerResumenAnno(string vendedor, int anno)
        {
            return new Collection<ResumenComisionesMes>();
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            Collection<TramoComision> tramos = new Collection<TramoComision>
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

            return tramos;
        }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            Collection <TramoComision> tramos = new Collection<TramoComision>
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
            return tramos;
        }

        public decimal LeerUnionLaserVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            // OJO AQUÍ FALTAN LOS RENTING

            DateTime fechaDesde = FechaDesde(anno, mes);
            DateTime fechaHasta = FechaHasta(anno, mes);
            decimal venta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta &&
                    l.Familia == "UnionLaser" &&
                    l.Grupo.ToLower() != "otros aparatos" &&
                    (l.Estado == 2 || l.Estado == 4)
                ).Select(l => l.Base_Imponible).DefaultIfEmpty().Sum();

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