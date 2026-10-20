using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#671 (corte 2a): lo que calcula de noche el job «sugerencias-productos-pares» para la cuarta fuente y el
    /// modelo, sin BD y en C# puro (sin Microsoft.ML.Recommender), en la misma pasada que los pares.
    /// <para><b>Afinidad</b> (item-kNN implícita por coseno): cada cliente es el conjunto de productos sugeribles que ha
    /// comprado en el periodo. La similitud entre dos productos es clientes comunes ÷ √(clientes de A × clientes de B), con al
    /// menos <see cref="MINIMO_CLIENTES_COMUNES"/> clientes comunes, y cada producto se queda con sus
    /// <see cref="VECINOS_POR_PRODUCTO"/> vecinos más parecidos. La afinidad de un cliente con un producto que NO ha comprado
    /// es la suma de las similitudes de ese producto con los que sí compra; se guardan los
    /// <see cref="MAXIMO_AFINIDADES_POR_CLIENTE"/> primeros. Los clientes con más de <see cref="MAXIMO_PRODUCTOS_CLIENTE"/>
    /// productos (distribuidores, tiendas) no cuentan para las similitudes: meterían ruido y n² parejas.</para>
    /// <para><b>Popularidad</b>: clientes distintos en 12 y en 3 meses y cestas en 12 meses, por producto.</para>
    /// <para>Determinista: los empates se resuelven siempre por el código del producto o del cliente.</para>
    /// </summary>
    public static class CalculadoraAfinidadProductos
    {
        public const int MINIMO_CLIENTES_COMUNES = 3;
        public const int VECINOS_POR_PRODUCTO = 30;
        public const int MAXIMO_AFINIDADES_POR_CLIENTE = 50;
        public const int MAXIMO_PRODUCTOS_CLIENTE = 400;
        public const int MESES_POPULARIDAD = 12;
        public const int MESES_TENDENCIA = 3;

        public static List<AfinidadCliente> Afinidades(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            // Conjunto de productos de cada cliente (índices), en orden de cliente para que todo sea determinista
            var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var productos = new List<string>();
            var clientes = new SortedDictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (CompraDiaReposicion c in compras ?? Enumerable.Empty<CompraDiaReposicion>())
            {
                if (string.IsNullOrWhiteSpace(c.Cliente) || string.IsNullOrWhiteSpace(c.Producto))
                {
                    continue;
                }
                string producto = c.Producto.Trim();
                if (fichas == null || !fichas.TryGetValue(producto, out FichaProductoSugerencia f) || !MotorSugerenciasProductos.EsSugerible(f))
                {
                    continue;
                }
                if (!indices.TryGetValue(producto, out int i))
                {
                    i = productos.Count;
                    indices[producto] = i;
                    productos.Add(producto);
                }
                string cliente = c.Cliente.Trim();
                if (!clientes.TryGetValue(cliente, out SortedSet<int> conjunto))
                {
                    conjunto = new SortedSet<int>();
                    clientes[cliente] = conjunto;
                }
                _ = conjunto.Add(i);
            }

            List<Vecino>[] vecinos = Vecinos(clientes.Values.Where(s => s.Count <= MAXIMO_PRODUCTOS_CLIENTE).Select(s => s.ToArray()), productos);

            var resultado = new List<AfinidadCliente>();
            foreach (KeyValuePair<string, SortedSet<int>> cliente in clientes)
            {
                var suma = new Dictionary<int, double>();
                foreach (int q in cliente.Value)
                {
                    foreach (Vecino v in vecinos[q])
                    {
                        if (!cliente.Value.Contains(v.Producto))
                        {
                            suma[v.Producto] = (suma.TryGetValue(v.Producto, out double s) ? s : 0) + v.Similitud;
                        }
                    }
                }
                short rango = 0;
                foreach (KeyValuePair<int, double> p in suma
                    .OrderByDescending(p => p.Value)
                    .ThenBy(p => productos[p.Key], StringComparer.OrdinalIgnoreCase)
                    .Take(MAXIMO_AFINIDADES_POR_CLIENTE))
                {
                    resultado.Add(new AfinidadCliente
                    {
                        Cliente = cliente.Key,
                        Producto = productos[p.Key],
                        Afinidad = Math.Round((decimal)p.Value, 6),
                        Rango = ++rango
                    });
                }
            }
            return resultado;
        }

        internal struct Vecino
        {
            public int Producto;
            public double Similitud;
        }

        /// <summary>Los vecinos (similitud coseno) de cada producto, de más a menos parecido.</summary>
        internal static List<Vecino>[] Vecinos(IEnumerable<int[]> conjuntos, IList<string> productos)
        {
            int n = productos.Count;
            var clientesProducto = new int[n];
            var comunes = new Dictionary<long, int>();
            foreach (int[] conjunto in conjuntos)
            {
                foreach (int i in conjunto)
                {
                    clientesProducto[i]++;
                }
                for (int x = 0; x < conjunto.Length; x++)
                {
                    for (int y = x + 1; y < conjunto.Length; y++)
                    {
                        int a = Math.Min(conjunto[x], conjunto[y]);
                        int b = Math.Max(conjunto[x], conjunto[y]);
                        long clave = ((long)a << 32) | (uint)b;
                        comunes[clave] = (comunes.TryGetValue(clave, out int c) ? c : 0) + 1;
                    }
                }
            }
            var candidatos = new List<Vecino>[n];
            for (int i = 0; i < n; i++)
            {
                candidatos[i] = new List<Vecino>();
            }
            foreach (KeyValuePair<long, int> par in comunes)
            {
                if (par.Value < MINIMO_CLIENTES_COMUNES)
                {
                    continue;
                }
                int a = (int)(par.Key >> 32);
                int b = (int)(par.Key & 0xFFFFFFFF);
                double similitud = par.Value / Math.Sqrt((double)clientesProducto[a] * clientesProducto[b]);
                candidatos[a].Add(new Vecino { Producto = b, Similitud = similitud });
                candidatos[b].Add(new Vecino { Producto = a, Similitud = similitud });
            }
            for (int i = 0; i < n; i++)
            {
                candidatos[i] = candidatos[i]
                    .OrderByDescending(v => v.Similitud)
                    .ThenBy(v => productos[v.Producto], StringComparer.OrdinalIgnoreCase)
                    .Take(VECINOS_POR_PRODUCTO)
                    .ToList();
            }
            return candidatos;
        }

        public static List<PopularidadProducto> Popularidad(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas, DateTime hoy)
        {
            DateTime desde12 = hoy.Date.AddMonths(-MESES_POPULARIDAD);
            DateTime desde3 = hoy.Date.AddMonths(-MESES_TENDENCIA);
            var clientes12 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var clientes3 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var cestas12 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (CompraDiaReposicion c in compras ?? Enumerable.Empty<CompraDiaReposicion>())
            {
                if (string.IsNullOrWhiteSpace(c.Cliente) || string.IsNullOrWhiteSpace(c.Producto) || c.Dia < desde12 || c.Dia >= hoy.Date.AddDays(1))
                {
                    continue;
                }
                string producto = c.Producto.Trim();
                if (fichas == null || !fichas.TryGetValue(producto, out FichaProductoSugerencia f) || !MotorSugerenciasProductos.EsSugerible(f))
                {
                    continue;
                }
                string cliente = c.Cliente.Trim().ToUpperInvariant();
                Anadir(clientes12, producto, cliente);
                Anadir(cestas12, producto, cliente + "|" + c.Dia.Date.ToString("yyyyMMdd"));
                if (c.Dia >= desde3)
                {
                    Anadir(clientes3, producto, cliente);
                }
            }
            int rango = 0;
            return clientes12
                .OrderByDescending(p => p.Value.Count)
                .ThenByDescending(p => cestas12[p.Key].Count)
                .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => new PopularidadProducto
                {
                    Producto = p.Key,
                    Clientes12Meses = p.Value.Count,
                    Clientes3Meses = clientes3.TryGetValue(p.Key, out HashSet<string> c3) ? c3.Count : 0,
                    Cestas12Meses = cestas12[p.Key].Count,
                    Rango = ++rango
                })
                .ToList();
        }

        private static void Anadir(Dictionary<string, HashSet<string>> mapa, string clave, string valor)
        {
            if (!mapa.TryGetValue(clave, out HashSet<string> conjunto))
            {
                conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                mapa[clave] = conjunto;
            }
            _ = conjunto.Add(valor);
        }
    }
}
