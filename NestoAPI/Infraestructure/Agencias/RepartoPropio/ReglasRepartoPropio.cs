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

    /// <summary>
    /// NestoAPI#778 (Carlos 03/11/26, «al crear el envío»): el reembolso de un envío del reparto propio no es nunca un reembolso
    /// «pendiente de cobrar por la agencia» (FechaPagoReembolso NULL): lo cobra nuestro repartidor (efectivo a su caja, tarjeta
    /// que liquida la factura, #773). Por eso nace ya con FechaPagoReembolso = Fecha (sin hora) y el correo de cuadre de
    /// agencias con la 55500092 (Reembolso &lt;&gt; 0 AND FechaPagoReembolso IS NULL) no lo ve. El importe sigue en Reembolso,
    /// que es lo que Ariadna cobra y lo que sale en la firma. Único sitio de la regla: todos los caminos que crean o cambian un
    /// envío llaman a <see cref="AplicarFechaPago"/>.
    /// </summary>
    public static class ReembolsoRepartoPropio
    {
        /// <summary>
        /// ¿El reembolso de esta agencia lo cobramos nosotros? Es la misma regla que decide cuándo se contabiliza
        /// (<see cref="MomentosContabilizarReembolso.De(int)"/>: al entregar, contra la caja del repartidor), no una copia.
        /// </summary>
        public static bool EsAgenciaRepartoPropio(int agencia)
            => MomentosContabilizarReembolso.De(agencia) == MomentoContabilizarReembolso.AlEntregar;

        /// <summary>
        /// Deja FechaPagoReembolso como le toca al envío tras crearlo o cambiarlo:
        /// <list type="bullet">
        /// <item>Reparto propio con reembolso (≠ 0) y sin fecha: FechaPagoReembolso = Fecha (sin hora).</item>
        /// <item>Reparto propio sin reembolso: no se toca.</item>
        /// <item>Agencia de fuera que ANTES era de reparto propio (<paramref name="agenciaAnterior"/>): NULL, vuelve a estar
        /// pendiente de que la agencia lo pague.</item>
        /// <item>Agencia de fuera que ya lo era: no se toca (su fecha la pone «Contabilizar reembolsos»).</item>
        /// </list>
        /// </summary>
        public static void AplicarFechaPago(EnviosAgencia envio, int? agenciaAnterior = null)
        {
            if (envio == null)
            {
                return;
            }
            if (EsAgenciaRepartoPropio(envio.Agencia))
            {
                if (envio.Reembolso != 0m && !envio.FechaPagoReembolso.HasValue)
                {
                    envio.FechaPagoReembolso = envio.Fecha.Date;
                }
                return;
            }
            if (agenciaAnterior.HasValue && EsAgenciaRepartoPropio(agenciaAnterior.Value))
            {
                envio.FechaPagoReembolso = null;
            }
        }

        /// <summary>
        /// ¿El reembolso está ya pagado por la agencia (y por tanto no se puede cambiar ni anular el envío)? En el reparto propio
        /// la FechaPagoReembolso no significa eso (la lleva desde que nace, #778), así que nunca bloquea: como antes de #778.
        /// </summary>
        public static bool PagadoPorLaAgencia(EnviosAgencia envio)
            => envio != null && envio.FechaPagoReembolso.HasValue && !EsAgenciaRepartoPropio(envio.Agencia);
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

    /// <summary>
    /// NestoAPI#758 (corte 2 de #755): los códigos de barras de las notas de entrega, por NÚMERO DE PEDIDO (empresa 1) y no por
    /// envío, porque se imprimen antes de facturar la ruta y valen para las notas de Nesto viejo: <c>NE{Pedido}</c> (documento de
    /// una nota de ENTREGA), <c>NR{Pedido}</c> (documento de una nota de RECOGIDA) y <c>NR{Pedido}-{N}</c> (etiqueta del bulto N
    /// que se recoge). Las entregas no llevan etiqueta propia: llevan la RP del packing.
    /// </summary>
    public static class CodigoNotaEntrega
    {
        public const string PREFIJO_ENTREGA = "NE";
        public const string PREFIJO_RECOGIDA = "NR";
        /// <summary>NestoAPI#786: la parada de una nota de COBRO. No es un código de bulto (no tiene): no se lee al cargar.</summary>
        public const string PREFIJO_COBRO = "NC";
        private static readonly Regex formato = new Regex(@"^(NE|NR)(\d{1,9})(?:-(\d{1,4}))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>«NE930123» o «NR930123».</summary>
        public static string Documento(bool esRecogida, int pedido)
            => (esRecogida ? PREFIJO_RECOGIDA : PREFIJO_ENTREGA) + pedido.ToString(CultureInfo.InvariantCulture);

        /// <summary>NestoAPI#786: «NC930123».</summary>
        public static string DocumentoCobro(int pedido) => PREFIJO_COBRO + pedido.ToString(CultureInfo.InvariantCulture);

        /// <summary>«NR930123-2».</summary>
        public static string BultoRecogida(int pedido, int bulto)
            => Documento(true, pedido) + "-" + bulto.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// «nr930123-2 » → (NR, 930123, 2); «NE930123» → (NE, 930123, null). False si no tiene esa forma (las entregas no tienen
        /// bultos con código: «NE930123-1» no vale).
        /// </summary>
        public static bool TryLeer(string codigo, out string prefijo, out int pedido, out int? bulto)
        {
            prefijo = null;
            pedido = 0;
            bulto = null;
            Match m = formato.Match(codigo?.Trim() ?? string.Empty);
            if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out pedido) || pedido <= 0)
            {
                pedido = 0;
                return false;
            }
            prefijo = m.Groups[1].Value.ToUpperInvariant();
            if (m.Groups[3].Success)
            {
                if (prefijo != PREFIJO_RECOGIDA
                    || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n <= 0)
                {
                    prefijo = null;
                    pedido = 0;
                    return false;
                }
                bulto = n;
            }
            return true;
        }
    }
}
