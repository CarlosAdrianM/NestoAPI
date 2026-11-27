using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Infraestructure.Calidad.Almacen
{
    // NestoAPI#914 (corte 2, Almacén: preparación de pedidos), solo lectura y en sombra. El preparador es el de ARIADNA: quien leyó cada
    // producto en el recorrido del picking (dbo.PreparacionEscaneos, Fase PICK). EtiquetasPicking.UsuarioQuePrepara NO vale (desde Nesto
    // sale siempre el mismo usuario). Las definiciones exactas están en docs/claude/calidad-kpi.md («Almacén: preparación de pedidos»).

    /// <summary>Sobre qué se mide un indicador (y, por tanto, su denominador).</summary>
    public enum AmbitoIndicadorAlmacen
    {
        /// <summary>Lo de un producto de un pedido en un picking (<see cref="LineaPreparadaAlmacen"/>).</summary>
        Linea,
        /// <summary>Un producto en un picking: la parada del recorrido (en el picking por ola sirve a varios pedidos).</summary>
        Parada,
        /// <summary>Las lecturas del preparador (sin denominador de líneas).</summary>
        Lecturas
    }

    /// <summary>Texto (de usted), si es un error imputable al preparador o solo informativo, y sobre qué se mide.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class IndicadorAlmacenAttribute : Attribute
    {
        public IndicadorAlmacenAttribute(string texto, AmbitoIndicadorAlmacen ambito, bool esError = true)
        {
            Texto = texto;
            Ambito = ambito;
            EsError = esError;
        }

        public string Texto { get; }
        public AmbitoIndicadorAlmacen Ambito { get; }
        public bool EsError { get; }
    }

    public enum IndicadorAlmacen
    {
        [IndicadorAlmacen("Falta en el packing: el recorrido lo dio por recogido y en la mesa de packing no apareció (falta del packing sin deshacer).",
            AmbitoIndicadorAlmacen.Linea)]
        FaltaEnPacking = 1,

        [IndicadorAlmacen("Descuadre entre picking y packing: lo metido en las cajas más lo dado por falta en el packing no es lo que se leyó en el recorrido.",
            AmbitoIndicadorAlmacen.Parada, esError: false)]
        DescuadrePickingPacking = 2,

        [IndicadorAlmacen("Falta en el recorrido: el producto se dio por falta (no estaba en el hueco).", AmbitoIndicadorAlmacen.Parada, esError: false)]
        FaltaEnPicking = 3,

        [IndicadorAlmacen("Falta deshecha por otro: dio una falta y otra persona la quitó (el producto sí estaba).", AmbitoIndicadorAlmacen.Parada, esError: false)]
        FaltaDeshechaPorOtro = 4,

        [IndicadorAlmacen("Línea rectificada: alguna unidad de la línea se abonó en una factura rectificativa (no se sabe por qué).",
            AmbitoIndicadorAlmacen.Linea, esError: false)]
        LineaRectificada = 5,

        [IndicadorAlmacen("Lectura que no casa: leyó un código desconocido o de otro producto y Ariadna lo paró (casi-error, no cuenta contra nadie).",
            AmbitoIndicadorAlmacen.Lecturas, esError: false)]
        LecturaNoCasada = 6
    }

    public static class InfoIndicadoresAlmacen
    {
        private static readonly Dictionary<IndicadorAlmacen, IndicadorAlmacenAttribute> atributos =
            Enum.GetValues(typeof(IndicadorAlmacen)).Cast<IndicadorAlmacen>().ToDictionary(i => i, i =>
                typeof(IndicadorAlmacen).GetField(i.ToString()).GetCustomAttribute<IndicadorAlmacenAttribute>());

        public static IReadOnlyList<IndicadorAlmacen> Todos { get; } = atributos.Keys.OrderBy(i => (int)i).ToList();

        public static string Texto(IndicadorAlmacen indicador) => atributos[indicador].Texto;

        public static bool EsError(IndicadorAlmacen indicador) => atributos[indicador].EsError;

        public static AmbitoIndicadorAlmacen Ambito(IndicadorAlmacen indicador) => atributos[indicador].Ambito;

        /// <summary>Por qué una línea o parada no se ha podido evaluar (IndicadoresNoEvaluados del resumen).</summary>
        public static string PorQueNoEvaluado(IndicadorAlmacen indicador)
        {
            switch (indicador)
            {
                case IndicadorAlmacen.FaltaEnPacking:
                    return "Solo las líneas cuyo pedido se empaquetó con Ariadna en ese picking (alguna lectura de packing del pedido). Las empaquetadas en papel, " +
                        "las que aún no se han empaquetado y las retenidas no cuentan; tampoco las que no tienen preparador.";
                case IndicadorAlmacen.DescuadrePickingPacking:
                    return "Solo las paradas cuyos pedidos (todos los del picking que llevan ese producto) se empaquetaron con Ariadna. Las paradas dadas enteras por falta " +
                        "ya no tienen línea en el picking y no se pueden casar con el packing.";
                case IndicadorAlmacen.LineaRectificada:
                    return "Solo las líneas ya facturadas: una línea sin factura todavía no se ha podido rectificar. Tampoco las que no tienen preparador.";
                case IndicadorAlmacen.LecturaNoCasada:
                    return "Solo hay rastro desde que Ariadna apunta las lecturas que no casan (dbo.LecturasNoCasadasAlmacen, Ariadna#43).";
                default:
                    return "Siempre se evalúa en las paradas con alguna lectura del recorrido en Ariadna.";
            }
        }
    }

    /// <summary>Un picking del periodo (su primera lectura del recorrido en Ariadna cae dentro).</summary>
    public class PickingCalidadAlmacen
    {
        public int Picking { get; set; }
        /// <summary>La primera lectura del recorrido (FechaEscaneo): manda para el periodo.</summary>
        public DateTime Fecha { get; set; }
        /// <summary>Quién lo terminó en Ariadna (PreparacionSalidasTerminadas); null si no está terminado o no hay tabla.</summary>
        public string TerminadoPor { get; set; }
        public DateTime? TerminadoEl { get; set; }
    }

    /// <summary>Lo leído en el recorrido de un picking por un usuario, por producto (netos: los «deshacer» van en negativo).</summary>
    public class LecturaPickingCalidadAlmacen
    {
        public int Picking { get; set; }
        public string Producto { get; set; }
        public string Usuario { get; set; }
        /// <summary>Unidades cogidas (lecturas que no son FALTA).</summary>
        public int Cogidas { get; set; }
        /// <summary>Unidades dadas por falta (Metodo FALTA). Negativo: quitó faltas de otro.</summary>
        public int Faltas { get; set; }
        public DateTime Ultima { get; set; }
    }

    /// <summary>Lo leído en el packing de un picking por un usuario, por pedido y producto (netos).</summary>
    public class LecturaPackingCalidadAlmacen
    {
        public int Picking { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public string Usuario { get; set; }
        public int Metidas { get; set; }
        public int Faltas { get; set; }
    }

    /// <summary>Una línea de producto de LinPedidoVta con ese picking (lo que se recoge: Cantidad − Recoger), con lo rectificado de ella.</summary>
    public class LineaPedidoCalidadAlmacen
    {
        public int Picking { get; set; }
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public short Estado { get; set; }
        public string Factura { get; set; }
        public string Almacen { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>Unidades de esta línea en facturas rectificativas (LinFacturaVtaRectificacion por FacturaOriginalLinea).</summary>
        public decimal UnidadesRectificadas { get; set; }
    }

    /// <summary>Lecturas que no casan en un picking, por usuario y pantalla.</summary>
    public class NoCasadasCalidadAlmacen
    {
        public int Picking { get; set; }
        public string Usuario { get; set; }
        public string Pantalla { get; set; }
        public int Lecturas { get; set; }
    }

    /// <summary>Un picking del rango de números del periodo que no tiene ninguna lectura del recorrido en Ariadna (en papel).</summary>
    public class PickingSinLecturasAlmacen
    {
        public int Picking { get; set; }
        public int Pedidos { get; set; }
        public int Lineas { get; set; }
        public string Almacen { get; set; }
    }

    /// <summary>Todo lo leído del periodo (lo rellena <see cref="LectorCalidadAlmacenSql"/>). Sin lógica.</summary>
    public class LecturaCalidadAlmacen
    {
        public List<PickingCalidadAlmacen> Pickings { get; set; } = new List<PickingCalidadAlmacen>();
        public List<LecturaPickingCalidadAlmacen> LecturasPicking { get; set; } = new List<LecturaPickingCalidadAlmacen>();
        public List<LecturaPackingCalidadAlmacen> LecturasPacking { get; set; } = new List<LecturaPackingCalidadAlmacen>();
        public List<LineaPedidoCalidadAlmacen> Lineas { get; set; } = new List<LineaPedidoCalidadAlmacen>();
        public List<NoCasadasCalidadAlmacen> NoCasadas { get; set; } = new List<NoCasadasCalidadAlmacen>();
        public List<PickingSinLecturasAlmacen> PickingsSinLecturas { get; set; } = new List<PickingSinLecturasAlmacen>();
        /// <summary>Primera lectura del recorrido de un picking en Ariadna (desde cuándo hay preparador).</summary>
        public DateTime? PrimeraLecturaPicking { get; set; }
        public DateTime? PrimeraLecturaPacking { get; set; }
        public DateTime? PrimeraNoCasada { get; set; }
        /// <summary>Tablas opcionales que no están.</summary>
        public List<string> TablasQueFaltan { get; set; } = new List<string>();
    }

    /// <summary>Lo de un producto en un picking: la parada del recorrido, con quién la hizo.</summary>
    public class ParadaPreparadaAlmacen
    {
        public int Picking { get; set; }
        public DateTime FechaPicking { get; set; }
        public string Producto { get; set; }
        /// <summary>Quien leyó más unidades (o, sin cogidas, quien dio la falta). Null: nadie leyó nada (no se crea la parada).</summary>
        public string Preparador { get; set; }
        public List<string> OtrosQueLeyeron { get; set; } = new List<string>();
        public int Cogidas { get; set; }
        /// <summary>Faltas netas de todos.</summary>
        public int Faltas { get; set; }
        /// <summary>Quién dio la falta (el de más faltas netas positivas).</summary>
        public string DioLaFalta { get; set; }
        /// <summary>Lo pedido de este producto en las líneas que siguen en el picking (0 si se dio entero por falta: la línea sale del picking).</summary>
        public int CantidadEnLineas { get; set; }
        public int MetidasEnPacking { get; set; }
        public int FaltasEnPacking { get; set; }
        public HashSet<IndicadorAlmacen> Indicadores { get; } = new HashSet<IndicadorAlmacen>();
        public HashSet<IndicadorAlmacen> NoEvaluados { get; } = new HashSet<IndicadorAlmacen>();

        public string Clave => ReglaCalidadAlmacen.ClaveParada(Picking, Producto);

        /// <summary>A quién se imputa: las faltas del recorrido, a quien las dio; el descuadre, al preparador.</summary>
        public string Imputado(IndicadorAlmacen indicador) =>
            (indicador == IndicadorAlmacen.FaltaEnPicking || indicador == IndicadorAlmacen.FaltaDeshechaPorOtro) && DioLaFalta != null
                ? DioLaFalta
                : Preparador;
    }

    /// <summary>
    /// Lo de UN producto de UN pedido en UN picking (normalmente una línea; si el pedido lleva el producto en varias líneas, van juntas).
    /// Es lo que se puede casar con las lecturas: el picking por ola no guarda el pedido de cada lectura del recorrido.
    /// </summary>
    public class LineaPreparadaAlmacen
    {
        public int Picking { get; set; }
        public DateTime FechaPicking { get; set; }
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public List<int> LineasPedido { get; set; } = new List<int>();
        public int Cantidad { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Almacen { get; set; }
        public string Factura { get; set; }
        /// <summary>El de su parada (null: sin rastro de Ariadna).</summary>
        public string Preparador { get; set; }
        public bool PackingEvaluado { get; set; }
        public string Empaquetador { get; set; }
        public int MetidasEnPacking { get; set; }
        public int FaltasEnPacking { get; set; }
        public decimal UnidadesRectificadas { get; set; }
        public HashSet<IndicadorAlmacen> Indicadores { get; } = new HashSet<IndicadorAlmacen>();
        public HashSet<IndicadorAlmacen> NoEvaluados { get; } = new HashSet<IndicadorAlmacen>();

        public string ClaveParada => ReglaCalidadAlmacen.ClaveParada(Picking, Producto);
    }

    /// <summary>El resultado de aplicar la regla a un periodo.</summary>
    public class EvaluacionCalidadAlmacen
    {
        public List<LineaPreparadaAlmacen> Lineas { get; set; } = new List<LineaPreparadaAlmacen>();
        public List<ParadaPreparadaAlmacen> Paradas { get; set; } = new List<ParadaPreparadaAlmacen>();
        public Dictionary<int, PickingCalidadAlmacen> Pickings { get; set; } = new Dictionary<int, PickingCalidadAlmacen>();
        /// <summary>Pickings con más de un preparador (paradas de más de una persona).</summary>
        public HashSet<int> PickingsCompartidos { get; set; } = new HashSet<int>();
    }

    /// <summary>LA REGLA (pura): de las lecturas y las líneas, quién preparó cada parada y cada línea, y qué les pasó.</summary>
    public static class ReglaCalidadAlmacen
    {
        /// <summary>«Alfredo» de «NUEVAVISION\Alfredo» (NestoAPI#823: una persona, una identidad). Null o vacío → null.</summary>
        public static string Persona(string usuario)
        {
            string limpio = UsuarioAuditoriaHelper.SinDominio(usuario);
            return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
        }

        public static string ClaveParada(int picking, string producto) =>
            picking.ToString(CultureInfo.InvariantCulture) + "|" + ClaveProducto(producto);

        private static string ClaveProducto(string producto) => producto?.Trim().ToUpperInvariant() ?? string.Empty;

        /// <summary>
        /// El preparador de un producto en un picking: quien tiene más unidades cogidas netas (empate: la lectura más reciente). Si nadie
        /// tiene cogidas pero alguien dio falta, el de más faltas (hizo la parada y no lo encontró). Null si nadie leyó nada.
        /// </summary>
        public static string Preparador(IEnumerable<LecturaPickingCalidadAlmacen> lecturasDelProducto)
        {
            List<LecturaPickingCalidadAlmacen> porUsuario = PorUsuario(lecturasDelProducto);
            LecturaPickingCalidadAlmacen elegido = porUsuario.Where(l => l.Cogidas > 0)
                .OrderByDescending(l => l.Cogidas).ThenByDescending(l => l.Ultima).FirstOrDefault()
                ?? porUsuario.Where(l => l.Faltas > 0).OrderByDescending(l => l.Faltas).ThenByDescending(l => l.Ultima).FirstOrDefault();
            return elegido?.Usuario;
        }

        /// <summary>Las lecturas sumadas por persona (sin dominio, sin distinguir mayúsculas).</summary>
        internal static List<LecturaPickingCalidadAlmacen> PorUsuario(IEnumerable<LecturaPickingCalidadAlmacen> lecturas) =>
            (lecturas ?? Enumerable.Empty<LecturaPickingCalidadAlmacen>())
                .Where(l => Persona(l.Usuario) != null)
                .GroupBy(l => Persona(l.Usuario), StringComparer.OrdinalIgnoreCase)
                .Select(g => new LecturaPickingCalidadAlmacen
                {
                    Picking = g.First().Picking,
                    Producto = g.First().Producto?.Trim(),
                    Usuario = Persona(g.First().Usuario),
                    Cogidas = g.Sum(l => l.Cogidas),
                    Faltas = g.Sum(l => l.Faltas),
                    Ultima = g.Max(l => l.Ultima)
                }).ToList();

        public static EvaluacionCalidadAlmacen Evaluar(LecturaCalidadAlmacen lectura)
        {
            var evaluacion = new EvaluacionCalidadAlmacen();
            if (lectura == null)
            {
                return evaluacion;
            }
            foreach (PickingCalidadAlmacen p in lectura.Pickings ?? new List<PickingCalidadAlmacen>())
            {
                evaluacion.Pickings[p.Picking] = p;
            }

            // Líneas: producto de un pedido en un picking
            List<LineaPreparadaAlmacen> lineas = (lectura.Lineas ?? new List<LineaPedidoCalidadAlmacen>())
                .Where(l => evaluacion.Pickings.ContainsKey(l.Picking) && l.Cantidad > 0)
                .GroupBy(l => Tuple.Create(l.Picking, l.Pedido, ClaveProducto(l.Producto)))
                .Select(g => Linea(g.ToList(), evaluacion.Pickings[g.Key.Item1]))
                .ToList();
            Dictionary<string, List<LineaPreparadaAlmacen>> lineasPorParada = lineas.GroupBy(l => l.ClaveParada).ToDictionary(g => g.Key, g => g.ToList());

            // Packing: ¿se empaquetó el pedido con Ariadna en ese picking? Y lo de cada producto
            List<LecturaPackingCalidadAlmacen> packing = (lectura.LecturasPacking ?? new List<LecturaPackingCalidadAlmacen>())
                .Where(l => evaluacion.Pickings.ContainsKey(l.Picking)).ToList();
            var empaquetados = new HashSet<Tuple<int, int>>(packing.Select(l => Tuple.Create(l.Picking, l.Pedido)));
            Dictionary<Tuple<int, int, string>, List<LecturaPackingCalidadAlmacen>> packingPorProducto = packing
                .GroupBy(l => Tuple.Create(l.Picking, l.Pedido, ClaveProducto(l.Producto)))
                .ToDictionary(g => g.Key, g => g.ToList());

            // Paradas: de las lecturas del recorrido (también las dadas enteras por falta, que ya no tienen línea en el picking)
            foreach (IGrouping<string, LecturaPickingCalidadAlmacen> g in (lectura.LecturasPicking ?? new List<LecturaPickingCalidadAlmacen>())
                .Where(l => evaluacion.Pickings.ContainsKey(l.Picking))
                .GroupBy(l => ClaveParada(l.Picking, l.Producto)))
            {
                List<LecturaPickingCalidadAlmacen> porUsuario = PorUsuario(g);
                string preparador = Preparador(g);
                if (preparador == null)
                {
                    continue;
                }
                LecturaPickingCalidadAlmacen primera = g.First();
                LecturaPickingCalidadAlmacen dioLaFalta = porUsuario.Where(u => u.Faltas > 0)
                    .OrderByDescending(u => u.Faltas).ThenByDescending(u => u.Ultima).FirstOrDefault();
                var parada = new ParadaPreparadaAlmacen
                {
                    Picking = primera.Picking,
                    FechaPicking = evaluacion.Pickings[primera.Picking].Fecha,
                    Producto = primera.Producto?.Trim(),
                    Preparador = preparador,
                    OtrosQueLeyeron = porUsuario.Select(u => u.Usuario)
                        .Where(u => !string.Equals(u, preparador, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList(),
                    Cogidas = porUsuario.Sum(u => u.Cogidas),
                    Faltas = porUsuario.Sum(u => u.Faltas)
                };
                parada.DioLaFalta = parada.Faltas > 0 ? dioLaFalta?.Usuario : null;
                if (parada.Faltas > 0)
                {
                    _ = parada.Indicadores.Add(IndicadorAlmacen.FaltaEnPicking);
                }
                // La falta de uno, quitada por otro: alguien con faltas netas positivas y alguien con negativas
                if (dioLaFalta != null && porUsuario.Any(u => u.Faltas < 0))
                {
                    parada.DioLaFalta = parada.DioLaFalta ?? dioLaFalta.Usuario;
                    _ = parada.Indicadores.Add(IndicadorAlmacen.FaltaDeshechaPorOtro);
                }

                lineasPorParada.TryGetValue(parada.Clave, out List<LineaPreparadaAlmacen> suyas);
                suyas = suyas ?? new List<LineaPreparadaAlmacen>();
                parada.CantidadEnLineas = suyas.Sum(l => l.Cantidad);
                foreach (LineaPreparadaAlmacen l in suyas)
                {
                    l.Preparador = preparador;
                    packingPorProducto.TryGetValue(Tuple.Create(l.Picking, l.Pedido, ClaveProducto(l.Producto)), out List<LecturaPackingCalidadAlmacen> pack);
                    pack = pack ?? new List<LecturaPackingCalidadAlmacen>();
                    l.PackingEvaluado = empaquetados.Contains(Tuple.Create(l.Picking, l.Pedido));
                    l.MetidasEnPacking = pack.Sum(x => x.Metidas);
                    l.FaltasEnPacking = pack.Sum(x => x.Faltas);
                    l.Empaquetador = pack.Where(x => Persona(x.Usuario) != null)
                        .GroupBy(x => Persona(x.Usuario), StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(x => x.Sum(y => Math.Abs(y.Metidas) + Math.Abs(y.Faltas))).Select(x => x.Key).FirstOrDefault();
                    if (!l.PackingEvaluado)
                    {
                        _ = l.NoEvaluados.Add(IndicadorAlmacen.FaltaEnPacking);
                    }
                    else if (l.FaltasEnPacking > 0)
                    {
                        _ = l.Indicadores.Add(IndicadorAlmacen.FaltaEnPacking);
                    }
                    if (string.IsNullOrWhiteSpace(l.Factura))
                    {
                        _ = l.NoEvaluados.Add(IndicadorAlmacen.LineaRectificada);
                    }
                    else if (l.UnidadesRectificadas > 0)
                    {
                        _ = l.Indicadores.Add(IndicadorAlmacen.LineaRectificada);
                    }
                }
                parada.MetidasEnPacking = suyas.Sum(l => l.MetidasEnPacking);
                parada.FaltasEnPacking = suyas.Sum(l => Math.Max(0, l.FaltasEnPacking));
                if (suyas.Count == 0 || !suyas.All(l => l.PackingEvaluado))
                {
                    _ = parada.NoEvaluados.Add(IndicadorAlmacen.DescuadrePickingPacking);
                }
                else if (parada.MetidasEnPacking + parada.FaltasEnPacking != parada.Cogidas)
                {
                    _ = parada.Indicadores.Add(IndicadorAlmacen.DescuadrePickingPacking);
                }
                evaluacion.Paradas.Add(parada);
            }

            // Las líneas sin ninguna lectura de su producto en el recorrido: sin preparador, nada evaluado
            foreach (LineaPreparadaAlmacen l in lineas.Where(l => l.Preparador == null))
            {
                foreach (IndicadorAlmacen i in InfoIndicadoresAlmacen.Todos.Where(i => InfoIndicadoresAlmacen.Ambito(i) == AmbitoIndicadorAlmacen.Linea))
                {
                    _ = l.NoEvaluados.Add(i);
                }
            }

            foreach (IGrouping<int, ParadaPreparadaAlmacen> p in evaluacion.Paradas.GroupBy(x => x.Picking))
            {
                if (p.Select(x => x.Preparador).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                {
                    _ = evaluacion.PickingsCompartidos.Add(p.Key);
                }
            }
            evaluacion.Paradas = evaluacion.Paradas
                .OrderBy(x => x.FechaPicking).ThenBy(x => x.Picking).ThenBy(x => x.Producto, StringComparer.Ordinal).ToList();
            evaluacion.Lineas = lineas
                .OrderBy(x => x.FechaPicking).ThenBy(x => x.Picking).ThenBy(x => x.Pedido).ThenBy(x => x.Producto, StringComparer.Ordinal).ToList();
            return evaluacion;
        }

        private static LineaPreparadaAlmacen Linea(List<LineaPedidoCalidadAlmacen> lineas, PickingCalidadAlmacen picking)
        {
            LineaPedidoCalidadAlmacen primera = lineas.OrderBy(l => l.Linea).First();
            return new LineaPreparadaAlmacen
            {
                Picking = primera.Picking,
                FechaPicking = picking.Fecha,
                Pedido = primera.Pedido,
                Producto = primera.Producto?.Trim(),
                LineasPedido = lineas.Select(l => l.Linea).OrderBy(l => l).ToList(),
                Cantidad = lineas.Sum(l => l.Cantidad),
                Cliente = primera.Cliente?.Trim(),
                Contacto = primera.Contacto?.Trim(),
                Almacen = primera.Almacen?.Trim(),
                Factura = lineas.Select(l => l.Factura?.Trim()).FirstOrDefault(f => !string.IsNullOrWhiteSpace(f)),
                UnidadesRectificadas = lineas.Sum(l => l.UnidadesRectificadas)
            };
        }
    }
}
