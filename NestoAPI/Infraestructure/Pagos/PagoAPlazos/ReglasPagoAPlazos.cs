using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>NestoAPI#864: los estados de una solicitud de pago a plazos (los que se guardan y los que se enseñan).</summary>
    public static class EstadosPagoAPlazos
    {
        /// <summary>Solo en las respuestas: el pedido no tiene ninguna solicitud.</summary>
        public const string SIN_SOLICITUD = "SinSolicitud";
        /// <summary>Creada: el cliente todavía no ha terminado (o el proveedor no ha contestado).</summary>
        public const string PENDIENTE = "Pendiente";
        /// <summary>El proveedor la ha aprobado: el pedido tiene su prepago y hay que enviarlo dentro del plazo.</summary>
        public const string APROBADO = "Aprobado";
        /// <summary>Cobro confirmado al proveedor (el pedido ha salido).</summary>
        public const string CAPTURADO = "Capturado";
        /// <summary>Cancelada por nosotros (a mano, o porque el pedido se ha borrado o ha cambiado).</summary>
        public const string CANCELADO = "Cancelado";
        /// <summary>El cliente no la terminó a tiempo, o el pedido no salió dentro del plazo de captura.</summary>
        public const string CADUCADO = "Caducado";
        /// <summary>El proveedor no ha aprobado la financiación.</summary>
        public const string RECHAZADO = "Rechazado";

        public static bool EsViva(string estado) => Es(estado, PENDIENTE) || Es(estado, APROBADO);

        /// <summary>El pedido tiene (o ha tenido) su financiación concedida: no se abre otra.</summary>
        public static bool EsConcedida(string estado) => Es(estado, APROBADO) || Es(estado, CAPTURADO);

        public static bool Es(string estado, string cual) => string.Equals(estado?.Trim(), cual, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Una fila de dbo.SolicitudesPagoAPlazos.</summary>
    public class SolicitudPagoAPlazos
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Código del proveedor (<see cref="IProveedorPagoAPlazos.Codigo"/>).</summary>
        public string Proveedor { get; set; }
        /// <summary>Nuestra referencia ante el proveedor (PAP-{Id}).</summary>
        public string Referencia { get; set; }
        public string ReferenciaExterna { get; set; }
        public decimal Importe { get; set; }
        public string Canal { get; set; }
        public string Estado { get; set; }
        public bool Pruebas { get; set; }
        public string Usuario { get; set; }
        public string Correo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaAprobacion { get; set; }
        public DateTime? FechaLimiteCaptura { get; set; }
        public DateTime? FechaCaptura { get; set; }
        public decimal? ImporteCapturado { get; set; }
        public DateTime? FechaAvisoPlazo { get; set; }
        public DateTime? FechaCierre { get; set; }
        public string Motivo { get; set; }
        public int? PrepagoId { get; set; }

        public ReferenciaSolicitudProveedor ParaElProveedor() => new ReferenciaSolicitudProveedor
        {
            Referencia = Referencia?.Trim(),
            ReferenciaExterna = ReferenciaExterna?.Trim(),
            Pruebas = Pruebas,
            Importe = Importe
        };
    }

    /// <summary>Lo que hace falta saber del pedido para ofrecer, abrir y seguir su pago a plazos.</summary>
    public class PedidoPagoAPlazos
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public bool EsNotaEntrega { get; set; }
        public bool ContraReembolso { get; set; }
        public bool RecogidaEnTienda { get; set; }
        public List<LineaPagoAPlazos> Lineas { get; set; } = new List<LineaPagoAPlazos>();
        /// <summary>Lo que suman los prepagos que ya tiene el pedido.</summary>
        public decimal Prepagos { get; set; }
        /// <summary>Lo cobrado con enlaces de pago de este pedido.</summary>
        public decimal CobradoPorEnlaces { get; set; }
        public string Correo { get; set; }
        public DireccionPagoAPlazos Facturacion { get; set; }
        public DireccionPagoAPlazos Envio { get; set; }
        /// <summary>El cliente tiene la app de clientes con algún dispositivo activo para las notificaciones.</summary>
        public bool ClienteTieneApp { get; set; }
    }

    // GET api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos/Disponible
    public class DisponibilidadPagoAPlazosDTO
    {
        public bool Disponible { get; set; }
        /// <summary>Por qué no (de usted). Null si está disponible.</summary>
        public string Motivo { get; set; }
        /// <summary>Código del proveedor activo. Null si no hay.</summary>
        public string Proveedor { get; set; }
        /// <summary>El nombre para pintarlo («Aplázame»).</summary>
        public string NombreProveedor { get; set; }
        /// <summary>Lo que se financiaría: lo pendiente de pagar del pedido, con IVA.</summary>
        public decimal Importe { get; set; }
        public decimal? ImporteMinimo { get; set; }
        public decimal? ImporteMaximo { get; set; }
        /// <summary>Días que da el proveedor para enviar el pedido desde que aprueba.</summary>
        public int? DiasPlazoCaptura { get; set; }
        public bool ClienteTieneApp { get; set; }
        /// <summary>El canal sugerido: «App» si el cliente la tiene; si no, «Enlace».</summary>
        public string Canal { get; set; }
        /// <summary>Algo que conviene decirle a quien pasa el pedido (no impide nada). Null si no hay.</summary>
        public string Aviso { get; set; }
        /// <summary>Se haría contra el entorno de pruebas del proveedor (sin dinero).</summary>
        public bool Pruebas { get; set; }
    }

    // POST api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos
    public class PeticionPagoAPlazosDTO
    {
        /// <summary>«App», «Enlace» o vacío (App si el cliente la tiene; si no, Enlace).</summary>
        public string Canal { get; set; }
        /// <summary>El correo del cliente para el proveedor. Vacío: el de su ficha.</summary>
        public string Correo { get; set; }
    }

    // GET api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos/Estado
    public class EstadoPagoAPlazosDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public bool HaySolicitud { get; set; }
        public int? IdSolicitud { get; set; }
        /// <summary><see cref="EstadosPagoAPlazos"/>.</summary>
        public string Estado { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        public string Canal { get; set; }
        public decimal? Importe { get; set; }
        public decimal? ImporteCapturado { get; set; }
        public DateTime? FechaSolicitud { get; set; }
        public DateTime? FechaAprobacion { get; set; }
        /// <summary>Hasta cuándo se puede enviar el pedido sin perder la financiación (solo aprobada).</summary>
        public DateTime? FechaLimiteCaptura { get; set; }
        /// <summary>Días que quedan para enviarlo (solo aprobada y sin capturar).</summary>
        public int? DiasParaCapturar { get; set; }
        public DateTime? FechaCaptura { get; set; }
        /// <summary>Cuándo se canceló, caducó o se rechazó.</summary>
        public DateTime? FechaCierre { get; set; }
        public string Motivo { get; set; }
        /// <summary>Quién la pidió.</summary>
        public string Usuario { get; set; }
        /// <summary>El enlace para el cliente (solo pendiente y por el canal Enlace).</summary>
        public string UrlPago { get; set; }
        public bool Pruebas { get; set; }
    }

    /// <summary>La respuesta del POST: el estado y qué se ha hecho para que el cliente se entere.</summary>
    public class SolicitudPagoAPlazosDTO : EstadoPagoAPlazosDTO
    {
        /// <summary>A cuántos dispositivos del cliente se le ha mandado la push (canal App).</summary>
        public int DispositivosAvisados { get; set; }
        /// <summary>Algo que conviene decirle a quien pasa el pedido. Null si no hay.</summary>
        public string Aviso { get; set; }
    }

    // GET api/Pedidos/Cliente/{pedido}/PagoAPlazos (la app de clientes)
    public class PagoAPlazosClienteDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int IdSolicitud { get; set; }
        public string Estado { get; set; }
        public decimal Importe { get; set; }
        public string Proveedor { get; set; }
        public string NombreProveedor { get; set; }
        /// <summary>Lo que hay que abrir en el WebView para terminar (solo pendiente).</summary>
        public string UrlPago { get; set; }
        public DateTime FechaSolicitud { get; set; }
    }

    /// <summary>Qué hay que hacer con una solicitud aprobada y sin capturar.</summary>
    public enum AccionPlazoPagoAPlazos
    {
        Nada,
        /// <summary>El pedido ha salido entero: confirmar el cobro.</summary>
        Capturar,
        /// <summary>Quedan pocos días y el pedido sigue sin salir: avisar (una vez).</summary>
        Avisar,
        /// <summary>Se acabó el plazo y no ha salido nada: se pierde la financiación.</summary>
        Caducar,
        /// <summary>Se acabó el plazo con parte del pedido ya servida: se cobra lo servido y se cierra.</summary>
        CapturarLoServido,
        /// <summary>El pedido ya no existe (o se ha quedado sin líneas): cancelar la financiación.</summary>
        CancelarSinPedido
    }

    /// <summary>
    /// NestoAPI#864: las reglas puras del pago a plazos de un pedido. No saben qué financiera es: trabajan con las
    /// condiciones que da el proveedor.
    /// </summary>
    public static class ReglasPagoAPlazos
    {
        public const string MOTIVO_NO_DISPONIBLE = "El pago a plazos no está disponible ahora mismo.";
        public const string MOTIVO_CANAL_NO_VALIDO = "El canal tiene que ser «App» o «Enlace».";
        public const string MOTIVO_SIN_APP = "El cliente no tiene la app con las notificaciones activas: mándele el enlace.";
        public const string AVISO_SIN_APP = "El cliente no tiene la app: con la app instalada le sería más cómodo terminar el pago.";
        public const string AVISO_CONTRA_REEMBOLSO = "El pedido va contra reembolso: cambie su forma de pago para que no se le cobre otra vez al entregarlo.";

        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        internal static string Euros(decimal importe) => importe.ToString("N2", Es) + " €";

        /// <summary>Línea que se financia: pendiente, en curso o en albarán (ni presupuesto ni ya facturada).</summary>
        public static bool SeFinancia(LineaPagoAPlazos linea)
            => linea != null && linea.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && linea.Estado <= Constantes.EstadosLineaVenta.ALBARAN;

        /// <summary>Línea que todavía no ha salido.</summary>
        public static bool EstaSinServir(LineaPagoAPlazos linea)
            => linea != null && linea.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && linea.Estado < Constantes.EstadosLineaVenta.ALBARAN;

        /// <summary>Lo que queda por pagar del pedido: sus líneas sin facturar menos los prepagos y lo cobrado por enlace.</summary>
        public static decimal ImportePendiente(PedidoPagoAPlazos pedido)
        {
            if (pedido == null)
            {
                return 0;
            }
            decimal total = RoundingHelper.DosDecimalesRound((pedido.Lineas ?? new List<LineaPagoAPlazos>()).Where(SeFinancia).Sum(l => l.Total));
            decimal pendiente = total - pedido.Prepagos - pedido.CobradoPorEnlaces;
            return pendiente < 0 ? 0 : RoundingHelper.DosDecimalesRound(pendiente);
        }

        /// <summary>
        /// Por qué NO se puede ofrecer el pago a plazos de este pedido (de usted), o null si se puede. El orden importa: primero
        /// lo que es del interruptor y del proveedor, después lo que es del pedido.
        /// </summary>
        public static string MotivoNoDisponible(ConfiguracionPagoAPlazos configuracion, string nombreProveedor,
            CondicionesProveedorPagoAPlazos condiciones, PedidoPagoAPlazos pedido, IEnumerable<SolicitudPagoAPlazos> solicitudes, DateTime hoy)
        {
            if (configuracion == null || pedido == null || !configuracion.SeOfreceA(pedido.Cliente)
                || condiciones == null || !condiciones.Operativo)
            {
                return MOTIVO_NO_DISPONIBLE;
            }
            string nombre = string.IsNullOrWhiteSpace(nombreProveedor) ? "El proveedor" : nombreProveedor.Trim();
            List<LineaPagoAPlazos> lineas = (pedido.Lineas ?? new List<LineaPagoAPlazos>()).Where(l => l != null).ToList();

            if (pedido.EsNotaEntrega)
            {
                return "Las notas de entrega no se pueden pagar a plazos.";
            }
            if (lineas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.FACTURA))
            {
                return "El pedido ya está facturado: el pago a plazos es solo para pedidos, no para deuda.";
            }
            if ((solicitudes ?? Enumerable.Empty<SolicitudPagoAPlazos>()).Any(s => s != null && EstadosPagoAPlazos.EsConcedida(s.Estado)))
            {
                return $"Este pedido ya está financiado con {nombre}.";
            }
            if (!lineas.Any(SeFinancia))
            {
                return lineas.Any(l => l.Estado == Constantes.EstadosLineaVenta.PRESUPUESTO)
                    ? "Es un presupuesto: páselo a pedido antes de ofrecer el pago a plazos."
                    : "El pedido no tiene nada pendiente que se pueda pagar a plazos.";
            }
            decimal importe = ImportePendiente(pedido);
            if (importe <= 0)
            {
                return "El pedido ya está pagado.";
            }
            if (condiciones.ImporteMinimo.HasValue && importe < condiciones.ImporteMinimo.Value)
            {
                return $"{nombre} financia pedidos desde {Euros(condiciones.ImporteMinimo.Value)} y a este le quedan por pagar {Euros(importe)}.";
            }
            if (condiciones.ImporteMaximo.HasValue && importe > condiciones.ImporteMaximo.Value)
            {
                return $"{nombre} financia pedidos de hasta {Euros(condiciones.ImporteMaximo.Value)} y a este le quedan por pagar {Euros(importe)}.";
            }
            DateTime limite = hoy.Date.AddDays(condiciones.DiasPlazoCaptura);
            List<LineaPagoAPlazos> tarde = lineas.Where(l => EstaSinServir(l) && l.FechaEntrega.Date > limite).ToList();
            if (tarde.Any())
            {
                DateTime entrega = tarde.Max(l => l.FechaEntrega.Date);
                string cuando = entrega.Year >= 2099 ? "sin fecha de entrega" : "con entrega el " + entrega.ToString("dd/MM/yyyy", Es);
                return $"{nombre} exige enviar el pedido en {condiciones.DiasPlazoCaptura} días desde que aprueba el pago " +
                    $"y este pedido tiene líneas {cuando}: no puede salir en plazo.";
            }
            return null;
        }

        /// <summary>
        /// El canal: el que se pide o, si no se pide ninguno, App si el cliente la tiene y Enlace si no. Devuelve null y el
        /// motivo (con <paramref name="conflicto"/> = true si es que pide App y el cliente no la tiene) si no puede ser.
        /// </summary>
        public static string ElegirCanal(string canalPedido, bool clienteTieneApp, out string motivo, out bool conflicto)
        {
            motivo = null;
            conflicto = false;
            if (string.IsNullOrWhiteSpace(canalPedido))
            {
                return clienteTieneApp ? CanalesPagoAPlazos.APP : CanalesPagoAPlazos.ENLACE;
            }
            string canal = CanalesPagoAPlazos.Normalizar(canalPedido);
            if (canal == null)
            {
                motivo = MOTIVO_CANAL_NO_VALIDO;
                return null;
            }
            if (canal == CanalesPagoAPlazos.APP && !clienteTieneApp)
            {
                motivo = MOTIVO_SIN_APP;
                conflicto = true;
                return null;
            }
            return canal;
        }

        /// <summary>Nuestra referencia ante el proveedor, estable y única: PAP-{Id}.</summary>
        public static string Referencia(int idSolicitud) => "PAP-" + idSolicitud.ToString(CultureInfo.InvariantCulture);

        /// <summary>El concepto del Prepago (50 caracteres): qué es, de qué pedido y la referencia, que lo hace idempotente.</summary>
        public static string ConceptoPrepago(string prefijo, int pedido, string referencia)
        {
            string concepto = $"{prefijo?.Trim()} {pedido.ToString(CultureInfo.InvariantCulture)} {referencia?.Trim()}".Trim();
            return concepto.Length > 50 ? concepto.Substring(0, 50) : concepto;
        }

        /// <summary>Días (enteros, hacia arriba) que quedan hasta el límite; 0 si ya ha pasado.</summary>
        public static int DiasHasta(DateTime limite, DateTime ahora)
        {
            double dias = (limite - ahora).TotalDays;
            return dias <= 0 ? 0 : (int)Math.Ceiling(dias);
        }

        /// <summary>Una solicitud que el cliente no ha terminado caduca a los <paramref name="diasValidez"/> días.</summary>
        public static bool PendienteCaducada(SolicitudPagoAPlazos solicitud, DateTime ahora, int diasValidez)
            => solicitud != null && EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.PENDIENTE)
               && solicitud.FechaCreacion.AddDays(diasValidez) <= ahora;

        /// <summary>
        /// Qué toca con una solicitud aprobada y sin capturar. <paramref name="pedido"/> null = el pedido ya no existe.
        /// Se captura cuando TODAS las líneas están en albarán o factura (como el job de #663).
        /// </summary>
        public static AccionPlazoPagoAPlazos DecidirPlazo(SolicitudPagoAPlazos solicitud, PedidoPagoAPlazos pedido, DateTime ahora, int diasAviso)
        {
            if (solicitud == null || !EstadosPagoAPlazos.Es(solicitud.Estado, EstadosPagoAPlazos.APROBADO))
            {
                return AccionPlazoPagoAPlazos.Nada;
            }
            List<LineaPagoAPlazos> lineas = (pedido?.Lineas ?? new List<LineaPagoAPlazos>())
                .Where(l => l != null && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE).ToList();
            if (pedido == null || lineas.Count == 0)
            {
                return AccionPlazoPagoAPlazos.CancelarSinPedido;
            }
            if (lineas.All(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN))
            {
                return AccionPlazoPagoAPlazos.Capturar;
            }
            if (!solicitud.FechaLimiteCaptura.HasValue)
            {
                return AccionPlazoPagoAPlazos.Nada;
            }
            if (solicitud.FechaLimiteCaptura.Value <= ahora)
            {
                return lineas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN)
                    ? AccionPlazoPagoAPlazos.CapturarLoServido
                    : AccionPlazoPagoAPlazos.Caducar;
            }
            return !solicitud.FechaAvisoPlazo.HasValue && solicitud.FechaLimiteCaptura.Value.AddDays(-diasAviso) <= ahora
                ? AccionPlazoPagoAPlazos.Avisar
                : AccionPlazoPagoAPlazos.Nada;
        }

        /// <summary>Lo servido del pedido (líneas en albarán o factura), con IVA.</summary>
        public static decimal ImporteServido(PedidoPagoAPlazos pedido)
            => RoundingHelper.DosDecimalesRound((pedido?.Lineas ?? new List<LineaPagoAPlazos>())
                .Where(l => l != null && l.Estado >= Constantes.EstadosLineaVenta.ALBARAN).Sum(l => l.Total));

        /// <summary>La solicitud que manda de un pedido: la concedida si la hay; si no, la última.</summary>
        public static SolicitudPagoAPlazos LaQueManda(IEnumerable<SolicitudPagoAPlazos> solicitudes)
        {
            List<SolicitudPagoAPlazos> lista = (solicitudes ?? Enumerable.Empty<SolicitudPagoAPlazos>()).Where(s => s != null).ToList();
            return lista.Where(s => EstadosPagoAPlazos.EsConcedida(s.Estado)).OrderByDescending(s => s.Id).FirstOrDefault()
                ?? lista.OrderByDescending(s => s.Id).FirstOrDefault();
        }
    }
}
