using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaGeneral : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

        IQueryable<vstLinPedidoVtaComisione> consulta;

        public EtiquetaGeneral()
        {

        }

        public string Nombre {
            get {
                return "General";
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = ServicioComisionesAnualesEstetica.FechaDesde(anno, mes);
            DateTime fechaHasta = ServicioComisionesAnualesEstetica.FechaHasta(anno, mes);
            CrearConsulta();
            consulta = consulta
                .Where(l =>
                    l.Vendedor == vendedor
                );

            return ServicioComisionesAnualesEstetica.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        private void CrearConsulta()
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.EstadoFamilia == 0 &&
                    l.Familia.ToLower() != "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );
        }

        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            DateTime fechaDesde = ServicioComisionesAnualesEstetica.FechaDesde(anno, mes);
            DateTime fechaHasta = ServicioComisionesAnualesEstetica.FechaHasta(anno, mes);
            CrearConsulta();
            consulta = consulta
                .Where(l =>
                    l.Vendedor == vendedor
                );
                
            return ServicioComisionesAnualesEstetica.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }
    }
}