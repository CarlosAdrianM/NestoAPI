using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: criterio del recordatorio de reposición, sin BD (todo en memoria y testeable).
    ///
    /// Para cada par cliente-producto con al menos <see cref="MINIMO_COMPRAS"/> días de compra (albarán,
    /// cualquier canal) en los últimos <see cref="MESES_HISTORIAL"/> meses:
    /// - intervalo = mediana de los días entre compras consecutivas (nunca menos de
    ///   <see cref="INTERVALO_MINIMO_DIAS"/>);
    /// - toca reponer si desde la última compra han pasado intervalo × 1,25 días o más, pero menos de
    ///   intervalo × 3 (más allá es un cliente que se ha ido, y eso es otro correo).
    ///
    /// De los que tocan, se quedan fuera CON MOTIVO (para revisarlo en el modo sombra): producto de baja
    /// o ficticio, sin stock, con un pedido pendiente o una nota de entrega posterior, o con una compra
    /// posterior de un sustituto (misma marca, grupo y subgrupo). Solo productos consumibles
    /// (<see cref="ConsumiblesReposicion"/>). Como mucho <see cref="MAXIMO_PRODUCTOS_POR_CORREO"/>
    /// productos por correo, los de más importe habitual, y un correo por dirección.
    /// </summary>
    public static class CalculadoraReposicion
    {
        public const int MESES_HISTORIAL = 24;
        public const int MINIMO_COMPRAS = 3;
        public const double FACTOR_AVISO = 1.25;
        public const double FACTOR_ABANDONO = 3;
        public const int INTERVALO_MINIMO_DIAS = 14;
        public const int MAXIMO_PRODUCTOS_POR_CORREO = 3;
        public const int PORCENTAJE_GRUPO_CONTROL = 10;

        public const string MOTIVO_PRODUCTO_BAJA = "Producto dado de baja o ficticio.";
        public const string MOTIVO_SIN_STOCK = "Producto sin stock disponible.";
        public const string MOTIVO_PEDIDO_PENDIENTE = "Ya lo tiene en un pedido pendiente de servir.";
        public const string MOTIVO_NOTA_ENTREGA = "Se le ha entregado con una nota de entrega después de la última compra.";
        public const string MOTIVO_SUSTITUTO = "Después ha comprado un producto equivalente (misma marca y subgrupo): ";
        public const string MOTIVO_SUSTITUTO_PENDIENTE = "Tiene pendiente un producto equivalente (misma marca y subgrupo): ";
        public const string MOTIVO_CLIENTE_NO_ENCONTRADO = "No se encuentra la ficha principal del cliente.";
        public const string MOTIVO_CLIENTE_BAJA = "Cliente de baja (estado 8).";
        public const string MOTIVO_SIN_CORREO = "El cliente no tiene correo electrónico.";
        public const string MOTIVO_MAXIMO_PRODUCTOS = "Ya van 3 productos en su correo (se eligen los de más importe).";
        public const string MOTIVO_CORREO_DUPLICADO = "Mismo correo que otro cliente al que ya se escribe: ";

        public static DateTime HistorialDesde(DateTime hoy) => hoy.Date.AddMonths(-MESES_HISTORIAL);

        /// <summary>
        /// Mediana de los días entre compras consecutivas (días distintos). Null si no hay
        /// <see cref="MINIMO_COMPRAS"/> días de compra.
        /// </summary>
        public static double? IntervaloMediano(IEnumerable<DateTime> dias)
        {
            List<DateTime> ordenados = (dias ?? Enumerable.Empty<DateTime>())
                .Select(d => d.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();
            if (ordenados.Count < MINIMO_COMPRAS)
            {
                return null;
            }
            List<double> intervalos = ordenados
                .Zip(ordenados.Skip(1), (a, b) => (b - a).TotalDays)
                .OrderBy(x => x)
                .ToList();
            int n = intervalos.Count;
            return n % 2 == 1
                ? intervalos[n / 2]
                : (intervalos[(n / 2) - 1] + intervalos[n / 2]) / 2.0;
        }

        public static bool TocaReponer(double intervaloDias, int diasDesdeUltimaCompra)
            => diasDesdeUltimaCompra >= intervaloDias * FACTOR_AVISO
                && diasDesdeUltimaCompra < intervaloDias * FACTOR_ABANDONO;

        /// <summary>
        /// Los pares cliente-producto a los que hoy les toca reponer, con Motivo null si se avisarían.
        /// Los que no tocan (o no son consumibles, o no tienen ritmo propio) ni aparecen.
        /// </summary>
        public static List<CandidatoReposicionDTO> EvaluarPares(IEnumerable<CompraDiaReposicion> compras,
            IEnumerable<LineaPendienteReposicion> pendientes,
            IDictionary<string, ProductoReposicion> productos,
            ConsumiblesReposicion consumibles,
            DateTime hoy)
        {
            DateTime fechaHoy = hoy.Date;
            DateTime desde = HistorialDesde(fechaHoy);
            var comprasValidas = (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Cliente) && !string.IsNullOrWhiteSpace(c.Producto)
                    && c.Dia.Date >= desde && c.Dia.Date <= fechaHoy)
                .Select(c => new CompraDiaReposicion
                {
                    Cliente = c.Cliente.Trim(),
                    Producto = c.Producto.Trim(),
                    Dia = c.Dia.Date,
                    BaseImponible = c.BaseImponible
                })
                .ToList();

            Dictionary<string, List<CompraDiaReposicion>> comprasPorCliente = comprasValidas
                .GroupBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<LineaPendienteReposicion>> pendientesPorCliente = (pendientes ?? Enumerable.Empty<LineaPendienteReposicion>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Cliente) && !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var resultado = new List<CandidatoReposicionDTO>();
            foreach (var par in comprasValidas.GroupBy(c => new { Cliente = c.Cliente.ToUpperInvariant(), Producto = c.Producto.ToUpperInvariant() }))
            {
                CompraDiaReposicion primera = par.First();
                if (!productos.TryGetValue(primera.Producto, out ProductoReposicion producto)
                    || !consumibles.EsConsumible(producto.Grupo, producto.SubGrupo))
                {
                    continue;
                }

                List<DateTime> dias = par.Select(c => c.Dia).Distinct().OrderBy(d => d).ToList();
                double? mediana = IntervaloMediano(dias);
                if (mediana == null)
                {
                    continue;
                }
                double intervalo = Math.Max(mediana.Value, INTERVALO_MINIMO_DIAS);
                DateTime ultima = dias.Last();
                int diasDesde = (int)(fechaHoy - ultima).TotalDays;
                if (!TocaReponer(intervalo, diasDesde))
                {
                    continue;
                }

                var candidato = new CandidatoReposicionDTO
                {
                    Cliente = primera.Cliente,
                    Producto = producto.Producto?.Trim() ?? primera.Producto,
                    NombreProducto = producto.Nombre?.Trim(),
                    NumeroCompras = dias.Count,
                    PrimeraCompra = dias.First(),
                    UltimaCompra = ultima,
                    IntervaloDias = (int)Math.Round(intervalo, MidpointRounding.AwayFromZero),
                    DiasDesdeUltimaCompra = diasDesde,
                    ImporteHabitual = RoundingHelper.Round(par.Sum(c => c.BaseImponible) / dias.Count, 2)
                };

                comprasPorCliente.TryGetValue(primera.Cliente, out List<CompraDiaReposicion> comprasCliente);
                pendientesPorCliente.TryGetValue(primera.Cliente, out List<LineaPendienteReposicion> pendientesCliente);
                candidato.Motivo = MotivoExclusion(producto, ultima, comprasCliente, pendientesCliente, productos);
                resultado.Add(candidato);
            }
            return resultado;
        }

        internal static string MotivoExclusion(ProductoReposicion producto, DateTime ultimaCompra,
            List<CompraDiaReposicion> comprasCliente, List<LineaPendienteReposicion> pendientesCliente,
            IDictionary<string, ProductoReposicion> productos)
        {
            if (producto.Estado < 0 || producto.Ficticio)
            {
                return MOTIVO_PRODUCTO_BAJA;
            }

            string id = producto.Producto?.Trim() ?? string.Empty;
            foreach (LineaPendienteReposicion linea in pendientesCliente ?? new List<LineaPendienteReposicion>())
            {
                if (!string.Equals(linea.Producto?.Trim(), id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (EsPedidoPendiente(linea))
                {
                    return MOTIVO_PEDIDO_PENDIENTE;
                }
                if (EsNotaEntregaPosterior(linea, ultimaCompra))
                {
                    return MOTIVO_NOTA_ENTREGA;
                }
            }

            CompraDiaReposicion sustitutoComprado = (comprasCliente ?? new List<CompraDiaReposicion>())
                .Where(c => c.Dia.Date > ultimaCompra.Date
                    && !string.Equals(c.Producto?.Trim(), id, StringComparison.OrdinalIgnoreCase)
                    && EsSustituto(producto, c.Producto, productos))
                .OrderBy(c => c.Dia)
                .FirstOrDefault();
            if (sustitutoComprado != null)
            {
                return MOTIVO_SUSTITUTO + sustitutoComprado.Producto.Trim();
            }

            LineaPendienteReposicion sustitutoPendiente = (pendientesCliente ?? new List<LineaPendienteReposicion>())
                .Where(l => !string.Equals(l.Producto?.Trim(), id, StringComparison.OrdinalIgnoreCase)
                    && (EsPedidoPendiente(l) || EsNotaEntregaPosterior(l, ultimaCompra))
                    && EsSustituto(producto, l.Producto, productos))
                .FirstOrDefault();
            if (sustitutoPendiente != null)
            {
                return MOTIVO_SUSTITUTO_PENDIENTE + sustitutoPendiente.Producto.Trim();
            }

            return null;
        }

        private static bool EsPedidoPendiente(LineaPendienteReposicion linea)
            => linea.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && linea.Estado <= Constantes.EstadosLineaVenta.EN_CURSO;

        private static bool EsNotaEntregaPosterior(LineaPendienteReposicion linea, DateTime ultimaCompra)
            => linea.Estado == Constantes.EstadosLineaVenta.NOTA_ENTREGA && linea.Fecha.Date >= ultimaCompra.Date;

        /// <summary>
        /// Sustituto = otro producto de la misma marca (familia), grupo y subgrupo: otro formato o una
        /// variante del mismo artículo. Si alguno de los tres falta, no se da por sustituto.
        /// </summary>
        internal static bool EsSustituto(ProductoReposicion producto, string otroId, IDictionary<string, ProductoReposicion> productos)
        {
            if (string.IsNullOrWhiteSpace(otroId) || !productos.TryGetValue(otroId.Trim(), out ProductoReposicion otro))
            {
                return false;
            }
            return Iguales(producto.Familia, otro.Familia)
                && Iguales(producto.Grupo, otro.Grupo)
                && Iguales(producto.SubGrupo, otro.SubGrupo);
        }

        private static bool Iguales(string a, string b)
            => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Marca con <see cref="MOTIVO_SIN_STOCK"/> los que se avisarían y cuyo producto está en
        /// <paramref name="productosSinStock"/>.
        /// </summary>
        public static void AplicarSinStock(IEnumerable<CandidatoReposicionDTO> candidatos, ISet<string> productosSinStock)
        {
            if (productosSinStock == null || productosSinStock.Count == 0)
            {
                return;
            }
            foreach (CandidatoReposicionDTO candidato in candidatos.Where(c => c.SeAvisaria))
            {
                if (productosSinStock.Contains(candidato.Producto?.Trim() ?? string.Empty))
                {
                    candidato.Motivo = MOTIVO_SIN_STOCK;
                }
            }
        }

        /// <summary>
        /// Sin stock = lo que hay en las sedes, menos lo pendiente de entregar, más lo que viene por
        /// traspaso, no llega a una unidad (la misma cuenta que el «rojo» de la plantilla de venta).
        /// </summary>
        public static HashSet<string> ProductosSinStock(ResumenStocksProductos resumen, IEnumerable<string> productos)
        {
            var sinStock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (resumen == null)
            {
                return sinStock;
            }
            foreach (string producto in productos ?? Enumerable.Empty<string>())
            {
                string clave = ResumenStocksProductos.Clave(producto);
                int disponible = ResumenStocksProductos.Valor(resumen.StockSedes, clave)
                    - ResumenStocksProductos.Valor(resumen.PendienteEntregarTotal, clave)
                    + ResumenStocksProductos.Valor(resumen.PendienteReposicion, clave);
                if (disponible <= 0)
                {
                    _ = sinStock.Add(producto.Trim());
                }
            }
            return sinStock;
        }

        /// <summary>
        /// Agrupa los que se avisarían en un correo por cliente (hasta 3 productos, los de más importe
        /// habitual), aparta el grupo de control y deja en <c>Descartes</c> todo lo que se queda fuera.
        /// </summary>
        public static ResultadoRecordatorioReposicionDTO Agrupar(List<CandidatoReposicionDTO> candidatos,
            IDictionary<string, ClienteReposicion> clientes, DateTime hoy, string consumibles = null)
        {
            var resultado = new ResultadoRecordatorioReposicionDTO
            {
                Fecha = hoy.Date,
                HistorialDesde = HistorialDesde(hoy),
                Consumibles = consumibles
            };
            resultado.Descartes.AddRange(candidatos.Where(c => !c.SeAvisaria));

            var correos = new List<RecordatorioReposicionClienteDTO>();
            foreach (var grupo in candidatos.Where(c => c.SeAvisaria).GroupBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase))
            {
                List<CandidatoReposicionDTO> delCliente = grupo
                    .OrderByDescending(c => c.ImporteHabitual)
                    .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string motivoCliente = null;
                if (!clientes.TryGetValue(grupo.Key, out ClienteReposicion cliente) || cliente == null)
                {
                    motivoCliente = MOTIVO_CLIENTE_NO_ENCONTRADO;
                }
                else if (cliente.Estado == 8)
                {
                    motivoCliente = MOTIVO_CLIENTE_BAJA;
                }
                else if (string.IsNullOrWhiteSpace(cliente.Email))
                {
                    motivoCliente = MOTIVO_SIN_CORREO;
                }

                if (motivoCliente != null)
                {
                    delCliente.ForEach(c => c.Motivo = motivoCliente);
                    resultado.Descartes.AddRange(delCliente);
                    continue;
                }

                foreach (CandidatoReposicionDTO sobrante in delCliente.Skip(MAXIMO_PRODUCTOS_POR_CORREO))
                {
                    sobrante.Motivo = MOTIVO_MAXIMO_PRODUCTOS;
                    resultado.Descartes.Add(sobrante);
                }

                correos.Add(new RecordatorioReposicionClienteDTO
                {
                    Cliente = cliente.Cliente?.Trim() ?? grupo.Key,
                    Nombre = cliente.Nombre?.Trim(),
                    Email = cliente.Email.Trim(),
                    VendedorNombre = NombreVendedor(cliente),
                    GrupoControl = EsGrupoControl(grupo.Key),
                    Productos = delCliente.Take(MAXIMO_PRODUCTOS_POR_CORREO).ToList()
                });
            }

            // Un correo por dirección: si varios clientes comparten correo, se queda el de más productos.
            foreach (var mismoCorreo in correos.GroupBy(c => c.Email.ToLowerInvariant()))
            {
                List<RecordatorioReposicionClienteDTO> ordenados = mismoCorreo
                    .OrderByDescending(c => c.Productos.Count)
                    .ThenByDescending(c => c.Productos.Sum(p => p.ImporteHabitual))
                    .ThenBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                RecordatorioReposicionClienteDTO elegido = ordenados.First();
                foreach (RecordatorioReposicionClienteDTO repetido in ordenados.Skip(1))
                {
                    repetido.Productos.ForEach(p => p.Motivo = MOTIVO_CORREO_DUPLICADO + elegido.Cliente);
                    resultado.Descartes.AddRange(repetido.Productos);
                }
                if (elegido.GrupoControl)
                {
                    resultado.GrupoControl.Add(elegido);
                }
                else
                {
                    resultado.SeAvisarian.Add(elegido);
                }
            }

            resultado.SeAvisarian = resultado.SeAvisarian.OrderBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase).ToList();
            resultado.GrupoControl = resultado.GrupoControl.OrderBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase).ToList();
            resultado.Descartes = resultado.Descartes
                .OrderBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return resultado;
        }

        /// <summary>El vendedor general («NV») no es una persona a la que pedirle nada.</summary>
        internal static string NombreVendedor(ClienteReposicion cliente)
        {
            if (string.IsNullOrWhiteSpace(cliente?.Vendedor)
                || string.Equals(cliente.Vendedor.Trim(), Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            string nombre = cliente.VendedorNombre?.Trim();
            return string.IsNullOrWhiteSpace(nombre) ? null : nombre;
        }

        /// <summary>
        /// Grupo de control estable: el mismo cliente cae siempre del mismo lado (hash FNV-1a del
        /// número de cliente, no <c>GetHashCode</c>, que cambia entre procesos de 32 y 64 bits).
        /// </summary>
        public static bool EsGrupoControl(string cliente, int porcentaje = PORCENTAJE_GRUPO_CONTROL)
        {
            if (porcentaje <= 0 || string.IsNullOrWhiteSpace(cliente))
            {
                return false;
            }
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in cliente.Trim().ToUpperInvariant())
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return hash % 100 < porcentaje;
            }
        }
    }
}
