using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
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
    ///   intervalo × <see cref="FACTOR_ABANDONO"/> (más allá es un cliente que se ha ido, y eso es otro correo).
    ///   NestoAPI#736: el factor era 3 (con ritmos de 120-190 días llegaba a 14-19 meses); ahora 2. Vale para el correo, la
    ///   push y los habituales de la app (mismo criterio); NO para «le toca» de las sugerencias ni su entrenamiento, que
    ///   tienen su propio factor en el núcleo (ReglasSugerencias.FACTOR_ABANDONO).
    /// - NestoAPI#736: además, en el recordatorio (correo y push, no en los habituales) un tope absoluto de días desde la
    ///   última compra (<see cref="AplicarTope"/>, parámetro RecordatorioReposicionTopeDias).
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
        /// <summary>NestoAPI#736 (Carlos 29/10/26): 3 → 2.</summary>
        public const double FACTOR_ABANDONO = 2;
        /// <summary>NestoAPI#736: tope de días desde la última compra del recordatorio sin fila de RecordatorioReposicionTopeDias.</summary>
        public const int TOPE_DIAS_POR_DEFECTO = 240;
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
        public const string MOTIVO_SIN_CORREO = "El cliente no tiene correo electrónico con «Enviar boletín» marcado.";
        public const string MOTIVO_MAXIMO_PRODUCTOS = "Ya van 3 productos en su correo (se eligen los de más importe).";
        public const string MOTIVO_CORREO_DUPLICADO = "Mismo correo que otro cliente al que ya se escribe: ";
        /// <summary>NestoAPI#736: «Última compra hace 300 días: más de 240.» (ver <see cref="MotivoTope"/>).</summary>
        public const string MOTIVO_TOPE_PREFIJO = "Última compra hace ";

        /// <summary>NestoAPI#736: motivo de los que se quedan fuera por el tope de días.</summary>
        public static string MotivoTope(int diasDesdeUltimaCompra, int topeDias)
            => $"{MOTIVO_TOPE_PREFIJO}{diasDesdeUltimaCompra} días: más de {topeDias}.";

        /// <summary>
        /// NestoAPI#736: los que se avisarían y cuya última compra es de hace MÁS de <paramref name="topeDias"/> días se
        /// quedan fuera con <see cref="MotivoTope"/>. Null o 0 (o menos): sin tope. Solo para el recordatorio (correo y
        /// push): los habituales de la app no lo aplican.
        /// </summary>
        public static void AplicarTope(IEnumerable<CandidatoReposicionDTO> candidatos, int? topeDias)
        {
            if (candidatos == null || topeDias == null || topeDias.Value <= 0)
            {
                return;
            }
            foreach (CandidatoReposicionDTO candidato in candidatos.Where(c => c != null && c.SeAvisaria))
            {
                if (candidato.DiasDesdeUltimaCompra > topeDias.Value)
                {
                    candidato.Motivo = MotivoTope(candidato.DiasDesdeUltimaCompra, topeDias.Value);
                }
            }
        }

        public static DateTime HistorialDesde(DateTime hoy) => hoy.Date.AddMonths(-MESES_HISTORIAL);

        /// <summary>
        /// Mediana de los días entre compras consecutivas (días distintos). Null si no hay
        /// <see cref="MINIMO_COMPRAS"/> días de compra. NestoAPI#671 (corte 2b): la cuenta vive en el núcleo de sugerencias
        /// (la misma que usan «le toca» y el entrenamiento offline).
        /// </summary>
        public static double? IntervaloMediano(IEnumerable<DateTime> dias)
            => ModeloSugerenciasProductos.Reglas.ReglasSugerencias.IntervaloMediano(dias, MINIMO_COMPRAS);

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
            => EvaluarTodos(compras, pendientes, productos, consumibles, hoy)
                .Where(e => e.Toca)
                .Select(e => e.Candidato)
                .ToList();

        /// <summary>
        /// NestoAPI#661: TODOS los pares cliente-producto con ficha de producto, toquen o no. Es el núcleo de
        /// <see cref="EvaluarPares"/> (el job, que solo quiere los que tocan) y de <see cref="Habituales"/> (la
        /// app, que quiere todo lo comprado con lo que toca arriba): un solo criterio.
        /// <see cref="EvaluacionParReposicion.Toca"/> = consumible, con ritmo propio (al menos
        /// <see cref="MINIMO_COMPRAS"/> días y mediana de al menos <see cref="INTERVALO_MINIMO_DIAS"/>) y dentro
        /// de la ventana; solo entonces se rellena el Motivo de exclusión.
        /// </summary>
        internal static List<EvaluacionParReposicion> EvaluarTodos(IEnumerable<CompraDiaReposicion> compras,
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
                    BaseImponible = c.BaseImponible,
                    Cantidad = c.Cantidad
                })
                .ToList();

            Dictionary<string, List<CompraDiaReposicion>> comprasPorCliente = comprasValidas
                .GroupBy(c => c.Cliente, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            Dictionary<string, List<LineaPendienteReposicion>> pendientesPorCliente = (pendientes ?? Enumerable.Empty<LineaPendienteReposicion>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Cliente) && !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            productos = productos ?? new Dictionary<string, ProductoReposicion>(StringComparer.OrdinalIgnoreCase);
            var resultado = new List<EvaluacionParReposicion>();
            foreach (var par in comprasValidas.GroupBy(c => new { Cliente = c.Cliente.ToUpperInvariant(), Producto = c.Producto.ToUpperInvariant() }))
            {
                CompraDiaReposicion primera = par.First();
                if (!productos.TryGetValue(primera.Producto, out ProductoReposicion producto) || producto == null)
                {
                    continue;
                }

                // Varias filas del mismo día (la consulta ya agrupa por día) cuentan como una sola compra
                List<CompraDiaReposicion> porDia = par
                    .GroupBy(c => c.Dia)
                    .Select(g => new CompraDiaReposicion
                    {
                        Cliente = primera.Cliente,
                        Producto = primera.Producto,
                        Dia = g.Key,
                        BaseImponible = g.Sum(c => c.BaseImponible),
                        Cantidad = g.Sum(c => c.Cantidad)
                    })
                    .OrderBy(c => c.Dia)
                    .ToList();
                List<DateTime> dias = porDia.Select(c => c.Dia).ToList();
                DateTime ultima = dias.Last();
                int diasDesde = (int)(fechaHoy - ultima).TotalDays;
                double? mediana = IntervaloMediano(dias);

                var candidato = new CandidatoReposicionDTO
                {
                    Cliente = primera.Cliente,
                    Producto = producto.Producto?.Trim() ?? primera.Producto,
                    NombreProducto = producto.Nombre?.Trim(),
                    NumeroCompras = dias.Count,
                    PrimeraCompra = dias.First(),
                    UltimaCompra = ultima,
                    IntervaloDias = mediana == null ? 0 : (int)Math.Round(mediana.Value, MidpointRounding.AwayFromZero),
                    DiasDesdeUltimaCompra = diasDesde,
                    ImporteHabitual = RoundingHelper.Round(porDia.Sum(c => c.BaseImponible) / dias.Count, 2),
                    CantidadHabitual = CantidadHabitual(porDia.Select(c => c.Cantidad))
                };

                bool consumible = consumibles != null && consumibles.EsConsumible(producto.Grupo, producto.SubGrupo);
                // Carlos (25/09/26): quien lo compra cada semana (o más a menudo) no necesita recordatorio
                bool conRitmo = mediana != null && mediana.Value >= INTERVALO_MINIMO_DIAS;
                bool toca = consumible && conRitmo && TocaReponer(mediana.Value, diasDesde);
                if (toca)
                {
                    comprasPorCliente.TryGetValue(primera.Cliente, out List<CompraDiaReposicion> comprasCliente);
                    pendientesPorCliente.TryGetValue(primera.Cliente, out List<LineaPendienteReposicion> pendientesCliente);
                    candidato.Motivo = MotivoExclusion(producto, ultima, comprasCliente, pendientesCliente, productos);
                }

                resultado.Add(new EvaluacionParReposicion
                {
                    Candidato = candidato,
                    Ficha = producto,
                    Toca = toca
                });
            }
            return resultado;
        }

        /// <summary>
        /// NestoAPI#661/#662: la cantidad que el cliente suele llevarse de un producto. Es la MEDIANA de las
        /// unidades por día de compra (no la moda: con tres o cuatro compras casi nunca hay moda, y la media se
        /// la lleva por delante un pedido grande puntual), redondeada AwayFromZero y nunca menos de 1:
        /// 2, 2, 6 → 2; 1, 2 → 2 (1,5 redondea a 2).
        /// </summary>
        public static int CantidadHabitual(IEnumerable<int> cantidadesPorDia)
        {
            List<int> ordenadas = (cantidadesPorDia ?? Enumerable.Empty<int>()).Where(c => c > 0).OrderBy(c => c).ToList();
            if (ordenadas.Count == 0)
            {
                return 1;
            }
            int n = ordenadas.Count;
            decimal mediana = n % 2 == 1
                ? ordenadas[n / 2]
                : (ordenadas[(n / 2) - 1] + ordenadas[n / 2]) / 2m;
            return Math.Max(1, (int)RoundingHelper.Round(mediana, 0));
        }

        /// <summary>
        /// NestoAPI#661: los productos que el cliente ha comprado (albarán, cualquier canal) en los últimos
        /// <see cref="MESES_HISTORIAL"/> meses, con lo que hoy le toca reponer arriba. Mismo criterio que el
        /// recordatorio: <see cref="HabitualClienteDTO.TocaReponer"/> es exactamente lo que
        /// <see cref="EvaluarPares"/> deja con Motivo null, y además con stock (como en el job). Los no
        /// consumibles o sin ritmo propio salen con TocaReponer false; los de baja o ficticios no salen.
        /// Orden: los que tocan (por importe habitual) y luego el resto por importe habitual y última compra.
        /// </summary>
        public static List<HabitualClienteDTO> Habituales(IEnumerable<CompraDiaReposicion> compras,
            IEnumerable<LineaPendienteReposicion> pendientes,
            IDictionary<string, ProductoReposicion> productos,
            ConsumiblesReposicion consumibles,
            DateTime hoy,
            Func<IReadOnlyCollection<string>, ISet<string>> leerSinStock = null)
        {
            List<EvaluacionParReposicion> evaluados = EvaluarTodos(compras, pendientes, productos, consumibles, hoy)
                .Where(e => e.Ficha.Estado >= 0 && !e.Ficha.Ficticio)
                .ToList();

            List<CandidatoReposicionDTO> tocan = evaluados.Where(e => e.Toca).Select(e => e.Candidato).ToList();
            List<string> avisables = tocan.Where(c => c.SeAvisaria)
                .Select(c => c.Producto).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (avisables.Any() && leerSinStock != null)
            {
                AplicarSinStock(tocan, leerSinStock(avisables));
            }

            List<HabitualClienteDTO> habituales = evaluados
                .Select(e => new
                {
                    e.Candidato,
                    TocaReponer = e.Toca && e.Candidato.SeAvisaria
                })
                .OrderByDescending(x => x.TocaReponer)
                .ThenByDescending(x => x.Candidato.ImporteHabitual)
                .ThenByDescending(x => x.Candidato.UltimaCompra)
                .ThenBy(x => x.Candidato.Producto, StringComparer.OrdinalIgnoreCase)
                .Select(x => new HabitualClienteDTO
                {
                    Producto = x.Candidato.Producto,
                    Nombre = x.Candidato.NombreProducto,
                    CantidadHabitual = x.Candidato.CantidadHabitual,
                    UltimaCompra = x.Candidato.UltimaCompra,
                    TocaReponer = x.TocaReponer
                })
                .ToList();
            for (int i = 0; i < habituales.Count; i++)
            {
                habituales[i].Orden = i + 1;
            }
            return habituales;
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
        /// <param name="exigirCorreo">NestoAPI#662: el canal push no necesita correo (va al móvil del cliente): con
        /// false no se descarta a nadie por no tener correo ni se junta por dirección.</param>
        /// <param name="porcentajeGrupoControl">NestoAPI#662: el canal push va sin grupo de control (0).</param>
        public static ResultadoRecordatorioReposicionDTO Agrupar(List<CandidatoReposicionDTO> candidatos,
            IDictionary<string, ClienteReposicion> clientes, DateTime hoy, string consumibles = null,
            bool exigirCorreo = true, int porcentajeGrupoControl = PORCENTAJE_GRUPO_CONTROL)
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
                else if (exigirCorreo && string.IsNullOrWhiteSpace(cliente.Email))
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
                    Email = cliente.Email?.Trim(),
                    VendedorNombre = NombreVendedor(cliente),
                    Vendedor = string.IsNullOrWhiteSpace(cliente.Vendedor) ? null : cliente.Vendedor.Trim(),
                    VendedorPeluqueria = string.IsNullOrWhiteSpace(cliente.VendedorPeluqueria) ? null : cliente.VendedorPeluqueria.Trim(),
                    GrupoControl = EsGrupoControl(grupo.Key, porcentajeGrupoControl),
                    Productos = delCliente.Take(MAXIMO_PRODUCTOS_POR_CORREO).ToList()
                });
            }

            // Un correo por dirección: si varios clientes comparten correo, se queda el de más productos.
            // Sin exigir correo (push), cada cliente va por su cuenta
            foreach (var mismoCorreo in correos.GroupBy(c => exigirCorreo ? c.Email.ToLowerInvariant() : c.Cliente.ToUpperInvariant()))
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
            return NombreDePila(cliente.VendedorNombre);
        }

        /// <summary>
        /// Carlos (25/09/26): en el correo, el comercial por su nombre de pila («Lidia», no «LIDIA HERNÁNDEZ
        /// YAGÜE»). La primera palabra en tipo título; si es una abreviatura («Mª», «M.») va con la siguiente.
        /// </summary>
        internal static string NombreDePila(string nombreCompleto)
        {
            string[] palabras = (nombreCompleto ?? string.Empty).Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (palabras.Length == 0)
            {
                return null;
            }
            bool abreviatura = palabras[0].Length <= 2 || palabras[0].EndsWith(".") || palabras[0].Contains("ª");
            string nombre = abreviatura && palabras.Length > 1 ? palabras[0] + " " + palabras[1] : palabras[0];
            return System.Globalization.CultureInfo.GetCultureInfo("es-ES").TextInfo.ToTitleCase(nombre.ToLower(System.Globalization.CultureInfo.GetCultureInfo("es-ES")));
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
