using System;
using System.Collections.Generic;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606: cuándo entregamos el pedido a la agencia. Lo devuelven
    /// <c>GET api/PedidosVenta/{empresa}/{numero}/FechaEntregaAgencia</c> (detalle del pedido) y
    /// <c>POST api/PedidosVenta/FechaEntregaAgencia</c> (plantilla, sin pedido creado). Las fechas son días, sin hora.
    /// </summary>
    public class FechaEntregaAgenciaDTO
    {
        /// <summary>Día de la primera entrega a la agencia. Null = sin fecha (o nada sale de Algete por agencia).</summary>
        public DateTime? PrimeraEntrega { get; set; }
        /// <summary>Día de la entrega con la que queda todo servido. Null = algo no tiene fecha.</summary>
        public DateTime? EntregaCompleta { get; set; }
        /// <summary>La que hay que enseñar: la completa en «Todo junto»; la primera en el resto de modos.</summary>
        public DateTime? FechaEntregaAgencia { get; set; }
        /// <summary>"Completa" o "Primera": cuál de las dos es <see cref="FechaEntregaAgencia"/>.</summary>
        public string Aplica { get; set; }
        /// <summary>Todos los días en que sale algo, en orden.</summary>
        public List<DateTime> Entregas { get; set; } = new List<DateTime>();
        /// <summary>Por qué, en castellano (para el tooltip).</summary>
        public string Motivo { get; set; }
        /// <summary>La fecha que se dio al crear el pedido (CabPedidoVta.FechaEntregaAgenciaPrometida). Null en la plantilla,
        /// en los pedidos anteriores a la columna y en los que nacieron sin fecha.</summary>
        public DateTime? FechaPrometida { get; set; }
        /// <summary>
        /// NestoAPI#606 (08/10): aviso para la plantilla, null si no hay. Hoy solo con «Todo junto» + «Todo ahora, lo pendiente se
        /// entrega después» y algo que falta (el pedido no sale: todo va a una nota de entrega sin fecha), con la alternativa:
        /// «Con «Todo junto» el pedido no sale hasta que esté todo; si elige «…», la primera parte sale el jueves 15/10.»
        /// </summary>
        public string Aviso { get; set; }
        /// <summary>
        /// NestoAPI#606 (09/10): de dónde sale cada producto, para el correo del pedido (y para quien quiera enseñarlo). Las
        /// mismas cuentas que <see cref="Motivo"/>, sin texto.
        /// </summary>
        public List<PartidaFechaEntregaAgenciaDTO> Desglose { get; set; } = new List<PartidaFechaEntregaAgenciaDTO>();

        /// <summary>
        /// Sugerencia 1340: solo en un pedido en modo 6 («Junto con lo pendiente del cliente») con algo con lo que juntarse. La fecha de
        /// arriba ya es la del conjunto (la peor); aquí, con qué se junta y, si es lo pendiente lo que la retrasa, qué espera. Null en los
        /// demás pedidos. <see cref="Desglose"/> sigue siendo solo el del propio pedido.
        /// </summary>
        public JuntoConPendienteFechaDTO JuntoConPendiente { get; set; }

        public const string APLICA_PRIMERA = "Primera";
        public const string APLICA_COMPLETA = "Completa";
    }

    /// <summary>
    /// NestoAPI#606 (09/10): unidades de un producto que salen del mismo sitio el mismo día (stock de Algete, reposición de
    /// una tienda, pedido a proveedor…). Lo monta <c>CalculadoraFechaEntregaAgencia</c>.
    /// </summary>
    public class PartidaFechaEntregaAgenciaDTO
    {
        /// <summary>Ya tiene picking: sale hoy.</summary>
        public const string ORIGEN_PICKING = "Picking";
        /// <summary>Libre en Algete.</summary>
        public const string ORIGEN_ALGETE = "Algete";
        /// <summary>NestoAPI#606 (kits, 13/10/26): desmontando un kit libre en Algete que lo lleva (sale como lo libre).</summary>
        public const string ORIGEN_KIT = "Kit";
        /// <summary>
        /// NestoAPI#606 (packs montables, 18/11/26): el producto es el titular de un kit (un pack) que se MONTA en Algete con sus
        /// componentes. Sin <see cref="ComponenteQueFalta"/>: los componentes están libres en Algete y sale como lo libre (almacén
        /// lo monta con el informe de kits de antes del picking). Con él: espera a ese componente, que llega por su propio origen
        /// (<see cref="OrigenComponente"/>, con sus datos en <see cref="Tienda"/>, <see cref="LlegaAAlgete"/>, <see cref="FechaPrevistaProveedor"/>…).
        /// </summary>
        public const string ORIGEN_MONTAJE = "Montaje";
        /// <summary>En una tienda: llega con su próxima reposición.</summary>
        public const string ORIGEN_TIENDA = "Tienda";
        /// <summary>Ya viene de camino desde una tienda (reposición generada y sin recibir).</summary>
        public const string ORIGEN_EN_CAMINO = "EnCamino";
        /// <summary>De un pedido a proveedor enviado.</summary>
        public const string ORIGEN_PROVEEDOR = "Proveedor";
        /// <summary>Sin stock ni fecha de llegada.</summary>
        public const string ORIGEN_SIN_STOCK = "SinStock";
        /// <summary>El orden en que se explican.</summary>
        public static readonly string[] ORDEN_ORIGENES = { ORIGEN_PICKING, ORIGEN_ALGETE, ORIGEN_KIT, ORIGEN_MONTAJE, ORIGEN_TIENDA, ORIGEN_EN_CAMINO, ORIGEN_PROVEEDOR, ORIGEN_SIN_STOCK };

        public string Origen { get; set; }
        public string Producto { get; set; }
        public int Unidades { get; set; }
        /// <summary>Solo <see cref="ORIGEN_TIENDA"/>: código del almacén (REI, ALC).</summary>
        public string Tienda { get; set; }
        /// <summary>Solo <see cref="ORIGEN_KIT"/>: el kit que se desmonta.</summary>
        public string Kit { get; set; }
        /// <summary>
        /// NestoAPI#606 (packs montables): solo <see cref="ORIGEN_MONTAJE"/> (o <see cref="ORIGEN_SIN_STOCK"/> de un pack): el componente que
        /// no está en Algete y del que depende la fecha (el peor). Null = todos los componentes están en Algete.
        /// </summary>
        public string ComponenteQueFalta { get; set; }
        /// <summary>NestoAPI#606 (packs montables): el nombre de <see cref="ComponenteQueFalta"/> (Productos.Nombre), si se conoce.</summary>
        public string NombreComponente { get; set; }
        /// <summary>
        /// NestoAPI#606 (packs montables): de dónde llega <see cref="ComponenteQueFalta"/> (<see cref="ORIGEN_TIENDA"/>,
        /// <see cref="ORIGEN_EN_CAMINO"/>, <see cref="ORIGEN_PROVEEDOR"/> o <see cref="ORIGEN_SIN_STOCK"/>).
        /// </summary>
        public string OrigenComponente { get; set; }
        /// <summary>Solo <see cref="ORIGEN_TIENDA"/>: día y hora a la que suele llegar a Algete la reposición que lo trae.</summary>
        public DateTime? LlegaAAlgete { get; set; }
        /// <summary>Primer día en que puede salir a la agencia. Null = sin fecha.</summary>
        public DateTime? PuedeSalirEl { get; set; }
        /// <summary>Solo <see cref="ORIGEN_PROVEEDOR"/>: la fecha prevista del pedido a proveedor.</summary>
        public DateTime? FechaPrevistaProveedor { get; set; }
        /// <summary>Solo con la fecha prevista del proveedor VENCIDA: el día que se ha supuesto que llega.</summary>
        public DateTime? LlegadaSupuesta { get; set; }
        /// <summary>Solo <see cref="ORIGEN_PROVEEDOR"/>: número del pedido a proveedor (si se conoce).</summary>
        public int? PedidoProveedor { get; set; }
        /// <summary>Por qué no tiene fecha.</summary>
        public string MotivoSinFecha { get; set; }
        /// <summary>
        /// NestoAPI#606 (pedido 928335, 13/10/26): facturación «todo ahora» y no sale en la primera entrega: se factura ya y pasa
        /// a la nota de entrega, que nace SIN fecha (la pone almacén). <see cref="PuedeSalirEl"/> sigue diciendo cuándo podría salir.
        /// </summary>
        public bool ANotaDeEntrega { get; set; }

        /// <summary>«Segura»: sale de Algete (picking, stock libre o desmontando un kit).</summary>
        public const string CERTEZA_SEGURA = "Segura";
        /// <summary>«CasiSegura»: en una tienda, de camino desde una tienda, o pedido a proveedor ya enviado con seguimiento de la agencia.</summary>
        public const string CERTEZA_CASI_SEGURA = "CasiSegura";
        /// <summary>«Aproximada»: pedido a proveedor con fecha prevista, sin seguimiento de la agencia.</summary>
        public const string CERTEZA_APROXIMADA = "Aproximada";
        /// <summary>«Orientativa»: partida SIN fecha con <see cref="FechaOrientativa"/> (no es un compromiso).</summary>
        public const string CERTEZA_ORIENTATIVA = "Orientativa";

        /// <summary>
        /// NestoAPI#853: grado de certeza de la fecha de la partida (<see cref="CERTEZA_SEGURA"/>, <see cref="CERTEZA_CASI_SEGURA"/>,
        /// <see cref="CERTEZA_APROXIMADA"/>, <see cref="CERTEZA_ORIENTATIVA"/>). Null si no hay nada que decir (p. ej. sin fecha y sin estimación).
        /// </summary>
        public string Certeza { get; set; }
        /// <summary>NestoAPI#853: solo <see cref="ORIGEN_PROVEEDOR"/>: el pedido a proveedor tiene un correo de seguimiento de la agencia aplicado y vigente.</summary>
        public bool ConSeguimientoAgencia { get; set; }
        /// <summary>NestoAPI#853: la agencia de ese seguimiento, si se conoce («UPS», «GLS»…).</summary>
        public string AgenciaSeguimiento { get; set; }
        /// <summary>
        /// NestoAPI#853: solo en partidas SIN fecha (<see cref="PuedeSalirEl"/> sigue null): el día en que estimamos que PODRÍA salir.
        /// Orientativa: no entra en ninguna fecha del pedido (primera, completa, prometida) ni en la promesa en riesgo.
        /// </summary>
        public DateTime? FechaOrientativa { get; set; }
        /// <summary>NestoAPI#853: si <see cref="FechaOrientativa"/> sale de un pedido a proveedor enviado con la prevista a más de 15 días, ese pedido.</summary>
        public int? PedidoProveedorOrientativo { get; set; }
        /// <summary>NestoAPI#853: la prevista de ese pedido lejano (la que no cuenta como «llega en días»).</summary>
        public DateTime? FechaPrevistaProveedorOrientativo { get; set; }
    }

    /// <summary>Sugerencia 1340: con qué sale junto un pedido en modo 6 y qué lo retrasa (lo monta ReglaFechaJuntoConPendiente).</summary>
    public class JuntoConPendienteFechaDTO
    {
        /// <summary>Los pedidos y notas con los que se junta.</summary>
        public List<int> Pedidos { get; set; } = new List<int>();
        /// <summary>«la nota de entrega 929701» o «la nota de entrega 929701 y el pedido 931020».</summary>
        public string Documentos { get; set; }
        /// <summary>La fecha del conjunto (la peor; null = sin fecha). Es la misma que FechaEntregaAgencia.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>La fecha del propio pedido (como «Todo junto»), sin esperar a nada.</summary>
        public DateTime? FechaPropia { get; set; }
        /// <summary>Lo pendiente sale DESPUÉS que el pedido (o sin fecha): es lo que pone la fecha.</summary>
        public bool LoRetrasaLoPendiente { get; set; }
        /// <summary>Con <see cref="LoRetrasaLoPendiente"/>: el documento que manda («la nota de entrega 929701»).</summary>
        public string DocumentoQueRetrasa { get; set; }
        /// <summary>Con <see cref="LoRetrasaLoPendiente"/>: el producto que espera ese documento (el último en llegar), su nombre y su certeza.</summary>
        public string Producto { get; set; }
        public string NombreProducto { get; set; }
        public string Certeza { get; set; }
        /// <summary>La frase, de usted: «Sale el lunes 30/11/2026, junto con la nota de entrega 929701, que espera el CONJUNTO DUPLO STARPIL BASIC (45238).»</summary>
        public string Texto { get; set; }
    }
}
