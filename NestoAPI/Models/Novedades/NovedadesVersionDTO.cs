using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Novedades
{
    /// <summary>NestoAPI#890: las novedades de una versión, para cargarlas por la API en vez de con un script de sa.</summary>
    public class CargaNovedadesVersionDTO
    {
        /// <summary>«1.10.67.0» (Nesto y NestoAPI, 1.x) o «2.45» (NestoApp y Ariadna, 2.x).</summary>
        public string Version { get; set; }
        /// <summary>Sin ella, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>Nesto, NestoAPI, NestoApp o Ariadna.</summary>
        public string Ambito { get; set; }
        public List<NovedadVersionDTO> Novedades { get; set; }
    }

    public class NovedadVersionDTO
    {
        /// <summary>Nuevo, Mejorado o Corregido.</summary>
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Vendedores, Almacén, Tiendas, Administración, Compras (vacío o «Todos» = para todos).</summary>
        public List<string> Perfiles { get; set; }
        /// <summary>
        /// Con él NO se crea una novedad: esa sugerencia pasa a Implementada en esta versión (la sugerencia implementada
        /// ES la novedad), con este título, descripción, categoría y perfiles.
        /// </summary>
        public int? SugerenciaId { get; set; }
    }

    /// <summary>PUT api/Novedades/{id}: lo que llega a null se queda como estaba.</summary>
    public class CambioNovedadVersionDTO
    {
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string Categoria { get; set; }
        /// <summary>null = no tocar; [] o ["Todos"] = para todos.</summary>
        public List<string> Perfiles { get; set; }
        public bool? Publicada { get; set; }
    }

    public class NovedadVersionLeidaDTO
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public DateTime Fecha { get; set; }
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string Ambito { get; set; }
        public bool Publicada { get; set; }
        public List<string> Perfiles { get; set; }
        /// <summary>Una sugerencia de un usuario implementada en esta versión (se cambia aquí, pero nunca se borra).</summary>
        public bool EsSugerencia { get; set; }
    }

    public class NovedadCargadaDTO
    {
        /// <summary>null en el ensayo.</summary>
        public int? Id { get; set; }
        public int? SugerenciaId { get; set; }
        public string Categoria { get; set; }
        public string Titulo { get; set; }
    }

    public class ResultadoCargaNovedadesDTO
    {
        public string Version { get; set; }
        public string Ambito { get; set; }
        public DateTime Fecha { get; set; }
        public bool Ensayo { get; set; }
        public List<NovedadCargadaDTO> Creadas { get; set; } = new List<NovedadCargadaDTO>();
        /// <summary>Ya estaban (misma versión, ámbito y título, o la sugerencia ya implementada en esta versión): no se tocan.</summary>
        public List<NovedadCargadaDTO> YaEstaban { get; set; } = new List<NovedadCargadaDTO>();
        public List<NovedadCargadaDTO> SugerenciasImplementadas { get; set; } = new List<NovedadCargadaDTO>();
        public string Mensaje { get; set; }
    }
}
