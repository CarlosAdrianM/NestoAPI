using System;

namespace NestoAPI.Models.Globos
{
    /// <summary>
    /// NestoAPI#774: un globo («consejo», coach mark) tal como lo pinta la app. GET api/Globos devuelve solo los pendientes del
    /// usuario autenticado, ya ordenados (Orden, Id). La app no decide nada más que DÓNDE pintarlo: junto al control cuya clave
    /// de ancla coincide con <see cref="Ancla"/> o, si no está a la vista, como tarjeta centrada.
    /// </summary>
    public class GloboDTO
    {
        public int Id { get; set; }
        /// <summary>Nesto, Ariadna, TNV o NestoApp.</summary>
        public string Aplicacion { get; set; }
        /// <summary>Clave de la pantalla (Nesto: la vista sin «View»; Ariadna: la página sin «Page»). P. ej. «Reparto».</summary>
        public string Pantalla { get; set; }
        /// <summary>Clave completa del control («Reparto.EnLaFurgoneta»). Null o vacía = tarjeta centrada.</summary>
        public string Ancla { get; set; }
        /// <summary>Opcional.</summary>
        public string Titulo { get; set; }
        public string Texto { get; set; }
        public short Orden { get; set; }
        /// <summary>La novedad que explica, si la hay (para «Ver la novedad»).</summary>
        public int? NovedadId { get; set; }
    }

    /// <summary>NestoAPI#774: la fila de la tabla Globos (fuera del EDMX; Scripts/Globos.sql).</summary>
    public class GloboFila
    {
        public int Id { get; set; }
        public string Aplicacion { get; set; }
        public string VersionDesde { get; set; }
        public string Pantalla { get; set; }
        public string Ancla { get; set; }
        public string Titulo { get; set; }
        public string Texto { get; set; }
        public string Grupos { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public short Orden { get; set; }
        public int? NovedadId { get; set; }
        public bool Activo { get; set; }

        public GloboDTO ADto() => new GloboDTO
        {
            Id = Id,
            Aplicacion = Aplicacion?.Trim(),
            Pantalla = Pantalla?.Trim(),
            Ancla = string.IsNullOrWhiteSpace(Ancla) ? null : Ancla.Trim(),
            Titulo = string.IsNullOrWhiteSpace(Titulo) ? null : Titulo.Trim(),
            Texto = Texto?.Trim(),
            Orden = Orden,
            NovedadId = NovedadId
        };
    }

    /// <summary>NestoAPI#774: respuesta de POST api/Globos/{id}/Visto.</summary>
    public class GloboVistoDTO
    {
        public int Id { get; set; }
        /// <summary>True si ya estaba marcado (la segunda llamada no cambia nada).</summary>
        public bool YaEstaba { get; set; }
    }
}
