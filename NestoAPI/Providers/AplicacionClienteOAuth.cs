using Microsoft.Owin.Security;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace NestoAPI.Providers
{
    /// <summary>
    /// NestoAPI#575 (fase 2): desde qué aplicación se entra por /oauth/token. Ariadna (la app del almacén)
    /// entra igual que NestoApp, así que manda client_id=Ariadna y su token lleva el claim «app» = Ariadna.
    /// Solo se distingue Ariadna: cualquier otra cosa (NestoApp entra SIN client_id y renueva con
    /// client_id=NestoApp) se queda exactamente como estaba, sin claim y sin propiedad nueva en el ticket.
    /// NestoApp no hace nunca logout: si esto fallara, todos los vendedores se quedarían fuera.
    /// </summary>
    internal static class AplicacionClienteOAuth
    {
        /// <summary>Claim con la aplicación (solo lo llevan los tokens de Ariadna).</summary>
        public const string TIPO_CLAIM = "app";

        /// <summary>
        /// Propiedad del ticket que viaja dentro del refresh_token. SimpleRefreshTokenProvider ya la
        /// leía para la columna ClientId (sin ella, «NestoApp»).
        /// </summary>
        public const string CLAVE_PROPIEDAD = "as:client_id";

        /// <summary>«Ariadna» si el client_id es el suyo; null para todo lo demás (= NestoApp, como siempre).</summary>
        public static string Reconocida(string clientId)
        {
            return string.Equals(clientId?.Trim(), Constantes.Aplicaciones.ARIADNA, StringComparison.OrdinalIgnoreCase)
                ? Constantes.Aplicaciones.ARIADNA
                : null;
        }

        /// <summary>Con aplicación: claim en el token y propiedad en el ticket (para renovar). Sin ella, nada.</summary>
        public static void Marcar(ClaimsIdentity identidad, AuthenticationProperties propiedades, string aplicacion)
        {
            if (aplicacion == null)
            {
                return;
            }
            AñadirClaim(identidad, aplicacion);
            if (propiedades != null)
            {
                propiedades.Dictionary[CLAVE_PROPIEDAD] = aplicacion;
            }
        }

        /// <summary>Al renovar: la aplicación con la que se entró (los refresh_token ya emitidos no la llevan: null).</summary>
        public static string DelTicket(AuthenticationProperties propiedades)
        {
            IDictionary<string, string> diccionario = propiedades?.Dictionary;
            return diccionario != null && diccionario.TryGetValue(CLAVE_PROPIEDAD, out string valor)
                ? Reconocida(valor)
                : null;
        }

        public static void AñadirClaim(ClaimsIdentity identidad, string aplicacion)
        {
            if (identidad == null || aplicacion == null || identidad.HasClaim(c => c.Type == TIPO_CLAIM))
            {
                return;
            }
            identidad.AddClaim(new Claim(TIPO_CLAIM, aplicacion));
        }
    }
}
