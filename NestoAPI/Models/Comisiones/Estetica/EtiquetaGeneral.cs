using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaGeneral : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

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
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.EstadoFamilia == 0 &&
                    l.Familia.ToLower() != "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );

            return ServicioComisionesAnualesEstetica.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        public ICollection<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            throw new NotImplementedException();
        }
    }
}