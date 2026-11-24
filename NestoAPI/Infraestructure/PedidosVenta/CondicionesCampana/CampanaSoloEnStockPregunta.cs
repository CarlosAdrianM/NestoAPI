using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Lo que el vendedor contesta a la pregunta (<see cref="PedidoVentaDTO.CampanaSoloEnStock"/>).</summary>
    public static class OpcionesCampanaSoloEnStock
    {
        public const string QUITAR_DESCUENTO = "QuitarDescuento";
        public const string MANTENER_DESCUENTO = "MantenerDescuento";

        public static bool EsQuitar(string opcion) => string.Equals(opcion?.Trim(), QUITAR_DESCUENTO, StringComparison.OrdinalIgnoreCase);
        public static bool EsMantener(string opcion) => string.Equals(opcion?.Trim(), MANTENER_DESCUENTO, StringComparison.OrdinalIgnoreCase);
        /// <summary>Ya se le ha preguntado (cualquier respuesta reconocida): el aviso de después no sale.</summary>
        public static bool YaContestada(string opcion) => EsQuitar(opcion) || EsMantener(opcion);
    }

    /// <summary>Lo que tendría el cliente en ese producto SIN la campaña (el motor sin sus filas de tarifa del producto).</summary>
    public class PrecioSinCampana
    {
        public decimal Precio { get; set; }
        public decimal Descuento { get; set; }
        public decimal Pvp { get; set; }
        public bool AplicarDescuento { get; set; } = true;
        /// <summary>Lo que paga por unidad (precio con su descuento).</summary>
        public decimal PrecioEfectivo => Precio * (1 - Descuento);
    }

    /// <summary>Un producto de la respuesta de <c>POST api/PedidosVenta/AvisoCampanaSoloEnStock</c>.</summary>
    public class ProductoCampanaSoloEnStockDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Pedidas { get; set; }
        public int Disponibles { get; set; }
        public int ConDescuento { get; set; }
        public int SinDescuento { get; set; }
        public decimal DescuentoCampana { get; set; }
        public decimal DescuentoSinCampana { get; set; }
        public decimal PrecioSinCampana { get; set; }
        public string Texto { get; set; }
    }

    /// <summary>Respuesta de <c>POST api/PedidosVenta/AvisoCampanaSoloEnStock</c> (siempre 200).</summary>
    public class AvisoCampanaSoloEnStockRespuesta
    {
        public bool HayAviso { get; set; }
        public string Campana { get; set; }
        public string Mensaje { get; set; }
        public List<ProductoCampanaSoloEnStockDTO> Productos { get; set; } = new List<ProductoCampanaSoloEnStockDTO>();
    }

    /// <summary>Un producto de la campaña que no llega al stock.</summary>
    public class FaltaCampanaSoloEnStock
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Campana { get; set; }
        public int Pedidas { get; set; }
        /// <summary>Lo disponible (nunca negativo).</summary>
        public int Disponibles { get; set; }
        /// <summary>El mayor descuento de las líneas que lo llevan (el de la campaña, para enseñarlo).</summary>
        public decimal DescuentoCampana { get; set; }
        public int ConDescuento => Math.Max(0, Math.Min(Disponibles, Pedidas));
        public int SinDescuento => Pedidas - ConDescuento;
    }

    /// <summary>Calcula el precio «sin la campaña» de un producto para un cliente.</summary>
    public interface ICalculadorPrecioSinCampana
    {
        /// <summary>
        /// Null si no se puede (producto que no existe…). <paramref name="fichaDesbloqueadaPorLaCampana"/>: la ficha tiene «Aplicar Dto» = 1
        /// solo por la campaña (<see cref="CampanasAplicarDto"/>): sin ella no admite descuento.
        /// </summary>
        PrecioSinCampana Calcular(string empresa, string cliente, string contacto, string producto, string campana, int cantidad,
            bool fichaDesbloqueadaPorLaCampana);
    }

    /// <summary>
    /// El motor de siempre (<see cref="GestorPrecios.calcularDescuentoProducto(PrecioDescuentoProducto)"/>) con
    /// <see cref="PrecioDescuentoProducto.campanaExcluida"/>: sus filas de tarifa del producto no cuentan. Al ser un distribuidor, sin la
    /// fila de campaña el motor no compone nada: sale su descuento propio.
    /// </summary>
    public class CalculadorPrecioSinCampanaMotor : ICalculadorPrecioSinCampana
    {
        public PrecioSinCampana Calcular(string empresa, string cliente, string contacto, string producto, string campana, int cantidad,
            bool fichaDesbloqueadaPorLaCampana)
        {
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string elProducto = producto?.Trim();
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                Producto ficha = db.Productos.SingleOrDefault(p => p.Empresa == laEmpresa && p.Número == elProducto);
                if (ficha == null || ficha.PVP == null)
                {
                    return null;
                }
                var datos = new PrecioDescuentoProducto
                {
                    producto = ficha,
                    cliente = cliente,
                    contacto = contacto,
                    cantidad = (short)Math.Max(1, Math.Min(cantidad, short.MaxValue)),
                    // Fuera de la campaña una ficha que ella desbloquea no admite descuento (como una bloqueada: el motor da la tarifa).
                    aplicarDescuento = ficha.Aplicar_Dto && !fichaDesbloqueadaPorLaCampana,
                    campanaExcluida = campana
                };
                GestorPrecios.calcularDescuentoProducto(datos, db);
                return new PrecioSinCampana
                {
                    Precio = datos.precioCalculado,
                    Descuento = datos.descuentoCalculado,
                    Pvp = (decimal)ficha.PVP,
                    AplicarDescuento = datos.aplicarDescuento
                };
            }
        }
    }

    /// <summary>Lo hecho con un producto al quitar el descuento de lo que no hay.</summary>
    public class AjusteCampanaSoloEnStock
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Campana { get; set; }
        public int ConDescuento { get; set; }
        public int SinDescuento { get; set; }
        public int LineasNuevas { get; set; }
        public PrecioSinCampana SinCampana { get; set; }
    }

    /// <summary>
    /// NestoAPI#889 (Black Friday 2026, Carlos 24/11/26): como el contado, se PREGUNTA antes de guardar y, por defecto, lo que no hay en
    /// stock va SIN el descuento de la campaña («sin campaña» = lo que el cliente tendría sin ella: su descuento o precio habitual o la
    /// tarifa). Qué líneas cuentan y el disponible: los de <see cref="AvisoCampanaSoloEnStock"/>; además, una línea que ya va a precio
    /// «sin campaña» (o peor) no cuenta (así, lo ya partido no se vuelve a preguntar).
    /// </summary>
    public static class ReglaCampanaSoloEnStock
    {
        public const string TIPO_APLICADO = "CampanaSoloEnStockAplicado";
        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Lo que paga por unidad la línea (con su descuento de producto y de línea).</summary>
        internal static decimal PrecioEfectivoLinea(LineaPedidoVentaDTO l) =>
            l.PrecioUnitario * (l.AplicarDescuento ? 1 - l.DescuentoProducto : 1) * (1 - l.DescuentoLinea);

        /// <summary>La línea ya no lleva nada más que lo de «sin campaña».</summary>
        public static bool YaVaSinCampana(LineaPedidoVentaDTO l, PrecioSinCampana sinCampana) =>
            sinCampana != null && PrecioEfectivoLinea(l) >= sinCampana.PrecioEfectivo - 0.005M;

        /// <summary>Una línea que puede llevar el descuento de la campaña (la del aviso) y no va ya a precio «sin campaña».</summary>
        public static bool LineaConCampana(LineaPedidoVentaDTO l, PrecioSinCampana sinCampana) =>
            AvisoCampanaSoloEnStock.LineaCuenta(l) && l.oferta == null && !l.EsBonificadoGanavisiones && !l.EsFicticio && !l.EsCupon && !l.EsChequeRegalo
            && !YaVaSinCampana(l, sinCampana);

        /// <summary>
        /// LA REGLA (pura): la ficha tiene «Aplicar Dto» = 1 solo por ESTA campaña: fila de <c>dbo.CampanasAplicarDto</c> sin restaurar,
        /// vigente hoy o ya activada, y que no tuviera ya «Aplicar Dto» = 1 antes de activarla.
        /// </summary>
        public static bool FichaDesbloqueadaPorLaCampana(IEnumerable<FilaCampanaAplicarDto> filas, string campana, string empresa, string producto, DateTime hoy)
        {
            if (filas == null || string.IsNullOrWhiteSpace(campana) || string.IsNullOrWhiteSpace(producto))
            {
                return false;
            }
            string laEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            return filas.Any(f => f != null && f.FechaRestaurado == null && f.AplicarDtoAnterior != true
                && string.Equals(f.Campana?.Trim(), campana.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(string.IsNullOrWhiteSpace(f.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : f.Empresa.Trim(), laEmpresa, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.Producto?.Trim(), producto.Trim(), StringComparison.OrdinalIgnoreCase)
                && (f.FechaActivado != null || (f.FechaDesde.Date <= hoy.Date && hoy.Date <= f.FechaHasta.Date)));
        }

        /// <summary>La campaña de cada producto (la vigente que empezó antes).</summary>
        public static Dictionary<string, string> CampanaDe(IEnumerable<ProductoCampanaStock> filas, string empresa, DateTime hoy)
        {
            var campanaDe = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductoCampanaStock f in CampanasCondicionStock.Vigentes(filas, empresa, hoy).OrderBy(f => f.FechaDesde))
            {
                string producto = f.Producto.Trim();
                if (!campanaDe.ContainsKey(producto))
                {
                    campanaDe.Add(producto, f.Campana?.Trim());
                }
            }
            return campanaDe;
        }

        /// <summary>LA REGLA (pura): los productos de la campaña que no llegan al stock.</summary>
        public static List<FaltaCampanaSoloEnStock> Faltas(IEnumerable<LineaPedidoVentaDTO> lineas, string empresa, IEnumerable<ProductoCampanaStock> filas,
            DateTime hoy, IEnumerable<DisponibleProductoCampana> disponibles, IDictionary<string, PrecioSinCampana> sinCampana)
        {
            var resultado = new List<FaltaCampanaSoloEnStock>();
            Dictionary<string, string> campanaDe = CampanaDe(filas, empresa, hoy);
            if (campanaDe.Count == 0)
            {
                return resultado;
            }
            var disponiblePor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DisponibleProductoCampana d in (disponibles ?? Enumerable.Empty<DisponibleProductoCampana>()).Where(d => !string.IsNullOrWhiteSpace(d?.Producto)))
            {
                string producto = d.Producto.Trim();
                disponiblePor[producto] = (disponiblePor.TryGetValue(producto, out int ya) ? ya : 0) + d.Disponible;
            }
            var grupos = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto) && campanaDe.ContainsKey(l.Producto.Trim())
                    && LineaConCampana(l, SinCampanaDe(sinCampana, l.Producto)))
                .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase);
            foreach (var g in grupos)
            {
                int pedidas = g.Sum(l => l.Cantidad);
                int disponible = disponiblePor.TryGetValue(g.Key, out int d) ? d : 0;
                if (disponible >= pedidas)
                {
                    continue;
                }
                resultado.Add(new FaltaCampanaSoloEnStock
                {
                    Producto = g.Key,
                    Nombre = g.Select(l => l.texto?.Trim()).FirstOrDefault(t => !string.IsNullOrEmpty(t)),
                    Campana = campanaDe[g.Key],
                    Pedidas = pedidas,
                    Disponibles = Math.Max(0, disponible),
                    DescuentoCampana = g.Max(l => l.DescuentoProducto > l.DescuentoLinea ? l.DescuentoProducto : l.DescuentoLinea)
                });
            }
            return resultado;
        }

        internal static PrecioSinCampana SinCampanaDe(IDictionary<string, PrecioSinCampana> sinCampana, string producto)
        {
            if (sinCampana == null || string.IsNullOrWhiteSpace(producto))
            {
                return null;
            }
            return sinCampana.TryGetValue(producto.Trim(), out PrecioSinCampana p) ? p : null;
        }

        /// <summary>
        /// LA REGLA (pura) de «QuitarDescuento»: por producto, las unidades con el descuento de la campaña se limitan a lo disponible
        /// (repartidas por orden de línea; las que no se pueden tocar, primero, porque ya cuentan). El resto va «sin campaña»: si la
        /// línea se queda con alguna unidad con descuento, se parte (línea NUEVA, id 0, copia de la original); si no, se cambia la propia
        /// línea. Las unidades del producto no cambian. Un producto sin precio «sin campaña» no se toca.
        /// </summary>
        public static List<AjusteCampanaSoloEnStock> Ajustar(List<LineaPedidoVentaDTO> lineas, IEnumerable<FaltaCampanaSoloEnStock> faltas,
            IDictionary<string, PrecioSinCampana> sinCampana, Func<LineaPedidoVentaDTO, bool> sePuedeTocar)
        {
            var ajustes = new List<AjusteCampanaSoloEnStock>();
            if (lineas == null || faltas == null)
            {
                return ajustes;
            }
            Func<LineaPedidoVentaDTO, bool> tocable = sePuedeTocar ?? (l => true);
            foreach (FaltaCampanaSoloEnStock falta in faltas)
            {
                PrecioSinCampana precio = SinCampanaDe(sinCampana, falta.Producto);
                if (precio == null)
                {
                    continue;
                }
                List<LineaPedidoVentaDTO> delProducto = lineas
                    .Where(l => l != null && string.Equals(l.Producto?.Trim(), falta.Producto, StringComparison.OrdinalIgnoreCase) && LineaConCampana(l, precio))
                    .ToList();
                // Primero las que no se pueden tocar (ya consumen lo disponible); el orden de línea se conserva (OrderBy es estable).
                delProducto = delProducto.OrderBy(l => tocable(l) ? 1 : 0).ToList();
                int restante = falta.Disponibles;
                var ajuste = new AjusteCampanaSoloEnStock { Producto = falta.Producto, Nombre = falta.Nombre, Campana = falta.Campana, SinCampana = precio };
                foreach (LineaPedidoVentaDTO linea in delProducto)
                {
                    if (restante >= linea.Cantidad || !tocable(linea))
                    {
                        int con = Math.Min(restante, linea.Cantidad);
                        restante -= con;
                        ajuste.ConDescuento += linea.Cantidad; // se queda como está
                        continue;
                    }
                    int quedan = Math.Max(0, restante);
                    int mover = linea.Cantidad - quedan;
                    restante = 0;
                    if (quedan == 0)
                    {
                        PonerSinCampana(linea, precio);
                    }
                    else
                    {
                        LineaPedidoVentaDTO nueva = linea.CopiaParaPartir();
                        nueva.Cantidad = mover;
                        PonerSinCampana(nueva, precio);
                        linea.Cantidad = quedan;
                        lineas.Insert(lineas.IndexOf(linea) + 1, nueva);
                        ajuste.LineasNuevas++;
                    }
                    ajuste.ConDescuento += quedan;
                    ajuste.SinDescuento += mover;
                }
                if (ajuste.SinDescuento > 0)
                {
                    ajustes.Add(ajuste);
                }
            }
            return ajustes;
        }

        private static void PonerSinCampana(LineaPedidoVentaDTO linea, PrecioSinCampana precio)
        {
            linea.PrecioUnitario = precio.Precio;
            linea.DescuentoProducto = precio.Descuento;
            linea.DescuentoLinea = 0;
            linea.AplicarDescuento = precio.AplicarDescuento;
        }

        // ---------------------------------------------------------------- Textos (de usted)

        internal static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100M).ToString("0.##", ES) + " %";
        internal static string Euros(decimal importe) => importe.ToString("0.00", ES) + " €";

        /// <summary>«(al 10 % habitual)», «(a su precio de 12,50 €)» o «(a precio de tarifa)»; vacío si no se sabe.</summary>
        public static string ComoIriaSinCampana(PrecioSinCampana p)
        {
            if (p == null)
            {
                return string.Empty;
            }
            if (p.Descuento > 0)
            {
                return $" (al {Porcentaje(p.Descuento)} habitual)";
            }
            if (p.Precio < p.Pvp)
            {
                return $" (a su precio de {Euros(p.Precio)})";
            }
            return " (a precio de tarifa)";
        }

        internal static string ConNombre(string producto, string nombre) =>
            string.IsNullOrWhiteSpace(nombre) ? producto : $"{producto} ({nombre.Trim()})";

        /// <summary>«Del 45929 (NOMBRE) hay 2 de las 5 que pide: 3 irían sin el descuento de la campaña (al 10 % habitual).»</summary>
        public static string TextoProducto(FaltaCampanaSoloEnStock f, PrecioSinCampana p)
        {
            string como = ComoIriaSinCampana(p);
            string del = ConNombre(f.Producto, f.Nombre);
            if (f.ConDescuento <= 0)
            {
                return f.Pedidas == 1
                    ? $"Del {del} hoy no hay: la que pide iría sin el descuento de la campaña{como}."
                    : $"Del {del} hoy no hay: las {f.Pedidas} que pide irían sin el descuento de la campaña{como}.";
            }
            string iria = f.SinDescuento == 1 ? "iría" : "irían";
            return $"Del {del} hay {f.ConDescuento} de las {f.Pedidas} que pide: {f.SinDescuento} {iria} sin el descuento de la campaña{como}.";
        }

        internal static string NombreCampanas(IEnumerable<string> campanas)
        {
            List<string> nombres = campanas.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return nombres.Count == 0 ? null : string.Join(" y ", nombres);
        }

        /// <summary>La pregunta entera (sin la pregunta final, que la pone el cliente).</summary>
        public static AvisoCampanaSoloEnStockRespuesta Respuesta(List<FaltaCampanaSoloEnStock> faltas, IDictionary<string, PrecioSinCampana> sinCampana)
        {
            if (faltas == null || faltas.Count == 0)
            {
                return new AvisoCampanaSoloEnStockRespuesta { HayAviso = false };
            }
            string campana = NombreCampanas(faltas.Select(f => f.Campana));
            var productos = faltas.Select(f =>
            {
                PrecioSinCampana p = SinCampanaDe(sinCampana, f.Producto);
                return new ProductoCampanaSoloEnStockDTO
                {
                    Producto = f.Producto,
                    Nombre = f.Nombre,
                    Pedidas = f.Pedidas,
                    Disponibles = f.Disponibles,
                    ConDescuento = f.ConDescuento,
                    SinDescuento = f.SinDescuento,
                    DescuentoCampana = f.DescuentoCampana,
                    DescuentoSinCampana = p?.Descuento ?? 0,
                    PrecioSinCampana = p?.Precio ?? 0,
                    Texto = TextoProducto(f, p)
                };
            }).ToList();
            string cabecera = campana == null
                ? "El descuento de la campaña es solo para lo que haya en stock."
                : $"{campana}: el descuento es solo para lo que haya en stock.";
            return new AvisoCampanaSoloEnStockRespuesta
            {
                HayAviso = true,
                Campana = campana,
                Mensaje = cabecera + " " + string.Join(" ", productos.Select(p => p.Texto)),
                Productos = productos
            };
        }

        /// <summary>El aviso informativo de lo hecho al guardar con «QuitarDescuento».</summary>
        public static AvisoPedidoDTO AvisoAplicado(List<AjusteCampanaSoloEnStock> ajustes)
        {
            if (ajustes == null || ajustes.Count == 0)
            {
                return null;
            }
            string campana = NombreCampanas(ajustes.Select(a => a.Campana));
            IEnumerable<string> textos = ajustes.Select(a =>
            {
                string del = ConNombre(a.Producto, a.Nombre);
                string como = ComoIriaSinCampana(a.SinCampana);
                return a.ConDescuento <= 0
                    ? $"Del {del}, {(a.SinDescuento == 1 ? "la unidad va" : $"las {a.SinDescuento} van")} sin el descuento de la campaña{como}."
                    : $"Del {del}, {a.ConDescuento} con el descuento de la campaña y {a.SinDescuento} sin él{como}, en otra línea.";
            });
            return new AvisoPedidoDTO
            {
                Tipo = TIPO_APLICADO,
                Mensaje = $"{campana ?? "Campaña"}: lo que no hay en stock va sin el descuento. " + string.Join(" ", textos),
                Datos = ajustes.Select(a => new { a.Producto, a.Campana, a.ConDescuento, a.SinDescuento, a.LineasNuevas,
                    DescuentoSinCampana = a.SinCampana?.Descuento, PrecioSinCampana = a.SinCampana?.Precio }).ToList()
            };
        }
    }

    /// <summary>
    /// NestoAPI#889: la consulta antes de guardar y el ajuste al guardar con «QuitarDescuento». Nunca lanza: ante cualquier error, sin
    /// aviso (la consulta) o el pedido tal cual (el ajuste), y ELMAH.
    /// </summary>
    public static class ServicioCampanaSoloEnStock
    {
        internal class Analisis
        {
            public List<FaltaCampanaSoloEnStock> Faltas { get; set; } = new List<FaltaCampanaSoloEnStock>();
            public Dictionary<string, PrecioSinCampana> SinCampana { get; set; } = new Dictionary<string, PrecioSinCampana>(StringComparer.OrdinalIgnoreCase);
        }

        internal static async Task<Analisis> Analizar(PedidoVentaDTO pedido, DateTime hoy, ILectorDisponibleCampanaStock lector,
            IEnumerable<ProductoCampanaStock> filas, ICalculadorPrecioSinCampana calculador, Action<Exception> registrar,
            Func<Task<List<FilaCampanaAplicarDto>>> leerDesbloqueos = null)
        {
            var analisis = new Analisis();
            IEnumerable<ProductoCampanaStock> lasFilas = filas ?? CampanasCondicionStock.Actuales;
            List<string> productos = AvisoCampanaSoloEnStock.ProductosALeer(pedido, lasFilas, hoy);
            if (!productos.Any())
            {
                return analisis;
            }
            ILectorDisponibleCampanaStock elLector = lector ?? LectorDisponibleCampanaStockSql.DesdeConfiguracion();
            List<DisponibleProductoCampana> disponibles = await elLector.Leer(pedido.empresa, pedido.numero, productos).ConfigureAwait(false);
            Dictionary<string, string> campanaDe = ReglaCampanaSoloEnStock.CampanaDe(lasFilas, pedido.empresa, hoy);
            ICalculadorPrecioSinCampana elCalculador = calculador ?? new CalculadorPrecioSinCampanaMotor();
            // Las fichas que la campaña desbloquea (una lectura por tanda; si falla, como si no hubiera ninguna y ELMAH).
            List<FilaCampanaAplicarDto> desbloqueos = new List<FilaCampanaAplicarDto>();
            try
            {
                Func<Task<List<FilaCampanaAplicarDto>>> leer = leerDesbloqueos
                    ?? (calculador == null ? () => new RepositorioCampanasAplicarDto().LeerPendientes() : (Func<Task<List<FilaCampanaAplicarDto>>>)null);
                if (leer != null)
                {
                    desbloqueos = await leer().ConfigureAwait(false) ?? new List<FilaCampanaAplicarDto>();
                }
            }
            catch (Exception ex)
            {
                Registrar(registrar, new Exception($"[Campaña solo en stock #889] No se han podido leer las fichas desbloqueadas por la campaña: {ex.Message}", ex));
            }
            foreach (string producto in productos)
            {
                try
                {
                    int cantidad = pedido.Lineas.Where(l => AvisoCampanaSoloEnStock.LineaCuenta(l)
                        && string.Equals(l.Producto.Trim(), producto, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Cantidad);
                    string campana = campanaDe.TryGetValue(producto, out string c) ? c : null;
                    PrecioSinCampana precio = elCalculador.Calcular(pedido.empresa, pedido.cliente, pedido.contacto, producto, campana, cantidad,
                        ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(desbloqueos, campana, pedido.empresa, producto, hoy));
                    if (precio != null)
                    {
                        analisis.SinCampana[producto] = precio;
                    }
                }
                catch (Exception ex)
                {
                    Registrar(registrar, new Exception($"[Campaña solo en stock #889] No se ha podido calcular el precio sin campaña del {producto}: {ex.Message}", ex));
                }
            }
            analisis.Faltas = ReglaCampanaSoloEnStock.Faltas(pedido.Lineas, pedido.empresa, lasFilas, hoy, disponibles, analisis.SinCampana);
            return analisis;
        }

        /// <summary><c>POST api/PedidosVenta/AvisoCampanaSoloEnStock</c>: la pregunta. Nunca lanza (HayAviso false).</summary>
        public static async Task<AvisoCampanaSoloEnStockRespuesta> Consultar(PedidoVentaDTO pedido, DateTime hoy, ILectorDisponibleCampanaStock lector = null,
            IEnumerable<ProductoCampanaStock> filas = null, ICalculadorPrecioSinCampana calculador = null, Action<Exception> registrar = null,
            Func<Task<List<FilaCampanaAplicarDto>>> leerDesbloqueos = null)
        {
            try
            {
                if (pedido?.Lineas == null)
                {
                    return new AvisoCampanaSoloEnStockRespuesta { HayAviso = false };
                }
                Analisis analisis = await Analizar(pedido, hoy, lector, filas, calculador, registrar, leerDesbloqueos).ConfigureAwait(false);
                return ReglaCampanaSoloEnStock.Respuesta(analisis.Faltas, analisis.SinCampana);
            }
            catch (Exception ex)
            {
                Registrar(registrar, new Exception($"[Campaña solo en stock #889] No se ha podido preguntar por el pedido {pedido?.numero}: {ex.Message}", ex));
                return new AvisoCampanaSoloEnStockRespuesta { HayAviso = false };
            }
        }

        /// <summary>
        /// Al guardar (POST/PUT), ANTES de validar: con «QuitarDescuento», ajusta las líneas (<see cref="ReglaCampanaSoloEnStock.Ajustar"/>)
        /// y devuelve el aviso de lo hecho; con cualquier otra cosa, null y nada cambia. Nunca lanza.
        /// </summary>
        public static async Task<AvisoPedidoDTO> AlGuardar(PedidoVentaDTO pedido, DateTime hoy, Func<LineaPedidoVentaDTO, bool> sePuedeTocar,
            ILectorDisponibleCampanaStock lector = null, IEnumerable<ProductoCampanaStock> filas = null, ICalculadorPrecioSinCampana calculador = null,
            Action<Exception> registrar = null, Func<Task<List<FilaCampanaAplicarDto>>> leerDesbloqueos = null)
        {
            if (pedido?.Lineas == null || !OpcionesCampanaSoloEnStock.EsQuitar(pedido.CampanaSoloEnStock))
            {
                return null;
            }
            try
            {
                Analisis analisis = await Analizar(pedido, hoy, lector, filas, calculador, registrar, leerDesbloqueos).ConfigureAwait(false);
                if (!analisis.Faltas.Any())
                {
                    return null;
                }
                List<LineaPedidoVentaDTO> lineas = pedido.Lineas as List<LineaPedidoVentaDTO> ?? pedido.Lineas.ToList();
                List<AjusteCampanaSoloEnStock> ajustes = ReglaCampanaSoloEnStock.Ajustar(lineas, analisis.Faltas, analisis.SinCampana, sePuedeTocar);
                if (!ReferenceEquals(lineas, pedido.Lineas))
                {
                    pedido.Lineas.Clear();
                    foreach (LineaPedidoVentaDTO l in lineas)
                    {
                        pedido.Lineas.Add(l);
                    }
                }
                pedido.VincularLineas();
                return ReglaCampanaSoloEnStock.AvisoAplicado(ajustes);
            }
            catch (Exception ex)
            {
                Registrar(registrar, new Exception($"[Campaña solo en stock #889] No se ha podido quitar el descuento de lo que no hay en el pedido {pedido?.numero}: {ex.Message}", ex));
                return null;
            }
        }

        private static void Registrar(Action<Exception> registrar, Exception ex)
        {
            try
            {
                (registrar ?? (e => ElmahHelper.Log(e)))(ex);
            }
            catch
            {
                // El registro nunca rompe nada.
            }
        }
    }
}
