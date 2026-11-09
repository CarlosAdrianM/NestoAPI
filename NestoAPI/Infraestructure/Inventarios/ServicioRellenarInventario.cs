using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>NestoAPI#789: lo que pide Nesto para rellenar el inventario de una tienda.</summary>
    public class RellenarInventarioDTO
    {
        public string Empresa { get; set; }
        public string Almacen { get; set; }
        /// <summary>El día del inventario (se ignora la hora). Sin fecha, hoy.</summary>
        public DateTime? Fecha { get; set; }
        /// <summary>
        /// NestoAPI#784 (09/11/26): «Contar otra vez». Sin él, si ese día ya hay filas contabilizadas en el almacén, 409
        /// <c>YaContabilizado</c>. Con filas pendientes sigue siendo 409 <c>YaRellenado</c>.
        /// </summary>
        public bool OtraVez { get; set; }
    }

    /// <summary>NestoAPI#784: los códigos del 409 de rellenar y de GET/POST api/Inventarios (el campo Codigo del cuerpo).</summary>
    public static class CodigosConflictoInventario
    {
        /// <summary>Ya hay filas pendientes (Estado 1) de eso ese día: no se puede rellenar otra vez (ni con OtraVez).</summary>
        public const string YA_RELLENADO = "YaRellenado";
        /// <summary>Eso ya se contó y contabilizó ese día: se puede repetir con OtraVez = true («Contar otra vez»).</summary>
        public const string YA_CONTABILIZADO = "YaContabilizado";
        /// <summary>GET api/Inventarios: el producto tiene más de una fila pendiente ese día (Lineas): hay que dejar una.</summary>
        public const string FILAS_DUPLICADAS = "FilasDuplicadas";
        /// <summary>GET api/Inventarios sin ubicación: el producto tiene filas pendientes en varias ubicaciones (Algete): hay que decir cuál.</summary>
        public const string VARIAS_UBICACIONES = "VariasUbicaciones";
        /// <summary>POST api/Inventarios: el producto ya tiene fila pendiente ese día (Lineas): se suma a esa (PUT), no se crea otra.</summary>
        public const string YA_TIENE_FILA = "YaTieneFila";
    }

    /// <summary>NestoAPI#784: lo ya contabilizado ese día en el almacén, por usuario y traspaso.</summary>
    public class ContabilizadasInventarioUsuario
    {
        public string Usuario { get; set; }
        public int Filas { get; set; }
        public int? Traspaso { get; set; }
    }

    /// <summary>NestoAPI#784: el cuerpo del 409 de POST api/Inventarios/Rellenar.</summary>
    public class YaRellenadoTiendaInventarioDTO
    {
        public string Message { get; set; }
        /// <summary>YaRellenado o YaContabilizado (<see cref="CodigosConflictoInventario"/>).</summary>
        public string Codigo { get; set; }
        /// <summary>True solo con YaContabilizado: se puede repetir con OtraVez = true.</summary>
        public bool PuedeContarOtraVez { get; set; }
        public List<PendientesInventarioUsuario> Pendientes { get; set; } = new List<PendientesInventarioUsuario>();
        public List<ContabilizadasInventarioUsuario> Contabilizadas { get; set; } = new List<ContabilizadasInventarioUsuario>();
    }

    /// <summary>NestoAPI#789: lo que se ha rellenado.</summary>
    public class RellenarInventarioRespuestaDTO
    {
        public string Almacen { get; set; }
        public DateTime Fecha { get; set; }
        public int Filas { get; set; }
        /// <summary>Suma de StockCalculado de las filas insertadas.</summary>
        public int Unidades { get; set; }
        public string Usuario { get; set; }
    }

    /// <summary>NestoAPI#789: quién tiene ya filas pendientes de ese almacén ese día.</summary>
    public class PendientesInventarioUsuario
    {
        public string Usuario { get; set; }
        public int Filas { get; set; }
    }

    /// <summary>NestoAPI#789: el stock de un producto en el almacén (suma de ExtractoProducto de las empresas 1 y 3).</summary>
    public class StockProductoInventario
    {
        public string Numero { get; set; }
        public string Descripcion { get; set; }
        public string Grupo { get; set; }
        public string SubGrupo { get; set; }
        public string Familia { get; set; }
        public bool Ficticio { get; set; }
        public int Stock { get; set; }
    }

    public class AlmacenInventario
    {
        public string Nombre { get; set; }
        public bool ControlUbicaciones { get; set; }
    }

    public enum EstadoRellenarInventario
    {
        Ok,
        PeticionIncorrecta,
        ConUbicaciones,
        YaRellenado,
        /// <summary>NestoAPI#784: ese día ya hay filas contabilizadas en el almacén y no se ha pedido OtraVez (409).</summary>
        YaContabilizado
    }

    public class ResultadoRellenarInventario
    {
        public EstadoRellenarInventario Estado { get; set; }
        public string Mensaje { get; set; }
        public RellenarInventarioRespuestaDTO Respuesta { get; set; }
        /// <summary>Con <see cref="EstadoRellenarInventario.YaRellenado"/>: las filas pendientes por usuario.</summary>
        public List<PendientesInventarioUsuario> Pendientes { get; set; }
        /// <summary>NestoAPI#784: con <see cref="EstadoRellenarInventario.YaContabilizado"/>, lo contabilizado ese día.</summary>
        public List<ContabilizadasInventarioUsuario> Contabilizadas { get; set; }
    }

    public interface IServicioRellenarInventario
    {
        Task<ResultadoRellenarInventario> Rellenar(RellenarInventarioDTO peticion, string usuario);
    }

    public interface IRepositorioRellenarInventario
    {
        /// <summary>Null si el almacén no existe en la empresa.</summary>
        Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen);
        /// <summary>Abre la transacción, con el almacén bloqueado para que dos a la vez no rellenen dos veces.</summary>
        Task<IUnidadRellenarInventario> Empezar(string empresa, string almacen);
    }

    /// <summary>Lo que va dentro de la transacción. Sin <see cref="Confirmar"/>, el Dispose lo deshace.</summary>
    public interface IUnidadRellenarInventario : IDisposable
    {
        Task<List<PendientesInventarioUsuario>> Pendientes(string empresa, string almacen, DateTime fecha);
        /// <summary>NestoAPI#784: las filas contabilizadas (Estado 2, con NºTraspaso) del almacén ese día, por usuario y traspaso.</summary>
        Task<List<ContabilizadasInventarioUsuario>> Contabilizadas(string empresa, string almacen, DateTime fecha);
        Task<List<StockProductoInventario>> Stocks(string empresa, string almacen);
        Task Insertar(IList<Inventario> filas);
        void Confirmar();
    }

    /// <summary>
    /// NestoAPI#789 (épica #784): el INSERT inicial del inventario de las tiendas que antes se hacía a mano desde SSMS. Una fila
    /// por producto no ficticio con stock distinto de cero en el almacén, con StockReal 0, a nombre del usuario que luego
    /// cuenta y contabiliza. Solo almacenes sin control de ubicaciones (Algete va por ubicaciones desde Ariadna).
    /// </summary>
    public class ServicioRellenarInventario : IServicioRellenarInventario
    {
        public const byte ESTADO_SIN_CONTABILIZAR = 1;
        public const string APLICACION = "NestoAPI";

        private readonly IRepositorioRellenarInventario repositorio;

        public ServicioRellenarInventario(IRepositorioRellenarInventario repositorio)
        {
            this.repositorio = repositorio;
        }

        public async Task<ResultadoRellenarInventario> Rellenar(RellenarInventarioDTO peticion, string usuario)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Almacen))
            {
                return Error(EstadoRellenarInventario.PeticionIncorrecta, "Falta el almacén.");
            }
            string empresa = string.IsNullOrWhiteSpace(peticion.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : peticion.Empresa.Trim();
            string almacen = peticion.Almacen.Trim().ToUpperInvariant();
            DateTime fecha = (peticion.Fecha ?? DateTime.Today).Date;
            string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);

            AlmacenInventario datosAlmacen = await repositorio.LeerAlmacen(empresa, almacen).ConfigureAwait(false);
            if (datosAlmacen == null)
            {
                return Error(EstadoRellenarInventario.PeticionIncorrecta, $"No existe el almacén {almacen} en la empresa {empresa}.");
            }
            if (datosAlmacen.ControlUbicaciones)
            {
                string nombre = string.IsNullOrWhiteSpace(datosAlmacen.Nombre) ? almacen : datosAlmacen.Nombre.Trim();
                return Error(EstadoRellenarInventario.ConUbicaciones, $"{nombre} se inventaría por ubicaciones desde Ariadna.");
            }

            using (IUnidadRellenarInventario unidad = await repositorio.Empezar(empresa, almacen).ConfigureAwait(false))
            {
                List<PendientesInventarioUsuario> pendientes = await unidad.Pendientes(empresa, almacen, fecha).ConfigureAwait(false)
                    ?? new List<PendientesInventarioUsuario>();
                if (pendientes.Sum(p => p.Filas) > 0)
                {
                    return new ResultadoRellenarInventario
                    {
                        Estado = EstadoRellenarInventario.YaRellenado,
                        Mensaje = MensajeYaRellenado(almacen, fecha, pendientes),
                        Pendientes = pendientes
                    };
                }

                // NestoAPI#784 (09/11/26): lo contado y contabilizado ese día no se vuelve a poner a contar sin querer (la fila nueva
                // llevaría como teórico lo ya contado y, si nadie la cuenta, contabilizarla lo deja a cero). Con OtraVez, sí.
                if (!peticion.OtraVez)
                {
                    List<ContabilizadasInventarioUsuario> contabilizadas = await unidad.Contabilizadas(empresa, almacen, fecha).ConfigureAwait(false)
                        ?? new List<ContabilizadasInventarioUsuario>();
                    if (contabilizadas.Sum(c => c.Filas) > 0)
                    {
                        return new ResultadoRellenarInventario
                        {
                            Estado = EstadoRellenarInventario.YaContabilizado,
                            Mensaje = MensajeYaContabilizado(almacen, fecha, contabilizadas),
                            Contabilizadas = contabilizadas
                        };
                    }
                }

                List<StockProductoInventario> stocks = await unidad.Stocks(empresa, almacen).ConfigureAwait(false);
                List<Inventario> filas = FilasARellenar(stocks, empresa, almacen, fecha, usuarioAuditoria);
                if (filas.Count > 0)
                {
                    await unidad.Insertar(filas).ConfigureAwait(false);
                }
                unidad.Confirmar();

                return new ResultadoRellenarInventario
                {
                    Estado = EstadoRellenarInventario.Ok,
                    Respuesta = new RellenarInventarioRespuestaDTO
                    {
                        Almacen = almacen,
                        Fecha = fecha,
                        Filas = filas.Count,
                        Unidades = filas.Sum(f => f.StockCalculado ?? 0),
                        Usuario = usuarioAuditoria
                    }
                };
            }
        }

        /// <summary>La regla: productos no ficticios con stock distinto de cero (positivo o negativo), una fila por producto.</summary>
        internal static List<Inventario> FilasARellenar(IEnumerable<StockProductoInventario> stocks, string empresa, string almacen, DateTime fecha, string usuario)
        {
            return (stocks ?? Enumerable.Empty<StockProductoInventario>())
                .Where(s => s != null && !s.Ficticio && !string.IsNullOrWhiteSpace(s.Numero))
                .GroupBy(s => s.Numero.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Producto = g.First(), Stock = g.Sum(s => s.Stock) })
                .Where(x => x.Stock != 0)
                .OrderBy(x => x.Producto.Numero.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(x => new Inventario
                {
                    Empresa = empresa,
                    Almacén = almacen,
                    Fecha = fecha.Date,
                    Número = x.Producto.Numero.Trim(),
                    Descripción = x.Producto.Descripcion?.Trim(),
                    Grupo = x.Producto.Grupo?.Trim(),
                    Subgrupo = x.Producto.SubGrupo?.Trim(),
                    Familia = x.Producto.Familia?.Trim(),
                    StockCalculado = x.Stock,
                    StockReal = 0,
                    Estado = ESTADO_SIN_CONTABILIZAR,
                    Aplicacion = APLICACION,
                    Usuario = usuario,
                    PorCodigoBarras = null
                })
                .ToList();
        }

        internal static string MensajeYaRellenado(string almacen, DateTime fecha, List<PendientesInventarioUsuario> pendientes)
        {
            int total = pendientes.Sum(p => p.Filas);
            string quienes = string.Join(", ", pendientes
                .Where(p => p.Filas > 0)
                .OrderByDescending(p => p.Filas)
                .Select(p => $"{(string.IsNullOrWhiteSpace(p.Usuario) ? UsuarioAuditoriaHelper.DESCONOCIDO : p.Usuario.Trim())}: {p.Filas}"));
            string filas = total == 1 ? "1 fila pendiente" : $"{total} filas pendientes";
            return $"El inventario de {almacen} del {fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} ya está rellenado: " +
                $"hay {filas} de contabilizar ({quienes}).";
        }

        /// <summary>NestoAPI#784: «Ya hay 1 fila de ALC contada y contabilizada hoy (NUEVAVISION\Paloma: 1, traspaso 81350). Si de verdad...».</summary>
        internal static string MensajeYaContabilizado(string almacen, DateTime fecha, List<ContabilizadasInventarioUsuario> contabilizadas)
        {
            int total = contabilizadas.Sum(c => c.Filas);
            string filas = total == 1 ? "1 fila de " + almacen + " contada y contabilizada" : $"{total} filas de {almacen} contadas y contabilizadas";
            string quienes = string.Join(", ", contabilizadas
                .Where(c => c.Filas > 0)
                .OrderBy(c => c.Traspaso ?? 0)
                .Select(c => $"{(string.IsNullOrWhiteSpace(c.Usuario) ? UsuarioAuditoriaHelper.DESCONOCIDO : c.Usuario.Trim())}: {c.Filas}, traspaso {c.Traspaso}"));
            return $"Ya hay {filas} {ServicioInventarioUbicaciones.Cuando(fecha)} ({quienes}). Si de verdad hay que volver a contar, pulse «Contar otra vez».";
        }

        private static ResultadoRellenarInventario Error(EstadoRellenarInventario estado, string mensaje)
        {
            return new ResultadoRellenarInventario { Estado = estado, Mensaje = mensaje };
        }
    }

    /// <summary>NestoAPI#789: el acceso a la BD del rellenado, por ADO y en una transacción.</summary>
    public class RepositorioRellenarInventarioSql : IRepositorioRellenarInventario
    {
        private readonly string cadenaConexion;

        public RepositorioRellenarInventarioSql(NVEntities db)
        {
            cadenaConexion = db.Database.Connection.ConnectionString;
        }

        public async Task<AlmacenInventario> LeerAlmacen(string empresa, string almacen)
        {
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                using (var comando = new SqlCommand("SELECT Descripción, CAST(ControlUbicaciones AS bit) FROM Almacenes WHERE Empresa = @Empresa AND Número = @Almacen", conexion))
                {
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        if (!await lector.ReadAsync().ConfigureAwait(false))
                        {
                            return null;
                        }
                        return new AlmacenInventario
                        {
                            Nombre = lector.IsDBNull(0) ? null : lector.GetString(0).Trim(),
                            ControlUbicaciones = !lector.IsDBNull(1) && lector.GetBoolean(1)
                        };
                    }
                }
            }
        }

        public async Task<IUnidadRellenarInventario> Empezar(string empresa, string almacen)
        {
            var conexion = new SqlConnection(cadenaConexion);
            try
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted);
                var unidad = new UnidadRellenarInventarioSql(conexion, transaccion);
                await unidad.Bloquear(empresa, almacen).ConfigureAwait(false);
                return unidad;
            }
            catch
            {
                conexion.Dispose();
                throw;
            }
        }

        private class UnidadRellenarInventarioSql : IUnidadRellenarInventario
        {
            private const int FILAS_POR_LOTE = 200;
            private readonly SqlConnection conexion;
            private readonly SqlTransaction transaccion;
            private bool confirmada;

            public UnidadRellenarInventarioSql(SqlConnection conexion, SqlTransaction transaccion)
            {
                this.conexion = conexion;
                this.transaccion = transaccion;
            }

            /// <summary>Dos rellenados a la vez del mismo almacén: el segundo espera y, al entrar, ve las filas del primero (409).</summary>
            public async Task Bloquear(string empresa, string almacen)
            {
                const string sql = @"DECLARE @r int;
EXEC @r = sp_getapplock @Resource = @Recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;
IF @r < 0 THROW 50001, 'Otro usuario está rellenando el inventario de este almacén ahora mismo. Inténtelo de nuevo en unos segundos.', 1;";
                using (var comando = Comando(sql))
                {
                    comando.Parameters.Add("@Recurso", SqlDbType.NVarChar, 255).Value = $"InventarioRellenar:{empresa}:{almacen}";
                    await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            public async Task<List<PendientesInventarioUsuario>> Pendientes(string empresa, string almacen, DateTime fecha)
            {
                const string sql = @"SELECT RTRIM(Usuario) AS Usuario, COUNT(*) AS Filas
FROM Inventarios
WHERE Empresa = @Empresa AND Almacén = @Almacen AND Fecha >= @Desde AND Fecha < @Hasta AND Estado = 1 AND NºTraspaso IS NULL
GROUP BY RTRIM(Usuario)";
                var resultado = new List<PendientesInventarioUsuario>();
                using (var comando = Comando(sql))
                {
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = fecha.Date;
                    comando.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = fecha.Date.AddDays(1);
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new PendientesInventarioUsuario
                            {
                                Usuario = lector.IsDBNull(0) ? null : lector.GetString(0),
                                Filas = lector.GetInt32(1)
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task<List<ContabilizadasInventarioUsuario>> Contabilizadas(string empresa, string almacen, DateTime fecha)
            {
                const string sql = @"SELECT RTRIM(Usuario) AS Usuario, COUNT(*) AS Filas, NºTraspaso
FROM Inventarios
WHERE Empresa = @Empresa AND Almacén = @Almacen AND Fecha >= @Desde AND Fecha < @Hasta AND Estado = 2 AND NºTraspaso IS NOT NULL
GROUP BY RTRIM(Usuario), NºTraspaso
ORDER BY NºTraspaso";
                var resultado = new List<ContabilizadasInventarioUsuario>();
                using (var comando = Comando(sql))
                {
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    comando.Parameters.Add("@Desde", SqlDbType.DateTime).Value = fecha.Date;
                    comando.Parameters.Add("@Hasta", SqlDbType.DateTime).Value = fecha.Date.AddDays(1);
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new ContabilizadasInventarioUsuario
                            {
                                Usuario = lector.IsDBNull(0) ? null : lector.GetString(0),
                                Filas = lector.GetInt32(1),
                                Traspaso = lector.IsDBNull(2) ? (int?)null : lector.GetInt32(2)
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task<List<StockProductoInventario>> Stocks(string empresa, string almacen)
            {
                // Lo de siempre: la suma del extracto de las empresas 1 y 3 en el almacén, con los datos de la ficha de la empresa.
                const string sql = @"SELECT p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, CAST(p.Ficticio AS bit) AS Ficticio, SUM(CAST(e.Cantidad AS int)) AS Stock
FROM ExtractoProducto e
INNER JOIN Productos p ON p.Empresa = @Empresa AND p.Número = e.Número
WHERE e.Empresa IN ('1', '3') AND e.Almacén = @Almacen
GROUP BY p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, p.Ficticio
HAVING SUM(CAST(e.Cantidad AS int)) <> 0";
                var resultado = new List<StockProductoInventario>();
                using (var comando = Comando(sql))
                {
                    comando.CommandTimeout = 120;
                    comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = empresa;
                    comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = almacen;
                    using (var lector = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await lector.ReadAsync().ConfigureAwait(false))
                        {
                            resultado.Add(new StockProductoInventario
                            {
                                Numero = Texto(lector, 0),
                                Descripcion = Texto(lector, 1),
                                Grupo = Texto(lector, 2),
                                SubGrupo = Texto(lector, 3),
                                Familia = Texto(lector, 4),
                                Ficticio = !lector.IsDBNull(5) && lector.GetBoolean(5),
                                Stock = lector.GetInt32(6)
                            });
                        }
                    }
                }
                return resultado;
            }

            public async Task Insertar(IList<Inventario> filas)
            {
                for (int inicio = 0; inicio < filas.Count; inicio += FILAS_POR_LOTE)
                {
                    List<Inventario> lote = filas.Skip(inicio).Take(FILAS_POR_LOTE).ToList();
                    var sql = new StringBuilder("INSERT INTO Inventarios (Empresa, Almacén, Fecha, Número, Descripción, Grupo, Subgrupo, Familia, StockCalculado, StockReal, Estado, Aplicacion, Usuario) VALUES ");
                    using (var comando = Comando(string.Empty))
                    {
                        Inventario primera = lote[0];
                        comando.Parameters.Add("@Empresa", SqlDbType.Char, 3).Value = primera.Empresa;
                        comando.Parameters.Add("@Almacen", SqlDbType.Char, 3).Value = primera.Almacén;
                        comando.Parameters.Add("@Fecha", SqlDbType.DateTime).Value = primera.Fecha;
                        comando.Parameters.Add("@Estado", SqlDbType.TinyInt).Value = primera.Estado;
                        comando.Parameters.Add("@Aplicacion", SqlDbType.Char, 10).Value = primera.Aplicacion;
                        comando.Parameters.Add("@Usuario", SqlDbType.VarChar, 30).Value = primera.Usuario;
                        for (int i = 0; i < lote.Count; i++)
                        {
                            Inventario fila = lote[i];
                            if (i > 0)
                            {
                                sql.Append(", ");
                            }
                            sql.Append($"(@Empresa, @Almacen, @Fecha, @n{i}, @d{i}, @g{i}, @s{i}, @f{i}, @c{i}, 0, @Estado, @Aplicacion, @Usuario)");
                            comando.Parameters.Add($"@n{i}", SqlDbType.Char, 15).Value = fila.Número;
                            comando.Parameters.Add($"@d{i}", SqlDbType.Char, 50).Value = (object)fila.Descripción ?? DBNull.Value;
                            comando.Parameters.Add($"@g{i}", SqlDbType.Char, 3).Value = (object)fila.Grupo ?? DBNull.Value;
                            comando.Parameters.Add($"@s{i}", SqlDbType.Char, 3).Value = (object)fila.Subgrupo ?? DBNull.Value;
                            comando.Parameters.Add($"@f{i}", SqlDbType.Char, 10).Value = (object)fila.Familia ?? DBNull.Value;
                            comando.Parameters.Add($"@c{i}", SqlDbType.Int).Value = (object)fila.StockCalculado ?? DBNull.Value;
                        }
                        comando.CommandText = sql.ToString();
                        await comando.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }

            public void Confirmar()
            {
                transaccion.Commit();
                confirmada = true;
            }

            public void Dispose()
            {
                if (!confirmada)
                {
                    try
                    {
                        transaccion.Rollback();
                    }
                    catch (InvalidOperationException)
                    {
                        // Ya no estaba viva (la cerró un error de SQL): no hay nada que deshacer.
                    }
                }
                transaccion.Dispose();
                conexion.Dispose();
            }

            private SqlCommand Comando(string sql)
            {
                return new SqlCommand(sql, conexion, transaccion);
            }

            private static string Texto(SqlDataReader lector, int columna)
            {
                return lector.IsDBNull(columna) ? null : lector.GetString(columna).Trim();
            }
        }
    }
}
