using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Una fila de ReposicionesBultos (NestoAPI#645).</summary>
    public class FilaBultoReposicion
    {
        public string Empresa { get; set; }
        public int Traspaso { get; set; }
        public short Bulto { get; set; }
        public short Total { get; set; }
        public string Codigo { get; set; }
        public DateTime? FechaLectura { get; set; }
        public string UsuarioLectura { get; set; }
    }

    /// <summary>De dónde sale y adónde va un traspaso de reposición.</summary>
    public class OrigenDestinoReposicion
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
    }

    /// <summary>
    /// NestoAPI#645: el código y el texto de la etiqueta de cada bulto. Un solo sitio para el formato: «R81010-2» en el
    /// código de barras y «81010 · bulto 2/3 · ALC→ALG» debajo.
    /// </summary>
    public static class CodigosBultoReposicion
    {
        public static string Codigo(int traspaso, int bulto) => string.Format(CultureInfo.InvariantCulture, "R{0}-{1}", traspaso, bulto);

        public static string Texto(int traspaso, int bulto, int total, string origen, string destino)
            => string.Format(CultureInfo.InvariantCulture, "{0} · bulto {1}/{2} · {3}→{4}", traspaso, bulto, total,
                origen?.Trim() ?? "¿?", destino?.Trim() ?? "¿?");

        /// <summary>Lo leído, sin espacios y en mayúsculas («r81010-2 » → «R81010-2»). Null si viene vacío.</summary>
        public static string Normalizar(string codigo)
        {
            string limpio = codigo?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(limpio) ? null : limpio;
        }

        /// <summary>Si tiene la forma de una etiqueta de bulto (R + traspaso + guion + bulto), con sus números.</summary>
        public static bool Leer(string codigo, out int traspaso, out int bulto)
        {
            traspaso = bulto = 0;
            string limpio = Normalizar(codigo);
            if (limpio == null || limpio.Length < 4 || limpio[0] != 'R')
            {
                return false;
            }
            int guion = limpio.IndexOf('-');
            return guion > 1
                && int.TryParse(limpio.Substring(1, guion - 1), NumberStyles.None, CultureInfo.InvariantCulture, out traspaso)
                && int.TryParse(limpio.Substring(guion + 1), NumberStyles.None, CultureInfo.InvariantCulture, out bulto)
                && traspaso > 0 && bulto > 0;
        }
    }

    /// <summary>
    /// NestoAPI#645: la etiqueta de bulto, en las mismas etiquetas de 30×20 mm que las de hueco (<see cref="GeneradorEtiquetasHueco"/>)
    /// y en EPL2. Arriba el código de barras «R81010-2» y debajo, en dos líneas, «81010 - bulto 2/3» y «ALC > ALG» (en la
    /// impresora no hay «·» ni «→»: el texto completo es el de <see cref="CodigosBultoReposicion.Texto"/>).
    /// <para>«R81010-2» en Code128 son unos 120 módulos: con el módulo de 2 puntos de la etiqueta de hueco no cabe en 30 mm,
    /// así que va con el de 1 punto (0,125 mm, unos 15 mm de ancho). Las posiciones son constantes para ajustarlas tras la
    /// etiqueta de prueba.</para>
    /// </summary>
    public static class GeneradorEtiquetasBulto
    {
        public const int CODIGO_ESTRECHO = 1;
        public const int CODIGO_ALTO = 64;
        public const int CODIGO_Y = 8;
        public const int MODULOS_APROXIMADOS = 125;
        public const int TEXTO_Y = 84;
        public const int TEXTO_INTERLINEA = 28;
        public const int TEXTO_FUENTE = 3;
        public const int ANCHO_CARACTER = 12;

        public static string Epl(IEnumerable<BultoReposicionDTO> bultos, int traspaso, string origen, string destino)
        {
            var epl = new StringBuilder();
            epl.Append("I8,A,034\r\n");
            epl.Append("q").Append(GeneradorEtiquetasHueco.ANCHO).Append("\r\n");
            epl.Append("Q").Append(GeneradorEtiquetasHueco.ALTO).Append(",").Append(GeneradorEtiquetasHueco.SEPARACION).Append("\r\n");
            foreach (BultoReposicionDTO bulto in bultos ?? Enumerable.Empty<BultoReposicionDTO>())
            {
                epl.Append("N\r\n");
                int x = Math.Max(0, (GeneradorEtiquetasHueco.ANCHO - MODULOS_APROXIMADOS * CODIGO_ESTRECHO) / 2);
                epl.Append(string.Format(CultureInfo.InvariantCulture, "B{0},{1},0,1,{2},{2},{3},N,\"{4}\"\r\n",
                    x, CODIGO_Y, CODIGO_ESTRECHO, CODIGO_ALTO, bulto.Codigo));
                string[] lineas =
                {
                    string.Format(CultureInfo.InvariantCulture, "{0} - bulto {1}/{2}", traspaso, bulto.Bulto, bulto.Total),
                    $"{origen?.Trim() ?? "?"} > {destino?.Trim() ?? "?"}"
                };
                for (int i = 0; i < lineas.Length; i++)
                {
                    int xTexto = Math.Max(0, (GeneradorEtiquetasHueco.ANCHO - lineas[i].Length * ANCHO_CARACTER) / 2);
                    epl.Append(string.Format(CultureInfo.InvariantCulture, "A{0},{1},0,{2},1,1,N,\"{3}\"\r\n",
                        xTexto, TEXTO_Y + i * TEXTO_INTERLINEA, TEXTO_FUENTE, lineas[i]));
                }
                epl.Append("P1\r\n");
            }
            return epl.ToString();
        }
    }

    /// <summary>
    /// NestoAPI#645: lo que se lee y se escribe de ReposicionesBultos. Sin la tabla (script sin lanzar), las lecturas
    /// devuelven null y todo sigue como hasta ahora. Lo usan el repositorio y la transacción de la recepción (con su db).
    /// </summary>
    public static class ReposicionesBultosSql
    {
        internal const string SQL_EXISTE_TABLA = @"
SELECT CAST(CASE WHEN OBJECT_ID(N'dbo.ReposicionesBultos', N'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        internal const string SQL_COLUMNAS = @"RTRIM(b.Empresa) AS Empresa, b.NumTraspaso AS Traspaso, b.Bulto, b.Total, RTRIM(b.Codigo) AS Codigo,
       b.FechaLectura, b.UsuarioLectura";

        internal const string SQL_LEER = @"
SELECT " + SQL_COLUMNAS + @"
FROM dbo.ReposicionesBultos b WHERE b.Empresa = @p0 AND b.NumTraspaso = @p1 ORDER BY b.Bulto";

        internal const string SQL_LEER_POR_CODIGO = @"
SELECT " + SQL_COLUMNAS + @"
FROM dbo.ReposicionesBultos b WHERE b.Empresa = @p0 AND b.Codigo = @p1";

        internal const string SQL_HAY_LEIDOS = @"
SELECT COUNT(*) FROM dbo.ReposicionesBultos WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @p0 AND NumTraspaso = @p1 AND FechaLectura IS NOT NULL";

        internal const string SQL_BORRAR = @"
DELETE FROM dbo.ReposicionesBultos WHERE Empresa = @p0 AND NumTraspaso = @p1";

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.ReposicionesBultos (Empresa, NumTraspaso, Bulto, Total, Codigo, FechaCreacion, UsuarioCreacion)
VALUES (@p0, @p1, @p2, @p3, @p4, GETDATE(), @p5)";

        // Se queda la primera lectura: un bulto leído otra vez (recepción parcial) no cambia nada
        internal const string SQL_MARCAR_LEIDO = @"
IF OBJECT_ID(N'dbo.ReposicionesBultos', N'U') IS NOT NULL
    UPDATE dbo.ReposicionesBultos SET FechaLectura = GETDATE(), UsuarioLectura = @p2
    WHERE Empresa = @p0 AND Codigo = @p1 AND FechaLectura IS NULL";

        // La cabecera de la API si la hay; si no, la fila de entrada del diario (Almacén = destino, Delegación = origen) o,
        // ya recibida, la del extracto
        internal const string SQL_ORIGEN_DESTINO = @"
DECLARE @o varchar(10), @d varchar(10);
IF OBJECT_ID(N'dbo.ReposicionesTraspasos', N'U') IS NOT NULL
    SELECT TOP 1 @o = Origen, @d = Destino FROM dbo.ReposicionesTraspasos WHERE Empresa = @p0 AND NumTraspaso = @p1;
IF @o IS NULL
    SELECT TOP 1 @o = [Delegación], @d = [Almacén] FROM PreExtrProducto WHERE Empresa = @p0 AND [NºTraspaso] = @p1 AND Cantidad > 0;
IF @o IS NULL
    SELECT TOP 1 @o = [Delegación], @d = [Almacén] FROM ExtractoProducto WHERE Empresa = @p0 AND [NºTraspaso] = @p1 AND Cantidad > 0;
SELECT RTRIM(@o) AS Origen, RTRIM(@d) AS Destino";

        internal static SqlParameter Char(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.Char, largo) { Value = (object)valor?.Trim() ?? DBNull.Value };

        internal static SqlParameter Varchar(string nombre, string valor, int largo)
            => new SqlParameter(nombre, SqlDbType.VarChar, largo) { Value = (object)valor ?? DBNull.Value };

        internal static SqlParameter Entero(string nombre, int valor) => new SqlParameter(nombre, SqlDbType.Int) { Value = valor };

        public static Task<bool> ExisteTabla(Database db) => db.SqlQuery<bool>(SQL_EXISTE_TABLA).FirstAsync();

        public static async Task<List<FilaBultoReposicion>> Leer(Database db, string empresa, int traspaso)
        {
            if (!await ExisteTabla(db).ConfigureAwait(false))
            {
                return null;
            }
            return await db.SqlQuery<FilaBultoReposicion>(SQL_LEER, Char("@p0", empresa, 3), Entero("@p1", traspaso))
                .ToListAsync().ConfigureAwait(false);
        }

        public static async Task<FilaBultoReposicion> LeerPorCodigo(Database db, string empresa, string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo) || !await ExisteTabla(db).ConfigureAwait(false))
            {
                return null;
            }
            return await db.SqlQuery<FilaBultoReposicion>(SQL_LEER_POR_CODIGO, Char("@p0", empresa, 3),
                Varchar("@p1", CodigosBultoReposicion.Normalizar(codigo), 20)).FirstOrDefaultAsync().ConfigureAwait(false);
        }

        public static async Task MarcarLeidos(Database db, string empresa, IEnumerable<string> codigos, string usuario)
        {
            foreach (string codigo in (codigos ?? Enumerable.Empty<string>()).Select(CodigosBultoReposicion.Normalizar).Where(c => c != null).Distinct())
            {
                _ = await db.ExecuteSqlCommandAsync(SQL_MARCAR_LEIDO, Char("@p0", empresa, 3), Varchar("@p1", codigo, 20),
                    Varchar("@p2", Recortar(usuario, 50), 50)).ConfigureAwait(false);
            }
        }

        public static Task<OrigenDestinoReposicion> LeerOrigenDestino(Database db, string empresa, int traspaso)
            => db.SqlQuery<OrigenDestinoReposicion>(SQL_ORIGEN_DESTINO, Char("@p0", empresa, 3), Entero("@p1", traspaso)).FirstOrDefaultAsync();

        internal static string Recortar(string texto, int largo) => texto == null || texto.Length <= largo ? texto : texto.Substring(0, largo);
    }

    public interface IRepositorioBultosReposicion
    {
        Task<bool> ExisteTabla();
        /// <summary>Los bultos del traspaso, por orden. Null si la tabla no existe todavía; vacía si no tiene.</summary>
        Task<List<FilaBultoReposicion>> Leer(string empresa, int traspaso);
        /// <summary>Null si no hay ningún bulto con ese código (o no existe la tabla).</summary>
        Task<FilaBultoReposicion> LeerPorCodigo(string empresa, string codigo);
        /// <summary>Null si no se encuentra el traspaso.</summary>
        Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso);
        /// <summary>
        /// Deja el traspaso con <paramref name="total"/> bultos (borra los que tuviera), todo o nada. False, sin tocar nada,
        /// si alguno de los que tenía ya se ha leído al recibir.
        /// </summary>
        Task<bool> Reemplazar(string empresa, int traspaso, int total, string usuario);
    }

    public class RepositorioBultosReposicionSql : IRepositorioBultosReposicion, IDisposable
    {
        private readonly NVEntities db;
        private bool contextoPropio;

        public RepositorioBultosReposicionSql(NVEntities db)
        {
            this.db = db;
        }

        public static RepositorioBultosReposicionSql ConContextoPropio() => new RepositorioBultosReposicionSql(new NVEntities()) { contextoPropio = true };

        public Task<bool> ExisteTabla() => ReposicionesBultosSql.ExisteTabla(db.Database);

        public Task<List<FilaBultoReposicion>> Leer(string empresa, int traspaso) => ReposicionesBultosSql.Leer(db.Database, empresa, traspaso);

        public Task<FilaBultoReposicion> LeerPorCodigo(string empresa, string codigo) => ReposicionesBultosSql.LeerPorCodigo(db.Database, empresa, codigo);

        public async Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso)
        {
            OrigenDestinoReposicion datos = await ReposicionesBultosSql.LeerOrigenDestino(db.Database, empresa, traspaso).ConfigureAwait(false);
            return datos == null || (string.IsNullOrWhiteSpace(datos.Origen) && string.IsNullOrWhiteSpace(datos.Destino)) ? null : datos;
        }

        public async Task<bool> Reemplazar(string empresa, int traspaso, int total, string usuario)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                int leidos = await db.Database.SqlQuery<int>(ReposicionesBultosSql.SQL_HAY_LEIDOS,
                    ReposicionesBultosSql.Char("@p0", empresa, 3), ReposicionesBultosSql.Entero("@p1", traspaso)).FirstAsync().ConfigureAwait(false);
                if (leidos > 0)
                {
                    transaccion.Rollback();
                    return false;
                }
                _ = await db.Database.ExecuteSqlCommandAsync(ReposicionesBultosSql.SQL_BORRAR,
                    ReposicionesBultosSql.Char("@p0", empresa, 3), ReposicionesBultosSql.Entero("@p1", traspaso)).ConfigureAwait(false);
                for (int bulto = 1; bulto <= total; bulto++)
                {
                    _ = await db.Database.ExecuteSqlCommandAsync(ReposicionesBultosSql.SQL_INSERTAR,
                        ReposicionesBultosSql.Char("@p0", empresa, 3), ReposicionesBultosSql.Entero("@p1", traspaso),
                        new SqlParameter("@p2", SqlDbType.SmallInt) { Value = (short)bulto },
                        new SqlParameter("@p3", SqlDbType.SmallInt) { Value = (short)total },
                        ReposicionesBultosSql.Varchar("@p4", CodigosBultoReposicion.Codigo(traspaso, bulto), 20),
                        ReposicionesBultosSql.Varchar("@p5", ReposicionesBultosSql.Recortar(usuario, 50), 50)).ConfigureAwait(false);
                }
                transaccion.Commit();
                return true;
            }
        }

        public void Dispose()
        {
            if (contextoPropio)
            {
                db?.Dispose();
            }
        }
    }

    /// <summary>Un error de los bultos con el código HTTP que le toca (503 sin tabla, 404, 409, 400).</summary>
    public class BultosReposicionException : Exception
    {
        public BultosReposicionException(HttpStatusCode codigo, string mensaje) : base(mensaje)
        {
            StatusCode = codigo;
        }

        public HttpStatusCode StatusCode { get; }
    }

    public interface IServicioBultosReposicion
    {
        /// <exception cref="BultosReposicionException">Sin tabla (503), traspaso desconocido (404), algún bulto ya leído (409), número que no vale (400).</exception>
        /// <exception cref="ImpresionBultosException">Los bultos se han creado pero no se han podido imprimir (502).</exception>
        Task<ResultadoBultosReposicionDTO> Crear(string empresa, int traspaso, CrearBultosReposicionDTO peticion, string usuario);
        /// <exception cref="BultosReposicionException">Sin tabla (503) o sin bultos (404).</exception>
        /// <exception cref="ImpresionBultosException">No se han podido imprimir (502).</exception>
        Task<ResultadoBultosReposicionDTO> Reimprimir(string empresa, int traspaso, string usuario);
        /// <summary>Null si esa etiqueta no es de ninguna reposición.</summary>
        /// <exception cref="BultosReposicionException">Sin tabla (503).</exception>
        Task<LecturaBultoReposicionDTO> Leer(string empresa, string codigo);
    }

    /// <summary>Los bultos están creados pero la impresión ha fallado: lleva el resultado para quien quiera usarlo.</summary>
    public class ImpresionBultosException : Exception
    {
        public ImpresionBultosException(string mensaje, ResultadoBultosReposicionDTO resultado, Exception interna) : base(mensaje, interna)
        {
            Resultado = resultado;
        }

        public ResultadoBultosReposicionDTO Resultado { get; }
    }

    /// <summary>
    /// NestoAPI#645 (corte 1): los bultos de una reposición al cerrarla (Nesto «Enviar reposición» en las tiendas, Ariadna en
    /// Algete) y su etiqueta, en la impresora de etiquetas del usuario (la de las etiquetas de hueco).
    /// </summary>
    public class ServicioBultosReposicion : IServicioBultosReposicion
    {
        public const int MAXIMO_BULTOS = 50;
        internal const string SIN_TABLA = "Las etiquetas de bulto todavía no están disponibles en el servidor (falta la tabla ReposicionesBultos).";

        private readonly IRepositorioBultosReposicion repositorio;
        private readonly ILectorParametrosUsuario parametros;
        private readonly IImpresoraEtiquetas impresora;

        public ServicioBultosReposicion(IRepositorioBultosReposicion repositorio, ILectorParametrosUsuario parametros, IImpresoraEtiquetas impresora)
        {
            this.repositorio = repositorio;
            this.parametros = parametros;
            this.impresora = impresora;
        }

        public async Task<ResultadoBultosReposicionDTO> Crear(string empresa, int traspaso, CrearBultosReposicionDTO peticion, string usuario)
        {
            int total = peticion?.Bultos ?? 0;
            if (total < 1 || total > MAXIMO_BULTOS)
            {
                throw new BultosReposicionException(HttpStatusCode.BadRequest, $"El número de bultos tiene que estar entre 1 y {MAXIMO_BULTOS}.");
            }
            await ComprobarTabla().ConfigureAwait(false);
            OrigenDestinoReposicion datos = await repositorio.LeerOrigenDestino(empresa, traspaso).ConfigureAwait(false)
                ?? throw new BultosReposicionException(HttpStatusCode.NotFound, $"No hay ninguna reposición con el número {traspaso}.");
            if (!await repositorio.Reemplazar(empresa, traspaso, total, usuario).ConfigureAwait(false))
            {
                throw new BultosReposicionException(HttpStatusCode.Conflict,
                    $"Algún bulto de la reposición {traspaso} ya se ha leído al recibirla: no se puede cambiar el número de bultos.");
            }
            List<FilaBultoReposicion> filas = Enumerable.Range(1, total)
                .Select(b => new FilaBultoReposicion { Traspaso = traspaso, Bulto = (short)b, Total = (short)total, Codigo = CodigosBultoReposicion.Codigo(traspaso, b) })
                .ToList();
            ResultadoBultosReposicionDTO resultado = Resultado(traspaso, datos, filas);
            if (peticion.Imprimir)
            {
                Imprimir(empresa, usuario, resultado);
            }
            return resultado;
        }

        public async Task<ResultadoBultosReposicionDTO> Reimprimir(string empresa, int traspaso, string usuario)
        {
            await ComprobarTabla().ConfigureAwait(false);
            List<FilaBultoReposicion> filas = await repositorio.Leer(empresa, traspaso).ConfigureAwait(false);
            if (filas == null || !filas.Any())
            {
                throw new BultosReposicionException(HttpStatusCode.NotFound, $"La reposición {traspaso} no tiene etiquetas de bulto.");
            }
            OrigenDestinoReposicion datos = await repositorio.LeerOrigenDestino(empresa, traspaso).ConfigureAwait(false) ?? new OrigenDestinoReposicion();
            ResultadoBultosReposicionDTO resultado = Resultado(traspaso, datos, filas);
            Imprimir(empresa, usuario, resultado);
            return resultado;
        }

        public async Task<LecturaBultoReposicionDTO> Leer(string empresa, string codigo)
        {
            await ComprobarTabla().ConfigureAwait(false);
            FilaBultoReposicion fila = await repositorio.LeerPorCodigo(empresa, CodigosBultoReposicion.Normalizar(codigo)).ConfigureAwait(false);
            if (fila == null)
            {
                return null;
            }
            OrigenDestinoReposicion datos = await repositorio.LeerOrigenDestino(empresa, fila.Traspaso).ConfigureAwait(false) ?? new OrigenDestinoReposicion();
            return new LecturaBultoReposicionDTO
            {
                Codigo = fila.Codigo?.Trim(),
                Traspaso = fila.Traspaso,
                Bulto = fila.Bulto,
                Total = fila.Total,
                Origen = datos.Origen?.Trim(),
                Destino = datos.Destino?.Trim(),
                Leido = fila.FechaLectura.HasValue,
                FechaLectura = fila.FechaLectura
            };
        }

        private async Task ComprobarTabla()
        {
            if (!await repositorio.ExisteTabla().ConfigureAwait(false))
            {
                throw new BultosReposicionException(HttpStatusCode.ServiceUnavailable, SIN_TABLA);
            }
        }

        private static ResultadoBultosReposicionDTO Resultado(int traspaso, OrigenDestinoReposicion datos, IEnumerable<FilaBultoReposicion> filas)
        {
            string origen = datos.Origen?.Trim();
            string destino = datos.Destino?.Trim();
            return new ResultadoBultosReposicionDTO
            {
                Traspaso = traspaso,
                Origen = origen,
                Destino = destino,
                Bultos = filas.OrderBy(f => f.Bulto).Select(f => new BultoReposicionDTO
                {
                    Codigo = f.Codigo?.Trim(),
                    Bulto = f.Bulto,
                    Total = f.Total,
                    Texto = CodigosBultoReposicion.Texto(traspaso, f.Bulto, f.Total, origen, destino)
                }).ToList()
            };
        }

        /// <summary>Un solo trabajo con una etiqueta por bulto, en la impresora de etiquetas del usuario (la de las de hueco).</summary>
        private void Imprimir(string empresa, string usuario, ResultadoBultosReposicionDTO resultado)
        {
            string destino;
            try
            {
                destino = ServicioEtiquetasHueco.ImpresoraDelUsuario(parametros, empresa, usuario);
            }
            catch (ArgumentException)
            {
                string sinDominio = (usuario ?? string.Empty).Substring((usuario ?? string.Empty).IndexOf('\\') + 1).Trim();
                throw new ImpresionBultosException($"Los bultos de la reposición {resultado.Traspaso} están creados, pero el usuario {sinDominio} " +
                    $"no tiene impresora de etiquetas (parámetro {ServicioEtiquetasHueco.PARAMETRO_IMPRESORA}). Pídasela a informática y reimprímalas.",
                    resultado, null);
            }
            try
            {
                impresora.Imprimir(destino, GeneradorEtiquetasBulto.Epl(resultado.Bultos, resultado.Traspaso, resultado.Origen, resultado.Destino),
                    $"Bultos reposición {resultado.Traspaso}");
            }
            catch (ImpresionEtiquetasException ex)
            {
                resultado.Impresora = destino;
                throw new ImpresionBultosException($"Los bultos de la reposición {resultado.Traspaso} están creados, pero no se han podido imprimir " +
                    $"las etiquetas: {ex.Message} Puede reimprimirlas.", resultado, ex);
            }
            resultado.Impresora = destino;
        }
    }
}
