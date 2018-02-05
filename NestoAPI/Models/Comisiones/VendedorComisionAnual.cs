using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class VendedorComisionAnual
    {
        private const decimal TIPO_FIJO_UNIONLASER = .1M;
        private const decimal TIPO_FIJO_OTROSAPARATOS = .02M;

        private IServicioComisionesAnuales servicio;
        private string vendedor;
        private int anno;
        private int mes;
        
        public VendedorComisionAnual(IServicioComisionesAnuales servicio, string vendedor, int anno)
        {
            this.servicio = servicio;
            this.vendedor = vendedor;
            this.anno = anno;

            Resumenes = servicio.LeerResumenAnno(vendedor, anno);
            mes = DateTime.Today.Month;
            ResumenMesActual = CrearResumenMesActual(true);
        }

        public ICollection<ResumenComisionesMes> Resumenes { get; set; }
        public ResumenComisionesMes ResumenMesActual { get; set; }

        private ResumenComisionesMes CrearResumenMesActual(bool incluirAlbaranes)
        {
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                GeneralVenta = servicio.LeerGeneralVentaMes(vendedor, anno, mes, incluirAlbaranes),
                UnionLaserVenta = servicio.LeerUnionLaserVentaMes(vendedor, anno, mes, incluirAlbaranes),
                EvaVisnuVenta = servicio.LeerEvaVisnuVentaMes(vendedor, anno, mes, incluirAlbaranes),
                OtrosAparatosVenta = servicio.LeerOtrosAparatosVentaMes(vendedor, anno, mes, incluirAlbaranes),
            };

            resumen.OtrosAparatosTipo = TIPO_FIJO_OTROSAPARATOS;

            int meses = Resumenes.Count + 1; //+1 por el mes actual
            decimal ventaAcumulada = Resumenes.Sum(r => r.GeneralVenta) + resumen.GeneralVenta;
            resumen.GeneralProyeccion = ventaAcumulada * (12/meses);

            ICollection<TramoComision> tramosMes = servicio.LeerTramosComisionMes(vendedor);

            TramoComision tramo = BuscarTramoComision(tramosMes, resumen.GeneralVenta);

            if (tramo != null)
            {
                resumen.GeneralTipo = tramo.Tipo;
                resumen.GeneralComision = Math.Round(resumen.GeneralVenta * tramo.Tipo);
                resumen.UnionLaserTipo = TIPO_FIJO_UNIONLASER + tramo.TipoExtra;
                resumen.EvaVisnuTipo = tramo.TipoExtra;
            } else
            {
                ICollection<TramoComision> tramosAnno = servicio.LeerTramosComisionAnno(vendedor);
                tramo = BuscarTramoComision(tramosAnno, resumen.GeneralProyeccion);
                if (tramo!=null)
                {
                    resumen.GeneralTipo = tramo.Tipo;
                    resumen.GeneralComision = Math.Round(ventaAcumulada * resumen.GeneralTipo - Resumenes.Sum(r => r.GeneralComision),2);
                    resumen.UnionLaserTipo = TIPO_FIJO_UNIONLASER + tramo.TipoExtra;
                    resumen.EvaVisnuTipo = tramo.TipoExtra;
                }
            }
                        
            return resumen;
        }

        private TramoComision BuscarTramoComision(ICollection<TramoComision> tramos, decimal importe)
        {
            foreach (TramoComision tramo in tramos)
            {
                if (tramo.Desde <= importe && tramo.Hasta >= importe)
                {
                    return tramo;
                }
            }

            return null;
        }
    }
}