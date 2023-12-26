using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaOtrasExclusivas : IEtiquetaComision, ICloneable
    {
        private IServicioComisionesAnualesVenta _servicioComisiones;
        private IQueryable<vstLinPedidoVtaComisione> consulta;

        public EtiquetaOtrasExclusivas(IServicioComisionesAnualesVenta servicioComisiones)
        {
            this._servicioComisiones = servicioComisiones;
        }

        public string Nombre => "Otras Exclusivas";

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
                throw new Exception("La comisión de las otras exclusivas no se puede fijar manualmente");
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

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }


        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private void CrearConsulta(string vendedor)
        {
            var listaVendedores = _servicioComisiones.ListaVendedores(vendedor);

            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>
                    FamiliasIncluidas.Contains(l.Familia.ToLower()) &&
                    !l.Grupo.ToLower().Equals("otros aparatos", StringComparison.OrdinalIgnoreCase) &&
                    listaVendedores.Contains(l.Vendedor)
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            decimal multiplo = 0.05M;
            decimal resultado = Math.Round((tramo.TipoExtra * 100 / 3.0M) / multiplo) * multiplo / 100;
            return resultado;
        }

        public static string[] FamiliasIncluidas = { "anubismed", "anubis", "belclinic", "cazcarra", "cv", "maystar" };

        public object Clone()
        {
            return new EtiquetaFamiliasEspeciales(_servicioComisiones)
            {
                Venta = this.Venta,
                Tipo = this.Tipo
            };
        }
    }
}