using System;

namespace NestoAPI.Infraestructure.Agencias.Tarifas
{
    /// <summary>
    /// NestoAPI#494 (Carlos 28/09/26): precio de los retornos de GLS e Innovatrans, sacado de sus ofertas
    /// 2026. Regla común con CTT: el retorno cuesta lo mismo que un envío. Lo que cambia es la RECOGIDA
    /// SUELTA (ir a casa del cliente solo a recoger):
    /// <list type="bullet">
    /// <item>GLS (ASM_2026.pdf, «Recogidas fuera de origen»): en Madrid, un envío; en capital de provincia,
    /// un envío + 1,23 €; en pueblos cobra los km a la capital (0,62 €/km ida y vuelta), que no sabemos
    /// calcular desde el CP → sin precio, fuera de la subasta.</item>
    /// <item>Innovatrans (Oferta Paquetería 2026 v.1): «Para recogidas resto de Península, sujetas a oferta
    /// o presupuesto previo» → solo en Madrid.</item>
    /// </list>
    /// La vuelta en la misma entrega («puente de vuelta», GLS: «se facturará como nuevo envío»; Innovatrans:
    /// «mismo precio del servicio original») es un envío más, sin recargos de recogida.
    /// </summary>
    public static class ReglasRetornoAgencias
    {
        internal const string PROVINCIA_ORIGEN = "28"; // Madrid (Algete)
        internal const decimal CANON_RECOGIDA_FUERA_ORIGEN_GLS = 1.23m;

        /// <summary>CP español de la provincia de origen (Madrid).</summary>
        internal static bool EsProvinciaDeOrigen(string codigoPostal, string paisIso)
            => EsEspana(paisIso) && NormalizarCp(codigoPostal)?.StartsWith(PROVINCIA_ORIGEN) == true;

        /// <summary>
        /// CP de una capital de provincia española: la tercera cifra es 0 (46001 Valencia, 08001 Barcelona,
        /// 07001 Palma). Los pueblos llevan otra cifra (46100 Burjassot).
        /// </summary>
        internal static bool EsCapitalDeProvincia(string codigoPostal, string paisIso)
        {
            string cp = NormalizarCp(codigoPostal);
            return EsEspana(paisIso) && cp != null && cp[2] == '0';
        }

        /// <summary>Recogida suelta por GLS a partir del coste de un envío al mismo sitio (sin reembolso).</summary>
        internal static decimal RecogidaSueltaGLS(decimal costeEnvio, string codigoPostal, string paisIso)
        {
            if (costeEnvio == decimal.MaxValue) return decimal.MaxValue;
            if (EsProvinciaDeOrigen(codigoPostal, paisIso)) return costeEnvio;
            if (EsCapitalDeProvincia(codigoPostal, paisIso)) return costeEnvio + CANON_RECOGIDA_FUERA_ORIGEN_GLS;
            return decimal.MaxValue; // pueblo (km sin calcular), Portugal o extranjero: sin precio
        }

        /// <summary>Recogida suelta por Innovatrans: solo en Madrid; el resto necesita presupuesto.</summary>
        internal static decimal RecogidaSueltaInnovatrans(decimal costeEnvio, string codigoPostal, string paisIso)
            => costeEnvio != decimal.MaxValue && EsProvinciaDeOrigen(codigoPostal, paisIso) ? costeEnvio : decimal.MaxValue;

        private static bool EsEspana(string paisIso)
        {
            string iso = (paisIso ?? string.Empty).Trim().ToUpperInvariant();
            return iso.Length == 0 || iso == "ES";
        }

        private static string NormalizarCp(string codigoPostal)
        {
            string cp = (codigoPostal ?? string.Empty).Trim();
            return cp.Length == 5 && cp.Length == cp.Trim().Length && int.TryParse(cp, out _) ? cp : null;
        }
    }
}
