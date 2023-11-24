using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaFamiliasEspeciales : IEtiquetaComision, ICloneable
    {
        private NVEntities db = new NVEntities();
        private IServicioComisionesAnuales servicioComisiones;
        private IQueryable<vstLinPedidoVtaComisione> consulta;

        public EtiquetaFamiliasEspeciales(IServicioComisionesAnuales servicioComisiones)
        {
            this.servicioComisiones = servicioComisiones;
        }

        public string Nombre
        {
            get
            {
                return "Especiales";
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
                throw new Exception("La comisión de las familias especiales no se puede fijar manualmente");
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

            return servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }


        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
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
            var servicioVendedores = new ServicioVendedores();
            var listaVendedores = (servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult()).Select(v => v.vendedor);
            
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    FamiliasEspeciales.Contains(l.Familia.ToLower()) &&
                    !l.Grupo.ToLower().Equals("otros aparatos", StringComparison.OrdinalIgnoreCase) &&
                    listaVendedores.Contains(l.Vendedor)
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.TipoExtra;
        }

        public static string[] FamiliasEspeciales = { "eva visnu", "santhilea", "max2origin", "mina", "apraise", "maderas", "diagmyskin", "faby", "cursos" };

        public object Clone()
        {
            return new EtiquetaFamiliasEspeciales(servicioComisiones)
            {
                Venta = this.Venta,
                Tipo = this.Tipo
            };
        }
    }
}