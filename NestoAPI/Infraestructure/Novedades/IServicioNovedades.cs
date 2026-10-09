using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Novedades
{
    public interface IServicioNovedades
    {
        /// <summary>Las del changelog: publicadas y con versión (las sugerencias no, #526).</summary>
        List<NovedadDTO> LeerNovedadesPublicadas();

        /// <summary>
        /// Sugerencia 551: el valor de la columna Perfiles de las novedades que lo tienen (Id → «Almacén,Tiendas»).
        /// Lanza si la columna aún no existe (Scripts/Sugerencia551_NovedadesPerfiles.sql): el controlador lo
        /// trata como «todas para todos».
        /// </summary>
        Dictionary<int, string> LeerPerfiles();

        /// <summary>Sugerencia 551: a quién afecta la novedad (null = a todos). false si no existe.</summary>
        bool GuardarPerfiles(int novedadId, string perfiles, string usuario);

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

        /// <summary>
        /// NestoAPI#558: los últimos errores de ELMAH (dbo.ELMAH_Error) de cualquiera de esos usuarios desde
        /// <paramref name="desdeUtc"/>, del más reciente al más antiguo. Para el contexto de las incidencias.
        /// </summary>
        List<ErrorElmahResumen> LeerErroresElmah(IReadOnlyList<string> usuarios, DateTime desdeUtc, int maximo);
    }
}
