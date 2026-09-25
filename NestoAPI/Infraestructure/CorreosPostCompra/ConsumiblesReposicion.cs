using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: lista BLANCA de grupos/subgrupos consumibles (solo de esos se recuerda reponer).
    /// Formato: elementos separados por coma o punto y coma. «COS» = todo el grupo; «PEL/TIN» = un
    /// subgrupo; «-COS/MMP» = quita un subgrupo de un grupo entero. Se puede cambiar sin publicar con el
    /// parámetro <c>RecordatorioReposicionConsumibles</c> de «(defecto)».
    /// </summary>
    public class ConsumiblesReposicion
    {
        /// <summary>
        /// Cosmética entera salvo muestras/material promocional, packs regalo y promociones; en accesorios,
        /// desechables, manicura y pedicura; y en peluquería lo que se gasta (acabado, desechables, lavado,
        /// moldeado, tintes, tratamiento) y el utillaje. Carlos (25/09/26): la manicura, la pedicura y el
        /// utillaje también se compran cíclicamente. Fuera: aparatología, cursos, materias primas, peines,
        /// extensiones y pelucas.
        /// </summary>
        public const string POR_DEFECTO = "COS, -COS/MMP, -COS/PRG, -COS/PRO, ACC/002, ACC/005, ACC/006, PEL/ACB, PEL/DES, PEL/LAV, PEL/MYD, PEL/PEL, PEL/TIN, PEL/TRA, PEL/UTJ";

        private readonly HashSet<string> grupos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> subgrupos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> excluidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Definicion { get; }

        private ConsumiblesReposicion(string definicion)
        {
            Definicion = definicion;
        }

        /// <summary>Vacío o sin ningún elemento válido: la lista por defecto.</summary>
        public static ConsumiblesReposicion Leer(string definicion)
        {
            ConsumiblesReposicion resultado = Construir(definicion);
            return resultado.grupos.Any() || resultado.subgrupos.Any()
                ? resultado
                : Construir(POR_DEFECTO);
        }

        private static ConsumiblesReposicion Construir(string definicion)
        {
            var resultado = new ConsumiblesReposicion(definicion?.Trim() ?? string.Empty);
            foreach (string bruto in (definicion ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string elemento = bruto.Trim();
                bool excluir = elemento.StartsWith("-");
                if (excluir)
                {
                    elemento = elemento.Substring(1).Trim();
                }
                if (elemento.Length == 0)
                {
                    continue;
                }
                string[] partes = elemento.Split('/');
                string grupo = partes[0].Trim();
                string subgrupo = partes.Length > 1 ? partes[1].Trim() : null;
                if (grupo.Length == 0)
                {
                    continue;
                }
                if (excluir)
                {
                    if (!string.IsNullOrEmpty(subgrupo))
                    {
                        _ = resultado.excluidos.Add(Clave(grupo, subgrupo));
                    }
                }
                else if (string.IsNullOrEmpty(subgrupo))
                {
                    _ = resultado.grupos.Add(grupo);
                }
                else
                {
                    _ = resultado.subgrupos.Add(Clave(grupo, subgrupo));
                }
            }
            return resultado;
        }

        public bool EsConsumible(string grupo, string subgrupo)
        {
            string g = grupo?.Trim() ?? string.Empty;
            string clave = Clave(g, subgrupo);
            if (excluidos.Contains(clave))
            {
                return false;
            }
            return subgrupos.Contains(clave) || grupos.Contains(g);
        }

        private static string Clave(string grupo, string subgrupo)
            => (grupo?.Trim() ?? string.Empty) + "/" + (subgrupo?.Trim() ?? string.Empty);
    }
}
