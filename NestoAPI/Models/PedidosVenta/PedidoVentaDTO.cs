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
        // NestoAPI#253: casilla "Avisar con importe cuando coja picking" (Nesto#389, NestoApp#140)
        public bool avisarConImporteAlCogerPicking { get; set; }
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
        /// <summary>NestoAPI#482: modo de servicio (1..4, ver Constantes.Pedidos.ModosServicio; 5 = retenido, #698). Null = el
        /// cliente no lo manda y se deriva de servirJunto. Al leer, siempre viene con valor.</summary>
        public byte? modoServicio { get; set; }
        /// <summary>
        /// NestoAPI#704 (caso 929152): de dónde sale el modoServicio que manda el cliente, para poder demostrar después si lo eligió el
        /// vendedor o venía puesto: «Usuario» (lo eligió en el selector), «Sugerido» (la sugerencia de ModoServicioSugerido aplicada sin
        /// tocarla), «PorDefecto» (parámetro o defecto del cliente) o «Corregido» (tras un rechazo de #518 o porque dejó de tener
        /// sentido, #187). Opcional; null = cliente antiguo. Solo se graba (PedidosModoServicioOrigen), no cambia nada del pedido.
        /// </summary>
        public string OrigenModoServicio { get; set; }
        /// <summary>
        /// NestoAPI#704: la sugerencia que el cliente tenía en pantalla al grabar (el Modo que le devolvió ModoServicioSugerido), si la
        /// manda. Opcional. Junto con la que calcula el servidor al grabar, dice si el stock cambió entre medias. Solo se graba.
        /// </summary>
        public byte? ModoServicioSugerido { get; set; }
        /// <summary>NestoAPI#698: la retención viva («hasta nuevo aviso») cuando modoServicio es 5; null si no está retenido. Solo lectura:
        /// se pone y se quita con POST …/Retener y …/Liberar, nunca en el PUT.</summary>
        public Infraestructure.PedidosVenta.Retencion.RetencionPedidoDTO retencion { get; set; }
        /// <summary>NestoAPI#542: modo de facturación (1 por entregas, 2 al completar el pedido, 3 todo ahora y lo
        /// pendiente después; ver Constantes.Pedidos.ModosFacturacion). Null = el cliente no lo manda y se
        /// deriva de mantenerJunto. Al leer, siempre viene con valor.</summary>
        public byte? modoFacturacion { get; set; }
        /// <summary>NestoAPI#542: en una nota de entrega creada por la API con lo pendiente de un pedido
        /// facturado entero, el número de ese pedido. Solo lectura.</summary>
        public int? pedidoOrigen { get; set; }
        /// <summary>NestoAPI#542: el albarán de ese pedido del que sale lo pendiente. Solo lectura.</summary>
        public int? albaranOrigen { get; set; }
        /// <summary>NestoAPI#688: «¿Servir lo pendiente en cuanto haya stock?» (solo con modoFacturacion 3). Al leer, el valor
        /// efectivo con «Todo ahora» (NULL guardado = sin marcar, como hasta ahora) y null cuando no aplica (otro modo de
        /// facturación), para que quien reenvía lo leído reciba la casilla marcada por defecto al pasar a «Todo ahora». Al grabar, null = no cambia; al elegir «Todo ahora» sin
        /// informarla, marcada. En una nota de entrega, si la nota sale sola en cuanto haya stock (la cambia
        /// POST api/PedidosVenta/{empresa}/{nota}/ServirPendienteAlHaberStock).</summary>
        public bool? servirPendienteAlHaberStock { get; set; }
        public string suPedido { get; set; }
        public string vendedor { get; set; }
        public bool vistoBuenoPlazosPago { get; set; }
        public bool CreadoSinPasarValidacion { get; set; }
        /// <summary>
        /// NestoAPI#712: aviso de fecha de entrega lejana (más de 60 días). Null = el cliente no sabe preguntar y graba como
        /// siempre, sin aviso. False = la API comprueba y, si alguna línea nueva o con la fecha cambiada va a más de 60 días,
        /// rechaza con FECHA_ENTREGA_LEJANA (400) y la pregunta en el mensaje. True = el usuario ya ha dicho que sí.
        /// </summary>
        public bool? ConfirmarFechaEntregaLejana { get; set; }
        /// <summary>
        /// NestoAPI#715 (corte 4, AÚN SIN EFECTO): el equivalente de ConfirmarFechaEntregaLejana para las señales de anomalía
        /// (cantidad, precio, descuento, importe fuera de lo habitual). En el corte 1 el modo es Sombra: la API registra las
        /// señales en PedidosAnomalias y no mira este flag. Cuando Anomalias:Modo sea Activo: null = el cliente no sabe
        /// preguntar y graba como siempre; false = la API comprueba y rechaza con ANOMALIA_PEDIDO (400); true = el usuario ya
        /// ha dicho que sí.
        /// </summary>
        public bool? ConfirmarAnomalias { get; set; }
        /// <summary>
        /// NestoAPI#732: al quitar (o poner a 0, o cambiar de producto) la línea que justifica una inscripción gratuita por compra a
        /// un evento, el PUT contesta 409 PEDIDO_JUSTIFICA_INSCRIPCIONES_EVENTO con la pregunta en el mensaje y las inscripciones en
        /// error.details.inscripciones, sin guardar nada. True = el usuario ya ha dicho que sí: se guarda el pedido, se anulan esas
        /// inscripciones y se avisa a la tienda del evento (y la respuesta trae el aviso INSCRIPCIONES_EVENTO_ANULADAS). Null o
        /// false = se pregunta.
        /// </summary>
        public bool? ConfirmarAnularInscripcionesEventos { get; set; }
        /// <summary>
        /// Issue #159: permite al vendedor marcar un pedido para que no se le cobre la comisión
        /// por contra reembolso, aunque la forma de pago sea EFC. Solo tiene efecto si la fecha
        /// actual es anterior a <see cref="Constantes.Pedidos.FECHA_CORTE_NO_COBRAR_COMISION_REEMBOLSO"/>;
        /// a partir de esa fecha el backend ignora el flag y siempre aplica la comisión si procede.
        /// </summary>
        public bool NoCobrarComisionReembolso { get; set; }
        /// <summary>
        /// Issue #218: casilla "Añadir portes al pedido" de PlantillaVenta. Default <c>true</c>
        /// por retrocompatibilidad (los clientes que no lo envíen se comportan como hasta ahora).
        /// Solo los grupos Almacén y Compras pueden desmarcarlo para suprimir portes; el resto de
        /// usuarios lo llevan siempre, y REI/ALC nunca añaden portes aunque venga <c>true</c>.
        /// La autorización real la hace el servidor (no fiarse de este flag).
        /// </summary>
        public bool AnadirPortes { get; set; } = true;
        /// <summary>
        /// NestoAPI#670: el Guid de la sesión de sugerencias de la plantilla (el IdSesion de api/SugerenciasProductos/Calcular).
        /// Opcional. Al guardar, se enlaza la sesión con el pedido y se marca qué sugerencias entraron en él.
        /// </summary>
        public Guid? IdSesionSugerencias { get; set; }

        /// <summary>
        /// NestoAPI#691 (Nesto#543): solo en la respuesta del POST. Los pedidos que la API ha creado aparte al guardar: cada curso
        /// va en su propio pedido (serie CV, prepago), con Motivo «Cursos». Null si no hay ninguno.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<Infraestructure.Eventos.PedidoAdicionalDTO> PedidosAdicionales { get; set; }

        /// <summary>
        /// NestoAPI#691: solo en la respuesta del POST. Los cursos que NO se han podido crear después de crear el pedido (y por qué),
        /// para que el vendedor inscriba a la clienta desde el evento. Null si no ha fallado ninguno.
        /// </summary>
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<string> AvisosCursos { get; set; }

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
            VincularLineas();
        }

        /// <summary>
        /// Cada línea apunta a este pedido. Es lo que hace que su descuento (y con él la base y el total) lleve el
        /// pronto pago de la cabecera (<see cref="LineaPedidoVentaDTO.SumaDescuentos"/>). Lo hace solo la
        /// deserialización: un DTO montado en el servidor (el pedido de la app, NestoAPI#593) tiene que llamarlo, y
        /// otra vez tras añadirle líneas (los portes).
        /// </summary>
        public void VincularLineas()
        {
            foreach (LineaPedidoVentaDTO linea in Lineas)
            {
                linea.Pedido = this;
            }
        }
    }
}