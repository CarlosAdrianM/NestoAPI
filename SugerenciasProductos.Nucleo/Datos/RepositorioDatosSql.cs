using System.Data;
using System.Data.Common;

namespace ModeloSugerenciasProductos.Datos;

/// <summary>
/// Lectura de la BD de producción para el entrenamiento offline, SOLO LECTURA, con NOLOCK y READ UNCOMMITTED.
/// <para>Las compras se leen con la MISMA consulta que el job nocturno de la API (SelectorRecordatoriosReposicion.SQL_COMPRAS,
/// #532): la cubre entera un índice de LinPedidoVta (Producto, Nº Cliente, Empresa, Estado + Cantidad, Base Imponible y Fecha
/// Albarán), así que es un recorrido de ese índice sin búsquedas en la tabla (medido el 25/09/26: 24 meses, ~41.000 lecturas
/// lógicas, ~3 s). La fecha NO es clave de ese índice (va incluida), así que cada consulta lo recorre entero sea cual sea el
/// rango: partir la lectura en bloques multiplicaría las lecturas. Por eso <see cref="MESES_POR_BLOQUE"/> es mayor que la
/// ventana (una sola lectura); se puede bajar si algún día se quiere trocear (con una pausa entre bloques). NO añadir
/// columnas ni condiciones que no estén en ese índice.</para>
/// <para>Recibe la conexión (sin abrir): el núcleo no depende de ningún SqlClient (netstandard2.0, System.Data.Common).</para>
/// </summary>
public sealed class RepositorioDatosSql
{
    public const int TIEMPO_COMANDO_POR_DEFECTO = 600;
    public const int MESES_POR_BLOQUE = 48;
    public const string EMPRESA = "1";

    internal const string SQL_COMPRAS = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(l.[Nº Cliente]) AS Cliente, RTRIM(l.Producto) AS Producto,
       CAST(CAST(l.[Fecha Albarán] AS date) AS datetime) AS Dia, SUM(l.[Base Imponible]) AS BaseImponible,
       CAST(SUM(l.Cantidad) AS int) AS Cantidad
FROM LinPedidoVta l WITH (NOLOCK)
WHERE l.Empresa = @empresa AND l.Estado >= 2 AND l.Cantidad > 0 AND l.Producto IS NOT NULL
  AND l.[Fecha Albarán] >= @desde AND l.[Fecha Albarán] < @hasta
GROUP BY l.[Nº Cliente], l.Producto, CAST(l.[Fecha Albarán] AS date)";

    internal const string SQL_FICHAS = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;
SELECT RTRIM(p.Número) AS Producto, RTRIM(p.Nombre) AS Nombre, RTRIM(p.Grupo) AS Grupo, RTRIM(p.SubGrupo) AS SubGrupo,
       RTRIM(p.Familia) AS Familia, ISNULL(p.Estado, 0) AS Estado, p.Ficticio
FROM Productos p WITH (NOLOCK)
WHERE p.Empresa = @empresa";

    private readonly Func<DbConnection> _crearConexion;
    private readonly int _tiempoComando;
    private readonly TimeSpan _pausaEntreBloques;

