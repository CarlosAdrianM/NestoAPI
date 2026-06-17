using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// NestoAPI#231: lee el mapeo terminal TPV → usuario de la tabla TerminalesUsuariosTPV,
    /// editable sin recompilar. Devuelve null si no hay datos o no se puede leer (la tabla aún no
    /// existe), para que ContabilidadService use su diccionario por defecto como fallback.
    /// </summary>
    public interface IRepositorioTerminalesTPV
    {
        Dictionary<string, string> LeerMapa();
    }

    public class RepositorioTerminalesTPV : IRepositorioTerminalesTPV
    {
        public Dictionary<string, string> LeerMapa()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    List<TerminalUsuarioTPV> filas = db.Database
                        .SqlQuery<TerminalUsuarioTPV>("SELECT Terminal, Usuario FROM TerminalesUsuariosTPV")
                        .ToList();

                    if (filas.Count == 0)
                    {
                        return null;
                    }
                    return filas
                        .Where(f => !string.IsNullOrWhiteSpace(f.Terminal))
                        .GroupBy(f => f.Terminal.Trim())
                        .ToDictionary(g => g.Key, g => g.First().Usuario?.Trim() ?? string.Empty);
                }
            }
            catch
            {
                // La tabla no existe todavía o hay un error de lectura: fallback al diccionario fijo.
                return null;
            }
        }
    }

    internal class TerminalUsuarioTPV
    {
        public string Terminal { get; set; }
        public string Usuario { get; set; }
    }
}
