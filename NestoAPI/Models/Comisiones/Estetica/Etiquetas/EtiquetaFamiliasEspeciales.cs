using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaFamiliasEspeciales : EtiquetaComisionVentaBase, IEtiquetaComisionVenta, ICloneable
    {
        protected IServicioComisionesAnuales _servicioComisiones;
        protected IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly string[] _familiasIncluidas;

        public EtiquetaFamiliasEspeciales(IServicioComisionesAnuales servicioComisiones, string[] familiasIncluidas)
        {
            _servicioComisiones = servicioComisiones;
            _familiasIncluidas = familiasIncluidas;
        }

        public override string Nombre => "Familias Especiales";

        public override decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2); set => throw new Exception("La comisión de las familias especiales no se puede fijar manualmente");
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
            return tramo.TipoExtra;
        }

        public override object Clone()
        {
            return new EtiquetaFamiliasEspeciales(_servicioComisiones, _familiasIncluidas)
            {
                Venta = Venta,
                Tipo = Tipo
            };
        }
    }
}