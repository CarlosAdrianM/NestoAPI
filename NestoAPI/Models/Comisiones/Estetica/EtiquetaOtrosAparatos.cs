using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaOtrosAparatos : IEtiquetaComision, ICloneable
    {
        private const decimal TIPO_FIJO_OTROSAPARATOS = .02M;

        private NVEntities db = new NVEntities();
        IQueryable<vstLinPedidoVtaComisione> consulta;
        private IServicioComisionesAnuales servicioComisiones;

        public EtiquetaOtrosAparatos(IServicioComisionesAnuales servicioComisiones)
        {
            this.servicioComisiones = servicioComisiones;
        }

        public string Nombre
        {
            get
            {
                return "Otros Aparatos";
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
                throw new Exception("La comisión de Otros Aparatos no se puede fijar manualmente");
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
                    listaVendedores.Contains(l.Vendedor) &&
                    l.Grupo.ToLower() == "otros aparatos" &&
                    l.EstadoFamilia == 0
                );
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return TIPO_FIJO_OTROSAPARATOS;
        }

        public object Clone()
        {
            // Crea una nueva instancia de EtiquetaGeneral y copia las propiedades
            return new EtiquetaOtrosAparatos(servicioComisiones)
            {
                Venta = this.Venta,
                Tipo = this.Tipo
            };
        }
    }
}