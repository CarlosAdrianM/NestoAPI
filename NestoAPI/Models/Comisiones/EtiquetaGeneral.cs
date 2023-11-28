using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaGeneral : IEtiquetaComision
    {
        IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnuales _servicioComisiones;

        public EtiquetaGeneral(IServicioComisionesAnuales servicioComisiones)
        {
            this._servicioComisiones = servicioComisiones;
        }

        public string Nombre => "General";

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision { get; set; }
        

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);
            consulta = consulta
                .Where(l =>
                    listaVendedores.Contains(l.Vendedor)
                );

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public decimal SetTipo(TramoComision tramo) => tramo.Tipo;
        private void CrearConsulta()
        {
            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.EstadoFamilia == 0 &&
                    l.Familia.ToLower() != "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );
        }

        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);
            consulta = consulta
                .Where(l =>
                    listaVendedores.Contains(l.Vendedor)
                );

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public object Clone() => new EtiquetaGeneral(_servicioComisiones)
        {
            Venta = this.Venta,
            Tipo = this.Tipo,
            Comision = this.Comision
        };
    }
}