using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaEvaVisnu : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

        private IQueryable<vstLinPedidoVtaComisione> consulta;

        public string Nombre
        {
            get
            {
                return "Eva Visnú";
            }
        }

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get
            {
                return Math.Round(Venta * Tipo, 2);
            }
            set
            {
                throw new Exception("La comisión de Eva Visnú no se puede fijar manualmente");
            }
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

            return ServicioComisionesAnualesComun.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }


        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return ServicioComisionesAnualesComun.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private void CrearConsulta(string vendedor)
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    (l.Familia.ToLower() == "eva visnu" || l.Empresa == "4") &&
                    l.Grupo.ToLower() != "otros aparatos" &&
                    l.Vendedor == vendedor
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.TipoExtra;
        }
    }
}