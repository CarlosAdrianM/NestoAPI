using System;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#522: una factura que Verifactu todavía no da por buena, para la ventana de administración
    /// de Nesto: sin registrar (pendiente, rechazada por Verifacti, incidencia técnica, sin datos fiscales)
    /// o registrada pero marcada por la AEAT como incorrecta o rechazada.
    /// </summary>
    public class FacturaPendienteVerifactuDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        public string Serie { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        /// <summary>En qué situación está, en palabras de administración (ver ServicioFacturasPendientesVerifactu).</summary>
        public string Situacion { get; set; }
        /// <summary>El estado tal y como lo devuelve Verifacti (Incorrecto, Rechazado...), o null si no llegó.</summary>
        public string Estado { get; set; }
        /// <summary>Por qué no está registrada: el último error de Verifacti o el motivo de la AEAT.</summary>
        public string Motivo { get; set; }
        /// <summary>Qué hay que hacer para arreglarla.</summary>
        public string QueHacer { get; set; }
        public DateTime? UltimoIntento { get; set; }
        /// <summary>Si tiene sentido pulsar «Reintentar» (no, si no hay camino para declararla desde aquí).</summary>
        public bool PuedeReintentar { get; set; }
        /// <summary>NestoAPI#392: está marcada para declararse como simplificada (F2; si es rectificativa, R5).</summary>
        public bool DeclararSimplificada { get; set; }
        /// <summary>
        /// NestoAPI#392: si tiene sentido ofrecer «Declarar como simplificada»: el problema es el NIF del
        /// destinatario y no es rectificativa ni está ya registrada o marcada. El límite de importe lo valida el endpoint.
        /// </summary>
        public bool PuedeDeclararSimplificada { get; set; }
    }

    /// <summary>NestoAPI#392: cuerpo de POST api/Verifactu/DeclararSimplificada.</summary>
    public class DeclararSimplificadaVerifactuDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
        /// <summary>Obligatorio: por qué no se puede conseguir el NIF (queda en la tabla Modificaciones).</summary>
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#522: resultado de reintentar el envío a Verifactu desde la ventana de administración.</summary>
    public class ResultadoReintentoVerifactuDTO
    {
        public bool Exitoso { get; set; }
        public string Mensaje { get; set; }
        /// <summary>La factura tal y como queda si sigue pendiente; null si ya no lo está.</summary>
        public FacturaPendienteVerifactuDTO Factura { get; set; }
    }

    /// <summary>NestoAPI#522: cuerpo de POST api/Verifactu/ReintentarFactura.</summary>
    public class ReintentarFacturaVerifactuDTO
    {
        public string Empresa { get; set; }
        public string Numero { get; set; }
    }
}
