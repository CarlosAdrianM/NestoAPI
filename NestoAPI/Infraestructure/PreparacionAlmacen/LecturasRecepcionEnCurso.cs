using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Ariadna#51: qué recepción es (las cuatro columnas que la identifican en RecepcionesLecturasEnCurso).</summary>
    public class ClaveRecepcionEnCurso
    {
        public string Empresa { get; set; }
        /// <summary>REPO, COMP (en mayúsculas).</summary>
        public string Tipo { get; set; }
        public string Documento { get; set; }
        /// <summary>El almacén que recibe (en mayúsculas).</summary>
        public string Almacen { get; set; }
    }

    /// <summary>Ariadna#51: una fila EnCurso de RecepcionesLecturasEnCurso (la de apertura: sin Producto ni CodigoBulto, Cantidad 0).</summary>
    public class FilaLecturaRecepcionEnCurso
    {
        public long Id { get; set; }
        public Guid IdRecepcion { get; set; }
        public string Producto { get; set; }
        public string CodigoBulto { get; set; }
        public int Cantidad { get; set; }
        public string Usuario { get; set; }
        public string Dispositivo { get; set; }
        public DateTime Fecha { get; set; }

        /// <summary>La fila de apertura (solo guarda el IdRecepcion). Método y no propiedad: SqlQuery no busca columna para él.</summary>
        public static bool EsApertura(FilaLecturaRecepcionEnCurso fila) => string.IsNullOrWhiteSpace(fila.Producto) && string.IsNullOrWhiteSpace(fila.CodigoBulto);
    }

    /// <summary>Ariadna#51: un apunte ya validado y normalizado, listo para grabar.</summary>
    public class LecturaRecepcionEnCursoNueva
    {
        public Guid IdCliente { get; set; }
        public string Producto { get; set; }
        public string CodigoBulto { get; set; }
        public int Cantidad { get; set; }
        public string Dispositivo { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>
    /// Ariadna#51 (corte 2): lo leído de las recepciones en curso, en RecepcionesLecturasEnCurso (fuera del EDMX, SQL a mano).
    /// Cada operación devuelve las filas EnCurso de la recepción después de hacerla (siempre al menos la de apertura).
    /// </summary>
    public interface IRepositorioLecturasRecepcion
    {
        /// <summary>False mientras no se haya lanzado Scripts/Ariadna51_RecepcionesLecturasEnCurso.sql.</summary>
        bool TablaExiste();
        /// <summary>Lo en curso; si no hay nada, abre la recepción (un IdRecepcion nuevo, con su fila de apertura).</summary>
        Task<List<FilaLecturaRecepcionEnCurso>> Abrir(ClaveRecepcionEnCurso clave, string usuario, string dispositivo);
        /// <summary>Apunta las lecturas en la recepción en curso (abriéndola si hace falta). Un IdCliente ya grabado no se repite.</summary>
        Task<List<FilaLecturaRecepcionEnCurso>> Anotar(ClaveRecepcionEnCurso clave, string usuario, IReadOnlyCollection<LecturaRecepcionEnCursoNueva> lecturas);
        /// <summary>«Descartar lo leído»: lo en curso pasa a Descartada (no se borra) y se abre un IdRecepcion nuevo.</summary>
        Task<List<FilaLecturaRecepcionEnCurso>> Descartar(ClaveRecepcionEnCurso clave, string usuario, string dispositivo);
    }

    /// <summary>
    /// Ariadna#51: el SQL de RecepcionesLecturasEnCurso, común al repositorio (GET, POST, DELETE) y a las transacciones de
    /// Terminar de cada tipo (que lo llaman con su propio contexto, dentro de su transacción).
    /// </summary>
    public static class LecturasRecepcionEnCursoSql
    {
        public const string TABLA = "dbo.RecepcionesLecturasEnCurso";
        public const string EN_CURSO = "EnCurso";
        public const string TERMINADA = "Terminada";
        public const string DESCARTADA = "Descartada";

        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN OBJECT_ID('" + TABLA + "', 'U') IS NULL THEN 0 ELSE 1 END AS bit)";

        private const string CLAVE = "Empresa = @Empresa AND TipoOrigen = @Tipo AND Documento = @Documento AND Almacen = @Almacen";

        /// <summary>
        /// El IdRecepcion en curso; si no hay, la fila de apertura con @Nuevo. UPDLOCK + HOLDLOCK (dentro de la transacción): dos
        /// equipos que abren a la vez la misma recepción se ponen en fila y comparten el mismo IdRecepcion.
        /// </summary>
        internal const string SQL_ABRIR = @"
SET NOCOUNT ON;
DECLARE @Id uniqueidentifier = (SELECT TOP (1) IdRecepcion FROM " + TABLA + @" WITH (UPDLOCK, HOLDLOCK)
    WHERE " + CLAVE + @" AND Estado = '" + EN_CURSO + @"' ORDER BY Id);
IF @Id IS NULL
BEGIN
    SET @Id = @Nuevo;
    INSERT INTO " + TABLA + @" (IdCliente, Empresa, TipoOrigen, Documento, Almacen, IdRecepcion, Producto, CodigoBulto, Cantidad, Usuario, Dispositivo, Fecha, Estado)
    VALUES (NEWID(), @Empresa, @Tipo, @Documento, @Almacen, @Id, NULL, NULL, 0, @Usuario, @Dispositivo, GETDATE(), '" + EN_CURSO + @"');
END
SELECT @Id;";

        /// <summary>Un apunte. Un IdCliente ya grabado (en esta recepción o en cualquier otra) no se repite.</summary>
        internal const string SQL_ANOTAR = @"
INSERT INTO " + TABLA + @" (IdCliente, Empresa, TipoOrigen, Documento, Almacen, IdRecepcion, Producto, CodigoBulto, Cantidad, Usuario, Dispositivo, Fecha, Estado)
SELECT @IdCliente, @Empresa, @Tipo, @Documento, @Almacen, @IdRecepcion, @Producto, @CodigoBulto, @Cantidad, @Usuario, @Dispositivo, @Fecha, '" + EN_CURSO + @"'
WHERE NOT EXISTS (SELECT 1 FROM " + TABLA + @" WHERE IdCliente = @IdCliente)";

        internal const string SQL_LEER = @"
SELECT Id, IdRecepcion, RTRIM(Producto) AS Producto, RTRIM(CodigoBulto) AS CodigoBulto, Cantidad, RTRIM(Usuario) AS Usuario,
       RTRIM(Dispositivo) AS Dispositivo, Fecha
FROM " + TABLA + @"
WHERE " + CLAVE + @" AND Estado = '" + EN_CURSO + @"' AND IdRecepcion = @IdRecepcion
ORDER BY Id";

        internal const string SQL_DESCARTAR = @"
UPDATE " + TABLA + @" SET Estado = '" + DESCARTADA + @"', FechaEstado = GETDATE(), UsuarioEstado = @Usuario
WHERE " + CLAVE + @" AND Estado = '" + EN_CURSO + "'";

        /// <summary>
        /// Al terminar bien con el IdRecepcion compartido: lo en curso de ese IdRecepcion pasa a Terminada y, si se ha recibido
        /// en parte (@AbrirOtra), se abre uno nuevo para lo que queda. Con un IdRecepcion que no es el compartido (una versión
        /// anterior) no toca nada. Sin la tabla, tampoco (resolución diferida de nombres: el lote compila igual).
        /// </summary>
        internal const string SQL_TERMINAR = @"
SET NOCOUNT ON;
IF OBJECT_ID('" + TABLA + @"', 'U') IS NOT NULL
BEGIN
    UPDATE " + TABLA + @" SET Estado = '" + TERMINADA + @"', FechaEstado = GETDATE(), UsuarioEstado = @Usuario
    WHERE " + CLAVE + @" AND IdRecepcion = @IdRecepcion AND Estado = '" + EN_CURSO + @"';
    DECLARE @Terminadas int = @@ROWCOUNT;
    IF @Terminadas > 0 AND @AbrirOtra = 1 AND NOT EXISTS (SELECT 1 FROM " + TABLA + @" WITH (UPDLOCK, HOLDLOCK)
        WHERE " + CLAVE + @" AND Estado = '" + EN_CURSO + @"')
        INSERT INTO " + TABLA + @" (IdCliente, Empresa, TipoOrigen, Documento, Almacen, IdRecepcion, Producto, CodigoBulto, Cantidad, Usuario, Dispositivo, Fecha, Estado)
        VALUES (NEWID(), @Empresa, @Tipo, @Documento, @Almacen, @Nuevo, NULL, NULL, 0, @Usuario, @Dispositivo, GETDATE(), '" + EN_CURSO + @"');
    SELECT @Terminadas;
END
ELSE
    SELECT 0;";

        /// <summary>
        /// Dentro de la transacción de Terminar de cada tipo. Devuelve cuántas filas han pasado a Terminada (0: el IdRecepcion no
        /// era el compartido, o no hay tabla).
        /// </summary>
        public static Task<int> Terminar(NVEntities db, ClaveRecepcionEnCurso clave, Guid idRecepcion, bool abrirOtra, string usuario, string dispositivo)
        {
            List<SqlParameter> parametros = Parametros(clave);
            parametros.Add(new SqlParameter("@IdRecepcion", idRecepcion));
            parametros.Add(new SqlParameter("@AbrirOtra", System.Data.SqlDbType.Bit) { Value = abrirOtra });
            parametros.Add(new SqlParameter("@Nuevo", Guid.NewGuid()));
            parametros.Add(Texto("@Usuario", Usuario(usuario), 50));
            parametros.Add(Texto("@Dispositivo", Recortar(dispositivo, 50), 50));
            return db.Database.SqlQuery<int>(SQL_TERMINAR, parametros.Cast<object>().ToArray()).SingleAsync();
        }

        internal static async Task<Guid> Abrir(NVEntities db, ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
        {
            List<SqlParameter> parametros = Parametros(clave);
            parametros.Add(new SqlParameter("@Nuevo", Guid.NewGuid()));
            parametros.Add(Texto("@Usuario", Usuario(usuario), 50));
            parametros.Add(Texto("@Dispositivo", Recortar(dispositivo, 50), 50));
            return await db.Database.SqlQuery<Guid>(SQL_ABRIR, parametros.Cast<object>().ToArray()).SingleAsync().ConfigureAwait(false);
        }

        internal static async Task Anotar(NVEntities db, ClaveRecepcionEnCurso clave, Guid idRecepcion, string usuario, LecturaRecepcionEnCursoNueva lectura)
        {
            List<SqlParameter> parametros = Parametros(clave);
            parametros.Add(new SqlParameter("@IdCliente", lectura.IdCliente));
            parametros.Add(new SqlParameter("@IdRecepcion", idRecepcion));
            parametros.Add(new SqlParameter("@Producto", System.Data.SqlDbType.Char, 15) { Value = (object)lectura.Producto ?? DBNull.Value });
            parametros.Add(Texto("@CodigoBulto", lectura.CodigoBulto, 20));
            parametros.Add(new SqlParameter("@Cantidad", System.Data.SqlDbType.Int) { Value = lectura.Cantidad });
            parametros.Add(Texto("@Usuario", Usuario(usuario), 50));
            parametros.Add(Texto("@Dispositivo", Recortar(lectura.Dispositivo, 50), 50));
            parametros.Add(new SqlParameter("@Fecha", System.Data.SqlDbType.DateTime) { Value = lectura.Fecha });
            try
            {
                _ = await db.Database.ExecuteSqlCommandAsync(SQL_ANOTAR, parametros.Cast<object>().ToArray()).ConfigureAwait(false);
            }
            catch (Exception ex) when (EsDuplicado(ex))
            {
                // El mismo IdCliente en dos peticiones a la vez: lo ha grabado la otra (UX_RecepcionesLecturasEnCurso_IdCliente)
            }
        }

        internal static Task<List<FilaLecturaRecepcionEnCurso>> Leer(NVEntities db, ClaveRecepcionEnCurso clave, Guid idRecepcion)
        {
            List<SqlParameter> parametros = Parametros(clave);
            parametros.Add(new SqlParameter("@IdRecepcion", idRecepcion));
            return db.Database.SqlQuery<FilaLecturaRecepcionEnCurso>(SQL_LEER, parametros.Cast<object>().ToArray()).ToListAsync();
        }

        internal static Task<int> Descartar(NVEntities db, ClaveRecepcionEnCurso clave, string usuario)
        {
            List<SqlParameter> parametros = Parametros(clave);
            parametros.Add(Texto("@Usuario", Usuario(usuario), 50));
            return db.Database.ExecuteSqlCommandAsync(SQL_DESCARTAR, parametros.Cast<object>().ToArray());
        }

        private static bool EsDuplicado(Exception ex)
        {
            SqlException sql = ex as SqlException ?? ex.InnerException as SqlException;
            return sql != null && (sql.Number == 2601 || sql.Number == 2627);
        }

        private static List<SqlParameter> Parametros(ClaveRecepcionEnCurso clave)
        {
            return new List<SqlParameter>
            {
                new SqlParameter("@Empresa", System.Data.SqlDbType.Char, 3) { Value = clave.Empresa },
                new SqlParameter("@Tipo", System.Data.SqlDbType.Char, 4) { Value = clave.Tipo },
                Texto("@Documento", clave.Documento, 20),
                new SqlParameter("@Almacen", System.Data.SqlDbType.Char, 3) { Value = clave.Almacen }
            };
        }

        private static SqlParameter Texto(string nombre, string valor, int longitud)
        {
            return new SqlParameter(nombre, System.Data.SqlDbType.VarChar, longitud) { Value = (object)Recortar(valor, longitud) ?? DBNull.Value };
        }

        private static string Usuario(string usuario)
        {
            string limpio = usuario?.Trim();
            return string.IsNullOrEmpty(limpio) ? UsuarioAuditoriaHelper.DESCONOCIDO : Recortar(limpio, 50);
        }

        private static string Recortar(string texto, int maximo)
        {
            string limpio = texto?.Trim();
            return string.IsNullOrEmpty(limpio) ? null : (limpio.Length > maximo ? limpio.Substring(0, maximo) : limpio);
        }
    }

    public class RepositorioLecturasRecepcionSql : IRepositorioLecturasRecepcion
    {
        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;
        /// <summary>Sin la tabla, se vuelve a mirar cada minuto (para que funcione en cuanto se lance el script).</summary>
        private static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(1);

        private readonly Func<NVEntities> crearContexto;

        public RepositorioLecturasRecepcionSql() : this(null)
        {
        }

        internal RepositorioLecturasRecepcionSql(Func<NVEntities> crearContexto)
        {
            this.crearContexto = crearContexto ?? (() => new NVEntities());
        }

        public bool TablaExiste()
        {
            lock (cerrojo)
            {
                if (existe || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_TABLA)
                {
                    return existe;
                }
            }
            bool ahora;
            try
            {
                using (NVEntities db = crearContexto())
                {
                    ahora = db.Database.SqlQuery<bool>(LecturasRecepcionEnCursoSql.SQL_EXISTE).FirstOrDefault();
                }
            }
            catch (Exception)
            {
                ahora = false;
            }
            lock (cerrojo)
            {
                existe = ahora;
                ultimaComprobacion = DateTime.Now;
                return existe;
            }
        }

        public Task<List<FilaLecturaRecepcionEnCurso>> Abrir(ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
        {
            return EnTransaccion(clave, usuario, dispositivo, (db, id) => Task.CompletedTask);
        }

        public Task<List<FilaLecturaRecepcionEnCurso>> Anotar(ClaveRecepcionEnCurso clave, string usuario, IReadOnlyCollection<LecturaRecepcionEnCursoNueva> lecturas)
        {
            string dispositivo = lecturas?.Select(l => l.Dispositivo).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
            return EnTransaccion(clave, usuario, dispositivo, async (db, id) =>
            {
                foreach (LecturaRecepcionEnCursoNueva lectura in lecturas ?? new LecturaRecepcionEnCursoNueva[0])
                {
                    await LecturasRecepcionEnCursoSql.Anotar(db, clave, id, usuario, lectura).ConfigureAwait(false);
                }
            });
        }

        public async Task<List<FilaLecturaRecepcionEnCurso>> Descartar(ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
        {
            using (NVEntities db = crearContexto())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                _ = await LecturasRecepcionEnCursoSql.Descartar(db, clave, usuario).ConfigureAwait(false);
                Guid id = await LecturasRecepcionEnCursoSql.Abrir(db, clave, usuario, dispositivo).ConfigureAwait(false);
                List<FilaLecturaRecepcionEnCurso> filas = await LecturasRecepcionEnCursoSql.Leer(db, clave, id).ConfigureAwait(false);
                transaccion.Commit();
                return filas;
            }
        }

        private async Task<List<FilaLecturaRecepcionEnCurso>> EnTransaccion(ClaveRecepcionEnCurso clave, string usuario, string dispositivo,
            Func<NVEntities, Guid, Task> trabajo)
        {
            using (NVEntities db = crearContexto())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                Guid id = await LecturasRecepcionEnCursoSql.Abrir(db, clave, usuario, dispositivo).ConfigureAwait(false);
                await trabajo(db, id).ConfigureAwait(false);
                List<FilaLecturaRecepcionEnCurso> filas = await LecturasRecepcionEnCursoSql.Leer(db, clave, id).ConfigureAwait(false);
                transaccion.Commit();
                return filas;
            }
        }
    }

    /// <summary>Ariadna#51 (corte 2): lo leído de una recepción en curso, compartido entre Nesto y Ariadna.</summary>
    public interface IServicioLecturasRecepcion
    {
        /// <summary>Null si la tabla no existe todavía (script sin lanzar): el controlador da 404 y los clientes siguen como hoy.</summary>
        Task<LecturasRecepcionEnCursoDTO> Leer(string tipo, string empresa, string almacen, string documento, IPrincipal usuario, string dispositivo);
        Task<LecturasRecepcionEnCursoDTO> Anotar(string tipo, string empresa, string almacen, string documento,
            IEnumerable<LecturaRecepcionEnCursoDTO> lecturas, IPrincipal usuario);
        Task<LecturasRecepcionEnCursoDTO> Descartar(string tipo, string empresa, string almacen, string documento, IPrincipal usuario, string dispositivo);
    }

    /// <summary>
    /// Ariadna#51 (corte 2). Leer, apuntar y descartar lo leído de una recepción en curso. Los permisos son los de terminar esa
    /// recepción (<see cref="IOrigenRecepcion.PuedeTerminar"/>): quien puede recibir en ese almacén. Cada lectura es un apunte
    /// (+/−) y lo que se devuelve es la suma de todos, de todos los equipos.
    /// </summary>
    public class ServicioLecturasRecepcion : IServicioLecturasRecepcion
    {
        internal const string MENSAJE_SIN_TABLA = "Este servidor todavía no guarda lo leído de las recepciones en curso " +
            "(falta lanzar Scripts/Ariadna51_RecepcionesLecturasEnCurso.sql).";

        private readonly Dictionary<string, IOrigenRecepcion> origenes;
        private readonly IRepositorioLecturasRecepcion repositorio;
        private readonly Func<DateTime> ahora;
        /// <summary>Sugerencia 778: un código de barras se apunta como la referencia de su producto (lo ven así todos los equipos).</summary>
        private readonly TraductorCodigosRecepcion traductor;

        /// <param name="codigos">Sugerencia 778: para apuntar un código de barras como su producto. Sin él, se apunta tal cual.</param>
        public ServicioLecturasRecepcion(IEnumerable<IOrigenRecepcion> origenes, IRepositorioLecturasRecepcion repositorio,
            Productos.IRepositorioCodigosBarras codigos = null)
            : this(origenes, repositorio, null, codigos)
        {
        }

        internal ServicioLecturasRecepcion(IEnumerable<IOrigenRecepcion> origenes, IRepositorioLecturasRecepcion repositorio, Func<DateTime> ahora,
            Productos.IRepositorioCodigosBarras codigos = null)
        {
            this.origenes = (origenes ?? Enumerable.Empty<IOrigenRecepcion>())
                .ToDictionary(o => o.Tipo.Trim().ToUpperInvariant(), o => o);
            this.repositorio = repositorio;
            this.ahora = ahora ?? (() => DateTime.Now);
            traductor = new TraductorCodigosRecepcion(codigos);
        }

        public async Task<LecturasRecepcionEnCursoDTO> Leer(string tipo, string empresa, string almacen, string documento, IPrincipal usuario, string dispositivo)
        {
            if (!Preparar(tipo, empresa, almacen, documento, usuario, out ClaveRecepcionEnCurso clave, out string nombre))
            {
                return null;
            }
            return Sumar(await repositorio.Abrir(clave, nombre, dispositivo).ConfigureAwait(false));
        }

        public async Task<LecturasRecepcionEnCursoDTO> Anotar(string tipo, string empresa, string almacen, string documento,
            IEnumerable<LecturaRecepcionEnCursoDTO> lecturas, IPrincipal usuario)
        {
            if (!Preparar(tipo, empresa, almacen, documento, usuario, out ClaveRecepcionEnCurso clave, out string nombre))
            {
                return null;
            }
            List<LecturaRecepcionEnCursoNueva> nuevas = Validar(lecturas);
            // Sugerencia 778: el EAN leído (8435454004144) se guarda como su producto (40945): así casa con su línea en todos los
            // equipos y en Nesto, y al terminar llega la referencia (también desde las versiones de Ariadna que mandan el código)
            Dictionary<string, string> traducidos = await traductor.Traducir(clave.Empresa, nuevas.Select(n => n.Producto),
                async () => (await Origen(tipo).LeerEsperado(clave.Empresa, clave.Almacen, clave.Documento).ConfigureAwait(false))
                    ?.Lineas?.Select(l => l.Producto) ?? Enumerable.Empty<string>()).ConfigureAwait(false);
            foreach (LecturaRecepcionEnCursoNueva nueva in nuevas.Where(n => n.Producto != null))
            {
                nueva.Producto = TraductorCodigosRecepcion.Producto(traducidos, nueva.Producto);
            }
            return Sumar(await repositorio.Anotar(clave, nombre, nuevas).ConfigureAwait(false));
        }

        public async Task<LecturasRecepcionEnCursoDTO> Descartar(string tipo, string empresa, string almacen, string documento, IPrincipal usuario, string dispositivo)
        {
            if (!Preparar(tipo, empresa, almacen, documento, usuario, out ClaveRecepcionEnCurso clave, out string nombre))
            {
                return null;
            }
            return Sumar(await repositorio.Descartar(clave, nombre, dispositivo).ConfigureAwait(false));
        }

        /// <summary>
        /// Valida el tipo y el permiso y monta la clave. False si no hay tabla (después de comprobar el permiso: sin permiso es
        /// 403 haya tabla o no).
        /// </summary>
        private bool Preparar(string tipo, string empresa, string almacen, string documento, IPrincipal usuario,
            out ClaveRecepcionEnCurso clave, out string nombre)
        {
            IOrigenRecepcion origen = Origen(tipo);
            nombre = usuario?.Identity?.IsAuthenticated == true ? usuario.Identity.Name?.Trim() : null;
            if (string.IsNullOrWhiteSpace(nombre))
            {
                throw new UnauthorizedAccessException("Para recibir hay que estar identificado.");
            }
            string limpioEmpresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            string limpioAlmacen = string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant();
            string limpioDocumento = documento?.Trim();
            if (string.IsNullOrEmpty(limpioDocumento) || limpioDocumento.Length > 20)
            {
                throw new NestoBusinessException($"«{documento}» no es un documento de recepción.");
            }
            if (!origen.PuedeTerminar(usuario, limpioEmpresa, limpioAlmacen))
            {
                throw new UnauthorizedAccessException($"No tiene permiso para recibir {Nombre(origen)} en {limpioAlmacen}.");
            }
            clave = new ClaveRecepcionEnCurso
            {
                Empresa = limpioEmpresa,
                Tipo = origen.Tipo.Trim().ToUpperInvariant(),
                Documento = limpioDocumento,
                Almacen = limpioAlmacen
            };
            return repositorio.TablaExiste();
        }

        private static string Nombre(IOrigenRecepcion origen)
        {
            switch (origen.Tipo?.Trim().ToUpperInvariant())
            {
                case OrigenRecepcionReposiciones.TIPO:
                    return "reposiciones";
                case OrigenRecepcionCompras.TIPO:
                    return "mercancía de proveedores";
                default:
                    return $"recepciones de tipo {origen.Tipo}";
            }
        }

        private List<LecturaRecepcionEnCursoNueva> Validar(IEnumerable<LecturaRecepcionEnCursoDTO> lecturas)
        {
            List<LecturaRecepcionEnCursoDTO> todas = (lecturas ?? Enumerable.Empty<LecturaRecepcionEnCursoDTO>()).Where(l => l != null).ToList();
            if (!todas.Any())
            {
                throw new NestoBusinessException("No ha llegado ninguna lectura.");
            }
            var nuevas = new List<LecturaRecepcionEnCursoNueva>();
            var vistos = new HashSet<Guid>();
            DateTime servidor = ahora();
            foreach (LecturaRecepcionEnCursoDTO lectura in todas)
            {
                if (lectura.IdCliente == Guid.Empty)
                {
                    throw new NestoBusinessException("Cada lectura tiene que llevar su IdCliente. No se ha apuntado nada.");
                }
                string producto = string.IsNullOrWhiteSpace(lectura.Producto) ? null : PlanificadorRecepcionCompra.Normalizar(lectura.Producto);
                string bulto = string.IsNullOrWhiteSpace(lectura.CodigoBulto) ? null : CodigosBultoReposicion.Normalizar(lectura.CodigoBulto);
                if (producto == null && bulto == null)
                {
                    throw new NestoBusinessException("Cada lectura tiene que llevar el producto o la etiqueta del bulto. No se ha apuntado nada.");
                }
                if (producto != null && bulto != null)
                {
                    throw new NestoBusinessException("Una lectura es de un producto o de una etiqueta de bulto, no de los dos. No se ha apuntado nada.");
                }
                if (producto != null && producto.Length > 15)
                {
                    throw new NestoBusinessException($"«{producto}» no es un número de producto. No se ha apuntado nada.");
                }
                if (bulto != null && bulto.Length > 20)
                {
                    throw new NestoBusinessException($"«{bulto}» no es una etiqueta de bulto. No se ha apuntado nada.");
                }
                if (lectura.Cantidad == 0)
                {
                    throw new NestoBusinessException("Una lectura no puede ser de 0 unidades. No se ha apuntado nada.");
                }
                if (!vistos.Add(lectura.IdCliente))
                {
                    continue; // repetida en el mismo lote
                }
                // La fecha del dispositivo, salvo que no llegue o sea absurda (reloj mal puesto: más de un día en el futuro)
                DateTime? enDispositivo = lectura.Fecha.HasValue && lectura.Fecha.Value.Kind == DateTimeKind.Utc
                    ? lectura.Fecha.Value.ToLocalTime()
                    : lectura.Fecha;
                DateTime fecha = enDispositivo.HasValue && enDispositivo.Value > new DateTime(2000, 1, 1) && enDispositivo.Value < servidor.AddDays(1)
                    ? enDispositivo.Value
                    : servidor;
                nuevas.Add(new LecturaRecepcionEnCursoNueva
                {
                    IdCliente = lectura.IdCliente,
                    Producto = producto,
                    CodigoBulto = bulto,
                    Cantidad = lectura.Cantidad,
                    Dispositivo = lectura.Dispositivo?.Trim(),
                    Fecha = fecha
                });
            }
            return nuevas;
        }

        /// <summary>Todos los apuntes, sumados: por producto, por etiqueta y quién ha leído algo.</summary>
        internal static LecturasRecepcionEnCursoDTO Sumar(IEnumerable<FilaLecturaRecepcionEnCurso> filas)
        {
            List<FilaLecturaRecepcionEnCurso> todas = (filas ?? Enumerable.Empty<FilaLecturaRecepcionEnCurso>()).OrderBy(f => f.Id).ToList();
            List<FilaLecturaRecepcionEnCurso> lecturas = todas.Where(f => !FilaLecturaRecepcionEnCurso.EsApertura(f)).ToList();
            return new LecturasRecepcionEnCursoDTO
            {
                IdRecepcion = todas.Select(f => f.IdRecepcion).FirstOrDefault(),
                Lecturas = lecturas
                    .Where(f => !string.IsNullOrWhiteSpace(f.Producto))
                    .GroupBy(f => PlanificadorRecepcionCompra.Normalizar(f.Producto))
                    .Select(g => new { Primera = g.Min(f => f.Id), Lectura = new LecturaRecepcionDTO { Producto = g.Key, Cantidad = g.Sum(f => f.Cantidad) } })
                    .Where(x => x.Lectura.Cantidad > 0)
                    .OrderBy(x => x.Primera)
                    .Select(x => x.Lectura)
                    .ToList(),
                CodigosBulto = lecturas
                    .Where(f => !string.IsNullOrWhiteSpace(f.CodigoBulto))
                    .GroupBy(f => f.CodigoBulto.Trim().ToUpperInvariant())
                    .Where(g => g.Sum(f => f.Cantidad) > 0)
                    .OrderBy(g => g.Min(f => f.Id))
                    .Select(g => g.Key)
                    .ToList(),
                Equipos = lecturas
                    .GroupBy(f => new { Usuario = f.Usuario?.Trim() ?? string.Empty, Dispositivo = f.Dispositivo?.Trim() ?? string.Empty })
                    .Select(g => new EquipoRecepcionDTO
                    {
                        Usuario = g.Key.Usuario,
                        Dispositivo = g.Key.Dispositivo.Length == 0 ? null : g.Key.Dispositivo,
                        Ultima = g.Max(f => f.Fecha)
                    })
                    .OrderByDescending(e => e.Ultima)
                    .ToList()
            };
        }

        private IOrigenRecepcion Origen(string tipo)
        {
            string clave = tipo?.Trim().ToUpperInvariant() ?? string.Empty;
            return origenes.TryGetValue(clave, out IOrigenRecepcion origen)
                ? origen
                : throw new NestoBusinessException($"No se conoce el tipo de recepción «{tipo}». Tipos: {string.Join(", ", origenes.Keys)}.");
        }
    }
}
