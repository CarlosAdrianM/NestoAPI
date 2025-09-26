using System;
using System.Linq;
using System.Linq.Expressions;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class EtiquetaKach : EtiquetaComisionVentaBase, IEtiquetaComisionVenta
    {
        private IQueryable<vstLinPedidoVtaComisione> consulta;

        private readonly IServicioComisionesAnuales _servicioComisiones;

        public EtiquetaKach(IServicioComisionesAnuales servicioComisiones)
        {
            _servicioComisiones = servicioComisiones;
        }
        public override string Nombre => "Kach";

        public override decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Kach no se puede fijar manualmente");
        }
        public override bool EsComisionAcumulada => false;
        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public override decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
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

        public override IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
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
            return l => l.Familia.ToLower() == "kach";
        }

        public override bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            var filtro = PredicadoFiltro().Compile();
            return filtro(linea);
        }
        private void CrearConsulta(string vendedor)
        {
            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l => l.Vendedor == vendedor)
                .Where(PredicadoFiltro());
        }

        public override decimal SetTipo(TramoComision tramo)
        {
            return tramo.TipoExtra;
        }

        public override object Clone()
        {
            return new EtiquetaKach(_servicioComisiones)
            {
                Venta = Venta,
                Tipo = Tipo
            };
        }
    }
}