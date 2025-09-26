using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones
{
    public abstract class EtiquetaComisionVentaAcumuladaBase : EtiquetaComisionVentaBase, IEtiquetaComisionVentaAcumulada
    {
        protected IQueryable<vstLinPedidoVtaComisione> consulta;
        protected readonly IServicioComisionesAnuales _servicioComisiones;

        protected EtiquetaComisionVentaAcumuladaBase(IServicioComisionesAnuales servicioComisiones)
        {
            _servicioComisiones = servicioComisiones;
        }

        public override decimal Comision { get; set; }
        public override bool EsComisionAcumulada => true;
        public override bool SumaEnTotalVenta => true;

        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }

        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();

            // Solo aplicar filtro de vendedor si se debe filtrar por vendedor
            if (DebeAplicarFiltroVendedor && !string.IsNullOrEmpty(vendedor))
            {
                var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);
                consulta = consulta.Where(l => listaVendedores.Contains(l.Vendedor));
            }

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public override IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();

            // Solo aplicar filtro de vendedor si se debe filtrar por vendedor
            if (DebeAplicarFiltroVendedor && !string.IsNullOrEmpty(vendedor))
            {
                var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);
                consulta = consulta.Where(l => listaVendedores.Contains(l.Vendedor));
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public override decimal SetTipo(TramoComision tramo)
        {
            return tramo.Tipo;
        }

        public override bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }

        private void CrearConsulta()
        {
            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(PredicadoFiltro());
        }

        // Método abstracto que cada clase hija debe implementar para definir su filtro específico
        protected abstract Expression<Func<vstLinPedidoVtaComisione, bool>> PredicadoFiltro();

        // Propiedad virtual para controlar si se debe aplicar filtro por vendedor
        protected virtual bool DebeAplicarFiltroVendedor => true;

        // Propiedades específicas de comisión acumulada
        public decimal FaltaParaSalto { get; set; }
        public decimal InicioTramo { get; set; }
        public decimal FinalTramo { get; set; }
        public bool BajaSaltoMesSiguiente { get; set; }
        public decimal Proyeccion { get; set; }
        public decimal VentaAcumulada { get; set; }
        public decimal ComisionAcumulada { get; set; }
        public decimal TipoConseguido { get; set; }

        public virtual decimal ComisionRecuperadaEsteMes =>
            Math.Abs(Math.Round((Venta * Tipo) - Comision, 2, MidpointRounding.AwayFromZero)) <= 0.01M
                ? 0
                : Math.Round((Venta * Tipo) - Comision, 2, MidpointRounding.AwayFromZero);

        public virtual string TextoSobrepago
        {
            get
            {
                if (!EsSobrepago)
                {
                    if (ComisionRecuperadaEsteMes == 0)
                    {
                        return Nombre;
                    }
                    else
                    {
                        return ComisionRecuperadaEsteMes > 0
                            ? $"Al cambiar a un tramo inferior se han bajado {ComisionRecuperadaEsteMes:C2} de comisión"
                            : $"Al cambiar a un tramo superior se han incrementado {-ComisionRecuperadaEsteMes:C2} de comisión";
                    }
                }

                string comisionFormatoMoneda = (-ComisionSinEstrategia.GetValueOrDefault(0)).ToString("C2");
                string tipoRealFormatoPorcentaje = TipoRealmenteAplicado.GetValueOrDefault(0).ToString("P2");
                string tipoCorrespondeFormatoPorcentaje = TipoCorrespondePorTramo.GetValueOrDefault(0).ToString("P2");
                string ajuste = Math.Round((decimal)((Venta * TipoCorrespondePorTramo) - (Venta * TipoRealmenteAplicado)), 2, MidpointRounding.AwayFromZero).ToString("C2");

                return $"Hay un sobrepago de {comisionFormatoMoneda} por lo que se aplicará un tipo del {tipoRealFormatoPorcentaje} en lugar del {tipoCorrespondeFormatoPorcentaje} (ajuste de {ajuste})";
            }
        }

        public decimal TipoReal => VentaAcumulada == 0
            ? 0
            : Math.Round(ComisionAcumulada / VentaAcumulada, 4, MidpointRounding.AwayFromZero);

        // Propiedades de estrategia
        public string EstrategiaUtilizada { get; set; }
        public decimal? TipoCorrespondePorTramo { get; set; }
        public decimal? TipoRealmenteAplicado { get; set; }
        public string MotivoEstrategia { get; set; }
        public decimal? ComisionSinEstrategia { get; set; }

        public bool TieneEstrategiaEspecial => !string.IsNullOrEmpty(EstrategiaUtilizada);
        public bool EsSobrepago => TipoCorrespondePorTramo.HasValue && TipoRealmenteAplicado.HasValue &&
                                   TipoCorrespondePorTramo != TipoRealmenteAplicado;
    }
}