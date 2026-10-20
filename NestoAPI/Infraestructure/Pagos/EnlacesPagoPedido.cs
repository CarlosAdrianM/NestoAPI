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

        private static readonly CultureInfo Espanol = new CultureInfo("es-ES");

        /// <summary>El número de pedido del concepto del enlace, o null si no lleva ninguno.</summary>
        public static int? PedidoDelConcepto(string concepto)
        {
            if (string.IsNullOrWhiteSpace(concepto))
            {
                return null;
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
        public static string TextoAvisoPendientes(IReadOnlyList<EnlacePagoPedido> pendientes)
        {
            if (pendientes == null || pendientes.Count == 0)
            {
                return null;
            }
            EnlacePagoPedido ultimo = pendientes.OrderByDescending(e => e.FechaCreacion).First();
            string fecha = ultimo.FechaCreacion.ToString("dd/MM/yyyy", Espanol);
            return pendientes.Count == 1
                ? string.Format(Espanol, "Este pedido tiene un enlace de pago pendiente de {0:C} (enviado el {1}): ¿seguro que sale contra reembolso?",
                    ultimo.Importe, fecha)
                : string.Format(Espanol, "Este pedido tiene {0} enlaces de pago pendientes (el último, de {1:C}, enviado el {2}): ¿seguro que sale contra reembolso?",
                    pendientes.Count, ultimo.Importe, fecha);
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
                    Usuario = p.Usuario?.Trim()
                })
                .ToList();
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
