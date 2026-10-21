using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#688: una nota de entrega viva sin fecha y sin avisar (candidata al aviso «lo pendiente ya está completo»).</summary>
    public class NotaPendienteSinAvisar
    {
        public string Empresa { get; set; }
        public int Nota { get; set; }
        public int? PedidoOrigen { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
    }

    /// <summary>NestoAPI#688: una línea pendiente de una nota (producto, almacén y cantidad).</summary>
    public class LineaNotaPendiente
    {
        public int Nota { get; set; }
        public string Producto { get; set; }
        public string Almacen { get; set; }
        public int Cantidad { get; set; }
    }

    /// <summary>NestoAPI#688: las columnas CabPedidoVta.ServirPendienteAlHaberStock y AvisoPendienteCompleto.</summary>
    public interface IRepositorioServirPendienteAlHaberStock
    {
        /// <summary>¿Existen las columnas (Scripts/Issue688_ServirPendienteAlHaberStock.sql lanzado)?</summary>
        bool ColumnasExisten();
        /// <summary>La casilla guardada; null si está a NULL, el pedido no existe o la columna aún no existe. Nunca lanza.</summary>
        bool? Leer(string empresa, int numero);
        /// <summary>Guarda la casilla. False si no se ha podido (columna inexistente o fallo, que se registra). Nunca lanza.</summary>
        bool Guardar(string empresa, int numero, bool valor);
        /// <summary>Notas de entrega con líneas vivas sin fecha (31/12/2099), sin picking y sin avisar. Vacía sin columnas.</summary>
        List<NotaPendienteSinAvisar> LeerNotasSinAvisar();
        /// <summary>Las líneas vivas de esas notas.</summary>
        List<LineaNotaPendiente> LeerLineas(string empresa, IEnumerable<int> notas);
        /// <summary>Marca la nota como avisada (solo si seguía sin avisar). True si la ha marcado ESTA llamada.</summary>
        bool MarcarAvisada(string empresa, int nota, DateTime cuando);
    }

    /// <summary>
    /// NestoAPI#688: las columnas NO están en el EDMX a propósito (como FechaEntregaAgenciaPrometida, #606): si estuvieran y el
    /// script no se hubiera lanzado, EF las pediría en TODAS las lecturas de CabPedidoVta y tumbaría la API. Se leen y se
    /// escriben con SQL parametrizado y, antes, se mira si existen (COL_LENGTH, cacheado: una vez que existen ya no se
    /// vuelve a preguntar; mientras no, se pregunta como mucho cada 5 minutos). Sin columnas: leer da null, guardar no hace
    /// nada y el job no encuentra notas. Cada operación abre su propio contexto (no se mezcla con el SaveChanges de quien
    /// llama).
    /// </summary>
    public class RepositorioServirPendienteAlHaberStock : IRepositorioServirPendienteAlHaberStock
    {
        internal const string SQL_EXISTEN =
            "SELECT CAST(CASE WHEN COL_LENGTH('dbo.CabPedidoVta', 'ServirPendienteAlHaberStock') IS NOT NULL " +
            "AND COL_LENGTH('dbo.CabPedidoVta', 'AvisoPendienteCompleto') IS NOT NULL THEN 1 ELSE 0 END AS bit)";

        internal const string SQL_LEER =
            "SELECT ServirPendienteAlHaberStock FROM dbo.CabPedidoVta WITH (NOLOCK) WHERE Empresa = @empresa AND Número = @numero";

        internal const string SQL_GUARDAR =
            "UPDATE dbo.CabPedidoVta SET ServirPendienteAlHaberStock = @valor WHERE Empresa = @empresa AND Número = @numero";

        // Notas vivas (alguna línea pendiente o en curso con la fecha «sin determinar» y sin picking) sin avisar y sin la
        // casilla marcada (las marcadas nacen con fecha; si alguien la quita a mano, tampoco se avisa: ya sale sola).
        internal const string SQL_NOTAS = @"SELECT c.Empresa, c.Número AS Nota, c.PedidoOrigen, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto
FROM dbo.CabPedidoVta c WITH (NOLOCK)
WHERE c.NotaEntrega = 1 AND c.PedidoOrigen IS NOT NULL AND c.AvisoPendienteCompleto IS NULL
  AND ISNULL(c.ServirPendienteAlHaberStock, 0) = 0
  AND EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK)
              WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1
                AND l.[Fecha Entrega] = @sinFecha)
  AND NOT EXISTS (SELECT 1 FROM dbo.LinPedidoVta l WITH (NOLOCK)
              WHERE l.Empresa = c.Empresa AND l.Número = c.Número AND l.Estado BETWEEN -1 AND 1
                AND ISNULL(l.Picking, 0) <> 0)";

        internal const string SQL_MARCAR =
            "UPDATE dbo.CabPedidoVta SET AvisoPendienteCompleto = @cuando WHERE Empresa = @empresa AND Número = @numero AND AvisoPendienteCompleto IS NULL";

        private static readonly TimeSpan REINTENTO_SIN_COLUMNAS = TimeSpan.FromMinutes(5);
        private static readonly object cerrojo = new object();
        private static bool existen;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly Func<NVEntities> crearContexto;
        private readonly Action<Exception> registrar;

        public RepositorioServirPendienteAlHaberStock() : this(null, null)
        {
        }

        internal RepositorioServirPendienteAlHaberStock(Func<NVEntities> crearContexto, Action<Exception> registrar)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>Para los tests: olvida lo que se sabía de las columnas.</summary>
        internal static void OlvidarComprobacion()
        {
            lock (cerrojo)
            {
                existen = false;
                ultimaComprobacion = DateTime.MinValue;
            }
        }

        public bool ColumnasExisten()
        {
            lock (cerrojo)
            {
                if (existen || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_COLUMNAS)
                {
                    return existen;
                }
            }
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(SQL_EXISTEN).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                ahora = false;
            }
            lock (cerrojo)
            {
                existen = ahora;
                ultimaComprobacion = DateTime.Now;
                return existen;
            }
        }

        public bool? Leer(string empresa, int numero)
        {
            if (!ColumnasExisten())
            {
                return null;
            }
            try
            {
                using (NVEntities db = crearContexto())
                {
                    return db.Database.SqlQuery<bool?>(SQL_LEER, ParametroEmpresa(empresa), ParametroNumero(numero)).FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Registrar($"no se pudo leer la casilla del pedido {numero}", ex);
                return null;
            }
        }

        public bool Guardar(string empresa, int numero, bool valor)
        {
            if (!ColumnasExisten())
            {
                return false;
            }
            try
            {
                using (NVEntities db = crearContexto())
                {
                    return db.Database.ExecuteSqlCommand(SQL_GUARDAR,
                        new SqlParameter("@valor", SqlDbType.Bit) { Value = valor },
                        ParametroEmpresa(empresa), ParametroNumero(numero)) > 0;
                }
            }
            catch (Exception ex)
            {
                Registrar($"no se pudo guardar la casilla del pedido {numero}", ex);
                return false;
            }
        }

        public List<NotaPendienteSinAvisar> LeerNotasSinAvisar()
        {
            if (!ColumnasExisten())
            {
                return new List<NotaPendienteSinAvisar>();
            }
            using (NVEntities db = crearContexto())
            {
                return db.Database.SqlQuery<NotaPendienteSinAvisar>(SQL_NOTAS,
                    new SqlParameter("@sinFecha", SqlDbType.DateTime) { Value = NotasEntrega.CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR })
                    .ToList()
                    .Select(n => { n.Empresa = n.Empresa?.Trim(); return n; })
                    .ToList();
            }
        }

        public List<LineaNotaPendiente> LeerLineas(string empresa, IEnumerable<int> notas)
        {
            List<int> lista = (notas ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (!lista.Any())
            {
                return new List<LineaNotaPendiente>();
            }
            using (NVEntities db = crearContexto())
            {
                return db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa && lista.Contains(l.Número)
                        && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO
                        && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    .Select(l => new { l.Número, l.Producto, l.Almacén, l.Cantidad })
                    .ToList()
                    .Select(l => new LineaNotaPendiente
                    {
                        Nota = l.Número,
                        Producto = l.Producto?.Trim(),
                        Almacen = l.Almacén?.Trim(),
                        Cantidad = l.Cantidad ?? 0
                    })
                    .ToList();
            }
        }

        public bool MarcarAvisada(string empresa, int nota, DateTime cuando)
        {
            if (!ColumnasExisten())
            {
                return false;
            }
            using (NVEntities db = crearContexto())
            {
                return db.Database.ExecuteSqlCommand(SQL_MARCAR,
                    new SqlParameter("@cuando", SqlDbType.DateTime) { Value = cuando },
                    ParametroEmpresa(empresa), ParametroNumero(nota)) > 0;
            }
        }

        private void Registrar(string que, Exception ex)
        {
            try
            {
                registrar(new Exception($"[Servir lo pendiente #688] {que}: {ex.Message}", ex));
            }
            catch
            {
                // nunca romper a quien llama
            }
        }

        // VarChar e Int, no NVarChar: Empresa es char y un parámetro Unicode obligaría a convertir la columna.
        private static SqlParameter ParametroEmpresa(string empresa) =>
            new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO };

        private static SqlParameter ParametroNumero(int numero) =>
            new SqlParameter("@numero", SqlDbType.Int) { Value = numero };
    }
}
