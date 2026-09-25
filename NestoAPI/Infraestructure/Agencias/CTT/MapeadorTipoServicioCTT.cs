using System;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.PedidosVenta;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// NestoAPI#493: código de servicio de la API de CTT ("Tipos de Servicio Cloud.xlsx") y país del
    /// destinatario a partir del código postal. Por defecto va el servicio económico (48 h), que es
    /// el de la tarifa que compite en el comparador (TarifaCTT48h):
    ///   C48   CTT 48 (España peninsular y Portugal; en la etiqueta sale "48P")
    ///   CBA48 CTT Baleares Economy
    ///   CCA48 CTT Canarias Aéreo 48h (hoy Canarias va por Canteras, pero la API lo cubre)
    /// NestoAPI#505: el usuario puede forzar a mano el URGENTE (EnviosAgencia.Servicio = 24, TarifaCTT24h):
    ///   C24   CTT 24 (España peninsular y Portugal)
    ///   CBA24 CTT Baleares Express (24 h Mallorca, 48 h islas menores)
    ///   CCA24 CTT Canarias Aéreo 24h
    /// Fuera de eso (Ceuta, Melilla, Andorra, internacional) no tenemos tarifa: se rechaza.
    /// </summary>
    public static class MapeadorTipoServicioCTT
    {
        public const string SERVICIO_48H = "C48";
        public const string SERVICIO_BALEARES_ECONOMY = "CBA48";
        public const string SERVICIO_CANARIAS_AEREO_48H = "CCA48";

        public const string SERVICIO_24H = "C24";
        public const string SERVICIO_BALEARES_EXPRESS = "CBA24";
        public const string SERVICIO_CANARIAS_AEREO_24H = "CCA24";

        /// <summary>EnviosAgencia.Servicio del económico (el de DefaultsEnvio y TarifaCTT48h.ServicioId).</summary>
        public const short SERVICIO_ID_48H = 48;
        /// <summary>EnviosAgencia.Servicio del urgente (TarifaCTT24h.ServicioId). Solo si el usuario lo fuerza.</summary>
        public const short SERVICIO_ID_24H = 24;

        /// <summary>Servicio por defecto (48 h) para el código postal.</summary>
        public static string TipoServicioDesdeCodigoPostal(string codigoPostal)
            => TipoServicio(SERVICIO_ID_48H, codigoPostal);

        /// <summary>
        /// NestoAPI#505: código de CTT para el servicio elegido (EnviosAgencia.Servicio) y el código
        /// postal. 0 (lo que guardaba Nesto antes de tener lista de servicios) y 48 son el económico;
        /// 24, el urgente. Cualquier otro valor se rechaza con un mensaje claro, igual que una zona que
        /// no tarificamos: nunca se manda a CTT un servicio que no hemos elegido.
        /// </summary>
        public static string TipoServicio(short servicio, string codigoPostal)
        {
            bool urgente;
            if (servicio == 0 || servicio == SERVICIO_ID_48H)
            {
                urgente = false;
            }
            else if (servicio == SERVICIO_ID_24H)
            {
                urgente = true;
            }
            else
            {
                throw new ArgumentException($"CTT no tiene el servicio {servicio}. Elige «CTT 48h» (el normal) o «CTT 24h» (urgente).");
            }

            string cp = (codigoPostal ?? string.Empty).Trim();
            if (EsPortugal(cp)) return urgente ? SERVICIO_24H : SERVICIO_48H;
            if (GestorPortes.EsCanarias(cp)) return urgente ? SERVICIO_CANARIAS_AEREO_24H : SERVICIO_CANARIAS_AEREO_48H;
            if (GestorPortes.EsBaleares(cp)) return urgente ? SERVICIO_BALEARES_EXPRESS : SERVICIO_BALEARES_ECONOMY;
            if (PerfilAgenciaCorreosExpress.EsCodigoPostalEspanol(cp) && !EsCeutaOMelilla(cp)) return urgente ? SERVICIO_24H : SERVICIO_48H;
            throw new ArgumentException($"CTT: el código postal '{cp}' no está en las zonas que tarificamos (península, Baleares, Canarias y Portugal).");
        }

        /// <summary>Código ISO alpha-2 del país del destinatario, deducido del código postal.</summary>
        public static string PaisDesdeCodigoPostal(string codigoPostal)
            => EsPortugal((codigoPostal ?? string.Empty).Trim()) ? "PT" : "ES";

        public static bool EsPortugal(string cp) => PerfilAgenciaCorreosExpress.EsCodigoPostalPortugues(cp);

        private static bool EsCeutaOMelilla(string cp) => cp.StartsWith("51") || cp.StartsWith("52");
    }
}
