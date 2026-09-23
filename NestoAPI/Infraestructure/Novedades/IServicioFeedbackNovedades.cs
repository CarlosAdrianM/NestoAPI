using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#520: votos y comentarios de los usuarios en las Novedades. Tablas satélite fuera del
    /// EDMX (como Novedades): se accede con SQL parametrizado para no tocar el designer.
    /// </summary>
    public interface IServicioFeedbackNovedades
    {
        /// <summary>Totales de TODAS las novedades pedidas en una sola consulta (nada de una por novedad).</summary>
        List<ResumenFeedbackNovedad> LeerResumen(IEnumerable<int> novedades, string usuario);

        bool ExisteNovedad(int novedadId);

        /// <summary>Upsert por (novedad, usuario): un solo voto por persona. Voto 0 = quitarlo.</summary>
        void Votar(int novedadId, string usuario, string cliente, short voto);

        List<ComentarioNovedadDTO> LeerComentarios(int novedadId, string usuario);

        int CrearComentario(ComentarioNovedadAGrabar comentario);

        ImagenComentarioNovedad LeerImagen(int comentarioId);

        /// <summary>Usuario autor del comentario, o null si no existe o ya está borrado.</summary>
        string LeerAutorComentario(int comentarioId);

        void BorrarComentario(int comentarioId);

        FeedbackNovedadesDTO LeerFeedback(DateTime desde, bool soloNoRevisados);

        bool MarcarRevisado(int comentarioId);
    }
}
