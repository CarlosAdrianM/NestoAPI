using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Pagos.PagoAPlazos
{
    // NestoAPI#864 (requisito de Carlos, 13/11/26): TODO lo del pago a plazos de un pedido va detrás de este contrato, sin
    // atarse a ninguna financiera. El núcleo (ServicioPagoAPlazos: pedido, prepago, estados, canal, avisos y plazos) solo
    // habla con IProveedorPagoAPlazos; Aplázame es la primera implementación (Pagos/Aplazame/ProveedorPagoAPlazosAplazame).
    // Para cambiar de financiera basta con otra clase que implemente la interfaz, darla de alta en
    // RegistroProveedoresPagoAPlazos y poner su código en el parámetro PagoAPlazosProveedor de «(defecto)».

    /// <summary>Por dónde termina el cliente la financiación.</summary>
    public static class CanalesPagoAPlazos
    {
        /// <summary>En la app de clientes, a la que llega con una push.</summary>
        public const string APP = "App";
        /// <summary>Con un enlace que le manda quien pasó el pedido.</summary>
        public const string ENLACE = "Enlace";

        /// <summary>El canal tal y como se escribe, o null si no es ninguno de los dos.</summary>
        public static string Normalizar(string canal)
        {
            string limpio = canal?.Trim();
            if (string.Equals(limpio, APP, StringComparison.OrdinalIgnoreCase))
            {
                return APP;
            }
            return string.Equals(limpio, ENLACE, StringComparison.OrdinalIgnoreCase) ? ENLACE : null;
        }
    }

    /// <summary>
    /// Lo que el proveedor dice de sí mismo: si se puede usar ahora, sus límites, su plazo para confirmar el cobro y cómo
    /// se apunta su dinero en el pedido. Son DATOS del proveedor: el núcleo no los sabe de antemano.
    /// </summary>
    public class CondicionesProveedorPagoAPlazos
    {
        /// <summary>Tiene lo que necesita para trabajar (claves…). False: no se ofrece.</summary>
        public bool Operativo { get; set; }
        /// <summary>Importe mínimo con IVA que financia. Null: sin mínimo.</summary>
        public decimal? ImporteMinimo { get; set; }
        /// <summary>Importe máximo con IVA que financia. Null: sin máximo.</summary>
        public decimal? ImporteMaximo { get; set; }
        /// <summary>Días, desde que aprueba, para confirmar el cobro (al enviar el pedido). Pasados, se pierde la financiación.</summary>
        public int DiasPlazoCaptura { get; set; }
        /// <summary>Cuenta del Prepago que se apunta al pedido cuando aprueba.</summary>
        public string CuentaPrepago { get; set; }
        /// <summary>Lo que va delante del pedido en el concepto del Prepago (50 caracteres con el pedido y la referencia).</summary>
        public string PrefijoConceptoPrepago { get; set; }
    }

    /// <summary>Una línea del pedido que se financia, ya leída.</summary>
    public class LineaPagoAPlazos
    {
        /// <summary>Línea de producto; si no, portes u otro cargo.</summary>
        public bool EsProducto { get; set; }
        public string Producto { get; set; }
        public string Texto { get; set; }
        public int Cantidad { get; set; }
        public decimal BaseImponible { get; set; }
        /// <summary>IVA + recargo de equivalencia, en tanto por uno (0,21).</summary>
        public decimal Impuestos { get; set; }
        public decimal Total { get; set; }
        public short Estado { get; set; }
        public DateTime FechaEntrega { get; set; }
    }

    public class DireccionPagoAPlazos
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Pais { get; set; }
        public string Telefono { get; set; }
    }

    /// <summary>Lo que el núcleo le da al proveedor para abrir la financiación de un pedido.</summary>
    public class PeticionSolicitudProveedor
    {
        /// <summary>Nuestra referencia, única por solicitud (la pone el núcleo): con ella nos notifica, se captura y se cancela.</summary>
        public string Referencia { get; set; }
        /// <summary>Contra el entorno de pruebas del proveedor (sin dinero).</summary>
        public bool Pruebas { get; set; }
        /// <summary><see cref="CanalesPagoAPlazos"/>.</summary>
        public string Canal { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Correo { get; set; }
        /// <summary>Lo que se financia, con IVA.</summary>
        public decimal Importe { get; set; }
        public List<LineaPagoAPlazos> Lineas { get; set; } = new List<LineaPagoAPlazos>();
        public DireccionPagoAPlazos Facturacion { get; set; }
        public DireccionPagoAPlazos Envio { get; set; }
        public bool RecogidaEnTienda { get; set; }
        /// <summary>A dónde nos tiene que notificar (la monta el núcleo con el código del proveedor).</summary>
        public string UrlNotificacion { get; set; }
    }

    /// <summary>Lo que hace falta para hablar con el proveedor de una solicitud que ya existe.</summary>
    public class ReferenciaSolicitudProveedor
    {
        public string Referencia { get; set; }
        /// <summary>El identificador que nos dio el proveedor al crearla.</summary>
        public string ReferenciaExterna { get; set; }
        public bool Pruebas { get; set; }
        public decimal Importe { get; set; }
    }

    public enum TipoNotificacionPagoAPlazos
    {
        /// <summary>No viene de quien dice venir: no se toca nada.</summary>
        NoAutorizada,
        /// <summary>No se entiende, o le falta la referencia.</summary>
        Ilegible,
        /// <summary>El proveedor ha aprobado la financiación y nos pide confirmarla: aceptar o rechazar.</summary>
        PideConfirmacion,
        /// <summary>Estado final: aprobada.</summary>
        Aprobada,
        /// <summary>Estado final: rechazada.</summary>
        Rechazada,
        /// <summary>Un paso intermedio (validación de identidad…): nada que hacer todavía.</summary>
        Intermedia
    }

    /// <summary>La notificación del proveedor, ya validada y traducida.</summary>
    public class NotificacionPagoAPlazos
    {
        public TipoNotificacionPagoAPlazos Tipo { get; set; }
        public string Referencia { get; set; }
        /// <summary>El importe que dice el proveedor, en euros. Null si no lo trae.</summary>
        public decimal? Importe { get; set; }
        /// <summary>Si viene del entorno de pruebas. Null si no lo dice.</summary>
        public bool? Pruebas { get; set; }
        /// <summary>El motivo que da el proveedor (para el ELMAH y los avisos).</summary>
        public string Motivo { get; set; }
    }

    /// <summary>Lo que el núcleo decide con una notificación; el proveedor lo traduce a su respuesta HTTP.</summary>
    public enum DecisionNotificacionPagoAPlazos
    {
        /// <summary>Recibida y conforme (confirmar la financiación, o enterado).</summary>
        Aceptar,
        /// <summary>No se confirma la financiación.</summary>
        Rechazar,
        NoAutorizada,
        Ilegible,
        /// <summary>No conocemos esa referencia.</summary>
        Desconocida,
        /// <summary>No se ha podido procesar ahora: que el proveedor lo reintente.</summary>
        Reintentar
    }

    public class RespuestaNotificacionProveedor
    {
        public int CodigoHttp { get; set; }
        /// <summary>Null: sin cuerpo.</summary>
        public string Cuerpo { get; set; }
        public string TipoContenido { get; set; } = "application/json";
    }

    /// <summary>Lo que el proveedor tiene cobrado (capturado) de una solicitud.</summary>
    public class EstadoCapturaProveedor
    {
        public decimal Capturado { get; set; }
        /// <summary>Lo que queda por capturar. Null si el proveedor no lo dice.</summary>
        public decimal? PendienteDeCapturar { get; set; }
        public DateTime? FechaUltimaCaptura { get; set; }
    }

    /// <summary>
    /// NestoAPI#864: un proveedor de pago a plazos (una financiera). Nombres neutros a propósito: nada de aquí es de Aplázame.
    /// </summary>
    public interface IProveedorPagoAPlazos
    {
        /// <summary>Su código en el registro, en el parámetro PagoAPlazosProveedor y en la tabla (sin tildes ni espacios).</summary>
        string Codigo { get; }

        /// <summary>Cómo se le nombra al usuario («Aplázame»).</summary>
        string Nombre { get; }

        /// <summary>Disponibilidad, límites, plazo de captura y cuenta del prepago.</summary>
        CondicionesProveedorPagoAPlazos Condiciones();

        /// <summary>Abre la financiación de un pedido. Devuelve el identificador que le da el proveedor.</summary>
        Task<string> CrearSolicitud(PeticionSolicitudProveedor peticion);

        /// <summary>
        /// La dirección que tiene que abrir el cliente para terminar la financiación, según el canal: en la app (vuelve a la
        /// app al acabar) o en el navegador (enlace).
        /// </summary>
        string UrlPago(ReferenciaSolicitudProveedor solicitud, string canal);

        /// <summary>Valida que la notificación es suya y la traduce. Nunca lanza: lo que no vale es NoAutorizada o Ilegible.</summary>
        NotificacionPagoAPlazos InterpretarNotificacion(string cabeceraAutorizacion, string cuerpo);

        /// <summary>Cómo se le contesta la decisión del núcleo.</summary>
        RespuestaNotificacionProveedor ResponderNotificacion(DecisionNotificacionPagoAPlazos decision);

        /// <summary>Lo que ya está cobrado de la solicitud (para no capturar dos veces).</summary>
        Task<EstadoCapturaProveedor> ConsultarCaptura(ReferenciaSolicitudProveedor solicitud);

        /// <summary>Confirma el cobro (captura) de ese importe: el pedido ha salido.</summary>
        Task Capturar(ReferenciaSolicitudProveedor solicitud, decimal importe);

        /// <summary>Cancela una financiación aprobada y sin capturar: al cliente no se le cobra nada.</summary>
        Task Cancelar(ReferenciaSolicitudProveedor solicitud);

        /// <summary>Devuelve al cliente ese importe de una financiación ya capturada.</summary>
        Task Reembolsar(ReferenciaSolicitudProveedor solicitud, decimal importe);
    }
}
