using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaUnionLaser : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

        public string Nombre
        {
            get
            {
                return "Unión Láser";
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            DateTime fechaDesde = ServicioComisionesAnualesEstetica.FechaDesde(anno, mes);
            DateTime fechaHasta = ServicioComisionesAnualesEstetica.FechaHasta(anno, mes);
            IQueryable<vstLinPedidoVtaComisione> consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.Familia.ToLower() == "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );
            decimal venta = ServicioComisionesAnualesEstetica.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);

            // OJO AQUÍ FALTA AJUSTAR LOS RENTING

            return venta;

        }

        public ICollection<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            throw new NotImplementedException();
        }
    }
}