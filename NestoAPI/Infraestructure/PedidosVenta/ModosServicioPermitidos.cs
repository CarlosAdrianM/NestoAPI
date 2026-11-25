using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#518: un modo de servicio y si tiene sentido para el pedido (y por qué no, si no lo tiene).</summary>
    public class ModoServicioPermitidoDTO
    {
        public byte Modo { get; set; }
        public string Nombre { get; set; }
        public bool Permitido { get; set; }
        /// <summary>Por qué NO se puede elegir, en lenguaje de usuario. Null si está permitido.</summary>
        public string Motivo { get; set; }
        /// <summary>Sugerencia 1340: solo en el modo 6, con qué se va a juntar («Saldrá junto con la nota de entrega 929701.»). Null en los demás.</summary>
        public string Explicacion { get; set; }
        /// <summary>Sugerencia 1340: solo en el modo 6, los pedidos y notas con los que se junta. Null en los demás.</summary>
        public List<int> PedidosConLosQueSeJunta { get; set; }
    }

    /// <summary>
    /// Carlos 19/11/26 («quizá sea un error equipararlo a colores; mejor verlo como casos individuales»): los hechos de un pedido de
    /// Algete con los que se compara lo que haría cada modo. Por grupo producto+almacén: unidades que salen AHORA de Algete (libres o
    /// desmontando kits), unidades que pueden traer las tiendas y unidades sin cubrir.
    /// </summary>
    public class HechosModoServicio
    {
        public class Linea
        {
            public string Producto { get; set; }
            public int Cantidad { get; set; }
            public int Ahora { get; set; }
            public int DeTiendas { get; set; }
            public int SinCubrir => System.Math.Max(0, Cantidad - Ahora - DeTiendas);
        }

        public List<Linea> Lineas { get; set; } = new List<Linea>();
        /// <summary>Alguna unidad sale ya (aunque no se sepa de qué línea: la regla #561 de siempre, con el stock de la línea).</summary>
        public bool HayAlgoAhora { get; set; }

        public int UnidadesAhora => Lineas.Sum(l => l.Ahora);
        public int UnidadesDeTiendas => Lineas.Sum(l => l.DeTiendas);
        public bool HayAhora => HayAlgoAhora || UnidadesAhora > 0;
        public bool HayDeTiendas => UnidadesDeTiendas > 0;
        public List<Linea> ConSinCubrir => Lineas.Where(l => l.SinCubrir > 0).ToList();
    }

    /// <summary>
    /// Carlos 19/11/26: el PLAN DE ENTREGAS de cada modo con los hechos del pedido (en cuántas entregas sale y qué va en cada una).
    /// Sucesos: «A» lo que hay ahora; «T» lo que traen las tiendas; «P1», «P2»… lo sin cubrir de cada línea (cada una su llegada).
    /// Un suceso de más de una unidad puede llegar en VARIAS veces (dos tiendas en días distintos, una compra que llega partida): en los
    /// modos que salen según va llegando se marca con «*». Las tiendas van juntas en un suceso (sin desglose por tienda: no está en la
    /// lectura del sugeridor), así que con más de una unidad se tratan como posible llegada partida. PURA.
    /// <list type="bullet">
    /// <item>1 «Todo junto»: una entrega con todo.</item>
    /// <item>2 «Según vaya entrando»: lo de ahora y una entrega por cada llegada.</item>
    /// <item>3 «Tras reponer de tiendas»: espera a TODO lo de las tiendas y sale con lo de ahora; lo demás, una por llegada.</item>
    /// <item>4 «Ahora lo que hay y el resto de una vez»: lo de ahora y una entrega más con todo lo demás.</item>
    /// </list>
    /// </summary>
    public static class PlanEntregasModoServicio
    {
        public static string Plan(byte modo, HechosModoServicio h)
        {
            bool a = h.HayAhora;
            int t = h.UnidadesDeTiendas;
            List<HechosModoServicio.Linea> sinCubrir = h.ConSinCubrir;
            var sucesosP = sinCubrir.Select((l, i) => new { Id = "P" + (i + 1), Partible = l.SinCubrir > 1 }).ToList();
            var entregas = new List<string>();
            string Junto(IEnumerable<string> ids) => string.Join("+", ids.OrderBy(x => x, System.StringComparer.Ordinal));
            IEnumerable<string> Todos() => (a ? new[] { "A" } : new string[0]).Concat(t > 0 ? new[] { "T" } : new string[0]).Concat(sucesosP.Select(p => p.Id));
            void PorLlegada() => entregas.AddRange(sucesosP.Select(p => p.Id + (p.Partible ? "*" : string.Empty)));
            switch (modo)
            {
                case Constantes.Pedidos.ModosServicio.TODO_JUNTO:
                    entregas.Add(Junto(Todos()));
                    break;
                case Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO:
                    if (a) entregas.Add("A");
                    if (t > 0) entregas.Add(t > 1 ? "T*" : "T");
                    PorLlegada();
                    break;
                case Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS:
                    if (t > 0) entregas.Add(a ? "A+T" : "T");
                    else if (a) entregas.Add("A");
                    PorLlegada();
                    break;
                case Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ:
                    if (a) entregas.Add("A");
                    List<string> resto = Todos().Where(x => x != "A").ToList();
                    if (resto.Any()) entregas.Add(Junto(resto));
                    break;
            }
            return string.Join("|", entregas.OrderBy(x => x, System.StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// NestoAPI#518 (Carlos, 23/09/26): qué modos de servicio tienen sentido para un pedido, para que Nesto y
    /// NestoApp no dejen elegir los que no (los usuarios elegían «Tras reponer de tiendas» en pedidos que salen
    /// enteros de Algete). Desde el 19/11/26 (Carlos) en Algete se decide comparando el PLAN de cada modo
    /// (<see cref="PlanEntregasModoServicio"/>, <see cref="CalcularPorPlanes"/>): de cada grupo de modos que hacen exactamente lo mismo con
    /// este pedido se ofrece UNO (preferencia 1 &gt; 4 &gt; 3 &gt; 2), el 3 nunca sin nada que traer de las tiendas, el 4 nunca sin nada que
    /// servir ahora y sigue la excepción #561. Así «verde + rosa» (caso A) ya no ofrece el 3 (= «Todo junto») y una roja con parte en
    /// tiendas (caso B) sí. La regla por colores de abajo queda para la comprobación tolerante al modificar:
    /// <list type="bullet">
    /// <item>Pedido de TIENDA (todas sus líneas de producto en Alcobendas o Reina): solo «Según vaya entrando»;
    /// el cliente se lleva lo que hay.</item>
    /// <item>Algete, todo verde: solo «Todo junto» (los demás acaban haciendo lo mismo).</item>
    /// <item>NestoAPI#561 (pedido 927293, matiz de Carlos 29/09/26): Algete sin NINGUNA unidad que servir ahora
    /// (ni verdes, ni rosas, ni stock parcial de alguna roja): solo «Todo junto». Si alguna línea tiene parte
    /// del stock (1 de 2), «Ahora lo que hay, el resto de una vez» sí tiene sentido: sale esa unidad con sus
    /// portes y la otra en una entrega más.</item>
    /// <item>Algete, ninguna rosa: todos menos «Tras reponer de tiendas» (no hay nada que reponer).</item>
    /// <item>Algete con alguna rosa, pedido sin líneas de producto, o almacén de otro tipo (Amazon…): todos.</item>
    /// </list>
    /// Almacén del pedido con líneas de varios almacenes: si alguna es de Algete, manda Algete (el pedido lo
    /// sirve el almacén central); solo es «de tienda» si TODAS sus líneas son de una tienda.
    /// </summary>
    public static class ModosServicioPermitidos
    {
        public enum TipoAlmacen
        {
            /// <summary>Algete: aplica la regla por colores.</summary>
            Central,
            /// <summary>Alcobendas o Reina: solo «Según vaya entrando».</summary>
            Tienda,
            /// <summary>Cualquier otro (Amazon FBA…) o sin líneas: sin restricción.</summary>
            Otro
        }

        internal const string MOTIVO_TIENDA = "En tienda el cliente se lleva lo que hay: el pedido se sirve según vaya entrando.";
        internal const string MOTIVO_TODO_VERDE = "Todo el pedido tiene stock en Algete: sale todo junto.";
        internal const string MOTIVO_SIN_ROSAS = "No hay nada que traer de las tiendas: esperar a la reposición no cambiaría nada.";
        internal const string MOTIVO_NADA_CON_STOCK = "No hay ninguna unidad del pedido en stock: sale todo junto cuando llegue.";
        internal const string MOTIVO_NADA_AHORA ="No hay nada que servir ahora: es lo mismo que «Todo junto».";
        internal const string MOTIVO_TIENDAS_COMPLETAN = "Todo lo que falta viene de las tiendas: es lo mismo que «Todo junto».";

        /// <summary>De los modos que hacen lo mismo con el pedido, el que se ofrece (Carlos 19/11/26).</summary>
        internal static readonly byte[] PREFERENCIA =
        {
            Constantes.Pedidos.ModosServicio.TODO_JUNTO,
            Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ,
            Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
            Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO
        };

        /// <summary>
        /// Carlos 19/11/26: los cuatro modos de un pedido con sus hechos. Fuera de Algete (tienda, otro almacén) o sin líneas de producto,
        /// como siempre. En Algete: todo en Algete → solo «Todo junto»; nada ahora y nada en tiendas → «Todo junto» (#561) y, desde el
        /// 19/11/26 (Carlos), también «Según vaya entrando» si quedan varias líneas sin cubrir (<see cref="OfreceSegunVayaEntrandoSinNadaDisponible"/>); si no, el 3 necesita algo de las tiendas, el 4 algo que
        /// servir ahora, y de los que tienen el mismo plan se ofrece el primero de <see cref="PREFERENCIA"/>. «Todo junto» siempre se ofrece.
        /// </summary>
        public static List<ModoServicioPermitidoDTO> CalcularPorPlanes(TipoAlmacen tipo, HechosModoServicio hechos, bool hayLineasProducto)
        {
            if (!hayLineasProducto || tipo != TipoAlmacen.Central || hechos == null)
            {
                return Calcular(tipo, 0, 1, 0, hayLineasProducto);
            }
            var motivos = new Dictionary<byte, string>();
            bool ahora = hechos.HayAhora;
            bool tiendas = hechos.HayDeTiendas;
            bool sinCubrir = hechos.ConSinCubrir.Any();
            if (!tiendas && !sinCubrir)
            {
                foreach (byte m in TODOS.Where(m => m != Constantes.Pedidos.ModosServicio.TODO_JUNTO))
                {
                    motivos[m] = MOTIVO_TODO_VERDE;
                }
            }
            else if (!ahora && !tiendas)
            {
                // NestoAPI#561: ninguna unidad que servir ahora ni en las tiendas: todo junto (si no, salían solo los portes).
                // Carlos 19/11/26: con VARIAS líneas que pueden llegar en fechas distintas, también «Según vaya entrando».
                motivos[Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS] = MOTIVO_SIN_ROSAS;
                if (!OfreceSegunVayaEntrandoSinNadaDisponible(hechos))
                {
                    motivos[Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO] = MOTIVO_NADA_CON_STOCK;
                }
                motivos[Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ] = MOTIVO_NADA_CON_STOCK;
            }
            else
            {
                if (!tiendas)
                {
                    motivos[Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS] = MOTIVO_SIN_ROSAS;
                }
                if (!ahora)
                {
                    motivos[Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ] = MOTIVO_NADA_AHORA;
                }
                var ofrecidos = new List<byte>();
                foreach (byte m in PREFERENCIA)
                {
                    if (motivos.ContainsKey(m))
                    {
                        continue;
                    }
                    string plan = PlanEntregasModoServicio.Plan(m, hechos);
                    byte? igual = ofrecidos.Cast<byte?>().FirstOrDefault(o => PlanEntregasModoServicio.Plan(o.Value, hechos) == plan);
                    if (igual.HasValue)
                    {
                        motivos[m] = MotivoEquivalente(m, igual.Value, hechos);
                    }
                    else
                    {
                        ofrecidos.Add(m);
                    }
                }
            }
            return TODOS.Select(modo => new ModoServicioPermitidoDTO
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                Motivo = motivos.TryGetValue(modo, out string motivo) ? motivo : null
            })
            .Select(m => { m.Permitido = m.Motivo == null; return m; })
            .ToList();
        }

        /// <summary>
        /// LA REGLA (Carlos 19/11/26): sin nada ahora ni en las tiendas, «Según vaya entrando» se ofrece solo si quedan VARIAS líneas (grupos
        /// producto+almacén) sin cubrir, que pueden llegar en fechas distintas: cada una sale cuando llega. Con una sola, es «Todo junto».
        /// </summary>
        public static bool OfreceSegunVayaEntrandoSinNadaDisponible(HechosModoServicio hechos)
            => hechos != null && !hechos.HayAhora && !hechos.HayDeTiendas && hechos.ConSinCubrir.Count >= 2;

        private static string MotivoEquivalente(byte modo, byte equivalente, HechosModoServicio hechos)
        {
            if (modo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS && equivalente == Constantes.Pedidos.ModosServicio.TODO_JUNTO
                && !hechos.ConSinCubrir.Any())
            {
                return MOTIVO_TIENDAS_COMPLETAN;
            }
            if (modo == Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO && hechos.ConSinCubrir.Count == 1 && hechos.ConSinCubrir[0].SinCubrir == 1
                && !hechos.HayDeTiendas)
            {
                return $"Solo falta una unidad: es lo mismo que «{Constantes.Pedidos.ModosServicio.Nombre(equivalente)}».";
            }
            return $"Con este pedido es lo mismo que «{Constantes.Pedidos.ModosServicio.Nombre(equivalente)}».";
        }

        /// <summary>
        /// Carlos 19/11/26: el modo que se ofrece en lugar de <paramref name="modo"/> si este no se ofrece (el de su mismo plan o, si no hay,
        /// el primero que se ofrezca). Así el sugerido siempre es uno de los ofrecidos.
        /// </summary>
        public static byte Ofrecido(byte modo, List<ModoServicioPermitidoDTO> modos, HechosModoServicio hechos)
        {
            List<byte> permitidos = Permitidos(modos);
            if (permitidos.Count == 0 || permitidos.Contains(modo))
            {
                return modo;
            }
            if (hechos != null && TODOS.Contains(modo))
            {
                string plan = PlanEntregasModoServicio.Plan(modo, hechos);
                foreach (byte m in PREFERENCIA.Where(permitidos.Contains))
                {
                    if (PlanEntregasModoServicio.Plan(m, hechos) == plan)
                    {
                        return m;
                    }
                }
            }
            return PREFERENCIA.FirstOrDefault(permitidos.Contains) is byte primero && primero != 0 ? primero : permitidos.First();
        }

        private static readonly byte[] TODOS =
        {
            Constantes.Pedidos.ModosServicio.TODO_JUNTO,
            Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO,
            Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS,
            Constantes.Pedidos.ModosServicio.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ
        };

        /// <summary>El tipo de almacén del pedido a partir de sus líneas de producto.</summary>
        public static TipoAlmacen Clasificar(IEnumerable<LineaPedidoVentaDTO> lineasProducto)
            => ClasificarAlmacenes((lineasProducto ?? Enumerable.Empty<LineaPedidoVentaDTO>()).Select(l => l?.almacen));

        /// <summary>Incidencia 1460: el tipo de UN almacén (el de los validadores de «Servir junto»), con la misma regla.</summary>
        public static TipoAlmacen ClasificarAlmacen(string almacen) => ClasificarAlmacenes(new[] { almacen });

        private static TipoAlmacen ClasificarAlmacenes(IEnumerable<string> codigos)
        {
            List<string> almacenes = codigos
                .Select(a => a?.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!almacenes.Any())
            {
                return TipoAlmacen.Otro;
            }
            if (almacenes.Any(a => string.Equals(a, Constantes.Almacenes.ALGETE, System.StringComparison.OrdinalIgnoreCase)))
            {
                return TipoAlmacen.Central;
            }
            bool todasTienda = almacenes.All(a =>
                string.Equals(a, Constantes.Almacenes.ALCOBENDAS, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, Constantes.Almacenes.REINA, System.StringComparison.OrdinalIgnoreCase));
            return todasTienda ? TipoAlmacen.Tienda : TipoAlmacen.Otro;
        }

        /// <summary>El núcleo puro: los cuatro modos con su permiso y el motivo de los que no.</summary>
        /// <param name="hayAlgoQueServirAhora">NestoAPI#561: alguna línea tiene al menos una unidad disponible ya en su
        /// almacén (verde, o roja con stock parcial). Por defecto true: sin ese dato no se restringe nada.</param>
        public static List<ModoServicioPermitidoDTO> Calcular(TipoAlmacen tipo, int verdes, int rosas, int rojas, bool hayLineasProducto,
            bool hayAlgoQueServirAhora = true)
        {
            return TODOS.Select(modo => new ModoServicioPermitidoDTO
            {
                Modo = modo,
                Nombre = Constantes.Pedidos.ModosServicio.Nombre(modo),
                Motivo = MotivoNoPermitido(modo, tipo, verdes, rosas, rojas, hayLineasProducto, hayAlgoQueServirAhora)
            })
            .Select(m => { m.Permitido = m.Motivo == null; return m; })
            .ToList();
        }

        private static string MotivoNoPermitido(byte modo, TipoAlmacen tipo, int verdes, int rosas, int rojas, bool hayLineasProducto,
            bool hayAlgoQueServirAhora)
        {
            if (!hayLineasProducto)
            {
                return null;
            }
            switch (tipo)
            {
                case TipoAlmacen.Tienda:
                    return modo == Constantes.Pedidos.ModosServicio.SEGUN_VAYA_ENTRANDO ? null : MOTIVO_TIENDA;
                case TipoAlmacen.Central:
                    if (verdes > 0 && rosas == 0 && rojas == 0)
                    {
                        return modo == Constantes.Pedidos.ModosServicio.TODO_JUNTO ? null : MOTIVO_TODO_VERDE;
                    }
                    if (verdes == 0 && rosas == 0 && !hayAlgoQueServirAhora)
                    {
                        // NestoAPI#561: ninguna unidad que servir ahora: todo junto (si no, salían solo los portes)
                        if (modo == Constantes.Pedidos.ModosServicio.TODO_JUNTO)
                        {
                            return null;
                        }
                        return modo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS ? MOTIVO_SIN_ROSAS : MOTIVO_NADA_CON_STOCK;
                    }
                    if (rosas == 0)
                    {
                        return modo == Constantes.Pedidos.ModosServicio.TRAS_REPONER_DE_TIENDAS ? MOTIVO_SIN_ROSAS : null;
                    }
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>Los números de los modos permitidos (lo que consumen los clientes para habilitar el combo).</summary>
        public static List<byte> Permitidos(IEnumerable<ModoServicioPermitidoDTO> modos)
            => (modos ?? Enumerable.Empty<ModoServicioPermitidoDTO>()).Where(m => m.Permitido).Select(m => m.Modo).ToList();
    }
}
