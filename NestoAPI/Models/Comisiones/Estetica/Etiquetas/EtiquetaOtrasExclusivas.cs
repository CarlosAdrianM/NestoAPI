using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaOtrasExclusivas : EtiquetaComisionVentaBase, IEtiquetaComisionVenta, ICloneable
    {
        private readonly IServicioComisionesAnuales _servicioComisiones;
        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly string[] _familiasIncluidas;

        public EtiquetaOtrasExclusivas(IServicioComisionesAnuales servicioComisiones, string[] familiasIncluidas)
        {
            _servicioComisiones = servicioComisiones;
            _familiasIncluidas = familiasIncluidas;
        }

        public override string Nombre => "Otras Exclusivas";

        public override decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2); set => throw new Exception("La comisión de las otras exclusivas no se puede fijar manualmente");
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
            return l => _familiasIncluidas.Contains(l.Familia.ToLower()) &&
                        !l.Grupo.ToLower().Equals("otros aparatos", StringComparison.OrdinalIgnoreCase);
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
            decimal multiplo = 0.05M;
            decimal resultado = Math.Round(tramo.TipoExtra * 100 / 3.0M / multiplo) * multiplo / 100;
            return resultado;
        }

        public override object Clone()
        {
            return new EtiquetaOtrasExclusivas(_servicioComisiones, _familiasIncluidas)
            {
                Venta = Venta,
                Tipo = Tipo
            };
        }
    }
}