using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>
    /// NestoAPI#581: «mientras tanto, servid la 45685 en lugar de la 25539». Lo que ve quien mete el pedido al
    /// meter el producto (Nesto, NestoApp) y lo que mantiene Compras desde la ficha del producto.
    /// </summary>
    public class SustitucionProductoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string NombreProducto { get; set; }
        public string ProductoSustituto { get; set; }
        public string NombreSustituto { get; set; }
        public string Motivo { get; set; }
        /// <summary>Deja de avisar en cuanto hay disponible suficiente del original (se apaga sola al llegar la mercancía).</summary>
        public bool MientrasNoHayaStock { get; set; }
        /// <summary>Último día en que avisa (incluido). Null: sin fecha (solo por stock).</summary>
        public DateTime? FechaHasta { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string UsuarioAnulacion { get; set; }
        /// <summary>Vigente, Sin efecto: hay stock, Caducada o Anulada.</summary>
        public string Estado { get; set; }
        /// <summary>True si ahora mismo hay que avisar (para la cantidad pedida, en el GET de la vigente).</summary>
        public bool Vigente { get; set; }
        /// <summary>El texto para el usuario, igual en todos los clientes: «Compras pide servir la 45685 (…) en lugar de la 25539 …».</summary>
        public string Aviso { get; set; }
    }

    public class NuevaSustitucionProductoDTO
    {
        public string Empresa { get; set; }
        public string ProductoSustituto { get; set; }
        public string Motivo { get; set; }
        /// <summary>Por defecto, true (como SAP y Dynamics: avisa mientras no hay stock del original).</summary>
        public bool MientrasNoHayaStock { get; set; } = true;
        public DateTime? FechaHasta { get; set; }
    }

    /// <summary>Una fila de dbo.ProductosSustituciones con los nombres de los dos productos.</summary>
    public class SustitucionProductoFila
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public string ProductoSustituto { get; set; }
        public string Motivo { get; set; }
        public bool MientrasNoHayaStock { get; set; }
        public DateTime? FechaHasta { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaAnulacion { get; set; }
        public string UsuarioAnulacion { get; set; }
        public string NombreProducto { get; set; }
        public string NombreSustituto { get; set; }
    }

    public enum EstadoOperacionSustitucion
    {
        Ok,
        NoEncontrado,
        Invalido
    }

    public class ResultadoSustitucion
    {
        public EstadoOperacionSustitucion Estado { get; set; }
        public string Mensaje { get; set; }
        public SustitucionProductoDTO Sustitucion { get; set; }

        public static ResultadoSustitucion Invalido(string mensaje) => new ResultadoSustitucion { Estado = EstadoOperacionSustitucion.Invalido, Mensaje = mensaje };
        public static ResultadoSustitucion NoEncontrado(string mensaje) => new ResultadoSustitucion { Estado = EstadoOperacionSustitucion.NoEncontrado, Mensaje = mensaje };
    }

    public interface IRepositorioSustitucionesProducto
    {
        /// <summary>La no anulada del producto, o null (también si la tabla aún no existe).</summary>
        Task<SustitucionProductoFila> LeerActiva(string empresa, string producto);

        /// <summary>Todas las del producto, de la más nueva a la más vieja. Vacía si la tabla aún no existe.</summary>
        Task<List<SustitucionProductoFila>> Listar(string empresa, string producto);

        /// <summary>El nombre del producto, o null si no existe.</summary>
        Task<string> NombreProducto(string empresa, string producto);

        /// <summary>Anula la activa del producto (si la hay) e inserta la nueva, en una transacción. Devuelve el Id.</summary>
        Task<int> Crear(SustitucionProductoFila fila);

        /// <summary>False si no existía o ya estaba anulada.</summary>
        Task<bool> Anular(string empresa, string producto, int id, string usuario);
    }

    public interface IServicioSustitucionesProducto
    {
        /// <summary>La sustitución que hay que avisar ahora al pedir esa cantidad del producto, o null.</summary>
        Task<SustitucionProductoDTO> Vigente(string empresa, string producto, int cantidad);

        Task<List<SustitucionProductoDTO>> Listar(string empresa, string producto);

        Task<ResultadoSustitucion> Crear(string producto, NuevaSustitucionProductoDTO nueva, string usuario);

        Task<bool> Anular(string empresa, string producto, int id, string usuario);
    }

    public class ServicioSustitucionesProducto : IServicioSustitucionesProducto
    {
        public const string ESTADO_VIGENTE = "Vigente";
        public const string ESTADO_HAY_STOCK = "Sin efecto: hay stock";
        public const string ESTADO_CADUCADA = "Caducada";
        public const string ESTADO_ANULADA = "Anulada";
        public const int LONGITUD_MOTIVO = 250;

        private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-ES");

        private readonly IRepositorioSustitucionesProducto repositorio;
        private readonly Func<string, int> disponibleTodosLosAlmacenes;
        private readonly Func<DateTime> hoy;

        public ServicioSustitucionesProducto()
            : this(new RepositorioSustitucionesProducto(),
                  producto => new GestorStocks(new ServicioGestorStocks()).UnidadesDisponiblesTodosLosAlmacenes(producto),
                  () => DateTime.Today)
        {
        }

        public ServicioSustitucionesProducto(IRepositorioSustitucionesProducto repositorio, Func<string, int> disponibleTodosLosAlmacenes, Func<DateTime> hoy)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.disponibleTodosLosAlmacenes = disponibleTodosLosAlmacenes ?? throw new ArgumentNullException(nameof(disponibleTodosLosAlmacenes));
            this.hoy = hoy ?? throw new ArgumentNullException(nameof(hoy));
        }

        public async Task<SustitucionProductoDTO> Vigente(string empresa, string producto, int cantidad)
        {
            string numero = producto?.Trim();
            if (string.IsNullOrEmpty(numero))
            {
                return null;
            }
            SustitucionProductoFila fila = await repositorio.LeerActiva(empresa, numero).ConfigureAwait(false);
            if (fila == null || EstaCaducada(fila, hoy()))
            {
                return null;   // ni se mira el stock
            }
            int? disponible = fila.MientrasNoHayaStock ? disponibleTodosLosAlmacenes(numero) : (int?)null;
            SustitucionProductoDTO dto = ADto(fila, hoy(), disponible, cantidad);
            return dto.Vigente ? dto : null;
        }

        public async Task<List<SustitucionProductoDTO>> Listar(string empresa, string producto)
        {
            string numero = producto?.Trim();
            if (string.IsNullOrEmpty(numero))
            {
                return new List<SustitucionProductoDTO>();
            }
            List<SustitucionProductoFila> filas = await repositorio.Listar(empresa, numero).ConfigureAwait(false) ?? new List<SustitucionProductoFila>();
            DateTime dia = hoy();
            // El stock, una vez y solo si hace falta
            int? disponible = filas.Any(f => f.FechaAnulacion == null && f.MientrasNoHayaStock && !EstaCaducada(f, dia))
                ? disponibleTodosLosAlmacenes(numero)
                : (int?)null;
            return filas
                .Select(f => ADto(f, dia, disponible, 1))
                .OrderBy(d => d.FechaAnulacion == null ? 0 : 1)
                .ThenByDescending(d => d.FechaCreacion)
                .ToList();
        }

        public async Task<ResultadoSustitucion> Crear(string producto, NuevaSustitucionProductoDTO nueva, string usuario)
        {
            if (nueva == null)
            {
                return ResultadoSustitucion.Invalido("Faltan los datos de la sustitución.");
            }
            string empresa = Empresa(nueva.Empresa);
            string numero = producto?.Trim();
            string sustituto = nueva.ProductoSustituto?.Trim();
            if (string.IsNullOrEmpty(numero) || string.IsNullOrEmpty(sustituto))
            {
                return ResultadoSustitucion.Invalido("Hay que indicar el producto y el que lo sustituye.");
            }
            if (string.Equals(numero, sustituto, StringComparison.OrdinalIgnoreCase))
            {
                return ResultadoSustitucion.Invalido("Un producto no se puede sustituir por sí mismo.");
            }
            if (!nueva.MientrasNoHayaStock && !nueva.FechaHasta.HasValue)
            {
                return ResultadoSustitucion.Invalido("Hay que decir hasta cuándo: mientras no haya stock, hasta una fecha o las dos cosas.");
            }
            if (nueva.FechaHasta.HasValue && nueva.FechaHasta.Value.Date < hoy().Date)
            {
                return ResultadoSustitucion.Invalido("La fecha «hasta» ya ha pasado.");
            }
            string motivo = string.IsNullOrWhiteSpace(nueva.Motivo) ? null : nueva.Motivo.Trim();
            if (motivo != null && motivo.Length > LONGITUD_MOTIVO)
            {
                return ResultadoSustitucion.Invalido($"El motivo no puede pasar de {LONGITUD_MOTIVO} caracteres.");
            }

            string nombreProducto = await repositorio.NombreProducto(empresa, numero).ConfigureAwait(false);
            if (nombreProducto == null)
            {
                return ResultadoSustitucion.NoEncontrado($"El producto {numero} no existe.");
            }
            string nombreSustituto = await repositorio.NombreProducto(empresa, sustituto).ConfigureAwait(false);
            if (nombreSustituto == null)
            {
                return ResultadoSustitucion.Invalido($"El producto {sustituto} no existe.");
            }
            SustitucionProductoFila delSustituto = await repositorio.LeerActiva(empresa, sustituto).ConfigureAwait(false);
            if (delSustituto != null && !EstaCaducada(delSustituto, hoy())
                && string.Equals(delSustituto.ProductoSustituto?.Trim(), numero, StringComparison.OrdinalIgnoreCase))
            {
                return ResultadoSustitucion.Invalido($"El {sustituto} ya se sustituye por el {numero}: se avisarían el uno al otro. Anula antes esa.");
            }

            var fila = new SustitucionProductoFila
            {
                Empresa = empresa,
                Producto = numero,
                ProductoSustituto = sustituto,
                Motivo = motivo,
                MientrasNoHayaStock = nueva.MientrasNoHayaStock,
                FechaHasta = nueva.FechaHasta?.Date,
                Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario),
                FechaCreacion = DateTime.Now,
                NombreProducto = nombreProducto,
                NombreSustituto = nombreSustituto
            };
            fila.Id = await repositorio.Crear(fila).ConfigureAwait(false);
            int? disponible = fila.MientrasNoHayaStock ? disponibleTodosLosAlmacenes(numero) : (int?)null;
            return new ResultadoSustitucion { Estado = EstadoOperacionSustitucion.Ok, Sustitucion = ADto(fila, hoy(), disponible, 1) };
        }

        public Task<bool> Anular(string empresa, string producto, int id, string usuario)
        {
            return repositorio.Anular(Empresa(empresa), producto?.Trim(), id, UsuarioAuditoriaHelper.ParaAuditoria(usuario));
        }

        /// <summary>La fecha «hasta» está incluida: el día 31 todavía avisa; el 1 ya no.</summary>
        public static bool EstaCaducada(SustitucionProductoFila fila, DateTime hoy)
        {
            return fila.FechaHasta.HasValue && hoy.Date > fila.FechaHasta.Value.Date;
        }

        /// <summary>
        /// «Mientras no haya stock» = mientras el disponible de todos los almacenes (stock menos lo pendiente de servir,
        /// el mismo número que enseña la plantilla) no llegue a lo que se pide. Con 5 disponibles y 100 pedidas, avisa.
        /// </summary>
        public static bool HayStockSuficiente(int disponible, int cantidad)
        {
            return disponible >= Math.Max(cantidad, 1);
        }

        private static SustitucionProductoDTO ADto(SustitucionProductoFila fila, DateTime hoy, int? disponible, int cantidad)
        {
            string estado;
            if (fila.FechaAnulacion.HasValue)
            {
                estado = ESTADO_ANULADA;
            }
            else if (EstaCaducada(fila, hoy))
            {
                estado = ESTADO_CADUCADA;
            }
            else if (fila.MientrasNoHayaStock && disponible.HasValue && HayStockSuficiente(disponible.Value, cantidad))
            {
                estado = ESTADO_HAY_STOCK;
            }
            else
            {
                estado = ESTADO_VIGENTE;
            }
            return new SustitucionProductoDTO
            {
                Id = fila.Id,
                Empresa = fila.Empresa?.Trim(),
                Producto = fila.Producto?.Trim(),
                NombreProducto = fila.NombreProducto?.Trim(),
                ProductoSustituto = fila.ProductoSustituto?.Trim(),
                NombreSustituto = fila.NombreSustituto?.Trim(),
                Motivo = fila.Motivo,
                MientrasNoHayaStock = fila.MientrasNoHayaStock,
                FechaHasta = fila.FechaHasta,
                Usuario = fila.Usuario,
                FechaCreacion = fila.FechaCreacion,
                FechaAnulacion = fila.FechaAnulacion,
                UsuarioAnulacion = fila.UsuarioAnulacion,
                Estado = estado,
                Vigente = estado == ESTADO_VIGENTE,
                Aviso = TextoAviso(fila)
            };
        }

        /// <summary>
        /// «Compras pide servir la 45685 (PANTALON PRESOTERAPIA COD040308) en lugar de la 25539 mientras no haya stock,
        /// como mucho hasta el 31/10/2026. Motivo: el proveedor tarda.»
        /// </summary>
        public static string TextoAviso(SustitucionProductoFila fila)
        {
            string sustituto = fila.ProductoSustituto?.Trim();
            string nombre = fila.NombreSustituto?.Trim();
            string texto = $"Compras pide servir la {sustituto}" + (string.IsNullOrEmpty(nombre) ? string.Empty : $" ({nombre})") +
                $" en lugar de la {fila.Producto?.Trim()}";
            string hasta = fila.FechaHasta?.ToString("dd/MM/yyyy", Espanol);
            if (fila.MientrasNoHayaStock && hasta != null)
            {
                texto += $" mientras no haya stock, como mucho hasta el {hasta}";
            }
            else if (fila.MientrasNoHayaStock)
            {
                texto += " mientras no haya stock";
            }
            else if (hasta != null)
            {
                texto += $" hasta el {hasta}";
            }
            texto += ".";
            if (!string.IsNullOrWhiteSpace(fila.Motivo))
            {
                string motivo = fila.Motivo.Trim();
                texto += $" Motivo: {motivo}" + (motivo.EndsWith(".") ? string.Empty : ".");
            }
            return texto;
        }

        private static string Empresa(string empresa)
        {
            return string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
        }
    }

    /// <summary>
    /// NestoAPI#581: SQL parametrizado sobre dbo.ProductosSustituciones (Scripts/Issue581_ProductosSustituciones.sql),
    /// fuera del EDMX. Las lecturas comprueban antes que la tabla exista: la API se puede publicar antes que el script
    /// y entonces, sencillamente, no hay sustituciones.
    /// </summary>
    public class RepositorioSustitucionesProducto : IRepositorioSustitucionesProducto
    {
        private const string SI_EXISTE_LA_TABLA = "IF OBJECT_ID(N'dbo.ProductosSustituciones', N'U') IS NOT NULL ";

        private const string SELECT_FILAS = @"
            SELECT s.Id, RTRIM(s.Empresa) AS Empresa, RTRIM(s.Producto) AS Producto, RTRIM(s.ProductoSustituto) AS ProductoSustituto,
                   s.Motivo, s.MientrasNoHayaStock, CAST(s.FechaHasta AS datetime) AS FechaHasta, s.Usuario, s.FechaCreacion,
                   s.FechaAnulacion, s.UsuarioAnulacion, RTRIM(p.Nombre) AS NombreProducto, RTRIM(ps.Nombre) AS NombreSustituto
            FROM dbo.ProductosSustituciones s
            LEFT JOIN Productos p ON p.Empresa = s.Empresa AND p.Número = s.Producto
            LEFT JOIN Productos ps ON ps.Empresa = s.Empresa AND ps.Número = s.ProductoSustituto
            WHERE s.Empresa = @empresa AND s.Producto = @producto ";

        // Sin tabla, el mismo resultado vacío (con columnas, para que SqlQuery lo materialice)
        private const string SELECT_VACIO = @"
            ELSE SELECT CAST(0 AS int) AS Id, CAST(NULL AS varchar(3)) AS Empresa, CAST(NULL AS varchar(15)) AS Producto,
                   CAST(NULL AS varchar(15)) AS ProductoSustituto, CAST(NULL AS nvarchar(250)) AS Motivo, CAST(0 AS bit) AS MientrasNoHayaStock,
                   CAST(NULL AS datetime) AS FechaHasta, CAST(NULL AS nvarchar(50)) AS Usuario, GETDATE() AS FechaCreacion,
                   CAST(NULL AS datetime) AS FechaAnulacion, CAST(NULL AS nvarchar(50)) AS UsuarioAnulacion,
                   CAST(NULL AS varchar(50)) AS NombreProducto, CAST(NULL AS varchar(50)) AS NombreSustituto WHERE 1 = 0";

        public async Task<SustitucionProductoFila> LeerActiva(string empresa, string producto)
        {
            using (var db = new NVEntities())
            {
                List<SustitucionProductoFila> filas = await db.Database.SqlQuery<SustitucionProductoFila>(
                    SI_EXISTE_LA_TABLA + SELECT_FILAS + "AND s.FechaAnulacion IS NULL " + SELECT_VACIO,
                    Parametros(empresa, producto)).ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task<List<SustitucionProductoFila>> Listar(string empresa, string producto)
        {
            using (var db = new NVEntities())
            {
                return await db.Database.SqlQuery<SustitucionProductoFila>(
                    SI_EXISTE_LA_TABLA + SELECT_FILAS + "ORDER BY s.FechaCreacion DESC, s.Id DESC " + SELECT_VACIO,
                    Parametros(empresa, producto)).ToListAsync().ConfigureAwait(false);
            }
        }

        public async Task<string> NombreProducto(string empresa, string producto)
        {
            using (var db = new NVEntities())
            {
                List<string> nombres = await db.Database.SqlQuery<string>(
                    "SELECT RTRIM(ISNULL(Nombre, '')) FROM Productos WHERE Empresa = @empresa AND Número = @producto",
                    Parametros(empresa, producto)).ToListAsync().ConfigureAwait(false);
                return nombres.FirstOrDefault();
            }
        }

        public async Task<int> Crear(SustitucionProductoFila fila)
        {
            using (var db = new NVEntities())
            {
                // Una sola activa por producto (UX_ProductosSustituciones_Activa): la anterior se anula en el mismo lote
                List<int> ids = await db.Database.SqlQuery<int>(@"
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;
                    BEGIN TRANSACTION;
                    UPDATE dbo.ProductosSustituciones WITH (UPDLOCK, HOLDLOCK)
                       SET FechaAnulacion = GETDATE(), UsuarioAnulacion = @usuario
                     WHERE Empresa = @empresa AND Producto = @producto AND FechaAnulacion IS NULL;
                    INSERT INTO dbo.ProductosSustituciones (Empresa, Producto, ProductoSustituto, Motivo, MientrasNoHayaStock, FechaHasta, Usuario, FechaCreacion)
                    OUTPUT INSERTED.Id
                    VALUES (@empresa, @producto, @sustituto, @motivo, @mientrasNoHayaStock, @fechaHasta, @usuario, @fechaCreacion);
                    COMMIT TRANSACTION;",
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = fila.Empresa },
                    new SqlParameter("@producto", SqlDbType.Char, 15) { Value = fila.Producto },
                    new SqlParameter("@sustituto", SqlDbType.Char, 15) { Value = fila.ProductoSustituto },
                    new SqlParameter("@motivo", SqlDbType.NVarChar, 250) { Value = (object)fila.Motivo ?? DBNull.Value },
                    new SqlParameter("@mientrasNoHayaStock", SqlDbType.Bit) { Value = fila.MientrasNoHayaStock },
                    new SqlParameter("@fechaHasta", SqlDbType.Date) { Value = (object)fila.FechaHasta ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)fila.Usuario ?? DBNull.Value },
                    new SqlParameter("@fechaCreacion", SqlDbType.DateTime) { Value = fila.FechaCreacion })
                    .ToListAsync().ConfigureAwait(false);
                return ids.Single();
            }
        }

        public async Task<bool> Anular(string empresa, string producto, int id, string usuario)
        {
            using (var db = new NVEntities())
            {
                int filas = await db.Database.ExecuteSqlCommandAsync(@"
                    UPDATE dbo.ProductosSustituciones SET FechaAnulacion = GETDATE(), UsuarioAnulacion = @usuario
                     WHERE Id = @id AND Empresa = @empresa AND Producto = @producto AND FechaAnulacion IS NULL",
                    new SqlParameter("@id", id),
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa ?? string.Empty },
                    new SqlParameter("@producto", SqlDbType.Char, 15) { Value = producto ?? string.Empty },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)usuario ?? DBNull.Value })
                    .ConfigureAwait(false);
                return filas > 0;
            }
        }

        private static SqlParameter[] Parametros(string empresa, string producto)
        {
            return new[]
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim() },
                new SqlParameter("@producto", SqlDbType.Char, 15) { Value = producto?.Trim() ?? string.Empty }
            };
        }
    }
}
