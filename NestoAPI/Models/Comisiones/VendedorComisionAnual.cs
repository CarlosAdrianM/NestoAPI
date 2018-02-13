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
        private bool incluirAlbaranes;
        
        public VendedorComisionAnual(IServicioComisionesAnuales servicio, string vendedor, int anno)
            : this(servicio, vendedor, anno, DateTime.Today.Month)
        {
        }

        public VendedorComisionAnual(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes)
            :this(servicio, vendedor, anno, mes, false)
        {
        }

        public VendedorComisionAnual(IServicioComisionesAnuales servicio, string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            this.servicio = servicio;
            this.vendedor = vendedor;
            this.anno = anno;
            this.mes = mes;
            this.incluirAlbaranes = incluirAlbaranes;

            Resumenes = servicio.LeerResumenAnno(vendedor, anno);
            ResumenMesActual = Resumenes.SingleOrDefault(r => r.Mes == mes);
            if (ResumenMesActual == null)
            {
                ResumenMesActual = CrearResumenMesActual(incluirAlbaranes);
            } else
            {
                decimal ventaAcumulada = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Sum(r => r.GeneralVenta);
                int meses = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Count();
                ResumenMesActual.GeneralProyeccion = ventaAcumulada * (12 / meses);
            }
        }

        public ICollection<ResumenComisionesMes> Resumenes { get; set; }
        public ResumenComisionesMes ResumenMesActual { get; set; }

        private ResumenComisionesMes CrearResumenMesActual(bool incluirAlbaranes)
        {
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = vendedor,
                Anno = anno,
                Mes = mes,
                GeneralVenta = servicio.Etiquetas.Single(e => e.Nombre == "General").LeerVentaMes(vendedor, anno, mes, incluirAlbaranes),
                UnionLaserVenta = servicio.Etiquetas.Single(e => e.Nombre == "Unión Láser").LeerVentaMes(vendedor, anno, mes, incluirAlbaranes),
                EvaVisnuVenta = servicio.Etiquetas.Single(e => e.Nombre == "Eva Visnú").LeerVentaMes(vendedor, anno, mes, incluirAlbaranes),
                OtrosAparatosVenta = servicio.Etiquetas.Single(e => e.Nombre == "Otros Aparatos").LeerVentaMes(vendedor, anno, mes, incluirAlbaranes),
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
                resumen.GeneralFaltaParaSalto = tramo.Hasta - resumen.GeneralVenta;
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
                    resumen.GeneralFaltaParaSalto = tramo.Hasta == decimal.MaxValue ? 
                        decimal.MaxValue : 
                        Math.Round((tramo.Hasta/(12 / meses)) - ventaAcumulada,2);
                }
            }

            if (resumen.GeneralComision < 0)
            {
                resumen.GeneralComision = 0;
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