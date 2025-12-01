using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// Interfaz genérica para servicios de Verifactu.
    /// Permite abstraer el proveedor concreto (Verifacti, otro futuro, etc.)
    /// </summary>
    public interface IServicioVerifactu
    {
        /// <summary>
        /// Indica si el servicio está habilitado y configurado correctamente.
        /// Si es false, no se deben enviar facturas.
        /// </summary>
        bool EstaHabilitado { get; }

        /// <summary>
        /// Indica si estamos en modo sandbox (pruebas) o producción.
        /// </summary>
        bool EsSandbox { get; }

        /// <summary>
        /// Nombre del proveedor (ej: "Verifacti", "OtroProveedor")
        /// </summary>
        string NombreProveedor { get; }

        /// <summary>
        /// Envía una factura al sistema Verifactu.
        /// </summary>
        /// <param name="factura">Datos de la factura a enviar</param>
        /// <returns>Respuesta con UUID, QR, huella, etc.</returns>
        Task<VerifactuResponse> EnviarFacturaAsync(VerifactuFacturaRequest factura);

        /// <summary>
        /// Consulta el estado de una factura enviada previamente.
        /// </summary>
        /// <param name="uuid">UUID de la factura</param>
        /// <returns>Estado actual de la factura</returns>
        Task<VerifactuResponse> ConsultarEstadoAsync(string uuid);

        /// <summary>
        /// Envía un registro de anulación a la AEAT para una factura.
        /// Una vez anulada, no se puede crear otra factura con la misma serie, número y fecha.
        /// Casos de uso: factura rechazada por AEAT, factura que no llegó a registrarse, o anulación real.
        /// Nota: Lo normal para corregir facturas es usar rectificativas (R1, R3, R4), no anulación.
        /// </summary>
        /// <param name="serie">Serie de la factura</param>
        /// <param name="numero">Número de la factura</param>
        /// <param name="fechaExpedicion">Fecha de expedición de la factura</param>
        /// <param name="rechazoPrevio">True si la factura fue rechazada previamente por AEAT</param>
        /// <param name="sinRegistroPrevio">True si la factura no existe en AEAT</param>
        /// <returns>Respuesta de la anulación</returns>
        Task<VerifactuResponse> AnularFacturaAsync(string serie, string numero, System.DateTime fechaExpedicion,
            bool rechazoPrevio = false, bool sinRegistroPrevio = false);
    }
}
