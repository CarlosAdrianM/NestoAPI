using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#178: cómo se cobra con una tarjeta guardada.
    ///
    /// <para><b>Directo (MIT)</b>: el servidor cobra por REST con el token, sin autenticar al
    /// cliente (COF_INI=N, DIRECTPAYMENT=true, EXCEP_SCA=MIT). Comercia lo habilitó el 02/09/26,
    /// pero <b>NO se debe usar para pedidos que hace el propio cliente</b>: si el titular está
    /// presente y es él quien dispara el pago, la operación es CIT sobre credencial en fichero,
    /// no MIT (EBA Q&amp;A 2018_4031 y reglas de Visa/MC sobre stored credentials). Marcarla como
    /// MIT la clasifica mal y renuncia al traslado de responsabilidad al emisor: el fraude lo
    /// asumiríamos nosotros. La exención MIT queda reservada para la cartera de cobros
    /// aplazados/periódicos de #181, donde el cobro sí lo inicia el comercio sin el cliente.</para>
    ///
    /// <para><b>Por redirección (el que está activo, y el correcto para pedidos del cliente)</b>:
    /// el cliente pasa por la pasarela con DS_MERCHANT_IDENTIFIER = su token, así que Redsys le
    /// enseña la tarjeta guardada y solo le pide la autenticación del banco, sin volver a
    /// teclearla. Es un CIT autenticado por 3DS, con traslado de responsabilidad. La pasarela se
    /// abre dentro de la app (PagoRedsysWebView), así que el cliente no sale de ella. El cobro
    /// llega por la notificación de siempre y entra como prepago del pedido.</para>
    ///
    /// <para>Para llegar a la experiencia "Amazon" (frictionless sin ver pasarela) el camino NO
    /// es este flag, sino EMV 3DS 2 por REST (iniciaPeticionREST + trataPeticionREST): el reto
    /// solo aparece cuando lo exige el emisor, y se conserva el traslado de responsabilidad.</para>
    /// </summary>
    public static class ModoCobroTarjetaGuardada
    {
        public const string CLAVE_APPSETTING = "Redsys:CobroTarjetaGuardadaDirecto";

        public static bool EsCobroDirecto => Leer(ConfigurationManager.AppSettings[CLAVE_APPSETTING]);

        internal static bool Leer(string valor)
        {
            return !string.IsNullOrWhiteSpace(valor)
                && string.Equals(valor.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
