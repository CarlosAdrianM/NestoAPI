using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Pagos
{
    /// <summary>
    /// NestoAPI#178: cómo se cobra con una tarjeta guardada.
    ///
    /// <para><b>Directo (MIT)</b>: el servidor cobra por REST con el token, sin que el cliente vea
    /// la pasarela (COF_INI=N, EXCEP_SCA=MIT). Es el objetivo, pero el 02/09/26 el terminal 1 de
    /// 329515704 devolvió SIS0883 ("no se puede marcar la exención MIT") con tres tarjetas
    /// distintas: la exención MIT no está habilitada en el terminal y se ha pedido a Comercia.</para>
    ///
    /// <para><b>Por redirección (plan B, el que está activo)</b>: el pedido se crea y el cliente
    /// pasa por la pasarela con DS_MERCHANT_IDENTIFIER = su token, así que Redsys le enseña la
    /// tarjeta guardada y solo le pide la autenticación del banco, sin volver a teclearla. El
    /// cobro llega por la notificación de siempre y entra como prepago del pedido.</para>
    ///
    /// <para>El día que Comercia active MIT: poner <c>Redsys:CobroTarjetaGuardadaDirecto</c> a
    /// <c>true</c> en el appSettings del Web.config. Nada más.</para>
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
