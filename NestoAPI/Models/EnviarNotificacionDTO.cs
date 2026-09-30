namespace NestoAPI.Models
{
    public class EnviarNotificacionDTO
    {
        public string Destinatario { get; set; }
        public string TipoDestinatario { get; set; }
        public string Empresa { get; set; }
        public string Aplicacion { get; set; }
        public NotificacionPushDTO Notificacion { get; set; }
    }

    /// <summary>POST api/Notificaciones/NuevaVersionNesto (ritual del deploy de Nesto, 25/09/26).</summary>
    /// <summary>28/09/26: aviso en la campana de Nesto a uno o varios usuarios concretos.</summary>
    public class AvisoNestoDTO
    {
        /// <summary>Usuarios de Windows («NUEVAVISION\Alfredo» o solo «Alfredo»).</summary>
        public System.Collections.Generic.List<string> Usuarios { get; set; }
        public string Titulo { get; set; }
        public string Texto { get; set; }
    }

    public class NuevaVersionNestoDTO
    {
        public string Version { get; set; }
        /// <summary>Opcional: otro texto en vez del de siempre («cerrad Nesto y volved a abrirlo…»).</summary>
        public string Texto { get; set; }
        /// <summary>
        /// NestoAPI#568: avisar solo a estos (de los que tienen Nesto abierto), para repartir el aviso
        /// por tandas. Vacío o null = a todos los que lo tienen abierto, como siempre.
        /// </summary>
        public System.Collections.Generic.List<string> Usuarios { get; set; }
        /// <summary>NestoAPI#568: no avisa a nadie; solo devuelve quién tiene Nesto abierto.</summary>
        public bool SoloListar { get; set; }
    }
}
