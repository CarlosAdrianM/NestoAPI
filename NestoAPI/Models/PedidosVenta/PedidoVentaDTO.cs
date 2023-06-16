using NestoAPI.Models.PedidosBase;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace NestoAPI.Models.PedidosVenta
{
    public class PedidoVentaDTO : PedidoBase<LineaPedidoVentaDTO>
        {
        public PedidoVentaDTO() : base()
        {
            this.Lineas = new HashSet<LineaPedidoVentaDTO>();
            this.Prepagos = new HashSet<PrepagoDTO>();
            this.Efectos = new HashSet<EfectoPedidoVentaDTO>();
        }

        public string empresa { get; set; }
        public int numero { get; set; }
        public string cliente { get; set; }
        public string contacto { get; set; }
        public bool crearEfectosManualmente { get; set; }
        public Nullable<System.DateTime> fecha { get; set; }
        public string formaPago { get; set; }
        [Required]
        public string plazosPago { get; set; }
        public Nullable<System.DateTime> primerVencimiento { get; set; }
        public string iva { get; set; }
        public string vendedor { get; set; }
        public string comentarios { get; set; }
        public string comentarioPicking { get; set; }
        public string periodoFacturacion { get; set; }
        public string ruta { get; set; }
        public string serie { get; set; }
        public string ccc { get; set; }
        public string origen { get; set; }
        public string contactoCobro { get; set; }
        public decimal noComisiona { get; set; }
        public bool vistoBuenoPlazosPago { get; set; }
        public bool mantenerJunto { get; set; }
        public bool servirJunto { get; set; }
        public bool EsPresupuesto { get; set; }
        public bool notaEntrega { get; set; }
        public decimal DescuentoPP { get; set; }
        //public string Usuario { get; set; }

        //public virtual ICollection<LineaPedidoVentaDTO> Lineas { get; set; }
        public virtual ICollection<PrepagoDTO> Prepagos { get; set; }
        public virtual ICollection<VendedorGrupoProductoDTO> VendedoresGrupoProducto { get; set; }
        public virtual ICollection<EfectoPedidoVentaDTO> Efectos { get; set; }
    }
}