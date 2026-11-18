using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta.Retencion;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Caso 931007 (Carlos 18/11/26): lo que el cliente pide hacer con lo que añade a un pedido EN PREPARACIÓN
    /// (<see cref="PedidoVentaDTO.AmpliacionEnPreparacion"/>). «Añadirlo sabiendo que sale aparte» no existe para nadie.
    /// </summary>
    public static class OpcionesAmpliacionEnPreparacion
    {
        /// <summary>Lo añadido va en un pedido nuevo del mismo cliente y contacto, con sus propios portes (lo crea la API).</summary>
        public const string PEDIDO_NUEVO = "PedidoNuevo";
        /// <summary>Se guarda en el mismo pedido y se pregunta a Almacén (urgente) si entra en el envío de hoy.</summary>
        public const string PEDIR_ALMACEN = "PedirAlmacen";

        /// <summary>El valor que vale (sin mayúsculas ni espacios), null si no viene y <paramref name="valido"/> false si es otra cosa.</summary>
        public static string Normalizar(string valor, out bool valido)
        {
            valido = true;
            if (string.IsNullOrWhiteSpace(valor))
            {
                return null;
            }
            string limpio = valor.Trim();
            if (string.Equals(limpio, PEDIDO_NUEVO, StringComparison.OrdinalIgnoreCase))
            {
                return PEDIDO_NUEVO;
            }
            if (string.Equals(limpio, PEDIR_ALMACEN, StringComparison.OrdinalIgnoreCase))
            {
                return PEDIR_ALMACEN;
            }
            valido = false;
            return null;
        }
    }

    /// <summary>Un envío del pedido con lo que necesita la ampliación: su reembolso y si está cargado en la furgoneta (agencia 14).</summary>
    public class EnvioAmpliacionPedido : EnvioDelPedido
    {
        public decimal Reembolso { get; set; }
        /// <summary>Reparto propio: algún bulto suyo cargado o algún movimiento en la furgoneta. Para el reparto propio, «la agencia ya lo tiene».</summary>
        public bool CargadoEnFurgoneta { get; set; }
    }

    /// <summary>Qué hacer con un PUT que añade líneas a un pedido guardado.</summary>
    public enum DecisionAmpliacion
    {
        /// <summary>No hay líneas nuevas de producto o el pedido no está en preparación: como siempre.</summary>
        Nada,
        /// <summary>Tiene picking pero hoy no sale (retenido, aplazado, retenido por falta): se guarda y se avisa a Almacén (campana normal).</summary>
        NoSaleHoy,
        /// <summary>En preparación y sale hoy: el cliente tiene que elegir (409 PEDIDO_EN_PREPARACION si no lo hace).</summary>
        EnPreparacion
    }

    public class EvaluacionAmpliacion
    {
        public DecisionAmpliacion Decision { get; set; }
        public List<int> Pickings { get; set; } = new List<int>();
        /// <summary>El envío que lo lleva (el de la agencia si ya lo tiene; si no, el que está en curso), o null.</summary>
        public int? Envio { get; set; }
        public bool AgenciaYaLoTiene { get; set; }
        /// <summary>Lo que se le ofrece: siempre PedidoNuevo; PedirAlmacen solo si la agencia no lo tiene y la etiqueta no lleva reembolso.</summary>
        public List<string> Opciones { get; set; } = new List<string>();
        /// <summary>Las líneas nuevas de producto (y las de texto nuevas que van con ellas): las que irían al pedido nuevo.</summary>
        public List<LineaPedidoVentaDTO> LineasAMover { get; set; } = new List<LineaPedidoVentaDTO>();
        /// <summary>El 409 (de usted) o, con NoSaleHoy, el motivo por el que no sale hoy.</summary>
        public string Mensaje { get; set; }
        public bool RecogeEnTienda { get; set; }

        public bool PuedePedirAlmacen => Opciones.Contains(OpcionesAmpliacionEnPreparacion.PEDIR_ALMACEN);
    }

    /// <summary>
    /// Caso 931007 (Carlos 18/11/26), LA REGLA: el pedido 931007 («Todo junto») tenía picking a las 12:21 y a las 13:33 se le añadió una
    /// línea con stock. Se quedó sin picking y habría salido al día siguiente en otro envío, sin portes y sin que nadie lo decidiera.
    /// <list type="bullet">
    /// <item>«En preparación» = la misma regla que el cambio de modo (#533, <see cref="CambioModoConPicking"/>): línea viva con picking o
    /// albarán de HOY.</item>
    /// <item>Solo cuentan las líneas NUEVAS de PRODUCTO (id 0, cantidad positiva, sin evento, ni cheque regalo ni cupón). Las de texto nuevas
    /// van con ellas al pedido nuevo; las de cuenta contable nuevas (portes, ajustes) se quedan donde estaban (los portes los recalcula cada
    /// pedido). Una línea nueva de texto o de cuenta sola no cambia nada.</item>
    /// <item>No salta en presupuestos ni en notas de entrega (tienen su propio bloqueo), ni si el pedido hoy no sale (retenido, aplazado,
    /// retenido por falta: <see cref="GuardiaPedidoRetenido"/>): ahí se guarda como siempre y se avisa a Almacén.</item>
    /// <item>«La agencia ya lo tiene» = <see cref="AplazamientoPedidoConPicking.YaLoTieneLaAgencia(IEnumerable{EnvioDelPedido}, IEnumerable{int}, out int)"/>
    /// o, en el reparto propio, el envío ya cargado en la furgoneta. Entonces (o con la etiqueta hecha con reembolso, que cambiaría) solo
    /// cabe el pedido nuevo.</item>
    /// <item>Recogida en tienda (AM, ALC, REI): no hay agencia ni portes; se ofrecen las dos opciones (Almacén lo prepara con lo demás).</item>
    /// </list>
    /// Pura: sin base de datos.
    /// </summary>
    public static class AmpliacionPedidoEnPreparacion
    {
        public const string CODIGO_ERROR = "PEDIDO_EN_PREPARACION";
        /// <summary>Datos["tipo"] del aviso de la respuesta del PUT cuando lo añadido ha ido a un pedido nuevo.</summary>
        public const string TIPO_AVISO_PEDIDO_NUEVO = "AmpliacionEnPedidoNuevo";
        /// <summary>Datos["tipo"] del aviso de la respuesta del PUT cuando se ha pedido a Almacén.</summary>
        public const string TIPO_AVISO_PEDIDO_A_ALMACEN = "AmpliacionPedidaAAlmacen";
        /// <summary>Lo que espera la pregunta a Almacén antes de pasar lo añadido a un pedido nuevo.</summary>
        public static readonly TimeSpan ESPERA_RESPUESTA_ALMACEN = TimeSpan.FromMinutes(60);

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        /// <summary>Las líneas nuevas de producto del PUT: las que deciden si salta.</summary>
        public static List<LineaPedidoVentaDTO> LineasNuevasDeProducto(IEnumerable<LineaPedidoVentaDTO> lineasDelPut)
        {
            return (lineasDelPut ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && l.id == 0 && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && l.Cantidad > 0
                    && !l.EventoId.HasValue && !l.EsChequeRegalo && !l.EsCupon && string.IsNullOrWhiteSpace(l.CodigoCupon))
                .ToList();
        }

        /// <summary>Lo que iría al pedido nuevo: las nuevas de producto y las nuevas de texto (en el orden en que vienen).</summary>
        public static List<LineaPedidoVentaDTO> LineasAMover(IEnumerable<LineaPedidoVentaDTO> lineasDelPut)
        {
            List<LineaPedidoVentaDTO> lista = (lineasDelPut ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Where(l => l != null).ToList();
            HashSet<LineaPedidoVentaDTO> productos = new HashSet<LineaPedidoVentaDTO>(LineasNuevasDeProducto(lista));
            if (productos.Count == 0)
            {
                return new List<LineaPedidoVentaDTO>();
            }
            return lista.Where(l => productos.Contains(l) || (l.id == 0 && l.tipoLinea == Constantes.TiposLineaVenta.TEXTO)).ToList();
        }

        /// <summary>Los pickings que hacen que el pedido esté en preparación (líneas vivas con picking o con albarán de hoy). Vacía si no lo está.</summary>
        public static List<int> PickingsEnPreparacion(IEnumerable<LinPedidoVta> guardadas, DateTime hoy, out bool enPreparacion)
        {
            List<LinPedidoVta> lineas = (guardadas ?? Enumerable.Empty<LinPedidoVta>()).Where(l => l != null).ToList();
            List<LinPedidoVta> vivas = lineas.Where(CambioModoConPicking.TienePickingVivo).ToList();
            List<LinPedidoVta> deHoy = lineas.Where(l => CambioModoConPicking.TieneAlbaranDeHoy(l, hoy)).ToList();
            enPreparacion = vivas.Count > 0 || deHoy.Count > 0;
            return vivas.Concat(deHoy).Select(l => l.Picking ?? 0).Where(p => p > 0).Distinct().OrderBy(p => p).ToList();
        }

        /// <summary>LA REGLA. <paramref name="motivoNoSaleHoy"/>: el de <see cref="GuardiaPedidoRetenido"/> (null si sale).</summary>
        public static EvaluacionAmpliacion Evaluar(int pedido, IEnumerable<LinPedidoVta> guardadas, bool notaEntrega,
            IEnumerable<LineaPedidoVentaDTO> lineasDelPut, IEnumerable<EnvioAmpliacionPedido> envios, string ruta, string motivoNoSaleHoy, DateTime hoy)
        {
            var nada = new EvaluacionAmpliacion { Decision = DecisionAmpliacion.Nada };
            List<LineaPedidoVentaDTO> nuevas = LineasNuevasDeProducto(lineasDelPut);
            List<LinPedidoVta> lineas = (guardadas ?? Enumerable.Empty<LinPedidoVta>()).Where(l => l != null).ToList();
            if (nuevas.Count == 0 || notaEntrega || lineas.Count == 0 || TransicionPresupuesto.EsPresupuestoVivo(lineas))
            {
                return nada;
            }
            List<int> pickings = PickingsEnPreparacion(lineas, hoy, out bool enPreparacion);
            if (!enPreparacion)
            {
                return nada;
            }
            var evaluacion = new EvaluacionAmpliacion
            {
                Pickings = pickings,
                LineasAMover = LineasAMover(lineasDelPut),
                RecogeEnTienda = TiendasRecogida.EsRutaDeRecogida(ruta)
            };
            if (!string.IsNullOrWhiteSpace(motivoNoSaleHoy))
            {
                evaluacion.Decision = DecisionAmpliacion.NoSaleHoy;
                evaluacion.Mensaje = motivoNoSaleHoy.Trim();
                return evaluacion;
            }
            evaluacion.Decision = DecisionAmpliacion.EnPreparacion;

            List<EnvioAmpliacionPedido> todos = (envios ?? Enumerable.Empty<EnvioAmpliacionPedido>()).Where(e => e != null).ToList();
            List<int> pickingsParaEnvios = pickings.Count > 0 ? pickings : null;
            var conjunto = pickingsParaEnvios == null ? null : new HashSet<int>(pickingsParaEnvios);
            bool enAgencia = AplazamientoPedidoConPicking.YaLoTieneLaAgencia(todos, pickingsParaEnvios, out int envioAgencia);
            EnvioAmpliacionPedido cargado = todos
                .Where(e => e.EsRepartoPropioPorEntregar && e.CargadoEnFurgoneta && e.PuedeLlevar(conjunto))
                .OrderByDescending(e => e.Numero).FirstOrDefault();
            EnvioAmpliacionPedido etiquetaConReembolso = todos
                .Where(e => e.Estado == Constantes.Agencias.ESTADO_EN_CURSO && e.Reembolso > 0 && e.PuedeLlevar(conjunto))
                .OrderByDescending(e => e.Numero).FirstOrDefault();
            EnvioAmpliacionPedido enCurso = todos
                .Where(e => e.Estado == Constantes.Agencias.ESTADO_EN_CURSO && e.PuedeLlevar(conjunto))
                .OrderByDescending(e => e.Numero).FirstOrDefault();

            evaluacion.AgenciaYaLoTiene = enAgencia || cargado != null;
            evaluacion.Envio = enAgencia ? envioAgencia : cargado?.Numero ?? etiquetaConReembolso?.Numero ?? enCurso?.Numero;
            evaluacion.Opciones.Add(OpcionesAmpliacionEnPreparacion.PEDIDO_NUEVO);
            if (!evaluacion.AgenciaYaLoTiene && etiquetaConReembolso == null)
            {
                evaluacion.Opciones.Add(OpcionesAmpliacionEnPreparacion.PEDIR_ALMACEN);
            }
            evaluacion.Mensaje = Mensaje(pedido, evaluacion, enAgencia ? (int?)envioAgencia : null, cargado?.Numero, etiquetaConReembolso?.Numero);
            return evaluacion;
        }

        /// <summary>«picking 100125» / «pickings 100125 y 100130» / «albarán de hoy».</summary>
        public static string TextoPickings(IReadOnlyCollection<int> pickings)
        {
            List<int> lista = (pickings ?? new List<int>()).Where(p => p > 0).Distinct().OrderBy(p => p).ToList();
            if (lista.Count == 0)
            {
                return "albarán de hoy";
            }
            if (lista.Count == 1)
            {
                return $"picking {lista[0]}";
            }
            return $"pickings {string.Join(", ", lista.Take(lista.Count - 1))} y {lista.Last()}";
        }

        internal static string Mensaje(int pedido, EvaluacionAmpliacion evaluacion, int? envioEnAgencia, int? envioEnFurgoneta, int? envioConReembolso)
        {
            string inicio = $"El pedido {pedido} ya está en preparación ({TextoPickings(evaluacion.Pickings)})";
            if (envioEnAgencia.HasValue)
            {
                return $"{inicio} y la agencia ya tiene su envío {envioEnAgencia.Value}: lo que quiera añadir no puede ir en él. " +
                    "Haga un pedido nuevo con lo que quiera añadir.";
            }
            if (envioEnFurgoneta.HasValue)
            {
                return $"{inicio} y su envío {envioEnFurgoneta.Value} ya está cargado en la furgoneta: lo que quiera añadir no puede ir en él. " +
                    "Haga un pedido nuevo con lo que quiera añadir.";
            }
            if (envioConReembolso.HasValue)
            {
                return $"{inicio} y la etiqueta de su envío {envioConReembolso.Value} ya está hecha con reembolso: lo que quiera añadir no puede ir " +
                    "en él. Haga un pedido nuevo con lo que quiera añadir.";
            }
            if (evaluacion.RecogeEnTienda)
            {
                return $"{inicio} para recogerlo en la tienda. Haga un pedido nuevo con lo que quiera añadir o, desde Nesto, pida a Almacén " +
                    "que lo prepare con lo demás.";
            }
            return $"{inicio} y sale hoy. Haga un pedido nuevo con lo que quiera añadir o, desde Nesto, pida a Almacén que lo meta en el mismo envío.";
        }

        /// <summary>El 409 PEDIDO_EN_PREPARACION (antes de tocar nada), con el formato de GlobalExceptionFilter.</summary>
        public static PedidoEnPreparacionException Excepcion(string empresa, int pedido, EvaluacionAmpliacion evaluacion, string mensaje = null)
            => new PedidoEnPreparacionException(mensaje ?? evaluacion.Mensaje, empresa, pedido, evaluacion);

        /// <summary>«39284 (1 ud.)» / «39284 (1 ud.) y 40010 (3 uds.)».</summary>
        public static string TextoProductos(IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            List<string> partes = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                .Select(l => $"{l.Producto.Trim()} ({l.Cantidad} {(l.Cantidad == 1 ? "ud." : "uds.")})")
                .ToList();
            if (partes.Count == 0)
            {
                return "lo añadido";
            }
            return partes.Count == 1 ? partes[0] : $"{string.Join(", ", partes.Take(partes.Count - 1))} y {partes.Last()}";
        }

        /// <summary>
        /// El pedido nuevo con lo añadido: la cabecera del original (cliente, contacto, forma y plazos de pago, IVA, vendedor, ruta, serie,
        /// su pedido…) y esas líneas, por el camino normal de creación (validación de precios, portes de GestorPortes y modo de servicio por
        /// defecto: modos sin informar). Sin prepagos, ni efectos, ni nota de entrega. Pura.
        /// </summary>
        public static PedidoVentaDTO ConstruirPedidoNuevo(PedidoVentaDTO original, IEnumerable<LineaPedidoVentaDTO> lineas, string usuario,
            DateTime hoy, bool creadoSinPasarValidacion)
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }
            var nuevo = new PedidoVentaDTO
            {
                empresa = original.empresa,
                numero = 0,
                cliente = original.cliente,
                contacto = original.contacto,
                fecha = hoy.Date,
                formaPago = original.formaPago,
                plazosPago = original.plazosPago,
                ccc = original.ccc,
                iva = original.iva,
                vendedor = original.vendedor,
                ruta = original.ruta,
                serie = original.serie,
                periodoFacturacion = original.periodoFacturacion,
                contactoCobro = original.contactoCobro,
                origen = original.origen,
                noComisiona = original.noComisiona,
                suPedido = original.suPedido,
                NoCobrarComisionReembolso = original.NoCobrarComisionReembolso,
                DescuentoPP = original.DescuentoPP,
                DescuentoEntidad = original.DescuentoEntidad,
                comentarios = $"Añadido al pedido {original.numero} cuando ya estaba en preparación.",
                Usuario = string.IsNullOrWhiteSpace(usuario) ? original.Usuario : usuario,
                CreadoSinPasarValidacion = creadoSinPasarValidacion,
                // Modos sin informar: nace en los de por defecto (NestoAPI#482/#542), como cualquier pedido nuevo
                modoServicio = null,
                modoFacturacion = null,
                servirJunto = false,
                mantenerJunto = false,
                ParametrosIva = original.ParametrosIva
            };
            foreach (LineaPedidoVentaDTO linea in lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
            {
                if (linea == null)
                {
                    continue;
                }
                linea.id = 0;
                linea.picking = 0;
                linea.estado = Constantes.EstadosLineaVenta.PENDIENTE;
                linea.Albaran = null;
                linea.Factura = null;
                if (linea.fechaEntrega.Date < hoy.Date)
                {
                    linea.fechaEntrega = hoy.Date;
                }
                nuevo.Lineas.Add(linea);
            }
            nuevo.VincularLineas();
            return nuevo;
        }

        /// <summary>El aviso de la respuesta del PUT (y de UnirPedidos) cuando lo añadido va en un pedido nuevo.</summary>
        public static string AvisoPedidoNuevo(PedidoVentaDTO original, int pedidoNuevo, IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            string prepago = EsPrepago(original)
                ? $" El pedido {original.numero} es de prepago: el pedido {pedidoNuevo} no lleva su prepago, cóbrelo aparte."
                : string.Empty;
            return $"El pedido {original.numero} ya estaba en preparación: {TextoProductos(lineas)} va en el pedido nuevo {pedidoNuevo}, " +
                $"con sus propios portes.{prepago}";
        }

        /// <summary>El aviso de la respuesta del PUT cuando se ha pedido a Almacén que lo meta en el envío de hoy.</summary>
        public static string AvisoPedidoAAlmacen(int pedido, IEnumerable<LineaPedidoVentaDTO> lineas, bool avisado)
        {
            string espera = ((int)ESPERA_RESPUESTA_ALMACEN.TotalMinutes).ToString(castellano);
            return avisado
                ? $"Se ha pedido a Almacén que meta {TextoProductos(lineas)} en el envío de hoy del pedido {pedido}. Le avisaremos con su respuesta; " +
                  $"si no contestan en {espera} minutos, irá en un pedido nuevo con sus propios portes."
                : $"{TextoProductos(lineas)} se ha guardado en el pedido {pedido}, pero no se ha podido avisar a Almacén: llámeles para que " +
                  "lo metan en el envío de hoy o lo pasen a un pedido nuevo.";
        }

        /// <summary>El pedido lleva plazos de prepago (PRE).</summary>
        public static bool EsPrepago(PedidoVentaDTO pedido)
            => string.Equals(pedido?.plazosPago?.Trim(), Constantes.PlazosPago.PREPAGO, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Caso 931007: el 409 PEDIDO_EN_PREPARACION. JSON de GlobalExceptionFilter: error.code, error.message y error.details con Pickings,
    /// Envio, AgenciaYaLoTiene, Opciones y Productos (además de empresa y pedido). No va a ELMAH (4xx de negocio).
    /// </summary>
    public class PedidoEnPreparacionException : NestoBusinessException
    {
        public PedidoEnPreparacionException(string mensaje, string empresa, int pedido, EvaluacionAmpliacion evaluacion)
            : base(mensaje, new ErrorContext { ErrorCode = AmpliacionPedidoEnPreparacion.CODIGO_ERROR, Empresa = empresa?.Trim(), Pedido = pedido })
        {
            StatusCode = HttpStatusCode.Conflict;
            Evaluacion = evaluacion;
            Context.AdditionalData["Pickings"] = evaluacion?.Pickings ?? new List<int>();
            Context.AdditionalData["Envio"] = evaluacion?.Envio;
            Context.AdditionalData["AgenciaYaLoTiene"] = evaluacion?.AgenciaYaLoTiene ?? false;
            Context.AdditionalData["Opciones"] = evaluacion?.Opciones ?? new List<string>();
            Context.AdditionalData["Productos"] = (evaluacion?.LineasAMover ?? new List<LineaPedidoVentaDTO>())
                .Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO).Select(l => l.Producto?.Trim()).ToList();
        }

        [JsonIgnore]
        public EvaluacionAmpliacion Evaluacion { get; }
    }
}
