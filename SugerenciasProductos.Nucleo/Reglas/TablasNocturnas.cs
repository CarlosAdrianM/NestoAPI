using ModeloSugerenciasProductos.Datos;

namespace ModeloSugerenciasProductos.Reglas;

/// <summary>Una fila de SugerenciasProductosAfinidad: item-kNN de un cliente con un producto que no compra.</summary>
public sealed class AfinidadProducto
{
    public string Cliente { get; set; } = "";
    public string Producto { get; set; } = "";
    public decimal Afinidad { get; set; }
    public short Rango { get; set; }
}

/// <summary>Una fila de SugerenciasProductosPopularidad.</summary>
public sealed class PopularidadDeProducto
{
    public string Producto { get; set; } = "";
    public int Clientes12Meses { get; set; }
    public int Clientes3Meses { get; set; }
    public int Cestas12Meses { get; set; }
    public int Rango { get; set; }
}

/// <summary>
/// NestoAPI#670/#671: lo que calcula de noche el job «sugerencias-productos-pares» y, con lo anterior a cada instantánea, el
/// entrenamiento offline: pares «comprados juntos», afinidad item-kNN y popularidad. Sin BD y deterministas (los empates se
/// resuelven por código).
/// <para><b>Pares</b> (reglas de asociación): una cesta es lo que se ha llevado un cliente en un día. Por cada pareja A→B:
/// VecesJuntos, Soporte (÷ cestas), Confianza (÷ cestas con A) y Lift (Confianza ÷ proporción de cestas con B). Se queda con los
/// que se repiten (≥ <see cref="MINIMO_VECES_JUNTOS"/>), con confianza y lift mínimos, y con los
/// <see cref="MAXIMO_PARES_POR_PRODUCTO"/> mejores de cada A. Las cestas de más de <see cref="MAXIMO_PRODUCTOS_CESTA"/>
/// productos no cuentan.</para>
/// <para><b>Afinidad</b> (item-kNN implícita por coseno, sin Microsoft.ML.Recommender): cada cliente es el conjunto de productos
/// sugeribles que ha comprado. Similitud = clientes comunes ÷ √(clientes de A × clientes de B), con ≥
/// <see cref="MINIMO_CLIENTES_COMUNES"/> comunes y <see cref="VECINOS_POR_PRODUCTO"/> vecinos por producto. Afinidad de un
/// cliente con un producto que NO ha comprado = suma de las similitudes con los que sí compra; los
/// <see cref="MAXIMO_AFINIDADES_POR_CLIENTE"/> primeros. Los clientes con más de <see cref="MAXIMO_PRODUCTOS_CLIENTE"/> productos
/// no cuentan para las similitudes.</para>
/// <para><b>Popularidad</b>: clientes distintos en 12 y 3 meses y cestas en 12 meses.</para>
/// </summary>
public static class TablasNocturnas
{
    public const int MINIMO_VECES_JUNTOS = 5;
    public const decimal MINIMO_CONFIANZA = 0.05m;
    public const decimal MINIMO_LIFT = 1.2m;
    public const int MAXIMO_PARES_POR_PRODUCTO = 20;
    public const int MAXIMO_PRODUCTOS_CESTA = 50;

    public const int MINIMO_CLIENTES_COMUNES = 3;
    public const int VECINOS_POR_PRODUCTO = 30;
    public const int MAXIMO_AFINIDADES_POR_CLIENTE = 50;
    public const int MAXIMO_PRODUCTOS_CLIENTE = 400;

    public const int MESES_POPULARIDAD = 12;
    public const int MESES_TENDENCIA = 3;

