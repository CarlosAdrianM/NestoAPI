using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    public interface IRepositorioSugerenciasProductos
    {
        /// <summary>Si existen SugerenciasProductosSesiones y SugerenciasProductosMostradas (el script Issue670 lanzado).</summary>
        Task<bool> ExistenTablas();
        /// <summary>Null si no existe.</summary>
        Task<SesionSugerencias> LeerSesion(Guid id);
        Task CrearSesion(SesionSugerencias sesion);
        Task ActualizarSesion(Guid id, string contacto, string vendedor, string carrito);
        Task<List<SugerenciaMostrada>> LeerMostradas(Guid sesion);
        /// <summary>Null si no existe.</summary>
        Task<SugerenciaMostrada> LeerMostrada(int id);
        /// <summary>Devuelve el Id. Con las columnas del script Issue671a, también Fuentes, TamanoCarrito y Exploracion.</summary>
        Task<int> InsertarMostrada(SugerenciaMostrada mostrada);
        Task GuardarRespuesta(int id, string respuesta);
        /// <summary>Productos a los que este cliente ha dicho «No le interesa» desde <paramref name="desde"/>.</summary>
        Task<HashSet<string>> LeerRechazados(string empresa, string cliente, DateTime desde);
        Task<List<CompraDiaReposicion>> LeerComprasCliente(string empresa, string cliente, DateTime desde, DateTime hasta);
        /// <summary>Productos en pedidos pendientes o en curso del cliente.</summary>
        Task<HashSet<string>> LeerPendientesCliente(string empresa, string cliente);
        /// <summary>Pares con un antecedente del carrito. Vacía si el job aún no tiene tabla.</summary>
        Task<List<ParCompradoJunto>> LeerPares(IEnumerable<string> productosCarrito);
        /// <summary>Ofertas activas, «Potenciar hasta» vigente y novedades.</summary>
        Task<List<CandidatoPotenciar>> LeerPotenciar(string empresa, DateTime hoy);
        /// <summary>Producto → clientes del perfil que lo compran. Vacío si el job aún no tiene tabla.</summary>
        Task<Dictionary<string, int>> LeerClientesPerfil(string grupo, string subGrupo);
        /// <summary>NestoAPI#671: las afinidades item-kNN del cliente. Vacía si el job aún no tiene tabla (script Issue671a).</summary>
        Task<List<AfinidadCliente>> LeerAfinidad(string cliente);
        Task<Dictionary<string, FichaProductoSugerencia>> LeerFichas(string empresa, IEnumerable<string> productos);
        /// <summary>De los productos dados, los que tienen stock disponible o una llegada prevista de un pedido a proveedor enviado.</summary>
        Task<HashSet<string>> LeerVendibles(string empresa, IEnumerable<string> productos);
        Task<DatosGrupoVendedor> LeerDatosGrupoVendedor(string empresa, string cliente, string contacto, string vendedor);
        Task EnlazarPedido(Guid sesion, int pedido);
        Task MarcarEntradaEnPedido(int id, bool entro, int? cantidad);
    }

    /// <summary>
    /// NestoAPI#670: SQL a mano de las tablas de sugerencias (fuera del EDMX). Sin tablas, ExistenTablas da false y el resto
    /// no se llama; las del job (pares y perfiles) se miran cada vez y, si faltan, se leen vacías.
    /// </summary>
    public static class SugerenciasProductosSql
    {
        internal const string SQL_EXISTEN_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosSesiones', N'U') IS NULL
                   OR OBJECT_ID(N'dbo.SugerenciasProductosMostradas', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_EXISTE_TABLA = @"
SELECT CAST(CASE WHEN OBJECT_ID(@p0, N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_EXISTE_COLUMNA = @"
SELECT CAST(CASE WHEN COL_LENGTH(@p0, @p1) IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string TABLA_PARES = "dbo.SugerenciasProductosPares";
        internal const string TABLA_PERFILES = "dbo.SugerenciasProductosPerfilesProductos";
        internal const string TABLA_AFINIDAD = "dbo.SugerenciasProductosAfinidad";
        internal const string TABLA_MOSTRADAS = "dbo.SugerenciasProductosMostradas";

        internal const string SQL_LEER_SESION = @"
SELECT Id, RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, RTRIM(Vendedor) AS Vendedor,
       Usuario, Aplicacion, FechaInicio, Carrito, Pedido
FROM dbo.SugerenciasProductosSesiones WHERE Id = @p0";

        internal const string SQL_CREAR_SESION = @"
INSERT INTO dbo.SugerenciasProductosSesiones (Id, Empresa, Cliente, Contacto, Vendedor, Usuario, Aplicacion, FechaInicio)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, GETDATE())";

        internal const string SQL_ACTUALIZAR_SESION = @"
UPDATE dbo.SugerenciasProductosSesiones SET Contacto = @p1, Vendedor = @p2, Carrito = @p3, FechaUltimoCalculo = GETDATE() WHERE Id = @p0";

        internal const string SQL_COLUMNAS_MOSTRADA = @"Id, SesionId, RTRIM(Producto) AS Producto, Motivo, Puntuacion, Posicion, VersionMotor, Texto,
       FechaMostrada, Respuesta, FechaRespuesta, EntroEnPedido, CantidadEnPedido";

        internal const string SQL_LEER_MOSTRADAS = @"
SELECT " + SQL_COLUMNAS_MOSTRADA + @" FROM dbo.SugerenciasProductosMostradas WHERE SesionId = @p0 ORDER BY Id";

        internal const string SQL_LEER_MOSTRADA = @"
SELECT " + SQL_COLUMNAS_MOSTRADA + @" FROM dbo.SugerenciasProductosMostradas WHERE Id = @p0";

        internal const string SQL_INSERTAR_MOSTRADA = @"
INSERT INTO dbo.SugerenciasProductosMostradas (SesionId, Producto, Motivo, Puntuacion, Posicion, VersionMotor, Texto, FechaMostrada)
OUTPUT INSERTED.Id
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, GETDATE())";

        /// <summary>NestoAPI#671: con las columnas del script Issue671a.</summary>
        internal const string SQL_INSERTAR_MOSTRADA_MODELO = @"
INSERT INTO dbo.SugerenciasProductosMostradas (SesionId, Producto, Motivo, Puntuacion, Posicion, VersionMotor, Texto, FechaMostrada,
       Fuentes, TamanoCarrito, Exploracion)
OUTPUT INSERTED.Id
VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, GETDATE(), @p7, @p8, @p9)";

        internal const string SQL_AFINIDAD_CLIENTE = @"
SELECT RTRIM(Cliente) AS Cliente, RTRIM(Producto) AS Producto, Afinidad, Rango FROM dbo.SugerenciasProductosAfinidad WHERE Cliente = @p0";

        internal const string SQL_GUARDAR_RESPUESTA = @"
UPDATE dbo.SugerenciasProductosMostradas SET Respuesta = @p1, FechaRespuesta = GETDATE() WHERE Id = @p0";

        internal const string SQL_RECHAZADOS = @"
SELECT DISTINCT RTRIM(m.Producto)
FROM dbo.SugerenciasProductosMostradas m
JOIN dbo.SugerenciasProductosSesiones s ON s.Id = m.SesionId
WHERE s.Empresa = @p0 AND s.Cliente = @p1 AND m.Respuesta = 'NoLeInteresa' AND m.FechaRespuesta >= @p2";

        // Las líneas pendientes son pocos cientos y Estado es la clave del índice agrupado (como en #532).
        internal const string SQL_PENDIENTES_CLIENTE = @"
SELECT DISTINCT RTRIM(l.Producto)
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Estado BETWEEN -1 AND 1 AND l.Empresa = @empresa AND l.TipoLinea = 1 AND l.Producto IS NOT NULL
  AND l.[Nº Cliente] = @cliente";

        internal const string SQL_ENLAZAR_PEDIDO = @"
UPDATE dbo.SugerenciasProductosSesiones SET Pedido = @p1, FechaPedido = GETDATE() WHERE Id = @p0";

        internal const string SQL_MARCAR_ENTRADA = @"
UPDATE dbo.SugerenciasProductosMostradas SET EntroEnPedido = @p1, CantidadEnPedido = @p2 WHERE Id = @p0";

        internal const string SQL_CLIENTES_PERFIL = @"
SELECT RTRIM(Grupo) AS Grupo, RTRIM(SubGrupo) AS SubGrupo, RTRIM(Producto) AS Producto, Clientes FROM dbo.SugerenciasProductosPerfilesProductos WHERE Grupo = @p0 AND SubGrupo = @p1";

        // Novedades: fase A (nuevo) del último mes de las propuestas de stock, con menos de 3 meses desde la primera venta
        // (o sin venta todavía).
        internal const string SQL_NOVEDADES = @"
SELECT DISTINCT RTRIM(p.Producto)
FROM dbo.PropuestasStock p WITH (NOLOCK)
WHERE p.Mes = (SELECT MAX(Mes) FROM dbo.PropuestasStock WITH (NOLOCK)) AND p.Fase = 'A'
  AND (p.MesesDesdePrimeraVenta IS NULL OR p.MesesDesdePrimeraVenta < @p0)";

        internal const string SQL_POTENCIAR_HASTA = @"
SELECT DISTINCT RTRIM(p.Producto)
FROM dbo.PropuestasStock p WITH (NOLOCK)
WHERE p.Decision = 'Potenciar' AND p.PotenciarHasta >= @p0";

        internal const int TAMANO_LOTE = 200;

        internal static SqlParameter Char(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.Char, largo) { Value = (object)valor?.Trim() ?? DBNull.Value };

        internal static SqlParameter Varchar(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.VarChar, largo) { Value = (object)Recortar(valor?.Trim(), largo) ?? DBNull.Value };

        internal static SqlParameter Nvarchar(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.NVarChar, largo) { Value = (object)Recortar(valor, largo) ?? DBNull.Value };

        internal static SqlParameter Entero(string nombre, int valor) => new SqlParameter(nombre, SqlDbType.Int) { Value = valor };

        internal static SqlParameter Identificador(string nombre, Guid valor) => new SqlParameter(nombre, SqlDbType.UniqueIdentifier) { Value = valor };

        internal static SqlParameter Fecha(string nombre, DateTime valor) => new SqlParameter(nombre, SqlDbType.DateTime) { Value = valor };

        internal static string Recortar(string texto, int largo) => texto == null || texto.Length <= largo ? texto : texto.Substring(0, largo);

        /// <summary>El carrito como se guarda en la sesión: «producto:cantidad;...».</summary>
        public static string TextoCarrito(IDictionary<string, int> carrito)
            => carrito == null || carrito.Count == 0 ? string.Empty
                : string.Join(";", carrito.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Select(c => c.Key + ":" + c.Value));

        public static Dictionary<string, int> LeerCarrito(string texto)
        {
            var carrito = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string parte in (texto ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int dosPuntos = parte.LastIndexOf(':');
                string producto = (dosPuntos < 0 ? parte : parte.Substring(0, dosPuntos)).Trim();
                if (producto.Length == 0)
                {
                    continue;
                }
                int cantidad = dosPuntos >= 0 && int.TryParse(parte.Substring(dosPuntos + 1), out int c) ? c : 1;
                carrito[producto] = (carrito.TryGetValue(producto, out int antes) ? antes : 0) + cantidad;
            }
            return carrito;
        }
    }

    public class RepositorioSugerenciasProductosSql : IRepositorioSugerenciasProductos
    {
        private readonly NVEntities db;

        public RepositorioSugerenciasProductosSql(NVEntities db)
        {
            this.db = db;
        }

        public Task<bool> ExistenTablas() => db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_EXISTEN_TABLAS).FirstAsync();

        private Task<bool> ExisteTabla(string tabla)
            => db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_EXISTE_TABLA, SugerenciasProductosSql.Nvarchar("@p0", tabla, 128)).FirstAsync();

        private Task<bool> ExisteColumna(string tabla, string columna)
            => db.Database.SqlQuery<bool>(SugerenciasProductosSql.SQL_EXISTE_COLUMNA, SugerenciasProductosSql.Nvarchar("@p0", tabla, 128),
                SugerenciasProductosSql.Nvarchar("@p1", columna, 128)).FirstAsync();

        public Task<SesionSugerencias> LeerSesion(Guid id)
            => db.Database.SqlQuery<SesionSugerencias>(SugerenciasProductosSql.SQL_LEER_SESION, SugerenciasProductosSql.Identificador("@p0", id)).FirstOrDefaultAsync();

        public Task CrearSesion(SesionSugerencias s)
            => db.Database.ExecuteSqlCommandAsync(SugerenciasProductosSql.SQL_CREAR_SESION,
                SugerenciasProductosSql.Identificador("@p0", s.Id), SugerenciasProductosSql.Char("@p1", s.Empresa, 3),
                SugerenciasProductosSql.Varchar("@p2", s.Cliente, 10), SugerenciasProductosSql.Varchar("@p3", s.Contacto, 3),
                SugerenciasProductosSql.Varchar("@p4", s.Vendedor, 10), SugerenciasProductosSql.Varchar("@p5", s.Usuario, 50),
                SugerenciasProductosSql.Varchar("@p6", s.Aplicacion, 10));

        public Task ActualizarSesion(Guid id, string contacto, string vendedor, string carrito)
            => db.Database.ExecuteSqlCommandAsync(SugerenciasProductosSql.SQL_ACTUALIZAR_SESION, SugerenciasProductosSql.Identificador("@p0", id),
                SugerenciasProductosSql.Varchar("@p1", contacto, 3), SugerenciasProductosSql.Varchar("@p2", vendedor, 10),
                new SqlParameter("@p3", SqlDbType.VarChar, -1) { Value = (object)carrito ?? DBNull.Value });

        public Task<List<SugerenciaMostrada>> LeerMostradas(Guid sesion)
            => db.Database.SqlQuery<SugerenciaMostrada>(SugerenciasProductosSql.SQL_LEER_MOSTRADAS, SugerenciasProductosSql.Identificador("@p0", sesion)).ToListAsync();

        public Task<SugerenciaMostrada> LeerMostrada(int id)
            => db.Database.SqlQuery<SugerenciaMostrada>(SugerenciasProductosSql.SQL_LEER_MOSTRADA, SugerenciasProductosSql.Entero("@p0", id)).FirstOrDefaultAsync();

        private bool? hayColumnasModelo;

        public async Task<int> InsertarMostrada(SugerenciaMostrada m)
        {
            // NestoAPI#671: sin el script Issue671a, como en el corte 1 (se mira una vez por petición)
            if (hayColumnasModelo == null)
            {
                hayColumnasModelo = await ExisteColumna(SugerenciasProductosSql.TABLA_MOSTRADAS, "Fuentes").ConfigureAwait(false)
                    && await ExisteColumna(SugerenciasProductosSql.TABLA_MOSTRADAS, "TamanoCarrito").ConfigureAwait(false)
                    && await ExisteColumna(SugerenciasProductosSql.TABLA_MOSTRADAS, "Exploracion").ConfigureAwait(false);
            }
            var parametros = new List<SqlParameter>
            {
                SugerenciasProductosSql.Identificador("@p0", m.SesionId),
                SugerenciasProductosSql.Varchar("@p1", m.Producto, 15), SugerenciasProductosSql.Varchar("@p2", m.Motivo, 20),
                new SqlParameter("@p3", SqlDbType.Decimal) { Precision = 12, Scale = 4, Value = Math.Round(Math.Min(m.Puntuacion, 99999999m), 4) },
                new SqlParameter("@p4", SqlDbType.TinyInt) { Value = m.Posicion },
                SugerenciasProductosSql.Varchar("@p5", m.VersionMotor, 20), SugerenciasProductosSql.Nvarchar("@p6", m.Texto, 200)
            };
            if (hayColumnasModelo != true)
            {
                return await db.Database.SqlQuery<int>(SugerenciasProductosSql.SQL_INSERTAR_MOSTRADA, parametros.Cast<object>().ToArray())
                    .FirstAsync().ConfigureAwait(false);
            }
            parametros.Add(SugerenciasProductosSql.Varchar("@p7", m.Fuentes, 50));
            parametros.Add(new SqlParameter("@p8", SqlDbType.Int) { Value = (object)m.TamanoCarrito ?? DBNull.Value });
            parametros.Add(new SqlParameter("@p9", SqlDbType.Bit) { Value = m.Exploracion });
            return await db.Database.SqlQuery<int>(SugerenciasProductosSql.SQL_INSERTAR_MOSTRADA_MODELO, parametros.Cast<object>().ToArray())
                .FirstAsync().ConfigureAwait(false);
        }

        public Task GuardarRespuesta(int id, string respuesta)
            => db.Database.ExecuteSqlCommandAsync(SugerenciasProductosSql.SQL_GUARDAR_RESPUESTA, SugerenciasProductosSql.Entero("@p0", id),
                SugerenciasProductosSql.Varchar("@p1", respuesta, 20));

        public async Task<HashSet<string>> LeerRechazados(string empresa, string cliente, DateTime desde)
        {
            List<string> productos = await db.Database.SqlQuery<string>(SugerenciasProductosSql.SQL_RECHAZADOS,
                SugerenciasProductosSql.Char("@p0", empresa, 3), SugerenciasProductosSql.Varchar("@p1", cliente, 10),
                SugerenciasProductosSql.Fecha("@p2", desde)).ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(productos.Where(p => p != null).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        // La consulta de #661 (un cliente, búsqueda por los índices que empiezan por Nº Cliente: milisegundos).
        public Task<List<CompraDiaReposicion>> LeerComprasCliente(string empresa, string cliente, DateTime desde, DateTime hasta)
            => db.Database.SqlQuery<CompraDiaReposicion>(SelectorRecordatoriosReposicion.SQL_COMPRAS_CLIENTE,
                new SqlParameter("@empresa", empresa), new SqlParameter("@desde", desde), new SqlParameter("@hasta", hasta),
                new SqlParameter("@cliente", SqlDbType.VarChar, 10) { Value = cliente.Trim() }).ToListAsync();

        public async Task<HashSet<string>> LeerPendientesCliente(string empresa, string cliente)
        {
            List<string> productos = await db.Database.SqlQuery<string>(SugerenciasProductosSql.SQL_PENDIENTES_CLIENTE,
                new SqlParameter("@empresa", empresa), new SqlParameter("@cliente", SqlDbType.VarChar, 10) { Value = cliente.Trim() })
                .ToListAsync().ConfigureAwait(false);
            return new HashSet<string>(productos.Where(p => p != null).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        public async Task<List<ParCompradoJunto>> LeerPares(IEnumerable<string> productosCarrito)
        {
            var pares = new List<ParCompradoJunto>();
            List<string> lista = Limpios(productosCarrito);
            if (lista.Count == 0 || !await ExisteTabla(SugerenciasProductosSql.TABLA_PARES).ConfigureAwait(false))
            {
                return pares;
            }
            for (int i = 0; i < lista.Count; i += SugerenciasProductosSql.TAMANO_LOTE)
            {
                List<string> lote = lista.Skip(i).Take(SugerenciasProductosSql.TAMANO_LOTE).ToList();
                List<SqlParameter> parametros = lote.Select((p, n) => SugerenciasProductosSql.Varchar("@a" + n, p, 15)).ToList();
                string sql = "SELECT RTRIM(ProductoA) AS ProductoA, RTRIM(ProductoB) AS ProductoB, VecesJuntos, Soporte, Confianza, Lift " +
                    "FROM dbo.SugerenciasProductosPares WHERE ProductoA IN (" + string.Join(", ", parametros.Select(p => p.ParameterName)) + ")";
                pares.AddRange(await db.Database.SqlQuery<ParCompradoJunto>(sql, parametros.Cast<object>().ToArray()).ToListAsync().ConfigureAwait(false));
            }
            return pares;
        }

        public async Task<List<CandidatoPotenciar>> LeerPotenciar(string empresa, DateTime hoy)
        {
            DateTime dia = hoy.Date;
            var candidatos = new List<CandidatoPotenciar>();

            // Ofertas de producto concreto, para todos los clientes, que regalan algo y están vigentes (mismas fechas que
            // ServicioPrecios.BuscarOfertasPermitidas).
            var ofertas = await db.OfertasPermitidas
                .Where(o => o.Empresa == empresa && o.Número != null && o.Cliente == null && !o.Denegar && o.CantidadRegalo > 0
                    && (o.FechaDesde == null || o.FechaDesde <= dia) && (o.FechaHasta == null || o.FechaHasta >= dia))
                .Select(o => new { o.Número, o.CantidadConPrecio, o.CantidadRegalo })
                .ToListAsync().ConfigureAwait(false);
            candidatos.AddRange(ofertas.Select(o => new CandidatoPotenciar
            {
                Producto = o.Número.Trim(),
                Tipo = CandidatoPotenciar.TIPO_OFERTA,
                Detalle = $"{o.CantidadConPrecio}+{o.CantidadRegalo}"
            }));

            // Ofertas combinadas de campaña (con fecha de fin): las que no la tienen son configuración permanente.
            List<string> combinadas = await db.OfertasCombinadasDetalles
                .Where(d => d.Producto != null && d.OfertasCombinada.Empresa == empresa && d.OfertasCombinada.FechaHasta != null
                    && d.OfertasCombinada.FechaHasta >= dia && (d.OfertasCombinada.FechaDesde == null || d.OfertasCombinada.FechaDesde <= dia))
                .Select(d => d.Producto)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            candidatos.AddRange(combinadas.Select(p => new CandidatoPotenciar { Producto = p.Trim(), Tipo = CandidatoPotenciar.TIPO_OFERTA }));

            if (await ExisteTabla("dbo.PropuestasStock").ConfigureAwait(false))
            {
                if (await ExisteColumna("dbo.PropuestasStock", "PotenciarHasta").ConfigureAwait(false))
                {
                    List<string> potenciar = await db.Database.SqlQuery<string>(SugerenciasProductosSql.SQL_POTENCIAR_HASTA,
                        new SqlParameter("@p0", SqlDbType.Date) { Value = dia }).ToListAsync().ConfigureAwait(false);
                    candidatos.AddRange(potenciar.Where(p => p != null).Select(p => new CandidatoPotenciar { Producto = p.Trim(), Tipo = CandidatoPotenciar.TIPO_POTENCIAR }));
                }
                List<string> novedades = await db.Database.SqlQuery<string>(SugerenciasProductosSql.SQL_NOVEDADES,
                    SugerenciasProductosSql.Entero("@p0", MotorSugerenciasProductos.MESES_NOVEDAD)).ToListAsync().ConfigureAwait(false);
                candidatos.AddRange(novedades.Where(p => p != null).Select(p => new CandidatoPotenciar { Producto = p.Trim(), Tipo = CandidatoPotenciar.TIPO_NOVEDAD }));
            }
            return candidatos;
        }

        public async Task<Dictionary<string, int>> LeerClientesPerfil(string grupo, string subGrupo)
        {
            var resultado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(grupo) || !await ExisteTabla(SugerenciasProductosSql.TABLA_PERFILES).ConfigureAwait(false))
            {
                return resultado;
            }
            List<ProductoPerfil> filas = await db.Database.SqlQuery<ProductoPerfil>(SugerenciasProductosSql.SQL_CLIENTES_PERFIL,
                SugerenciasProductosSql.Varchar("@p0", grupo, 3), SugerenciasProductosSql.Varchar("@p1", subGrupo ?? string.Empty, 3))
                .ToListAsync().ConfigureAwait(false);
            foreach (ProductoPerfil fila in filas.Where(f => f.Producto != null))
            {
                resultado[fila.Producto.Trim()] = fila.Clientes;
            }
            return resultado;
        }

        public async Task<List<AfinidadCliente>> LeerAfinidad(string cliente)
        {
            if (string.IsNullOrWhiteSpace(cliente) || !await ExisteTabla(SugerenciasProductosSql.TABLA_AFINIDAD).ConfigureAwait(false))
            {
                return new List<AfinidadCliente>();
            }
            return await db.Database.SqlQuery<AfinidadCliente>(SugerenciasProductosSql.SQL_AFINIDAD_CLIENTE,
                SugerenciasProductosSql.Varchar("@p0", cliente, 10)).ToListAsync().ConfigureAwait(false);
        }

        public async Task<Dictionary<string, FichaProductoSugerencia>> LeerFichas(string empresa, IEnumerable<string> productos)
        {
            var resultado = new Dictionary<string, FichaProductoSugerencia>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = Limpios(productos);
            for (int i = 0; i < lista.Count; i += SugerenciasProductosSql.TAMANO_LOTE)
            {
                List<string> lote = lista.Skip(i).Take(SugerenciasProductosSql.TAMANO_LOTE).ToList();
                var fichas = await db.Productos
                    .Where(p => p.Empresa == empresa && lote.Contains(p.Número))
                    .Select(p => new { p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, p.Estado, p.Ficticio })
                    .ToListAsync().ConfigureAwait(false);
                foreach (var f in fichas)
                {
                    resultado[f.Número.Trim()] = new FichaProductoSugerencia
                    {
                        Producto = f.Número.Trim(),
                        Nombre = f.Nombre?.Trim(),
                        Grupo = f.Grupo?.Trim(),
                        SubGrupo = f.SubGrupo?.Trim(),
                        Familia = f.Familia?.Trim(),
                        Estado = f.Estado ?? 0,
                        Ficticio = f.Ficticio
                    };
                }
            }
            return resultado;
        }

        public async Task<HashSet<string>> LeerVendibles(string empresa, IEnumerable<string> productos)
        {
            List<string> lista = Limpios(productos);
            var vendibles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lista.Count == 0)
            {
                return vendibles;
            }
            // El disponible de #532 (sedes − pendiente de entregar + pendiente de reposición).
            ResumenStocksProductos resumen = new ServicioGestorStocks().LeerResumenStocks(lista);
            HashSet<string> sinStock = CalculadoraReposicion.ProductosSinStock(resumen, lista);
            foreach (string producto in lista.Where(p => !sinStock.Contains(p)))
            {
                _ = vendibles.Add(producto);
            }
            List<string> sinStockLista = sinStock.ToList();
            if (sinStockLista.Count > 0)
            {
                // Sin stock, pero con fecha de llegada: un pedido a proveedor enviado y sin recibir (como ProductoService).
                List<string> conLlegada = await db.LinPedidoCmps
                    .Where(l => (l.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO || l.Empresa == Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO)
                        && sinStockLista.Contains(l.Producto) && l.Enviado && l.FechaRecepción != null
                        && (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO))
                    .Select(l => l.Producto)
                    .Distinct()
                    .ToListAsync().ConfigureAwait(false);
                foreach (string producto in conLlegada.Where(p => p != null))
                {
                    _ = vendibles.Add(producto.Trim());
                }
            }
            return vendibles;
        }

        public async Task<DatosGrupoVendedor> LeerDatosGrupoVendedor(string empresa, string cliente, string contacto, string vendedor)
        {
            var datos = new DatosGrupoVendedor();
            string codigo = vendedor?.Trim();
            if (!string.IsNullOrEmpty(codigo))
            {
                datos.EstadoVendedor = await db.Vendedores
                    .Where(v => (v.Empresa == empresa || v.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO) && v.Número == codigo)
                    .OrderBy(v => v.Empresa == empresa ? 0 : 1)
                    .Select(v => (short?)v.Estado)
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
            var fichaCliente = await db.Clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && (contacto == null ? c.ClientePrincipal : c.Contacto == contacto))
                .Select(c => new { c.Contacto, c.Vendedor })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (fichaCliente != null)
            {
                datos.VendedorEsteticaCliente = fichaCliente.Vendedor?.Trim();
                string contactoFicha = fichaCliente.Contacto;
                datos.VendedorPeluqueriaCliente = (await db.VendedoresClientesGruposProductos
                    .Where(v => v.Empresa == empresa && v.Cliente == cliente && v.Contacto == contactoFicha
                        && v.GrupoProducto == Constantes.Productos.GRUPO_PELUQUERIA)
                    .Select(v => v.Vendedor)
                    .FirstOrDefaultAsync().ConfigureAwait(false))?.Trim();
            }
            return datos;
        }

        public Task EnlazarPedido(Guid sesion, int pedido)
            => db.Database.ExecuteSqlCommandAsync(SugerenciasProductosSql.SQL_ENLAZAR_PEDIDO, SugerenciasProductosSql.Identificador("@p0", sesion),
                SugerenciasProductosSql.Entero("@p1", pedido));

        public Task MarcarEntradaEnPedido(int id, bool entro, int? cantidad)
            => db.Database.ExecuteSqlCommandAsync(SugerenciasProductosSql.SQL_MARCAR_ENTRADA, SugerenciasProductosSql.Entero("@p0", id),
                new SqlParameter("@p1", SqlDbType.Bit) { Value = entro },
                new SqlParameter("@p2", SqlDbType.Int) { Value = (object)cantidad ?? DBNull.Value });

        private static List<string> Limpios(IEnumerable<string> productos)
            => (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
