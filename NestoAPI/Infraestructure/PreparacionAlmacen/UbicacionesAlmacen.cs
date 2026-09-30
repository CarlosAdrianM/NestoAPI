using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Unidades pendientes de colocar de un producto, por su origen.</summary>
    public class FilaPendienteDeUbicar
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
        public int? PedidoCompra { get; set; }
        public int? AlbaranCompra { get; set; }
        public int? TraspasoReposicion { get; set; }
        public int Cantidad { get; set; }
        public DateTime? DesdeCuando { get; set; }
    }

    /// <summary>Lo que hay de un producto en un hueco (estado 0) o sin hueco todavía (estado 2).</summary>
    public class FilaUbicacionProducto
    {
        public string Producto { get; set; }
        public int Estado { get; set; }
        public string Pasillo { get; set; }
        public string Fila { get; set; }
        public string Columna { get; set; }
        public int Cantidad { get; set; }
    }

    public class FilaProductoAlmacen
    {
        public string Producto { get; set; }
        public string Descripcion { get; set; }
        public string CodigoBarras { get; set; }
    }

    public interface IRepositorioUbicacionesAlmacen
    {
        Task<List<FilaPendienteDeUbicar>> LeerPendienteDeUbicar(string empresa, string almacen);
        /// <summary>Los huecos (estado 0) de los productos que tienen algo pendiente de colocar en ese almacén.</summary>
        Task<List<FilaUbicacionProducto>> LeerHuecosDeLoPendiente(string empresa, string almacen);
        Task<List<FilaProductoAlmacen>> BuscarProductos(string empresa, string codigo);
        /// <summary>Huecos (estado 0) y pendiente de colocar (estado 2) de un producto.</summary>
        Task<List<FilaUbicacionProducto>> LeerUbicacionesDelProducto(string empresa, string almacen, string producto);
        /// <summary>
        /// Coloca con prdUbicar, el mismo procedimiento que usa Ariadna Vieja, y deja anotado quién lo ha hecho.
        /// </summary>
        /// <param name="tipoFiltro">0 sin filtro, 1 pedido de compra, 2 albarán de compra, 3 reposición, 4 pedido de venta.</param>
        Task Ubicar(string empresa, UbicarProductoDTO ubicar, int tipoFiltro, int? filtro, string usuario);
    }

    /// <summary>
    /// NestoAPI#556/#559: la tabla Ubicaciones para Ariadna. Estados que importan aquí:
    /// 0 = colocado en un hueco; 2 = recibido y pendiente de colocar (lo deja así el albarán de compra,
    /// y es lo que hoy se coloca con Ariadna Vieja llamando a prdUbicar); 3 = reservado para un picking.
    /// Colocar se hace con el mismo prdUbicar: aquí no se escribe nada a mano en Ubicaciones.
    /// </summary>
    public class RepositorioUbicacionesAlmacen : IRepositorioUbicacionesAlmacen
    {
        private readonly Database baseDeDatos;

        public RepositorioUbicacionesAlmacen(NVEntities db) : this(db.Database)
        {
        }

        internal RepositorioUbicacionesAlmacen(Database baseDeDatos)
        {
            this.baseDeDatos = baseDeDatos;
        }

        internal const string SQL_PENDIENTE_DE_UBICAR = @"
SELECT RTRIM(u.[Número]) AS Producto, RTRIM(MAX(p.Nombre)) AS Descripcion, RTRIM(MAX(p.CodBarras)) AS CodigoBarras,
       u.PedidoCmp AS PedidoCompra, u.[AlbaránCmp] AS AlbaranCompra, u.[NºTraspasoRepo] AS TraspasoReposicion,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad, MIN(u.[FechaCreación]) AS DesdeCuando
FROM Ubicaciones u
     LEFT JOIN Productos p ON p.Empresa = u.Empresa AND p.[Número] = u.[Número]
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.Estado = 2
GROUP BY u.[Número], u.PedidoCmp, u.[AlbaránCmp], u.[NºTraspasoRepo]
HAVING SUM(u.Cantidad) <> 0";

        internal const string SQL_HUECOS_DE_LO_PENDIENTE = @"
SELECT RTRIM(u.[Número]) AS Producto, u.Estado, RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.Estado = 0
      AND u.[Número] IN (SELECT x.[Número] FROM Ubicaciones x WHERE x.Empresa = @p0 AND x.[Almacén] = @p1 AND x.Estado = 2)
GROUP BY u.[Número], u.Estado, u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) <> 0";

        internal const string SQL_BUSCAR_PRODUCTOS = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Descripcion, RTRIM(p.CodBarras) AS CodigoBarras
