using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#670: lo que calcula de noche el job «sugerencias-productos-pares», sin BD.
    /// <para><b>Pares</b> (reglas de asociación): una cesta es lo que se ha llevado un cliente en un día (albaranes de 24 meses).
    /// Por cada pareja A→B: VecesJuntos (cestas con los dos), Soporte (VecesJuntos ÷ cestas), Confianza (VecesJuntos ÷ cestas
    /// con A) y Lift (Confianza ÷ proporción de cestas con B). Se queda con los pares que se repiten (al menos
    /// <see cref="MINIMO_VECES_JUNTOS"/> cestas), con confianza y lift mínimos, y con los <see cref="MAXIMO_PARES_POR_PRODUCTO"/>
    /// mejores de cada A. Las cestas de más de <see cref="MAXIMO_PRODUCTOS_CESTA"/> productos (aperturas, reposiciones de una
    /// tienda) no cuentan: meterían ruido y n² parejas.</para>
    /// <para><b>Perfiles</b>: el perfil de cada cliente (<see cref="MotorSugerenciasProductos.Perfil"/>) y, por perfil, cuántos
    /// clientes se han llevado cada producto en <see cref="MESES_PERFIL"/> meses (los <see cref="MAXIMO_PRODUCTOS_POR_PERFIL"/>
    /// primeros, con al menos <see cref="MINIMO_CLIENTES_PERFIL"/> clientes).</para>
    /// </summary>
    public static class CalculadoraParesCompradosJuntos
    {
        public const int MINIMO_VECES_JUNTOS = 5;
        public const decimal MINIMO_CONFIANZA = 0.05m;
        public const decimal MINIMO_LIFT = 1.2m;
        public const int MAXIMO_PARES_POR_PRODUCTO = 20;
        public const int MAXIMO_PRODUCTOS_CESTA = 50;
        public const int MESES_PERFIL = 12;
        public const int MINIMO_CLIENTES_PERFIL = 2;
        public const int MAXIMO_PRODUCTOS_POR_PERFIL = 500;

        public static List<ParCompradoJunto> Pares(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var productos = new List<string>();
            var cestasConProducto = new List<int>();
            var juntos = new Dictionary<long, int>();
            int cestas = 0;

            foreach (IGrouping<string, CompraDiaReposicion> cesta in (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto))
                .GroupBy(c => c.Cliente.Trim().ToUpperInvariant() + "|" + c.Dia.Date.ToString("yyyyMMdd")))
            {
                List<int> enCesta = cesta
                    .Select(c => c.Producto.Trim())
                    .Where(p => fichas != null && fichas.TryGetValue(p, out FichaProductoSugerencia f) && MotorSugerenciasProductos.EsSugerible(f))
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

            var pares = new List<ParCompradoJunto>();
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

        private static void AnadirSiVale(List<ParCompradoJunto> pares, string a, string b, int vecesJuntos, int cestasA, int cestasB, int cestas)
        {
            decimal confianza = (decimal)vecesJuntos / cestasA;
            decimal proporcionB = (decimal)cestasB / cestas;
            decimal lift = proporcionB == 0 ? 0 : confianza / proporcionB;
            if (confianza < MINIMO_CONFIANZA || lift < MINIMO_LIFT)
            {
                return;
            }
            pares.Add(new ParCompradoJunto
            {
                ProductoA = a,
                ProductoB = b,
                VecesJuntos = vecesJuntos,
                Soporte = Math.Round((decimal)vecesJuntos / cestas, 6),
                Confianza = Math.Round(confianza, 6),
                Lift = Math.Round(lift, 4)
            });
        }

        public static List<ProductoPerfil> PerfilesProductos(IEnumerable<CompraDiaReposicion> compras,
            IDictionary<string, FichaProductoSugerencia> fichas, DateTime hoy)
        {
            DateTime desde = hoy.Date.AddMonths(-MESES_PERFIL);
            var cuenta = new Dictionary<string, ProductoPerfil>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, CompraDiaReposicion> cliente in (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto))
                .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                Tuple<string, string> perfil = MotorSugerenciasProductos.Perfil(cliente, fichas);
                if (perfil == null)
                {
                    continue;
                }
                foreach (string producto in cliente
                    .Where(c => c.Dia >= desde)
                    .Select(c => c.Producto.Trim())
                    .Where(p => fichas.TryGetValue(p, out FichaProductoSugerencia f) && MotorSugerenciasProductos.EsSugerible(f))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    string clave = perfil.Item1 + "|" + perfil.Item2 + "|" + producto;
                    if (!cuenta.TryGetValue(clave, out ProductoPerfil fila))
                    {
                        fila = new ProductoPerfil { Grupo = perfil.Item1, SubGrupo = perfil.Item2, Producto = producto };
                        cuenta[clave] = fila;
                    }
                    fila.Clientes++;
                }
            }
            return cuenta.Values
                .Where(f => f.Clientes >= MINIMO_CLIENTES_PERFIL)
                .GroupBy(f => f.Grupo + "|" + f.SubGrupo)
                .SelectMany(g => g.OrderByDescending(f => f.Clientes).ThenBy(f => f.Producto, StringComparer.OrdinalIgnoreCase)
                    .Take(MAXIMO_PRODUCTOS_POR_PERFIL))
                .ToList();
        }
    }
}