    /// <param name="crearConexion">Conexión nueva sin abrir (se cierra al terminar cada bloque).</param>
    /// <param name="tiempoComandoSegundos">Tiempo de espera de cada consulta.</param>
    /// <param name="pausaEntreBloques">Respiro para la BD entre bloque y bloque (por defecto 2 s).</param>
    public RepositorioDatosSql(Func<DbConnection> crearConexion, int tiempoComandoSegundos = TIEMPO_COMANDO_POR_DEFECTO, TimeSpan? pausaEntreBloques = null)
    {
        _crearConexion = crearConexion ?? throw new ArgumentNullException(nameof(crearConexion));
        _tiempoComando = tiempoComandoSegundos;
        _pausaEntreBloques = pausaEntreBloques ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Los bloques [desde, hasta) en que se parte la lectura.</summary>
    public static List<(DateTime Desde, DateTime Hasta)> Bloques(DateTime desde, DateTime hasta, int mesesPorBloque = MESES_POR_BLOQUE)
    {
        var bloques = new List<(DateTime, DateTime)>();
        for (DateTime d = desde.Date; d < hasta.Date; d = d.AddMonths(mesesPorBloque))
        {
            DateTime h = d.AddMonths(mesesPorBloque);
            bloques.Add((d, h < hasta.Date ? h : hasta.Date));
        }
        return bloques;
    }

    /// <summary>Compras de [desde, hasta) y fichas de la empresa 1.</summary>
    public DatosCrudos Leer(DateTime desde, DateTime hasta, Action<string>? log = null)
    {
        var datos = new DatosCrudos();
        List<(DateTime Desde, DateTime Hasta)> bloques = Bloques(desde, hasta);
        for (int i = 0; i < bloques.Count; i++)
        {
            if (i > 0 && _pausaEntreBloques > TimeSpan.Zero) System.Threading.Thread.Sleep(_pausaEntreBloques);
            int antes = datos.Compras.Count;
            using DbConnection conexion = _crearConexion();
            conexion.Open();
            using DbCommand comando = Comando(conexion, SQL_COMPRAS);
            Parametro(comando, "@empresa", DbType.AnsiStringFixedLength, EMPRESA, 3);
            Parametro(comando, "@desde", DbType.DateTime, bloques[i].Desde);
            Parametro(comando, "@hasta", DbType.DateTime, bloques[i].Hasta);
            using DbDataReader lector = comando.ExecuteReader();
            while (lector.Read())
            {
                datos.Compras.Add(new Compra
                {
                    Cliente = Convert.ToString(lector[0])!.Trim(),
                    Producto = Convert.ToString(lector[1])!.Trim(),
                    Dia = lector.GetDateTime(2),
                    BaseImponible = lector.IsDBNull(3) ? 0 : Convert.ToDecimal(lector[3]),
                    Cantidad = lector.IsDBNull(4) ? 0 : Convert.ToInt32(lector[4])
                });
            }
            log?.Invoke($"  Compras del {bloques[i].Desde:dd/MM/yyyy} al {bloques[i].Hasta.AddDays(-1):dd/MM/yyyy}: {datos.Compras.Count - antes} filas.");
        }

        using (DbConnection conexion = _crearConexion())
        {
            conexion.Open();
            using DbCommand comando = Comando(conexion, SQL_FICHAS);
            Parametro(comando, "@empresa", DbType.AnsiStringFixedLength, EMPRESA, 3);
            using DbDataReader lector = comando.ExecuteReader();
            while (lector.Read())
            {
                string producto = Convert.ToString(lector[0])!.Trim();
                if (producto.Length == 0 || datos.Fichas.ContainsKey(producto)) continue;
                datos.Fichas[producto] = new FichaProducto
                {
                    Producto = producto,
                    Nombre = lector.IsDBNull(1) ? null : Convert.ToString(lector[1])!.Trim(),
                    Grupo = lector.IsDBNull(2) ? null : Convert.ToString(lector[2])!.Trim(),
                    SubGrupo = lector.IsDBNull(3) ? null : Convert.ToString(lector[3])!.Trim(),
                    Familia = lector.IsDBNull(4) ? null : Convert.ToString(lector[4])!.Trim(),
                    Estado = Convert.ToInt16(lector[5]),
                    Ficticio = Convert.ToBoolean(lector[6])
                };
            }
        }
        log?.Invoke($"  Fichas: {datos.Fichas.Count} productos.");
        return datos;
    }

    private DbCommand Comando(DbConnection conexion, string sql)
    {
        DbCommand comando = conexion.CreateCommand();
        comando.CommandText = sql;
        comando.CommandTimeout = _tiempoComando;
        return comando;
    }

    private static void Parametro(DbCommand comando, string nombre, DbType tipo, object valor, int tamanno = 0)
    {
        DbParameter parametro = comando.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.DbType = tipo;
        if (tamanno > 0) parametro.Size = tamanno;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }
}
