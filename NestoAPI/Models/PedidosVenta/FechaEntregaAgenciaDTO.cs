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
        /// «Con «Todo junto» el pedido no sale hasta que esté todo; si eliges «…», la primera parte sale el jueves 15/10.»
        /// </summary>
        public string Aviso { get; set; }
        /// <summary>
        /// NestoAPI#606 (09/10): de dónde sale cada producto, para el correo del pedido (y para quien quiera enseñarlo). Las
        /// mismas cuentas que <see cref="Motivo"/>, sin texto.
        /// </summary>
        public List<PartidaFechaEntregaAgenciaDTO> Desglose { get; set; } = new List<PartidaFechaEntregaAgenciaDTO>();

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
        /// <summary>En una tienda: llega con su próxima reposición.</summary>
        public const string ORIGEN_TIENDA = "Tienda";
        /// <summary>Ya viene de camino desde una tienda (reposición generada y sin recibir).</summary>
        public const string ORIGEN_EN_CAMINO = "EnCamino";
        /// <summary>De un pedido a proveedor enviado.</summary>
        public const string ORIGEN_PROVEEDOR = "Proveedor";
        /// <summary>Sin stock ni fecha de llegada.</summary>
        public const string ORIGEN_SIN_STOCK = "SinStock";
        /// <summary>El orden en que se explican.</summary>
        public static readonly string[] ORDEN_ORIGENES = { ORIGEN_PICKING, ORIGEN_ALGETE, ORIGEN_TIENDA, ORIGEN_EN_CAMINO, ORIGEN_PROVEEDOR, ORIGEN_SIN_STOCK };

        public string Origen { get; set; }
        public string Producto { get; set; }
        public int Unidades { get; set; }
        /// <summary>Solo <see cref="ORIGEN_TIENDA"/>: código del almacén (REI, ALC).</summary>
        public string Tienda { get; set; }
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
    }
}
