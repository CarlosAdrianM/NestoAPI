using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaUnionLaser : IEtiquetaComisionVenta
    {
        
        private decimal _tipoFijoUnionLaser;
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

        public string Nombre => "Unión Láser";

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Unión Láser no se puede fijar manualmente");
        }
        public bool EsComisionAcumulada => false;
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
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

        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComisionVenta.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
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

        public bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }

        private void CrearConsultaRenting(string vendedor, bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta)
        {
            var facturasRenting = _servicioComisiones.Db.RentingFacturas.Select(r => r.Numero);
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consultaRenting = _servicioComisiones.Db.vstLinPedidoVtaComisiones.Where(l => listaVendedores.Contains(l.Vendedor) && facturasRenting.Contains(l.Nº_Factura));

            if (incluirAlbaranes)
            {
                consultaRenting = consultaRenting.Where(l => l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else
            {
                consultaRenting = consultaRenting.Where(l => l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4);
            }
        }

        public virtual decimal SetTipo(TramoComision tramo) => _tipoFijoUnionLaser + tramo.TipoExtra;

        public object Clone() => new EtiquetaUnionLaser(_servicioComisiones, _tipoFijoUnionLaser)
        {
            Venta = this.Venta,
            Tipo = this.Tipo
        };
    }
}