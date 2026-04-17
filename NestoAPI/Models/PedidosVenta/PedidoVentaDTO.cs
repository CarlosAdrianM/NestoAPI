using NestoAPI.Models.PedidosBase;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;

namespace NestoAPI.Models.PedidosVenta
{
    public class PedidoVentaDTO : PedidoBase<LineaPedidoVentaDTO>
    {
        public PedidoVentaDTO() : base()
        {
            Lineas = new HashSet<LineaPedidoVentaDTO>();
            Prepagos = new HashSet<PrepagoDTO>();
            Efectos = new List<EfectoPedidoVentaDTO>();
        }
        public bool Agrupada { get; set; }
        public string ccc { get; set; }
        public string cliente { get; set; }
        public string comentarios { get; set; }
        public string comentarioPicking { get; set; }
        public string contacto { get; set; }
        public string contactoCobro { get; set; }
        public bool crearEfectosManualmente { get; set; }
        public decimal DescuentoCliente
        {
            get => DescuentoEntidad;
            set => DescuentoEntidad = value;
        }
        public string empresa { get; set; }
        public bool EsPresupuesto { get; set; }
        public Nullable<System.DateTime> fecha { get; set; }
        public string formaPago { get; set; }
        public string iva { get; set; }
        public bool mantenerJunto { get; set; }
        public decimal noComisiona { get; set; }
        public bool notaEntrega { get; set; }
        public int numero { get; set; }
        public string origen { get; set; }
        public string periodoFacturacion { get; set; }
        [Required]
        public string plazosPago { get; set; }
        public Nullable<System.DateTime> primerVencimiento { get; set; }
        public string ruta { get; set; }
        public string serie { get; set; }
        public bool servirJunto { get; set; }
        public string suPedido { get; set; }
        public string vendedor { get; set; }
        public bool vistoBuenoPlazosPago { get; set; }
        public bool CreadoSinPasarValidacion { get; set; }
        /// <summary>
        /// Issue #159: permite al vendedor marcar un pedido para que no se le cobre la comisión
        /// por contra reembolso, aunque la forma de pago sea EFC. Solo tiene efecto si la fecha
        /// actual es anterior a <see cref="Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO"/>;
        /// a partir de esa fecha el backend ignora el flag y siempre aplica la comisión si procede.
        /// </summary>
        public bool NoCobrarComisionReembolso { get; set; }

        public virtual ICollection<PrepagoDTO> Prepagos { get; set; }
        public virtual ICollection<VendedorGrupoProductoDTO> VendedoresGrupoProducto { get; set; }
        public virtual ICollection<EfectoPedidoVentaDTO> Efectos { get; set; }

        /// <summary>
        /// Ajusta el último efecto manual para que la suma de efectos coincida con el total del pedido.
        /// Evita descuadres por redondeo (ej: 100.01 / 2 = 50.005 → 50.01 + 50.01 = 100.02).
        /// </summary>
        public void CuadrarEfectos()
        {
            if (!crearEfectosManualmente || Efectos == null || !Efectos.Any())
            {
                return;
            }
            decimal sumaEfectos = Efectos.Sum(e => e.Importe);
            decimal diferencia = Total - sumaEfectos;
            if (diferencia != 0)
            {
                Efectos.Last().Importe += diferencia;
            }
        }

        [OnDeserialized]
        private void OnDeserializedMethod(StreamingContext context)
        {
            // Establecer la referencia Pedido en cada línea después de la deserialización
            foreach (LineaPedidoVentaDTO linea in Lineas)
            {
                linea.Pedido = this;
            }
        }
    }
}