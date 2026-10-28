using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: interruptor de POST api/FacturacionRutas/FinDeMes (<c>Facturacion:FinDeMesPorApi</c> en Web.config).
    /// Nace ENCENDIDO en el Web.config del repo; como los demás interruptores, solo "true" enciende (sin la clave, apagado:
    /// facturar tiene que ser una decisión). Apagado, el endpoint contesta 400 y el fin de mes se hace como siempre.
    /// </summary>
    public static class InterruptorFinDeMesPorApi
    {
        public const string CLAVE = "Facturacion:FinDeMesPorApi";

        public const string MOTIVO_APAGADO = "La facturación de fin de mes por la API está desactivada (Facturacion:FinDeMesPorApi). Hágala como siempre desde Nesto.";

        /// <summary>Lee el Web.config en cada llamada.</summary>
        public static bool EstaActivo() => EstaActivo(ConfigurationManager.AppSettings[CLAVE]);

        public static bool EstaActivo(string valor)
            => string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }
}
