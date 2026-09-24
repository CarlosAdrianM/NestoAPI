using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Novedades
{
    // NestoAPI#520: feedback de los usuarios en las Novedades. Los tipos casan EXACTAMENTE con las
    // columnas (SqlQuery<T> no convierte: lección de #514): smallint → short, datetime2 → DateTime,
    // bit → bool; los COUNT/SUM se castean a int en la propia consulta.

    /// <summary>Totales de una novedad y el voto de quien pregunta, en una sola consulta para todas.</summary>
    public class ResumenFeedbackNovedad
    {
        public int NovedadId { get; set; }
        public int Positivos { get; set; }
        public int Negativos { get; set; }
        public int Comentarios { get; set; }
        /// <summary>1, -1 o null si el usuario no ha votado (o no hay usuario).</summary>
        public short? MiVoto { get; set; }
    }

    /// <summary>Cuerpo de PUT api/Novedades/{id}/Voto: 1 me gusta, -1 no me gusta, 0 quitar el voto.</summary>
    public class VotoNovedadDTO
    {
        public short Voto { get; set; }
    }

    /// <summary>Cuerpo de POST api/Novedades/{id}/Comentarios. El usuario sale del token, nunca de aquí.</summary>
    public class NuevoComentarioNovedadDTO
    {
        public string Texto { get; set; }
        /// <summary>Captura opcional en base64 (sin el prefijo «data:...;base64,», aunque se tolera).</summary>
        public string ImagenBase64 { get; set; }
        /// <summary>image/png o image/jpeg. Si no viene, se deduce de los bytes.</summary>
        public string ImagenTipo { get; set; }
        /// <summary>Versión del cliente que comenta (Nesto 1.10.29.1, NestoApp 2.20.5…), para el diagnóstico.</summary>
        public string VersionCliente { get; set; }
    }

    /// <summary>
    /// NestoAPI#531: la respuesta del asistente IA a uno o varios comentarios. Los contestados
    /// pasan a revisados.
    /// </summary>
    public class NuevoComentarioAsistenteDTO : NuevoComentarioNovedadDTO
    {
        public List<int> ComentariosContestados { get; set; } = new List<int>();
    }

    /// <summary>Comentario tal cual se muestra (la imagen se pide aparte, por su Id).</summary>
    public class ComentarioNovedadDTO
    {
        public int Id { get; set; }
        public int NovedadId { get; set; }
        public string NombreVisible { get; set; }
        public string Cliente { get; set; }
        public string VersionCliente { get; set; }
        public string Texto { get; set; }
        public DateTime Fecha { get; set; }
        public bool TieneImagen { get; set; }
        /// <summary>El comentario es de quien pregunta: el cliente puede ofrecer «Borrar».</summary>
        public bool EsMio { get; set; }
    }

    /// <summary>Lo que se graba al comentar (interno: ya validado y con el usuario del token).</summary>
    /// <summary>Nesto#477: a quién avisar cuando el asistente contesta un comentario.</summary>
    public class AutorComentarioNovedad
    {
        public int Id { get; set; }
        public int NovedadId { get; set; }
        public string Usuario { get; set; }
        public string NombreVisible { get; set; }
        public string Cliente { get; set; }
    }

    public class ComentarioNovedadAGrabar
    {
        public int NovedadId { get; set; }
        public string Usuario { get; set; }
        public string NombreVisible { get; set; }
        public string Cliente { get; set; }
        public string VersionCliente { get; set; }
        public string Texto { get; set; }
        public byte[] Imagen { get; set; }
        public string ImagenTipo { get; set; }
    }

    public class ImagenComentarioNovedad
    {
        public byte[] Imagen { get; set; }
        public string ImagenTipo { get; set; }
    }

    /// <summary>Para el desarrollo (revisión diaria, como ELMAH): comentarios y votos negativos nuevos.</summary>
    public class FeedbackNovedadesDTO
    {
        public DateTime Desde { get; set; }
        public List<ComentarioFeedbackDTO> Comentarios { get; set; } = new List<ComentarioFeedbackDTO>();
        public List<VotoNegativoFeedbackDTO> VotosNegativos { get; set; } = new List<VotoNegativoFeedbackDTO>();
    }

    public class ComentarioFeedbackDTO
    {
        public int Id { get; set; }
        public int NovedadId { get; set; }
        public string Version { get; set; }
        public string Titulo { get; set; }
        public string Usuario { get; set; }
        public string NombreVisible { get; set; }
        public string Cliente { get; set; }
        public string VersionCliente { get; set; }
        public string Texto { get; set; }
        public bool TieneImagen { get; set; }
        public DateTime Fecha { get; set; }
        public bool Revisado { get; set; }
        public int? IssueGitHub { get; set; }
    }

    public class VotoNegativoFeedbackDTO
    {
        public int NovedadId { get; set; }
        public string Version { get; set; }
        public string Titulo { get; set; }
        public string Usuario { get; set; }
        public string Cliente { get; set; }
        public DateTime Fecha { get; set; }
    }
}