    public static List<ParComprado> Pares(IEnumerable<Compra>? compras, Func<string, bool> esSugerible)
    {
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var productos = new List<string>();
        var cestasConProducto = new List<int>();
        var juntos = new Dictionary<long, int>();
        int cestas = 0;

        foreach (IGrouping<string, Compra> cesta in (compras ?? Enumerable.Empty<Compra>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto))
            .GroupBy(c => c.Cliente.Trim().ToUpperInvariant() + "|" + c.Dia.Date.ToString("yyyyMMdd")))
        {
            List<int> enCesta = cesta
                .Select(c => c.Producto.Trim())
                .Where(esSugerible)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p =>
                {
                    if (!indices.TryGetValue(p, out int i))
                    {
                        i = productos.Count;
                        indices[p] = i;
                        productos.Add(p);
                        cestasConProducto.Add(0);
                    }
                    return i;
                })
                .OrderBy(i => i)
                .ToList();
            if (enCesta.Count == 0 || enCesta.Count > MAXIMO_PRODUCTOS_CESTA)
            {
                continue;
            }
            cestas++;
            foreach (int i in enCesta)
            {
                cestasConProducto[i]++;
            }
            for (int x = 0; x < enCesta.Count; x++)
            {
                for (int y = x + 1; y < enCesta.Count; y++)
                {
                    long clave = ((long)enCesta[x] << 32) | (uint)enCesta[y];
                    juntos[clave] = (juntos.TryGetValue(clave, out int n) ? n : 0) + 1;
                }
            }
        }

        var pares = new List<ParComprado>();
        if (cestas == 0)
        {
            return pares;
        }
        foreach (KeyValuePair<long, int> par in juntos.Where(p => p.Value >= MINIMO_VECES_JUNTOS))
        {
            int a = (int)(par.Key >> 32);
            int b = (int)(par.Key & 0xFFFFFFFF);
            AnadirSiVale(pares, productos[a], productos[b], par.Value, cestasConProducto[a], cestasConProducto[b], cestas);
            AnadirSiVale(pares, productos[b], productos[a], par.Value, cestasConProducto[b], cestasConProducto[a], cestas);
        }
        return pares
            .GroupBy(p => p.ProductoA, StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.OrderByDescending(p => p.Confianza).ThenByDescending(p => p.Lift)
                .ThenBy(p => p.ProductoB, StringComparer.OrdinalIgnoreCase).Take(MAXIMO_PARES_POR_PRODUCTO))
            .ToList();
    }

    private static void AnadirSiVale(List<ParComprado> pares, string a, string b, int vecesJuntos, int cestasA, int cestasB, int cestas)
    {
        decimal confianza = (decimal)vecesJuntos / cestasA;
        decimal proporcionB = (decimal)cestasB / cestas;
        decimal lift = proporcionB == 0 ? 0 : confianza / proporcionB;
        if (confianza < MINIMO_CONFIANZA || lift < MINIMO_LIFT)
        {
            return;
        }
        pares.Add(new ParComprado
        {
            ProductoA = a,
            ProductoB = b,
            VecesJuntos = vecesJuntos,
            Soporte = Math.Round((decimal)vecesJuntos / cestas, 6),
            Confianza = Math.Round(confianza, 6),
            Lift = Math.Round(lift, 4)
        });
    }

    public static List<AfinidadProducto> Afinidades(IEnumerable<Compra>? compras, Func<string, bool> esSugerible)
    {
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var productos = new List<string>();
        var clientes = new SortedDictionary<string, SortedSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (Compra c in compras ?? Enumerable.Empty<Compra>())
        {
            if (string.IsNullOrWhiteSpace(c.Cliente) || string.IsNullOrWhiteSpace(c.Producto))
            {
                continue;
            }
            string producto = c.Producto.Trim();
            if (!indices.TryGetValue(producto, out int i))
            {
                if (!esSugerible(producto))
                {
                    continue;
                }
                i = productos.Count;
                indices[producto] = i;
                productos.Add(producto);
            }
            string cliente = c.Cliente.Trim();
            if (!clientes.TryGetValue(cliente, out SortedSet<int>? conjunto))
            {
                conjunto = new SortedSet<int>();
                clientes[cliente] = conjunto;
            }
            _ = conjunto.Add(i);
        }

        List<Vecino>[] vecinos = Vecinos(clientes.Values.Where(s => s.Count <= MAXIMO_PRODUCTOS_CLIENTE).Select(s => s.ToArray()), productos);

        var resultado = new List<AfinidadProducto>();
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
                resultado.Add(new AfinidadProducto
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

    /// <summary>Popularidad con lo comprado en (hoy − 12 meses, hoy] (incluido hoy).</summary>
    public static List<PopularidadDeProducto> Popularidad(IEnumerable<Compra>? compras, Func<string, bool> esSugerible, DateTime hoy)
    {
        DateTime desde12 = hoy.Date.AddMonths(-MESES_POPULARIDAD);
        DateTime desde3 = hoy.Date.AddMonths(-MESES_TENDENCIA);
        DateTime hasta = hoy.Date.AddDays(1);
        var clientes12 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var clientes3 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var cestas12 = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var sugeribles = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (Compra c in compras ?? Enumerable.Empty<Compra>())
        {
            if (string.IsNullOrWhiteSpace(c.Cliente) || string.IsNullOrWhiteSpace(c.Producto) || c.Dia < desde12 || c.Dia >= hasta)
            {
                continue;
            }
            string producto = c.Producto.Trim();
            if (!sugeribles.TryGetValue(producto, out bool vale))
            {
                vale = esSugerible(producto);
                sugeribles[producto] = vale;
            }
            if (!vale)
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
            .ToList()
            .Select(p => new PopularidadDeProducto
            {
                Producto = p.Key,
                Clientes12Meses = p.Value.Count,
                Clientes3Meses = clientes3.TryGetValue(p.Key, out HashSet<string>? c3) ? c3.Count : 0,
                Cestas12Meses = cestas12[p.Key].Count,
                Rango = ++rango
            })
            .ToList();
    }

    private static void Anadir(Dictionary<string, HashSet<string>> mapa, string clave, string valor)
    {
        if (!mapa.TryGetValue(clave, out HashSet<string>? conjunto))
        {
            conjunto = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            mapa[clave] = conjunto;
        }
        _ = conjunto.Add(valor);
    }
}
