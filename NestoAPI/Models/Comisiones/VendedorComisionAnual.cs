using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class VendedorComisionAnual
    {

        private readonly IComisionesAnuales comisiones;
        private readonly string vendedor;
        private readonly int anno;
        private readonly int mes;
        private readonly bool incluirAlbaranes;
        private readonly bool incluirPicking;
        private readonly int mesesAnno = 12;

        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno)
            : this(comisiones, vendedor, anno, DateTime.Today.Month)
        {
        }

        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno, int mes)
            : this(comisiones, vendedor, anno, mes, false)
        {
        }

        public VendedorComisionAnual(IComisionesAnuales comisiones, string vendedor, int anno, int mes, bool incluirAlbaranes)
            : this(comisiones, vendedor, anno, mes, incluirAlbaranes, false)
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
            mesesAnno = Resumenes != null && Resumenes.Count > 0 ? 12 - Resumenes.Min(r => r.Mes) + 1 : 12 - mes + 1;
            ResumenMesActual = Resumenes.SingleOrDefault(r => r.Mes == mes);

            if (ResumenMesActual == null)
            {
                ResumenMesActual = CrearResumenMesActual(incluirAlbaranes, incluirPicking);
            }
            else
            {
                // Si se llama a este método tiene que ser una etiqueta de venta
                decimal ventaAcumulada = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Venta);
                int meses = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Count();
                var etiquetaAcumulada = ComisionesHelper.ObtenerEtiquetaAcumulada(ResumenMesActual.Etiquetas);
                etiquetaAcumulada.Proyeccion = comisiones.CalculadorProyecciones.CalcularProyeccion(vendedor, anno, mes, ventaAcumulada, meses, mesesAnno);
                ICollection<TramoComision> tramosAnno = comisiones.LeerTramosComisionAnno(vendedor);
                CalcularLimitesTramo(etiquetaAcumulada, tramosAnno);
                etiquetaAcumulada.BajaSaltoMesSiguiente = comisiones.CalculadorProyecciones.CalcularSiBajaDeSalto(vendedor, anno, mes, mesesAnno, ResumenMesActual, ventaAcumulada, meses, tramosAnno);
                etiquetaAcumulada.VentaAcumulada = ventaAcumulada;
                etiquetaAcumulada.ComisionAcumulada = Resumenes.Where(r => r.Mes <= ResumenMesActual.Mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Comision);
                etiquetaAcumulada.TipoConseguido = BuscarTramoComision(tramosAnno, etiquetaAcumulada.VentaAcumulada).Tipo;
                foreach (IEtiquetaComision etiqueta in ResumenMesActual.Etiquetas)
                {
                    if (etiqueta is IEtiquetaComisionVenta)
                    {
                        etiqueta.CifraAnual = CalcularCifraAnualVenta(etiqueta.Nombre, vendedor, anno, mes);
                    }
                    else if (etiqueta is IEtiquetaComisionClientes)
                    {
                        etiqueta.CifraAnual = CalcularCifraAnualClientes(etiqueta.Nombre, vendedor, anno, mes);
                    }

                    etiqueta.ComisionAnual = CalcularComisionAnual(etiqueta.Nombre, vendedor, anno, mes);
                    etiqueta.PorcentajeAnual = etiqueta.CifraAnual == 0 ? 0 : Math.Round(etiqueta.ComisionAnual / etiqueta.CifraAnual, 4, MidpointRounding.AwayFromZero);
                }
            }
            ResumenMesActual.TotalComisionAcumulada = TotalComisionesAcumuladas;
            ResumenMesActual.TotalVentaAcumulada = TotalVentasAcumuladas;
        }

        public ICollection<ResumenComisionesMes> Resumenes { get; private set; }
        public ResumenComisionesMes ResumenMesActual { get; private set; }

        public decimal TotalVentasAcumuladas =>
            Resumenes.Sum(r => r.Etiquetas.OfType<IEtiquetaComisionVenta>()
                .Where(e => e.SumaEnTotalVenta)
                .Sum(e => e.Venta)) +
            (ResumenMesActual?.Etiquetas.OfType<IEtiquetaComisionVenta>()
                .Where(e => e.SumaEnTotalVenta)
                .Sum(e => e.Venta) ?? 0);
        public decimal TotalComisionesAcumuladas => Resumenes.Sum(r => r.Etiquetas.Sum(e => e.Comision)) +
                       (ResumenMesActual?.Etiquetas.Sum(e => e.Comision) ?? 0);

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

                etiqueta.PorcentajeAnual = etiqueta.CifraAnual == 0
                    ? 0
                    : Math.Round(etiqueta.ComisionAnual / etiqueta.CifraAnual, 4, MidpointRounding.AwayFromZero);
            }

            IEtiquetaComisionVentaAcumulada etiquetaAcumulada = ComisionesHelper.ObtenerEtiquetaAcumulada(resumen.Etiquetas) as IEtiquetaComisionVentaAcumulada;
            int meses = Resumenes.Count + 1; //+1 por el mes actual
            decimal ventaAcumulada = Resumenes.Where(r => r.Mes <= mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Venta) + etiquetaAcumulada.Venta;
            if (ventaAcumulada == etiquetaAcumulada.Venta)
            {
                meses = 1;
            }
            etiquetaAcumulada.Proyeccion = comisiones.CalculadorProyecciones.CalcularProyeccion(vendedor, anno, mes, ventaAcumulada, meses, mesesAnno);

            ICollection<TramoComision> tramosMes = comisiones.LeerTramosComisionMes(vendedor);

            TramoComision tramo = BuscarTramoComision(tramosMes, etiquetaAcumulada.Venta);
            ICollection<TramoComision> tramosAnno = comisiones.LeerTramosComisionAnno(vendedor);

            if (tramo != null)
            {
                foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
                {
                    etiqueta.Tipo = etiqueta.SetTipo(tramo);
                }

                etiquetaAcumulada.Comision = Math.Round(etiquetaAcumulada.Venta * tramo.Tipo, 2);
                etiquetaAcumulada.FaltaParaSalto = tramo.Hasta - etiquetaAcumulada.Venta;
            }
            else
            {

                tramo = BuscarTramoComision(tramosAnno, etiquetaAcumulada.Proyeccion);
                if (tramo != null)
                {
                    foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
                    {
                        etiqueta.Tipo = etiqueta.SetTipo(tramo);
                    }

                    etiquetaAcumulada.Comision =
                        Math.Round((ventaAcumulada * etiquetaAcumulada.Tipo) - Resumenes.Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Comision), 2);
                    decimal mesesDecimales = (decimal)mesesAnno / meses;
                    etiquetaAcumulada.FaltaParaSalto = tramo.Hasta == decimal.MaxValue ?
                        decimal.MaxValue :
                        comisiones.CalculadorProyecciones.CalcularFaltaParaSalto(ventaAcumulada, tramo.Hasta, mesesDecimales, etiquetaAcumulada.Proyeccion);
                }
            }

            CalcularLimitesTramo(etiquetaAcumulada, tramosAnno);
            etiquetaAcumulada.BajaSaltoMesSiguiente = comisiones.CalculadorProyecciones.CalcularSiBajaDeSalto(vendedor, anno, mes, mesesAnno, resumen, ventaAcumulada, meses, tramosAnno);

            // Si hay sobrepago de comisión, se recalcula la comisión del mes actual            
            var estrategia = ServicioSelectorTipoComisionesAnualesVendedor.EstrategiaComisionSobrepago(vendedor, anno, mes);
            estrategia.AplicarEstrategia(etiquetaAcumulada, tramosAnno);

            etiquetaAcumulada.VentaAcumulada = ventaAcumulada;
            etiquetaAcumulada.ComisionAcumulada = Resumenes.Where(r => r.Mes <= mes).Sum(r => ComisionesHelper.ObtenerEtiquetaAcumulada(r.Etiquetas).Comision) + etiquetaAcumulada.Comision;
            var tramoEncontrado = BuscarTramoComision(tramosAnno, etiquetaAcumulada.VentaAcumulada);
            etiquetaAcumulada.TipoConseguido = tramoEncontrado != null ? tramoEncontrado.Tipo : 0;

            foreach (IEtiquetaComision etiqueta in resumen.Etiquetas)
            {
                if (etiqueta is IEtiquetaComisionVenta)
                {
                    etiqueta.CifraAnual = CalcularCifraAnualVenta(etiqueta.Nombre, vendedor, anno, mes) + (etiqueta as IEtiquetaComisionVenta).Venta;
                }
                else if (etiqueta is IEtiquetaComisionClientes)
                {
                    etiqueta.CifraAnual = CalcularCifraAnualClientes(etiqueta.Nombre, vendedor, anno, mes) + (etiqueta as IEtiquetaComisionClientes).Recuento;
                }

                etiqueta.ComisionAnual = CalcularComisionAnual(etiqueta.Nombre, vendedor, anno, mes) + etiqueta.Comision;
                etiqueta.PorcentajeAnual = etiqueta.CifraAnual == 0
                    ? 0
                    : Math.Round(etiqueta.ComisionAnual / etiqueta.CifraAnual, 4, MidpointRounding.AwayFromZero);
            }

            return resumen;
        }

        private void CalcularLimitesTramo(IEtiquetaComisionAcumulada etiquetaAcumulada, ICollection<TramoComision> tramosAnno)
        {
            var tramoProyeccion = BuscarTramoComision(tramosAnno, etiquetaAcumulada.Proyeccion);
            etiquetaAcumulada.InicioTramo = tramoProyeccion.Desde;
            etiquetaAcumulada.FinalTramo = tramoProyeccion.Hasta;
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

        private decimal CalcularCifraAnualVenta(string nombreEtiqueta, string vendedor, int anno, int hastaEelMes)
        {
            return Resumenes.Where(r => r.Mes <= hastaEelMes)
                           .Sum(r => (r.Etiquetas.Where(e => e.Nombre == nombreEtiqueta).SingleOrDefault() as IEtiquetaComisionVenta)?.Venta ?? 0);
        }

        private decimal CalcularCifraAnualClientes(string nombreEtiqueta, string vendedor, int anno, int hastaEelMes)
        {
            return Resumenes.Where(r => r.Mes <= hastaEelMes)
                           .Sum(r => (r.Etiquetas.Where(e => e.Nombre == nombreEtiqueta).SingleOrDefault() as IEtiquetaComisionClientes)?.Recuento ?? 0);
        }

        private decimal CalcularComisionAnual(string nombreEtiqueta, string vendedor, int anno, int hastaEelMes)
        {
            return Resumenes.Where(r => r.Mes <= hastaEelMes)
                           .Sum(r => r.Etiquetas.Where(e => e.Nombre == nombreEtiqueta).SingleOrDefault()?.Comision ?? 0);
        }

        public static DateTime FechaDesde(int anno, int mes)
        {
            return new DateTime(anno, mes, 1);
        }

        public static DateTime FechaHasta(int anno, int mes)
        {
            return mes != 12 ? new DateTime(anno, mes + 1, 1).AddDays(-1) : new DateTime(anno + 1, 1, 1).AddDays(-1);
        }
    }
}