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
        public int Bultos { get; set; }
        /// <summary>0 sin reembolso.</summary>
        public decimal Reembolso { get; set; }
        /// <summary>"Tramitado", "Incidentado" o "Entregado".</summary>
        public string Estado { get; set; }
        public string Observaciones { get; set; }
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
        /// <summary>"Efectivo", "Tarjeta" o "NoCobrado". Con reembolso y sin esto, «NoCobrado».</summary>
        public string CobroReembolso { get; set; }
        /// <summary>Lo cobrado; sin él, el reembolso del envío (si se ha cobrado).</summary>
        public decimal? ImporteCobrado { get; set; }
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
        public const string COBRO_TARJETA = "Tarjeta";
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
        public string Usuario { get; set; }
        public DateTime FechaRegistro { get; set; }
        public bool TieneFirma { get; set; }
    }
}
