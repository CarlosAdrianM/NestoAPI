using NestoAPI.Models.Novedades;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Novedades
{
    public interface IServicioNovedades
    {
        /// <summary>Las del changelog: publicadas y con versión (las sugerencias no, #526).</summary>
        List<NovedadDTO> LeerNovedadesPublicadas();

        /// <summary>NestoAPI#526: las sugerencias (sin versión). Sin cerradas, solo pendientes y aceptadas.</summary>
        List<NovedadConSugerenciaFila> LeerSugerencias(bool incluirCerradas);

        /// <summary>NestoAPI#526: devuelve el Id de la nueva novedad sin versión.</summary>
        int CrearSugerencia(SugerenciaNovedadAGrabar sugerencia);

        /// <summary>NestoAPI#526: false si no existe o no es una sugerencia.</summary>
        bool ActualizarSugerencia(int id, ActualizarSugerenciaNovedadDTO cambios, string usuario);

        /// <summary>NestoAPI#526: la captura que acompaña a la sugerencia, o null.</summary>
        ImagenComentarioNovedad LeerImagen(int novedadId);

        /// <summary>NestoAPI#527: novedades y sugerencias publicadas que contienen TODAS las palabras.</summary>
        List<NovedadConSugerenciaFila> Buscar(IReadOnlyList<string> palabras);
    }
}