FROM Productos p
WHERE p.Empresa = @p0 AND (p.CodBarras = @p1 OR p.[Número] = @p1)";

        internal const string SQL_UBICACIONES_DEL_PRODUCTO = @"
SELECT RTRIM(u.[Número]) AS Producto, u.Estado, RTRIM(u.Pasillo) AS Pasillo, RTRIM(u.Fila) AS Fila, RTRIM(u.Columna) AS Columna,
       CAST(SUM(u.Cantidad) AS int) AS Cantidad
FROM Ubicaciones u
WHERE u.Empresa = @p0 AND u.[Almacén] = @p1 AND u.[Número] = @p2 AND u.Estado IN (0, 2)
GROUP BY u.[Número], u.Estado, u.Pasillo, u.Fila, u.Columna
HAVING SUM(u.Cantidad) <> 0";

        // El procedimiento recibe el filtro como texto (char(1000)) y lo compara con la columna entera
        internal const string SQL_UBICAR = @"
DECLARE @resultado int;
EXEC @resultado = prdUbicar @Empresa = @p0, @Almacen = @p1, @Producto = @p2, @Pasillo = @p3, @Fila = @p4, @Columna = @p5,
                            @Cantidad = @p6, @TipoFiltro = @p7, @Filtro = @p8;
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (N'Ubicaciones', @p9, @p10, @p11);";

        public async Task Ubicar(string empresa, UbicarProductoDTO ubicar, int tipoFiltro, int? filtro, string usuario)
        {
            string origen = tipoFiltro == 0 ? "lo más antiguo" : $"filtro {tipoFiltro} = {filtro}";
            _ = await baseDeDatos.ExecuteSqlCommandAsync(SQL_UBICAR,
                new SqlParameter("@p0", empresa),
                new SqlParameter("@p1", ubicar.Almacen),
                new SqlParameter("@p2", ubicar.Producto),
                new SqlParameter("@p3", ubicar.Pasillo),
                new SqlParameter("@p4", ubicar.Fila),
                new SqlParameter("@p5", ubicar.Columna),
                new SqlParameter("@p6", ubicar.Cantidad),
                new SqlParameter("@p7", tipoFiltro),
                new SqlParameter("@p8", filtro.HasValue ? filtro.Value.ToString() : string.Empty),
                new SqlParameter("@p9", $"Producto {ubicar.Producto} pendiente de ubicar en {ubicar.Almacen} ({origen})"),
                new SqlParameter("@p10", $"{ubicar.Cantidad} unidades en {ubicar.Pasillo}/{ubicar.Fila}/{ubicar.Columna} (Ariadna)"),
                new SqlParameter("@p11", usuario)).ConfigureAwait(false);
        }

        public Task<List<FilaPendienteDeUbicar>> LeerPendienteDeUbicar(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<FilaPendienteDeUbicar>(SQL_PENDIENTE_DE_UBICAR, empresa, almacen).ToListAsync();
        }

        public Task<List<FilaUbicacionProducto>> LeerHuecosDeLoPendiente(string empresa, string almacen)
        {
            return baseDeDatos.SqlQuery<FilaUbicacionProducto>(SQL_HUECOS_DE_LO_PENDIENTE, empresa, almacen).ToListAsync();
        }

        public Task<List<FilaProductoAlmacen>> BuscarProductos(string empresa, string codigo)
        {
            // char(15) las dos columnas: un parámetro más largo no puede coincidir con nada
            return baseDeDatos.SqlQuery<FilaProductoAlmacen>(SQL_BUSCAR_PRODUCTOS,
                new SqlParameter("@p0", empresa),
                new SqlParameter("@p1", System.Data.SqlDbType.VarChar, 15) { Value = codigo }).ToListAsync();
        }

        public Task<List<FilaUbicacionProducto>> LeerUbicacionesDelProducto(string empresa, string almacen, string producto)
        {
            return baseDeDatos.SqlQuery<FilaUbicacionProducto>(SQL_UBICACIONES_DEL_PRODUCTO, empresa, almacen, producto).ToListAsync();
        }
    }

    public interface IServicioUbicacionesAlmacen
    {
        Task<PendienteDeUbicarDTO> LeerPendienteDeUbicar(string empresa, string almacen);
        Task<List<ProductoAlmacenDTO>> BuscarProducto(string empresa, string almacen, string codigo);
        /// <summary>Coloca unidades pendientes en un hueco y devuelve cómo queda el producto.</summary>
        Task<ProductoAlmacenDTO> Ubicar(string empresa, UbicarProductoDTO ubicar, string usuario);
    }

    /// <summary>
    /// NestoAPI#556/#559: qué hay recibido y sin colocar, con la sugerencia de dónde ponerlo, y dónde
    /// está un producto. Es la parte de lectura de lo que hoy hace Ariadna Vieja.
    /// </summary>
    public class ServicioUbicacionesAlmacen : IServicioUbicacionesAlmacen, IDisposable
    {
        public const int ESTADO_COLOCADO = 0;
        public const int ESTADO_PENDIENTE_DE_UBICAR = 2;
        /// <summary>Un código de barras o un número de producto no pasa de aquí.</summary>
        public const int LONGITUD_MAXIMA_CODIGO = 15;

        private readonly IRepositorioUbicacionesAlmacen repositorio;
        private readonly NVEntities dbPropio;

        public ServicioUbicacionesAlmacen()
        {
            dbPropio = new NVEntities();
            repositorio = new RepositorioUbicacionesAlmacen(dbPropio);
        }

        internal ServicioUbicacionesAlmacen(IRepositorioUbicacionesAlmacen repositorio)
        {
            this.repositorio = repositorio;
        }

        public async Task<PendienteDeUbicarDTO> LeerPendienteDeUbicar(string empresa, string almacen)
        {
            List<FilaPendienteDeUbicar> pendiente = await repositorio.LeerPendienteDeUbicar(empresa, almacen).ConfigureAwait(false);
            List<FilaUbicacionProducto> huecos = pendiente.Any()
                ? await repositorio.LeerHuecosDeLoPendiente(empresa, almacen).ConfigureAwait(false)
                : new List<FilaUbicacionProducto>();

            return new PendienteDeUbicarDTO
            {
                Empresa = empresa,
                Almacen = almacen,
                Productos = MontarPendiente(pendiente, huecos)
            };
        }

        /// <summary>
        /// Un producto por fila, con sus orígenes y los huecos donde ya hay de ese producto. Se ordena
        /// para colocar andando lo menos posible: por el primer hueco de cada producto (pasillo,
        /// columna, fila); lo que no tiene hueco todavía va al final.
        /// </summary>
        internal static List<ProductoPendienteDeUbicarDTO> MontarPendiente(
            IEnumerable<FilaPendienteDeUbicar> pendiente, IEnumerable<FilaUbicacionProducto> huecos)
        {
            ILookup<string, FilaUbicacionProducto> huecosPorProducto = (huecos ?? Enumerable.Empty<FilaUbicacionProducto>())
                .Where(h => h.Cantidad > 0)
                .ToLookup(h => h.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            List<ProductoPendienteDeUbicarDTO> productos = (pendiente ?? Enumerable.Empty<FilaPendienteDeUbicar>())
                .GroupBy(f => f.Producto?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    string codigo = g.Select(f => f.CodigoBarras).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
                    return new ProductoPendienteDeUbicarDTO
                    {
                        Producto = g.Key,
                        Descripcion = g.First().Descripcion?.Trim(),
                        CodigoBarras = codigo,
                        SinCodigo = codigo == null,
                        Cantidad = g.Sum(f => f.Cantidad),
                        DesdeCuando = g.Min(f => f.DesdeCuando),
                        Origenes = g.Select(f => new OrigenPendienteDeUbicarDTO
                        {
                            PedidoCompra = f.PedidoCompra,
                            AlbaranCompra = f.AlbaranCompra,
                            TraspasoReposicion = f.TraspasoReposicion,
                            Cantidad = f.Cantidad
                        }).ToList(),
                        UbicacionesActuales = OrdenarHuecos(huecosPorProducto[g.Key])
                    };
                })
                .Where(p => p.Cantidad > 0)
                .ToList();

            productos = productos
                .OrderBy(p => !p.UbicacionesActuales.Any())
                .ThenBy(p => p.UbicacionesActuales.FirstOrDefault()?.Pasillo, StringComparer.Ordinal)
                .ThenBy(p => p.UbicacionesActuales.FirstOrDefault()?.Columna, StringComparer.Ordinal)
                .ThenBy(p => p.UbicacionesActuales.FirstOrDefault()?.Fila, StringComparer.Ordinal)
                .ThenBy(p => p.Producto, StringComparer.Ordinal)
                .ToList();

            int orden = 0;
            foreach (ProductoPendienteDeUbicarDTO producto in productos)
            {
                producto.Orden = ++orden;
            }
            return productos;
        }

        private static List<UbicacionAlmacenDTO> OrdenarHuecos(IEnumerable<FilaUbicacionProducto> huecos)
        {
            return huecos
                .OrderBy(h => h.Pasillo?.Trim(), StringComparer.Ordinal)
                .ThenBy(h => h.Columna?.Trim(), StringComparer.Ordinal)
                .ThenBy(h => h.Fila?.Trim(), StringComparer.Ordinal)
                .Select(h => new UbicacionAlmacenDTO
                {
                    Pasillo = h.Pasillo?.Trim(),
                    Fila = h.Fila?.Trim(),
                    Columna = h.Columna?.Trim(),
                    Ubicacion = CasadorEscaneos.TextoUbicacion(h.Pasillo, h.Fila, h.Columna),
                    Cantidad = h.Cantidad
                })
                .ToList();
        }

        public async Task<List<ProductoAlmacenDTO>> BuscarProducto(string empresa, string almacen, string codigo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > LONGITUD_MAXIMA_CODIGO)
            {
                return new List<ProductoAlmacenDTO>();
            }

            var resultado = new List<ProductoAlmacenDTO>();
            foreach (FilaProductoAlmacen producto in await repositorio.BuscarProductos(empresa, limpio).ConfigureAwait(false))
            {
                List<FilaUbicacionProducto> filas = await repositorio
                    .LeerUbicacionesDelProducto(empresa, almacen, producto.Producto).ConfigureAwait(false);
                resultado.Add(new ProductoAlmacenDTO
                {
                    Producto = producto.Producto?.Trim(),
                    Descripcion = producto.Descripcion?.Trim(),
                    CodigoBarras = string.IsNullOrWhiteSpace(producto.CodigoBarras) ? null : producto.CodigoBarras.Trim(),
                    Almacen = almacen,
                    Ubicaciones = OrdenarHuecos(filas.Where(f => f.Estado == ESTADO_COLOCADO && f.Cantidad > 0)),
                    PendienteDeUbicar = filas.Where(f => f.Estado == ESTADO_PENDIENTE_DE_UBICAR).Sum(f => f.Cantidad)
                });
            }
            return resultado;
        }

        /// <summary>
        /// Por qué no se puede colocar, o null si la petición está bien. Normaliza de paso el almacén,
        /// el producto y el hueco (tres cifras cada parte, como están todos en la tabla: 001/002/004).
        /// </summary>
        internal static string PrepararUbicacion(UbicarProductoDTO ubicar, out int tipoFiltro, out int? filtro)
        {
            tipoFiltro = 0;
            filtro = null;
            if (ubicar == null)
            {
                return "No ha llegado nada que colocar.";
            }
            ubicar.Producto = ubicar.Producto?.Trim();
            if (string.IsNullOrEmpty(ubicar.Producto) || ubicar.Producto.Length > LONGITUD_MAXIMA_CODIGO)
            {
                return "Falta el producto.";
            }
            ubicar.Almacen = string.IsNullOrWhiteSpace(ubicar.Almacen) ? Constantes.Almacenes.ALGETE : ubicar.Almacen.Trim().ToUpperInvariant();
            if (ubicar.Almacen.Length > 3)
            {
                return "El almacén no es válido.";
            }
            if (ubicar.Cantidad <= 0)
            {
                return "La cantidad a colocar tiene que ser mayor que cero.";
            }

            string pasillo = ParteDelHueco(ubicar.Pasillo);
            string fila = ParteDelHueco(ubicar.Fila);
            string columna = ParteDelHueco(ubicar.Columna);
            if (pasillo == null || fila == null || columna == null)
            {
                return "El hueco no es válido: pasillo, fila y columna son números de hasta tres cifras (001/002/004).";
            }
            ubicar.Pasillo = pasillo;
            ubicar.Fila = fila;
            ubicar.Columna = columna;

            var filtros = new[]
            {
                new { Tipo = 1, Valor = ubicar.PedidoCompra },
                new { Tipo = 2, Valor = ubicar.AlbaranCompra },
                new { Tipo = 3, Valor = ubicar.TraspasoReposicion },
                new { Tipo = 4, Valor = ubicar.PedidoVenta }
            }.Where(f => f.Valor.HasValue).ToList();
            if (filtros.Count > 1)
            {
                return "Solo se puede indicar un origen: pedido de compra, albarán de compra, reposición o pedido de venta.";
            }
            if (filtros.Count == 1)
            {
                if (filtros[0].Valor <= 0)
                {
                    return "El número del origen no es válido.";
                }
                tipoFiltro = filtros[0].Tipo;
                filtro = filtros[0].Valor;
            }
            return null;
        }

        private static string ParteDelHueco(string parte)
        {
            string limpio = parte?.Trim();
            return !string.IsNullOrEmpty(limpio) && limpio.Length <= 3 && limpio.All(char.IsDigit)
                ? limpio.PadLeft(3, '0')
                : null;
        }

        public async Task<ProductoAlmacenDTO> Ubicar(string empresa, UbicarProductoDTO ubicar, string usuario)
        {
            string motivo = PrepararUbicacion(ubicar, out int tipoFiltro, out int? filtro);
            if (motivo != null)
            {
                throw new NestoBusinessException(motivo);
            }

            // Lo comprueba también el procedimiento, pero así el aviso dice cuánto hay
            List<FilaUbicacionProducto> antes = await repositorio
                .LeerUbicacionesDelProducto(empresa, ubicar.Almacen, ubicar.Producto).ConfigureAwait(false);
            int pendiente = antes.Where(f => f.Estado == ESTADO_PENDIENTE_DE_UBICAR).Sum(f => f.Cantidad);
            if (pendiente < ubicar.Cantidad)
            {
                throw new NestoBusinessException(pendiente <= 0
                    ? $"El producto {ubicar.Producto} no tiene nada pendiente de colocar en {ubicar.Almacen}."
                    : $"Del producto {ubicar.Producto} solo hay {pendiente} unidades pendientes de colocar en {ubicar.Almacen}: no se pueden colocar {ubicar.Cantidad}.");
            }

            try
            {
                await repositorio.Ubicar(empresa, ubicar, tipoFiltro, filtro, UsuarioAuditoriaHelper.ParaAuditoria(usuario)).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Class >= 11 && ex.Class <= 16)
            {
                // Los avisos del propio procedimiento («No puede ubicar más cantidad de la pendiente»…)
                throw new NestoBusinessException(ex.Message, ex);
            }

            List<FilaUbicacionProducto> despues = await repositorio
                .LeerUbicacionesDelProducto(empresa, ubicar.Almacen, ubicar.Producto).ConfigureAwait(false);
            return new ProductoAlmacenDTO
            {
                Producto = ubicar.Producto,
                Almacen = ubicar.Almacen,
                Ubicaciones = OrdenarHuecos(despues.Where(f => f.Estado == ESTADO_COLOCADO && f.Cantidad > 0)),
                PendienteDeUbicar = despues.Where(f => f.Estado == ESTADO_PENDIENTE_DE_UBICAR).Sum(f => f.Cantidad)
            };
        }

        public void Dispose()
        {
            dbPropio?.Dispose();
        }
    }
}
