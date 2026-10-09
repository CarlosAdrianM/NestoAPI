using System.Data;
using System.Data.Common;

namespace ModeloLlamadaPedido.Datos;

public sealed class DatosCrudos
{
    public List<RapportCrudo> Rapports { get; } = new();
    public List<PedidoDia> Pedidos { get; } = new();
    public List<VentaGrupoMes> Ventas { get; } = new();
}

/// <summary>
/// Lectura de la BD de producción, SOLO LECTURA y con NOLOCK (y READ UNCOMMITTED). Trae datos crudos (rapports, pedidos por
/// día y ventas por grupo/mes) de los clientes con algún rapport en la ventana; las features se calculan en C#
/// (<see cref="Features.CalculadoraFeatures"/>) para que sean deterministas, testeables y replicables en la API.
/// <para>NestoAPI#619: recibe la conexión (sin abrir) en vez de la cadena: la consola pone la suya y la API la de
/// NestoConnection, y el núcleo no depende de ningún SqlClient (netstandard2.0, System.Data.Common).</para>
/// </summary>
public sealed class RepositorioDatosSql
{
    /// <summary>Media hora: con todos los vendedores, 3 años y 24 meses de historial, la consulta tarda.</summary>
    public const int TIEMPO_COMANDO_POR_DEFECTO = 1800;

    internal const string SQL = @"
        SET NOCOUNT ON;
        SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

        SELECT DISTINCT s.Número Cliente, s.Contacto
        INTO #Clientes
        FROM SeguimientoCliente s WITH (NOLOCK)
        WHERE s.Fecha >= @Desde AND s.Fecha < @Hasta AND s.Estado = 0 AND s.Tipo IN ('T', 'V', 'W')
            AND (@Vendedor IS NULL OR s.Vendedor = @Vendedor);
        CREATE CLUSTERED INDEX IX_Clientes ON #Clientes (Cliente, Contacto);

        -- 1. Rapports (con historial previo para la tasa de conversión)
        SELECT s.Número Cliente, s.Contacto, s.Fecha, s.Tipo, CAST(s.Pedido AS int) Pedido,
            ISNULL(NULLIF(RTRIM(s.Vendedor), ''), RTRIM(c.Vendedor)) Vendedor
        FROM SeguimientoCliente s WITH (NOLOCK)
        INNER JOIN #Clientes x ON x.Cliente = s.Número AND x.Contacto = s.Contacto
        LEFT JOIN Clientes c WITH (NOLOCK) ON c.Empresa = '1' AND c.[Nº Cliente] = s.Número AND c.Contacto = s.Contacto
        WHERE s.Fecha >= @DesdeHistorial AND s.Fecha < @Hasta AND s.Estado = 0 AND s.Tipo IN ('T', 'V', 'W');

        -- 2. Pedidos: un día distinto por cliente, con su importe
        SELECT cab.[Nº Cliente] Cliente, cab.Contacto, CAST(cab.Fecha AS date) Dia, SUM(l.[Base Imponible]) Importe
        FROM CabPedidoVta cab WITH (NOLOCK)
        INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = cab.Empresa AND l.Número = cab.Número
        INNER JOIN #Clientes x ON x.Cliente = cab.[Nº Cliente] AND x.Contacto = cab.Contacto
        WHERE cab.Empresa = '1' AND cab.Fecha >= @DesdeHistorial AND cab.Fecha < @HastaPedidos
            AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
        GROUP BY cab.[Nº Cliente], cab.Contacto, CAST(cab.Fecha AS date);

