using System;

namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// Nesto#261: una fila de la consulta de auditoría de enlaces de pago (NestoPago / TPV virtual).
    /// Quién lo creó, cuándo, para qué cliente, por cuánto y adónde se envió.
    /// </summary>
    public class PagoTPVAuditoriaDTO
    {
        public int Id { get; set; }
        public string NumeroOrden { get; set; }
        public string Tipo { get; set; }
        public string Estado { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        public decimal Importe { get; set; }
        public string Descripcion { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaActualizacion { get; set; }
        /// <summary>Correo al que se envió el enlace (null si no se envió por correo).</summary>
        public string Correo { get; set; }
        /// <summary>Móvil al que se envió por SMS (null si no se envió por SMS).</summary>
        public string Movil { get; set; }
        public string CodigoRespuesta { get; set; }
        public string CodigoAutorizacion { get; set; }
        public string MetodoPago { get; set; }
        public int NumeroEfectos { get; set; }
        /// <summary>Documentos de los efectos que cubre el enlace, separados por comas.</summary>
        public string Documentos { get; set; }
    }

    /// <summary>
    /// Nesto#261: filtros de la consulta de auditoría. Todos opcionales; se combinan con Y.
    /// Si se indica <see cref="NumeroOrden"/> (el identificador del enlace, p. ej. B9BC22C32366),
    /// se ignoran las fechas: el identificador es único y el enlace puede ser antiguo.
    /// </summary>
    public class FiltroAuditoriaPagosTPV
    {
        public const int LIMITE_POR_DEFECTO = 500;
        public const int LIMITE_MAXIMO = 2000;

        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Cliente { get; set; }
        /// <summary>Contiene (el usuario se guarda unas veces con dominio y otras sin él).</summary>
        public string Usuario { get; set; }
        public string Estado { get; set; }
        public string NumeroOrden { get; set; }
        public int? Limite { get; set; }
    }
}
