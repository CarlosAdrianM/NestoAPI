using NestoAPI.Models.PedidosBase;
using Newtonsoft.Json;
using System;

namespace NestoAPI.Models.PedidosVenta
{
    public class LineaPedidoVentaDTO : LineaPedidoBase
    {
        [JsonIgnore]
        public PedidoVentaDTO Pedido { get; set; }
        public int id { get; set; }
        public string almacen { get; set; }
        public string delegacion { get; set; }
        public short estado { get; set; }

        // Issue #299: estado del PRODUCTO (Productos.Estado / LinPedidoVta.EstadoProducto), distinto
        // del estado de la LÍNEA de venta (propiedad `estado`: -1 pendiente, 1 en curso...). Lo usa
        // GestorPortes para la regla de "sobre pedido" (#211). El servidor lo rellena en el GET y lo
        // recalcula en PUT/POST (RellenarEstadoProducto); si viene null se trata como no sobre pedido.
        public short? EstadoProducto { get; set; }
        public System.DateTime fechaEntrega { get; set; }
        public string formaVenta { get; set; }
        public string GrupoProducto { get; set; }
        public string iva { get; set; }
        public Nullable<int> oferta { get; set; }
        public int picking { get; set; }
        public string SubgrupoProducto { get; set; }
        public string texto { get; set; }
        public Nullable<byte> tipoLinea { get; set; }
        public string usuario { get; set; }
        public bool vistoBueno { get; set; }
        public decimal precioTarifa { get; set; }
        public int? Albaran { get; set; }
        public string Factura { get; set; }

        // Carlos 09/12/25: Issue #253/#52 - Indica si el producto es ficticio (cuenta contable, etc.)
        // Se usa para determinar si se puede cambiar el almacén de la línea
        public bool EsFicticio { get; set; }

        // Issue #279: regalo del sistema Ganavisiones, CONFIRMADO contra la tabla Ganavision al leer
        // el pedido (no la heurística de texto '(BONIFICADO)', que se trunca a 50 caracteres). Permite
        // a los clientes (Nesto#397) reconstruir en la plantilla qué líneas a 0€ son Ganavisiones y
        // cuáles MMP/regalos por importe. Solo se rellena en el GET; en escrituras se ignora (derivado).
        public bool EsBonificadoGanavisiones { get; set; }

        // Carlos 23/10/25: para controlar en modificaciones qué líneas son nuevas o tienen cantidad modificada
        [JsonIgnore]
        public int? CantidadAnterior { get; set; }
        [JsonIgnore]
        public string ProductoAnterior { get; set; }
        [JsonIgnore]
        public bool EsLineaNueva => id == 0;
        [JsonIgnore]
        public bool CambioProducto => !EsLineaNueva && ProductoAnterior != null && ProductoAnterior.Trim() != Producto?.Trim();

        // Issue #237: una línea preexistente que NO se toca (misma cantidad y precio que en BD) y que
        // no pertenece a una oferta no debe re-validar su descuento al modificar/unir el pedido: una
        // subida de tarifa posterior la bloquearía sin haberla tocado. Lo marca el controller; el
        // validador de descuentos omite el producto solo si TODAS sus líneas están marcadas.
        [JsonIgnore]
        public bool NoRevalidarDescuento { get; set; }

        /// <summary>
        /// Issue #237: la línea es preexistente, no se ha tocado (misma cantidad y precio que en BD) y
        /// no pertenece a una oferta. En ese caso su descuento no debe re-validarse (era correcto al
        /// crearse; una subida de tarifa posterior no debe bloquearla). Si cambia cantidad/precio, es
        /// nueva, o tiene oferta, devuelve false → se valida con normalidad.
        /// </summary>
        public bool EsIntactaParaDescuento(int cantidadBD, decimal precioBD)
            => !EsLineaNueva && oferta == null && Cantidad == cantidadBD && PrecioUnitario == precioBD;

        public override decimal SumaDescuentos
        {
            get
            {
                decimal descuentoPP = Pedido == null ? 0 : Pedido.DescuentoPP;
                return AplicarDescuento ? 1 - ((1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea) * (1 - descuentoPP)) : 1 - ((1 - DescuentoLinea) * (1 - descuentoPP));
            }
        }

        /// <summary>
        /// Suma de los descuentos de línea SIN el descuento pronto pago.
        /// Se usa para mostrar en el correo de pedidos, donde el PP se muestra aparte.
        /// </summary>
        public decimal SumaDescuentosSinPP => AplicarDescuento
            ? 1 - ((1 - DescuentoEntidad) * (1 - DescuentoProducto) * (1 - DescuentoLinea))
            : DescuentoLinea;
    }

}