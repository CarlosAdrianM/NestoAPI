using System;
using NestoAPI.Infraestructure.Agencias.Tarifas;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Elige el código de tipo de servicio (tipoServ, 4 chars) de DataTrans DTX según la zona de
    /// destino del envío. Catálogo confirmado por el integrador (16/06/26):
    /// Economy = 0048 (península), 14H Portugal = 0014, Islas Portugal = 0006 (Madeira/Azores, no
    /// modelado), Marítimo Baleares/Canarias = 0EXP. La zona se calcula con
    /// <see cref="CalculadoraZonaEnvio"/> (mismo criterio que el comparador de agencias).
    /// OJO: aunque el catálogo agrupa Baleares y Canarias en 0EXP, CANARIAS NO sale por Innovatrans
    /// (va siempre por Canteras, igual que en el comparador). Si un CP de Canarias llega aquí es un
    /// error de enrutado: lanzamos en vez de mandar el envío por la agencia equivocada.
    /// </summary>
    public static class MapeadorTipoServicioDataTrans
    {
        public const string SERVICIO_ECONOMY = "0048";
        public const string SERVICIO_PORTUGAL_14H = "0014";
        public const string SERVICIO_MARITIMO_ISLAS = "0EXP";

        public static string TipoServicioDesdeCodigoPostal(string codigoPostal)
        {
            ZonasEnvioAgencia zona = CalculadoraZonaEnvio.CalcularZona(codigoPostal);
            switch (zona)
            {
                case ZonasEnvioAgencia.Provincial:
                case ZonasEnvioAgencia.Peninsular:
                    return SERVICIO_ECONOMY;
                case ZonasEnvioAgencia.Portugal:
                    return SERVICIO_PORTUGAL_14H;
                case ZonasEnvioAgencia.BalearesMayores:
                case ZonasEnvioAgencia.BalearesMenores:
                    return SERVICIO_MARITIMO_ISLAS;
                case ZonasEnvioAgencia.CanariasMayores:
                case ZonasEnvioAgencia.CanariasMenores:
                    throw new ArgumentException(
                        $"Canarias (CP '{codigoPostal}') no sale por Innovatrans, va por Canteras.", nameof(codigoPostal));
                default:
                    throw new ArgumentException(
                        $"Innovatrans no tiene servicio para la zona '{zona}' (CP '{codigoPostal}').", nameof(codigoPostal));
            }
        }
    }
}
