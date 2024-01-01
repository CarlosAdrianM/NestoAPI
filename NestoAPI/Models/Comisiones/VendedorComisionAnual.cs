using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class VendedorComisionAnual
    {
        
        const string GENERAL = "General";

        private readonly IComisionesAnuales comisiones;
        private string vendedor;
        private int anno;
        private int mes;
        private bool incluirAlbaranes;
        private bool incluirPicking;
        private int mesesAnno = 12;
        
        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno)
            : this(comisiones, vendedor, anno, DateTime.Today.Month)
        {
        }

        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno, int mes)
            :this(comisiones, vendedor, anno, mes, false)
        {
        }

        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno, int mes, bool incluirAlbaranes)
            :this(comisiones, vendedor, anno, mes, incluirAlbaranes, false)
        {
        }
        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            this.comisiones = comisiones;
            this.vendedor = vendedor;
            this.anno = anno;
            this.mes = mes;
            this.incluirAlbaranes = incluirAlbaranes;
            this.incluirPicking = incluirPicking;

            Resumenes = comisiones.LeerResumenAnno(vendedor, anno);
            if (Resumenes != null && Resumenes.Count>0)
            {
                mesesAnno = 12 - Resumenes.Min(r => r.Mes) + 1;
            } else
            {
                mesesAnno = 12 - mes + 1;
            }
            ResumenMesActual = Resumenes.SingleOrDefault(r => r.Mes == mes);

            if (ResumenMesActual == null)
            {
                ResumenMesActual = CrearResumenMesActual(incluirAlbaranes, incluirPicking);
            } else
            {
                // Si se llama a este método tiene que ser una etiqueta de venta
                decimal ventaAcumulada = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Sum(r => (r.Etiquetas.Where(e => e.Nombre == GENERAL).Single() as IEtiquetaComisionVenta).Venta);
                int meses = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Count();
                ResumenMesActual.GeneralProyeccion = comisiones.CalculadorProyecciones.CalcularProyeccion(vendedor, anno, mes, ventaAcumulada, meses, mesesAnno);
                ICollection<TramoComision> tramosAnno = comisiones.LeerTramosComisionAnno(vendedor);
                CalcularLimitesTramo(ResumenMesActual, tramosAnno);
                ResumenMesActual.GeneralBajaSaltoMesSiguiente = comisiones.CalculadorProyecciones.CalcularSiBajaDeSalto(vendedor, anno, mes, mesesAnno, ResumenMesActual, ventaAcumulada, meses, tramosAnno);
                ResumenMesActual.GeneralVentaAcumulada = ventaAcumulada;
                ResumenMesActual.GeneralComisionAcumulada = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision);
                ResumenMesActual.GeneralTipoConseguido = BuscarTramoComision(tramosAnno, ResumenMesActual.GeneralVentaAcumulada).Tipo;
            }
        }

        public ICollection<ResumenComisionesMes> Resumenes { get; private set; }
        public ResumenComisionesMes ResumenMesActual { get; private set; }

        private ResumenComisionesMes CrearResumenMesActual(bool incluirAlbaranes, bool incluirPicking)
        {
            ResumenComisionesMes resumen = new ResumenComisionesMes
            {
                Vendedor = vendedor,
                Anno = anno,
                Mes = mes,
                Etiquetas = comisiones.NuevasEtiquetas
            };

            foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
            {
                if (etiqueta is IEtiquetaComisionVenta)
                {
                    IEtiquetaComisionVenta etiquetaComision = comisiones.Etiquetas.Single(e => e.Nombre == etiqueta.Nombre) as IEtiquetaComisionVenta;
                    (etiqueta as IEtiquetaComisionVenta).Venta = etiquetaComision.LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, incluirPicking);
                }
                else if (etiqueta is IEtiquetaComisionClientes)
                {
                    IEtiquetaComisionClientes etiquetaComision = comisiones.Etiquetas.Single(e => e.Nombre == etiqueta.Nombre) as IEtiquetaComisionClientes;
                    (etiqueta as IEtiquetaComisionClientes).Recuento = etiquetaComision.LeerClientesMes(vendedor, anno, mes);
                }
                else
                {
                    throw new Exception("Tipo de etiqueta no contemplado");
                }                
            }

            IEtiquetaComisionVenta etiquetaGeneral = resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single() as IEtiquetaComisionVenta;
            int meses = Resumenes.Count + 1; //+1 por el mes actual
            decimal ventaAcumulada = Resumenes.Where(r => r.Mes <= mes).Sum(r => (r.Etiquetas.Where(e => e.Nombre == GENERAL).Single() as IEtiquetaComisionVenta).Venta) + etiquetaGeneral.Venta;
            resumen.GeneralProyeccion = comisiones.CalculadorProyecciones.CalcularProyeccion(vendedor, anno, mes, ventaAcumulada, meses, mesesAnno);

            ICollection<TramoComision> tramosMes = comisiones.LeerTramosComisionMes(vendedor);

            TramoComision tramo = BuscarTramoComision(tramosMes, etiquetaGeneral.Venta);
            ICollection<TramoComision> tramosAnno = comisiones.LeerTramosComisionAnno(vendedor);

            if (tramo != null)
            {
                foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
                {
                    etiqueta.Tipo = etiqueta.SetTipo(tramo);
                }

                etiquetaGeneral.Comision = Math.Round(etiquetaGeneral.Venta * tramo.Tipo, 2);
                resumen.GeneralFaltaParaSalto = tramo.Hasta - etiquetaGeneral.Venta;
            }
            else
            {

                tramo = BuscarTramoComision(tramosAnno, resumen.GeneralProyeccion);
                if (tramo != null)
                {
                    foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
                    {
                        etiqueta.Tipo = etiqueta.SetTipo(tramo);
                    }

                    etiquetaGeneral.Comision =
                        //mes == 8 ?
                        //Math.Round(resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Venta * resumen.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Tipo, 2) :
                        Math.Round(ventaAcumulada * etiquetaGeneral.Tipo - Resumenes.Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision), 2);
                    decimal mesesDecimales = (decimal)mesesAnno / meses;
                    resumen.GeneralFaltaParaSalto = tramo.Hasta == decimal.MaxValue ?
                        decimal.MaxValue :
                        comisiones.CalculadorProyecciones.CalcularFaltaParaSalto(ventaAcumulada, tramo.Hasta, mesesDecimales, resumen.GeneralProyeccion);
                }
            }

            CalcularLimitesTramo(resumen, tramosAnno);
            resumen.GeneralBajaSaltoMesSiguiente = comisiones.CalculadorProyecciones.CalcularSiBajaDeSalto(vendedor, anno, mes, mesesAnno, resumen, ventaAcumulada, meses, tramosAnno);

            if (etiquetaGeneral.Comision < 0)
            {
                etiquetaGeneral.Comision = 0;
            }

            resumen.GeneralVentaAcumulada = ventaAcumulada;
            resumen.GeneralComisionAcumulada = Resumenes.Where(r => r.Mes <= mes).Sum(r => r.Etiquetas.Where(e => e.Nombre == GENERAL).Single().Comision) + etiquetaGeneral.Comision;
            var tramoEncontrado = BuscarTramoComision(tramosAnno, resumen.GeneralVentaAcumulada);
            resumen.GeneralTipoConseguido = tramoEncontrado != null ? tramoEncontrado.Tipo : 0;

            return resumen;
        }

        private void CalcularLimitesTramo(ResumenComisionesMes resumen, ICollection<TramoComision> tramosAnno)
        {
            var tramoProyeccion = BuscarTramoComision(tramosAnno, resumen.GeneralProyeccion);
            resumen.GeneralInicioTramo = tramoProyeccion.Desde;
            resumen.GeneralFinalTramo = tramoProyeccion.Hasta;
        }

        public static TramoComision BuscarTramoComision(ICollection<TramoComision> tramos, decimal importe)
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

        public static DateTime FechaDesde(int anno, int mes)
        {
            return new DateTime(anno, mes, 1);
        }

        public static DateTime FechaHasta(int anno, int mes)
        {
            if (mes != 12)
            {
                return (new DateTime(anno, mes + 1, 1)).AddDays(-1);
            } else
            {
                return (new DateTime(anno + 1, 1, 1)).AddDays(-1);
            }            
        }
    }
}