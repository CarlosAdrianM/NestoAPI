using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class EtiquetaLisap : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();
        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnuales servicioComisiones;

        public EtiquetaLisap(IServicioComisionesAnuales servicioComisiones)
        {
            this.servicioComisiones = servicioComisiones;
        }

        public string Nombre
        {
            get
            {
                return "Lisap";
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
                throw new Exception("La comisión de Lisap no se puede fijar manualmente");
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

            if (fechaDesde < new DateTime(2018, 4, 1))
            {
                throw new Exception("Las comisiones anuales de peluquería entraron en vigor el 01/04/18");
            }

            CrearConsulta(vendedor);

            return servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private void CrearConsulta(string vendedor)
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Familia.ToLower() == "lisap" &&
                    l.Vendedor == vendedor
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.TipoExtra;
        }


        public object Clone()
        {
            return new EtiquetaLisap(servicioComisiones)
            {
                Venta = this.Venta,
                Tipo = this.Tipo
            };
        }
    }
}