using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaUnionLaser : IEtiquetaComision
    {
        private const decimal TIPO_FIJO_UNIONLASER = .1M;

        private NVEntities db = new NVEntities();

        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private IQueryable<vstLinPedidoVtaComisione> consultaRenting;

        public string Nombre
        {
            get
            {
                return "Unión Láser";
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
                throw new Exception("La comisión de Unión Láser no se puede fijar manualmente");
            }
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            const decimal PORCENTAJE_BASE_IMPONIBLE_RENTING = .75M;
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
                        
            CrearConsultaRenting(vendedor, incluirAlbaranes, fechaDesde, fechaHasta);
            decimal ventaRenting = consultaRenting.Select(l => (decimal)l.PrecioTarifa * PORCENTAJE_BASE_IMPONIBLE_RENTING).DefaultIfEmpty().Sum();

            CrearConsulta(vendedor);
            decimal venta = ServicioComisionesAnualesComun.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
            
            return venta + ventaRenting;

        }

        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            
            if (consultaRenting == null)
            {
                CrearConsultaRenting(vendedor, incluirAlbaranes, fechaDesde, fechaHasta);
            }

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return ServicioComisionesAnualesComun.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta);
        }

        private void CrearConsulta (string vendedor)
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Vendedor == vendedor &&
                    l.Familia.ToLower() == "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                )
                .Except(consultaRenting);
        }

        private void CrearConsultaRenting(string vendedor, bool incluirAlbaranes, DateTime fechaDesde, DateTime fechaHasta)
        {
            var facturasRenting = db.RentingFacturas.Select(r => r.Numero);
            consultaRenting = db.vstLinPedidoVtaComisiones.Where(l => l.Vendedor == vendedor && facturasRenting.Contains(l.Nº_Factura));

            if (incluirAlbaranes)
            {
                consultaRenting = consultaRenting.Where(l => l.Estado == 2 || (l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4));
            }
            else
            {
                consultaRenting = consultaRenting.Where(l => l.Fecha_Factura >= fechaDesde && l.Fecha_Factura <= fechaHasta && l.Estado == 4);
            }
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return TIPO_FIJO_UNIONLASER + tramo.TipoExtra;
        }
    }
}