        -- 3. Cantidad por grupo+subgrupo y mes (para el subgrupo más vendido)
        SELECT cab.[Nº Cliente] Cliente, cab.Contacto, YEAR(cab.Fecha) * 100 + MONTH(cab.Fecha) AnnoMes,
            l.Grupo + l.SubGrupo GrupoSubgrupo, SUM(l.Cantidad) Cantidad
        FROM CabPedidoVta cab WITH (NOLOCK)
        INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = cab.Empresa AND l.Número = cab.Número
        INNER JOIN #Clientes x ON x.Cliente = cab.[Nº Cliente] AND x.Contacto = cab.Contacto
        WHERE cab.Empresa = '1' AND cab.Fecha >= @DesdeHistorial AND cab.Fecha < @Hasta
            AND l.TipoLinea = 1 AND l.[Base Imponible] > 0 AND cab.NotaEntrega = 0 AND l.Estado >= -1
            AND l.SubGrupo <> 'MMP'
        GROUP BY cab.[Nº Cliente], cab.Contacto, YEAR(cab.Fecha) * 100 + MONTH(cab.Fecha), l.Grupo + l.SubGrupo;";

    private readonly Func<DbConnection> _crearConexion;
    private readonly int _tiempoComando;

    /// <param name="crearConexion">Conexión nueva sin abrir (se cierra al terminar).</param>
    /// <param name="tiempoComandoSegundos">Tiempo de espera de la consulta.</param>
    public RepositorioDatosSql(Func<DbConnection> crearConexion, int tiempoComandoSegundos = TIEMPO_COMANDO_POR_DEFECTO)
    {
        _crearConexion = crearConexion ?? throw new ArgumentNullException(nameof(crearConexion));
        _tiempoComando = tiempoComandoSegundos;
    }

    /// <param name="desde">Inicio de la ventana de rapports del dataset.</param>
    /// <param name="hasta">Fin (excluido) de la ventana de rapports.</param>
    /// <param name="vendedor">Solo los clientes con rapports de ese vendedor en la ventana (null = todos).</param>
    /// <param name="mesesHistorial">Meses de historial previo que se cargan para las features.</param>
    /// <param name="diasPositivo">Días después de <paramref name="hasta"/> que se cargan pedidos para la etiqueta.</param>
    public DatosCrudos Leer(DateTime desde, DateTime hasta, string? vendedor, int mesesHistorial, int diasPositivo)
    {
        var datos = new DatosCrudos();
        using DbConnection conexion = _crearConexion();
        conexion.Open();
        using DbCommand comando = conexion.CreateCommand();
        comando.CommandText = SQL;
        comando.CommandTimeout = _tiempoComando;
        Parametro(comando, "@Desde", DbType.DateTime, desde);
        Parametro(comando, "@Hasta", DbType.DateTime, hasta);
        Parametro(comando, "@DesdeHistorial", DbType.DateTime, desde.AddMonths(-mesesHistorial));
        Parametro(comando, "@HastaPedidos", DbType.DateTime, hasta.AddDays(diasPositivo + 1));
        Parametro(comando, "@Vendedor", DbType.AnsiString, (object?)vendedor ?? DBNull.Value, 10);

        using DbDataReader lector = comando.ExecuteReader();
        while (lector.Read())
        {
            datos.Rapports.Add(new RapportCrudo(
                Claves.ClienteId(lector.GetString(0), lector.GetString(1)),
                lector.GetDateTime(2),
                Convert.ToString(lector[3])!.Trim(),
                Convert.ToInt32(lector[4]) != 0,
                lector.IsDBNull(5) ? "" : Convert.ToString(lector[5])!.Trim()));
        }
        lector.NextResult();
        while (lector.Read())
        {
            datos.Pedidos.Add(new PedidoDia(
                Claves.ClienteId(lector.GetString(0), lector.GetString(1)),
                lector.GetDateTime(2),
                Convert.ToDecimal(lector[3])));
        }
        lector.NextResult();
        while (lector.Read())
        {
            datos.Ventas.Add(new VentaGrupoMes(
                Claves.ClienteId(lector.GetString(0), lector.GetString(1)),
                Convert.ToInt32(lector[2]),
                Convert.ToString(lector[3])!.Trim(),
                Convert.ToDecimal(lector[4])));
        }
        return datos;
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
