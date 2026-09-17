using System;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.PedidosVenta;

namespace NestoAPI.Infraestructure.Agencias.CTT
{
    /// <summary>
    /// NestoAPI#493: código de servicio de la API de CTT ("Tipos de Servicio Cloud.xlsx") y país del
    /// destinatario a partir del código postal. Usamos siempre el servicio económico (48 h), que es
    /// el de la tarifa que compite en el comparador (TarifaCTT48h):
    ///   C48   CTT 48 (España peninsular y Portugal; en la etiqueta sale "48P")
    ///   CBA48 CTT Baleares Economy
    ///   CCA48 CTT Canarias Aéreo 48h (hoy Canarias va por Canteras, pero la API lo cubre)
    /// Fuera de eso (Ceuta, Melilla, Andorra, internacional) no tenemos tarifa: se rechaza.
    /// </summary>
    public static class MapeadorTipoServicioCTT
    {
        public const string SERVICIO_48H = "C48";
        public const string SERVICIO_BALEARES_ECONOMY = "CBA48";
        public const string SERVICIO_CANARIAS_AEREO_48H = "CCA48";

        public static string TipoServicioDesdeCodigoPostal(string codigoPostal)
        {
            string cp = (codigoPostal ?? string.Empty).Trim();
            if (EsPortugal(cp)) return SERVICIO_48H;
            if (GestorPortes.EsCanarias(cp)) return SERVICIO_CANARIAS_AEREO_48H;
            if (GestorPortes.EsBaleares(cp)) return SERVICIO_BALEARES_ECONOMY;
            if (PerfilAgenciaCorreosExpress.EsCodigoPostalEspanol(cp) && !EsCeutaOMelilla(cp)) return SERVICIO_48H;
            throw new ArgumentException($"CTT: el código postal '{cp}' no está en las zonas que tarificamos (península, Baleares, Canarias y Portugal).");
        }

        /// <summary>Código ISO alpha-2 del país del destinatario, deducido del código postal.</summary>
        public static string PaisDesdeCodigoPostal(string codigoPostal)
            => EsPortugal((codigoPostal ?? string.Empty).Trim()) ? "PT" : "ES";

        public static bool EsPortugal(string cp) => PerfilAgenciaCorreosExpress.EsCodigoPostalPortugues(cp);

        private static bool EsCeutaOMelilla(string cp) => cp.StartsWith("51") || cp.StartsWith("52");
    }
}
