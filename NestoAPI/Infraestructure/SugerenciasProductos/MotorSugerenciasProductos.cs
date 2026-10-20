using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CompraNucleo = ModeloSugerenciasProductos.Datos.Compra;
using FuentesNucleo = ModeloSugerenciasProductos.Reglas.Fuentes;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>
    /// NestoAPI#670 (corte 1 de la épica #669): las reglas de las sugerencias de la plantilla de venta, sin BD (todo llega
    /// leído), para poder probarlas con datos inventados.
    /// <list type="bullet">
    /// <item><b>CompradoJunto</b>: los pares que calcula de noche el job (<see cref="CalculadoraParesCompradosJuntos"/>) con un
    /// producto del carrito como antecedente; gana la confianza más alta.</item>
    /// <item><b>LeToca</b>: lo que el cliente compra con ritmo (mediana de días entre compras, con al menos 3 días de compra, la
    /// misma de <see cref="CalculadoraReposicion.IntervaloMediano"/>) y ya se ha pasado de esa mediana; gana el que más se pasa.
    /// Si se pasa más de <see cref="CalculadoraReposicion.FACTOR_ABANDONO"/> veces, se da por dejado y no se sugiere.</item>
    /// <item><b>Potenciar</b>: ofertas activas, «Potenciar hasta» vigente y novedades que el cliente no ha comprado y que
    /// compran clientes parecidos (mismo perfil: el grupo y subgrupo en el que más compra); gana el que más clientes
    /// parecidos compran.</item>
    /// <item><b>Parecidos</b> (NestoAPI#671, corte 2a): lo que el job nocturno calcula por item-kNN
    /// (<see cref="CalculadoraAfinidadProductos"/>): productos que el cliente no compra y compran los clientes que compran lo
    /// mismo que él; gana la afinidad más alta.</item>
    /// </list>
    /// Filtros (<see cref="MotivoDescarte"/>): del grupo del vendedor, vendible (estado, ficticio; el stock o la fecha de llegada
    /// los mira el servicio), ni en el carrito, ni en un pedido pendiente, ni rechazado por ese cliente en 60 días.
    /// Mezcla (<see cref="Elegir"/>): al menos una de cada fuente si hay, hasta <see cref="SUGERENCIAS_VISIBLES"/>.
    /// <para>NestoAPI#671 (corte 2b): la matemática pura (sugerible, «le toca», «comprados juntos», perfil y la mezcla) vive
    /// en el núcleo SugerenciasProductos.Nucleo (<see cref="ReglasSugerencias"/>), para que el entrenamiento offline mida
    /// reglas-1 exactamente como funciona; aquí quedan los filtros con datos de la API y los textos.</para>
    /// </summary>
    public static class MotorSugerenciasProductos
    {
        /// <summary>Va en cada fila de SugerenciasProductosMostradas: el corte 2 (ML.NET) tendrá otra.</summary>
        public const string VERSION_MOTOR = "reglas-1";
        /// <summary>Las que se enseñan (decisión de Carlos: 3).</summary>
        public const int SUGERENCIAS_VISIBLES = 3;
        /// <summary>Tope del contrato: nunca más de 4.</summary>
        public const int MAXIMO_SUGERENCIAS = 4;
        public const int DIAS_SIN_REPETIR_RECHAZO = 60;
        public const int MESES_HISTORIAL = CalculadoraReposicion.MESES_HISTORIAL;
        /// <summary>Candidatos por fuente a los que se mira el stock (los mejores; el stock es lo más caro de leer).</summary>
        public const int CANDIDATOS_POR_FUENTE = 8;
        /// <summary>Una novedad es un producto en fase A de las propuestas de stock con menos de estos meses desde la primera venta.</summary>
        public const int MESES_NOVEDAD = 3;

        internal const string DESCARTE_SIN_FICHA = "No se encuentra la ficha del producto.";
        internal const string DESCARTE_NO_VENDIBLE = "Producto de baja o ficticio.";
        internal const string DESCARTE_NO_SUGERIBLE = "Materia prima o muestra.";
        internal const string DESCARTE_OTRO_GRUPO = "Es del otro grupo (peluquería/estética).";
        internal const string DESCARTE_EN_CARRITO = "Ya está en el carrito.";
        internal const string DESCARTE_PENDIENTE = "Ya lo tiene en un pedido pendiente de servir.";
        internal const string DESCARTE_RECHAZADO = "No le interesó hace menos de 60 días.";

        /// <summary>El orden en que se cogen las fuentes al mezclar.</summary>
        public static readonly string[] ORDEN_FUENTES = FuentesNucleo.ORDEN;

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>
        /// El grupo de productos que ve el vendedor. LA regla de comisiones (ServicioSelectorTipoComisionesAnualesVendedor) y
        /// de #665 (<see cref="PedidosVenta.ComercialesPedido.CodigosVendedores"/>): un vendedor con estado
        /// <see cref="Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA"/> es de peluquería; para un cliente, el de peluquería es
        /// el de VendedoresClienteGrupoProducto con grupo PEL y el de estética el de la ficha (Clientes.Vendedor).
        /// <list type="number">
        /// <item>Sin vendedor o con el vendedor general: todos.</item>
        /// <item>Vendedor de peluquería (estado 4): peluquería.</item>
        /// <item>Es el vendedor de peluquería de este cliente: peluquería; si además es el de estética, todos.</item>
        /// <item>En cualquier otro caso: estética (lo mismo que comisiones, donde lo que no es peluquería es estética).</item>
        /// </list>
        /// </summary>
        public static FiltroGrupoVendedor GrupoVendedor(string vendedor, DatosGrupoVendedor datos)
        {
            string codigo = vendedor?.Trim();
            if (string.IsNullOrEmpty(codigo) || string.Equals(codigo, Constantes.Vendedores.VENDEDOR_GENERAL, StringComparison.OrdinalIgnoreCase))
            {
                return FiltroGrupoVendedor.Todos;
            }
            if (datos?.EstadoVendedor == Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA)
            {
                return FiltroGrupoVendedor.Peluqueria;
            }
            bool esElDePeluqueria = string.Equals(datos?.VendedorPeluqueriaCliente?.Trim(), codigo, StringComparison.OrdinalIgnoreCase);
            bool esElDeEstetica = string.Equals(datos?.VendedorEsteticaCliente?.Trim(), codigo, StringComparison.OrdinalIgnoreCase);
            if (esElDePeluqueria)
            {
                return esElDeEstetica ? FiltroGrupoVendedor.Todos : FiltroGrupoVendedor.Peluqueria;
            }
            return FiltroGrupoVendedor.Estetica;
        }

        public static bool EsDeSuGrupo(FichaProductoSugerencia ficha, FiltroGrupoVendedor filtro)
        {
            bool esPeluqueria = string.Equals(ficha?.Grupo?.Trim(), Constantes.Productos.GRUPO_PELUQUERIA, StringComparison.OrdinalIgnoreCase);
            switch (filtro)
            {
                case FiltroGrupoVendedor.Peluqueria:
                    return esPeluqueria;
                case FiltroGrupoVendedor.Estetica:
                    return !esPeluqueria;
                default:
                    return true;
            }
        }

        /// <summary>Lo que nunca se sugiere ni entra en los pares: de baja, ficticio, materias primas y muestras.</summary>
        public static bool EsSugerible(FichaProductoSugerencia ficha) => ReglasSugerencias.EsSugerible(ficha);

        /// <summary>Por qué no se puede sugerir el producto (null si se puede; el stock va aparte).</summary>
        public static string MotivoDescarte(string producto, FichaProductoSugerencia ficha, FiltroGrupoVendedor filtro,
            ISet<string> carrito, ISet<string> pendientes, ISet<string> rechazados)
        {
            string id = producto?.Trim();
            if (ficha == null)
            {
                return DESCARTE_SIN_FICHA;
            }
            if (ficha.Estado < 0 || ficha.Ficticio)
            {
                return DESCARTE_NO_VENDIBLE;
            }
            if (!EsSugerible(ficha))
            {
                return DESCARTE_NO_SUGERIBLE;
            }
            if (!EsDeSuGrupo(ficha, filtro))
            {
                return DESCARTE_OTRO_GRUPO;
            }
            if (carrito != null && carrito.Contains(id))
            {
                return DESCARTE_EN_CARRITO;
            }
            if (pendientes != null && pendientes.Contains(id))
            {
                return DESCARTE_PENDIENTE;
            }
            if (rechazados != null && rechazados.Contains(id))
            {
                return DESCARTE_RECHAZADO;
            }
            return null;
        }

        /// <summary>
        /// «Comprados juntos»: por cada producto que no está en el carrito, el mejor par (más confianza y, a igualdad, más
        /// lift) con un producto del carrito como antecedente.
        /// </summary>
        public static List<CandidatoSugerencia> CompradoJunto(IEnumerable<ParCompradoJunto> pares, ISet<string> carrito,
            IDictionary<string, FichaProductoSugerencia> fichas)
        {
            return ReglasSugerencias.CompradoJunto((pares ?? Enumerable.Empty<ParCompradoJunto>()).Where(p => p != null).Select(AParNucleo), carrito)
                .Select(mejor => new CandidatoSugerencia
                {
                    Producto = mejor.ProductoB.Trim(),
                    Motivo = MotivosSugerencia.COMPRADO_JUNTO,
                    Puntuacion = mejor.Confianza,
                    Texto = TextoCompradoJunto(mejor.Confianza, Nombre(mejor.ProductoA.Trim(), fichas))
                })
                .ToList();
        }

        /// <summary>
        /// «Ya le toca»: productos con al menos 3 días de compra en los que los días desde la última superan la mediana de días
        /// entre compras (y no la triplican). Puntuación: días desde la última ÷ mediana.
        /// </summary>
        public static List<CandidatoSugerencia> LeToca(IEnumerable<CompraDiaReposicion> compras, DateTime hoy)
        {
            return ReglasSugerencias.LeToca(ACompras(compras), hoy)
                .Select(r => new CandidatoSugerencia
                {
                    Producto = r.Producto,
                    Motivo = MotivosSugerencia.LE_TOCA,
                    Puntuacion = r.Puntuacion,
                    Texto = TextoLeToca(r.Mediana, r.DiasDesdeUltima)
                })
                .ToList();
        }

        /// <summary>
        /// El perfil de un cliente: el grupo y subgrupo en el que más ha comprado (base imponible). Null si no ha comprado nada
        /// con ficha. Lo usan el job (para todos) y el servicio (para el cliente de la plantilla): tiene que ser la misma regla.
        /// </summary>
        public static Tuple<string, string> Perfil(IEnumerable<CompraDiaReposicion> compras, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            if (fichas == null)
            {
                return null;
            }
            return ReglasSugerencias.Perfil(ACompras(compras), Buscador(fichas));
        }

        /// <summary>
        /// «A potenciar»: de los productos que se quieren empujar, los que el cliente no ha comprado y compran clientes de su
        /// perfil. Puntuación: cuántos clientes parecidos lo compran.
        /// </summary>
        public static List<CandidatoSugerencia> Potenciar(IEnumerable<CandidatoPotenciar> candidatos, IDictionary<string, int> clientesPerfil,
            ISet<string> compradosPorElCliente)
        {
            if (clientesPerfil == null || clientesPerfil.Count == 0)
            {
                return new List<CandidatoSugerencia>();
            }
            ISet<string> comprados = compradosPorElCliente ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (candidatos ?? Enumerable.Empty<CandidatoPotenciar>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Producto))
                .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => !comprados.Contains(g.Key) && clientesPerfil.TryGetValue(g.Key, out int n) && n > 0)
                .Select(g => new CandidatoSugerencia
                {
                    Producto = g.Key,
                    Motivo = MotivosSugerencia.POTENCIAR,
                    Puntuacion = clientesPerfil[g.Key],
                    Texto = TextoPotenciar(g.ToList(), clientesPerfil[g.Key])
                })
                .OrderByDescending(c => c.Puntuacion)
                .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// NestoAPI#671: «Parecidos»: las afinidades item-kNN del cliente (las calcula el job), sin lo que ya ha comprado
        /// (el job mira 24 meses hasta anoche; aquí se quitan también las compras de hoy). Puntuación: la afinidad.
        /// </summary>
        public static List<CandidatoSugerencia> Parecidos(IEnumerable<AfinidadCliente> afinidades, ISet<string> compradosPorElCliente)
        {
            ISet<string> comprados = compradosPorElCliente ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (afinidades ?? Enumerable.Empty<AfinidadCliente>())
                .Where(a => !string.IsNullOrWhiteSpace(a.Producto) && !comprados.Contains(a.Producto.Trim()))
                .GroupBy(a => a.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new CandidatoSugerencia
                {
                    Producto = g.Key,
                    Motivo = MotivosSugerencia.PARECIDOS,
                    Puntuacion = g.Max(a => a.Afinidad),
                    Texto = TextoParecidos()
                })
                .OrderByDescending(c => c.Puntuacion)
                .ThenBy(c => c.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// NestoAPI#671: todas las fuentes que proponen cada producto, en el orden de <see cref="ORDEN_FUENTES"/> y separadas
        /// por comas («CompradoJunto,Parecidos»). Va en la columna Fuentes de lo mostrado.
        /// </summary>
        public static Dictionary<string, string> Fuentes(IEnumerable<CandidatoSugerencia> candidatos)
            => (candidatos ?? Enumerable.Empty<CandidatoSugerencia>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Producto) && c.Motivo != null)
                .GroupBy(c => c.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(",", ORDEN_FUENTES.Where(f => g.Any(c => c.Motivo == f))),
                    StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Las nuevas que se enseñan para llenar <paramref name="huecos"/>: primero la mejor de cada fuente que todavía no está
        /// a la vista (en <see cref="ORDEN_FUENTES"/>), después, por turnos, la siguiente mejor de cada fuente. Los candidatos
        /// llegan ya filtrados; aquí solo se quitan los <paramref name="excluidos"/> (lo ya enseñado en la sesión) y los repetidos.
        /// </summary>
        public static List<CandidatoSugerencia> Elegir(IEnumerable<CandidatoSugerencia> candidatos, IEnumerable<string> motivosVisibles,
            ISet<string> excluidos, int huecos)
        {
            return ReglasSugerencias.Elegir((candidatos ?? Enumerable.Empty<CandidatoSugerencia>()).Where(c => c != null),
                c => c.Producto, c => c.Motivo, c => c.Puntuacion, ORDEN_FUENTES, motivosVisibles, excluidos, huecos);
        }

        /// <summary>NestoAPI#671: las compras de la API como las mira el núcleo.</summary>
        internal static IEnumerable<CompraNucleo> ACompras(IEnumerable<CompraDiaReposicion> compras)
            => (compras ?? Enumerable.Empty<CompraDiaReposicion>())
                .Where(c => c != null)
                .Select(c => new CompraNucleo
                {
                    Cliente = c.Cliente ?? string.Empty,
                    Producto = c.Producto ?? string.Empty,
                    Dia = c.Dia,
                    BaseImponible = c.BaseImponible,
                    Cantidad = c.Cantidad
                });

        /// <summary>NestoAPI#671: la ficha de un producto para el núcleo (FichaProductoSugerencia hereda de su FichaProducto).</summary>
        internal static Func<string, ModeloSugerenciasProductos.Datos.FichaProducto> Buscador(IDictionary<string, FichaProductoSugerencia> fichas)
            => producto => fichas != null && producto != null && fichas.TryGetValue(producto.Trim(), out FichaProductoSugerencia f) ? f : null;

        internal static ParComprado AParNucleo(ParCompradoJunto p) => new ParComprado
        {
            ProductoA = p.ProductoA ?? string.Empty,
            ProductoB = p.ProductoB ?? string.Empty,
            VecesJuntos = p.VecesJuntos,
            Soporte = p.Soporte,
            Confianza = p.Confianza,
            Lift = p.Lift
        };

        /// <summary>El primer hueco libre del panel (1..4). 0 si están todos ocupados.</summary>
        public static int HuecoLibre(IEnumerable<int> ocupados)
        {
            var usados = new HashSet<int>(ocupados ?? Enumerable.Empty<int>());
            for (int posicion = 1; posicion <= MAXIMO_SUGERENCIAS; posicion++)
            {
                if (!usados.Contains(posicion))
                {
                    return posicion;
                }
            }
            return 0;
        }

        /// <summary>Si el producto de la sugerencia está en las líneas del pedido guardado, y con cuántas unidades.</summary>
        public static Tuple<bool, int?> EntradaEnPedido(string producto, IDictionary<string, int> lineasPedido)
        {
            if (lineasPedido != null && !string.IsNullOrWhiteSpace(producto) && lineasPedido.TryGetValue(producto.Trim(), out int cantidad))
            {
                return Tuple.Create(true, (int?)cantidad);
            }
            return Tuple.Create(false, (int?)null);
        }

        internal static string TextoCompradoJunto(decimal confianza, string nombreAntecedente)
        {
            int porcentaje = (int)Math.Round(confianza * 100, MidpointRounding.AwayFromZero);
            return $"Va en el {Math.Max(1, porcentaje)} % de las compras de {Acortar(nombreAntecedente, 40)}.";
        }

        internal static string TextoLeToca(double mediana, int diasDesdeUltima)
        {
            int cada = (int)Math.Round(mediana, MidpointRounding.AwayFromZero);
            return $"Lo compra cada ~{cada} días; el último, hace {diasDesdeUltima}.";
        }

        internal static string TextoPotenciar(IList<CandidatoPotenciar> tipos, int clientesParecidos)
        {
            CandidatoPotenciar oferta = tipos.FirstOrDefault(t => t.Tipo == CandidatoPotenciar.TIPO_OFERTA);
            string primero;
            if (oferta != null)
            {
                primero = string.IsNullOrWhiteSpace(oferta.Detalle) ? "En oferta" : $"En oferta ({oferta.Detalle.Trim()})";
            }
            else if (tipos.Any(t => t.Tipo == CandidatoPotenciar.TIPO_NOVEDAD))
            {
                primero = "Novedad";
            }
            else
            {
                primero = "Producto a potenciar";
            }
            string clientes = clientesParecidos == 1
                ? "lo compra 1 cliente parecido"
                : string.Format(ES, "lo compran {0:N0} clientes parecidos", clientesParecidos);
            return $"{primero}; {clientes}.";
        }

        internal static string TextoParecidos() => "Lo compran clientes parecidos, con compras como las suyas, y aún no lo ha pedido.";

        private static string Nombre(string producto, IDictionary<string, FichaProductoSugerencia> fichas)
        {
            if (fichas != null && fichas.TryGetValue(producto, out FichaProductoSugerencia ficha) && !string.IsNullOrWhiteSpace(ficha.Nombre))
            {
                return ficha.Nombre.Trim();
            }
            return producto;
        }

        private static string Acortar(string texto, int largo)
        {
            string limpio = texto?.Trim() ?? string.Empty;
            return limpio.Length <= largo ? limpio : limpio.Substring(0, largo - 1).TrimEnd() + "…";
        }
    }
}
