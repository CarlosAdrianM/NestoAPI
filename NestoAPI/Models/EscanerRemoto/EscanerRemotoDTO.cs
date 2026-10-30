using System;

namespace NestoAPI.Models.EscanerRemoto
{
    /// <summary>
    /// NestoAPI#768 (Carlos 30/10/26): una lectura que Ariadna (en el móvil o en la PDA) manda al Nesto del mismo usuario.
    /// POST api/EscanerRemoto/Lecturas.
    /// </summary>
    public class LecturaEscanerRemotoDTO
    {
        /// <summary>Lo leído. Obligatorio; se recorta; como mucho <see cref="EscanerRemotoLimites.LONGITUD_MAXIMA_CODIGO"/>.</summary>
        public string Codigo { get; set; }

        /// <summary>Una por lectura: repetir el POST con la misma clave da la misma respuesta y no se vuelve a mandar a Nesto.</summary>
        public string ClaveIdempotencia { get; set; }

        /// <summary>Cuándo se leyó, hora local del aparato sin zona. Opcional (solo informa).</summary>
        public DateTime? FechaHora { get; set; }
    }

    /// <summary>
    /// NestoAPI#768: a qué Nesto va (GET api/EscanerRemoto/Destino) o ha ido (POST api/EscanerRemoto/Lecturas) lo leído.
    /// </summary>
    public class DestinoEscanerRemotoDTO
    {
        /// <summary>Hay al menos un Nesto abierto (y que sabe recibir lecturas) con el usuario de quien pregunta.</summary>
        public bool HayNesto { get; set; }

        /// <summary>Solo en el POST: la API se lo ha mandado a ese Nesto (SignalR 2 no confirma que haya llegado).</summary>
        public bool Entregado { get; set; }

        /// <summary>El usuario sin dominio, tal como entra en Ariadna («Alfredo»).</summary>
        public string Usuario { get; set; }

        /// <summary>El equipo del Nesto elegido («RDS2016»). Null si no hay ninguno o no lo ha dicho.</summary>
        public string Equipo { get; set; }

        /// <summary>Cuántos Nesto abiertos tiene ese usuario (si hay varios, va al que tuvo el foco más recientemente).</summary>
        public int NestosAbiertos { get; set; }

        /// <summary>El texto listo para enseñarlo en Ariadna.</summary>
        public string Mensaje { get; set; }
    }

    /// <summary>NestoAPI#768: lo que Nesto recibe por SignalR (AvisosHub, «lecturaEscanerRemoto»).</summary>
    public class LecturaEscanerRemotoAviso
    {
        public string Codigo { get; set; }
        public string ClaveIdempotencia { get; set; }
        public DateTime? FechaHora { get; set; }
        /// <summary>Quién lo ha leído (sin dominio): siempre el mismo usuario que el de Nesto.</summary>
        public string Usuario { get; set; }
    }

    public static class EscanerRemotoLimites
    {
        public const int LONGITUD_MAXIMA_CODIGO = 200;
        public const int LONGITUD_MAXIMA_CLAVE = 100;
    }
}
