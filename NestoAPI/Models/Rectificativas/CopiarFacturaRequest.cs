using System.Collections.Generic;

namespace NestoAPI.Models.Rectificativas
{
    /// <summary>
    /// Request para copiar líneas de una factura a un pedido nuevo o existente.
    /// Útil para crear rectificativas rápidas o traspasar facturas entre clientes.
    /// Issue #85, #279
    /// </summary>
    public class CopiarFacturaRequest
    {
        /// <summary>
        /// Empresa de la factura origen
        /// </summary>
        public string Empresa { get; set; }

        /// <summary>
        /// Cliente de la factura origen
        /// </summary>
        public string Cliente { get; set; }

        /// <summary>
        /// Número de factura a copiar (ej: "NV26/001234").
        /// Para una sola factura. Se ignora si NumerosFactura tiene elementos.
        /// </summary>
        public string NumeroFactura { get; set; }

        /// <summary>
        /// Lista de números de factura a copiar (para selección múltiple).
        /// Si tiene elementos, se ignora NumeroFactura.
        /// Issue #279 - SelectorFacturas
        /// </summary>
        public List<string> NumerosFactura { get; set; }

        /// <summary>
        /// Si true, agrupa todas las facturas en una sola rectificativa.
        /// Si false, crea una rectificativa por cada factura.
        /// Solo aplica cuando NumerosFactura tiene más de un elemento.
        /// Issue #279
        /// </summary>
        public bool AgruparEnUnaRectificativa { get; set; } = true;

        /// <summary>
        /// Si true, invierte el signo de las cantidades (para crear rectificativa/abono)
        /// </summary>
        public bool InvertirCantidades { get; set; }

        /// <summary>
        /// Si true, anade las lineas al pedido original en lugar de crear uno nuevo
        /// </summary>
        public bool AnadirAPedidoOriginal { get; set; }

        /// <summary>
        /// Si true, mantiene precios/descuentos/IVA originales.
        /// Si false, recalcula según condiciones del cliente destino.
        /// Solo aplica si ClienteDestino es diferente al Cliente origen.
        /// </summary>
        public bool MantenerCondicionesOriginales { get; set; } = true;

        /// <summary>
        /// Si true, después de copiar crea albarán y factura automáticamente.
        /// Para mismo cliente con InvertirCantidades=true: solo factura las negativas.
        /// </summary>
        public bool CrearAlbaranYFactura { get; set; }

        /// <summary>
        /// Cliente destino. Si es null o vacío, se usa el mismo cliente origen.
        /// </summary>
        public string ClienteDestino { get; set; }

        /// <summary>
        /// Contacto del cliente destino. Requerido si ClienteDestino es diferente.
        /// </summary>
        public string ContactoDestino { get; set; }

        /// <summary>
        /// Indica si el cliente destino es diferente al origen
        /// </summary>
        public bool EsCambioCliente => !string.IsNullOrWhiteSpace(ClienteDestino) && ClienteDestino.Trim() != Cliente?.Trim();

        /// <summary>
        /// Si true, crea DOS operaciones en un solo clic:
        /// 1. Rectificativa (abono) al cliente ORIGEN de la factura
        /// 2. Factura nueva (cargo) al cliente DESTINO seleccionado
        ///
        /// Requiere que ClienteDestino esté informado.
        /// Ignora InvertirCantidades (siempre hace abono y cargo).
        /// </summary>
        public bool CrearAbonoYCargo { get; set; }

        /// <summary>
        /// Comentarios a añadir en el pedido creado.
        /// Se concatenan con el comentario automático de trazabilidad.
        /// </summary>
        public string Comentarios { get; set; }

        /// <summary>
        /// Si true, usa el vendedor de la factura original en ambos documentos (rectificativa y cargo).
        /// Si false, usa el vendedor de la ficha del cliente de cada documento.
        /// Por defecto true.
        /// </summary>
        public bool UsarVendedorFacturaOriginal { get; set; } = true;

        /// <summary>
        /// NestoAPI#699: la agencia devuelve el pedido y el cliente lo sigue queriendo. En un solo paso:
        /// 1. Abono (rectificativa con las cantidades invertidas) de la factura, como siempre; con
        ///    albarán y factura si se marca <see cref="CrearAlbaranYFactura"/>.
        /// 2. Pedido nuevo para el MISMO cliente y contacto (misma dirección de entrega), con las mismas
        ///    líneas, precios, descuentos, forma y plazos de pago y vendedor que el pedido de la factura.
        ///    Queda pendiente de servir (sin albarán ni factura) para que salga por el picking normal.
        ///
        /// Solo con una factura y sin cambio de cliente (para otro cliente está <see cref="CrearAbonoYCargo"/>).
        /// Ignora <see cref="InvertirCantidades"/> y <see cref="AnadirAPedidoOriginal"/>.
        /// </summary>
        public bool RehacerPedido { get; set; }

