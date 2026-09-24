using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// Issue Nesto#372: la tabla Novedades no está en el EDMX a propósito; es una tabla satélite
    /// de solo lectura para la API y se consulta con SqlQuery para no tocar el designer.
    /// Las entradas se insertan por SQL al publicar cada versión (ver Scripts/Issue372_Novedades.sql).
    ///
    /// <para>NestoAPI#526: las sugerencias de los usuarios son filas SIN versión
    /// (Scripts/Issue526_SugerenciasNovedades.sql).</para>
    /// </summary>
    public class ServicioNovedades : IServicioNovedades
    {
        private const string COLUMNAS_SUGERENCIA =
            "Id, Version, Fecha, Categoria, Titulo, Descripcion, Ambito, TextoOriginal, SugeridaNombre, SugeridaFecha, Estado, " +
            "CAST(CASE WHEN Imagen IS NULL THEN 0 ELSE 1 END AS bit) AS TieneImagen";

        public List<NovedadDTO> LeerNovedadesPublicadas()
        {
            using (NVEntities db = new NVEntities())
            {
                // NestoAPI#526: sin versión es una sugerencia; nunca sale en el changelog ni en el popup
                return db.Database.SqlQuery<NovedadDTO>(
                    "SELECT Id, Version, Fecha, Categoria, Titulo, Descripcion, Ambito " +
                    "FROM Novedades WHERE Publicada = 1 AND Version IS NOT NULL").ToList();
            }
        }

        public List<NovedadConSugerenciaFila> LeerSugerencias(bool incluirCerradas)
        {
            string filtroEstado = incluirCerradas ? string.Empty : " AND Estado IN ('Pendiente', 'Aceptada')";
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<NovedadConSugerenciaFila>(
                    "SELECT " + COLUMNAS_SUGERENCIA + " FROM Novedades " +
                    "WHERE Publicada = 1 AND Version IS NULL" + filtroEstado).ToList();
            }
        }

        public int CrearSugerencia(SugerenciaNovedadAGrabar s)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<int>(@"
                    INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario,
                                           TextoOriginal, Imagen, ImagenTipo, SugeridaPor, SugeridaNombre, SugeridaFecha, Estado)
                    OUTPUT CAST(INSERTED.Id AS int)
                    VALUES (NULL, CAST(GETDATE() AS date), @categoria, @titulo, NULL, @ambito, 1, @usuarioCorto,
                            @texto, @imagen, @tipo, @usuario, @nombre, SYSDATETIME(), 'Pendiente');",
                    new SqlParameter("@categoria", SqlDbType.NVarChar, 20) { Value = ReglasSugerenciasNovedades.CATEGORIA_SUGERENCIA },
                    new SqlParameter("@titulo", SqlDbType.NVarChar, 200) { Value = s.Titulo },
                    new SqlParameter("@ambito", SqlDbType.NVarChar, 20) { Value = s.Ambito },
                    new SqlParameter("@usuarioCorto", SqlDbType.NVarChar, 50) { Value = Recortar(s.SugeridaPor, 50) },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 128) { Value = s.SugeridaPor },
                    new SqlParameter("@texto", SqlDbType.NVarChar, 2000) { Value = s.TextoOriginal },
                    new SqlParameter("@imagen", SqlDbType.VarBinary, -1) { Value = (object)s.Imagen ?? DBNull.Value },
                    new SqlParameter("@tipo", SqlDbType.VarChar, 30) { Value = (object)s.ImagenTipo ?? DBNull.Value },
                    new SqlParameter("@nombre", SqlDbType.NVarChar, 100) { Value = s.SugeridaNombre }).Single();
            }
        }

        public bool ActualizarSugerencia(int id, ActualizarSugerenciaNovedadDTO cambios, string usuario)
        {
            // Solo se tocan las sugerencias (TextoOriginal informado), y nunca el texto del usuario.
            // ISNULL: lo que llega a null se queda como estaba.
            using (NVEntities db = new NVEntities())
            {
                return db.Database.ExecuteSqlCommand(@"
                    UPDATE Novedades SET
                        Titulo = ISNULL(@titulo, Titulo),
                        Descripcion = ISNULL(@descripcion, Descripcion),
                        Estado = ISNULL(@estado, Estado),
                        Version = ISNULL(@version, Version),
                        Categoria = ISNULL(@categoria, Categoria),
                        Usuario = @usuario,
                        Fecha_Modificación = GETDATE()
                    WHERE Id = @id AND TextoOriginal IS NOT NULL;",
                    new SqlParameter("@titulo", SqlDbType.NVarChar, 200) { Value = (object)cambios.Titulo ?? DBNull.Value },
                    new SqlParameter("@descripcion", SqlDbType.NVarChar, 1000) { Value = (object)cambios.Descripcion ?? DBNull.Value },
                    new SqlParameter("@estado", SqlDbType.VarChar, 20) { Value = (object)cambios.Estado ?? DBNull.Value },
                    new SqlParameter("@version", SqlDbType.VarChar, 23) { Value = (object)cambios.Version ?? DBNull.Value },
                    new SqlParameter("@categoria", SqlDbType.NVarChar, 20) { Value = (object)cambios.Categoria ?? DBNull.Value },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) },
                    new SqlParameter("@id", id)) > 0;
            }
        }

        public ImagenComentarioNovedad LeerImagen(int novedadId)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<ImagenComentarioNovedad>(
                    "SELECT Imagen, ImagenTipo FROM Novedades WHERE Id = @id AND Publicada = 1 AND Imagen IS NOT NULL",
                    new SqlParameter("@id", novedadId)).FirstOrDefault();
            }
        }

        public List<NovedadConSugerenciaFila> Buscar(IReadOnlyList<string> palabras)
        {
            if (palabras == null || palabras.Count == 0)
            {
                return new List<NovedadConSugerenciaFila>();
            }
            // Cada palabra tiene que estar en el título, la descripción o el texto del usuario, sin
            // distinguir mayúsculas ni tildes («camion» encuentra «camión»).
            List<SqlParameter> parametros = new List<SqlParameter>();
            List<string> condiciones = new List<string>();
            for (int i = 0; i < palabras.Count; i++)
            {
                string nombre = "@p" + i;
                parametros.Add(new SqlParameter(nombre, SqlDbType.NVarChar, 200) { Value = ReglasSugerenciasNovedades.PatronLike(palabras[i]) });
                condiciones.Add("(Titulo COLLATE Latin1_General_CI_AI LIKE " + nombre +
                                " OR ISNULL(Descripcion, N'') COLLATE Latin1_General_CI_AI LIKE " + nombre +
                                " OR ISNULL(TextoOriginal, N'') COLLATE Latin1_General_CI_AI LIKE " + nombre + ")");
            }
            string sql = "SELECT TOP (" + ReglasSugerenciasNovedades.MAXIMO_RESULTADOS + ") " + COLUMNAS_SUGERENCIA +
                         " FROM Novedades WHERE Publicada = 1 AND " + string.Join(" AND ", condiciones) +
                         " ORDER BY Fecha DESC, Id DESC";
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<NovedadConSugerenciaFila>(sql, parametros.ToArray()).ToList();
            }
        }

        private static string Recortar(string texto, int longitud)
        {
            string limpio = (texto ?? string.Empty).Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
