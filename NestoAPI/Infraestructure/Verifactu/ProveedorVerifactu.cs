using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// Verifactu #326 (petición de Carlos): el ÚNICO punto que sabe qué proveedor de Verifactu usamos
    /// (hoy Verifacti) y cómo se llaman sus claves de configuración. El resto del código (facturas,
    /// PDF, jobs) habla con <see cref="IServicioVerifactu"/> y con estas propiedades genéricas, así que
    /// cambiar a otro proveedor (Fiscaly, Holded…) es tocar solo esta clase.
    /// </summary>
    public static class ProveedorVerifactu
    {
        // Compartido: una sola instancia (y un solo HttpClient) para toda la API.
        private static readonly Lazy<IServicioVerifactu> actual =
            new Lazy<IServicioVerifactu>(() => new Verifacti.ServicioVerifacti());

        /// <summary>El servicio Verifactu del proveedor en uso.</summary>
        public static IServicioVerifactu Actual => actual.Value;

        /// <summary>Clave (de Verifacti) del interruptor de impresión del QR en las facturas PDF.</summary>
        internal const string CLAVE_MOSTRAR_QR_EN_PDF = "Verifacti:MostrarQrEnPdf";

        /// <summary>
        /// Verifactu #35: ¿se imprime el QR tributario en los PDF? Independiente de que el envío esté
        /// habilitado: en la fase en sombra se envía al sandbox pero el QR de preproducción no se imprime.
        /// </summary>
        public static bool MostrarQrEnPdf => LeerBooleano(ConfigurationManager.AppSettings[CLAVE_MOSTRAR_QR_EN_PDF]);

        internal static bool LeerBooleano(string valor) => bool.TryParse(valor?.Trim(), out bool resultado) && resultado;
    }
}
