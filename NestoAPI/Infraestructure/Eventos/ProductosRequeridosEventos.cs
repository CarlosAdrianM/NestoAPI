using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#732: un producto que hay que haber comprado o pedido para apuntarse al evento («gratuito por compra»).</summary>
    public class ProductoRequeridoEventoDTO
    {
        public string Producto { get; set; }
        /// <summary>Solo lectura: el nombre del producto (null si ya no existe).</summary>
        public string Nombre { get; set; }
    }

    /// <summary>NestoAPI#732: una fila de EventosProductosRequeridos, con el nombre del producto.</summary>
    public class ProductoRequeridoLeido
    {
        public int EventoId { get; set; }
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public DateTime FechaCreacion { get; set; }
    }

    /// <summary>NestoAPI#732: la línea de venta que justifica la inscripción (la mejor que hay).</summary>
    public class JustificanteCompraEvento
    {
        public int Pedido { get; set; }
        public string Producto { get; set; }
        public int Estado { get; set; }
    }

    /// <summary>
    /// NestoAPI#732: una inscripción que se anularía (o se ha anulado) al borrar el pedido que la justifica, o al quitarle la
    /// línea del producto. Va en el aviso 409 (error.details.inscripciones) y en el aviso del PUT ya confirmado.
    /// </summary>
    public class InscripcionAfectadaPedidoDTO
    {
        public int Id { get; set; }
        public int EventoId { get; set; }
        public string Evento { get; set; }
        public DateTime FechaEvento { get; set; }
        public string Almacen { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string UsuarioCreacion { get; set; }
        [Newtonsoft.Json.JsonIgnore]
        public string Origen { get; set; }
    }

    /// <summary>
    /// NestoAPI#732 (Carlos, 29/10/26). Reglas del evento «gratuito por compra».
    /// <list type="bullet">
    /// <item>Modo del evento (Eventos.ProductosRequeridosTodos, Carlos 29/10/26): «Alguno» (por defecto) basta con UNO de los
    /// productos requeridos (son alternativas); «Todos» hay que haber comprado o pedido CADA uno, y el justificante es el
    /// pedido más reciente de los que justifican.</item>
    /// <item>Vale cualquier línea de venta del cliente (empresa del evento + cliente, de cualquier contacto) de ese producto,
    /// con cantidad positiva, en estado pendiente (-1), en curso (1), albarán (2) o factura (4). Presupuestos y notas de
    /// entrega no cuentan.</item>
    /// <item>Ventana: pedidos con fecha desde <see cref="DIAS_ANTES"/> días antes de que se pusiera el requisito (la
    /// FechaCreacion más antigua de los productos requeridos del evento; los eventos no guardan su fecha de alta). Así vale
    /// la compra hecha para el evento, aunque sea un poco antes de anunciarlo, y no vale una de hace años.</item>
    /// <item>Justificante: la mejor línea (factura antes que albarán, en curso o pendiente; y, a igualdad, el pedido más
    /// reciente), que es la que menos riesgo tiene de borrarse.</item>
    /// </list>
    /// </summary>
    public static class ReglasProductosRequeridosEvento
    {
        public const int DIAS_ANTES = 60;
        public static readonly int[] ESTADOS_QUE_CUENTAN =
        {
            Constantes.EstadosLineaVenta.PENDIENTE, Constantes.EstadosLineaVenta.EN_CURSO,
            Constantes.EstadosLineaVenta.ALBARAN, Constantes.EstadosLineaVenta.FACTURA
        };

        /// <summary>400 al inscribir sin haber comprado ni pedido ninguno de los productos.</summary>
        public const string ERROR_REQUIERE_COMPRA = "EVENTO_REQUIERE_COMPRA";
        /// <summary>409 al borrar el pedido (o quitarle la línea) que justifica inscripciones, sin confirmar.</summary>
        public const string ERROR_PEDIDO_JUSTIFICA = "PEDIDO_JUSTIFICA_INSCRIPCIONES_EVENTO";
        /// <summary>Tipo del aviso del PUT del pedido cuando, confirmado, se han anulado inscripciones.</summary>
        public const string TIPO_AVISO_ANULADAS = "INSCRIPCIONES_EVENTO_ANULADAS";

        /// <summary>Basta con uno de los productos requeridos (por defecto, lo de siempre).</summary>
        public const string MODO_ALGUNO = "Alguno";
        /// <summary>Hay que haber comprado o pedido todos los productos requeridos.</summary>
        public const string MODO_TODOS = "Todos";

        /// <summary>El modo canónico («Alguno» o «Todos», sin mirar mayúsculas ni espacios); null si no es ninguno de los dos.</summary>
        public static string NormalizarModo(string modo)
        {
            string limpio = modo?.Trim();
            return string.Equals(limpio, MODO_ALGUNO, StringComparison.OrdinalIgnoreCase) ? MODO_ALGUNO
                : string.Equals(limpio, MODO_TODOS, StringComparison.OrdinalIgnoreCase) ? MODO_TODOS
                : null;
        }

        public static string Modo(bool todos) => todos ? MODO_TODOS : MODO_ALGUNO;

        private static readonly CultureInfo es = new CultureInfo("es-ES");

        public static DateTime Desde(IEnumerable<ProductoRequeridoLeido> requeridos) =>
            requeridos.Min(r => r.FechaCreacion).Date.AddDays(-DIAS_ANTES);

        /// <summary>Los códigos limpios, sin repetir y sin vacíos.</summary>
        public static List<string> Normalizar(IEnumerable<ProductoRequeridoEventoDTO> productos) =>
            (productos ?? Enumerable.Empty<ProductoRequeridoEventoDTO>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Producto))
                .Select(p => p.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static string Lista(IEnumerable<ProductoRequeridoLeido> requeridos)
        {
            List<string> textos = requeridos.OrderBy(r => r.Producto)
                .Select(r => string.IsNullOrWhiteSpace(r.Nombre) ? r.Producto : $"{r.Producto} ({r.Nombre.Trim()})")
                .ToList();
            return textos.Count == 1 ? textos[0] : "alguno de estos productos: " + string.Join(", ", textos);
        }

        public static string MensajeRequiereCompra(string titulo, List<ProductoRequeridoLeido> requeridos, DateTime desde) =>
            $"Para apuntarse a «{titulo?.Trim()}» hay que haber comprado o pedido {Lista(requeridos)}, desde el " +
            $"{desde.ToString("dd/MM/yyyy", es)}. No hemos encontrado ningún pedido del cliente con ese producto.";

        public static NestoBusinessException RequiereCompra(string titulo, List<ProductoRequeridoLeido> requeridos, DateTime desde) =>
            ErrorRequiereCompra(MensajeRequiereCompra(titulo, requeridos, desde), requeridos, desde, MODO_ALGUNO);

        /// <summary>Modo «Todos»: el texto dice cuáles le faltan.</summary>
        public static string MensajeRequiereCompraTodos(string titulo, List<ProductoRequeridoLeido> faltan, DateTime desde) =>
            $"Para apuntarse a «{titulo?.Trim()}» hay que haber comprado o pedido todos estos productos; le faltan: " +
            $"{string.Join(", ", faltan.OrderBy(r => r.Producto).Select(r => string.IsNullOrWhiteSpace(r.Nombre) ? r.Producto : $"{r.Producto} ({r.Nombre.Trim()})"))}. " +
            $"Cuentan los pedidos del cliente desde el {desde.ToString("dd/MM/yyyy", es)}.";

        /// <summary>Modo «Todos»: details.productos lleva solo los que faltan y details.modo = "Todos".</summary>
        public static NestoBusinessException RequiereCompraTodos(string titulo, List<ProductoRequeridoLeido> faltan, DateTime desde) =>
            ErrorRequiereCompra(MensajeRequiereCompraTodos(titulo, faltan, desde), faltan, desde, MODO_TODOS);

        private static NestoBusinessException ErrorRequiereCompra(string mensaje, List<ProductoRequeridoLeido> productos, DateTime desde, string modo) =>
            new NestoBusinessException(mensaje,
                new ErrorContext { ErrorCode = ERROR_REQUIERE_COMPRA }
                    .WithData("productos", productos.OrderBy(r => r.Producto).Select(r => new ProductoRequeridoEventoDTO { Producto = r.Producto, Nombre = r.Nombre }).ToList())
                    .WithData("desde", desde.Date)
                    .WithData("modo", modo))
            { StatusCode = HttpStatusCode.BadRequest, IsWarning = true };

        /// <summary>
        /// Los productos que justifican hoy en las líneas grabadas del pedido (cantidad positiva, estado que cuenta). Con el modo
        /// «Todos», al modificar el pedido solo se pregunta si se va alguno de estos.
        /// </summary>
        public static List<string> ProductosQueJustifican(IEnumerable<LinPedidoVta> lineasGrabadas) =>
            (lineasGrabadas ?? Enumerable.Empty<LinPedidoVta>())
                .Where(l => l.Cantidad > 0 && ESTADOS_QUE_CUENTAN.Contains((int)l.Estado) && !string.IsNullOrWhiteSpace(l.Producto))
                .Select(l => l.Producto.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        public static string MensajeAviso(int pedido, List<InscripcionAfectadaPedidoDTO> afectadas, bool borrarPedido)
        {
            string que = borrarPedido ? $"Si borra el pedido {pedido}" : $"Si quita del pedido {pedido} esa línea";
            string cuales = string.Join("; ", afectadas.Select(a =>
                $"{a.Cliente}/{a.Contacto}{(string.IsNullOrWhiteSpace(a.Nombre) ? "" : $" ({a.Nombre})")} en «{a.Evento}» del {a.FechaEvento.ToString("dd/MM/yyyy", es)}"));
            string inscripcion = afectadas.Count == 1 ? "la inscripción gratuita" : $"las {afectadas.Count} inscripciones gratuitas";
            return $"El pedido {pedido} justifica {inscripcion} por compra de: {cuales}. {que}, " +
                (afectadas.Count == 1 ? "se anulará la inscripción" : "se anularán las inscripciones") +
                " y se avisará a la tienda del evento. ¿Quiere seguir?";
        }

        /// <summary>El 409 que pide confirmación. Se repite la llamada con la confirmación para seguir.</summary>
        public static NestoBusinessException Aviso(string empresa, int pedido, List<InscripcionAfectadaPedidoDTO> afectadas, bool borrarPedido) =>
            new NestoBusinessException(MensajeAviso(pedido, afectadas, borrarPedido),
                new ErrorContext { ErrorCode = ERROR_PEDIDO_JUSTIFICA, Empresa = empresa, Pedido = pedido }
                    .WithData("inscripciones", afectadas))
            { StatusCode = HttpStatusCode.Conflict, IsWarning = true };

        public static string MotivoAnulacion(int pedido, bool borrarPedido) => borrarPedido
            ? $"se ha borrado el pedido {pedido}, que justificaba la inscripción gratuita por compra"
            : $"se ha quitado del pedido {pedido} el producto que justificaba la inscripción gratuita por compra";
    }

    /// <summary>
    /// NestoAPI#732: EventosProductosRequeridos y EventosInscripciones.PedidoJustificante
    /// (Scripts/Issue732_EventosProductosRequeridos.sql), fuera del EDMX. Sin el script: no hay requisitos ni justificantes
    /// (como antes) y guardar contesta false.
    /// </summary>
    public interface IRepositorioProductosRequeridosEventos
    {
        Task<Dictionary<int, List<ProductoRequeridoLeido>>> Leer(IReadOnlyCollection<int> eventos);
        /// <summary>Deja exactamente esos productos (los que ya estaban conservan su FechaCreacion). False sin la tabla.</summary>
        Task<bool> Guardar(int eventoId, IReadOnlyCollection<string> productos, string usuario);
        Task<JustificanteCompraEvento> BuscarJustificante(string empresa, string cliente, IReadOnlyCollection<string> productos, DateTime desde);
        /// <summary>False sin la columna.</summary>
        Task<bool> PonerJustificante(int inscripcion, int pedido);
        Task<Dictionary<int, int>> LeerJustificantes(IReadOnlyCollection<int> inscripciones);
        /// <summary>Las inscripciones (todas, de cualquier estado) que justifica el pedido.</summary>
        Task<List<int>> InscripcionesDelPedido(string empresa, int pedido);
        /// <summary>
        /// De esos eventos, los que exigen TODOS los productos requeridos (Eventos.ProductosRequeridosTodos,
        /// Scripts/Issue732_EventosProductosRequeridosModo.sql). Sin la columna, ninguno (todos «Alguno»).
        /// </summary>
        Task<HashSet<int>> EventosQueExigenTodos(IReadOnlyCollection<int> eventos);
        /// <summary>Guarda el modo. False sin la columna.</summary>
        Task<bool> GuardarModo(int eventoId, bool todos);
    }

    public class RepositorioProductosRequeridosEventos : IRepositorioProductosRequeridosEventos
    {
        internal const string SQL_EXISTE_TABLA = "SELECT CAST(CASE WHEN OBJECT_ID('dbo.EventosProductosRequeridos', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_EXISTE_COLUMNA = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.EventosInscripciones', 'PedidoJustificante') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_LEER = @"
SELECT r.EventoId, RTRIM(r.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre, r.FechaCreacion
FROM dbo.EventosProductosRequeridos r WITH (NOLOCK)
JOIN dbo.Eventos e WITH (NOLOCK) ON e.Id = r.EventoId
LEFT JOIN dbo.Productos p WITH (NOLOCK) ON p.Empresa = e.Empresa AND p.Número = r.Producto
WHERE r.EventoId IN ({0})";

        internal const string SQL_JUSTIFICANTE = @"
SELECT TOP 1 c.Número AS Pedido, RTRIM(l.Producto) AS Producto, CAST(l.Estado AS int) AS Estado
FROM dbo.CabPedidoVta c WITH (NOLOCK)
JOIN dbo.LinPedidoVta l WITH (NOLOCK) ON l.Empresa = c.Empresa AND l.Número = c.Número
WHERE c.Empresa = @empresa AND c.[Nº Cliente] = @cliente AND c.Fecha >= @desde
  AND l.Producto IN ({0}) AND l.Estado IN (-1, 1, 2, 4) AND l.Cantidad > 0
ORDER BY l.Estado DESC, c.Número DESC";

        internal const string SQL_PONER_JUSTIFICANTE = "UPDATE dbo.EventosInscripciones SET PedidoJustificante = @pedido WHERE Id = @id";
        internal const string SQL_LEER_JUSTIFICANTES = "SELECT Id, PedidoJustificante FROM dbo.EventosInscripciones WITH (NOLOCK) " +
            "WHERE PedidoJustificante IS NOT NULL AND Id IN ({0})";
        internal const string SQL_DEL_PEDIDO = "SELECT Id FROM dbo.EventosInscripciones WITH (NOLOCK) WHERE Empresa = @empresa AND PedidoJustificante = @pedido";

        internal const string SQL_EXISTE_COLUMNA_MODO = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.Eventos', 'ProductosRequeridosTodos') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER_MODO = "SELECT Id FROM dbo.Eventos WITH (NOLOCK) WHERE ProductosRequeridosTodos = 1 AND Id IN ({0})";
        internal const string SQL_GUARDAR_MODO = "UPDATE dbo.Eventos SET ProductosRequeridosTodos = @todos WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos tabla = new ComprobadorEsquemaEventos(SQL_EXISTE_TABLA);
        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE_COLUMNA);
        private static readonly ComprobadorEsquemaEventos columnaModo = new ComprobadorEsquemaEventos(SQL_EXISTE_COLUMNA_MODO);
        private readonly NVEntities db;

        public RepositorioProductosRequeridosEventos(NVEntities db)
        {
            this.db = db;
        }

        public async Task<Dictionary<int, List<ProductoRequeridoLeido>>> Leer(IReadOnlyCollection<int> eventos)
        {
            List<int> ids = (eventos ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !tabla.Existe(db))
            {
                return new Dictionary<int, List<ProductoRequeridoLeido>>();
            }
            List<ProductoRequeridoLeido> filas = await db.Database
                .SqlQuery<ProductoRequeridoLeido>(string.Format(SQL_LEER, string.Join(",", ids)))
                .ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f.EventoId).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Producto).ToList());
        }

        public async Task<bool> Guardar(int eventoId, IReadOnlyCollection<string> productos, string usuario)
        {
            if (!tabla.Existe(db))
            {
                return false;
            }
            List<string> lista = (productos ?? new List<string>()).ToList();
            var parametros = new List<object>
            {
                new SqlParameter("@evento", SqlDbType.Int) { Value = eventoId },
                new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = (object)Recortar(usuario, 50) ?? "NestoAPI" }
            };
            var sql = new StringBuilder();
            List<string> nombres = new List<string>();
            for (int i = 0; i < lista.Count; i++)
            {
                nombres.Add("@p" + i);
                parametros.Add(new SqlParameter("@p" + i, SqlDbType.Char, 15) { Value = lista[i] });
            }
            _ = sql.Append("DELETE FROM dbo.EventosProductosRequeridos WHERE EventoId = @evento");
            if (nombres.Any())
            {
                _ = sql.Append(" AND Producto NOT IN (").Append(string.Join(", ", nombres)).Append(")");
            }
            _ = sql.AppendLine(";");
            foreach (string nombre in nombres)
            {
                _ = sql.AppendLine($"IF NOT EXISTS (SELECT 1 FROM dbo.EventosProductosRequeridos WHERE EventoId = @evento AND Producto = {nombre}) " +
                    $"INSERT INTO dbo.EventosProductosRequeridos (EventoId, Producto, Usuario) VALUES (@evento, {nombre}, @usuario);");
            }
            // ExecuteSqlCommand va en una transacción: o todo o nada
            _ = await db.Database.ExecuteSqlCommandAsync(sql.ToString(), parametros.ToArray()).ConfigureAwait(false);
            return true;
        }

        public async Task<JustificanteCompraEvento> BuscarJustificante(string empresa, string cliente, IReadOnlyCollection<string> productos, DateTime desde)
        {
            List<string> lista = (productos ?? new List<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            if (!lista.Any())
            {
                return null;
            }
            var parametros = new List<object>
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente?.Trim() ?? "" },
                new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde.Date }
            };
            var nombres = new List<string>();
            for (int i = 0; i < lista.Count; i++)
            {
                nombres.Add("@p" + i);
                parametros.Add(new SqlParameter("@p" + i, SqlDbType.Char, 15) { Value = lista[i] });
            }
            return await db.Database
                .SqlQuery<JustificanteCompraEvento>(string.Format(SQL_JUSTIFICANTE, string.Join(", ", nombres)), parametros.ToArray())
                .FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public async Task<bool> PonerJustificante(int inscripcion, int pedido)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_PONER_JUSTIFICANTE,
                new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido },
                new SqlParameter("@id", SqlDbType.Int) { Value = inscripcion }).ConfigureAwait(false);
            return true;
        }

        public async Task<Dictionary<int, int>> LeerJustificantes(IReadOnlyCollection<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !columna.Existe(db))
            {
                return new Dictionary<int, int>();
            }
            var resultado = new Dictionary<int, int>();
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (FilaJustificante fila in await db.Database
                    .SqlQuery<FilaJustificante>(string.Format(SQL_LEER_JUSTIFICANTES, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false))
                {
                    if (fila.PedidoJustificante.HasValue)
                    {
                        resultado[fila.Id] = fila.PedidoJustificante.Value;
                    }
                }
            }
            return resultado;
        }

        public async Task<List<int>> InscripcionesDelPedido(string empresa, int pedido)
        {
            if (!columna.Existe(db))
            {
                return new List<int>();
            }
            return await db.Database.SqlQuery<int>(SQL_DEL_PEDIDO,
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO },
                new SqlParameter("@pedido", SqlDbType.Int) { Value = pedido }).ToListAsync().ConfigureAwait(false);
        }

        public async Task<HashSet<int>> EventosQueExigenTodos(IReadOnlyCollection<int> eventos)
        {
            List<int> ids = (eventos ?? new List<int>()).Distinct().ToList();
            if (!ids.Any() || !columnaModo.Existe(db))
            {
                return new HashSet<int>();
            }
            var resultado = new HashSet<int>();
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                resultado.UnionWith(await db.Database.SqlQuery<int>(string.Format(SQL_LEER_MODO, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false));
            }
            return resultado;
        }

        public async Task<bool> GuardarModo(int eventoId, bool todos)
        {
            if (!columnaModo.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_MODO,
                new SqlParameter("@todos", SqlDbType.Bit) { Value = todos },
                new SqlParameter("@id", SqlDbType.Int) { Value = eventoId }).ConfigureAwait(false);
            return true;
        }

        private static string Recortar(string texto, int maximo)
        {
            string limpio = texto?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : limpio.Length > maximo ? limpio.Substring(0, maximo) : limpio;
        }

        private class FilaJustificante
        {
            public int Id { get; set; }
            public int? PedidoJustificante { get; set; }
        }
    }

    /// <summary>
    /// NestoAPI#732: lo que mira PedidosVentaController al borrar un pedido o al quitarle líneas: qué inscripciones gratuitas
    /// por compra se quedarían sin justificante y, confirmado, anularlas y avisar a la tienda.
    /// </summary>
    public interface IInscripcionesPorCompraEventos
    {
        /// <summary>Las inscripciones vivas (Reservada o Inscrita, de eventos de hoy en adelante) que justifica el pedido.</summary>
        Task<List<InscripcionAfectadaPedidoDTO>> AfectadasAlBorrar(string empresa, int pedido);
        /// <summary>
        /// Las que se quedan sin justificante si el pedido queda con estos productos (las líneas que quedan con cantidad
        /// positiva): con el modo «Alguno», las de un evento ninguno de cuyos productos requeridos queda en el pedido; con
        /// «Todos», las de un evento al que se le va del pedido alguno de sus productos requeridos que el pedido tenía
        /// (<paramref name="productosQueTenia"/>; null = se miran todos los requeridos).
        /// </summary>
        Task<List<InscripcionAfectadaPedidoDTO>> AfectadasAlModificar(string empresa, int pedido, IEnumerable<string> productosQueQuedan,
            IEnumerable<string> productosQueTenia = null);
        /// <summary>Anula (Estado Anulada + Modificaciones) y avisa a la tienda. Devuelve las anuladas. Nunca lanza por el aviso.</summary>
        Task<List<InscripcionAfectadaPedidoDTO>> Anular(List<InscripcionAfectadaPedidoDTO> afectadas, int pedido, bool borrarPedido, string usuario);
    }

    /// <summary>NestoAPI#732: aviso a la tienda del evento (correo) y a quien inscribió (campana de Nesto y push de NestoApp).</summary>
    public interface IAvisadorInscripcionAnuladaPorPedido
    {
        Task Avisar(InscripcionAfectadaPedidoDTO inscripcion, int pedido, string motivo, string usuario);
    }

    public class ServicioInscripcionesPorCompraEventos : IInscripcionesPorCompraEventos
    {
        private readonly NVEntities db;
        private readonly IRepositorioProductosRequeridosEventos repositorio;
        private readonly Func<DateTime> reloj;
        private readonly IAvisadorInscripcionAnuladaPorPedido avisador;

        public ServicioInscripcionesPorCompraEventos(NVEntities db)
            : this(db, new RepositorioProductosRequeridosEventos(db), () => DateTime.Now, new AvisadorInscripcionAnuladaPorPedido(db))
        {
        }

        internal ServicioInscripcionesPorCompraEventos(NVEntities db, IRepositorioProductosRequeridosEventos repositorio, Func<DateTime> reloj,
            IAvisadorInscripcionAnuladaPorPedido avisador)
        {
            this.db = db;
            this.repositorio = repositorio;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.avisador = avisador;
        }

        public async Task<List<InscripcionAfectadaPedidoDTO>> AfectadasAlBorrar(string empresa, int pedido) =>
            (await Vivas(empresa, pedido).ConfigureAwait(false)).Select(v => v.Value).ToList();

        public async Task<List<InscripcionAfectadaPedidoDTO>> AfectadasAlModificar(string empresa, int pedido, IEnumerable<string> productosQueQuedan,
            IEnumerable<string> productosQueTenia = null)
        {
            List<KeyValuePair<EventoInscripcion, InscripcionAfectadaPedidoDTO>> vivas = await Vivas(empresa, pedido).ConfigureAwait(false);
            if (!vivas.Any())
            {
                return new List<InscripcionAfectadaPedidoDTO>();
            }
            var quedan = new HashSet<string>((productosQueQuedan ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            HashSet<string> tenia = productosQueTenia == null ? null : new HashSet<string>(productosQueTenia
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            List<int> idsEventos = vivas.Select(v => v.Key.EventoId).Distinct().ToList();
            Dictionary<int, List<ProductoRequeridoLeido>> requeridos = await repositorio
                .Leer(idsEventos).ConfigureAwait(false) ?? new Dictionary<int, List<ProductoRequeridoLeido>>();
            HashSet<int> exigenTodos = await repositorio.EventosQueExigenTodos(idsEventos).ConfigureAwait(false) ?? new HashSet<int>();
            return vivas
                // Si el evento ya no exige compra, su inscripción no depende del pedido
                .Where(v => requeridos.TryGetValue(v.Key.EventoId, out List<ProductoRequeridoLeido> r) && r.Any()
                    && (exigenTodos.Contains(v.Key.EventoId)
                        ? r.Select(p => p.Producto?.Trim() ?? "").Any(p => (tenia == null || tenia.Contains(p)) && !quedan.Contains(p))
                        : !r.Any(p => quedan.Contains(p.Producto?.Trim() ?? ""))))
                .Select(v => v.Value)
                .ToList();
        }

        private async Task<List<KeyValuePair<EventoInscripcion, InscripcionAfectadaPedidoDTO>>> Vivas(string empresa, int pedido)
        {
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            List<int> ids = await repositorio.InscripcionesDelPedido(empresaLimpia, pedido).ConfigureAwait(false) ?? new List<int>();
            if (!ids.Any())
            {
                return new List<KeyValuePair<EventoInscripcion, InscripcionAfectadaPedidoDTO>>();
            }
            List<string> anulables = new[] { EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Inscrita }.Select(e => e.ToString()).ToList();
            List<EventoInscripcion> inscripciones = (await db.EventosInscripciones.Where(i => ids.Contains(i.Id)).ToListAsync().ConfigureAwait(false))
                .Where(i => anulables.Contains(i.Estado?.Trim()))
                .ToList();
            if (!inscripciones.Any())
            {
                return new List<KeyValuePair<EventoInscripcion, InscripcionAfectadaPedidoDTO>>();
            }
            List<int> idsEventos = inscripciones.Select(i => i.EventoId).Distinct().ToList();
            DateTime hoy = reloj().Date;
            Dictionary<int, Evento> eventos = (await db.Eventos.Where(e => idsEventos.Contains(e.Id)).ToListAsync().ConfigureAwait(false))
                .ToDictionary(e => e.Id);
            List<string> clientes = inscripciones.Select(i => i.Cliente?.Trim()).Distinct().ToList();
            var nombres = (await db.Clientes.Where(c => c.Empresa == empresaLimpia && clientes.Contains(c.Nº_Cliente))
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.Nombre }).ToListAsync().ConfigureAwait(false))
                .GroupBy(c => c.Nº_Cliente?.Trim() + "/" + c.Contacto?.Trim())
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim());
            return inscripciones
                .Where(i => eventos.TryGetValue(i.EventoId, out Evento e) && e.Fecha.Date >= hoy)
                .OrderBy(i => i.Id)
                .Select(i =>
                {
                    Evento evento = eventos[i.EventoId];
                    nombres.TryGetValue(i.Cliente?.Trim() + "/" + i.Contacto?.Trim(), out string nombre);
                    return new KeyValuePair<EventoInscripcion, InscripcionAfectadaPedidoDTO>(i, new InscripcionAfectadaPedidoDTO
                    {
                        Id = i.Id,
                        EventoId = i.EventoId,
                        Evento = evento.Titulo?.Trim(),
                        FechaEvento = evento.Fecha.Date,
                        Almacen = evento.Almacen?.Trim(),
                        Empresa = i.Empresa?.Trim(),
                        Cliente = i.Cliente?.Trim(),
                        Contacto = i.Contacto?.Trim(),
                        Nombre = nombre,
                        UsuarioCreacion = i.UsuarioCreacion?.Trim(),
                        Origen = i.Origen?.Trim()
                    });
                })
                .ToList();
        }

        public async Task<List<InscripcionAfectadaPedidoDTO>> Anular(List<InscripcionAfectadaPedidoDTO> afectadas, int pedido, bool borrarPedido, string usuario)
        {
            var anuladas = new List<InscripcionAfectadaPedidoDTO>();
            if (afectadas == null || !afectadas.Any())
            {
                return anuladas;
            }
            List<int> ids = afectadas.Select(a => a.Id).ToList();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones.Where(i => ids.Contains(i.Id)).ToListAsync().ConfigureAwait(false);
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            string motivo = ReglasProductosRequeridosEvento.MotivoAnulacion(pedido, borrarPedido);
            foreach (EventoInscripcion inscripcion in inscripciones)
            {
                EstadoInscripcionEvento actual = ReglasInscripcionEvento.Estado(inscripcion.Estado) ?? EstadoInscripcionEvento.Anulada;
                if (!ReglasInscripcionEvento.PuedePasar(actual, EstadoInscripcionEvento.Anulada))
                {
                    continue;
                }
                inscripcion.Estado = EstadoInscripcionEvento.Anulada.ToString();
                inscripcion.Usuario = usuarioAuditoria;
                inscripcion.FechaModificacion = reloj();
                db.Modificaciones.Add(new Modificacion
                {
                    Tabla = "EventosInscripciones",
                    Anterior = $"Inscripción {inscripcion.Id} (evento {inscripcion.EventoId}, cliente {inscripcion.Cliente?.Trim()}/{inscripcion.Contacto?.Trim()}) " +
                        $"{ReglasInscripcionEvento.Texto(actual)}, justificada por el pedido {pedido}",
                    Nuevo = $"Anulada: {motivo} (NestoAPI#732)",
                    Usuario = usuarioAuditoria
                });
                anuladas.Add(afectadas.First(a => a.Id == inscripcion.Id));
            }
            if (!anuladas.Any())
            {
                return anuladas;
            }
            await db.SaveChangesAsync().ConfigureAwait(false);
            foreach (InscripcionAfectadaPedidoDTO anulada in anuladas)
            {
                try
                {
                    if (avisador != null)
                    {
                        await avisador.Avisar(anulada, pedido, motivo, usuarioAuditoria).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Eventos #732] La inscripción {anulada.Id} está anulada, pero no se ha podido avisar a la tienda: {ex.Message}", ex));
                }
            }
            return anuladas;
        }
    }

    /// <summary>
    /// NestoAPI#732: como el resto de avisos de las inscripciones (#611): correo a quien mantiene la tienda del evento (o al buzón
    /// de la tienda) y campana/push a quien la inscribió si fue alguien de la casa.
    /// </summary>
    public class AvisadorInscripcionAnuladaPorPedido : IAvisadorInscripcionAnuladaPorPedido
    {
        public const string TIPO_NOTIFICACION = "InscripcionEventoAnuladaPorPedido";
        private readonly NVEntities db;
        private readonly IServicioCorreoElectronico correo;
        private readonly Func<string, NotificacionPushDTO, Task> avisar;

        public AvisadorInscripcionAnuladaPorPedido(NVEntities db)
            : this(db, new ServicioCorreoElectronico(), (usuario, notificacion) => AvisoCobroNestoPago.Avisar(usuario, notificacion, new ServicioNotificacionesPush()))
        {
        }

        internal AvisadorInscripcionAnuladaPorPedido(NVEntities db, IServicioCorreoElectronico correo, Func<string, NotificacionPushDTO, Task> avisar)
        {
            this.db = db;
            this.correo = correo;
            this.avisar = avisar;
        }

        public async Task Avisar(InscripcionAfectadaPedidoDTO inscripcion, int pedido, string motivo, string usuario)
        {
            List<string> destinatarios = await InscripcionesEventosJobsService.LeerCorreosTienda(db,
                inscripcion.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO, inscripcion.Almacen).ConfigureAwait(false) ?? new List<string>();
            if (!destinatarios.Any())
            {
                destinatarios.Add(InscripcionesEventosJobsService.CorreoGeneralTienda(inscripcion.Almacen));
            }
            using (MailMessage mail = Correo(inscripcion, pedido, motivo, usuario, destinatarios))
            {
                if (!correo.EnviarCorreoSMTP(mail))
                {
                    ElmahHelper.Log(new Exception($"[Eventos #732] No se ha podido mandar el correo de la inscripción {inscripcion.Id} anulada."));
                }
            }
            bool deLaCasa = !string.Equals(inscripcion.Origen, OrigenInscripcionEvento.TNV.ToString(), StringComparison.OrdinalIgnoreCase);
            if (deLaCasa && !string.IsNullOrWhiteSpace(inscripcion.UsuarioCreacion) && avisar != null)
            {
                await avisar(inscripcion.UsuarioCreacion, Notificacion(inscripcion, pedido, motivo)).ConfigureAwait(false);
            }
        }

        internal static NotificacionPushDTO Notificacion(InscripcionAfectadaPedidoDTO inscripcion, int pedido, string motivo) => new NotificacionPushDTO
        {
            Titulo = "Inscripción anulada",
            Cuerpo = $"Se ha anulado la inscripción de {inscripcion.Cliente}/{inscripcion.Contacto}" +
                $"{(string.IsNullOrWhiteSpace(inscripcion.Nombre) ? "" : $" ({inscripcion.Nombre})")} a «{inscripcion.Evento}»: {motivo}.",
            Tipo = TIPO_NOTIFICACION,
            Datos = new Dictionary<string, string>
            {
                ["tipo"] = TIPO_NOTIFICACION,
                ["empresa"] = inscripcion.Empresa,
                ["cliente"] = inscripcion.Cliente,
                ["contacto"] = inscripcion.Contacto,
                ["eventoId"] = inscripcion.EventoId.ToString(CultureInfo.InvariantCulture),
                ["inscripcionId"] = inscripcion.Id.ToString(CultureInfo.InvariantCulture),
                ["pedido"] = pedido.ToString(CultureInfo.InvariantCulture)
            }
        };

        internal static MailMessage Correo(InscripcionAfectadaPedidoDTO inscripcion, int pedido, string motivo, string usuario, List<string> destinatarios)
        {
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = $"Inscripción anulada en «{inscripcion.Evento}» ({inscripcion.FechaEvento:dd/MM/yyyy}): {inscripcion.Cliente}/{inscripcion.Contacto}",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in destinatarios)
            {
                mail.To.Add(destinatario);
            }
            string nombre = string.IsNullOrWhiteSpace(inscripcion.Nombre) ? "" : $" ({WebUtility.HtmlEncode(inscripcion.Nombre)})";
            mail.Body = "<!DOCTYPE html><html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>" +
                $"<p>Se ha anulado la inscripción gratuita por compra del cliente {WebUtility.HtmlEncode(inscripcion.Cliente)}/" +
                $"{WebUtility.HtmlEncode(inscripcion.Contacto)}{nombre} a «{WebUtility.HtmlEncode(inscripcion.Evento)}» " +
                $"({inscripcion.FechaEvento:dd/MM/yyyy}, {WebUtility.HtmlEncode(PermisosEventos.NombreAlmacen(inscripcion.Almacen))}): " +
                $"{WebUtility.HtmlEncode(motivo)}.</p>" +
                $"<p>Lo ha hecho {WebUtility.HtmlEncode(usuario)} al tocar el pedido {pedido}. La plaza queda libre. Si el cliente vuelve a " +
                "pedir el producto, se le puede inscribir de nuevo.</p>" +
                "<p style='color:#666'>Eventos gratuitos por compra (NestoAPI#732).</p></body></html>";
            return mail;
        }
    }
}
