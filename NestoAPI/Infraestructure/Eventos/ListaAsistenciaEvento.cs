using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#908 (Carlos 24/11/26, Masterclass Rosácea: evento online del 24/11 con 21 inscritas facturadas). Reglas PURAS de la lista de
    /// asistencia:
    /// <list type="bullet">
    /// <item>En un evento ONLINE (<see cref="FormatosEvento.EsOnline"/>) nadie «viene»: no se pasa lista. Al día siguiente el job de siempre
    /// (<c>DarPorAsistidosSinLista</c>) da por asistidas a todas las pagadas (menos las que avisaron «No podré ir», decisión del 22/10/26).</item>
    /// <item>En cualquier evento, guardar la lista sin NADIE marcado (con inscritas pagadas) pide un «confirmo» expreso
    /// (<see cref="AsistenciaEventoDTO.Confirmar"/>): todas pasarían a «No asistió» y perderían el cupón.</item>
    /// </list>
    /// </summary>
    public static class ReglasListaAsistencia
    {
        public const string ERROR_EVENTO_ONLINE = "LISTA_EVENTO_ONLINE";
        public const string ERROR_SIN_ASISTENTES = "LISTA_SIN_ASISTENTES";

        /// <summary>¿Se pasa lista en este evento? No en los online.</summary>
        public static bool SePasaLista(Evento evento) => !FormatosEvento.EsOnline(evento);

        /// <summary>El texto para Nesto en lugar de «Marque quién ha venido…».</summary>
        public const string TEXTO_ONLINE = "Evento online: todas las inscritas pagadas se dan por asistidas.";

        /// <summary>Por qué no se puede pasar lista (null si se puede).</summary>
        public static string MotivoNoSePasaLista(Evento evento) => SePasaLista(evento)
            ? null
            : $"El evento «{evento?.Titulo?.Trim()}» es online: no se pasa lista. Al día siguiente todas las inscritas pagadas se dan por asistidas " +
              "(menos las que avisaron de que no podrían ir).";

        /// <summary>
        /// Si hace falta el «confirmo»: con inscritas pagadas en la lista y NINGUNA marcada, sin confirmar, el motivo (de usted, con cuántas
        /// perderían el cupón); si no, null.
        /// </summary>
        public static string MotivoConfirmar(int enLista, int marcadas, bool confirmado)
        {
            if (confirmado || enLista <= 0 || marcadas > 0)
            {
                return null;
            }
            string cuantas = enLista == 1 ? "la inscrita pagada pasará" : $"las {enLista} inscritas pagadas pasarán";
            string cupon = enLista == 1 ? "perderá el cupón" : "perderán el cupón";
            return $"No ha marcado a nadie: {cuantas} a «No asistió» y {cupon}. Si es así, vuelva a guardar la lista confirmándolo.";
        }
    }
}
