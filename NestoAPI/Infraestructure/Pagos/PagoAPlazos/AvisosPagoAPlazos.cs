using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    /// <summary>
    /// NestoAPI#864: los avisos del pago a plazos de un pedido. Al CLIENTE, la push de la app para que termine el pago y la de
    /// cómo ha acabado (aprobado o rechazado); a quien pasó el pedido, a quien pidió la financiación y a su vendedor, la campana
    /// de Nesto y la push de NestoApp.
    /// </summary>
    public interface IAvisosPagoAPlazos
    {
        /// <summary>Push al cliente (app de clientes) para que termine el pago. Devuelve a cuántos dispositivos ha salido.</summary>
        Task<int> AlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor);

        /// <summary>El proveedor ha aprobado: el pedido ya tiene su prepago. <paramref name="faltaPorPagar"/> &gt; 0 si no lo cubre entero.</summary>
        Task Aprobado(SolicitudPagoAPlazos solicitud, string nombreProveedor, decimal faltaPorPagar, bool contraReembolso);

        Task Rechazado(SolicitudPagoAPlazos solicitud, string nombreProveedor);

        /// <summary>
        /// Push al CLIENTE (app de clientes): su pago a plazos está aprobado. Solo si tiene la app (sin dispositivo, nada).
        /// Devuelve a cuántos dispositivos ha salido.
        /// </summary>
        Task<int> AprobadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor);

        /// <summary>
        /// Push al CLIENTE (app de clientes): su pago a plazos no ha salido adelante. <paramref name="despuesDeAprobar"/> si
        /// antes se le había dicho que estaba aprobado. Solo si tiene la app. Devuelve a cuántos dispositivos ha salido.
        /// </summary>
        Task<int> RechazadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor, bool despuesDeAprobar);

        /// <summary>Quedan <paramref name="dias"/> días para enviar el pedido o se pierde la financiación.</summary>
        Task PlazoCerca(SolicitudPagoAPlazos solicitud, string nombreProveedor, int dias);

        /// <summary>
        /// El pago a plazos se ha quedado sin efecto (plazo vencido, cancelado, pedido cambiado…). <paramref name="motivo"/> es la
        /// explicación entera, con lo que ha pasado con el prepago.
        /// </summary>
        Task Perdido(SolicitudPagoAPlazos solicitud, string nombreProveedor, string motivo);
    }

    /// <summary>
    /// NestoAPI#864: los textos y los Datos de cada aviso. Puro, y es el CONTRATO con las apps: el tipo y los Datos de la
    /// push al cliente los lee TiendasNuevaVision; los de los avisos al vendedor, Nesto y NestoApp (llevan «pedido», así que
    /// NestoApp abre el pedido sin cambios).
    /// </summary>
    public static class TextosAvisosPagoAPlazos
    {
        /// <summary>Push al CLIENTE (app de clientes): tiene un pedido pendiente de pago a plazos.</summary>
        public const string TIPO_CLIENTE = "PagoAPlazosPendiente";
        /// <summary>Push al CLIENTE: su pago a plazos está aprobado. La app lo abre como el pendiente (TiendasNuevaVision#99).</summary>
        public const string TIPO_APROBADO_CLIENTE = "PagoAPlazosAprobadoCliente";
        /// <summary>Push al CLIENTE: su pago a plazos no se ha aprobado (o se ha rechazado después de aprobarlo).</summary>
        public const string TIPO_RECHAZADO_CLIENTE = "PagoAPlazosRechazadoCliente";
        public const string TIPO_APROBADO = "PagoAPlazosAprobado";
        public const string TIPO_RECHAZADO = "PagoAPlazosRechazado";
        public const string TIPO_PLAZO_CERCA = "PagoAPlazosPlazoCerca";
        public const string TIPO_PERDIDO = "PagoAPlazosPerdido";

        private static readonly CultureInfo Es = new CultureInfo("es-ES");

        private static string Nombre(string nombreProveedor) => string.IsNullOrWhiteSpace(nombreProveedor) ? "el proveedor" : nombreProveedor.Trim();

        /// <summary>Al cliente no se le habla de «el proveedor»: sin nombre, «la financiera».</summary>
        private static string NombreAlCliente(string nombreProveedor) => string.IsNullOrWhiteSpace(nombreProveedor) ? "la financiera" : nombreProveedor.Trim();

        private static Dictionary<string, string> Datos(string tipo, SolicitudPagoAPlazos s, string nombreProveedor, string estado)
        {
            return new Dictionary<string, string>
            {
                ["tipo"] = tipo,
                ["empresa"] = s.Empresa?.Trim(),
                ["pedido"] = s.Pedido.ToString(CultureInfo.InvariantCulture),
                ["idSolicitud"] = s.Id.ToString(CultureInfo.InvariantCulture),
                ["cliente"] = s.Cliente?.Trim(),
                ["estado"] = estado,
                ["proveedor"] = nombreProveedor?.Trim(),
                ["importe"] = s.Importe.ToString("0.00", CultureInfo.InvariantCulture)
            };
        }

        /// <summary>La push al cliente. La app habla de tú.</summary>
        public static NotificacionPushDTO AlCliente(SolicitudPagoAPlazos s, string nombreProveedor)
        {
            return new NotificacionPushDTO
            {
                Titulo = "Tienes un pedido pendiente de pago",
                Cuerpo = $"Termina el pago a plazos de tu pedido {s.Pedido} ({ReglasPagoAPlazos.Euros(s.Importe)}) con {Nombre(nombreProveedor)}. Toca aquí para continuar.",
                Tipo = TIPO_CLIENTE,
                Datos = Datos(TIPO_CLIENTE, s, nombreProveedor, EstadosPagoAPlazos.PENDIENTE)
            };
        }

        /// <summary>La push al cliente cuando la financiera aprueba. De tú y sin datos del vendedor.</summary>
        public static NotificacionPushDTO AprobadoAlCliente(SolicitudPagoAPlazos s, string nombreProveedor)
        {
            return new NotificacionPushDTO
            {
                Titulo = "Pago a plazos aprobado",
                Cuerpo = $"Tu pago a plazos del pedido {s.Pedido} ({ReglasPagoAPlazos.Euros(s.Importe)}) con {NombreAlCliente(nombreProveedor)} " +
                    "está aprobado. Te avisaremos cuando salga.",
                Tipo = TIPO_APROBADO_CLIENTE,
                Datos = Datos(TIPO_APROBADO_CLIENTE, s, nombreProveedor, EstadosPagoAPlazos.APROBADO)
            };
        }

        /// <summary>
        /// La push al cliente cuando la financiera no aprueba (o rechaza lo que había aprobado: a ese cliente ya se le dijo que
        /// estaba aprobado). De tú.
        /// </summary>
        public static NotificacionPushDTO RechazadoAlCliente(SolicitudPagoAPlazos s, string nombreProveedor, bool despuesDeAprobar)
        {
            string financiera = NombreAlCliente(nombreProveedor);
            financiera = char.ToUpper(financiera[0], Es) + financiera.Substring(1);
            return new NotificacionPushDTO
            {
                Titulo = "Pago a plazos no aprobado",
                Cuerpo = (despuesDeAprobar
                        ? $"{financiera} ha anulado el pago a plazos del pedido {s.Pedido} que había aprobado."
                        : $"{financiera} no ha aprobado el pago a plazos del pedido {s.Pedido}.") +
                    " Habla con tu vendedor para ver otra forma de pago.",
                Tipo = TIPO_RECHAZADO_CLIENTE,
                Datos = Datos(TIPO_RECHAZADO_CLIENTE, s, nombreProveedor, EstadosPagoAPlazos.RECHAZADO)
            };
        }

        public static NotificacionPushDTO Aprobado(SolicitudPagoAPlazos s, string nombreProveedor, decimal faltaPorPagar, bool contraReembolso)
        {
            string limite = s.FechaLimiteCaptura.HasValue
                ? $" Hay que enviarlo antes del {s.FechaLimiteCaptura.Value.ToString("dd/MM/yyyy", Es)} o se pierde la financiación."
                : string.Empty;
            NotificacionPushDTO notificacion = new NotificacionPushDTO
            {
                Titulo = $"Pedido {s.Pedido}: pago a plazos aprobado",
                Cuerpo = $"{Nombre(nombreProveedor)} ha aprobado el pago a plazos del pedido {s.Pedido} (cliente {s.Cliente?.Trim()}, " +
                    $"{ReglasPagoAPlazos.Euros(s.Importe)}): ya tiene su prepago.{limite}" +
                    (faltaPorPagar > 0 ? $" Al pedido le quedan por pagar {ReglasPagoAPlazos.Euros(faltaPorPagar)}." : string.Empty) +
                    (contraReembolso ? " " + ReglasPagoAPlazos.AVISO_CONTRA_REEMBOLSO : string.Empty),
                Tipo = TIPO_APROBADO,
                Datos = Datos(TIPO_APROBADO, s, nombreProveedor, EstadosPagoAPlazos.APROBADO)
            };
            PonerLimite(notificacion, s, null);
            return notificacion;
        }

        public static NotificacionPushDTO Rechazado(SolicitudPagoAPlazos s, string nombreProveedor)
        {
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {s.Pedido}: pago a plazos rechazado",
                Cuerpo = $"{Nombre(nombreProveedor)} no ha aprobado el pago a plazos del pedido {s.Pedido} (cliente {s.Cliente?.Trim()}, " +
                    $"{ReglasPagoAPlazos.Euros(s.Importe)}). El pedido sigue sin pagar: hable con el cliente para cobrarlo de otra forma.",
                Tipo = TIPO_RECHAZADO,
                Datos = Datos(TIPO_RECHAZADO, s, nombreProveedor, EstadosPagoAPlazos.RECHAZADO)
            };
        }

        public static NotificacionPushDTO PlazoCerca(SolicitudPagoAPlazos s, string nombreProveedor, int dias)
        {
            string quedan = dias <= 0 ? "Hoy es el último día" : dias == 1 ? "Queda 1 día" : $"Quedan {dias} días";
            string limite = s.FechaLimiteCaptura.HasValue ? $" (hasta el {s.FechaLimiteCaptura.Value.ToString("dd/MM/yyyy", Es)})" : string.Empty;
            NotificacionPushDTO notificacion = new NotificacionPushDTO
            {
                Titulo = $"Pedido {s.Pedido}: {quedan.ToLower(Es)} para enviarlo",
                Cuerpo = $"{quedan} para enviar el pedido {s.Pedido} (cliente {s.Cliente?.Trim()}){limite} o se pierde la financiación de " +
                    $"{Nombre(nombreProveedor)} ({ReglasPagoAPlazos.Euros(s.Importe)}).",
                Tipo = TIPO_PLAZO_CERCA,
                Datos = Datos(TIPO_PLAZO_CERCA, s, nombreProveedor, EstadosPagoAPlazos.APROBADO)
            };
            PonerLimite(notificacion, s, dias);
            return notificacion;
        }

        public static NotificacionPushDTO Perdido(SolicitudPagoAPlazos s, string nombreProveedor, string motivo)
        {
            return new NotificacionPushDTO
            {
                Titulo = $"Pedido {s.Pedido}: pago a plazos sin efecto",
                Cuerpo = $"El pago a plazos del pedido {s.Pedido} (cliente {s.Cliente?.Trim()}, {ReglasPagoAPlazos.Euros(s.Importe)} con " +
                    $"{Nombre(nombreProveedor)}) se ha quedado sin efecto: {motivo?.Trim()}",
                Tipo = TIPO_PERDIDO,
                Datos = Datos(TIPO_PERDIDO, s, nombreProveedor, EstadosPagoAPlazos.Es(s.Estado, EstadosPagoAPlazos.CADUCADO)
                    ? EstadosPagoAPlazos.CADUCADO : EstadosPagoAPlazos.CANCELADO)
            };
        }

        private static void PonerLimite(NotificacionPushDTO notificacion, SolicitudPagoAPlazos s, int? dias)
        {
            if (s.FechaLimiteCaptura.HasValue)
            {
                notificacion.Datos["fechaLimite"] = s.FechaLimiteCaptura.Value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (dias.HasValue)
            {
                notificacion.Datos["dias"] = dias.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// A quién va un aviso del vendedor: los del aviso de faltas del picking (quien hizo el pedido y sus vendedores reales)
        /// y, además, quien pidió la financiación si es otra persona.
        /// </summary>
        public static DestinatariosAvisoPagoAPlazos Destinatarios(ComercialesPedido comerciales, string usuarioSolicitud)
        {
            DestinatariosAvisoFaltasPicking delPedido = AvisoFaltasPicking.Destinatarios(comerciales);
            DestinatariosAvisoPagoAPlazos destinatarios = new DestinatariosAvisoPagoAPlazos
            {
                BuzonNesto = (delPedido.BuzonNesto ?? new List<string>()).ToList(),
                VendedoresNestoApp = (delPedido.VendedoresNestoApp ?? new List<string>()).ToList()
            };
            if (!string.IsNullOrWhiteSpace(delPedido.UsuarioNestoApp))
            {
                destinatarios.UsuariosNestoApp.Add(delPedido.UsuarioNestoApp.Trim());
            }
            string usuario = usuarioSolicitud?.Trim();
            string sinDominio = ComercialesPedido.SinDominio(usuario);
            if (string.IsNullOrWhiteSpace(sinDominio))
            {
                return destinatarios;
            }
            bool yaEsta = destinatarios.UsuariosNestoApp.Contains(sinDominio, StringComparer.OrdinalIgnoreCase)
                || destinatarios.BuzonNesto.Any(u => string.Equals(ComercialesPedido.SinDominio(u), sinDominio, StringComparison.OrdinalIgnoreCase));
            if (yaEsta)
            {
                return destinatarios;
            }
            if (usuario.Contains("\\"))
            {
                destinatarios.BuzonNesto.Add(usuario);
            }
            destinatarios.UsuariosNestoApp.Add(sinDominio);
            return destinatarios;
        }
    }

    /// <summary>A quién va un aviso del vendedor.</summary>
    public class DestinatariosAvisoPagoAPlazos
    {
        /// <summary>Usuarios con dominio para el buzón (campana) de Nesto.</summary>
        public List<string> BuzonNesto { get; set; } = new List<string>();
        /// <summary>Usuarios sin dominio para su push de NestoApp (si tienen la app).</summary>
        public List<string> UsuariosNestoApp { get; set; } = new List<string>();
        /// <summary>Los vendedores reales del pedido que no son quien lo hizo: push de NestoApp.</summary>
        public List<string> VendedoresNestoApp { get; set; } = new List<string>();
    }

    public class AvisosPagoAPlazos : IAvisosPagoAPlazos
    {
        private readonly Func<IServicioNotificacionesPush> crearNotificaciones;
        private readonly Func<string, int, ComercialesPedido> leerComerciales;

        public AvisosPagoAPlazos() : this(() => new ServicioNotificacionesPush(), LeerComerciales)
        {
        }

        internal AvisosPagoAPlazos(Func<IServicioNotificacionesPush> crearNotificaciones, Func<string, int, ComercialesPedido> leerComerciales)
        {
            this.crearNotificaciones = crearNotificaciones;
            this.leerComerciales = leerComerciales;
        }

        private static ComercialesPedido LeerComerciales(string empresa, int pedido)
        {
            using (NVEntities db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return new LectorComercialesPedido(db).Leer(empresa, pedido);
            }
        }

        public Task<int> AlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor)
            => PushAlCliente(solicitud, s => TextosAvisosPagoAPlazos.AlCliente(s, nombreProveedor));

        public Task<int> AprobadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor)
            => PushAlCliente(solicitud, s => TextosAvisosPagoAPlazos.AprobadoAlCliente(s, nombreProveedor));

        public Task<int> RechazadoAlCliente(SolicitudPagoAPlazos solicitud, string nombreProveedor, bool despuesDeAprobar)
            => PushAlCliente(solicitud, s => TextosAvisosPagoAPlazos.RechazadoAlCliente(s, nombreProveedor, despuesDeAprobar));

        /// <summary>
        /// A los dispositivos del cliente en la app de clientes (y a su buzón). Sin dispositivo no se manda ni se guarda nada:
        /// devuelve 0.
        /// </summary>
        private async Task<int> PushAlCliente(SolicitudPagoAPlazos solicitud, Func<SolicitudPagoAPlazos, NotificacionPushDTO> texto)
        {
            if (solicitud == null || string.IsNullOrWhiteSpace(solicitud.Cliente))
            {
                return 0;
            }
            return await crearNotificaciones().EnviarACliente(solicitud.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                solicitud.Cliente.Trim(), texto(solicitud)).ConfigureAwait(false);
        }

        public Task Aprobado(SolicitudPagoAPlazos solicitud, string nombreProveedor, decimal faltaPorPagar, bool contraReembolso)
            => AlVendedor(solicitud, TextosAvisosPagoAPlazos.Aprobado(solicitud, nombreProveedor, faltaPorPagar, contraReembolso));

        public Task Rechazado(SolicitudPagoAPlazos solicitud, string nombreProveedor)
            => AlVendedor(solicitud, TextosAvisosPagoAPlazos.Rechazado(solicitud, nombreProveedor));

        public Task PlazoCerca(SolicitudPagoAPlazos solicitud, string nombreProveedor, int dias)
            => AlVendedor(solicitud, TextosAvisosPagoAPlazos.PlazoCerca(solicitud, nombreProveedor, dias));

        public Task Perdido(SolicitudPagoAPlazos solicitud, string nombreProveedor, string motivo)
            => AlVendedor(solicitud, TextosAvisosPagoAPlazos.Perdido(solicitud, nombreProveedor, motivo));

        /// <summary>Campana de Nesto y push de NestoApp (que también lo guarda en su buzón), como el aviso de faltas del picking.</summary>
        internal async Task AlVendedor(SolicitudPagoAPlazos solicitud, NotificacionPushDTO notificacion)
        {
            ComercialesPedido comerciales = leerComerciales(solicitud.Empresa?.Trim(), solicitud.Pedido);
            DestinatariosAvisoPagoAPlazos destinatarios = TextosAvisosPagoAPlazos.Destinatarios(comerciales, solicitud.Usuario);
            IServicioNotificacionesPush notificaciones = crearNotificaciones();
            foreach (string usuario in destinatarios.BuzonNesto)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            foreach (string usuario in destinatarios.UsuariosNestoApp.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _ = await notificaciones.EnviarAUsuario(usuario, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
            }
            foreach (string vendedor in destinatarios.VendedoresNestoApp)
            {
                _ = await notificaciones.EnviarAVendedor(solicitud.Empresa?.Trim(), vendedor, notificacion).ConfigureAwait(false);
            }
        }
    }
}
