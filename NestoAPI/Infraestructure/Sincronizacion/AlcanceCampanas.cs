using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#423: a qué productos alcanza una fila de campaña. Lo necesitan dos sitios y tiene
    /// que ser LA MISMA respuesta en los dos:
    ///
    ///   - el job nocturno (<see cref="VigenciaCampanasJobsService"/>), cuando una campaña empieza
    ///     o termina por fecha;
    ///   - el mantenimiento de campañas (<c>CampanasController</c>), cuando alguien la crea,
    ///     cambia o borra a mano y hay que republicar en el momento, sin esperar a la madrugada.
    ///
    /// Si divergieran, una campaña de marca republicaría 62 referencias al guardarla y solo 40 al
    /// caducar (o al revés), y la tienda se quedaría con productos descuadrados sin que nadie
    /// entienda por qué.
    /// </summary>
    internal static class AlcanceCampanas
    {
        /// <summary>
        /// Los números de producto (recortados, como los guarda Nesto_sync) a los que alcanzan
        /// estas filas de campaña. Las de producto se leen tal cual; las de familia y las de
        /// familia+grupo se expanden preguntando qué referencias VIVAS tiene esa marca hoy.
        ///
        /// El filtro de estado va solo en la expansión, no en el nivel de producto: una fila de
        /// producto la marca alguien a mano —si la marcó, sabrá por qué—, mientras que la familia
        /// se expande sola, y republicar las referencias muertas de una marca grande son 3 stocks
        /// y 2 llamadas HTTP cada una para no cambiar nada que se pueda comprar.
        /// </summary>
        internal static async Task<List<string>> ProductosAfectados(NVEntities db, List<DescuentosProducto> filas)
        {
            var productos = new HashSet<string>();
            if (filas == null || !filas.Any())
            {
                return productos.ToList();
            }

            foreach (DescuentosProducto fila in filas.Where(f => f.Familia == null && f.Nº_Producto != null))
            {
                string numero = fila.Nº_Producto.Trim();
                if (numero != string.Empty)
                {
                    _ = productos.Add(numero);
                }
            }

            List<DescuentosProducto> deFamilia = filas.Where(f => f.Familia != null).ToList();
            if (!deFamilia.Any())
            {
                return productos.ToList();
            }

            // Los char van SIN recortar en el Contains: DescuentosProducto.Familia y
            // Productos.Familia son los dos char(10), así que el relleno casa y SQL Server además
            // ignora los espacios finales al comparar.
            List<string> familias = deFamilia.Select(f => f.Familia).Distinct().ToList();

            var candidatos = await db.Productos
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && p.Estado >= 0
                    && familias.Contains(p.Familia))
                .Select(p => new { p.Número, p.Familia, p.Grupo })
                .ToListAsync().ConfigureAwait(false);

            foreach (DescuentosProducto fila in deFamilia)
            {
                string familia = fila.Familia.Trim();
                string grupo = fila.GrupoProducto?.Trim();

                foreach (var candidato in candidatos)
                {
                    if (candidato.Familia?.Trim() != familia)
                    {
                        continue;
                    }
                    // Una fila de familia+grupo solo alcanza a los productos de ESE grupo: es el
                    // nivel 5 del motor de precios, que exige las dos cosas a la vez.
                    if (grupo != null && candidato.Grupo?.Trim() != grupo)
                    {
                        continue;
                    }
                    string numero = candidato.Número?.Trim();
                    if (!string.IsNullOrEmpty(numero))
                    {
                        _ = productos.Add(numero);
                    }
                }
            }

            return productos.ToList();
        }
    }
}
