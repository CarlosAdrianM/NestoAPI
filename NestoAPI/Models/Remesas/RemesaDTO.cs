using System;

namespace NestoAPI.Models.Remesas
{
    /// <summary>
    /// Remesa de cobros ligera para el grid de RemesasViewModel (Nesto#340, Fase 1C.14 slice 2).
    /// Nombres ASCII: el cliente bindea Numero directamente.
    /// </summary>
    public class RemesaDTO
    {
        public int Numero { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Importe { get; set; }
        public string Banco { get; set; }
    }

    /// <summary>
    /// Movimiento (efecto) incluido en una remesa de cobro (Nesto#340, Fase 1C.14 slice 3).
    /// DTO ligero con las 7 columnas que pinta el grid, en vez de la entidad ExtractoCliente
    /// entera (mismo criterio que en Alquileres).
    /// </summary>
    public class MovimientoRemesaDTO
    {
        public int Id { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public string Concepto { get; set; }
        public decimal Importe { get; set; }
        public string Ccc { get; set; }
        // Slice 5: el grid de detalle de impagados pinta también la fecha del efecto.
        public DateTime Fecha { get; set; }
    }

    /// <summary>
    /// Asiento de impagados agrupado para el grid izquierdo de la pestaña Impagados
    /// (Nesto#340, Fase 1C.14 slice 4). Cuenta = nº de movimientos del asiento.
    /// </summary>
    public class ImpagadoRemesaDTO
    {
        public int Asiento { get; set; }
        public DateTime Fecha { get; set; }
        public int Cuenta { get; set; }
    }

    /// <summary>
    /// NestoAPI#332: efecto candidato a entrar en la remesa (modo simulación / preselección).
    /// Preseleccionado = cumple todas las reglas; si no, Motivo explica por qué queda fuera
    /// o retenido. ClienteConNegativos = la puerta de revisión de neteo (#332: el usuario
    /// decide liquidar antes de remesar).
    /// </summary>
    public class EfectoCandidatoDTO
    {
        public int Id { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime? Vencimiento { get; set; }
        public decimal ImportePendiente { get; set; }
        public string Ccc { get; set; }
        public bool Preseleccionado { get; set; }
        public string Motivo { get; set; }
        public bool ClienteConNegativos { get; set; }
    }
}
