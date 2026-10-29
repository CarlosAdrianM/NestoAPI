using System;

namespace NestoAPI.Models.Agencias
{
    // NestoAPI#739: el contrato de api/RepartoPropio (comentario de la issue del 29/10/26). JSON en PascalCase.

    /// <summary>Un envío del reparto propio, como lo ve el repartidor en Ariadna.</summary>
    public class EnvioRepartoDTO
    {
        public const string ESTADO_TRAMITADO = "Tramitado";
        public const string ESTADO_INCIDENTADO = "Incidentado";
        public const string ESTADO_ENTREGADO = "Entregado";

        public int EnvioId { get; set; }
        public string Empresa { get; set; }
        public int? Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        /// <summary>
        /// El correo del cliente del envío (EnviosAgencia.Email, el del contacto del pedido): Ariadna lo propone para el cobro por
        /// enlace. Null si no hay o no es válido.
        /// </summary>
        public string Correo { get; set; }
        public int Bultos { get; set; }
        /// <summary>0 sin reembolso.</summary>
        public decimal Reembolso { get; set; }
        /// <summary>"Tramitado", "Incidentado" o "Entregado".</summary>
        public string Estado { get; set; }
        public string Observaciones { get; set; }
        /// <summary>
        /// Ariadna#63: quién lo lleva (el último que cargó un bulto suyo en la furgoneta), sin dominio. Null si nadie ha cargado
        /// nada (o sin Scripts/Issue739_CargaFurgoneta.sql).
        /// </summary>
        public string Repartidor { get; set; }
        /// <summary>Ariadna#63: bultos distintos que ha cargado el repartidor (0 sin repartidor).</summary>
        public int BultosCargados { get; set; }
    }

    /// <summary>
    /// Ariadna#63: POST api/RepartoPropio/Cargar. El repartidor escanea al cargar cada bulto que se lleva. Idempotente por
    /// ClaveIdempotencia (la cola de Ariadna reintenta) y por bulto (el mismo bulto otra vez no da error).
    /// </summary>
    public class CargaBultoRepartoDTO
    {
        /// <summary>«RP{EnvioId}-{N}», el de la etiqueta del bulto.</summary>
        public string Codigo { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>La hora de la PDA. Null: la del servidor.</summary>
        public DateTime? FechaHora { get; set; }
        /// <summary>El envío lo lleva otro repartidor y este confirma que se lo lleva él (queda la traza en EnviosHistoria).</summary>
        public bool Reasignar { get; set; }
    }

    /// <summary>Ariadna#63: la respuesta de POST api/RepartoPropio/Cargar.</summary>
    public class ResultadoCargaBultoDTO
    {
        public int EnvioId { get; set; }
        public int Bulto { get; set; }
        public int Bultos { get; set; }
        /// <summary>Bultos distintos del envío cargados por este usuario.</summary>
        public int BultosCargados { get; set; }
        /// <summary>Quién lo lleva (este usuario), sin dominio.</summary>
        public string Repartidor { get; set; }
        /// <summary>El destinatario del envío.</summary>
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
    }

    /// <summary>Ariadna#63: GET api/RepartoPropio/SinCargar, un envío tramitado de ese día con algún bulto sin cargar.</summary>
    public class EnvioSinCargarDTO
    {
        public int EnvioId { get; set; }
        public int? Pedido { get; set; }
        public string Nombre { get; set; }
        public string Poblacion { get; set; }
        public int Bultos { get; set; }
        public int BultosCargados { get; set; }
        /// <summary>Null si no lo ha cargado nadie.</summary>
        public string Repartidor { get; set; }
    }

    /// <summary>GET api/RepartoPropio/Bulto/{codigo}: el envío y el bulto escaneado.</summary>
    public class EnvioRepartoBultoDTO : EnvioRepartoDTO
    {
        public int Bulto { get; set; }
    }

    /// <summary>
    /// POST api/RepartoPropio/Envios/{envioId}/Entregar. Carlos 29/10/26: de momento solo es obligatoria la clave (registrar la
    /// entrega es opcional y no debe frenar el reparto); más adelante se exigirán receptor y firma o DNI.
    /// </summary>
    public class EntregaRepartoDTO
    {
        public Guid ClaveIdempotencia { get; set; }
        /// <summary>Hora local de la entrega (la de la PDA, aunque llegue más tarde por falta de cobertura). Sin ella, ahora.</summary>
        public DateTime? FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        /// <summary>PNG en base64.</summary>
        public string FirmaPng { get; set; }
        /// <summary>
        /// "Efectivo", "Enlace" o "NoCobrado". Con reembolso y sin esto, «NoCobrado». El repartidor no lleva datáfono: "Tarjeta"
        /// da 400. Efectivo se contabiliza al entregar en la caja del repartidor (su CajaDefecto); Enlace manda un enlace de pago
        /// a <see cref="CorreoEnlace"/> y no contabiliza nada (el cobro entra por la notificación de Redsys).
        /// </summary>
        public string CobroReembolso { get; set; }
        /// <summary>Lo cobrado (o el importe del enlace); sin él, el reembolso del envío.</summary>
        public decimal? ImporteCobrado { get; set; }
        /// <summary>Obligatorio y con formato válido con CobroReembolso = "Enlace": a dónde se manda el enlace de pago.</summary>
        public string CorreoEnlace { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string Observaciones { get; set; }
    }

