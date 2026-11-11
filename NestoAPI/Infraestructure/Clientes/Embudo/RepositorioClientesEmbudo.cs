using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Embudo
{
    /// <summary>Un cliente que cumple las reglas 1-4 de la republicación (vendedor, peluquería, estados y, si se pide, correo).</summary>
    public class CandidatoEmbudo
    {
        public string Cliente { get; set; }
        public int Contactos { get; set; }
        public int ContactosActivos { get; set; }
        public int PersonasConCorreo { get; set; }
        /// <summary>Ya está en dbo.ClientesEmbudo (una sola vez, nunca se borra).</summary>
        public bool YaEnviado { get; set; }
        /// <summary>Tiene una fila de Nesto_sync sin sincronizar.</summary>
        public bool PendienteSincronizar { get; set; }
        public DateTime? UltimoPedido { get; set; }
        /// <summary>El alta más antigua de sus contactos (para ordenar a los que nunca han comprado).</summary>
        public DateTime? FechaAlta { get; set; }
    }

    /// <summary>Una fila para sembrar el registro con clientes que ya pasaron por el embudo de Odoo por otra puerta.</summary>
    public class FilaSiembraEmbudo
    {
        public string Cliente { get; set; }
        public DateTime? Fecha { get; set; }
        public string Origen { get; set; }
    }

    public class EstadoSiembraEmbudo
    {
        public string Cliente { get; set; }
        public bool Existe { get; set; }
        public bool YaEstaba { get; set; }
    }

    /// <summary>
    /// Embudo de Odoo (odoo-custom-addons#45): <c>dbo.ClientesEmbudo</c>, fuera del EDMX (script
    /// <c>Scripts/Issue_EmbudoOdooNV_ClientesEmbudo.sql</c>). Registro PERMANENTE de los clientes que han entrado en el embudo: cada
    /// cliente, una sola vez. FechaEnviado NULL = el bloque está pendiente de llegar a Odoo; el job de Nesto_sync añade los campos
    /// del embudo a todos sus contactos mientras siga así y la rellena cuando se han publicado todos.
    /// </summary>
    public interface IRepositorioClientesEmbudo
    {
        Task<bool> HayTabla();
        /// <summary>Los clientes que cumplen las reglas 1-4, en el orden de la republicación (Carlos 11/11/26: los más recientes
        /// primero, que rebotan menos: el último pedido más reciente; después los que nunca han comprado, el alta más reciente; al final
        /// los más antiguos), marcando los ya enviados y los pendientes de sincronizar.</summary>
        Task<List<CandidatoEmbudo>> LeerCandidatos(string vendedor, IList<short> estados, bool soloConCorreo);
        /// <summary>En una transacción: los mete en el registro con su bloque (los que no estuvieran) y encola esos en Nesto_sync.
        /// Devuelve cuántos entraron.</summary>
        Task<int> Registrar(string bloque, IList<string> clientes, string usuario);
        /// <summary>El bloque pendiente de enviar del cliente, o null (sin tabla, también null).</summary>
        Task<string> BloquePendiente(string cliente);
        Task MarcarEnviado(string cliente, string bloque);
        Task<List<EstadoSiembraEmbudo>> EstadoSiembra(IList<string> clientes);
        /// <summary>Mete las filas que no estuvieran ya (de clientes que existen), como ya enviadas. Devuelve cuántas entraron.</summary>
        Task<int> Sembrar(IList<FilaSiembraEmbudo> filas, string usuario);
        /// <summary>Embudo diario: los bloques con alguna fila desde ese día (nombre y cuándo se lanzaron). Sin tabla, vacío.</summary>
        Task<List<BloqueEmbudo>> LeerBloquesDesde(DateTime desde);
    }

    /// <summary>Embudo diario: un bloque del registro y su primera fila.</summary>
    public class BloqueEmbudo
    {
        public string Bloque { get; set; }
        public DateTime Fecha { get; set; }
    }

    public class RepositorioClientesEmbudoSql : IRepositorioClientesEmbudo
    {
        public const string NOMBRE_SCRIPT = "Scripts/Issue_EmbudoOdooNV_ClientesEmbudo.sql";
        public const string BLOQUE_SIEMBRA = "SIEMBRA";
        public const string ORIGEN_REPUBLICAR = "Nesto";
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        private const string HAY_TABLA = "IF OBJECT_ID(N'dbo.ClientesEmbudo', N'U') IS NOT NULL ";
        private const int FILAS_POR_LOTE = 500;

        private readonly Func<NVEntities> crearContexto;

        public RepositorioClientesEmbudoSql(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        public async Task<bool> HayTabla()
        {
            using (NVEntities db = crearContexto())
            {
                List<int> filas = await db.Database.SqlQuery<int>(
                    "SELECT CASE WHEN OBJECT_ID(N'dbo.ClientesEmbudo', N'U') IS NULL THEN 0 ELSE 1 END").ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault() == 1;
            }
        }

        /// <summary>
        /// Una sola consulta. Primero los clientes cuyos contactos activos son todos del vendedor y de los estados pedidos (un GROUP BY
        /// sobre los ~47.000 contactos de la empresa 1) a una tabla temporal; de ahí fuera los que tienen otro vendedor en algún grupo
        /// de producto; y solo para los que quedan, los conteos, el registro, Nesto_sync y el último pedido (mismo criterio que
        /// CalculoFechasComprasCliente: fecha de cabecera, línea en -1 o más, empresas 1 y espejo).
        /// </summary>
        internal static string SqlCandidatos(int numeroEstados)
        {
            string estados = string.Join(", ", Enumerable.Range(0, numeroEstados).Select(i => $"@e{i}"));
            return $@"
SET NOCOUNT ON;
IF OBJECT_ID(N'tempdb..#AptosEmbudo') IS NOT NULL DROP TABLE #AptosEmbudo;
SELECT c.[Nº Cliente] AS Cliente, COUNT(*) AS ContactosActivos, MIN(c.FechaAlta) AS FechaAlta
INTO #AptosEmbudo
FROM Clientes c WITH (NOLOCK)
WHERE c.Empresa = @empresa AND c.Estado >= 0
GROUP BY c.[Nº Cliente]
HAVING SUM(CASE WHEN c.Vendedor = @vendedor THEN 0 ELSE 1 END) = 0
   AND SUM(CASE WHEN c.Estado IN ({estados}) THEN 0 ELSE 1 END) = 0;

DELETE a FROM #AptosEmbudo a
WHERE EXISTS (SELECT 1 FROM VendedoresClienteGrupoProducto v WITH (NOLOCK)
              INNER JOIN Clientes c WITH (NOLOCK) ON c.Empresa = v.Empresa AND c.[Nº Cliente] = v.Cliente AND c.Contacto = v.Contacto
              WHERE v.Empresa = @empresa AND v.Cliente = a.Cliente AND c.Estado >= 0 AND v.Vendedor <> @vendedorGeneral);

SELECT RTRIM(t.Cliente) AS Cliente, t.Contactos, t.ContactosActivos, t.PersonasConCorreo, t.YaEnviado, t.PendienteSincronizar, t.UltimoPedido,
       t.FechaAlta
FROM (
    SELECT a.Cliente, a.ContactosActivos, a.FechaAlta,
        (SELECT COUNT(*) FROM Clientes c WITH (NOLOCK) WHERE c.Empresa = @empresa AND c.[Nº Cliente] = a.Cliente) AS Contactos,
        (SELECT COUNT(*) FROM PersonasContactoCliente p WITH (NOLOCK)
         INNER JOIN Clientes c WITH (NOLOCK) ON c.Empresa = p.Empresa AND c.[Nº Cliente] = p.[NºCliente] AND c.Contacto = p.Contacto
         WHERE p.Empresa = @empresa AND p.[NºCliente] = a.Cliente AND c.Estado >= 0 AND p.Estado >= 0
           AND p.CorreoElectrónico LIKE '%@%' AND p.EnviarBoletin = 1) AS PersonasConCorreo,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ClientesEmbudo e WITH (NOLOCK) WHERE e.Empresa = @empresa AND e.Cliente = a.Cliente)
             THEN 1 ELSE 0 END AS bit) AS YaEnviado,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM Nesto_sync ns WITH (NOLOCK)
                               WHERE ns.Tabla = 'Clientes' AND ns.Sincronizado IS NULL
                                 AND RTRIM(ns.ModificadoId) COLLATE DATABASE_DEFAULT = RTRIM(a.Cliente) COLLATE DATABASE_DEFAULT)
             THEN 1 ELSE 0 END AS bit) AS PendienteSincronizar,
        (SELECT MAX(cab.Fecha) FROM LinPedidoVta l WITH (NOLOCK)
         INNER JOIN CabPedidoVta cab WITH (NOLOCK) ON cab.Empresa = l.Empresa AND cab.Número = l.Número
         WHERE l.Empresa IN (@empresa, @empresaEspejo) AND l.[Nº Cliente] = a.Cliente AND l.Estado >= @pendiente) AS UltimoPedido
    FROM #AptosEmbudo a
) t
WHERE @soloConCorreo = 0 OR t.PersonasConCorreo > 0
ORDER BY CASE WHEN t.UltimoPedido IS NULL THEN 1 ELSE 0 END, t.UltimoPedido DESC, t.FechaAlta DESC, LEN(t.Cliente) DESC, t.Cliente DESC;

