using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#679 (pedido 928521, 19/10/26): las reglas de los enlaces de pago de un pedido. Un enlace de pago (PagosTPV de
    /// tipo TPV virtual) no lleva el pedido en ninguna columna (Documento va vacío): el pedido está en el concepto, que ponen
    /// Nesto («Pago pedido 928521 de Nueva Visión», PedidoVentaService/PlantillaVentaService.EnviarCobroTarjeta) y NestoApp
    /// («Pedido 928521»). Puras, para probarlas sin BD.
    /// </summary>
    public static class EnlacesPagoPedido
    {
        /// <summary>
        /// Un enlace pendiente más antiguo que esto se da por abandonado: los enlaces no caducan y quedan muchos sin pagar
        /// (en el 928521, tres de cuatro).
        /// </summary>
        public const int DIAS_ENLACE_PENDIENTE_VIVO = 30;

        // «Pedido 928521», «Pago pedido 928521 de Nueva Visión», «Pago pedido928521». Como mínimo 5 cifras: «Pago pedido 3»
        // (prueba de Reina) no es un pedido.
        private static readonly Regex PedidoEnConcepto = new Regex(@"pedido\s*n?º?\s*(\d{5,9})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // El concepto ENTERO es un número de pedido: «929559», « 929559 ». Seis cifras como mínimo (los pedidos de hoy): un
        // concepto de cinco cifras sueltas puede ser un cliente o un importe.
        private static readonly Regex SoloNumeroDePedido = new Regex(@"^\s*(\d{6,9})\s*$", RegexOptions.CultureInvariant);

        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>El número de pedido del concepto del enlace, o null si no lleva ninguno.</summary>
        public static int? PedidoDelConcepto(string concepto)
        {
            if (string.IsNullOrWhiteSpace(concepto))
            {
                return null;
            }
            // 11/11/26 (929559): el concepto puede ser solo el número del pedido («929559», tecleado a mano en el enlace). Sin
            // esto, la consulta de reembolso solo vio uno de los tres enlaces del pedido y los otros dos quedaron sin anular.
            Match soloNumero = SoloNumeroDePedido.Match(concepto);
            if (soloNumero.Success && int.TryParse(soloNumero.Groups[1].Value, out int pedidoSolo) && pedidoSolo > 0)
            {
                return pedidoSolo;
            }
            Match encontrado = PedidoEnConcepto.Match(concepto);
            return encontrado.Success && int.TryParse(encontrado.Groups[1].Value, out int numero) && numero > 0
                ? numero
                : (int?)null;
        }

        /// <summary>¿Es un enlace de pago (TPV virtual) de ese pedido? Los cobros de la app ya entran como Prepago (#436).</summary>
        public static bool EsEnlaceDelPedido(string tipo, string concepto, int pedido)
        {
            return string.Equals(tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase)
                && PedidoDelConcepto(concepto) == pedido;
        }

        public static bool EstaAutorizado(EnlacePagoPedido enlace)
            => string.Equals(enlace?.Estado?.Trim(), Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// NestoAPI#734: un enlace anulado (Administración decide que el pedido sale con reembolso y anula sus enlaces). La página
        /// de pago ya no deja pagarlo.
        /// </summary>
        public static bool EstaAnulado(string estado)
            => string.Equals(estado?.Trim(), Constantes.EstadosPagoTPV.CANCELADO, StringComparison.OrdinalIgnoreCase);

        public static bool EstaPendiente(EnlacePagoPedido enlace)
            => string.Equals(enlace?.Estado?.Trim(), Constantes.EstadosPagoTPV.PENDIENTE, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Lo cobrado con enlaces de pago del pedido: se trata como un prepago (#569), porque el cliente ya lo ha pagado.
        /// </summary>
        public static decimal ImporteAutorizado(IEnumerable<EnlacePagoPedido> enlaces)
        {
            decimal total = (enlaces ?? Enumerable.Empty<EnlacePagoPedido>()).Where(EstaAutorizado).Sum(e => e.Importe);
            total = RoundingHelper.DosDecimalesRound(total);
            return total < 0 ? 0 : total;
        }

        /// <summary>
        /// Los enlaces pendientes que siguen vivos, del más reciente al más antiguo: de los últimos
        /// <see cref="DIAS_ENLACE_PENDIENTE_VIVO"/> días y posteriores al último enlace cobrado (los de antes se dan por
        /// sustituidos: en el 928521 se pagó el cuarto y los otros tres quedaron pendientes).
        /// </summary>
        public static List<EnlacePagoPedido> PendientesVivos(IEnumerable<EnlacePagoPedido> enlaces, DateTime ahora)
        {
            List<EnlacePagoPedido> lista = (enlaces ?? Enumerable.Empty<EnlacePagoPedido>()).Where(e => e != null).ToList();
            DateTime? ultimoCobrado = lista.Where(EstaAutorizado).Select(e => (DateTime?)e.FechaCreacion).Max();
            DateTime limite = ahora.AddDays(-DIAS_ENLACE_PENDIENTE_VIVO);
            return lista
                .Where(e => EstaPendiente(e) && e.FechaCreacion >= limite && (!ultimoCobrado.HasValue || e.FechaCreacion > ultimoCobrado.Value))
                .OrderByDescending(e => e.FechaCreacion)
                .ToList();
        }

        /// <summary>
        /// El aviso de la propuesta de envío (NestoAPI#679, punto 1): «Este pedido tiene un enlace de pago pendiente de
        /// 252,94 € (enviado el 16/10/2026): ¿seguro que sale contra reembolso?».
        /// </summary>
        /// <param name="pedido">NestoAPI#824: el pedido del envío. Si algún enlace es de OTRO pedido del cliente, el aviso dice de qué
        /// pedido es cada uno («El cliente tiene un enlace de pago pendiente de 40,02 € del pedido 929852 …»).</param>
        public static string TextoAvisoPendientes(IReadOnlyList<EnlacePagoPedido> pendientes, int? pedido = null)
        {
            if (pendientes == null || pendientes.Count == 0)
            {
                return null;
            }
            EnlacePagoPedido ultimo = pendientes.OrderByDescending(e => e.FechaCreacion).First();
            string fecha = ultimo.FechaCreacion.ToString("dd/MM/yyyy", Espanol);
            if (pendientes.Any(e => !EsDeEstePedido(e, pedido)))
            {
                // NestoAPI#824 (929852, 05/11/26): el enlace del 929852 no se vio al tramitar el envío del 929323, que llevaba su factura
                string Uno(EnlacePagoPedido e) => string.Format(Espanol, "{0:C} {1} (enviado el {2})", e.Importe,
                    EsDeEstePedido(e, pedido) ? "de este pedido" : "del pedido " + e.Pedido.Value.ToString(CultureInfo.InvariantCulture),
                    e.FechaCreacion.ToString("dd/MM/yyyy", Espanol));
                List<string> enlaces = pendientes.OrderByDescending(e => e.FechaCreacion).Select(Uno).ToList();
                return pendientes.Count == 1
                    ? $"El cliente tiene un enlace de pago pendiente de {enlaces[0]}: ¿seguro que sale contra reembolso?"
                    : $"El cliente tiene {pendientes.Count} enlaces de pago pendientes: {string.Join(", ", enlaces.Take(enlaces.Count - 1))} " +
                      $"y {enlaces[enlaces.Count - 1]}: ¿seguro que sale contra reembolso?";
            }
            return pendientes.Count == 1
                ? string.Format(Espanol, "Este pedido tiene un enlace de pago pendiente de {0:C} (enviado el {1}): ¿seguro que sale contra reembolso?",
                    ultimo.Importe, fecha)
                : string.Format(Espanol, "Este pedido tiene {0} enlaces de pago pendientes (el último, de {1:C}, enviado el {2}): ¿seguro que sale contra reembolso?",
                    pendientes.Count, ultimo.Importe, fecha);
        }

        private static bool EsDeEstePedido(EnlacePagoPedido enlace, int? pedido)
            => !enlace.Pedido.HasValue || !pedido.HasValue || enlace.Pedido.Value == pedido.Value;

        /// <summary>
        /// NestoAPI#824: <see cref="PendientesVivos"/> pedido a pedido (lo de «posterior al último cobrado» es de cada pedido), todos
        /// juntos del más reciente al más antiguo. Para los enlaces de varios pedidos del mismo cliente.
        /// </summary>
        public static List<EnlacePagoPedido> PendientesVivosPorPedido(IEnumerable<EnlacePagoPedido> enlaces, DateTime ahora)
        {
            return (enlaces ?? Enumerable.Empty<EnlacePagoPedido>())
                .Where(e => e != null)
                .GroupBy(e => e.Pedido ?? 0)
                .SelectMany(g => PendientesVivos(g, ahora))
                .OrderByDescending(e => e.FechaCreacion)
                .ToList();
        }

        /// <summary>
        /// 09/11/26 (930167): LA REGLA de qué enlaces pendientes se miran antes de que un pedido salga contra reembolso, la misma
        /// para el aviso de la propuesta de envío (#824) y para la consulta a Administración (#734, que si no se quedaba en
        /// «no tiene enlaces» y no había forma de anular el del otro pedido): los del pedido y los de los otros pedidos del cliente
        /// y contacto (<see cref="LectorEnlacesPagoPedido.LeerDeOtrosPedidosDelCliente"/>), salvo los de pedidos ya pagados
        /// (<see cref="EnlacePagoPedido.PedidoPagado"/>), con <see cref="PendientesVivosPorPedido"/>.
        /// </summary>
        public static List<EnlacePagoPedido> PendientesDelPedidoYDelCliente(IEnumerable<EnlacePagoPedido> delPedido,
            IEnumerable<EnlacePagoPedido> deOtrosPedidos, DateTime ahora)
        {
            IEnumerable<EnlacePagoPedido> otros = (deOtrosPedidos ?? Enumerable.Empty<EnlacePagoPedido>()).Where(e => e != null && !e.PedidoPagado);
            return PendientesVivosPorPedido((delPedido ?? Enumerable.Empty<EnlacePagoPedido>()).Concat(otros), ahora);
        }

        /// <summary>
        /// 09/11/26 (930167): ¿el pedido está pagado? Facturado entero (alguna línea facturada y ninguna pendiente, en curso o en
        /// albarán) y sus facturas no deben nada en ExtractoCliente (todos sus apuntes con ImportePdte 0, y al menos uno). El 928286
        /// se cobró por reembolso con CTT: factura NV2616687 con todo a 0.
        /// </summary>
        public static bool EstaPagado(IEnumerable<short> estadosLineas, IEnumerable<decimal> pendienteApuntesFacturas)
        {
            List<short> estados = (estadosLineas ?? Enumerable.Empty<short>()).ToList();
            List<decimal> pendientes = (pendienteApuntesFacturas ?? Enumerable.Empty<decimal>()).ToList();
            bool facturadoEntero = estados.Contains(Constantes.EstadosLineaVenta.FACTURA)
                && !estados.Any(e => e == Constantes.EstadosLineaVenta.PENDIENTE || e == Constantes.EstadosLineaVenta.EN_CURSO
                    || e == Constantes.EstadosLineaVenta.ALBARAN);
            return facturadoEntero && pendientes.Count > 0 && pendientes.All(p => p == 0m);
        }

        // ---------------------------------------------------------------- envíos (puntos 2 y 3)

        /// <summary>
        /// ¿El envío está en manos de la agencia (o con la etiqueta ya hecha) y todavía puede cobrar el reembolso? En curso
        /// (etiqueta creada), tramitado o incidentado (puede reintentar la entrega, como el 249871). Pendiente no ha salido;
        /// entregado, devuelto y cerrado a mano ya no cobran.
        /// </summary>
        public static bool EstaEnReparto(short estado)
            => estado == Constantes.Agencias.ESTADO_EN_CURSO
            || estado == Constantes.Agencias.ESTADO_TRAMITADO
            || estado == Constantes.Agencias.ESTADO_INCIDENTADO;

        /// <summary>El envío del pedido en reparto con reembolso (el más reciente si hay varios); null si no hay ninguno.</summary>
        public static EnviosAgencia EnvioEnRepartoConReembolso(IEnumerable<EnviosAgencia> envios)
        {
            return (envios ?? Enumerable.Empty<EnviosAgencia>())
                .Where(e => e != null && EstaEnReparto(e.Estado) && e.Reembolso > 0m)
                .OrderByDescending(e => e.Numero)
                .FirstOrDefault();
        }

        /// <summary>
        /// NestoAPI#824 (929852, 05/11/26): TODOS los envíos en reparto con reembolso (el criterio de
        /// <see cref="EnvioEnRepartoConReembolso"/>): primero los del pedido (si se da) y después los demás, del más reciente al más
        /// antiguo. El reembolso se calcula con lo pendiente del CLIENTE, así que la factura de un pedido puede ir en el envío de otro.
        /// </summary>
        public static List<EnviosAgencia> EnviosEnRepartoConReembolso(IEnumerable<EnviosAgencia> envios, int? pedidoPrimero = null)
        {
            return (envios ?? Enumerable.Empty<EnviosAgencia>())
                .Where(e => e != null && EstaEnReparto(e.Estado) && e.Reembolso > 0m)
                .GroupBy(e => e.Numero).Select(g => g.First())
                .OrderByDescending(e => pedidoPrimero.HasValue && e.Pedido == pedidoPrimero.Value)
                .ThenByDescending(e => e.Numero)
                .ToList();
        }

        /// <summary>
        /// NestoAPI#679, punto 2: los avisos al generar un enlace de pago de un pedido. SOLO avisan: la forma de pago no se
        /// cambia (decisión pendiente).
        /// </summary>
        public static List<AvisoEnlacePago> AvisosAlGenerar(int pedido, DatosPedidoEnlacePago datos)
        {
            var avisos = new List<AvisoEnlacePago>();
            if (datos == null)
            {
                return avisos;
            }
            EnviosAgencia enCamino = EnvioEnRepartoConReembolso(datos.Envios);
            if (enCamino != null)
            {
                avisos.Add(new AvisoEnlacePago
                {
                    Codigo = CodigosAvisoEnlacePago.ENVIO_EN_CAMINO_CON_REEMBOLSO,
                    Texto = string.Format(Espanol, "Este pedido ya va en camino con un reembolso de {0:C}: si el cliente paga por enlace, " +
                        "la agencia intentará cobrarle otra vez. Hable antes con Administración/Logística.", enCamino.Reembolso)
                });
                return avisos;
            }
            if (datos.ContraReembolso && datos.TieneLineasSinServir)
            {
                avisos.Add(new AvisoEnlacePago
                {
                    Codigo = CodigosAvisoEnlacePago.PEDIDO_CONTRA_REEMBOLSO,
                    Texto = $"El pedido {pedido} va contra reembolso. Si el cliente lo paga con este enlace, pase el pedido a «tarjeta prepago» " +
                        "antes de que salga; si no, la agencia intentará cobrárselo otra vez."
                });
            }
            return avisos;
        }
    }

    /// <summary>NestoAPI#679, punto 2: lo que hace falta saber del pedido para avisar al generar un enlace de pago.</summary>
    public class DatosPedidoEnlacePago
    {
        /// <summary>La forma de pago del pedido es contra reembolso (<see cref="GestorPortes.EsContraReembolso"/>).</summary>
        public bool ContraReembolso { get; set; }
        /// <summary>Le quedan líneas pendientes o en curso: aún no ha salido todo.</summary>
        public bool TieneLineasSinServir { get; set; }
        public List<EnviosAgencia> Envios { get; set; } = new List<EnviosAgencia>();
    }

    /// <summary>NestoAPI#679: las lecturas de BD de los enlaces de pago de un pedido.</summary>
    public static class LectorEnlacesPagoPedido
    {
        /// <summary>Los enlaces de pago (TPV virtual) pendientes o autorizados del pedido.</summary>
        public static List<EnlacePagoPedido> Leer(NVEntities db, string empresa, int pedido)
        {
            string numero = pedido.ToString(CultureInfo.InvariantCulture);
            string empresaPedido = empresa?.Trim();
            // Primero lo que se puede filtrar en SQL (LIKE '%928521%'); el concepto exacto, en memoria.
            var candidatos = db.PagosTPV.AsNoTracking()
                .Where(p => p.Empresa == empresaPedido && p.Tipo == Constantes.TiposPagoTPV.TPV_VIRTUAL &&
                            (p.Estado == Constantes.EstadosPagoTPV.PENDIENTE || p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO) &&
                            p.Descripcion.Contains(numero))
                .Select(p => new { p.Id, p.Tipo, p.Descripcion, p.Importe, p.Estado, p.FechaCreacion, p.FechaActualizacion, p.Usuario })
                .ToList();
            return candidatos
                .Where(p => EnlacesPagoPedido.EsEnlaceDelPedido(p.Tipo, p.Descripcion, pedido))
                .Select(p => new EnlacePagoPedido
                {
                    Id = p.Id,
                    Importe = p.Importe,
                    Estado = p.Estado?.Trim(),
                    FechaCreacion = p.FechaCreacion,
                    FechaActualizacion = p.FechaActualizacion,
                    Usuario = p.Usuario?.Trim(),
                    Descripcion = p.Descripcion?.Trim(),
                    Pedido = pedido
                })
                .ToList();
        }

        /// <summary>
        /// NestoAPI#824 (929852, 05/11/26): los enlaces de pago (TPV virtual) pendientes o autorizados de los OTROS pedidos del mismo
        /// cliente y contacto, de los últimos <see cref="EnlacesPagoPedido.DIAS_ENLACE_PENDIENTE_VIVO"/> días. El enlace no guarda el
        /// contacto de forma fiable (PagosTPV.Contacto va a NULL en los de Nesto): se mira el del pedido del concepto.
        /// </summary>
        public static List<EnlacePagoPedido> LeerDeOtrosPedidosDelCliente(NVEntities db, string empresa, string cliente, string contacto,
            int excluirPedido, DateTime ahora)
        {
            string empresaPedido = empresa?.Trim();
            string clientePedido = cliente?.Trim();
            string contactoPedido = contacto?.Trim();
            if (string.IsNullOrEmpty(clientePedido))
            {
                return new List<EnlacePagoPedido>();
            }
            DateTime desde = ahora.AddDays(-EnlacesPagoPedido.DIAS_ENLACE_PENDIENTE_VIVO);
            var candidatos = db.PagosTPV.AsNoTracking()
                .Where(p => p.Empresa == empresaPedido && p.Cliente == clientePedido && p.Tipo == Constantes.TiposPagoTPV.TPV_VIRTUAL &&
                            (p.Estado == Constantes.EstadosPagoTPV.PENDIENTE || p.Estado == Constantes.EstadosPagoTPV.AUTORIZADO) &&
                            p.FechaCreacion >= desde)
                .Select(p => new { p.Id, p.Tipo, p.Descripcion, p.Importe, p.Estado, p.FechaCreacion, p.FechaActualizacion, p.Usuario })
                .ToList()
                .Select(p => new { Pago = p, Pedido = EnlacesPagoPedido.PedidoDelConcepto(p.Descripcion) })
                .Where(p => p.Pedido.HasValue && p.Pedido.Value != excluirPedido && EnlacesPagoPedido.EsEnlaceDelPedido(p.Pago.Tipo, p.Pago.Descripcion, p.Pedido.Value))
                .ToList();
            if (candidatos.Count == 0)
            {
                return new List<EnlacePagoPedido>();
            }
            List<int> pedidos = candidatos.Select(c => c.Pedido.Value).Distinct().ToList();
            HashSet<int> delContacto = new HashSet<int>(db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresaPedido && pedidos.Contains(c.Número) && c.Nº_Cliente == clientePedido && c.Contacto == contactoPedido)
                .Select(c => c.Número)
                .ToList());
            HashSet<int> pagados = PedidosPagados(db, empresaPedido, clientePedido, delContacto.ToList());
            return candidatos
                .Where(c => delContacto.Contains(c.Pedido.Value))
                .Select(c => new EnlacePagoPedido
                {
                    PedidoPagado = pagados.Contains(c.Pedido.Value),
                    Id = c.Pago.Id,
                    Importe = c.Pago.Importe,
                    Estado = c.Pago.Estado?.Trim(),
                    FechaCreacion = c.Pago.FechaCreacion,
                    FechaActualizacion = c.Pago.FechaActualizacion,
                    Usuario = c.Pago.Usuario?.Trim(),
                    Descripcion = c.Pago.Descripcion?.Trim(),
                    Pedido = c.Pedido
                })
                .ToList();
        }

        /// <summary>
        /// 09/11/26 (930167): de esos pedidos del cliente, los que ya están pagados (<see cref="EnlacesPagoPedido.EstaPagado"/>):
        /// las líneas del pedido y los apuntes de sus facturas en ExtractoCliente. Si falla, ninguno (se avisa como antes).
        /// </summary>
        internal static HashSet<int> PedidosPagados(NVEntities db, string empresa, string cliente, List<int> pedidos)
        {
            var pagados = new HashSet<int>();
            if (pedidos == null || pedidos.Count == 0)
            {
                return pagados;
            }
            try
            {
                var lineas = db.LinPedidoVtas.AsNoTracking()
                    .Where(l => l.Empresa == empresa && pedidos.Contains(l.Número))
                    .Select(l => new { l.Número, l.Estado, l.Nº_Factura })
                    .ToList();
                List<string> facturas = lineas.Where(l => !string.IsNullOrWhiteSpace(l.Nº_Factura))
                    .Select(l => l.Nº_Factura.Trim()).Distinct().ToList();
                var apuntes = facturas.Count == 0
                    ? new List<(string Documento, decimal Pendiente)>()
                    : db.ExtractosCliente.AsNoTracking()
                        .Where(e => e.Empresa == empresa && e.Número == cliente && facturas.Contains(e.Nº_Documento))
                        .Select(e => new { e.Nº_Documento, e.ImportePdte })
                        .ToList()
                        .Select(e => (Documento: e.Nº_Documento?.Trim(), Pendiente: e.ImportePdte))
                        .ToList();
                foreach (var pedido in lineas.GroupBy(l => l.Número))
                {
                    HashSet<string> suyas = new HashSet<string>(pedido.Where(l => !string.IsNullOrWhiteSpace(l.Nº_Factura)).Select(l => l.Nº_Factura.Trim()));
                    if (EnlacesPagoPedido.EstaPagado(pedido.Select(l => l.Estado), apuntes.Where(a => suyas.Contains(a.Documento)).Select(a => a.Pendiente)))
                    {
                        _ = pagados.Add(pedido.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Enlaces de pago] No se ha podido mirar si están pagados los pedidos {string.Join(", ", pedidos)}: {ex.Message}", ex),
                    "Sistema (enlaces de pago)");
            }
            return pagados;
        }

        /// <summary>NestoAPI#679, punto 2: el pedido (forma de pago y si le queda algo por servir) y sus envíos; null si no existe.</summary>
        public static async Task<DatosPedidoEnlacePago> LeerDatosPedido(string empresa, int pedido)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                string empresaPedido = empresa?.Trim();
                var cab = await db.CabPedidoVtas.AsNoTracking()
                    .Where(c => c.Empresa == empresaPedido && c.Número == pedido)
                    .Select(c => new { c.Forma_Pago, c.PlazosPago, c.CCC, c.Periodo_Facturacion, c.NotaEntrega })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (cab == null)
                {
                    return null;
                }
                bool sinServir = await db.LinPedidoVtas
                    .AnyAsync(l => l.Empresa == empresaPedido && l.Número == pedido &&
                                   (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO))
                    .ConfigureAwait(false);
                List<EnviosAgencia> envios = await db.EnviosAgencias.AsNoTracking()
                    .Where(e => e.Empresa == empresaPedido && e.Pedido == pedido)
                    .ToListAsync().ConfigureAwait(false);
                return new DatosPedidoEnlacePago
                {
                    ContraReembolso = GestorPortes.EsContraReembolso(cab.Forma_Pago?.Trim(), cab.PlazosPago, cab.CCC,
                        cab.Periodo_Facturacion?.Trim(), cab.NotaEntrega),
                    TieneLineasSinServir = sinServir,
                    Envios = envios
                };
            }
        }
    }
}
