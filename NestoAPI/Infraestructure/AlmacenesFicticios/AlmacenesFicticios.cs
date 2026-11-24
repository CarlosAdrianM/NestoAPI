using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.AlmacenesFicticios
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#894 (Carlos, 24/11/26): los almacenes ficticios (Almacenes.AlmacénFicticio = 1), casi todos de un vendedor (IF, AB,
    // IM…) y unos pocos que no (PRV proveedores, AMZ Amazon, EXT a extinguir, SDC, PAC…). Lo que hacía la pantalla «Altas y bajas de
    // almacén ficticio» de Nesto viejo: parejas de movimientos en ExtractoProducto con el MISMO NºTraspaso, −N en el almacén propio y
    // +N en el ficticio (Entregar) o al revés (Recoger). Siempre traspaso contra el almacén propio de quien lo hace; nunca alta o baja
    // en total. El saldo de un ficticio = SUM(ExtractoProducto.Cantidad) de las empresas 1 y 3 por producto en ese almacén.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Qué clase de almacén ficticio es (decide quién mete y saca en él).</summary>
    public enum TipoAlmacenFicticio
    {
        /// <summary>De un vendedor: algún usuario lo tiene en su parámetro «AlmacenFicticio». Mueven Almacén y tiendas.</summary>
        Vendedor,
        /// <summary>PRV (proveedores) y EXT (a extinguir): solo Almacén.</summary>
        SoloAlmacen,
        /// <summary>AMZ: Almacén y la tienda online.</summary>
        Amazon,
        /// <summary>Los demás que no son de nadie (SDC, PAC…): solo consulta, salvo Almacén.</summary>
        Otro
    }

    public static class SentidosTraspasoFicticio
    {
        /// <summary>Del almacén propio al ficticio.</summary>
        public const string ENTREGAR = "Entregar";
        /// <summary>Del ficticio al almacén propio.</summary>
        public const string RECOGER = "Recoger";

        /// <summary>«Entregar» o «Recoger» (sin mayúsculas); null si no es ninguno.</summary>
        public static string Normalizar(string sentido)
        {
            string limpio = sentido?.Trim();
            if (string.Equals(limpio, ENTREGAR, StringComparison.OrdinalIgnoreCase))
            {
                return ENTREGAR;
            }
            return string.Equals(limpio, RECOGER, StringComparison.OrdinalIgnoreCase) ? RECOGER : null;
        }
    }

    // ---------------------------------------------------------------- lo que se lee

    /// <summary>Una fila de Almacenes (empresa 1, Estado ≥ 0).</summary>
    public class AlmacenLeido
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public bool EsFicticio { get; set; }
        public bool PermiteNegativo { get; set; }
        public bool ControlUbicaciones { get; set; }
    }

    /// <summary>Un usuario con su parámetro «AlmacenFicticio» (y su parámetro «Vendedor», si lo tiene).</summary>
    public class DuenoAlmacenFicticio
    {
        public string Usuario { get; set; }
        public string Almacen { get; set; }
        public string Vendedor { get; set; }
    }

    public class ProductoTraspasoFicticio
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public int Estado { get; set; }
        public bool Ficticio { get; set; }
        public bool Ubicar { get; set; }
    }

    /// <summary>Lo libre de un producto en un hueco (Ubicaciones estado 0, sin pedido, albarán ni reserva).</summary>
    public class HuecoLibreFicticio
    {
        public string Producto { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Libres { get; set; }
        /// <summary>La fila libre más antigua del hueco (para proponer como FIFO).</summary>
        public DateTime Desde { get; set; }

        public string Codigo => (Pasillo ?? string.Empty) + (Fila ?? string.Empty) + (Columna ?? string.Empty);
        public string Texto => $"{Pasillo}/{Fila}/{Columna}";
    }

    /// <summary>Quién pregunta, con lo que hace falta para la regla de permisos.</summary>
    public class ContextoUsuarioFicticio
    {
        public bool EsClienteTienda { get; set; }
        public bool EsDireccion { get; set; }
        public bool EsInformatica { get; set; }
        public bool EsAlmacen { get; set; }
        public bool EsTiendas { get; set; }
        public bool EsTiendaOnline { get; set; }
        /// <summary>Sin dominio.</summary>
        public string Usuario { get; set; }
        /// <summary>El claim «Vendedor» o su parámetro «Vendedor».</summary>
        public string Vendedor { get; set; }
        /// <summary>Su equipo (VendedoresEquipoString: él y, si es jefe de equipo, los suyos).</summary>
        public List<string> Equipo { get; set; } = new List<string>();
        /// <summary>La tienda de un usuario de Tiendas (su AlmacénPedidoVta, REI o ALC); null si no tiene.</summary>
        public string AlmacenTienda { get; set; }
    }

    public class DecisionAlmacenFicticio
    {
        public TipoAlmacenFicticio Tipo { get; set; }
        public bool PuedeVer { get; set; }
        public bool EsSuyo { get; set; }
        public bool PuedeTraspasar { get; set; }
        /// <summary>Contra qué almacén mete y saca (el suyo): ALG o su tienda. Null si no puede.</summary>
        public string AlmacenPropio { get; set; }
        public string MotivoNoPuedeTraspasar { get; set; }
    }

    // ---------------------------------------------------------------- contrato

    /// <summary>GET api/AlmacenesFicticios: un almacén ficticio que el usuario puede ver.</summary>
    public class AlmacenFicticioDTO
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Vendedor, SoloAlmacen, Amazon u Otro.</summary>
        public string Tipo { get; set; }
        public bool EsDeVendedor { get; set; }
        /// <summary>Los vendedores dueños (parámetro «Vendedor» de los usuarios que lo tienen en «AlmacenFicticio»).</summary>
        public List<string> Vendedores { get; set; } = new List<string>();
        /// <summary>Es el del propio usuario.</summary>
        public bool EsSuyo { get; set; }
        public bool PuedeTraspasar { get; set; }
        /// <summary>Contra qué almacén mete y saca quien pregunta (ALG, REI o ALC); null si no puede.</summary>
        public string AlmacenContra { get; set; }
        public string MotivoNoPuedeTraspasar { get; set; }
        public bool PermiteNegativo { get; set; }
    }

    /// <summary>GET api/AlmacenesFicticios/{almacen}/Existencias.</summary>
    public class ExistenciasAlmacenFicticioDTO
    {
        public string Almacen { get; set; }
        public string Descripcion { get; set; }
        public List<ExistenciaAlmacenFicticioDTO> Productos { get; set; } = new List<ExistenciaAlmacenFicticioDTO>();
        public int NumeroProductos => Productos?.Count ?? 0;
        public int TotalUnidades => Productos?.Sum(p => p.Unidades) ?? 0;
    }

    /// <summary>Un producto con saldo ≠ 0 en el ficticio. Sin costes ni precios.</summary>
    public class ExistenciaAlmacenFicticioDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public string Familia { get; set; }
        public string NombreFamilia { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>POST api/AlmacenesFicticios/Traspasos.</summary>
    public class TraspasoAlmacenFicticioDTO
    {
        public string AlmacenFicticio { get; set; }
        /// <summary>«Entregar» (del almacén propio al ficticio) o «Recoger» (del ficticio al propio).</summary>
        public string Sentido { get; set; }
        public List<LineaTraspasoFicticioDTO> Lineas { get; set; } = new List<LineaTraspasoFicticioDTO>();
        /// <summary>Opcional, 500 como mucho: va al rastro.</summary>
        public string Comentario { get; set; }
        /// <summary>Opcional: con la misma clave, la misma respuesta y nada escrito dos veces.</summary>
        public Guid? Clave { get; set; }
    }

    public class LineaTraspasoFicticioDTO
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        /// <summary>Solo al entregar desde un almacén con huecos (Algete): de qué hueco sale. Sin él, el que propone la API.</summary>
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
    }

    public class ResultadoTraspasoFicticioDTO
    {
        public bool Ensayo { get; set; }
        /// <summary>El NºTraspaso de las dos filas de ExtractoProducto; null en un ensayo.</summary>
        public int? NumeroTraspaso { get; set; }
        public string AlmacenFicticio { get; set; }
        public string AlmacenPropio { get; set; }
        public string Sentido { get; set; }
        public List<LineaTraspasoFicticioHechaDTO> Lineas { get; set; } = new List<LineaTraspasoFicticioHechaDTO>();
        /// <summary>Lo que queda en el ficticio de cada producto (en un ensayo, lo que quedaría).</summary>
        public List<SaldoAlmacenFicticioDTO> Saldos { get; set; } = new List<SaldoAlmacenFicticioDTO>();
        /// <summary>Al recoger en Algete: unidades que han quedado «pendiente de ubicar» (las ubica el mozo con la PDA).</summary>
        public int UnidadesPendientesDeUbicar { get; set; }
        public string Mensaje { get; set; }
    }

    public class LineaTraspasoFicticioHechaDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        /// <summary>«002/002/004»; null si no sale de un hueco.</summary>
        public string Hueco { get; set; }
        /// <summary>El hueco lo ha propuesto la API (no venía en la petición).</summary>
        public bool HuecoPropuesto { get; set; }
        /// <summary>Al recoger en Algete: lo que queda pendiente de ubicar.</summary>
        public int PendienteDeUbicar { get; set; }
    }

    public class SaldoAlmacenFicticioDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Unidades { get; set; }
    }

    /// <summary>Una línea de la petición ya con su hueco decidido.</summary>
    public class LineaTraspasoFicticioPlan
    {
        public string Producto { get; set; }
        public int Cantidad { get; set; }
        public HuecoLibreFicticio Hueco { get; set; }
        public bool HuecoPropuesto { get; set; }
    }

    // ---------------------------------------------------------------- LAS REGLAS (puras)

    /// <summary>NestoAPI#894: quién ve y quién mueve cada almacén ficticio, y qué se puede traspasar. Pura: sin base de datos.</summary>
    public static class ReglasAlmacenesFicticios
    {
        /// <summary>ParametrosUsuario (empresa 1): el almacén ficticio de cada vendedor. Fila «(defecto)» vacía: sin valor, no tiene.</summary>
        public const string CLAVE_PARAMETRO = "AlmacenFicticio";
        /// <summary>Diario de sistema de estos traspasos (DiariosProducto, Scripts/Issue894_AlmacenesFicticios.sql).</summary>
        public const string DIARIO = "_Ficticio";
        public const string TABLA_MODIFICACIONES = "TraspasoAlmacenFicticio";
        public const string TIPO_NOTIFICACION = "TraspasoAlmacenFicticio";
        public const string PROVEEDORES = "PRV";
        public const string A_EXTINGUIR = "EXT";
        public const string AMAZON = "AMZ";
        public const int MAXIMO_LINEAS = 200;
        public const int LONGITUD_COMENTARIO = 500;

        public const string MENSAJE_CLIENTE_TIENDA = "Los almacenes ficticios no están disponibles para los clientes de la tienda.";
        public const string MENSAJE_NO_PUEDE_VER = "No puede ver el almacén {0}: cada vendedor ve el suyo (y el jefe de equipo, los de su equipo).";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static TipoAlmacenFicticio Tipo(string codigo, IEnumerable<DuenoAlmacenFicticio> duenos)
        {
            string limpio = codigo?.Trim().ToUpperInvariant();
            if (limpio == PROVEEDORES || limpio == A_EXTINGUIR)
            {
                return TipoAlmacenFicticio.SoloAlmacen;
            }
            if (limpio == AMAZON)
            {
                return TipoAlmacenFicticio.Amazon;
            }
            return DelAlmacen(codigo, duenos).Any() ? TipoAlmacenFicticio.Vendedor : TipoAlmacenFicticio.Otro;
        }

        /// <summary>Los dueños de <paramref name="codigo"/>.</summary>
        public static List<DuenoAlmacenFicticio> DelAlmacen(string codigo, IEnumerable<DuenoAlmacenFicticio> duenos) =>
            (duenos ?? Enumerable.Empty<DuenoAlmacenFicticio>())
                .Where(d => d != null && Igual(d.Almacen, codigo))
                .ToList();

        /// <summary>Es suyo: algún dueño es él (por usuario) o es su vendedor (claim «Vendedor» de NestoApp o su parámetro).</summary>
        public static bool EsSuyo(ContextoUsuarioFicticio usuario, IEnumerable<DuenoAlmacenFicticio> delAlmacen)
        {
            if (usuario == null)
            {
                return false;
            }
            return (delAlmacen ?? Enumerable.Empty<DuenoAlmacenFicticio>()).Any(d =>
                (!string.IsNullOrWhiteSpace(usuario.Usuario) && Igual(d.Usuario, usuario.Usuario))
                || (!string.IsNullOrWhiteSpace(usuario.Vendedor) && Igual(d.Vendedor, usuario.Vendedor)));
        }

        /// <summary>Es de alguien de su equipo (el jefe de equipo ve los de su equipo).</summary>
        public static bool EsDeSuEquipo(ContextoUsuarioFicticio usuario, IEnumerable<DuenoAlmacenFicticio> delAlmacen)
        {
            List<string> equipo = usuario?.Equipo ?? new List<string>();
            return equipo.Any() && (delAlmacen ?? Enumerable.Empty<DuenoAlmacenFicticio>())
                .Any(d => !string.IsNullOrWhiteSpace(d.Vendedor) && equipo.Any(v => Igual(v, d.Vendedor)));
        }

        /// <summary>
        /// LA REGLA (Carlos 24/11/26). Ver: Dirección, Informática, Almacén y Tiendas, todos; cada vendedor, el suyo; el jefe de equipo,
        /// los de su equipo; la tienda online, AMZ. Meter y sacar, siempre contra el almacén propio: en los de vendedor, Almacén (ALG) y
        /// las tiendas (la suya, AlmacénPedidoVta REI o ALC; una tienda va antes que Almacén); PRV y EXT, solo Almacén; AMZ, Almacén y la
        /// tienda online (ALG); los demás (SDC, PAC…), solo Almacén. Un cliente de la tienda, nada.
        /// </summary>
        public static DecisionAlmacenFicticio Decidir(ContextoUsuarioFicticio usuario, string codigo, IEnumerable<DuenoAlmacenFicticio> duenos)
        {
            List<DuenoAlmacenFicticio> delAlmacen = DelAlmacen(codigo, duenos);
            var decision = new DecisionAlmacenFicticio { Tipo = Tipo(codigo, delAlmacen) };
            if (usuario == null || usuario.EsClienteTienda)
            {
                decision.MotivoNoPuedeTraspasar = MENSAJE_CLIENTE_TIENDA;
                return decision;
            }
            decision.EsSuyo = EsSuyo(usuario, delAlmacen);
            decision.PuedeVer = usuario.EsDireccion || usuario.EsInformatica || usuario.EsAlmacen || usuario.EsTiendas
                || decision.EsSuyo || EsDeSuEquipo(usuario, delAlmacen)
                || (decision.Tipo == TipoAlmacenFicticio.Amazon && usuario.EsTiendaOnline);

            string almacen = codigo?.Trim().ToUpperInvariant();
            switch (decision.Tipo)
            {
                case TipoAlmacenFicticio.Vendedor:
                    if (usuario.EsTiendas && !string.IsNullOrWhiteSpace(usuario.AlmacenTienda))
                    {
                        decision.AlmacenPropio = usuario.AlmacenTienda.Trim().ToUpperInvariant();
                    }
                    else if (usuario.EsAlmacen)
                    {
                        decision.AlmacenPropio = Constantes.Almacenes.ALGETE;
                    }
                    else
                    {
                        decision.MotivoNoPuedeTraspasar = usuario.EsTiendas
                            ? GestorFacturacionTienda.MENSAJE_SIN_TIENDA
                            : "Meter y sacar de un almacén de vendedor lo hacen Almacén (contra Algete) y las tiendas (contra la suya).";
                    }
                    break;
                case TipoAlmacenFicticio.Amazon:
                    if (usuario.EsAlmacen || usuario.EsTiendaOnline)
                    {
                        decision.AlmacenPropio = Constantes.Almacenes.ALGETE;
                    }
                    else
                    {
                        decision.MotivoNoPuedeTraspasar = $"Meter y sacar de {almacen} lo hacen Almacén y la tienda online (contra Algete).";
                    }
                    break;
                default:
                    if (usuario.EsAlmacen)
                    {
                        decision.AlmacenPropio = Constantes.Almacenes.ALGETE;
                    }
                    else
                    {
                        decision.MotivoNoPuedeTraspasar = decision.Tipo == TipoAlmacenFicticio.SoloAlmacen
                            ? $"Meter y sacar de {almacen} solo lo hacen los de Almacén."
                            : $"Meter y sacar de {almacen} solo lo hacen los de Almacén; los demás solo pueden consultarlo.";
                    }
                    break;
            }
            decision.PuedeTraspasar = decision.AlmacenPropio != null;
            if (!decision.PuedeVer && decision.PuedeTraspasar)
            {
                decision.PuedeVer = true; // quien mueve, ve
            }
            return decision;
        }

        /// <summary>Por qué no vale la petición (400, de usted), o null. No mira la base de datos.</summary>
        public static string MotivoPeticionNoValida(TraspasoAlmacenFicticioDTO peticion)
        {
            if (peticion == null)
            {
                return "Falta el traspaso.";
            }
            if (string.IsNullOrWhiteSpace(peticion.AlmacenFicticio))
            {
                return "Diga el almacén ficticio.";
            }
            if (SentidosTraspasoFicticio.Normalizar(peticion.Sentido) == null)
            {
                return "El sentido tiene que ser «Entregar» (del almacén propio al ficticio) o «Recoger» (del ficticio al propio).";
            }
            List<LineaTraspasoFicticioDTO> lineas = peticion.Lineas ?? new List<LineaTraspasoFicticioDTO>();
            if (!lineas.Any())
            {
                return "Diga al menos un producto.";
            }
            if (lineas.Count > MAXIMO_LINEAS)
            {
                return $"Como mucho {MAXIMO_LINEAS} líneas por traspaso.";
            }
            foreach (LineaTraspasoFicticioDTO linea in lineas)
            {
                if (linea == null || string.IsNullOrWhiteSpace(linea.Producto))
                {
                    return "Hay una línea sin producto.";
                }
                if (linea.Cantidad <= 0)
                {
                    return $"La cantidad de {linea.Producto.Trim()} tiene que ser mayor que cero.";
                }
                if (linea.Cantidad > short.MaxValue)
                {
                    return $"No se pueden mover {linea.Cantidad} unidades de {linea.Producto.Trim()} de una vez.";
                }
                int partesHueco = new[] { linea.Pasillo, linea.Fila, linea.Columna }.Count(p => !string.IsNullOrWhiteSpace(p));
                if (partesHueco != 0 && partesHueco != 3)
                {
                    return $"El hueco de {linea.Producto.Trim()} necesita pasillo, fila y columna.";
                }
            }
            if (lineas.GroupBy(l => Limpio(l.Producto)).Sum(g => g.Sum(l => (long)l.Cantidad)) > int.MaxValue
                || lineas.GroupBy(l => Limpio(l.Producto)).Any(g => g.Sum(l => (long)l.Cantidad) > short.MaxValue))
            {
                return "Demasiadas unidades de un mismo producto en un traspaso.";
            }
            if (peticion.Comentario != null && peticion.Comentario.Trim().Length > LONGITUD_COMENTARIO)
            {
                return $"El comentario no puede tener más de {LONGITUD_COMENTARIO} caracteres.";
            }
            return null;
        }

        /// <summary>Lo que no vale de los productos (400): que no exista, que sea ficticio o, al entregar, que esté anulado.</summary>
        public static List<string> MotivosProductos(IEnumerable<string> pedidos, IReadOnlyDictionary<string, ProductoTraspasoFicticio> productos, string sentido)
        {
            var motivos = new List<string>();
            foreach (string producto in (pedidos ?? Enumerable.Empty<string>()).Select(Limpio).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (productos == null || !productos.TryGetValue(producto, out ProductoTraspasoFicticio ficha) || ficha == null)
                {
                    motivos.Add($"No existe el producto {producto}.");
                    continue;
                }
                if (ficha.Ficticio)
                {
                    motivos.Add($"El producto {producto} es ficticio: no lleva stock.");
                }
                else if (sentido == SentidosTraspasoFicticio.ENTREGAR && ficha.Estado < 0)
                {
                    motivos.Add($"El producto {producto} ({ficha.Nombre?.Trim()}) está anulado: no se puede entregar.");
                }
            }
            return motivos;
        }

        /// <summary>Unidades pedidas de cada producto (las líneas de un mismo producto se suman).</summary>
        public static Dictionary<string, int> UnidadesPorProducto(IEnumerable<LineaTraspasoFicticioDTO> lineas) =>
            (lineas ?? Enumerable.Empty<LineaTraspasoFicticioDTO>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto))
                .GroupBy(l => Limpio(l.Producto), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Cantidad), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lo que falta en el almacén de donde sale (el propio al entregar, el ficticio al recoger). Sin <paramref name="permiteNegativo"/>
        /// (Almacenes.PermitirNegativo) no puede quedar en negativo. Vacía si hay de todo.
        /// </summary>
        public static List<string> MotivosStock(IReadOnlyDictionary<string, int> pedidas, IReadOnlyDictionary<string, int> stock,
            bool permiteNegativo, string almacen, bool origenEsFicticio, IReadOnlyDictionary<string, ProductoTraspasoFicticio> productos)
        {
            var motivos = new List<string>();
            if (permiteNegativo)
            {
                return motivos;
            }
            foreach (KeyValuePair<string, int> pedida in pedidas ?? new Dictionary<string, int>())
            {
                int hay = stock != null && stock.TryGetValue(pedida.Key, out int s) ? s : 0;
                if (hay >= pedida.Value)
                {
                    continue;
                }
                string nombre = productos != null && productos.TryGetValue(pedida.Key, out ProductoTraspasoFicticio f) && !string.IsNullOrWhiteSpace(f?.Nombre)
                    ? $" ({f.Nombre.Trim()})" : string.Empty;
                motivos.Add(origenEsFicticio
                    ? $"En {almacen} solo hay {Math.Max(hay, 0)} de {pedida.Key}{nombre}, no {pedida.Value}: no se puede recoger más de lo que tiene."
                    : $"En {almacen} solo hay {Math.Max(hay, 0)} de {pedida.Key}{nombre}, no {pedida.Value}.");
            }
            return motivos;
        }

        /// <summary>
        /// Al entregar desde un almacén con huecos: el hueco de cada línea que lo necesita (producto con Ubicar). Con hueco en la petición,
        /// ese (y tiene que tener libre lo pedido, descontado lo de las líneas anteriores); sin él, el que propone la API: el primero (el
        /// más antiguo, FIFO) con libre para la línea entera. <paramref name="motivos"/> lleva lo que no se puede (400).
        /// </summary>
        public static List<LineaTraspasoFicticioPlan> AsignarHuecos(IEnumerable<LineaTraspasoFicticioDTO> lineas, IEnumerable<HuecoLibreFicticio> huecos,
            Func<string, bool> necesitaHueco, string almacen, out List<string> motivos)
        {
            motivos = new List<string>();
            var plan = new List<LineaTraspasoFicticioPlan>();
            var queda = (huecos ?? Enumerable.Empty<HuecoLibreFicticio>())
                .Where(h => h != null && h.Libres > 0)
                .Select(h => new HuecoLibreFicticio
                {
                    Producto = Limpio(h.Producto), Pasillo = h.Pasillo?.Trim(), Fila = h.Fila?.Trim(), Columna = h.Columna?.Trim(),
                    Libres = h.Libres, Desde = h.Desde
                })
                .ToList();
            foreach (LineaTraspasoFicticioDTO linea in lineas ?? Enumerable.Empty<LineaTraspasoFicticioDTO>())
            {
                string producto = Limpio(linea.Producto);
                var paso = new LineaTraspasoFicticioPlan { Producto = producto, Cantidad = linea.Cantidad };
                plan.Add(paso);
                if (necesitaHueco == null || !necesitaHueco(producto))
                {
                    continue;
                }
                List<HuecoLibreFicticio> delProducto = queda.Where(h => Igual(h.Producto, producto))
                    .OrderBy(h => h.Desde).ThenBy(h => h.Codigo, StringComparer.Ordinal).ToList();
                HuecoLibreFicticio elegido;
                if (!string.IsNullOrWhiteSpace(linea.Pasillo))
                {
                    string codigo = linea.Pasillo.Trim() + linea.Fila?.Trim() + linea.Columna?.Trim();
                    elegido = delProducto.FirstOrDefault(h => h.Codigo == codigo);
                    string texto = $"{linea.Pasillo.Trim()}/{linea.Fila?.Trim()}/{linea.Columna?.Trim()}";
                    if (elegido == null || elegido.Libres < linea.Cantidad)
                    {
                        motivos.Add($"En el hueco {texto} hay {elegido?.Libres ?? 0} libres de {producto}, no {linea.Cantidad}." +
                            OtrosHuecos(delProducto.Where(h => h != elegido)));
                        continue;
                    }
                }
                else
                {
                    elegido = delProducto.FirstOrDefault(h => h.Libres >= linea.Cantidad);
                    paso.HuecoPropuesto = true;
                    if (elegido == null)
                    {
                        motivos.Add(delProducto.Any()
                            ? $"Ningún hueco de {almacen} tiene {linea.Cantidad} libres de {producto}: elija de qué hueco sale (o parta la línea)." + OtrosHuecos(delProducto)
                            : $"En {almacen} no hay nada libre de {producto} en ningún hueco (puede estar reservado o pendiente de ubicar).");
                        continue;
                    }
                }
                elegido.Libres -= linea.Cantidad;
                paso.Hueco = new HuecoLibreFicticio
                {
                    Producto = producto, Pasillo = elegido.Pasillo, Fila = elegido.Fila, Columna = elegido.Columna,
                    Libres = linea.Cantidad, Desde = elegido.Desde
                };
            }
            return plan;
        }

        private static string OtrosHuecos(IEnumerable<HuecoLibreFicticio> huecos)
        {
            List<string> con = huecos.Where(h => h.Libres > 0).Select(h => $"{h.Texto} ({h.Libres})").ToList();
            return con.Any() ? $" Libres: {string.Join(", ", con)}." : string.Empty;
        }

        /// <summary>Las filas del extracto: por producto, −N en el almacén de donde sale y +N en el de destino, con el nombre de texto.</summary>
        public static List<ExtractosProducto.LineaMovimientoTraspaso> LineasExtracto(string sentido, string propio, string ficticio,
            IReadOnlyDictionary<string, int> unidades, IReadOnlyDictionary<string, ProductoTraspasoFicticio> productos)
        {
            bool entregar = sentido == SentidosTraspasoFicticio.ENTREGAR;
            string origen = entregar ? propio : ficticio;
            string destino = entregar ? ficticio : propio;
            var lineas = new List<ExtractosProducto.LineaMovimientoTraspaso>();
            foreach (KeyValuePair<string, int> u in unidades ?? new Dictionary<string, int>())
            {
                string texto = productos != null && productos.TryGetValue(u.Key, out ProductoTraspasoFicticio f) ? f?.Nombre?.Trim() : null;
                lineas.Add(new ExtractosProducto.LineaMovimientoTraspaso { Almacen = origen, Producto = u.Key, Cantidad = -u.Value, Texto = texto });
                lineas.Add(new ExtractosProducto.LineaMovimientoTraspaso { Almacen = destino, Producto = u.Key, Cantidad = u.Value, Texto = texto });
            }
            return lineas;
        }

        /// <summary>Lo que quedará en el ficticio: lo que tiene ± lo traspasado.</summary>
        public static Dictionary<string, int> SaldoPrevisto(string sentido, IReadOnlyDictionary<string, int> unidades, IReadOnlyDictionary<string, int> saldoFicticio)
        {
            int signo = sentido == SentidosTraspasoFicticio.ENTREGAR ? 1 : -1;
            return (unidades ?? new Dictionary<string, int>()).ToDictionary(u => u.Key,
                u => (saldoFicticio != null && saldoFicticio.TryGetValue(u.Key, out int s) ? s : 0) + signo * u.Value, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>«Entregadas a IF 5 uds. de 2 productos desde ALG (traspaso 81344).»</summary>
        public static string Mensaje(string sentido, string propio, string ficticio, int unidades, int productos, int? traspaso, int pendientesDeUbicar)
        {
            string uds = unidades == 1 ? "1 ud." : $"{unidades.ToString(es)} uds.";
            string prods = productos == 1 ? "1 producto" : $"{productos} productos";
            string que = sentido == SentidosTraspasoFicticio.ENTREGAR
                ? $"{(traspaso.HasValue ? "Entregadas" : "Se entregarían")} a {ficticio} {uds} de {prods} desde {propio}"
                : $"{(traspaso.HasValue ? "Recogidas" : "Se recogerían")} de {ficticio} {uds} de {prods} a {propio}";
            string numero = traspaso.HasValue ? $" (traspaso {traspaso.Value})" : " (ensayo: no se ha guardado nada)";
            string ubicar = pendientesDeUbicar > 0 ? $" {pendientesDeUbicar} quedan pendientes de ubicar en {propio}: ubíquelas con la PDA." : string.Empty;
            return que + numero + "." + ubicar;
        }

        /// <summary>La campana al dueño del almacén ficticio.</summary>
        public static NotificacionPushDTO Notificacion(ResultadoTraspasoFicticioDTO resultado, string quien)
        {
            bool entregar = resultado.Sentido == SentidosTraspasoFicticio.ENTREGAR;
            string lineas = string.Join(", ", resultado.Lineas
                .GroupBy(l => l.Producto)
                .Select(g => $"{g.Key} {g.First().Nombre?.Trim()} ({g.Sum(l => l.Cantidad)})".Replace("  ", " ")));
            return new NotificacionPushDTO
            {
                Titulo = entregar ? $"Le han entregado material en {resultado.AlmacenFicticio}" : $"Le han recogido material de {resultado.AlmacenFicticio}",
                Cuerpo = $"{quien ?? "NestoAPI"} ({resultado.AlmacenPropio}) {(entregar ? "le ha entregado" : "le ha recogido")}: {lineas}. Traspaso {resultado.NumeroTraspaso}.",
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    ["tipo"] = TIPO_NOTIFICACION,
                    ["almacen"] = resultado.AlmacenFicticio,
                    ["traspaso"] = resultado.NumeroTraspaso?.ToString(CultureInfo.InvariantCulture),
                    ["sentido"] = resultado.Sentido
                }
            };
        }

        public static string Limpio(string texto) => texto?.Trim().ToUpperInvariant();

        private static bool Igual(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
