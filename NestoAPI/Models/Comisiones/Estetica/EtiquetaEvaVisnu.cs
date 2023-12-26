using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaEvaVisnu : IEtiquetaComision
    {
        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnualesVenta _servicioComisiones;

        public EtiquetaEvaVisnu(IServicioComisionesAnualesVenta servicioComisiones)
        {
            _servicioComisiones = servicioComisiones;
        }

        public string Nombre => "Eva Visnú";

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Eva Visnú no se puede fijar manualmente");
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta(vendedor);

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }


        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private void CrearConsulta(string vendedor)
        {
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>
                    (l.Familia.ToLower() == "eva visnu" || l.Empresa == "4") &&
                    l.Grupo.ToLower() != "otros aparatos" &&
                    listaVendedores.Contains(l.Vendedor)
                );
        }

        public decimal SetTipo(TramoComision tramo) => tramo.TipoExtra;

        public object Clone() => new EtiquetaEvaVisnu(_servicioComisiones)
        {
            Venta = this.Venta,
            Tipo = this.Tipo
        };
    }
}