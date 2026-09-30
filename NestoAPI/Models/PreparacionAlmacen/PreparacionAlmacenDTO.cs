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
        public int Picking { get; set; }
        public int? Pedido { get; set; }
        public int? LineaPedido { get; set; }
        public string Producto { get; set; }
        /// <summary>PICK o PACK.</summary>
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
