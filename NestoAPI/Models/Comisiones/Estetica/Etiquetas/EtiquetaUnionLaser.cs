using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaUnionLaser : EtiquetaComisionVentaBase, IEtiquetaComisionVenta
    {

        private readonly decimal _tipoFijoUnionLaser;
        private readonly IServicioComisionesAnuales _servicioComisiones;

        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private IQueryable<vstLinPedidoVtaComisione> consultaRenting;

        public EtiquetaUnionLaser(IServicioComisionesAnuales servicioComisiones)
        {
            _servicioComisiones = servicioComisiones;
            _tipoFijoUnionLaser = 0.1M;
        }
        public EtiquetaUnionLaser(IServicioComisionesAnuales servicioComisiones, decimal tipoFijoUnionLaser)
        {
            _servicioComisiones = servicioComisiones;
            _tipoFijoUnionLaser = tipoFijoUnionLaser;
        }

        public override string Nombre => "Unión Láser";

        public override decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Unión Láser no se puede fijar manualmente");
        }
        public override bool EsComisionAcumulada => false;
        public override bool SumaEnTotalVenta => true;

        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            const decimal PORCENTAJE_BASE_IMPONIBLE_RENTING = .75M;
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            CrearConsultaRenting(vendedor, incluirAlbaranes, fechaDesde, fechaHasta);
            decimal ventaRenting = consultaRenting.Select(l => (decimal)l.PrecioTarifa * PORCENTAJE_BASE_IMPONIBLE_RENTING).DefaultIfEmpty().Sum();

            CrearConsulta(vendedor, fechaDesde);
            decimal venta = _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);

            return venta + ventaRenting;

        }

        public override IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consultaRenting == null)
            {
                CrearConsultaRenting(vendedor, incluirAlbaranes, fechaDesde, fechaHasta);
            }

            if (consulta == null)
            {
                CrearConsulta(vendedor, fechaDesde);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private Expression<Func<vstLinPedidoVtaComisione, bool>> PredicadoFiltro()
        {
            return l => l.Familia.ToLower() == "unionlaser" &&
                        l.Grupo.ToLower() != "otros aparatos";
        }
        private void CrearConsulta(string vendedor, DateTime fecha)
        {
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l => listaVendedores.Contains(l.Vendedor))
                .Where(PredicadoFiltro())
                .Except(consultaRenting);
        }

        public override bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }

        private void CrearConsultaRenting(string vendedor, bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta)
        {
            var facturasRenting = _servicioComisiones.Db.RentingFacturas.Select(r => r.Numero);
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consultaRenting = _servicioComisiones.Db.vstLinPedidoVtaComisiones.Where(l => listaVendedores.Contains(l.Vendedor) && facturasRenting.Contains(l.Nº_Factura));

            consultaRenting = incluirAlbaranes
                ? consultaRenting.Where(l => l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4))
                : consultaRenting.Where(l => l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4);
        }

        public override decimal SetTipo(TramoComision tramo)
        {
            return _tipoFijoUnionLaser + tramo.TipoExtra;
        }

        public override object Clone()
        {
            return new EtiquetaUnionLaser(_servicioComisiones, _tipoFijoUnionLaser)
            {
                Venta = Venta,
                Tipo = Tipo
            };
        }
    }
}