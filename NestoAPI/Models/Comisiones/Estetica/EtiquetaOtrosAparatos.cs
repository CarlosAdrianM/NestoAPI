using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaOtrosAparatos : IEtiquetaComision
    {
        private const decimal TIPO_FIJO_OTROSAPARATOS = .02M;

        private NVEntities db = new NVEntities();

        IQueryable<vstLinPedidoVtaComisione> consulta;

        public string Nombre
        {
            get
            {
                return "Otros Aparatos";
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
                throw new Exception("La comisión de Otros Aparatos no se puede fijar manualmente");
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            CrearConsulta(vendedor);

            return ServicioComisionesAnualesComun.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }
        
        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return ServicioComisionesAnualesComun.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        private void CrearConsulta(string vendedor)
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.Grupo.ToLower() == "otros aparatos" &&
                    l.EstadoFamilia == 0
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return TIPO_FIJO_OTROSAPARATOS;
        }
    }
}