using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PreparacionAlmacen
{
    // NestoAPI#556: lo que viaja entre la API y Ariadna (la app de almacén) para preparar pedidos
    // con lector de códigos: el picking por ubicación, el packing por pedido, los escaneos y los
    // bultos con su foto.

    /// <summary>
    /// Ariadna (03/10/26): lo que hace falta ver para no equivocarse de producto, además del nombre: la familia, el
    /// subgrupo, el tamaño y la unidad de medida. Lo rellena siempre <c>FichasProductoAlmacen</c> (un solo sitio).
    /// </summary>
    /// <summary>NestoAPI#605: una línea que se casa por código de barras. CodigoBarras es el principal (el de la ficha);
    /// CodigosBarras, todos los activos del producto (el principal, el primero), para casar por cualquiera.</summary>
    public interface IConCodigosBarras
    {
        string Producto { get; }
        string CodigoBarras { get; }
        List<string> CodigosBarras { get; set; }
    }

    /// <summary>
    /// NestoAPI#653 (Ariadna#35): lo mínimo para enseñar la foto de un producto junto a su nombre (reposiciones,
    /// diferencias). La rellena siempre <c>FichasProductoAlmacen</c>: una llamada por respuesta, para todos a la vez.
    /// </summary>
    public interface IConFotoProducto
    {
        string Producto { get; }
        /// <summary>Ariadna#2: la foto del producto (la de la tienda). Null si no tiene: la app no enseña nada.</summary>
        string UrlFoto { get; set; }
    }

    /// <summary>
    /// Ariadna#47 (Carlos, 19/10/26): otro producto con stock en el mismo hueco que la línea, para que el mozo distinga los
    /// que se parecen (SERUM / MÁSCARA LEVEL LASH): lo que no comparten se resalta en la app.
    /// </summary>
    public class ProductoMismoHuecoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        /// <summary>Las 5 últimas cifras del código de barras principal, solo si es un GTIN válido; si no, null.</summary>
        public string UltimasCifras { get; set; }
    }

    /// <summary>
    /// Ariadna#47: una línea que se enseña con su hueco y lleva los demás productos que hay en él. La rellena siempre
    /// <c>FichasProductoAlmacen.CompletarMismoHueco</c>: una consulta por respuesta, para todos los huecos a la vez.
    /// </summary>
    public interface IConProductosMismoHueco
    {
        string Producto { get; }
        /// <summary>El hueco con el que se compara (pasillo, fila y columna). Null: la línea no tiene hueco y no lleva nada.</summary>
        UbicacionAlmacenDTO HuecoDeReferencia { get; }
        /// <summary>
        /// Los demás productos con stock en ese hueco (sin el propio). Null si la línea no tiene hueco o no se ha podido
        /// leer (la app compara entonces con los de su propia lista); vacía si no hay ninguno más.
        /// </summary>
        List<ProductoMismoHuecoDTO> ProductosMismoHueco { get; set; }
    }

    /// <summary>
    /// NestoAPI#681 (Andre, 20/10/26): hay productos muy parecidos, con códigos de barras sin los números impresos, que solo se
    /// distinguen por la referencia del proveedor. La del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1),
    /// recortada; null si no hay. La rellena siempre <c>FichasProductoAlmacen</c>: una consulta por respuesta, para todos a la vez.
    /// </summary>
    public interface IConReferenciaProveedor : IConFotoProducto
    {
        string ReferenciaProveedor { get; set; }
    }

    public interface IConFichaProducto : IConReferenciaProveedor
    {
        /// <summary>El nombre de la familia (Familias.Descripción), no su código.</summary>
        string Familia { get; set; }
        /// <summary>El nombre del subgrupo (SubGruposProducto.Descripción), no su código.</summary>
        string Subgrupo { get; set; }
        short? Tamano { get; set; }
        string UnidadMedida { get; set; }
    }

    /// <summary>Una parada del picking: un producto en un hueco de la estantería.</summary>
    public class LineaPickingAlmacenDTO : IConCodigosBarras, IConProductosMismoHueco
    {
        /// <summary>Posición en el recorrido (1, 2, 3…): pasillo, columna, fila.</summary>
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Null si el producto no tiene código: se prepara con toque + cantidad.</summary>
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
        public bool SinCodigo { get; set; }
        /// <summary>Otro producto de este mismo picking comparte el código: hay que elegir a mano.</summary>
        public bool CodigoDuplicado { get; set; }
        public int Cantidad { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        /// <summary>Familia (Familias.Descripción): con ella se ordena lo que no tiene ubicación (las tiendas), como el papel de la reposición.</summary>
        public string Familia { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>Como se enseña siempre: pasillo/fila/columna (001/002/004). Null si no tiene ubicación.</summary>
        public string Ubicacion { get; set; }
        /// <summary>Ariadna#47: los demás productos con stock en este hueco. Ver <see cref="IConProductosMismoHueco"/>.</summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<ProductoMismoHuecoDTO> ProductosMismoHueco { get; set; }

        UbicacionAlmacenDTO IConProductosMismoHueco.HuecoDeReferencia => UbicacionAlmacenDTO.De(Pasillo, Fila, Columna);
    }

    public class PickingAlmacenDTO
    {
        public string Empresa { get; set; }
        /// <summary>Ariadna#47: el almacén de los huecos (para leer qué más hay en ellos). No viaja.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Almacen { get; set; }
        public int Picking { get; set; }
        public List<LineaPickingAlmacenDTO> Lineas { get; set; } = new List<LineaPickingAlmacenDTO>();
    }

    /// <summary>
    /// NestoAPI#574: algo que hay por recoger en un almacén, venga de un picking de pedidos o de
    /// una reposición a tienda. Para el mozo es el mismo trabajo.
    /// </summary>
    public class RecogidaPendienteDTO
    {
        /// <summary>PICK (picking de pedidos) o REPO (reposición a tienda).</summary>
        public string Tipo { get; set; }
        /// <summary>El número del picking o el del traspaso.</summary>
        public int Numero { get; set; }
        /// <summary>A dónde va lo recogido: «Mesa de packing», o la tienda (el código del almacén: REI).</summary>
        public string Destino { get; set; }
        public int Lineas { get; set; }
        /// <summary>Solo en un picking: cuántos pedidos lleva.</summary>
        public int? Pedidos { get; set; }
        public int Unidades { get; set; }

        // NestoAPI#600: lo que depende del tipo, ya resuelto por su estrategia (la app no mira el código del tipo)

        /// <summary>El nombre del destino para enseñarlo: «Mesa de packing», «Reina» (Almacenes.Descripción). Nunca el código.</summary>
        public string NombreDestino { get; set; }
        /// <summary>«Picking 99757», «Reposición 80905 hacia Reina». Siempre viene.</summary>
        public string Titulo { get; set; }
        /// <summary>La línea de debajo del título: «4 pedidos · 12 líneas · 30 uds → Mesa de packing». Siempre viene.</summary>
        public string Detalle { get; set; }
        /// <summary>Lo recogido se empaqueta después (hoy, el picking): la app ofrece el packing (F2) y lo pinta como «Packing».</summary>
        public bool TienePacking { get; set; }
    }

    /// <summary>Una parada del recorrido con lo que ya está hecho de ella.</summary>
    public class LineaRecogidaDTO : LineaPickingAlmacenDTO, IConFichaProducto
    {
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la referencia del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1), recortada. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        /// <summary>Unidades de esta parada ya cogidas o dadas por falta.</summary>
        public int Resuelto { get; set; }
        /// <summary>
        /// De lo resuelto, lo dado por falta (Ariadna «La encontré», Carlos 13/10/26): la app lo puede deshacer aunque la
        /// falta se diera en otra sesión u otra PDA.
        /// </summary>
        public int Faltas { get; set; }
        public int Pendiente => Math.Max(0, Cantidad - Resuelto);
        /// <summary>
        /// NestoAPI#640: quién ha resuelto esta parada (cada PDA o móvil que lee la misma salida), para refrescar lo que
        /// coge la otra y avisar antes de dar por falta lo que otro ya ha leído. Vacía si nadie ha leído nada de ella.
        /// Suma lo mismo que <see cref="Resuelto"/> (Leido) y <see cref="Faltas"/>.
        /// </summary>
        public List<LectorLineaRecogidaDTO> Lectores { get; set; } = new List<LectorLineaRecogidaDTO>();
    }

    /// <summary>NestoAPI#640: lo que ha leído y dado por falta un usuario en una parada.</summary>
    public class LectorLineaRecogidaDTO
    {
        /// <summary>El usuario con el que se leyó (PreparacionEscaneos.Usuario).</summary>
        public string Usuario { get; set; }
        /// <summary>Unidades cogidas (sin las faltas).</summary>
        public int Leido { get; set; }
        public int Faltas { get; set; }
        /// <summary>Es el mismo usuario que pregunta: lo suyo no hay que avisárselo.</summary>
        public bool Propio { get; set; }
    }

    /// <summary>
    /// El recorrido de una recogida con cómo va, en una sola llamada: la app abre directamente en la
    /// parada por la que iba (<see cref="SiguienteOrden"/>), sin que el mozo toque nada.
    /// </summary>
    public class RecogidaAlmacenDTO
    {
        public string Empresa { get; set; }
        /// <summary>
        /// Ariadna#47: el almacén de donde sale (el de los huecos), para leer qué más hay en ellos. Ariadna#12: viaja, para que
        /// la app no suponga Algete al cambiar de hueco. Null si no se sabe.
        /// </summary>
        public string Almacen { get; set; }
        public string Tipo { get; set; }
        public int Numero { get; set; }
        public string Destino { get; set; }
        public List<LineaRecogidaDTO> Lineas { get; set; } = new List<LineaRecogidaDTO>();
        /// <summary>La primera parada con algo por coger. Null si no queda nada.</summary>
        public int? SiguienteOrden { get; set; }
        /// <summary>No queda nada por resolver: todo cogido o dado por falta.</summary>
        public bool Terminada { get; set; }
        /// <summary>Terminada y sin faltas ni producto de más.</summary>
        public bool Completa { get; set; }
        /// <summary>Ya se ha terminado en el servidor (PreparacionSalidasTerminadas): no hay que volver a pulsar Terminar.</summary>
        public bool Cerrada { get; set; }
        /// <summary>Lo dado por falta en el servidor (por cualquier PDA) cuando se ha leído la recogida.</summary>
        public int UnidadesEnFalta { get; set; }

        // NestoAPI#600: lo que depende del tipo, ya resuelto por su estrategia (la app no mira el código del tipo). La
        // confirmación antes de terminar se monta así: TituloConfirmacion de encabezado; «Cogido: N unidades.» (de la app);
        // AvisoSinFaltas si no hay nada en falta, o «En falta: N unidades. » + AvisoConFaltas si lo hay; y AvisoAlTerminar
        // al final, si viene.

        /// <summary>El nombre del destino para enseñarlo: «Mesa de packing», «Reina» (Almacenes.Descripción). Nunca el código.</summary>
        public string NombreDestino { get; set; }
        /// <summary>«Picking 99757», «Reposición 80905 hacia Reina». Siempre viene.</summary>
        public string Titulo { get; set; }
        /// <summary>«¿Terminar el picking 99757?», «¿Terminar la reposición 80905?». Siempre viene.</summary>
        public string TituloConfirmacion { get; set; }
        /// <summary>Confirmación sin nada en falta: «Sin faltas.». Siempre viene.</summary>
        public string AvisoSinFaltas { get; set; }
        /// <summary>Confirmación con faltas, detrás de «En falta: N unidades.»: qué se hace con ellas. Siempre viene.</summary>
        public string AvisoConFaltas { get; set; }
        /// <summary>Confirmación, siempre al final: lo que pasa al terminar (en una reposición, que se contabiliza la salida). Null: nada.</summary>
        public string AvisoAlTerminar { get; set; }
        /// <summary>Lo que se le dice al abrirla si ya está terminada en el servidor (<see cref="Cerrada"/>). Siempre viene.</summary>
        public string AvisoCerrada { get; set; }
        /// <summary>Lo recogido se empaqueta después (hoy, el picking): al terminar se ofrece el packing.</summary>
        public bool TienePacking { get; set; }
        /// <summary>Si no está en el hueco, se puede ofrecer cogerlo de otro (Ariadna#12: api/Almacen/Picking/{n}/Alternativas).</summary>
        public bool PermiteCambiarHueco { get; set; }
    }

    /// <summary>
    /// Ariadna: un picking con alguna entrega (cliente + dirección) sin empaquetar, para la lista de «Empaquetar».
    /// Criterio exacto, el mismo que la pantalla de packing: el picking tiene líneas que siguen en él (estado 1, de
    /// producto, con algo que meter en la caja: Cantidad − Recoger ≠ 0) y alguna de sus entregas no tiene bultos o tiene
    /// alguno sin foto (EnviosAgenciaBultos de ese picking). Da igual cómo se recogió (Ariadna o papel). Al hacer la
    /// etiqueta en Agencias se hace el albarán y las líneas dejan el estado 1: el picking sale de la lista.
    /// </summary>
    public class PackingPendienteDTO
    {
        public int Picking { get; set; }
        /// <summary>Las entregas del picking (lo que va a un mismo cliente y dirección).</summary>
        public int Entregas { get; set; }
        public int EntregasSinEmpaquetar { get; set; }
        public int Pedidos { get; set; }
        public int Unidades { get; set; }
        /// <summary>Los bultos ya hechos en el picking.</summary>
        public int Bultos { get; set; }
        /// <summary>El nombre del cliente de la primera entrega sin empaquetar.</summary>
        public string Nombre { get; set; }
        /// <summary>Se recogió con Ariadna y se terminó (si no, se recogió en papel o aún no se ha recogido).</summary>
        public bool RecogidoEnAriadna { get; set; }

        /// <summary>NestoAPI#646: otro usuario está empaquetando algo de este picking (lo último, en los últimos minutos). Null si nadie.</summary>
        public string EmpaquetandoPor { get; set; }
        /// <summary>NestoAPI#646: cuándo fue lo último que hizo (una lectura del packing o una foto de bulto). Null si nadie.</summary>
        public DateTime? UltimaActividad { get; set; }
        /// <summary>NestoAPI#646: hace cuántos minutos (con la hora del servidor, no la de la PDA). Null si nadie.</summary>
        public int? MinutosDesdeActividad { get; set; }
        /// <summary>NestoAPI#646: «Lo está empaquetando Pedro (hace 2 min).». Null si nadie.</summary>
        public string AvisoEmpaquetando { get; set; }
        /// <summary>NestoAPI#646: lo que se ha hecho en el packing en los últimos minutos, de cualquier usuario. No viaja.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public List<ActividadPackingAlmacen> Actividad { get; set; } = new List<ActividadPackingAlmacen>();
    }

    /// <summary>NestoAPI#646: lo último que ha hecho un usuario en el packing de un pedido (lectura PACK o foto de bulto).</summary>
    public class ActividadPackingAlmacen
    {
        public int Picking { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        /// <summary>Hace cuántos segundos (con la hora de la base de datos).</summary>
        public int Segundos { get; set; }
    }

    public class LineaPackingAlmacenDTO : IConFichaProducto, IConCodigosBarras
    {
        /// <summary>LinPedidoVta.[Nº Orden].</summary>
        public int LineaPedido { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        // Ariadna#9: en el packing se ve el producto como en el resto de pantallas (ficha y foto)
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la referencia del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1), recortada. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
        public bool SinCodigo { get; set; }
        /// <summary>Otro producto del mismo grupo de pedidos comparte el código.</summary>
        public bool CodigoDuplicado { get; set; }
        /// <summary>Lo que va en la caja: la cantidad de la línea menos lo que el cliente recoge.</summary>
        public int Cantidad { get; set; }
        /// <summary>
        /// Lo ya metido en la caja de este pedido (lecturas del packing, fase PACK), para seguir el packing desde
        /// otra PDA o con otro mozo. Si el pedido lleva el producto en varias líneas, se reparte por orden y lo que
        /// sobra va a la última.
        /// </summary>
        public int Metidas { get; set; }
        /// <summary>Lo dado por falta en el packing: no aparece en la mesa después de buscarlo (se avisa a Compras).</summary>
        public int Faltas { get; set; }
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

        /// <summary>
        /// NestoAPI#646: los bultos ya hechos de esta entrega en este picking (una fila por pedido: un bulto compartido sale
        /// en cada pedido de la caja, con el mismo número). Para refrescar sin pedirlos pedido a pedido.
        /// </summary>
        public List<BultoAlmacenDTO> Bultos { get; set; } = new List<BultoAlmacenDTO>();
        /// <summary>NestoAPI#646: otro usuario está empaquetando esta entrega (lo último, en los últimos minutos). Null si nadie.</summary>
        public string EmpaquetandoPor { get; set; }
        /// <summary>NestoAPI#646: cuándo fue lo último que hizo (una lectura del packing o una foto de bulto). Null si nadie.</summary>
        public DateTime? UltimaActividad { get; set; }
        /// <summary>NestoAPI#646: hace cuántos minutos (con la hora del servidor). Null si nadie.</summary>
        public int? MinutosDesdeActividad { get; set; }
        /// <summary>NestoAPI#646: «Lo está empaquetando Pedro (hace 2 min).». Null si nadie: no hay que avisar.</summary>
        public string AvisoEmpaquetando { get; set; }
        /// <summary>NestoAPI#646: lo hecho en el packing de esta entrega en los últimos minutos, de cualquier usuario. No viaja.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public List<ActividadPackingAlmacen> Actividad { get; set; } = new List<ActividadPackingAlmacen>();
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
        /// <summary>
        /// SCAN (código del proveedor), ETIQ (nuestra etiqueta), TECLA (tecleado), FALTA o MANUAL (Ariadna sin #43: etiqueta o
        /// tecleado, sin distinguir). Ariadna#43: las unidades tecleadas tras una lectura llevan el método de esa lectura.
        /// </summary>
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

        // NestoAPI#646: varias PDA empaquetando la misma entrega. Si el número que manda la app ya lo tiene otra PDA u otro
        // usuario en esa entrega, el servidor le da el siguiente libre: la app tiene que usar Bulto, no el que mandó.

        /// <summary>El servidor ha cambiado el número de bulto que mandó la app (lo tenía otra PDA). Solo al subir la foto.</summary>
        public bool Renumerado { get; set; }
        /// <summary>El número que mandó la app, si se ha cambiado. Null si no.</summary>
        public int? BultoPedido { get; set; }
        /// <summary>Quién tenía ya ese número (el usuario, sin dominio), si se sabe.</summary>
        public string OcupadoPor { get; set; }
        /// <summary>Lo que se le dice al mozo si se ha cambiado: «Esta caja es el bulto 3: el 2 ya lo había cerrado Pedro.». Null si no.</summary>
        public string AvisoRenumerado { get; set; }
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

        /// <summary>Ariadna#47: el hueco de una línea; null si le falta el pasillo, la fila o la columna.</summary>
        public static UbicacionAlmacenDTO De(string pasillo, string fila, string columna)
        {
            if (string.IsNullOrWhiteSpace(pasillo) || string.IsNullOrWhiteSpace(fila) || string.IsNullOrWhiteSpace(columna))
            {
                return null;
            }
            return new UbicacionAlmacenDTO
            {
                Pasillo = pasillo.Trim(),
                Fila = fila.Trim(),
                Columna = columna.Trim(),
                Ubicacion = $"{pasillo.Trim()}/{fila.Trim()}/{columna.Trim()}"
            };
        }

        /// <summary>Ariadna#47: el hueco escrito como se enseña («001/002/004»); null si no lo es.</summary>
        public static UbicacionAlmacenDTO DeTexto(string ubicacion)
        {
            string[] partes = ubicacion?.Split('/');
            return partes != null && partes.Length == 3 ? De(partes[0], partes[1], partes[2]) : null;
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

    public class ProductoPendienteDeUbicarDTO : IConFichaProducto, IConCodigosBarras, IConProductosMismoHueco
    {
        /// <summary>Posición en el recorrido: por el hueco donde ya hay producto (pasillo, columna, fila).</summary>
        public int Orden { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la referencia del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1), recortada. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
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
        /// <summary>
        /// Ariadna#47: los demás productos con stock en el hueco sugerido (el primero de <see cref="UbicacionesActuales"/>
        /// o, si no queda ninguno, <see cref="UltimaUbicacion"/>). Ver <see cref="IConProductosMismoHueco"/>.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<ProductoMismoHuecoDTO> ProductosMismoHueco { get; set; }

        UbicacionAlmacenDTO IConProductosMismoHueco.HuecoDeReferencia
        {
            get
            {
                UbicacionAlmacenDTO actual = UbicacionesActuales?.Find(u => u != null);
                return actual != null ? UbicacionAlmacenDTO.De(actual.Pasillo, actual.Fila, actual.Columna) : UbicacionAlmacenDTO.DeTexto(UltimaUbicacion);
            }
        }
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
    public class ProductoAlmacenDTO : IConFichaProducto, IConCodigosBarras, IConProductosMismoHueco
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string Familia { get; set; }
        public string Subgrupo { get; set; }
        public short? Tamano { get; set; }
        public string UnidadMedida { get; set; }
        public string UrlFoto { get; set; }
        /// <summary>NestoAPI#681: la referencia del proveedor principal (ProveedoresProducto.ReferenciaProv con Orden = 1), recortada. Null si no hay.</summary>
        public string ReferenciaProveedor { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
        public string Almacen { get; set; }
        public List<UbicacionAlmacenDTO> Ubicaciones { get; set; } = new List<UbicacionAlmacenDTO>();
        /// <summary>Unidades recibidas sin hueco asignado.</summary>
        public int PendienteDeUbicar { get; set; }
        /// <summary>
        /// Ariadna#47: los demás productos con stock en el hueco donde se acaba de ubicar (al ubicar) o en el primero de
        /// <see cref="Ubicaciones"/> (al buscar). Ver <see cref="IConProductosMismoHueco"/>.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<ProductoMismoHuecoDTO> ProductosMismoHueco { get; set; }
        /// <summary>Ariadna#47: al ubicar, el hueco donde se ha ubicado (el que se compara). No viaja.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public UbicacionAlmacenDTO HuecoUbicado { get; set; }

        UbicacionAlmacenDTO IConProductosMismoHueco.HuecoDeReferencia
        {
            get
            {
                UbicacionAlmacenDTO hueco = HuecoUbicado ?? Ubicaciones?.Find(u => u != null);
                return hueco == null ? null : UbicacionAlmacenDTO.De(hueco.Pasillo, hueco.Fila, hueco.Columna);
            }
        }
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

    public class LineaRecepcionCompraDTO : IConCodigosBarras
    {
        /// <summary>LinPedidoCmp.NºOrden.</summary>
        public int LineaPedido { get; set; }
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
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
        /// <summary>El almacén del que viene (PreExtrProducto.Delegación de las filas de entrada), p. ej. «ALC».</summary>
        public string Origen { get; set; }
        /// <summary>Almacenes.Descripción del origen («Alcobendas»), para el título.</summary>
        public string NombreOrigen { get; set; }
        /// <summary>
        /// Sugerencia 581: la ha mandado la tienda aparte, con solo unos productos (ReposicionesTraspasos.Herramienta «Puntual»),
        /// no es la del calendario.
        /// </summary>
        public bool EsPuntual { get; set; }
        /// <summary>
        /// NestoAPI#673: productos distintos ya dados de alta en recepciones parciales (evidencia «Recepción parcial» en
        /// PreparacionEscaneos). 0 si no se ha recibido nada en parte.
        /// </summary>
        public int LineasRecibidas { get; set; }
        /// <summary>NestoAPI#673: unidades ya dadas de alta en recepciones parciales.</summary>
        public int UnidadesRecibidas { get; set; }
        /// <summary>NestoAPI#673: cuándo se hizo la primera recepción parcial. Null si ninguna.</summary>
        public DateTime? FechaPrimeraParcial { get; set; }
    }

    public class LineaReposicionDTO : IConCodigosBarras
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        /// <summary>NestoAPI#605: todos los códigos activos del producto (el principal, el primero). Vacía si no tiene.</summary>
        public List<string> CodigosBarras { get; set; } = new List<string>();
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
