using System;

namespace NestoAPI.Models.Novedades
{
    /// <summary>
    /// Recordatorios de respuesta (Carlos, 16/10/26): una sugerencia o incidencia en la que el asistente le ha
    /// preguntado algo al autor y se espera su respuesta (Scripts/NovedadesRecordatoriosRespuesta.sql).
    /// </summary>
    public class EsperaRespuestaNovedad
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public string Estado { get; set; }
        public string Categoria { get; set; }
        public string Ambito { get; set; }
        public string Titulo { get; set; }
        /// <summary>Clave estable del autor (NUEVAVISION\usuario en Nesto, el Id de Identity en las apps).</summary>
        public string SugeridaPor { get; set; }
        public string SugeridaNombre { get; set; }
        /// <summary>Desde cuándo se espera la respuesta del autor (la pregunta del asistente). null = no se espera nada.</summary>
        public DateTime? EsperandoRespuestaDesde { get; set; }
        /// <summary>Recordatorios ya mandados desde la pregunta (0, 1 o 2; al tercero se cierra).</summary>
        public byte RecordatoriosRespuesta { get; set; }
        /// <summary>Cuándo se mandó el último recordatorio (null = ninguno todavía).</summary>
        public DateTime? UltimoRecordatorioRespuesta { get; set; }
    }

    /// <summary>Lo que ha hecho (o haría, en ensayo) el job de recordatorios con cada sugerencia.</summary>
    public class ResultadoRecordatorioRespuestaDTO
    {
        public int NovedadId { get; set; }
        public string Titulo { get; set; }
        public string Autor { get; set; }
        public DateTime? EsperandoDesde { get; set; }
        /// <summary>Recordatorio o Cerrar.</summary>
        public string Accion { get; set; }
        /// <summary>Número del recordatorio (1 o 2); 0 al cerrar.</summary>
        public int Recordatorio { get; set; }
        public string Texto { get; set; }
        public bool Ensayo { get; set; }
        /// <summary>null si ha ido bien.</summary>
        public string Error { get; set; }
    }
}