DROP TABLE #AptosEmbudo;";
        }

        public async Task<List<CandidatoEmbudo>> LeerCandidatos(string vendedor, IList<short> estados, bool soloConCorreo)
        {
            var parametros = new List<object>
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                new SqlParameter("@empresaEspejo", SqlDbType.Char, 3) { Value = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO },
                new SqlParameter("@vendedor", SqlDbType.Char, 3) { Value = vendedor },
                new SqlParameter("@vendedorGeneral", SqlDbType.Char, 3) { Value = Constantes.Vendedores.VENDEDOR_GENERAL },
                new SqlParameter("@pendiente", SqlDbType.SmallInt) { Value = Constantes.EstadosLineaVenta.PENDIENTE },
                new SqlParameter("@soloConCorreo", SqlDbType.Bit) { Value = soloConCorreo }
            };
            for (int i = 0; i < estados.Count; i++)
            {
                parametros.Add(new SqlParameter($"@e{i}", SqlDbType.SmallInt) { Value = estados[i] });
            }
            using (NVEntities db = crearContexto())
            {
                // Lee mucho: más margen que los 30 s de siempre
                db.Database.CommandTimeout = 120;
                return await db.Database.SqlQuery<CandidatoEmbudo>(SqlCandidatos(estados.Count), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        internal static string SqlRegistrar(int numeroClientes)
        {
            string valores = string.Join(", ", Enumerable.Range(0, numeroClientes).Select(i => $"(@c{i})"));
            return $@"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @nuevos TABLE (Cliente char(10) COLLATE DATABASE_DEFAULT NOT NULL);
BEGIN TRANSACTION;
INSERT INTO dbo.ClientesEmbudo (Empresa, Cliente, Bloque, Fecha, Usuario, Origen, FechaEnviado)
OUTPUT inserted.Cliente INTO @nuevos (Cliente)
SELECT @empresa, v.Cliente, @bloque, GETDATE(), @usuario, @origen, NULL
FROM (VALUES {valores}) v(Cliente)
WHERE NOT EXISTS (SELECT 1 FROM dbo.ClientesEmbudo e WITH (UPDLOCK, HOLDLOCK) WHERE e.Empresa = @empresa AND e.Cliente = v.Cliente);
INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Clientes', RTRIM(n.Cliente), @usuario, GETDATE()
FROM @nuevos n
WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                  WHERE ns.Tabla = 'Clientes' AND ns.Sincronizado IS NULL
                    AND RTRIM(ns.ModificadoId) COLLATE DATABASE_DEFAULT = RTRIM(n.Cliente) COLLATE DATABASE_DEFAULT);
COMMIT TRANSACTION;
SELECT COUNT(*) FROM @nuevos;";
        }

        public async Task<int> Registrar(string bloque, IList<string> clientes, string usuario)
        {
            if (clientes == null || clientes.Count == 0)
            {
                return 0;
            }
            var parametros = new List<object>
            {
                new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                new SqlParameter("@bloque", SqlDbType.VarChar, 30) { Value = bloque },
                new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) },
                new SqlParameter("@origen", SqlDbType.VarChar, 30) { Value = ORIGEN_REPUBLICAR }
            };
            for (int i = 0; i < clientes.Count; i++)
            {
                parametros.Add(new SqlParameter($"@c{i}", SqlDbType.Char, 10) { Value = clientes[i] });
            }
            using (NVEntities db = crearContexto())
            {
                List<int> resultado = await db.Database.SqlQuery<int>(SqlRegistrar(clientes.Count), parametros.ToArray())
                    .ToListAsync().ConfigureAwait(false);
                return resultado.FirstOrDefault();
            }
        }

        internal const string SQL_BLOQUES_DESDE = HAY_TABLA +
            "SELECT RTRIM(Bloque) AS Bloque, MIN(Fecha) AS Fecha FROM dbo.ClientesEmbudo WITH (NOLOCK) " +
            "WHERE Empresa = @empresa AND Fecha >= @desde GROUP BY Bloque";

        public async Task<List<BloqueEmbudo>> LeerBloquesDesde(DateTime desde)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Database.SqlQuery<BloqueEmbudo>(SQL_BLOQUES_DESDE,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde })
                    .ToListAsync().ConfigureAwait(false);
            }
        }

        internal const string SQL_BLOQUE_PENDIENTE = HAY_TABLA +
            "SELECT TOP 1 RTRIM(Bloque) FROM dbo.ClientesEmbudo WITH (NOLOCK) WHERE Empresa = @empresa AND Cliente = @cliente AND FechaEnviado IS NULL";

        public async Task<string> BloquePendiente(string cliente)
        {
            using (NVEntities db = crearContexto())
            {
                List<string> filas = await db.Database.SqlQuery<string>(SQL_BLOQUE_PENDIENTE,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente?.Trim() ?? string.Empty })
                    .ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        internal const string SQL_MARCAR_ENVIADO = HAY_TABLA +
            "UPDATE dbo.ClientesEmbudo SET FechaEnviado = GETDATE() WHERE Empresa = @empresa AND Cliente = @cliente AND Bloque = @bloque AND FechaEnviado IS NULL";

        public async Task MarcarEnviado(string cliente, string bloque)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_MARCAR_ENVIADO,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente?.Trim() ?? string.Empty },
                    new SqlParameter("@bloque", SqlDbType.VarChar, 30) { Value = bloque }).ConfigureAwait(false);
            }
        }

        internal static string SqlEstadoSiembra(int numeroClientes)
        {
            string valores = string.Join(", ", Enumerable.Range(0, numeroClientes).Select(i => $"(@c{i})"));
            return $@"
SELECT RTRIM(v.Cliente) AS Cliente,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM Clientes c WITH (NOLOCK) WHERE c.Empresa = @empresa AND c.[Nº Cliente] = v.Cliente) THEN 1 ELSE 0 END AS bit) AS Existe,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.ClientesEmbudo e WITH (NOLOCK) WHERE e.Empresa = @empresa AND e.Cliente = v.Cliente) THEN 1 ELSE 0 END AS bit) AS YaEstaba
FROM (VALUES {valores}) v(Cliente);";
        }

        public async Task<List<EstadoSiembraEmbudo>> EstadoSiembra(IList<string> clientes)
        {
            var resultado = new List<EstadoSiembraEmbudo>();
            using (NVEntities db = crearContexto())
            {
                for (int inicio = 0; inicio < clientes.Count; inicio += FILAS_POR_LOTE)
                {
                    List<string> lote = clientes.Skip(inicio).Take(FILAS_POR_LOTE).ToList();
                    var parametros = new List<object> { new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA } };
                    for (int i = 0; i < lote.Count; i++)
                    {
                        parametros.Add(new SqlParameter($"@c{i}", SqlDbType.Char, 10) { Value = lote[i] });
                    }
                    resultado.AddRange(await db.Database.SqlQuery<EstadoSiembraEmbudo>(SqlEstadoSiembra(lote.Count), parametros.ToArray())
                        .ToListAsync().ConfigureAwait(false));
                }
            }
            return resultado;
        }

        internal static string SqlSembrar(int numeroFilas)
        {
            string valores = string.Join(", ", Enumerable.Range(0, numeroFilas).Select(i => $"(@c{i}, @f{i}, @o{i})"));
            return $@"
INSERT INTO dbo.ClientesEmbudo (Empresa, Cliente, Bloque, Fecha, Usuario, Origen, FechaEnviado)
SELECT @empresa, v.Cliente, @bloque, v.Fecha, @usuario, v.Origen, v.Fecha
FROM (VALUES {valores}) v(Cliente, Fecha, Origen)
WHERE EXISTS (SELECT 1 FROM Clientes c WITH (NOLOCK) WHERE c.Empresa = @empresa AND c.[Nº Cliente] = v.Cliente)
  AND NOT EXISTS (SELECT 1 FROM dbo.ClientesEmbudo e WITH (UPDLOCK, HOLDLOCK) WHERE e.Empresa = @empresa AND e.Cliente = v.Cliente);";
        }

        public async Task<int> Sembrar(IList<FilaSiembraEmbudo> filas, string usuario)
        {
            int metidas = 0;
            using (NVEntities db = crearContexto())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                // Lotes de 500 filas (3 parámetros cada una: el límite de SQL Server son 2.100 por sentencia)
                for (int inicio = 0; inicio < filas.Count; inicio += FILAS_POR_LOTE)
                {
                    List<FilaSiembraEmbudo> lote = filas.Skip(inicio).Take(FILAS_POR_LOTE).ToList();
                    var parametros = new List<object>
                    {
                        new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = EMPRESA },
                        new SqlParameter("@bloque", SqlDbType.VarChar, 30) { Value = BLOQUE_SIEMBRA },
                        new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) }
                    };
                    for (int i = 0; i < lote.Count; i++)
                    {
                        parametros.Add(new SqlParameter($"@c{i}", SqlDbType.Char, 10) { Value = lote[i].Cliente });
                        parametros.Add(new SqlParameter($"@f{i}", SqlDbType.DateTime) { Value = lote[i].Fecha.Value });
                        parametros.Add(new SqlParameter($"@o{i}", SqlDbType.VarChar, 30) { Value = lote[i].Origen });
                    }
                    metidas += await db.Database.ExecuteSqlCommandAsync(SqlSembrar(lote.Count), parametros.ToArray()).ConfigureAwait(false);
                }
                transaccion.Commit();
            }
            return metidas;
        }
    }
}
