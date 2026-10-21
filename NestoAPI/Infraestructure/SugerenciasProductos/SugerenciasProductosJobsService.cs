using Hangfire;
using ModeloSugerenciasProductos.Reglas;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.SugerenciasProductos
{
    /// <summary>NestoAPI#670: lo que ha hecho una pasada del job (para los tests y el log).</summary>
    public class ResultadoJobSugerenciasProductos
    {
        /// <summary>Por qué no ha calculado nada (interruptor, faltan tablas). Null si ha calculado.</summary>
        public string MotivoNoCalcula { get; set; }
        public int Compras { get; set; }
        public int Pares { get; set; }
        public int PerfilesProductos { get; set; }
        /// <summary>NestoAPI#671: filas de afinidad (0 si no está el script Issue671a).</summary>
        public int Afinidades { get; set; }
        /// <summary>NestoAPI#671: productos con popularidad (0 si no está el script Issue671a).</summary>
        public int Popularidad { get; set; }
        /// <summary>NestoAPI#683: filas de la popularidad por estado (0 si no está el script Issue683).</summary>
        public int PopularidadEstado { get; set; }
    }

    public interface IRepositorioJobSugerenciasProductos
    {
        /// <summary>Si existen SugerenciasProductosPares y SugerenciasProductosPerfilesProductos.</summary>
        bool ExistenTablas();
        /// <summary>Todas las compras (cliente, producto, día) de la empresa en el periodo.</summary>
        List<CompraDiaReposicion> LeerCompras(string empresa, DateTime desde, DateTime hasta);
        Dictionary<string, FichaProductoSugerencia> LeerFichas(string empresa);
        /// <summary>Vacía las dos tablas y las rellena, todo o nada.</summary>
        void Guardar(List<ParCompradoJunto> pares, List<ProductoPerfil> perfiles, DateTime fechaCalculo);
        /// <summary>NestoAPI#671: si existen SugerenciasProductosAfinidad y SugerenciasProductosPopularidad (script Issue671a).</summary>
        bool ExistenTablasModelo();
        /// <summary>NestoAPI#671: vacía las dos tablas y las rellena, todo o nada.</summary>
        void GuardarModelo(List<AfinidadCliente> afinidades, List<PopularidadProducto> popularidad, DateTime fechaCalculo);
        /// <summary>NestoAPI#683: si existe SugerenciasProductosPopularidadEstado (script Issue683).</summary>
        bool ExistenTablasEstado();
        /// <summary>NestoAPI#683: cliente → Clientes.Estado de hoy del contacto principal (el de menor código si hay dos).</summary>
        Dictionary<string, short?> LeerEstadosClientes(string empresa);
        /// <summary>NestoAPI#683: vacía la tabla y la rellena, todo o nada.</summary>
        void GuardarPopularidadEstado(List<PopularidadPorEstado> filas, DateTime fechaCalculo);
    }

    /// <summary>
    /// NestoAPI#670: job nocturno «sugerencias-productos-pares». Lee 24 meses de albaranes (cliente, producto, día) con la
    /// consulta de #532 (<see cref="SelectorRecordatoriosReposicion.SQL_COMPRAS"/>: NOLOCK y un único recorrido del índice que
    /// la cubre entera, ~3 s y ~41.000 lecturas lógicas medidos el 25/09/26), calcula en memoria los pares «comprados juntos» y
    /// los perfiles (<see cref="CalculadoraParesCompradosJuntos"/>) y deja las dos tablas rellenas en una transacción.
    /// NestoAPI#671 (corte 2a): en la misma pasada, con las mismas compras, la afinidad item-kNN de cada cliente y la
    /// popularidad de cada producto (<see cref="CalculadoraAfinidadProductos"/>), en otra transacción y solo si están las
    /// tablas del script Issue671a.
    /// NestoAPI#683: también la popularidad de cada producto y de cada familia por grupo de estado del cliente (con el estado de
    /// hoy), en otra transacción y solo si está la tabla del script Issue683. Es lo que necesita Calcular para usar un modelo de
    /// la versión 2 de las features: mientras no esté, sigue con el último de la versión 1.
    /// <para>De lunes a sábado a las 05:00: después de los jobs de la 1:00 a las 4:30 y nunca el domingo de madrugada (el SP de
    /// precios medios bloquea LinPedidoVta). En la cola «indicadores» (servidor de un hilo de los jobs largos) y sin reintentos:
    /// un reintento caería en horario. Interruptor: <see cref="Constantes.ParametrosUsuario.SUGERENCIAS_PRODUCTOS_JOB"/>.</para>
    /// </summary>
    public class SugerenciasProductosJobsService
    {
        public const string ID_JOB = "sugerencias-productos-pares";
        /// <summary>De lunes a sábado a las 05:00.</summary>
        public const string CRON = "0 5 * * 1-6";
        public const string COLA = IndicadoresStock.IndicadoresStockJobsService.COLA;
        internal const string VALOR_ACTIVO = "Activo";
        internal const string MOTIVO_APAGADO = "Interruptor SugerenciasProductosJob apagado.";
        internal const string MOTIVO_SIN_TABLAS = "Faltan las tablas del script Issue670_SugerenciasProductos.sql.";
        internal const string MOTIVO_SIN_COMPRAS = "No se ha leído ninguna compra: no se pisa lo que había.";

        private readonly IRepositorioJobSugerenciasProductos repositorio;
        private readonly Func<string> leerInterruptor;
        private readonly Func<DateTime> ahora;

        public SugerenciasProductosJobsService(IRepositorioJobSugerenciasProductos repositorio, Func<string> leerInterruptor, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio;
            this.leerInterruptor = leerInterruptor;
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Punto de entrada del job recurrente.</summary>
        [Queue(COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static Task CalcularProgramado()
        {
            try
            {
                var lector = new LectorParametrosUsuario();
                ResultadoJobSugerenciasProductos resultado = new SugerenciasProductosJobsService(new RepositorioJobSugerenciasProductosSql(),
                    () => lector.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                        Constantes.ParametrosUsuario.SUGERENCIAS_PRODUCTOS_JOB)).Ejecutar();
                Console.WriteLine(resultado.MotivoNoCalcula != null
                    ? $"🛒 [Hangfire] Sugerencias de productos: no se calcula ({resultado.MotivoNoCalcula})"
                    : $"🛒 [Hangfire] Sugerencias de productos: {resultado.Compras} compras, {resultado.Pares} pares, {resultado.PerfilesProductos} filas de perfiles, " +
                      $"{resultado.Afinidades} afinidades, {resultado.Popularidad} productos con popularidad, {resultado.PopularidadEstado} filas de popularidad por estado");
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Sugerencias #670] Error en el job nocturno de pares y perfiles: {ex.Message}", ex));
                throw;
            }
            return Task.CompletedTask;
        }

        public ResultadoJobSugerenciasProductos Ejecutar()
        {
            var resultado = new ResultadoJobSugerenciasProductos();
            string interruptor;
            try
            {
                interruptor = leerInterruptor?.Invoke();
            }
            catch
            {
                interruptor = null;
            }
            if (!string.Equals(interruptor?.Trim(), VALOR_ACTIVO, StringComparison.OrdinalIgnoreCase))
            {
                resultado.MotivoNoCalcula = MOTIVO_APAGADO;
                return resultado;
            }
            if (!repositorio.ExistenTablas())
            {
                resultado.MotivoNoCalcula = MOTIVO_SIN_TABLAS;
                return resultado;
            }
            DateTime momento = ahora();
            DateTime hoy = momento.Date;
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            List<CompraDiaReposicion> compras = repositorio.LeerCompras(empresa, hoy.AddMonths(-MotorSugerenciasProductos.MESES_HISTORIAL), hoy)
                ?? new List<CompraDiaReposicion>();
            resultado.Compras = compras.Count;
            if (compras.Count == 0)
            {
                resultado.MotivoNoCalcula = MOTIVO_SIN_COMPRAS;
                return resultado;
            }
            Dictionary<string, FichaProductoSugerencia> fichas = new Dictionary<string, FichaProductoSugerencia>(
                repositorio.LeerFichas(empresa) ?? new Dictionary<string, FichaProductoSugerencia>(), StringComparer.OrdinalIgnoreCase);
            List<ParCompradoJunto> pares = CalculadoraParesCompradosJuntos.Pares(compras, fichas);
            List<ProductoPerfil> perfiles = CalculadoraParesCompradosJuntos.PerfilesProductos(compras, fichas, hoy);
            repositorio.Guardar(pares, perfiles, momento);
            resultado.Pares = pares.Count;
            resultado.PerfilesProductos = perfiles.Count;

            if (repositorio.ExistenTablasModelo())
            {
                List<AfinidadCliente> afinidades = CalculadoraAfinidadProductos.Afinidades(compras, fichas);
                List<PopularidadProducto> popularidad = CalculadoraAfinidadProductos.Popularidad(compras, fichas, hoy);
                repositorio.GuardarModelo(afinidades, popularidad, momento);
                resultado.Afinidades = afinidades.Count;
                resultado.Popularidad = popularidad.Count;
            }

            if (repositorio.ExistenTablasEstado())
            {
                Dictionary<string, short?> estados = repositorio.LeerEstadosClientes(empresa) ?? new Dictionary<string, short?>();
                List<PopularidadPorEstado> porEstado = CalculadoraAfinidadProductos.PopularidadEstado(compras, fichas, estados, hoy);
                repositorio.GuardarPopularidadEstado(porEstado, momento);
                resultado.PopularidadEstado = porEstado.Count;
            }
            return resultado;
        }
    }

    public class RepositorioJobSugerenciasProductosSql : IRepositorioJobSugerenciasProductos
    {
        internal const int TIMEOUT_SEGUNDOS = 900;

        internal const string SQL_EXISTEN_TABLAS = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosPares', N'U') IS NULL
                   OR OBJECT_ID(N'dbo.SugerenciasProductosPerfilesProductos', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_EXISTE_TABLA_ESTADO = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosPopularidadEstado', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        /// <summary>NestoAPI#683: el estado de hoy del contacto principal de cada cliente (la misma que el entrenamiento).</summary>
        internal const string SQL_ESTADOS_CLIENTES = @"
SELECT RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, c.Estado
FROM Clientes c WITH (NOLOCK)
WHERE c.Empresa = @empresa AND c.ClientePrincipal = 1";

        internal const string SQL_EXISTEN_TABLAS_MODELO = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.SugerenciasProductosAfinidad', N'U') IS NULL
                   OR OBJECT_ID(N'dbo.SugerenciasProductosPopularidad', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        private static NVEntities Contexto()
        {
            var db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
            db.Database.CommandTimeout = TIMEOUT_SEGUNDOS;
            return db;
        }

        public bool ExistenTablas()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<bool>(SQL_EXISTEN_TABLAS).First();
            }
        }

        public bool ExistenTablasModelo()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<bool>(SQL_EXISTEN_TABLAS_MODELO).First();
            }
        }

        public bool ExistenTablasEstado()
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<bool>(SQL_EXISTE_TABLA_ESTADO).First();
            }
        }

        public Dictionary<string, short?> LeerEstadosClientes(string empresa)
        {
            using (NVEntities db = Contexto())
            {
                return EstadosPorCliente(db.Database.SqlQuery<EstadoClienteFila>(SQL_ESTADOS_CLIENTES, new SqlParameter("@empresa", empresa)).ToList());
            }
        }

        /// <summary>Un estado por cliente: el del contacto principal de menor código (como RepositorioDatosSql del núcleo).</summary>
        internal static Dictionary<string, short?> EstadosPorCliente(IEnumerable<EstadoClienteFila> filas)
            => (filas ?? Enumerable.Empty<EstadoClienteFila>())
                .Where(f => !string.IsNullOrWhiteSpace(f.Cliente))
                .GroupBy(f => f.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Contacto?.Trim() ?? string.Empty, StringComparer.Ordinal).First().Estado,
                    StringComparer.OrdinalIgnoreCase);

        public void GuardarPopularidadEstado(List<PopularidadPorEstado> filas, DateTime fechaCalculo)
        {
            DataTable tabla = new DataTable();
            _ = tabla.Columns.Add("Estado", typeof(string));
            _ = tabla.Columns.Add("Tipo", typeof(string));
            _ = tabla.Columns.Add("Clave", typeof(string));
            _ = tabla.Columns.Add("Clientes", typeof(int));
            _ = tabla.Columns.Add("FechaCalculo", typeof(DateTime));
            foreach (PopularidadPorEstado f in filas ?? new List<PopularidadPorEstado>())
            {
                _ = tabla.Rows.Add(f.Estado, f.Tipo, f.Clave ?? string.Empty, f.Clientes, fechaCalculo);
            }
            Reemplazar(Tuple.Create("dbo.SugerenciasProductosPopularidadEstado", tabla));
        }

        public List<CompraDiaReposicion> LeerCompras(string empresa, DateTime desde, DateTime hasta)
        {
            using (NVEntities db = Contexto())
            {
                return db.Database.SqlQuery<CompraDiaReposicion>(SelectorRecordatoriosReposicion.SQL_COMPRAS,
                    new SqlParameter("@empresa", empresa), new SqlParameter("@desde", desde), new SqlParameter("@hasta", hasta)).ToList();
            }
        }

        public Dictionary<string, FichaProductoSugerencia> LeerFichas(string empresa)
        {
            using (NVEntities db = Contexto())
            {
                return db.Productos
                    .Where(p => p.Empresa == empresa)
                    .Select(p => new { p.Número, p.Nombre, p.Grupo, p.SubGrupo, p.Familia, p.Estado, p.Ficticio })
                    .ToList()
                    .GroupBy(p => p.Número.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g =>
                    {
                        var f = g.First();
                        return new FichaProductoSugerencia
                        {
                            Producto = g.Key,
                            Nombre = f.Nombre?.Trim(),
                            Grupo = f.Grupo?.Trim(),
                            SubGrupo = f.SubGrupo?.Trim(),
                            Familia = f.Familia?.Trim(),
                            Estado = f.Estado ?? 0,
                            Ficticio = f.Ficticio
                        };
                    }, StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Guardar(List<ParCompradoJunto> pares, List<ProductoPerfil> perfiles, DateTime fechaCalculo)
        {
            DataTable tablaPares = new DataTable();
            _ = tablaPares.Columns.Add("ProductoA", typeof(string));
            _ = tablaPares.Columns.Add("ProductoB", typeof(string));
            _ = tablaPares.Columns.Add("VecesJuntos", typeof(int));
            _ = tablaPares.Columns.Add("Soporte", typeof(decimal));
            _ = tablaPares.Columns.Add("Confianza", typeof(decimal));
            _ = tablaPares.Columns.Add("Lift", typeof(decimal));
            _ = tablaPares.Columns.Add("FechaCalculo", typeof(DateTime));
            foreach (ParCompradoJunto p in pares ?? new List<ParCompradoJunto>())
            {
                _ = tablaPares.Rows.Add(p.ProductoA, p.ProductoB, p.VecesJuntos, p.Soporte, p.Confianza, Math.Min(p.Lift, 99999999m), fechaCalculo);
            }
            DataTable tablaPerfiles = new DataTable();
            _ = tablaPerfiles.Columns.Add("Grupo", typeof(string));
            _ = tablaPerfiles.Columns.Add("SubGrupo", typeof(string));
            _ = tablaPerfiles.Columns.Add("Producto", typeof(string));
            _ = tablaPerfiles.Columns.Add("Clientes", typeof(int));
            _ = tablaPerfiles.Columns.Add("FechaCalculo", typeof(DateTime));
            foreach (ProductoPerfil p in perfiles ?? new List<ProductoPerfil>())
            {
                _ = tablaPerfiles.Rows.Add(p.Grupo, p.SubGrupo ?? string.Empty, p.Producto, p.Clientes, fechaCalculo);
            }

            Reemplazar(Tuple.Create("dbo.SugerenciasProductosPares", tablaPares),
                Tuple.Create("dbo.SugerenciasProductosPerfilesProductos", tablaPerfiles));
        }

        public void GuardarModelo(List<AfinidadCliente> afinidades, List<PopularidadProducto> popularidad, DateTime fechaCalculo)
        {
            DataTable tablaAfinidad = new DataTable();
            _ = tablaAfinidad.Columns.Add("Cliente", typeof(string));
            _ = tablaAfinidad.Columns.Add("Producto", typeof(string));
            _ = tablaAfinidad.Columns.Add("Afinidad", typeof(decimal));
            _ = tablaAfinidad.Columns.Add("Rango", typeof(short));
            _ = tablaAfinidad.Columns.Add("FechaCalculo", typeof(DateTime));
            foreach (AfinidadCliente a in afinidades ?? new List<AfinidadCliente>())
            {
                _ = tablaAfinidad.Rows.Add(a.Cliente, a.Producto, Math.Min(a.Afinidad, 999999m), a.Rango, fechaCalculo);
            }
            DataTable tablaPopularidad = new DataTable();
            _ = tablaPopularidad.Columns.Add("Producto", typeof(string));
            _ = tablaPopularidad.Columns.Add("Clientes12Meses", typeof(int));
            _ = tablaPopularidad.Columns.Add("Clientes3Meses", typeof(int));
            _ = tablaPopularidad.Columns.Add("Cestas12Meses", typeof(int));
            _ = tablaPopularidad.Columns.Add("Rango", typeof(int));
            _ = tablaPopularidad.Columns.Add("FechaCalculo", typeof(DateTime));
            foreach (PopularidadProducto p in popularidad ?? new List<PopularidadProducto>())
            {
                _ = tablaPopularidad.Rows.Add(p.Producto, p.Clientes12Meses, p.Clientes3Meses, p.Cestas12Meses, p.Rango, fechaCalculo);
            }
            Reemplazar(Tuple.Create("dbo.SugerenciasProductosAfinidad", tablaAfinidad),
                Tuple.Create("dbo.SugerenciasProductosPopularidad", tablaPopularidad));
        }

        /// <summary>Vacía las tablas y las rellena con lo nuevo, todo en una transacción.</summary>
        private static void Reemplazar(params Tuple<string, DataTable>[] tablas)
        {
            using (NVEntities db = Contexto())
            {
                SqlConnection conexion = (SqlConnection)db.Database.Connection;
                bool abiertaAqui = conexion.State != ConnectionState.Open;
                if (abiertaAqui)
                {
                    conexion.Open();
                }
                try
                {
                    using (SqlTransaction transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted))
                    {
                        foreach (Tuple<string, DataTable> tabla in tablas)
                        {
                            using (SqlCommand borrar = new SqlCommand("DELETE FROM " + tabla.Item1, conexion, transaccion) { CommandTimeout = TIMEOUT_SEGUNDOS })
                            {
                                _ = borrar.ExecuteNonQuery();
                            }
                        }
                        foreach (Tuple<string, DataTable> tabla in tablas)
                        {
                            Copiar(conexion, transaccion, tabla.Item1, tabla.Item2);
                        }
                        transaccion.Commit();
                    }
                }
                finally
                {
                    if (abiertaAqui)
                    {
                        conexion.Close();
                    }
                }
            }
        }

        private static void Copiar(SqlConnection conexion, SqlTransaction transaccion, string destino, DataTable tabla)
        {
            using (SqlBulkCopy copia = new SqlBulkCopy(conexion, SqlBulkCopyOptions.CheckConstraints, transaccion)
            { DestinationTableName = destino, BulkCopyTimeout = TIMEOUT_SEGUNDOS, BatchSize = 5000 })
            {
                foreach (DataColumn columna in tabla.Columns)
                {
                    copia.ColumnMappings.Add(columna.ColumnName, columna.ColumnName);
                }
                copia.WriteToServer(tabla);
            }
        }
    }

    /// <summary>NestoAPI#683: una fila de <see cref="RepositorioJobSugerenciasProductosSql.SQL_ESTADOS_CLIENTES"/>.</summary>
    public class EstadoClienteFila
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public short? Estado { get; set; }
    }
}
