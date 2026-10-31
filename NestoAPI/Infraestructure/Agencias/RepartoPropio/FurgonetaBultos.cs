using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#772: los tipos de movimiento de un bulto (los mismos que el CHECK de RepartoBultosMovimientos).</summary>
    public static class TiposMovimientoBulto
    {
        public const string CARGADO = "Cargado";
        public const string RECOGIDO = "Recogido";
        public const string ENTREGADO = "Entregado";
        public const string DESCARGADO = "Descargado";

        /// <summary>Los que dejan el bulto en una furgoneta.</summary>
        public static bool DejaEnFurgoneta(string tipo) => tipo == CARGADO || tipo == RECOGIDO;
    }

    /// <summary>NestoAPI#772: quién apunta el movimiento.</summary>
    public static class OrigenesMovimientoBulto
    {
        public const string REPARTIDOR = "Repartidor";
        /// <summary>La red de seguridad: el almacén lee el bulto (recepción de la REPO, abono de la NR).</summary>
        public const string ALMACEN = "Almacen";
    }

    /// <summary>NestoAPI#772: una fila de RepartoBultosMovimientos.</summary>
    public class MovimientoBultoReparto
    {
        public int Id { get; set; }
        public string Codigo { get; set; }
        public string TipoCodigo { get; set; }
        public int? EnvioId { get; set; }
        public int? NumTraspaso { get; set; }
        public int? Pedido { get; set; }
        public int Bulto { get; set; }
        public int? Total { get; set; }
        public string Tipo { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
        public DateTime FechaRegistro { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        public string Origen { get; set; }
        public string Descripcion { get; set; }
        public Guid ClaveIdempotencia { get; set; }
    }

    /// <summary>NestoAPI#772: una fila de RepartoJornadas.</summary>
    public class JornadaReparto
    {
        public int Id { get; set; }
        public string Usuario { get; set; }
        public DateTime Fecha { get; set; }
        public DateTime FechaFin { get; set; }
        public int BultosPendientes { get; set; }
        public string Pendientes { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>NestoAPI#772: un código leído ya entendido: qué etiqueta es y su forma normalizada.</summary>
    public class CodigoBultoFurgoneta
    {
        public const string RP = "RP";
        public const string NR = "NR";
        public const string R = "R";

        /// <summary>«RP251300-1», «NR929554-2», «R81010-3».</summary>
        public string Codigo { get; set; }
        public string TipoCodigo { get; set; }
        /// <summary>RP: el envío.</summary>
        public int? EnvioId { get; set; }
        /// <summary>NR: el pedido de la nota.</summary>
        public int? Pedido { get; set; }
        /// <summary>R: el traspaso.</summary>
        public int? Traspaso { get; set; }
        public int Bulto { get; set; }
        /// <summary>«NR929554» sin «-N» (el documento: bulto 1, o todos los de la nota al leerla en el almacén).</summary>
        public bool SinBulto { get; set; }

        public static string CodigoRp(int envio, int bulto) => string.Format(CultureInfo.InvariantCulture, "RP{0}-{1}", envio, bulto);

        /// <summary>
        /// RP{envío}-{N}, NR{pedido}-{N} (o NR{pedido}: bulto 1) y R{traspaso}-{N} (con los separadores raros y el prefijo AIM). Null si
        /// no es la etiqueta de un bulto (NE{pedido} tampoco: es un documento; se resuelve a su envío en Cargar y Entregar).
        /// </summary>
        public static CodigoBultoFurgoneta Leer(string codigo)
        {
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (CodigoBultoRepartoPropio.TryLeerBulto(limpio, out int envio, out int bultoRp))
            {
                return new CodigoBultoFurgoneta { Codigo = CodigoRp(envio, bultoRp), TipoCodigo = RP, EnvioId = envio, Bulto = bultoRp };
            }
            if (CodigoNotaEntrega.TryLeer(limpio, out string prefijo, out int pedido, out int? bultoNota))
            {
                if (prefijo != CodigoNotaEntrega.PREFIJO_RECOGIDA)
                {
                    return null;
                }
                int n = bultoNota ?? 1;
                return new CodigoBultoFurgoneta
                {
                    Codigo = CodigoNotaEntrega.BultoRecogida(pedido, n), TipoCodigo = NR, Pedido = pedido, Bulto = n, SinBulto = !bultoNota.HasValue
                };
            }
            if (CodigosBultoReposicion.Leer(limpio, out int traspaso, out int bultoR))
            {
                return new CodigoBultoFurgoneta { Codigo = CodigosBultoReposicion.Codigo(traspaso, bultoR), TipoCodigo = R, Traspaso = traspaso, Bulto = bultoR };
            }
            return null;
        }
    }

    /// <summary>
    /// NestoAPI#772: las reglas de la furgoneta, sin BD. «Lo que lleva encima» un repartidor = los bultos cuyo ÚLTIMO movimiento
    /// (por FechaHora del aparato y, a igual hora, por Id) es Cargado o Recogido suyo. Por la hora del aparato y no por el orden de
    /// llegada: una recogida hecha sin cobertura que sube después de que el almacén haya leído el bulto no lo vuelve a meter.
    /// </summary>
    public static class NucleoFurgoneta
    {
        /// <summary>El último movimiento de cada bulto (por código).</summary>
        public static List<MovimientoBultoReparto> Ultimos(IEnumerable<MovimientoBultoReparto> movimientos)
            => (movimientos ?? Enumerable.Empty<MovimientoBultoReparto>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Codigo))
                .GroupBy(m => m.Codigo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(m => m.FechaHora).ThenBy(m => m.Id).Last())
                .ToList();

        /// <summary>El último movimiento de ese bulto, o null si no tiene.</summary>
        public static MovimientoBultoReparto Ultimo(IEnumerable<MovimientoBultoReparto> movimientos, string codigo)
            => Ultimos((movimientos ?? Enumerable.Empty<MovimientoBultoReparto>())
                .Where(m => string.Equals(m?.Codigo?.Trim(), codigo?.Trim(), StringComparison.OrdinalIgnoreCase))).FirstOrDefault();

        /// <summary>Los bultos en alguna furgoneta; con <paramref name="usuario"/>, solo en la suya. Por FechaHora.</summary>
        public static List<MovimientoBultoReparto> EnFurgoneta(IEnumerable<MovimientoBultoReparto> movimientos, string usuario = null)
            => Ultimos(movimientos)
                .Where(m => TiposMovimientoBulto.DejaEnFurgoneta(m.Tipo))
                .Where(m => usuario == null || ServicioRepartoPropio.MismoUsuario(m.Usuario, usuario))
                .OrderBy(m => m.FechaHora).ThenBy(m => m.Id)
                .ToList();

        /// <summary>¿Está en la furgoneta de <paramref name="usuario"/>?</summary>
        public static bool LoLleva(MovimientoBultoReparto ultimo, string usuario)
            => ultimo != null && TiposMovimientoBulto.DejaEnFurgoneta(ultimo.Tipo) && ServicioRepartoPropio.MismoUsuario(ultimo.Usuario, usuario);

        /// <summary>
        /// La clave de un movimiento que se deriva de otra petición (los bultos de una entrega, lo leído al recibir): siempre la misma
        /// para la misma petición y el mismo bulto, así un reintento no lo apunta dos veces.
        /// </summary>
        public static Guid ClaveDerivada(Guid clave, string codigo, string tipo)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] datos = clave.ToByteArray()
                    .Concat(Encoding.UTF8.GetBytes(((codigo ?? string.Empty).Trim().ToUpperInvariant()) + "|" + (tipo ?? string.Empty)))
                    .ToArray();
                return new Guid(md5.ComputeHash(datos));
            }
        }

        public static BultoFurgonetaDTO ADto(MovimientoBultoReparto m)
            => new BultoFurgonetaDTO
            {
                Codigo = m.Codigo?.Trim(),
                TipoCodigo = m.TipoCodigo?.Trim(),
                Tipo = m.Tipo?.Trim(),
                Usuario = ServicioRepartoPropio.NombreRepartidor(m.Usuario),
                EnvioId = m.EnvioId,
                Pedido = m.Pedido,
                Traspaso = m.NumTraspaso,
                Bulto = m.Bulto,
                Total = m.Total,
                Descripcion = m.Descripcion,
                FechaHora = m.FechaHora
            };

        /// <summary>«RP251300-1, R81010-2 y 3 más» para los avisos.</summary>
        public static string ListaCorta(IEnumerable<string> codigos, int maximo = 10)
        {
            List<string> lista = (codigos ?? Enumerable.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (lista.Count <= maximo)
            {
                return string.Join(", ", lista);
            }
            return string.Join(", ", lista.Take(maximo)) + $" y {lista.Count - maximo} más";
        }
    }

    /// <summary>NestoAPI#772: qué movimientos leer (cualquiera de las condiciones: OR). Vacía: ninguno.</summary>
    public class ConsultaMovimientosBulto
    {
        public IReadOnlyCollection<string> Codigos { get; set; }
        public IReadOnlyCollection<int> Envios { get; set; }
        public IReadOnlyCollection<int> Traspasos { get; set; }
        public IReadOnlyCollection<int> Pedidos { get; set; }

        public bool Vacia => (Codigos == null || Codigos.Count == 0) && (Envios == null || Envios.Count == 0)
            && (Traspasos == null || Traspasos.Count == 0) && (Pedidos == null || Pedidos.Count == 0);

        public bool Cumple(MovimientoBultoReparto m)
            => (Codigos != null && Codigos.Any(c => string.Equals(c?.Trim(), m.Codigo?.Trim(), StringComparison.OrdinalIgnoreCase)))
               || (Envios != null && m.EnvioId.HasValue && Envios.Contains(m.EnvioId.Value))
               || (Traspasos != null && m.NumTraspaso.HasValue && Traspasos.Contains(m.NumTraspaso.Value))
               || (Pedidos != null && m.Pedido.HasValue && Pedidos.Contains(m.Pedido.Value));
    }

    /// <summary>NestoAPI#772: las tablas de la furgoneta (Scripts/Issue772_FurgonetaBultos.sql), fuera del EDMX.</summary>
    public interface IRepositorioFurgoneta
    {
        /// <summary>¿Existen RepartoBultosMovimientos y RepartoJornadas?</summary>
        bool ScriptAplicado();
        /// <summary>El último movimiento de cada bulto que se haya movido desde <paramref name="desde"/> (FechaRegistro).</summary>
        Task<List<MovimientoBultoReparto>> LeerUltimos(DateTime desde);
        /// <summary>Toda la historia de los bultos que cumplan la consulta (por FechaHora e Id).</summary>
        Task<List<MovimientoBultoReparto>> LeerHistoria(ConsultaMovimientosBulto consulta);
        Task<MovimientoBultoReparto> LeerPorClave(Guid clave);
        /// <summary>En una transacción, los que no estén ya (por ClaveIdempotencia). Devuelve cuántos ha insertado.</summary>
        Task<int> Registrar(IReadOnlyList<MovimientoBultoReparto> movimientos);
        Task<JornadaReparto> LeerJornadaPorClave(Guid clave);
        /// <summary>Las jornadas terminadas ese día (Fecha).</summary>
        Task<List<JornadaReparto>> LeerJornadas(DateTime dia);
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task RegistrarJornada(JornadaReparto jornada);
    }

    public class RepositorioFurgoneta : IRepositorioFurgoneta
    {
        public const string TABLA = "RepartoBultosMovimientos";
        public const string TABLA_JORNADAS = "RepartoJornadas";

        private const string COLUMNAS = "Id, RTRIM(Codigo) AS Codigo, RTRIM(TipoCodigo) AS TipoCodigo, EnvioId, NumTraspaso, Pedido, CAST(Bulto AS int) AS Bulto, " +
            "CAST(Total AS int) AS Total, RTRIM(Tipo) AS Tipo, RTRIM(Usuario) AS Usuario, FechaHora, FechaRegistro, Latitud, Longitud, RTRIM(Origen) AS Origen, " +
            "Descripcion, ClaveIdempotencia";
        private const string COLUMNAS_JORNADA = "Id, RTRIM(Usuario) AS Usuario, CAST(Fecha AS datetime) AS Fecha, FechaFin, BultosPendientes, Pendientes, ClaveIdempotencia, FechaRegistro";

        private const string SQL_INSERTAR = @"
IF NOT EXISTS (SELECT 1 FROM dbo." + TABLA + @" WHERE ClaveIdempotencia = @Clave)
    INSERT INTO dbo." + TABLA + @" (Codigo, TipoCodigo, EnvioId, NumTraspaso, Pedido, Bulto, Total, Tipo, Usuario, FechaHora, FechaRegistro,
        Latitud, Longitud, Origen, Descripcion, ClaveIdempotencia)
    VALUES (@Codigo, @TipoCodigo, @EnvioId, @Traspaso, @Pedido, @Bulto, @Total, @Tipo, @Usuario, @FechaHora, @FechaRegistro,
        @Latitud, @Longitud, @Origen, @Descripcion, @Clave);";

        private readonly NVEntities db;

        public RepositorioFurgoneta(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA) && ColumnasOpcionales.ExisteTabla(db, TABLA_JORNADAS);

        public Task<List<MovimientoBultoReparto>> LeerUltimos(DateTime desde)
            => db.Database.SqlQuery<MovimientoBultoReparto>($@"
SELECT {COLUMNAS} FROM (
    SELECT m.*, ROW_NUMBER() OVER (PARTITION BY m.Codigo ORDER BY m.FechaHora DESC, m.Id DESC) AS Fila
    FROM dbo.{TABLA} m
    WHERE m.Codigo IN (SELECT DISTINCT Codigo FROM dbo.{TABLA} WHERE FechaRegistro >= @desde)
) x WHERE x.Fila = 1 ORDER BY x.FechaHora, x.Id",
                new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde }).ToListAsync();

        public Task<List<MovimientoBultoReparto>> LeerHistoria(ConsultaMovimientosBulto consulta)
        {
            if (consulta == null || consulta.Vacia)
            {
                return Task.FromResult(new List<MovimientoBultoReparto>());
            }
            var condiciones = new List<string>();
            var parametros = new List<SqlParameter>();
            if (consulta.Codigos != null && consulta.Codigos.Count > 0)
            {
                List<string> codigos = consulta.Codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < codigos.Count; i++)
                {
                    parametros.Add(new SqlParameter("@c" + i, SqlDbType.VarChar, 30) { Value = codigos[i] });
                }
                if (codigos.Count > 0)
                {
                    condiciones.Add("Codigo IN (" + string.Join(", ", codigos.Select((_, i) => "@c" + i)) + ")");
                }
            }
            // Enteros en el texto (sin inyección posible)
            if (consulta.Envios != null && consulta.Envios.Count > 0)
            {
                condiciones.Add("EnvioId IN (" + Lista(consulta.Envios) + ")");
            }
            if (consulta.Traspasos != null && consulta.Traspasos.Count > 0)
            {
                condiciones.Add("NumTraspaso IN (" + Lista(consulta.Traspasos) + ")");
            }
            if (consulta.Pedidos != null && consulta.Pedidos.Count > 0)
            {
                condiciones.Add("Pedido IN (" + Lista(consulta.Pedidos) + ")");
            }
            if (condiciones.Count == 0)
            {
                return Task.FromResult(new List<MovimientoBultoReparto>());
            }
            return db.Database.SqlQuery<MovimientoBultoReparto>(
                $"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE {string.Join(" OR ", condiciones)} ORDER BY FechaHora, Id",
                parametros.Cast<object>().ToArray()).ToListAsync();
        }

        public async Task<MovimientoBultoReparto> LeerPorClave(Guid clave)
            => (await db.Database.SqlQuery<MovimientoBultoReparto>($"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE ClaveIdempotencia = @clave",
                new SqlParameter("@clave", SqlDbType.UniqueIdentifier) { Value = clave }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task<int> Registrar(IReadOnlyList<MovimientoBultoReparto> movimientos)
        {
            if (movimientos == null || movimientos.Count == 0)
            {
                return 0;
            }
            int insertados = 0;
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    foreach (MovimientoBultoReparto m in movimientos)
                    {
                        try
                        {
                            insertados += await db.Database.ExecuteSqlCommandAsync(SQL_INSERTAR,
                                new SqlParameter("@Codigo", SqlDbType.VarChar, 30) { Value = Recortar(m.Codigo, 30) },
                                new SqlParameter("@TipoCodigo", SqlDbType.VarChar, 2) { Value = m.TipoCodigo ?? string.Empty },
                                Entero("@EnvioId", m.EnvioId),
                                Entero("@Traspaso", m.NumTraspaso),
                                Entero("@Pedido", m.Pedido),
                                new SqlParameter("@Bulto", SqlDbType.SmallInt) { Value = (short)Math.Max(1, Math.Min(m.Bulto, short.MaxValue)) },
                                new SqlParameter("@Total", SqlDbType.SmallInt) { Value = m.Total.HasValue ? (object)(short)Math.Min(m.Total.Value, short.MaxValue) : DBNull.Value },
                                new SqlParameter("@Tipo", SqlDbType.VarChar, 12) { Value = m.Tipo },
                                new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(m.Usuario, 50) ?? string.Empty },
                                new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = m.FechaHora },
                                new SqlParameter("@FechaRegistro", SqlDbType.DateTime) { Value = m.FechaRegistro },
                                Decimal("@Latitud", m.Latitud),
                                Decimal("@Longitud", m.Longitud),
                                new SqlParameter("@Origen", SqlDbType.VarChar, 10) { Value = m.Origen ?? OrigenesMovimientoBulto.REPARTIDOR },
                                new SqlParameter("@Descripcion", SqlDbType.NVarChar, 150) { Value = (object)Recortar(m.Descripcion, 150) ?? DBNull.Value },
                                new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = m.ClaveIdempotencia }).ConfigureAwait(false);
                        }
                        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                        {
                            // Otra petición con la misma clave a la vez: ya está apuntado (la sentencia falla, la transacción sigue).
                        }
                    }
                    transaccion.Commit();
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
            return insertados;
        }

        public async Task<JornadaReparto> LeerJornadaPorClave(Guid clave)
            => (await db.Database.SqlQuery<JornadaReparto>($"SELECT TOP 1 {COLUMNAS_JORNADA} FROM dbo.{TABLA_JORNADAS} WHERE ClaveIdempotencia = @clave",
                new SqlParameter("@clave", SqlDbType.UniqueIdentifier) { Value = clave }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public Task<List<JornadaReparto>> LeerJornadas(DateTime dia)
            => db.Database.SqlQuery<JornadaReparto>($"SELECT {COLUMNAS_JORNADA} FROM dbo.{TABLA_JORNADAS} WHERE Fecha = @dia ORDER BY Id",
                new SqlParameter("@dia", SqlDbType.Date) { Value = dia.Date }).ToListAsync();

        public async Task RegistrarJornada(JornadaReparto jornada)
        {
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(
                    $"INSERT INTO dbo.{TABLA_JORNADAS} (Usuario, Fecha, FechaFin, BultosPendientes, Pendientes, ClaveIdempotencia, FechaRegistro) " +
                    "VALUES (@Usuario, @Fecha, @FechaFin, @Bultos, @Pendientes, @Clave, @FechaRegistro)",
                    new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(jornada.Usuario, 50) ?? string.Empty },
                    new SqlParameter("@Fecha", SqlDbType.Date) { Value = jornada.Fecha.Date },
                    new SqlParameter("@FechaFin", SqlDbType.DateTime) { Value = jornada.FechaFin },
                    new SqlParameter("@Bultos", SqlDbType.Int) { Value = jornada.BultosPendientes },
                    new SqlParameter("@Pendientes", SqlDbType.NVarChar, -1) { Value = (object)jornada.Pendientes ?? DBNull.Value },
                    new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = jornada.ClaveIdempotencia },
                    new SqlParameter("@FechaRegistro", SqlDbType.DateTime) { Value = jornada.FechaRegistro }).ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                throw new ClaveIdempotenciaRepetidaException(ex);
            }
        }

        private static SqlParameter Entero(string nombre, int? valor) => new SqlParameter(nombre, SqlDbType.Int) { Value = (object)valor ?? DBNull.Value };

        private static SqlParameter Decimal(string nombre, decimal? valor)
            => new SqlParameter(nombre, SqlDbType.Decimal) { Precision = 9, Scale = 6, Value = (object)valor ?? DBNull.Value };

        private static string Lista(IEnumerable<int> ids) => string.Join(", ", ids.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