    /// <summary>POST api/RepartoPropio/Envios/{envioId}/Incidencia.</summary>
    public class IncidenciaRepartoDTO
    {
        public const string AUSENTE = "Ausente";
        public const string DIRECCION_INCORRECTA = "DireccionIncorrecta";
        public const string RECHAZADO = "Rechazado";
        public const string FALTAN_BULTOS = "FaltanBultos";
        public const string OTRO = "Otro";

        public Guid ClaveIdempotencia { get; set; }
        public DateTime? FechaHora { get; set; }
        /// <summary>"Ausente", "DireccionIncorrecta", "Rechazado", "FaltanBultos" u "Otro" (este exige <see cref="Texto"/>).</summary>
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>Respuesta de Entregar e Incidencia.</summary>
    public class EstadoEnvioRepartoDTO
    {
        public int EnvioId { get; set; }
        public string Estado { get; set; }
        /// <summary>La página del enlace de pago (CobroReembolso = "Enlace"); null si no hay enlace.</summary>
        public string UrlPago { get; set; }
    }

    /// <summary>
    /// GET api/RepartoPropio/MisCobros?fecha=yyyy-MM-dd: el extracto contable de ese día de la caja del usuario (su CajaDefecto),
    /// el mismo que el de la ventana de Cajas de Nesto (api/Informes/ExtractoContable).
    /// </summary>
    public class MisCobrosRepartoDTO
    {
        public string Cuenta { get; set; }
        public DateTime Fecha { get; set; }
        public decimal SaldoAnterior { get; set; }
        public System.Collections.Generic.List<ApunteCajaRepartoDTO> Apuntes { get; set; } = new System.Collections.Generic.List<ApunteCajaRepartoDTO>();
        public decimal TotalDia { get; set; }
        public decimal SaldoFinal { get; set; }
    }

    public class ApunteCajaRepartoDTO
    {
        public DateTime Fecha { get; set; }
        public string Concepto { get; set; }
        /// <summary>Debe − Haber de la cuenta de caja: positivo = entra en caja.</summary>
        public decimal Importe { get; set; }
        public int? Asiento { get; set; }
        public string Documento { get; set; }
    }

    /// <summary>GET api/RepartoPropio/Envios/{envioId}/PruebaEntrega (la firma va aparte, en .../Firma).</summary>
    public class PruebaEntregaDTO
    {
        public int EnvioId { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        /// <summary>La caja en la que se contabilizó el efectivo (null si no se contabilizó).</summary>
        public string CuentaCaja { get; set; }
        /// <summary>El enlace de pago que se mandó (cobro por enlace), o null.</summary>
        public string UrlPago { get; set; }
        public string Usuario { get; set; }
        public bool TieneFirma { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
    }

    /// <summary>Una fila de EnviosAgenciaPruebasEntrega (entrega o incidencia), sin la firma.</summary>
    public class PruebaEntregaReparto
    {
        public const string TIPO_ENTREGA = "Entrega";
        public const string TIPO_INCIDENCIA = "Incidencia";
        public const string COBRO_EFECTIVO = "Efectivo";
        /// <summary>Ya no se admite (el repartidor no lleva datáfono, Carlos 29/10/26): da 400.</summary>
        public const string COBRO_TARJETA = "Tarjeta";
        public const string COBRO_ENLACE = "Enlace";
        public const string COBRO_NO_COBRADO = "NoCobrado";

        public int Id { get; set; }
        public int EnvioId { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public string Tipo { get; set; }
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public string CobroReembolso { get; set; }
        public decimal? ImporteCobrado { get; set; }
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public int? Asiento { get; set; }
        /// <summary>
        /// NestoAPI#739: la cuenta de caja contra la que se contabilizó el reembolso cobrado: la CajaDefecto del usuario que
        /// entrega (cada repartidor en su caja). Null si no se contabilizó nada.
        /// </summary>
        public string CuentaCaja { get; set; }
        /// <summary>Cobro por enlace: el PagoTPV del enlace y su página (para devolver lo mismo si Ariadna reintenta).</summary>
        public int? PagoTPVId { get; set; }
        public string UrlPago { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaRegistro { get; set; }
        public bool TieneFirma { get; set; }
    }
}
