using NestoAPI.Models.Novedades;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#890 (Carlos 23/11/26: «para no tener que estar yo ejecutando los scripts»): las novedades de cada versión se
    /// cargan, cambian y borran por la API (Dirección/Informática) en vez de con un Scripts/Novedades_X_Y_Z_R.sql de sa.
    /// Idempotente por Versión + Ámbito + Título, como el WHERE NOT EXISTS de los scripts.
    /// </summary>
    internal class NovedadNormalizada
    {
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Valor de la columna Perfiles (null = para todos).</summary>
        public string PerfilesColumna { get; set; }
        /// <summary>false = no se han mandado (en una sugerencia, no se tocan los que tenga).</summary>
        public bool TraePerfiles { get; set; }
        public int? SugerenciaId { get; set; }
    }

    internal class CargaNormalizada
    {
        public string Version { get; set; }
        public DateTime Fecha { get; set; }
        public string Ambito { get; set; }
        public List<NovedadNormalizada> Novedades { get; set; } = new List<NovedadNormalizada>();
    }

    internal class CambioNovedadNormalizado
    {
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string Categoria { get; set; }
        public bool TocarPerfiles { get; set; }
        public string PerfilesColumna { get; set; }
        public bool? Publicada { get; set; }
    }

    /// <summary>Una fila de dbo.Novedades tal como la necesitan estos endpoints.</summary>
    public class NovedadVersionFila
    {
        public int Id { get; set; }
        public string Version { get; set; }
        public DateTime Fecha { get; set; }
        public string Categoria { get; set; }
        public string Titulo { get; set; }
        public string Descripcion { get; set; }
        public string Ambito { get; set; }
        public bool Publicada { get; set; }
        public string Perfiles { get; set; }
        public bool EsSugerencia { get; set; }
        public bool TieneFeedback { get; set; }

        internal NovedadVersionLeidaDTO ADto() => new NovedadVersionLeidaDTO
        {
            Id = Id,
            Version = Version?.Trim(),
            Fecha = Fecha,
            Categoria = Categoria?.Trim(),
            Titulo = Titulo?.Trim(),
            Descripcion = Descripcion?.Trim(),
            Ambito = Ambito?.Trim(),
            Publicada = Publicada,
            Perfiles = ReglasPerfilesNovedades.Parsear(Perfiles),
            EsSugerencia = EsSugerencia
        };
    }

    /// <summary>El 4xx de estos endpoints, con su motivo de usted.</summary>
    internal class ErrorNovedadesVersion : Exception
    {
        public HttpStatusCode Estado { get; }

        public ErrorNovedadesVersion(HttpStatusCode estado, string mensaje) : base(mensaje)
        {
            Estado = estado;
        }
    }

    /// <summary>LAS REGLAS (puras) de la carga, el cambio y el borrado.</summary>
    internal static class ReglasNovedadesVersion
    {
        public static readonly string[] CATEGORIAS = { "Nuevo", "Mejorado", "Corregido" };
        public const int MAXIMO_NOVEDADES = 100;
        public const string TABLA_RASTRO = "Novedades";

        private static string Categoria(string categoria) =>
            CATEGORIAS.FirstOrDefault(c => string.Equals(c, categoria?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static string ClaveTitulo(string titulo) => (titulo ?? string.Empty).Trim().ToUpperInvariant();

        /// <summary>Valida la carga entera (todo o nada) y la deja lista. Devuelve el motivo si no vale, o null.</summary>
        public static string Normalizar(CargaNovedadesVersionDTO carga, DateTime hoy, out CargaNormalizada normalizada)
        {
            normalizada = null;
            if (carga == null)
            {
                return "No se ha recibido ninguna novedad.";
            }
            string version = carga.Version?.Trim();
            if (string.IsNullOrEmpty(version) || version.Length > ReglasSugerenciasNovedades.LONGITUD_VERSION
                || !Version.TryParse(version, out Version v))
            {
                return "La versión no es válida (por ejemplo, 1.10.67.0 o 2.45).";
            }
            string ambito = ReglasSugerenciasNovedades.AMBITOS
                .FirstOrDefault(a => string.Equals(a, carga.Ambito?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (ambito == null)
            {
                return "El ámbito tiene que ser " + string.Join(", ", ReglasSugerenciasNovedades.AMBITOS) + ".";
            }
            bool esApp = ReglasSugerenciasNovedades.EsAmbitoDeApp(ambito);
            if ((esApp && v.Major != ReglasSugerenciasNovedades.VERSION_MAYOR_APPS)
                || (!esApp && v.Major != ReglasSugerenciasNovedades.VERSION_MAYOR_ESCRITORIO))
            {
                return $"La versión {version} no es de {ambito}: Nesto y NestoAPI van por 1.x, NestoApp y Ariadna por 2.x.";
            }
            if (carga.Novedades == null || carga.Novedades.Count == 0)
            {
                return "No se ha recibido ninguna novedad.";
            }
            if (carga.Novedades.Count > MAXIMO_NOVEDADES)
            {
                return $"Como mucho {MAXIMO_NOVEDADES} novedades por versión.";
            }

            var resultado = new CargaNormalizada
            {
                Version = version,
                Ambito = ambito,
                Fecha = (carga.Fecha ?? hoy).Date
            };
            var titulos = new HashSet<string>();
            var sugerencias = new HashSet<int>();
            for (int i = 0; i < carga.Novedades.Count; i++)
            {
                NovedadVersionDTO n = carga.Novedades[i];
                string donde = $"La novedad {i + 1}";
                if (n == null)
                {
                    return donde + " está vacía.";
                }
                string error = NormalizarNovedad(n, donde, out NovedadNormalizada lista);
                if (error != null)
                {
                    return error;
                }
                if (!titulos.Add(ClaveTitulo(lista.Titulo)))
                {
                    return $"{donde} repite el título «{lista.Titulo}».";
                }
                if (lista.SugerenciaId.HasValue && !sugerencias.Add(lista.SugerenciaId.Value))
                {
                    return $"{donde} repite la sugerencia {lista.SugerenciaId}.";
                }
                resultado.Novedades.Add(lista);
            }
            normalizada = resultado;
            return null;
        }

        private static string NormalizarNovedad(NovedadVersionDTO n, string donde, out NovedadNormalizada lista)
        {
            lista = null;
            string categoria = Categoria(n.Categoria);
            if (categoria == null)
            {
                return $"{donde}: la categoría tiene que ser " + string.Join(", ", CATEGORIAS) + ".";
            }
            string titulo = n.Titulo?.Trim();
            if (string.IsNullOrEmpty(titulo) || titulo.Length > ReglasSugerenciasNovedades.LONGITUD_TITULO)
            {
                return $"{donde}: el título tiene que tener entre 1 y {ReglasSugerenciasNovedades.LONGITUD_TITULO} caracteres.";
            }
            string descripcion = string.IsNullOrWhiteSpace(n.Descripcion) ? null : n.Descripcion.Trim();
            if (descripcion != null && descripcion.Length > ReglasSugerenciasNovedades.LONGITUD_DESCRIPCION)
            {
                return $"{donde} («{titulo}»): la descripción no puede pasar de {ReglasSugerenciasNovedades.LONGITUD_DESCRIPCION} caracteres.";
            }
            string perfiles = ReglasPerfilesNovedades.ValorColumna(n.Perfiles, out string errorPerfiles);
            if (errorPerfiles != null)
            {
                return $"{donde} («{titulo}»): {errorPerfiles}.";
            }
            if (n.SugerenciaId.HasValue && n.SugerenciaId.Value <= 0)
            {
                return $"{donde} («{titulo}»): la sugerencia no es válida.";
            }
            lista = new NovedadNormalizada
            {
                Categoria = categoria,
                Titulo = titulo,
                Descripcion = descripcion,
                PerfilesColumna = perfiles,
                TraePerfiles = n.Perfiles != null,
                SugerenciaId = n.SugerenciaId
            };
            return null;
        }

        /// <summary>Las que ya están en esa versión y ese ámbito (por título, sin mayúsculas ni espacios de más).</summary>
        public static bool YaEsta(NovedadNormalizada novedad, IEnumerable<NovedadVersionFila> deLaVersion) =>
            deLaVersion.Any(f => ClaveTitulo(f.Titulo) == ClaveTitulo(novedad.Titulo));

        /// <summary>
        /// Una novedad con SugerenciaId: qué hacer con esa fila. null = implementarla; «ya» = ya está en esta versión (no se
        /// toca); si no, el 4xx.
        /// </summary>
        public static bool SugerenciaYaImplementada(NovedadNormalizada novedad, NovedadVersionFila fila, string version, out ErrorNovedadesVersion error)
        {
            error = null;
            int id = novedad.SugerenciaId.Value;
            if (fila == null)
            {
                error = new ErrorNovedadesVersion(HttpStatusCode.BadRequest, $"La sugerencia {id} no existe.");
                return false;
            }
            if (!fila.EsSugerencia)
            {
                error = new ErrorNovedadesVersion(HttpStatusCode.BadRequest, $"La novedad {id} no es una sugerencia de un usuario.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(fila.Version))
            {
                return false;
            }
            if (string.Equals(fila.Version.Trim(), version, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            error = new ErrorNovedadesVersion(HttpStatusCode.Conflict,
                $"La sugerencia {id} ya está implementada en la versión {fila.Version.Trim()}.");
            return false;
        }

        /// <summary>Valida un PUT. Devuelve el motivo si no vale, o null.</summary>
        public static string NormalizarCambio(CambioNovedadVersionDTO cambio, out CambioNovedadNormalizado normalizado)
        {
            normalizado = null;
            if (cambio == null || (cambio.Titulo == null && cambio.Descripcion == null && cambio.Categoria == null
                && cambio.Perfiles == null && cambio.Publicada == null))
            {
                return "No se ha recibido ningún cambio.";
            }
            var n = new CambioNovedadNormalizado { Publicada = cambio.Publicada };
            if (cambio.Titulo != null)
            {
                n.Titulo = cambio.Titulo.Trim();
                if (n.Titulo.Length == 0 || n.Titulo.Length > ReglasSugerenciasNovedades.LONGITUD_TITULO)
                {
                    return $"El título tiene que tener entre 1 y {ReglasSugerenciasNovedades.LONGITUD_TITULO} caracteres.";
                }
            }
            if (cambio.Descripcion != null)
            {
                n.Descripcion = cambio.Descripcion.Trim();
                if (n.Descripcion.Length > ReglasSugerenciasNovedades.LONGITUD_DESCRIPCION)
                {
                    return $"La descripción no puede pasar de {ReglasSugerenciasNovedades.LONGITUD_DESCRIPCION} caracteres.";
                }
            }
            if (cambio.Categoria != null)
            {
                n.Categoria = Categoria(cambio.Categoria);
                if (n.Categoria == null)
                {
                    return "La categoría tiene que ser " + string.Join(", ", CATEGORIAS) + ".";
                }
            }
            if (cambio.Perfiles != null)
            {
                n.TocarPerfiles = true;
                n.PerfilesColumna = ReglasPerfilesNovedades.ValorColumna(cambio.Perfiles, out string error);
                if (error != null)
                {
                    return error + ".";
                }
            }
            normalizado = n;
            return null;
        }

        /// <summary>El PUT solo vale para novedades de una versión (una sugerencia sin implementar va por PUT api/Novedades/Sugerencias/{id}).</summary>
        public static string MotivoNoSePuedeCambiar(NovedadVersionFila fila) =>
            string.IsNullOrWhiteSpace(fila.Version)
                ? $"La {fila.Id} es una sugerencia sin implementar: cámbiela en PUT api/Novedades/Sugerencias/{fila.Id}."
                : null;

        /// <summary>Solo se borran las novedades de una versión que nadie ha comentado ni votado; lo demás, se despublica.</summary>
        public static string MotivoNoSePuedeBorrar(NovedadVersionFila fila)
        {
            if (string.IsNullOrWhiteSpace(fila.Version) || fila.EsSugerencia)
            {
                return $"La {fila.Id} es una sugerencia de un usuario: no se borra. Si no debe salir, despublíquela (Publicada = false).";
            }
            if (fila.TieneFeedback)
            {
                return $"La novedad {fila.Id} tiene comentarios o votos: no se borra. Si no debe salir, despublíquela (Publicada = false).";
            }
            return null;
        }
    }

    internal interface IRepositorioNovedadesVersion
    {
        List<NovedadVersionFila> LeerDeVersion(string version, string ambito);
        NovedadVersionFila Leer(int id);
        /// <summary>Inserta las que no estén (en una transacción, con su rastro). Devuelve el Id de cada título insertado.</summary>
        Dictionary<string, int> Insertar(CargaNormalizada carga, IReadOnlyList<NovedadNormalizada> nuevas, string usuario);
        bool Actualizar(int id, CambioNovedadNormalizado cambio, string anterior, string usuario);
        /// <summary>false = ya no existía.</summary>
        bool Borrar(int id, string anterior, string usuario);
        void Anotar(string anterior, string nuevo, string usuario);
    }

    internal class RepositorioNovedadesVersionSql : IRepositorioNovedadesVersion
    {
        private const string COLUMNAS = @"n.Id, n.Version, n.Fecha, n.Categoria, n.Titulo, n.Descripcion, n.Ambito, n.Publicada, n.Perfiles,
            CAST(CASE WHEN n.TextoOriginal IS NULL THEN 0 ELSE 1 END AS bit) AS EsSugerencia,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.NovedadesComentarios c WHERE c.NovedadId = n.Id)
                        OR EXISTS (SELECT 1 FROM dbo.NovedadesVotos v WHERE v.NovedadId = n.Id) THEN 1 ELSE 0 END AS bit) AS TieneFeedback";

        internal const string SQL_INSERTAR = @"
INSERT INTO dbo.Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
OUTPUT CAST(INSERTED.Id AS int)
SELECT @version, @fecha, @categoria, @titulo, @descripcion, @ambito, 1, @usuario, @perfiles
WHERE NOT EXISTS (SELECT 1 FROM dbo.Novedades x WHERE x.Version = @version AND x.Ambito = @ambito AND x.Titulo = @titulo);";

        private const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@tabla, @anterior, @nuevo, @usuarioRastro);";

        private readonly string cadenaConexion;

        internal RepositorioNovedadesVersionSql(string cadenaConexion)
        {
            this.cadenaConexion = cadenaConexion;
        }

        internal static RepositorioNovedadesVersionSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioNovedadesVersionSql(origen.ConnectionString);
        }

        private static string Recortar(string texto, int maximo) =>
            texto == null ? string.Empty : (texto.Length <= maximo ? texto : texto.Substring(0, maximo));

        private static List<NovedadVersionFila> LeerFilas(SqlCommand cmd)
        {
            var filas = new List<NovedadVersionFila>();
            using (SqlDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    filas.Add(new NovedadVersionFila
                    {
                        Id = r.GetInt32(0),
                        Version = r.IsDBNull(1) ? null : r.GetString(1).Trim(),
                        Fecha = r.GetDateTime(2),
                        Categoria = r.IsDBNull(3) ? null : r.GetString(3).Trim(),
                        Titulo = r.IsDBNull(4) ? null : r.GetString(4).Trim(),
                        Descripcion = r.IsDBNull(5) ? null : r.GetString(5),
                        Ambito = r.IsDBNull(6) ? null : r.GetString(6).Trim(),
                        Publicada = r.GetBoolean(7),
                        Perfiles = r.IsDBNull(8) ? null : r.GetString(8),
                        EsSugerencia = r.GetBoolean(9),
                        TieneFeedback = r.GetBoolean(10)
                    });
                }
            }
            return filas;
        }

        public List<NovedadVersionFila> LeerDeVersion(string version, string ambito)
        {
            using (var cn = new SqlConnection(cadenaConexion))
            using (var cmd = new SqlCommand("SELECT " + COLUMNAS + " FROM dbo.Novedades n WHERE n.Version = @version" +
                (ambito == null ? string.Empty : " AND n.Ambito = @ambito") + " ORDER BY n.Id", cn))
            {
                cmd.Parameters.Add("@version", SqlDbType.VarChar, 23).Value = version;
                if (ambito != null)
                {
                    cmd.Parameters.Add("@ambito", SqlDbType.NVarChar, 20).Value = ambito;
                }
                cn.Open();
                return LeerFilas(cmd);
            }
        }

        public NovedadVersionFila Leer(int id)
        {
            using (var cn = new SqlConnection(cadenaConexion))
            using (var cmd = new SqlCommand("SELECT " + COLUMNAS + " FROM dbo.Novedades n WHERE n.Id = @id", cn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                cn.Open();
                return LeerFilas(cmd).FirstOrDefault();
            }
        }

        private static void AnotarEn(SqlConnection cn, SqlTransaction tx, string anterior, string nuevo, string usuario)
        {
            using (var cmd = new SqlCommand(SQL_RASTRO, cn, tx))
            {
                cmd.Parameters.Add("@tabla", SqlDbType.NVarChar, 50).Value = ReglasNovedadesVersion.TABLA_RASTRO;
                cmd.Parameters.Add("@anterior", SqlDbType.NVarChar, -1).Value = (object)anterior ?? DBNull.Value;
                cmd.Parameters.Add("@nuevo", SqlDbType.NVarChar, -1).Value = (object)nuevo ?? DBNull.Value;
                cmd.Parameters.Add("@usuarioRastro", SqlDbType.NVarChar, 50).Value = Recortar(usuario, 50);
                _ = cmd.ExecuteNonQuery();
            }
        }

        public Dictionary<string, int> Insertar(CargaNormalizada carga, IReadOnlyList<NovedadNormalizada> nuevas, string usuario)
        {
            var ids = new Dictionary<string, int>();
            using (var cn = new SqlConnection(cadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                {
                    // Dos cargas de la misma versión a la vez no se pisan (la segunda ve lo que metió la primera).
                    using (var bloqueo = new SqlCommand("EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;", cn, tx))
                    {
                        bloqueo.Parameters.Add("@recurso", SqlDbType.NVarChar, 255).Value = $"NovedadesVersion:{carga.Ambito}:{carga.Version}";
                        SqlParameter r = bloqueo.Parameters.Add("@r", SqlDbType.Int);
                        r.Direction = ParameterDirection.Output;
                        _ = bloqueo.ExecuteNonQuery();
                        if ((int)r.Value < 0)
                        {
                            throw new ErrorNovedadesVersion(HttpStatusCode.Conflict, "Se están cargando ahora mismo las novedades de esta versión: vuelva a intentarlo.");
                        }
                    }
                    foreach (NovedadNormalizada n in nuevas)
                    {
                        using (var cmd = new SqlCommand(SQL_INSERTAR, cn, tx))
                        {
                            cmd.Parameters.Add("@version", SqlDbType.VarChar, 23).Value = carga.Version;
                            cmd.Parameters.Add("@fecha", SqlDbType.Date).Value = carga.Fecha;
                            cmd.Parameters.Add("@categoria", SqlDbType.NVarChar, 20).Value = n.Categoria;
                            cmd.Parameters.Add("@titulo", SqlDbType.NVarChar, 200).Value = n.Titulo;
                            cmd.Parameters.Add("@descripcion", SqlDbType.NVarChar, 1000).Value = (object)n.Descripcion ?? DBNull.Value;
                            cmd.Parameters.Add("@ambito", SqlDbType.NVarChar, 20).Value = carga.Ambito;
                            cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value = Recortar(usuario, 50);
                            cmd.Parameters.Add("@perfiles", SqlDbType.NVarChar, ReglasPerfilesNovedades.LONGITUD_COLUMNA).Value = (object)n.PerfilesColumna ?? DBNull.Value;
                            object id = cmd.ExecuteScalar();
                            if (id != null && id != DBNull.Value)
                            {
                                ids[n.Titulo] = (int)id;
                            }
                        }
                    }
                    if (ids.Count > 0)
                    {
                        AnotarEn(cn, tx, $"NovedadesVersion|{carga.Ambito}|{carga.Version}", JsonConvert.SerializeObject(new
                        {
                            carga.Version,
                            carga.Ambito,
                            Fecha = carga.Fecha.ToString("yyyy-MM-dd"),
                            Creadas = ids.Select(kv => new { Id = kv.Value, Titulo = kv.Key })
                        }), usuario);
                    }
                    tx.Commit();
                }
            }
            return ids;
        }

        public bool Actualizar(int id, CambioNovedadNormalizado c, string anterior, string usuario)
        {
            using (var cn = new SqlConnection(cadenaConexion))
            {
                cn.Open();
                using (SqlTransaction tx = cn.BeginTransaction())
                using (var cmd = new SqlCommand(@"
UPDATE dbo.Novedades SET
    Titulo = ISNULL(@titulo, Titulo),
    Descripcion = CASE WHEN @tocarDescripcion = 1 THEN NULLIF(@descripcion, N'') ELSE Descripcion END,
    Categoria = ISNULL(@categoria, Categoria),
    Perfiles = CASE WHEN @tocarPerfiles = 1 THEN @perfiles ELSE Perfiles END,
    Publicada = ISNULL(@publicada, Publicada),
    Usuario = @usuario,
    Fecha_Modificación = GETDATE()
WHERE Id = @id AND Version IS NOT NULL;", cn, tx))
                {
                    cmd.Parameters.Add("@titulo", SqlDbType.NVarChar, 200).Value = (object)c.Titulo ?? DBNull.Value;
                    cmd.Parameters.Add("@tocarDescripcion", SqlDbType.Bit).Value = c.Descripcion != null;
                    cmd.Parameters.Add("@descripcion", SqlDbType.NVarChar, 1000).Value = (object)c.Descripcion ?? DBNull.Value;
                    cmd.Parameters.Add("@categoria", SqlDbType.NVarChar, 20).Value = (object)c.Categoria ?? DBNull.Value;
                    cmd.Parameters.Add("@tocarPerfiles", SqlDbType.Bit).Value = c.TocarPerfiles;
                    cmd.Parameters.Add("@perfiles", SqlDbType.NVarChar, ReglasPerfilesNovedades.LONGITUD_COLUMNA).Value = (object)c.PerfilesColumna ?? DBNull.Value;
                    cmd.Parameters.Add("@publicada", SqlDbType.Bit).Value = (object)c.Publicada ?? DBNull.Value;
                    cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 50).Value = Recortar(usuario, 50);
                    cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                    if (cmd.ExecuteNonQuery() == 0)
                    {
                        return false;
                    }
                    AnotarEn(cn, tx, anterior, JsonConvert.SerializeObject(new { Id = id, Cambio = c }), usuario);
                    tx.Commit();
                    return true;
                }
            }
        }

        public bool Borrar(int id, string anterior, string usuario)
        {
            try
            {
                using (var cn = new SqlConnection(cadenaConexion))
                {
                    cn.Open();
                    using (SqlTransaction tx = cn.BeginTransaction())
                    using (var cmd = new SqlCommand(@"
DELETE n FROM dbo.Novedades n
WHERE n.Id = @id AND n.Version IS NOT NULL AND n.TextoOriginal IS NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.NovedadesComentarios c WHERE c.NovedadId = n.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.NovedadesVotos v WHERE v.NovedadId = n.Id);", cn, tx))
                    {
                        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                        if (cmd.ExecuteNonQuery() == 0)
                        {
                            return false;
                        }
                        AnotarEn(cn, tx, anterior, null, usuario);
                        tx.Commit();
                        return true;
                    }
                }
            }
            catch (SqlException ex) when (ex.Number == 547)
            {
                // Otra tabla la referencia (adjuntos, capturas, vídeo…): no se borra.
                throw new ErrorNovedadesVersion(HttpStatusCode.Conflict,
                    $"La novedad {id} tiene adjuntos, capturas o vídeo: no se borra. Si no debe salir, despublíquela (Publicada = false).");
            }
        }

        public void Anotar(string anterior, string nuevo, string usuario)
        {
            using (var cn = new SqlConnection(cadenaConexion))
            {
                cn.Open();
                AnotarEn(cn, null, anterior, nuevo, usuario);
            }
        }
    }

    /// <summary>NestoAPI#890: el núcleo de la carga, el cambio y el borrado (las reglas en <see cref="ReglasNovedadesVersion"/>).</summary>
    internal class ServicioNovedadesVersion
    {
        private readonly IRepositorioNovedadesVersion repositorio;
        private readonly IServicioNovedades novedades;
        private readonly Func<DateTime> hoy;

        internal ServicioNovedadesVersion(IRepositorioNovedadesVersion repositorio, IServicioNovedades novedades, Func<DateTime> hoy = null)
        {
            this.repositorio = repositorio;
            this.novedades = novedades;
            this.hoy = hoy ?? (() => DateTime.Today);
        }

        public ResultadoCargaNovedadesDTO Cargar(CargaNovedadesVersionDTO peticion, bool ensayo, string usuario)
        {
            string error = ReglasNovedadesVersion.Normalizar(peticion, hoy(), out CargaNormalizada carga);
            if (error != null)
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.BadRequest, error);
            }
            var resultado = new ResultadoCargaNovedadesDTO
            {
                Version = carga.Version,
                Ambito = carga.Ambito,
                Fecha = carga.Fecha,
                Ensayo = ensayo
            };

            // 1) Todo se comprueba antes de escribir nada.
            List<NovedadVersionFila> deLaVersion = repositorio.LeerDeVersion(carga.Version, carga.Ambito);
            var nuevas = new List<NovedadNormalizada>();
            var sugerenciasPorImplementar = new List<NovedadNormalizada>();
            foreach (NovedadNormalizada n in carga.Novedades)
            {
                if (n.SugerenciaId.HasValue)
                {
                    NovedadVersionFila fila = repositorio.Leer(n.SugerenciaId.Value);
                    bool ya = ReglasNovedadesVersion.SugerenciaYaImplementada(n, fila, carga.Version, out ErrorNovedadesVersion errorSugerencia);
                    if (errorSugerencia != null)
                    {
                        throw errorSugerencia;
                    }
                    if (ya)
                    {
                        resultado.YaEstaban.Add(Cargada(n, n.SugerenciaId));
                    }
                    else
                    {
                        sugerenciasPorImplementar.Add(n);
                    }
                }
                else if (ReglasNovedadesVersion.YaEsta(n, deLaVersion))
                {
                    NovedadVersionFila fila = deLaVersion.First(f => ReglasNovedadesVersion.ClaveTitulo(f.Titulo) == ReglasNovedadesVersion.ClaveTitulo(n.Titulo));
                    resultado.YaEstaban.Add(Cargada(n, fila.Id));
                }
                else
                {
                    nuevas.Add(n);
                }
            }

            if (ensayo)
            {
                resultado.Creadas.AddRange(nuevas.Select(n => Cargada(n, null)));
                resultado.SugerenciasImplementadas.AddRange(sugerenciasPorImplementar.Select(n => new NovedadCargadaDTO
                {
                    SugerenciaId = n.SugerenciaId,
                    Categoria = n.Categoria,
                    Titulo = n.Titulo
                }));
                resultado.Mensaje = Mensaje(resultado, true);
                return resultado;
            }

            // 2) Las nuevas, en una transacción con su rastro.
            if (nuevas.Count > 0)
            {
                Dictionary<string, int> ids = repositorio.Insertar(carga, nuevas, usuario);
                foreach (NovedadNormalizada n in nuevas)
                {
                    if (ids.TryGetValue(n.Titulo, out int id))
                    {
                        resultado.Creadas.Add(Cargada(n, id));
                    }
                    else
                    {
                        resultado.YaEstaban.Add(Cargada(n, null)); // la metió otra carga a la vez
                    }
                }
            }

            // 3) Las sugerencias, por el MISMO camino que PUT api/Novedades/Sugerencias/{id}.
            foreach (NovedadNormalizada n in sugerenciasPorImplementar)
            {
                var cambios = new ActualizarSugerenciaNovedadDTO
                {
                    Titulo = n.Titulo,
                    Descripcion = n.Descripcion,
                    Categoria = n.Categoria,
                    Version = carga.Version,
                    Ambito = carga.Ambito
                };
                string errorCambio = ReglasSugerenciasNovedades.Normalizar(cambios);
                if (errorCambio != null)
                {
                    throw new ErrorNovedadesVersion(HttpStatusCode.BadRequest, $"Sugerencia {n.SugerenciaId}: {errorCambio}.");
                }
                if (!novedades.ActualizarSugerencia(n.SugerenciaId.Value, cambios, usuario))
                {
                    throw new ErrorNovedadesVersion(HttpStatusCode.NotFound, $"La sugerencia {n.SugerenciaId} ya no existe.");
                }
                if (n.TraePerfiles)
                {
                    _ = novedades.GuardarPerfiles(n.SugerenciaId.Value, n.PerfilesColumna, usuario);
                }
                repositorio.Anotar($"SugerenciaImplementada|{n.SugerenciaId}", JsonConvert.SerializeObject(new
                {
                    Id = n.SugerenciaId,
                    carga.Version,
                    carga.Ambito,
                    n.Titulo,
                    n.Categoria,
                    Perfiles = n.TraePerfiles ? n.PerfilesColumna : "(sin tocar)"
                }), usuario);
                resultado.SugerenciasImplementadas.Add(Cargada(n, n.SugerenciaId));
            }

            resultado.Mensaje = Mensaje(resultado, false);
            return resultado;
        }

        private static NovedadCargadaDTO Cargada(NovedadNormalizada n, int? id) => new NovedadCargadaDTO
        {
            Id = id,
            SugerenciaId = n.SugerenciaId,
            Categoria = n.Categoria,
            Titulo = n.Titulo
        };

        internal static string Mensaje(ResultadoCargaNovedadesDTO r, bool ensayo)
        {
            string verbo = ensayo ? "Se crearían" : "Creadas";
            string sug = ensayo ? "se implementarían" : "implementadas";
            return $"{r.Ambito} {r.Version}: {verbo} {r.Creadas.Count}; ya estaban {r.YaEstaban.Count}; sugerencias {sug} {r.SugerenciasImplementadas.Count}.";
        }

        public List<NovedadVersionLeidaDTO> Leer(string version, string ambito)
        {
            string v = version?.Trim();
            if (string.IsNullOrEmpty(v) || v.Length > ReglasSugerenciasNovedades.LONGITUD_VERSION)
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.BadRequest, "Falta la versión (por ejemplo, 1.10.67.0).");
            }
            string a = null;
            if (!string.IsNullOrWhiteSpace(ambito))
            {
                a = ReglasSugerenciasNovedades.AMBITOS.FirstOrDefault(x => string.Equals(x, ambito.Trim(), StringComparison.OrdinalIgnoreCase));
                if (a == null)
                {
                    throw new ErrorNovedadesVersion(HttpStatusCode.BadRequest, "El ámbito tiene que ser " + string.Join(", ", ReglasSugerenciasNovedades.AMBITOS) + ".");
                }
            }
            return repositorio.LeerDeVersion(v, a).Select(f => f.ADto()).ToList();
        }

        public NovedadVersionLeidaDTO Cambiar(int id, CambioNovedadVersionDTO cambio, string usuario)
        {
            string error = ReglasNovedadesVersion.NormalizarCambio(cambio, out CambioNovedadNormalizado c);
            if (error != null)
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.BadRequest, error);
            }
            NovedadVersionFila fila = repositorio.Leer(id) ?? throw new ErrorNovedadesVersion(HttpStatusCode.NotFound, $"La novedad {id} no existe.");
            string motivo = ReglasNovedadesVersion.MotivoNoSePuedeCambiar(fila);
            if (motivo != null)
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.Conflict, motivo);
            }
            if (c.Titulo != null && ReglasNovedadesVersion.ClaveTitulo(c.Titulo) != ReglasNovedadesVersion.ClaveTitulo(fila.Titulo)
                && repositorio.LeerDeVersion(fila.Version, fila.Ambito).Any(f => f.Id != id && ReglasNovedadesVersion.ClaveTitulo(f.Titulo) == ReglasNovedadesVersion.ClaveTitulo(c.Titulo)))
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.Conflict, $"Ya hay otra novedad de {fila.Ambito} {fila.Version} con el título «{c.Titulo}».");
            }
            if (!repositorio.Actualizar(id, c, JsonConvert.SerializeObject(fila.ADto()), usuario))
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.NotFound, $"La novedad {id} no existe.");
            }
            return repositorio.Leer(id)?.ADto();
        }

        public void Borrar(int id, string usuario)
        {
            NovedadVersionFila fila = repositorio.Leer(id) ?? throw new ErrorNovedadesVersion(HttpStatusCode.NotFound, $"La novedad {id} no existe.");
            string motivo = ReglasNovedadesVersion.MotivoNoSePuedeBorrar(fila);
            if (motivo != null)
            {
                throw new ErrorNovedadesVersion(HttpStatusCode.Conflict, motivo);
            }
            if (!repositorio.Borrar(id, JsonConvert.SerializeObject(fila.ADto()), usuario))
            {
                // Entre leerla y borrarla alguien la ha comentado, votado o borrado.
                throw new ErrorNovedadesVersion(HttpStatusCode.Conflict, $"La novedad {id} ha cambiado mientras tanto (comentada, votada o ya borrada): vuelva a mirarla.");
            }
        }
    }
}
