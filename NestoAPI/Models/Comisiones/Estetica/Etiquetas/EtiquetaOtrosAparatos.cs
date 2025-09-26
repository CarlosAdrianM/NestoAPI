using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaOtrosAparatos : EtiquetaComisionVentaBase, IEtiquetaComisionVenta, ICloneable
    {
        private const decimal TIPO_FIJO_OTROSAPARATOS = .02M;

        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnuales _servicioComisiones;

        public EtiquetaOtrosAparatos(IServicioComisionesAnuales servicioComisiones)
        {
            _servicioComisiones = servicioComisiones;
        }

        public override string Nombre => "Otros Aparatos";

        public override decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Otros Aparatos no se puede fijar manualmente");
        }
        public override bool EsComisionAcumulada => false;

        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            CrearConsulta(vendedor, fechaDesde);

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public override IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor, fechaDesde);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }
        private Expression<Func<vstLinPedidoVtaComisione, bool>> PredicadoFiltro()
        {
            return l => l.Grupo != null &&
                        l.Grupo.ToLower().Trim() == "otros aparatos" &&
                        l.EstadoFamilia == 0;
        }
        private void CrearConsulta(string vendedor, DateTime fecha)
        {
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l => listaVendedores.Contains(l.Vendedor))
                .Where(PredicadoFiltro());
        }

        public override bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }

        public override decimal SetTipo(TramoComision tramo)
        {
            return TIPO_FIJO_OTROSAPARATOS;
        }

        public override object Clone()
        {
            return new EtiquetaOtrosAparatos(_servicioComisiones)
            {
                Venta = Venta,
                Tipo = Tipo
            };
        }
    }
}