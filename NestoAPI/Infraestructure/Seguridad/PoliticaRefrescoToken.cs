using System;
using System.Configuration;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// NestoAPI#427 (puntos 3 y 4): caducidad ABSOLUTA de las sesiones que se renuevan por
    /// <c>api/auth/refreshToken</c> (TiendasNuevaVision) y <c>api/auth/refreshOAuthToken</c>
    /// (NestoApp antigua, antes del refresh_token de #188).
    ///
    /// <para>Antes cada refresco emitía un token nuevo con <c>exp = ahora + 1 h</c> y el reloj
    /// volvía a cero: con una llamada al mes (o cada dos años en el de OAuth) la sesión era
    /// perpetua. Ahora el token lleva el claim <see cref="CLAIM_LOGIN_UTC"/> con la fecha del
    /// login ORIGINAL (segundos Unix, UTC), se copia tal cual en cada refresco y, pasados
    /// <see cref="DiasMaximos"/> días desde él, el refresco se rechaza: toca volver a entrar
    /// (en TNV, pedir el código por correo).</para>
    ///
    /// <para>⚠️ <b>Transición (compatibilidad con la app instalada).</b> Los tokens emitidos
    /// antes de este cambio NO llevan <c>login_utc</c>. Rechazarlos echaría de golpe a todos los
    /// clientes que hoy tienen la app abierta. Por eso un token auténtico SIN <c>login_utc</c> se
    /// acepta y el token nuevo sale con <c>login_utc = ahora</c>: el reloj de la caducidad
    /// absoluta empieza a contar en su primer refresco tras el despliegue. No es una puerta
    /// abierta: ese token viejo sigue sujeto a la firma y a la ventana de caducidad de cada
    /// endpoint (un mes en refreshToken, 30 días en refreshOAuthToken), así que deja de valer
    /// solo, y los que se emiten desde hoy ya llevan el claim. Lo fija el test
    /// <c>TokenSinLoginUtc_PasaYSaleConLoginUtcAhora_TransicionTNV</c>.</para>
    /// </summary>
    internal static class PoliticaRefrescoToken
    {
        internal const string CLAIM_LOGIN_UTC = "login_utc";

        internal const string SETTING_DIAS_TIENDA = "Auth:DiasMaximosSesionTienda";
        internal const string SETTING_DIAS_OAUTH = "Auth:DiasMaximosSesionOAuth";
        internal const int DIAS_TIENDA_POR_DEFECTO = 90;
        internal const int DIAS_OAUTH_POR_DEFECTO = 30;

        /// <summary>
        /// Margen para relojes: un login_utc «en el futuro» de hasta 5 minutos se admite (el token
        /// lo firmamos nosotros, pero el servidor puede cambiar de hora). Más allá, no.
        /// </summary>
        private static readonly TimeSpan MARGEN_RELOJ = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Días máximos desde el login original para la setting dada. Si falta, no es un número o
        /// no es positiva, el valor por defecto (nunca «sin límite»).
        /// </summary>
        internal static int DiasMaximos(string nombreSetting, int porDefecto)
        {
            string valor = ConfigurationManager.AppSettings[nombreSetting];
            return int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias) && dias > 0
                ? dias
                : porDefecto;
        }

        /// <summary>
        /// La fecha de login que debe llevar el token renovado, o null si el refresco se rechaza
        /// porque la sesión ya ha superado la caducidad absoluta (o el claim es ilegible).
        /// Sin claim → <paramref name="ahoraUtc"/> (transición: ver el comentario de la clase).
        /// </summary>
        internal static DateTime? FechaLoginParaRefresco(JwtSecurityToken token, DateTime ahoraUtc, int diasMaximos)
        {
            if (token == null)
            {
                return null;
            }

            string valor = token.Claims.FirstOrDefault(c => c.Type == CLAIM_LOGIN_UTC)?.Value;
            if (valor == null)
            {
                // ⚠️ Transición: token de antes de #427. Pasa UNA vez más y sale con el claim.
                return ahoraUtc;
            }

            DateTime? login = Leer(valor);
            if (login == null)
            {
                return null;
            }

            if (login.Value > ahoraUtc + MARGEN_RELOJ)
            {
                return null;
            }

            if (ahoraUtc - login.Value > TimeSpan.FromDays(diasMaximos))
            {
                return null;
            }

            return login.Value;
        }

        /// <summary>El valor del claim: segundos Unix en texto invariante.</summary>
        internal static string Escribir(DateTime loginUtc)
        {
            DateTime utc = loginUtc.Kind == DateTimeKind.Utc ? loginUtc : DateTime.SpecifyKind(loginUtc, DateTimeKind.Utc);
            return new DateTimeOffset(utc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        internal static DateTime? Leer(string valor)
        {
            if (!long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out long segundos))
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(segundos).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
    }
}