        /// <summary>
        /// NestoAPI#699: solo con <see cref="RehacerPedido"/>. Por defecto (false) los portes del reenvío
        /// «nos los comemos»: las líneas de portes y de comisión contra reembolso de la factura pasan al
        /// pedido nuevo con un 100 % de descuento (a 0 €), para que ni el picking ni el guardado del
        /// pedido vuelvan a cobrarlos. Con true se copian tal cual y el cliente los paga otra vez.
        /// </summary>
        public bool IncluirPortes { get; set; }

        /// <summary>
        /// NestoAPI#899: solo estos albaranes de la factura (Nº Albarán). Las líneas de cuenta contable (portes, comisión
        /// contra reembolso) de esos albaranes NO entran salvo que se pidan en <see cref="Lineas"/>. Null o vacía = toda la factura.
        /// Solo con UNA factura.
        /// </summary>
        public List<int> Albaranes { get; set; }

        /// <summary>
        /// NestoAPI#899/#863: solo estas líneas de la factura (Nº Orden de la línea del pedido facturado), y de cada una las
        /// unidades que se indiquen (sin cantidad = la línea entera). Se suman a las de <see cref="Albaranes"/>; si una línea
        /// está en los dos, manda su cantidad de aquí. Null o vacía = toda la factura (o solo los albaranes). Solo con UNA factura.
        /// Con <see cref="Albaranes"/> o <see cref="Lineas"/> la copia es FIEL: mismos precios y descuentos, sin recalcular nada.
        /// </summary>
        public List<LineaACopiarDTO> Lineas { get; set; }

        /// <summary>
        /// NestoAPI#863 (corte 1): Nº Orden de las líneas del abono cuyo género vuelve estropeado. Por ahora solo se deja
        /// constancia en los comentarios del abono para regularizar el stock a mano (dónde va se decide en el corte 2).
        /// </summary>
        public List<int> LineasNoVendibles { get; set; }

        /// <summary>NestoAPI#899: se copia solo una parte de la factura.</summary>
        public bool HaySeleccion => (Albaranes != null && Albaranes.Count > 0) || (Lineas != null && Lineas.Count > 0);
    }

    /// <summary>NestoAPI#899/#863: GET api/PedidosVenta/CopiarFactura/Lineas: las líneas de la factura para elegir qué se copia.</summary>
    public class LineasFacturaParaCopiarDTO
    {
        public string Empresa { get; set; }
        public string Factura { get; set; }
        public List<int> Albaranes { get; set; } = new List<int>();
        public List<LineaFacturaParaCopiarDTO> Lineas { get; set; } = new List<LineaFacturaParaCopiarDTO>();
    }

    public class LineaFacturaParaCopiarDTO
    {
        public int Pedido { get; set; }
        public int NumeroOrden { get; set; }
        public int? Albaran { get; set; }
        public System.DateTime? FechaAlbaran { get; set; }
        public byte? TipoLinea { get; set; }
        /// <summary>Cuenta contable (portes, comisión…): por albarán no entra, solo si se pide como línea.</summary>
        public bool EsCuentaContable { get; set; }
        /// <summary>Portes o comisión contra reembolso (cuentas 624).</summary>
        public bool EsGastosEnvio { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        /// <summary>La cantidad facturada (negativa en una rectificativa).</summary>
        public decimal Cantidad { get; set; }
        /// <summary>Lo ya abonado (rectificativas y copias sin facturar); en una rectificativa, lo ya deshecho.</summary>
        public decimal YaAbonada { get; set; }
        /// <summary>|Cantidad| − YaAbonada (nunca negativo): lo que aún se puede abonar.</summary>
        public decimal QuedanPorAbonar { get; set; }
        public decimal Precio { get; set; }
        public decimal BaseImponible { get; set; }
    }

    /// <summary>NestoAPI#899/#863: una línea de la factura que se copia (o abona) y cuántas unidades.</summary>
    public class LineaACopiarDTO
    {
        /// <summary>Nº Orden de la línea del pedido facturado.</summary>
        public int NumeroOrden { get; set; }

        /// <summary>Alias de <see cref="NumeroOrden"/> (el nombre que usa la issue #863). Solo de entrada.</summary>
        public int NumeroLinea
        {
            get => NumeroOrden;
            set => NumeroOrden = value;
        }

        public bool ShouldSerializeNumeroLinea() => false;

        /// <summary>
        /// El pedido de la línea: solo hace falta si la factura agrupa varios pedidos y ese Nº Orden está en más de uno (si no, 400).
        /// </summary>
        public int? Pedido { get; set; }

        /// <summary>Unidades (en positivo, de 1 a las de la línea). Null = la línea entera.</summary>
        public short? Cantidad { get; set; }
    }
}
