using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Productos
{
    /// <summary>NestoAPI#605: un código de barras de un producto, tal como lo devuelve la API.</summary>
    public class CodigoBarrasProductoDTO
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        /// <summary>Unidades que vale una lectura: 1 = unidad; 100 = caja de 100.</summary>
        public int Cantidad { get; set; }
        public string Proveedor { get; set; }
        public bool Principal { get; set; }
        /// <summary>Ficha | Almacen | Proveedor.</summary>
        public string Origen { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public bool Activo { get; set; }
    }

    /// <summary>NestoAPI#605: lo que se manda para añadir un código a un producto.</summary>
    public class NuevoCodigoBarrasDTO
    {
        public string Empresa { get; set; }
        public string Codigo { get; set; }
        /// <summary>Sin él (o 0), 1.</summary>
        public int? Cantidad { get; set; }
        public string Proveedor { get; set; }
        public bool Principal { get; set; }
        /// <summary>Ficha | Almacen | Proveedor. Ariadna manda Almacen. Sin él, Ficha.</summary>
        public string Origen { get; set; }
        /// <summary>True para añadirlo aunque ya esté activo en otro producto (caso guantes).</summary>
        public bool PermitirCompartido { get; set; }
    }

    /// <summary>NestoAPI#605: un producto activo que tiene un código (respuesta de api/Productos/PorCodigoBarras).</summary>
    public class ProductoPorCodigoBarrasDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        public int Cantidad { get; set; }
        public bool Principal { get; set; }
    }

    /// <summary>NestoAPI#605: el producto que ya tiene el código (en el 409).</summary>
    public class ProductoConCodigoCompartidoDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
    }

    /// <summary>NestoAPI#605: cuerpo del 409 cuando el código ya está activo en otro producto.</summary>
    public class CodigoBarrasCompartidoDTO
    {
        public string Message { get; set; }
        public List<ProductoConCodigoCompartidoDTO> Productos { get; set; } = new List<ProductoConCodigoCompartidoDTO>();
    }

    public enum EstadoOperacionCodigoBarras
    {
        Ok,
        Creado,
        NoValido,
        NoEncontrado,
        Compartido
    }

    public class ResultadoCodigoBarras
    {
        public EstadoOperacionCodigoBarras Estado { get; set; }
        public string Mensaje { get; set; }
        public CodigoBarrasProductoDTO Codigo { get; set; }
        public CodigoBarrasCompartidoDTO Compartido { get; set; }
    }

    public interface IRepositorioCodigosBarras
    {
        /// <summary>Todas las filas del producto, también las de baja (con seguimiento: se modifican y se guardan).</summary>
        Task<List<ProductoCodigoBarras>> LeerDelProducto(string empresa, string producto);
        /// <summary>El producto (con seguimiento, para poner CodBarras); null si no existe.</summary>
        Task<Producto> LeerProducto(string empresa, string producto);
        /// <summary>Los productos activos (Estado &gt;= 0) con ese código activo, en la tabla o en la ficha.</summary>
        Task<List<ProductoPorCodigoBarrasDTO>> ProductosConCodigo(string empresa, string codigo);
        /// <summary>Los códigos activos de cada producto (el principal, el primero; también el de la ficha).</summary>
        Task<Dictionary<string, List<string>>> CodigosActivos(string empresa, IReadOnlyCollection<string> productos);
        void Anadir(ProductoCodigoBarras fila);
        Task GuardarCambios();
    }

    public interface IServicioCodigosBarras
    {
        Task<List<CodigoBarrasProductoDTO>> Listar(string empresa, string producto);
        Task<ResultadoCodigoBarras> Anadir(string producto, NuevoCodigoBarrasDTO nuevo, string usuario);
        Task<ResultadoCodigoBarras> MarcarPrincipal(string empresa, string producto, int id, string usuario);
        Task<ResultadoCodigoBarras> DarDeBaja(string empresa, string producto, int id, string usuario);
        Task<List<ProductoPorCodigoBarrasDTO>> BuscarPorCodigo(string empresa, string codigo);
    }

    /// <summary>
    /// NestoAPI#605 (Carlos, 07/10/26): varios códigos de barras por producto, como SAP (EAN adicionales), Dynamics (Item
    /// References) y Odoo (product_multi_barcode). Cada código lleva cuántas unidades vale una lectura, su proveedor si es suyo,
    /// de dónde viene y si es el principal. <c>Productos.CodBarras</c> sigue siendo el principal (Nesto viejo, PrestaShop, TNV):
    /// al marcar otro, se actualiza la ficha. Un código solo puede estar en dos productos si se pide expresamente (los guantes:
    /// el proveedor cambia el código por lote y el de la caja de hoy es el de otra talla).
    /// </summary>
    public class ServicioCodigosBarras : IServicioCodigosBarras
    {
        public const string ORIGEN_FICHA = "Ficha";
        public const string ORIGEN_ALMACEN = "Almacen";
        public const string ORIGEN_PROVEEDOR = "Proveedor";
        public const int LONGITUD_MAXIMA = 20;
        /// <summary>Productos.CodBarras es char(13): un principal más largo no cabe en la ficha.</summary>
        public const int LONGITUD_MAXIMA_FICHA = 13;

        public const string MENSAJE_SIN_PERMISO =
            "Para cambiar los códigos de barras hay que ser de Compras, Almacén, Tiendas, Dirección o Informática.";

        private static readonly string[] gruposQuePuedenEscribir =
        {
            Constantes.GruposSeguridad.COMPRAS,
            Constantes.GruposSeguridad.ALMACEN,
            Constantes.GruposSeguridad.TIENDAS,
            Constantes.GruposSeguridad.DIRECCION,
            Constantes.GruposSeguridad.INFORMATICA
        };

        private readonly IRepositorioCodigosBarras repositorio;

        public ServicioCodigosBarras(IRepositorioCodigosBarras repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        public static bool PuedeEscribir(IPrincipal usuario)
        {
            return usuario != null && gruposQuePuedenEscribir.Any(g => usuario.IsInRoleSinDominio(g));
        }

        /// <summary>Sin espacios (ni dentro: el lector a veces mete alguno). Null si no queda nada.</summary>
        public static string Limpiar(string codigo)
        {
            if (codigo == null)
            {
                return null;
            }
            string limpio = Regex.Replace(codigo, @"\s+", string.Empty);
            return limpio.Length == 0 ? null : limpio;
        }

        /// <summary>Ficha, Almacen o Proveedor (admite tildes y mayúsculas). Sin origen, Ficha. Null si no es ninguno.</summary>
        public static string NormalizarOrigen(string origen)
        {
            string limpio = origen?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return ORIGEN_FICHA;
            }
            if (string.Equals(limpio, ORIGEN_FICHA, StringComparison.OrdinalIgnoreCase))
            {
                return ORIGEN_FICHA;
            }
            if (string.Equals(limpio, ORIGEN_ALMACEN, StringComparison.OrdinalIgnoreCase) || string.Equals(limpio, "Almacén", StringComparison.OrdinalIgnoreCase))
            {
                return ORIGEN_ALMACEN;
            }
            return string.Equals(limpio, ORIGEN_PROVEEDOR, StringComparison.OrdinalIgnoreCase) ? ORIGEN_PROVEEDOR : null;
        }

        public async Task<List<CodigoBarrasProductoDTO>> Listar(string empresa, string producto)
        {
            List<ProductoCodigoBarras> filas = await repositorio.LeerDelProducto(empresa, producto?.Trim()).ConfigureAwait(false)
                ?? new List<ProductoCodigoBarras>();
            return Ordenar(filas).Select(ADto).ToList();
        }

        public async Task<ResultadoCodigoBarras> Anadir(string producto, NuevoCodigoBarrasDTO nuevo, string usuario)
        {
            string numero = producto?.Trim();
            string empresa = string.IsNullOrWhiteSpace(nuevo?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : nuevo.Empresa.Trim();
            string codigo = Limpiar(nuevo?.Codigo);
            if (string.IsNullOrEmpty(numero))
            {
                return NoValido("Falta el producto.");
            }
            if (codigo == null)
            {
                return NoValido("Falta el código de barras.");
            }
            if (codigo.Length > LONGITUD_MAXIMA)
            {
                return NoValido($"El código de barras no puede tener más de {LONGITUD_MAXIMA} caracteres.");
            }
            int cantidad = nuevo.Cantidad.GetValueOrDefault() == 0 ? 1 : nuevo.Cantidad.Value;
            if (cantidad < 0)
            {
                return NoValido("La cantidad tiene que ser 1 o más (1 = unidad; 100 = caja de 100).");
            }
            string origen = NormalizarOrigen(nuevo.Origen);
            if (origen == null)
            {
                return NoValido("El origen tiene que ser Ficha, Almacen o Proveedor.");
            }
            string proveedor = string.IsNullOrWhiteSpace(nuevo.Proveedor) ? null : nuevo.Proveedor.Trim();
            if (proveedor != null && proveedor.Length > 10)
            {
                return NoValido("El proveedor no puede tener más de 10 caracteres.");
            }
            if (nuevo.Principal && codigo.Length > LONGITUD_MAXIMA_FICHA)
            {
                return NoValido($"Un código de más de {LONGITUD_MAXIMA_FICHA} caracteres no cabe en la ficha: no puede ser el principal.");
            }

            Producto ficha = await repositorio.LeerProducto(empresa, numero).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.NoEncontrado, Mensaje = $"No existe el producto {numero}." };
            }

            List<ProductoCodigoBarras> filas = await repositorio.LeerDelProducto(empresa, numero).ConfigureAwait(false) ?? new List<ProductoCodigoBarras>();
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            ProductoCodigoBarras existente = filas.FirstOrDefault(f => string.Equals(f.Codigo?.Trim(), codigo, StringComparison.OrdinalIgnoreCase));
            if (existente != null)
            {
                bool cambia = false;
                if (!existente.Activo)
                {
                    existente.Activo = true;
                    existente.Usuario = quien;
                    existente.Fecha = DateTime.Now;
                    cambia = true;
                }
                if (nuevo.Principal && !existente.Principal)
                {
                    await HacerPrincipal(filas, existente, ficha, quien).ConfigureAwait(false);
                }
                else if (cambia)
                {
                    await repositorio.GuardarCambios().ConfigureAwait(false);
                }
                return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.Ok, Codigo = ADto(existente) };
            }

            List<ProductoPorCodigoBarrasDTO> otros = ((await repositorio.ProductosConCodigo(empresa, codigo).ConfigureAwait(false)) ?? new List<ProductoPorCodigoBarrasDTO>())
                .Where(p => !string.Equals(p.Producto?.Trim(), numero, StringComparison.OrdinalIgnoreCase))
                .GroupBy(p => p.Producto?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (otros.Any() && !nuevo.PermitirCompartido)
            {
                string lista = string.Join(", ", otros.Select(o => $"{o.Producto?.Trim()} {o.Nombre?.Trim()}".Trim()));
                string mensaje = otros.Count == 1
                    ? $"El código {codigo} ya es del producto {lista}. Si este envase también es del {numero}, confírmalo para que lo compartan."
                    : $"El código {codigo} ya está en los productos {lista}. Si este envase también es del {numero}, confírmalo para que lo compartan.";
                return new ResultadoCodigoBarras
                {
                    Estado = EstadoOperacionCodigoBarras.Compartido,
                    Mensaje = mensaje,
                    Compartido = new CodigoBarrasCompartidoDTO
                    {
                        Message = mensaje,
                        Productos = otros.Select(o => new ProductoConCodigoCompartidoDTO { Producto = o.Producto?.Trim(), Nombre = o.Nombre?.Trim() }).ToList()
                    }
                };
            }

            bool hayPrincipal = filas.Any(f => f.Activo && f.Principal);
            bool principal = nuevo.Principal || (!hayPrincipal && codigo.Length <= LONGITUD_MAXIMA_FICHA && string.IsNullOrWhiteSpace(ficha.CodBarras));
            var fila = new ProductoCodigoBarras
            {
                Empresa = empresa,
                Producto = numero,
                Codigo = codigo,
                Cantidad = cantidad,
                Proveedor = proveedor,
                Principal = false,
                Origen = origen,
                Usuario = quien,
                Fecha = DateTime.Now,
                Activo = true
            };
            repositorio.Anadir(fila);
            filas.Add(fila);
            if (principal)
            {
                await HacerPrincipal(filas, fila, ficha, quien).ConfigureAwait(false);
            }
            else
            {
                await repositorio.GuardarCambios().ConfigureAwait(false);
            }
            return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.Creado, Codigo = ADto(fila) };
        }

        public async Task<ResultadoCodigoBarras> MarcarPrincipal(string empresa, string producto, int id, string usuario)
        {
            string numero = producto?.Trim();
            List<ProductoCodigoBarras> filas = await repositorio.LeerDelProducto(empresa, numero).ConfigureAwait(false) ?? new List<ProductoCodigoBarras>();
            ProductoCodigoBarras fila = filas.FirstOrDefault(f => f.Id == id);
            if (fila == null)
            {
                return NoEncontrado(numero, id);
            }
            if (fila.Codigo?.Trim().Length > LONGITUD_MAXIMA_FICHA)
            {
                return NoValido($"Un código de más de {LONGITUD_MAXIMA_FICHA} caracteres no cabe en la ficha: no puede ser el principal.");
            }
            Producto ficha = await repositorio.LeerProducto(empresa, numero).ConfigureAwait(false);
            if (ficha == null)
            {
                return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.NoEncontrado, Mensaje = $"No existe el producto {numero}." };
            }
            string quien = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
            if (!fila.Activo)
            {
                fila.Activo = true;
            }
            await HacerPrincipal(filas, fila, ficha, quien).ConfigureAwait(false);
            return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.Ok, Codigo = ADto(fila) };
        }

        public async Task<ResultadoCodigoBarras> DarDeBaja(string empresa, string producto, int id, string usuario)
        {
            string numero = producto?.Trim();
            List<ProductoCodigoBarras> filas = await repositorio.LeerDelProducto(empresa, numero).ConfigureAwait(false) ?? new List<ProductoCodigoBarras>();
            ProductoCodigoBarras fila = filas.FirstOrDefault(f => f.Id == id);
            if (fila == null)
            {
                return NoEncontrado(numero, id);
            }
            if (fila.Principal && fila.Activo)
            {
                return NoValido("El código principal no se puede dar de baja: marca antes otro como principal.");
            }
            if (fila.Activo)
            {
                fila.Activo = false;
                fila.Usuario = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
                fila.Fecha = DateTime.Now;
                await repositorio.GuardarCambios().ConfigureAwait(false);
            }
            return new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.Ok, Codigo = ADto(fila) };
        }

        public async Task<List<ProductoPorCodigoBarrasDTO>> BuscarPorCodigo(string empresa, string codigo)
        {
            string limpio = Limpiar(codigo);
            if (limpio == null)
            {
                return new List<ProductoPorCodigoBarrasDTO>();
            }
            return ((await repositorio.ProductosConCodigo(empresa, limpio).ConfigureAwait(false)) ?? new List<ProductoPorCodigoBarrasDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new ProductoPorCodigoBarrasDTO
                {
                    Producto = g.Key,
                    Nombre = g.Select(p => p.Nombre?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
                    Cantidad = g.OrderByDescending(p => p.Principal).First().Cantidad,
                    Principal = g.Any(p => p.Principal)
                })
                .OrderByDescending(p => p.Principal)
                .ThenBy(p => p.Producto, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Primero la tabla (este principal, los demás no) y LUEGO la ficha, en dos guardados: así el trigger de Productos
        /// ya no encuentra el principal anterior marcado y no lo da de baja (se queda como alternativo).
        /// </summary>
        private async Task HacerPrincipal(List<ProductoCodigoBarras> filas, ProductoCodigoBarras nueva, Producto ficha, string quien)
        {
            foreach (ProductoCodigoBarras f in filas.Where(f => f != nueva && f.Principal))
            {
                f.Principal = false;
            }
            nueva.Principal = true;
            nueva.Activo = true;
            await repositorio.GuardarCambios().ConfigureAwait(false);

            string codigo = nueva.Codigo.Trim();
            if (!string.Equals(ficha.CodBarras?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
            {
                ficha.CodBarras = codigo;
                ficha.Usuario = quien;
                ficha.Fecha_Modificación = DateTime.Now;
                await repositorio.GuardarCambios().ConfigureAwait(false);
            }
        }

        internal static IEnumerable<ProductoCodigoBarras> Ordenar(IEnumerable<ProductoCodigoBarras> filas)
        {
            return filas
                .OrderByDescending(f => f.Activo)
                .ThenByDescending(f => f.Principal)
                .ThenBy(f => f.Fecha)
                .ThenBy(f => f.Id);
        }

        internal static CodigoBarrasProductoDTO ADto(ProductoCodigoBarras f)
        {
            return new CodigoBarrasProductoDTO
            {
                Id = f.Id,
                Codigo = f.Codigo?.Trim(),
                Cantidad = f.Cantidad,
                Proveedor = string.IsNullOrWhiteSpace(f.Proveedor) ? null : f.Proveedor.Trim(),
                Principal = f.Principal,
                Origen = f.Origen?.Trim(),
                Usuario = f.Usuario?.Trim(),
                Fecha = f.Fecha,
                Activo = f.Activo
            };
        }

        private static ResultadoCodigoBarras NoValido(string mensaje)
            => new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.NoValido, Mensaje = mensaje };

        private static ResultadoCodigoBarras NoEncontrado(string producto, int id)
            => new ResultadoCodigoBarras { Estado = EstadoOperacionCodigoBarras.NoEncontrado, Mensaje = $"El producto {producto} no tiene el código {id}." };
    }

    /// <summary>NestoAPI#605: la tabla ProductosCodigosBarras (EF para escribir; SQL para buscar, sin el relleno de los char).</summary>
    public class RepositorioCodigosBarras : IRepositorioCodigosBarras, IDisposable
    {
        /// <summary>Muy por debajo del límite de 2.100 parámetros de SQL Server.</summary>
        internal const int PRODUCTOS_POR_CONSULTA = 500;

        // Los productos activos con ese código activo en la tabla, y los que lo llevan en la ficha (por si aún no está en ella)
        internal const string SQL_PRODUCTOS_CON_CODIGO = @"
SELECT RTRIM(p.[Número]) AS Producto, RTRIM(p.Nombre) AS Nombre, c.Cantidad AS Cantidad, c.Principal AS Principal
FROM ProductosCodigosBarras c
     JOIN Productos p ON p.Empresa = c.Empresa AND p.[Número] = c.Producto
WHERE c.Empresa = @empresa AND c.Codigo = @codigo AND c.Activo = 1 AND p.Estado >= 0
UNION
SELECT RTRIM(p.[Número]), RTRIM(p.Nombre), 1, CAST(1 AS bit)
FROM Productos p
WHERE p.Empresa = @empresa AND p.CodBarras = @codigo AND p.Estado >= 0
  AND NOT EXISTS (SELECT 1 FROM ProductosCodigosBarras c WHERE c.Empresa = p.Empresa AND c.Producto = p.[Número] AND c.Codigo = @codigo)";

        internal const string SQL_CODIGOS_ACTIVOS = @"
SELECT RTRIM(c.Producto) AS Producto, RTRIM(c.Codigo) AS Codigo, c.Principal AS Principal
FROM ProductosCodigosBarras c
WHERE c.Empresa = @p0 AND c.Activo = 1 AND c.Producto IN ({0})
UNION
SELECT RTRIM(p.[Número]), RTRIM(p.CodBarras), CAST(1 AS bit)
FROM Productos p
WHERE p.Empresa = @p0 AND p.[Número] IN ({0}) AND RTRIM(ISNULL(p.CodBarras, '')) <> ''";

        private class FilaCodigo
        {
            public string Producto { get; set; }
            public string Codigo { get; set; }
            public bool Principal { get; set; }
        }

        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioCodigosBarras(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public static RepositorioCodigosBarras ConContextoPropio() => new RepositorioCodigosBarras(new NVEntities()) { contextoPropio = true };

        public Task<List<ProductoCodigoBarras>> LeerDelProducto(string empresa, string producto)
            => db.ProductosCodigosBarras.Where(c => c.Empresa == empresa && c.Producto == producto).ToListAsync();

        public Task<Producto> LeerProducto(string empresa, string producto)
            => db.Productos.SingleOrDefaultAsync(p => p.Empresa == empresa && p.Número == producto);

        public async Task<List<ProductoPorCodigoBarrasDTO>> ProductosConCodigo(string empresa, string codigo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio) || limpio.Length > ServicioCodigosBarras.LONGITUD_MAXIMA)
            {
                return new List<ProductoPorCodigoBarrasDTO>();
            }
            // varchar (no nvarchar): casa con el char(13) de la ficha y el varchar(20) de la tabla sin convertir cada fila
            return await db.Database.SqlQuery<ProductoPorCodigoBarrasDTO>(SQL_PRODUCTOS_CON_CODIGO,
                    new SqlParameter("@empresa", System.Data.SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@codigo", System.Data.SqlDbType.VarChar, 20) { Value = limpio })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<Dictionary<string, List<string>>> CodigosActivos(string empresa, IReadOnlyCollection<string> productos)
        {
            var resultado = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            List<string> todos = (productos ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var filas = new List<FilaCodigo>();
            for (int desde = 0; desde < todos.Count; desde += PRODUCTOS_POR_CONSULTA)
            {
                List<string> tanda = todos.Skip(desde).Take(PRODUCTOS_POR_CONSULTA).ToList();
                var parametros = new List<object> { new SqlParameter("@p0", System.Data.SqlDbType.Char, 3) { Value = empresa } };
                for (int i = 0; i < tanda.Count; i++)
                {
                    parametros.Add(new SqlParameter("@p" + (i + 1), System.Data.SqlDbType.Char, 15) { Value = tanda[i] });
                }
                string lista = string.Join(", ", Enumerable.Range(1, tanda.Count).Select(i => "@p" + i));
                filas.AddRange(await db.Database.SqlQuery<FilaCodigo>(string.Format(SQL_CODIGOS_ACTIVOS, lista), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false));
            }
            foreach (IGrouping<string, FilaCodigo> grupo in filas.Where(f => !string.IsNullOrWhiteSpace(f.Producto) && !string.IsNullOrWhiteSpace(f.Codigo))
                .GroupBy(f => f.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                resultado[grupo.Key] = grupo
                    .OrderByDescending(f => f.Principal)
                    .Select(f => f.Codigo.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            return resultado;
        }

        public void Anadir(ProductoCodigoBarras fila) => db.ProductosCodigosBarras.Add(fila);

        public Task GuardarCambios() => db.SaveChangesAsync();

        public void Dispose()
        {
            if (contextoPropio)
            {
                db.Dispose();
            }
        }
    }

    /// <summary>
    /// NestoAPI#605: junta a cada línea (recorrido, packing, ubicar, recibir) todos los códigos activos de su producto, con el
    /// de <c>CodigoBarras</c> (el principal) el primero. Así Ariadna casa por cualquiera.
    /// </summary>
    public static class CompletadorCodigosBarras
    {
        public static async Task Completar(IRepositorioCodigosBarras repositorio, string empresa, IEnumerable<Models.PreparacionAlmacen.IConCodigosBarras> lineas)
        {
            List<Models.PreparacionAlmacen.IConCodigosBarras> lista = (lineas ?? Enumerable.Empty<Models.PreparacionAlmacen.IConCodigosBarras>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto))
                .ToList();
            if (lista.Count == 0)
            {
                return;
            }
            Dictionary<string, List<string>> codigos = repositorio == null
                ? new Dictionary<string, List<string>>()
                : await repositorio.CodigosActivos(empresa, lista.Select(l => l.Producto.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()).ConfigureAwait(false)
                    ?? new Dictionary<string, List<string>>();
            foreach (Models.PreparacionAlmacen.IConCodigosBarras linea in lista)
            {
                linea.CodigosBarras = Juntar(linea.CodigoBarras, codigos.TryGetValue(linea.Producto.Trim(), out List<string> suyos) ? suyos : null);
            }
        }

        /// <summary>El principal (si lo hay) el primero y luego los demás, sin repetir.</summary>
        public static List<string> Juntar(string principal, IEnumerable<string> otros)
        {
            var resultado = new List<string>();
            foreach (string codigo in new[] { principal }.Concat(otros ?? Enumerable.Empty<string>()))
            {
                string limpio = codigo?.Trim();
                if (!string.IsNullOrEmpty(limpio) && !resultado.Contains(limpio, StringComparer.OrdinalIgnoreCase))
                {
                    resultado.Add(limpio);
                }
            }
            return resultado;
        }
    }
}
