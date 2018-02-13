using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaEvaVisnu : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

        public string Nombre
        {
            get
            {
                return "Eva Visnú";
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = ServicioComisionesAnualesEstetica.FechaDesde(anno, mes);
            DateTime fechaHasta = ServicioComisionesAnualesEstetica.FechaHasta(anno, mes);
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Familia.ToLower() == "eva visnu" &&
                    l.Grupo.ToLower() != "otros aparatos" &&
                    l.Vendedor == vendedor
                );

            return ServicioComisionesAnualesEstetica.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        public ICollection<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            throw new NotImplementedException();
        }
    }
}