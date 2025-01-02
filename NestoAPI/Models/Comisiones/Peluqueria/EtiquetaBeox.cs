using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class EtiquetaBeox : IEtiquetaComisionVenta
    {
        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnuales _servicioComisiones;

        public EtiquetaBeox(IServicioComisionesAnuales servicioComisiones)
        {
            this._servicioComisiones = servicioComisiones;
        }

        public string Nombre => "Beox";

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Beox no se puede fijar manualmente");
        }
        public bool EsComisionAcumulada => false;
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

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private Expression<Func<vstLinPedidoVtaComisione, bool>> PredicadoFiltro()
        {
            return l => l.Familia.ToLower() == "beox";
        }
        
        public bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }
        private void CrearConsulta(string vendedor)
        {
            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>l.Vendedor == vendedor)
                .Where(PredicadoFiltro());
        }

        public decimal SetTipo(TramoComision tramo) => tramo.TipoExtra;

        public object Clone() => new EtiquetaBeox(_servicioComisiones)
        {
            Venta = this.Venta,
            Tipo = this.Tipo
        };
    }
}