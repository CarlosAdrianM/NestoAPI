using System;
using System.Configuration;
using System.Globalization;
using System.Text.RegularExpressions;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>NestoAPI#739: cuándo se contabiliza el reembolso de un envío.</summary>
    public enum MomentoContabilizarReembolso
    {
        /// <summary>Al tramitar (ConfirmarTramitacion): la agencia lo cobra y nos lo debe. Todas las de fuera.</summary>
        AlTramitar,
        /// <summary>Al entregar: lo cobra nuestro repartidor y va a su caja (reparto propio).</summary>
        AlEntregar
    }

    /// <summary>
    /// NestoAPI#739 (Carlos 29/10/26): el MOMENTO de contabilizar el reembolso es propiedad de la agencia, en un único sitio por
    /// número de agencia. Hoy todas al tramitar salvo el reparto propio, que contabiliza al entregar (contra la cuenta de caja del
    /// repartidor, AgenciasTransporte.CuentaReembolsos de la 14). NestoAPI#742: las demás, con la sobrecarga de tres parámetros.
    /// </summary>
    public static class MomentosContabilizarReembolso
    {
        public static MomentoContabilizarReembolso De(int agencia)
            => agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                ? MomentoContabilizarReembolso.AlEntregar
                : MomentoContabilizarReembolso.AlTramitar;

        /// <summary>
        /// NestoAPI#742 (Carlos 29/10/26): con el reembolso al entregar activo (interruptor Agencias:ReembolsoAlEntregar y
        /// script aplicado), TODAS las agencias contabilizan al entregar. Pero solo las que tienen SEGUIMIENTO: son las únicas
        /// que pasan solas a Entregado; en una sin seguimiento el reembolso diferido no se contabilizaría nunca. La 14 sigue
        /// con lo suyo (al entregar, contra la caja del repartidor, en ServicioRepartoPropio).
        /// </summary>
        public static MomentoContabilizarReembolso De(int agencia, bool reembolsoAlEntregarActivo, bool agenciaConSeguimiento)
            => De(agencia) == MomentoContabilizarReembolso.AlEntregar || (reembolsoAlEntregarActivo && agenciaConSeguimiento)
                ? MomentoContabilizarReembolso.AlEntregar
                : MomentoContabilizarReembolso.AlTramitar;
    }
}

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: interruptor del reparto propio como agencia, <c>Agencias:RepartoPropio</c> en Web.config. Encendido, la
    /// propuesta de envío ofrece la agencia 14 a los pedidos de ruta propia (AT, 16) y la API la tramita (numera los bultos) e
    /// imprime su etiqueta. Apagado, la agencia 14 ni se propone ni se tramita: todo como antes. Solo "true" enciende.
    /// </summary>
    public static class InterruptorRepartoPropio
    {
        public const string CLAVE = "Agencias:RepartoPropio";

        /// <summary>Lee el Web.config en cada llamada.</summary>
        public static bool EstaActivo() => EstaActivo(ConfigurationManager.AppSettings[CLAVE]);

        public static bool EstaActivo(string valor) => string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#739: qué pedidos van por el reparto propio. Las rutas son las de la «Ruta propia» de Facturar rutas.</summary>
    public static class RutasRepartoPropio
    {
        private static readonly RutaPropia rutaPropia = new RutaPropia();

        public static bool EsRutaPropia(string ruta) => rutaPropia.ContieneRuta(ruta);
    }

    /// <summary>
    /// NestoAPI#739: el código de barras de cada bulto, <c>RP{EnvioId}-{N}</c> (N = 1..Bultos), y el «albarán» del envío,
    /// <c>RP{EnvioId}</c>, que es lo que se guarda en EnviosAgencia.CodigoBarras.
    /// </summary>
    public static class CodigoBultoRepartoPropio
    {
        public const string PREFIJO = "RP";
        private static readonly Regex formatoBulto = new Regex(@"^RP(\d{1,9})-(\d{1,4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex formatoAlbaran = new Regex(@"^RP(\d{1,9})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Albaran(int envio) => PREFIJO + envio.ToString(CultureInfo.InvariantCulture);

        public static string Bulto(int envio, int bulto) => Albaran(envio) + "-" + bulto.ToString(CultureInfo.InvariantCulture);

        /// <summary>«RP123456-2» → (123456, 2). Sin espacios a los lados y sin distinguir mayúsculas. False si no tiene esa forma.</summary>
        public static bool TryLeerBulto(string codigo, out int envio, out int bulto)
        {
            envio = 0;
            bulto = 0;
            Match m = formatoBulto.Match(codigo?.Trim() ?? string.Empty);
            return m.Success
                && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out envio)
                && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out bulto)
                && envio > 0 && bulto > 0;
        }

        /// <summary>«RP123456» → 123456.</summary>
        public static bool TryLeerAlbaran(string albaran, out int envio)
        {
            envio = 0;
            Match m = formatoAlbaran.Match(albaran?.Trim() ?? string.Empty);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out envio) && envio > 0;
        }
    }
}
