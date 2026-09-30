using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#556: lo que viaja entre la API y Ariadna (la app de almacén) para preparar pedidos
    // con lector de códigos: el picking por ubicación, el packing por pedido, los escaneos y los
    // bultos con su foto.

    /// <summary>Una parada del picking: un producto en un hueco de la estantería.</summary>
    public class LineaPickingAlmacenDTO
    {
        /// <summary>Posición en el recorrido (1, 2, 3…): pasillo, columna, fila.</summary>
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Null si el producto no tiene código: se prepara con toque + cantidad.</summary>
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        /// <summary>Otro producto de este mismo picking comparte el código: hay que elegir a mano.</summary>
        public bool CodigoDuplicado { get; set; }
        public int Cantidad { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>Como se enseña siempre: pasillo/fila/columna (001/002/004). Null si no tiene ubicación.</summary>
        public string Ubicacion { get; set; }
    }

    public class PickingAlmacenDTO
    {
        public string Empresa { get; set; }
        public int Picking { get; set; }
        public List<LineaPickingAlmacenDTO> Lineas { get; set; } = new List<LineaPickingAlmacenDTO>();
    }

    public class LineaPackingAlmacenDTO
    {
        /// <summary>LinPedidoVta.[Nº Orden].</summary>
        public int LineaPedido { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        /// <summary>Otro producto del mismo grupo de pedidos comparte el código.</summary>
        public bool CodigoDuplicado { get; set; }
        /// <summary>Lo que va en la caja: la cantidad de la línea menos lo que el cliente recoge.</summary>
        public int Cantidad { get; set; }
    }

    public class PedidoPackingAlmacenDTO
    {
        public int Pedido { get; set; }
        public string ComentarioPicking { get; set; }
        public List<LineaPackingAlmacenDTO> Lineas { get; set; } = new List<LineaPackingAlmacenDTO>();
    }

    /// <summary>
    /// Lo que va a la misma dirección en el mismo picking. Se agrupa como el packing list de hoy
    /// (cliente + dirección de entrega); la app deja separar un pedido en sus propias cajas.
    /// </summary>
    public class EntregaPackingAlmacenDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Ruta { get; set; }
        public List<PedidoPackingAlmacenDTO> Pedidos { get; set; } = new List<PedidoPackingAlmacenDTO>();
    }

    /// <summary>Un picking sacado que todavía tiene líneas sin servir.</summary>
    public class PickingEnCursoDTO
    {
        public int Picking { get; set; }
        public int Lineas { get; set; }
        public int Pedidos { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>Cómo va un picking: lo que había que coger frente a lo leído y lo dado por falta.</summary>
    public class EstadoPickingDTO
    {
        public string Empresa { get; set; }
        public int Picking { get; set; }
        /// <summary>Todos los productos están resueltos: cogidos o dados por falta.</summary>
        public bool Terminado { get; set; }
        /// <summary>Terminado y sin ninguna falta ni producto de más.</summary>
        public bool Completo { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
    }

    public class PackingAlmacenDTO
    {
        public string Empresa { get; set; }
        public int Picking { get; set; }
        public List<EntregaPackingAlmacenDTO> Entregas { get; set; } = new List<EntregaPackingAlmacenDTO>();
    }

    /// <summary>Una lectura (o un toque manual, o una falta) hecha en el móvil.</summary>
    public class EscaneoAlmacenDTO
    {
        /// <summary>Lo genera el móvil. Un reenvío de la cola con el mismo valor no se guarda dos veces.</summary>
        public Guid IdCliente { get; set; }
        /// <summary>
        /// NestoAPI#574: de dónde sale el trabajo. PICK (picking de pedidos) o REPO (reposición a
        /// tienda). Si no se dice, es un picking.
        /// </summary>
        public string TipoOrigen { get; set; }
        /// <summary>El número del picking o el del traspaso, según <see cref="TipoOrigen"/>.</summary>
        public int NumeroOrigen { get; set; }
        /// <summary>Atajo para el caso de siempre: equivale a TipoOrigen = PICK y NumeroOrigen = el picking.</summary>
        public int Picking { get; set; }
        public int? Pedido { get; set; }
        public int? LineaPedido { get; set; }
        public string Producto { get; set; }
        /// <summary>PICK (recoger) o PACK (embalar).</summary>
        public string Fase { get; set; }
        /// <summary>Negativa para deshacer una lectura.</summary>
        public int Cantidad { get; set; }
        /// <summary>SCAN, MANUAL o FALTA.</summary>
        public string Metodo { get; set; }
        public int? Bulto { get; set; }
        public string Motivo { get; set; }
        public string Dispositivo { get; set; }
        /// <summary>La hora del móvil: la lectura puede subir más tarde.</summary>
        public DateTime FechaEscaneo { get; set; }
    }

    public class ResultadoEscaneosAlmacenDTO
    {
        public int Guardados { get; set; }
        /// <summary>Ya estaban (reenvío de la cola): no es un error.</summary>
        public int Repetidos { get; set; }
        /// <summary>Los que no se han podido guardar, con el motivo. El resto del lote sí se guarda.</summary>
        public List<EscaneoRechazadoDTO> Rechazados { get; set; } = new List<EscaneoRechazadoDTO>();
    }

    public class EscaneoRechazadoDTO
    {
        public Guid IdCliente { get; set; }
        public string Motivo { get; set; }
    }

    public class BultoAlmacenDTO
    {
        public int Id { get; set; }
        public Guid IdCliente { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        public int Bulto { get; set; }
        public decimal? Peso { get; set; }
        public bool TieneFoto { get; set; }
        public int? NumeroEnvio { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaFoto { get; set; }
        /// <summary>
        /// La ruta dentro del API (sin el servidor) para ver la foto sin usuario: para mandársela a
        /// un cliente o a la agencia. Vale mientras exista la foto. Null si no hay foto.
        /// </summary>
        public string RutaFotoPublica { get; set; }
        /// <summary>Solo para uso interno: no viaja al cliente.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string RutaBlob { get; set; }
    }

    /// <summary>Lo pedido frente a lo leído de un producto.</summary>
    public class DiferenciaPreparacionDTO
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public int Esperado { get; set; }
        public int Leido { get; set; }
        /// <summary>Leído menos esperado: negativo falta, positivo sobra.</summary>
        public int Diferencia => Leido - Esperado;
        /// <summary>Unidades que el mozo ha dado por falta («no está en el estante»). Solo en el picking.</summary>
        public int Faltas { get; set; }
        /// <summary>Se ha leído un producto que no está en lo que había que preparar.</summary>
        public bool Ajeno { get; set; }
    }

    public class EstadoPreparacionPedidoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Picking { get; set; }
        /// <summary>Todo lo esperado está leído, sin faltas, sobras ni productos ajenos.</summary>
        public bool Completo { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
        public List<BultoAlmacenDTO> Bultos { get; set; } = new List<BultoAlmacenDTO>();
        /// <summary>Hay al menos un bulto y todos tienen foto.</summary>
        public bool TodosLosBultosConFoto { get; set; }
    }
}

namespace NestoAPI.Models.PreparacionAlmacen
{
    // Ubicar la mercancía recibida y consultar dónde está un producto: lo que hoy se hace con
    // Ariadna Vieja (prdUbicar). De momento solo lectura.

    /// <summary>Un hueco de la estantería con lo que hay de un producto.</summary>
    public class UbicacionAlmacenDTO
    {
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>Pasillo/fila/columna, como se enseña siempre.</summary>
        public string Ubicacion { get; set; }
        /// <summary>
        /// Lo que lleva el código de barras de la etiqueta del hueco: pasillo, fila y columna con
        /// tres cifras cada uno, seguidos (002004001). Para comparar con lo que lee el lector.
        /// </summary>
        public string Codigo => CodigoDeHueco(Pasillo, Fila, Columna);
        public int Cantidad { get; set; }

        public static string CodigoDeHueco(string pasillo, string fila, string columna)
        {
            string[] partes = { pasillo?.Trim(), fila?.Trim(), columna?.Trim() };
            foreach (string parte in partes)
            {
                if (string.IsNullOrEmpty(parte) || parte.Length > 3 || !System.Linq.Enumerable.All(parte, char.IsDigit))
                {
                    return null;
                }
            }
            return partes[0].PadLeft(3, '0') + partes[1].PadLeft(3, '0') + partes[2].PadLeft(3, '0');
        }
    }

    /// <summary>De dónde viene lo que está pendiente de ubicar.</summary>
    public class OrigenPendienteDeUbicarDTO
    {
        public int? PedidoCompra { get; set; }
        public int? AlbaranCompra { get; set; }
        public int? TraspasoReposicion { get; set; }
        /// <summary>Una devolución de un cliente: el género vuelve y hay que darle hueco.</summary>
        public int? PedidoVenta { get; set; }
        /// <summary>Otro movimiento entre almacenes (montar o desmontar un kit, por ejemplo).</summary>
        public int? Traspaso { get; set; }
        public int Cantidad { get; set; }
    }

    public class ProductoPendienteDeUbicarDTO
    {
        /// <summary>Posición en el recorrido: por el hueco donde ya hay producto (pasillo, columna, fila).</summary>
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        /// <summary>Unidades recibidas que todavía no tienen hueco.</summary>
        public int Cantidad { get; set; }
        public DateTime? DesdeCuando { get; set; }
        public List<OrigenPendienteDeUbicarDTO> Origenes { get; set; } = new List<OrigenPendienteDeUbicarDTO>();
        /// <summary>Dónde hay ya de este producto: la sugerencia de dónde ubicarlo.</summary>
        public List<UbicacionAlmacenDTO> UbicacionesActuales { get; set; } = new List<UbicacionAlmacenDTO>();
        /// <summary>
        /// Cuando ahora mismo no queda de ese producto en ningún hueco: el último donde estuvo
        /// (pasillo/fila/columna). Null si nunca ha tenido hueco: es un producto nuevo.
        /// </summary>
        public string UltimaUbicacion { get; set; }
    }

    public class PendienteDeUbicarDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public List<ProductoPendienteDeUbicarDTO> Productos { get; set; } = new List<ProductoPendienteDeUbicarDTO>();
    }

    /// <summary>Ubicar en un hueco unidades recibidas que están pendientes de ubicar.</summary>
    public class UbicarProductoDTO
    {
        public string Producto { get; set; }
        /// <summary>Por defecto, Algete.</summary>
        public string Almacen { get; set; }
        /// <summary>
        /// Lo leído en la etiqueta del hueco (002004001), tal cual sale del lector. Si viene, no
        /// hace falta mandar pasillo, fila y columna; también vale escrito como 002/004/001.
        /// </summary>
        public string Hueco { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Cantidad { get; set; }
        // Opcional: de qué entrada se descuenta lo pendiente. Como mucho uno; sin ninguno, de la más antigua.
        public int? PedidoCompra { get; set; }
        public int? AlbaranCompra { get; set; }
        public int? TraspasoReposicion { get; set; }
        public int? PedidoVenta { get; set; }
    }

    /// <summary>Un producto encontrado por su código de barras o su número, con dónde está.</summary>
    public class ProductoAlmacenDTO
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public string Almacen { get; set; }
        public List<UbicacionAlmacenDTO> Ubicaciones { get; set; } = new List<UbicacionAlmacenDTO>();
        /// <summary>Unidades recibidas sin hueco asignado.</summary>
        public int PendienteDeUbicar { get; set; }
    }
}

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#559: entrada de mercancía de proveedor escaneando contra el pedido de compra.
    // De momento, leer lo que se espera y comparar con lo leído, sin guardar nada.

    public class PedidoCompraPendienteDTO
    {
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public DateTime? Fecha { get; set; }
        /// <summary>La fecha de recepción más cercana de sus líneas pendientes.</summary>
        public DateTime? FechaRecepcion { get; set; }
        public int Lineas { get; set; }
        public int Unidades { get; set; }
    }

    public class LineaRecepcionCompraDTO
    {
        /// <summary>LinPedidoCmp.NºOrden.</summary>
        public int LineaPedido { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        public bool CodigoDuplicado { get; set; }
        public int Cantidad { get; set; }
    }

    public class RecepcionCompraDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public List<LineaRecepcionCompraDTO> Lineas { get; set; } = new List<LineaRecepcionCompraDTO>();
    }

    /// <summary>Lo contado de un producto al recibir.</summary>
    public class LecturaRecepcionDTO
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
    }

    public class ResultadoRecepcionCompraDTO
    {
        public int Pedido { get; set; }
        /// <summary>Lo recibido coincide con lo pedido: sin faltas, sobras ni productos no pedidos.</summary>
        public bool Cuadra { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
    }
}

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#553 (fase 3): recibir una reposición entre almacenes leyendo los productos.
    // De momento, leer lo que se espera y comparar con lo contado, sin guardar nada.

    public class ReposicionPendienteDTO
    {
        /// <summary>PreExtrProducto.NºTraspaso: enlaza la salida del origen con la entrada del destino.</summary>
        public int Traspaso { get; set; }
        public int Lineas { get; set; }
        public int Unidades { get; set; }
        public DateTime? Fecha { get; set; }
        public string Usuario { get; set; }
    }

    public class LineaReposicionDTO
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public bool SinCodigo { get; set; }
        public bool CodigoDuplicado { get; set; }
        public int Cantidad { get; set; }
    }

    public class RecepcionReposicionDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        public int Traspaso { get; set; }
        public List<LineaReposicionDTO> Lineas { get; set; } = new List<LineaReposicionDTO>();
    }

    public class ResultadoRecepcionReposicionDTO
    {
        public int Traspaso { get; set; }
        /// <summary>Lo recibido coincide con lo enviado.</summary>
        public bool Cuadra { get; set; }
        public List<DiferenciaPreparacionDTO> Productos { get; set; } = new List<DiferenciaPreparacionDTO>();
    }
}
