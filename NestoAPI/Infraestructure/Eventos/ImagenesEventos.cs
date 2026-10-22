using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Novedades;
using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#690: la imagen ya validada, lista para grabar.</summary>
    public class ImagenEventoAGrabar
    {
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public byte[] Contenido { get; set; }
    }

    /// <summary>NestoAPI#690: la imagen guardada (con el contenido).</summary>
    public class ImagenEvento
    {
        public int EventoId { get; set; }
        public string Nombre { get; set; }
        public string Tipo { get; set; }
        public byte[] Contenido { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#690: la fecha de la imagen de cada evento (sin el contenido).</summary>
    public class FechaImagenEvento
    {
        public int EventoId { get; set; }
        public DateTime Fecha { get; set; }
    }

    /// <summary>NestoAPI#690: EventosImagenes (fuera del EDMX, como NovedadesAdjuntos de #616). Una imagen por evento.</summary>
    public interface IRepositorioImagenesEventos
    {
        /// <summary>Qué eventos tienen imagen y de cuándo es, en una consulta. Vacío si la tabla aún no existe.</summary>
        Task<Dictionary<int, DateTime>> LeerFechas(IEnumerable<int> eventos);
        /// <summary>La imagen, o null si no tiene (o si la tabla aún no existe).</summary>
        Task<ImagenEvento> Leer(int eventoId);
        /// <summary>Sustituye la que hubiera. Devuelve la fecha nueva (va en la URL para que no se vea la vieja de caché).</summary>
        Task<DateTime> Guardar(int eventoId, ImagenEventoAGrabar imagen, string usuario);
        /// <summary>False si no tenía.</summary>
        Task<bool> Borrar(int eventoId);
    }

    /// <summary>
    /// NestoAPI#690: qué imagen se puede poner a un evento. Las mismas reglas que los adjuntos de Novedades (#616): tipo y
    /// extensión que digan lo mismo, hasta 10 MB; pero solo imágenes (PNG, JPEG, GIF o WebP), sin PDF.
    /// </summary>
    public static class ReglasImagenesEventos
    {
        public const string TIPOS_PERMITIDOS_TEXTO = "PNG, JPEG, GIF o WebP";

        public class Validacion
        {
            public ImagenEventoAGrabar Imagen { get; set; }
            public string Error { get; set; }
            /// <summary>415 en vez de 400.</summary>
            public bool TipoNoPermitido { get; set; }
        }

        public static Validacion Validar(IEnumerable<(string Nombre, string Tipo, byte[] Contenido)> ficheros)
        {
            List<(string Nombre, string Tipo, byte[] Contenido)> lista = (ficheros ?? Enumerable.Empty<(string, string, byte[])>()).ToList();
            if (lista.Count > 1)
            {
                return new Validacion { Error = "Un evento lleva una sola imagen: mande solo un fichero." };
            }
            ValidacionAdjuntosNovedad validacion = ReglasAdjuntosNovedades.Validar(lista);
            if (validacion.Error != null)
            {
                return new Validacion
                {
                    Error = validacion.Error.Replace(ReglasAdjuntosNovedades.TIPOS_PERMITIDOS_TEXTO, TIPOS_PERMITIDOS_TEXTO)
                        .Replace("Envíalo como", "Envíelo como"),
                    TipoNoPermitido = validacion.TipoNoPermitido
                };
            }
            AdjuntoNovedadAGrabar adjunto = validacion.Adjuntos.Single();
            if (adjunto.Tipo == ReglasAdjuntosNovedades.TIPO_PDF)
            {
                return new Validacion
                {
                    Error = $"«{adjunto.Nombre}» no es una imagen: la imagen del evento tiene que ser {TIPOS_PERMITIDOS_TEXTO}.",
                    TipoNoPermitido = true
                };
            }
            return new Validacion { Imagen = new ImagenEventoAGrabar { Nombre = adjunto.Nombre, Tipo = adjunto.Tipo, Contenido = adjunto.Contenido } };
        }
    }

    /// <summary>
    /// NestoAPI#690: SQL parametrizado sobre EventosImagenes (Scripts/Issue690_EventosHorarioModalidadAsistencia.sql). Las
    /// lecturas comprueban antes que la tabla exista, para que la API se pueda publicar antes que el script sin que fallen
    /// los eventos.
    /// </summary>
    public class RepositorioImagenesEventos : IRepositorioImagenesEventos
    {
        public const string RUTA = "api/Eventos/{0}/Imagen";
        private const string SI_EXISTE_LA_TABLA = "IF OBJECT_ID(N'dbo.EventosImagenes', N'U') IS NOT NULL ";

        private readonly NVEntities db;

        public RepositorioImagenesEventos(NVEntities db)
        {
            this.db = db;
        }

        /// <summary>
        /// La URL pública de la imagen: https://api.nuevavision.es/api/Eventos/5/Imagen?v=20261022101500. La «v» cambia al
        /// sustituirla, para que ni el navegador ni el correo enseñen la vieja.
        /// </summary>
        public static string Url(int eventoId, DateTime fecha, string urlBase = null) =>
            $"{(string.IsNullOrWhiteSpace(urlBase) ? MiniaturasChequeRegalo.UrlPublicaApi : urlBase.Trim().TrimEnd('/'))}/" +
            string.Format(CultureInfo.InvariantCulture, RUTA, eventoId) + "?v=" + fecha.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        public async Task<Dictionary<int, DateTime>> LeerFechas(IEnumerable<int> eventos)
        {
            List<int> ids = (eventos ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, DateTime>();
            }
            // Los ids son int: se pueden componer en el IN sin riesgo de inyección.
            string sql = SI_EXISTE_LA_TABLA +
                "SELECT EventoId, Fecha FROM dbo.EventosImagenes WHERE EventoId IN (" + string.Join(",", ids) + ") " +
                "ELSE SELECT CAST(0 AS int) AS EventoId, CAST(NULL AS datetime) AS Fecha WHERE 1 = 0";
            List<FechaImagenEvento> filas = await db.Database.SqlQuery<FechaImagenEvento>(sql).ToListAsync().ConfigureAwait(false);
            return filas.GroupBy(f => f.EventoId).ToDictionary(g => g.Key, g => g.First().Fecha);
        }

        public async Task<ImagenEvento> Leer(int eventoId) =>
            await db.Database.SqlQuery<ImagenEvento>(SI_EXISTE_LA_TABLA +
                "SELECT EventoId, Nombre, Tipo, Contenido, Fecha FROM dbo.EventosImagenes WHERE EventoId = @id " +
                "ELSE SELECT CAST(0 AS int) AS EventoId, CAST(NULL AS nvarchar(200)) AS Nombre, CAST(NULL AS varchar(100)) AS Tipo, " +
                "CAST(NULL AS varbinary(max)) AS Contenido, CAST(NULL AS datetime) AS Fecha WHERE 1 = 0",
                new SqlParameter("@id", eventoId)).FirstOrDefaultAsync().ConfigureAwait(false);

        public async Task<DateTime> Guardar(int eventoId, ImagenEventoAGrabar imagen, string usuario)
        {
            // UPDLOCK + HOLDLOCK: dos subidas a la vez al mismo evento no chocan con la clave primaria
            return await db.Database.SqlQuery<DateTime>(@"
                SET XACT_ABORT ON;
                BEGIN TRAN;
                DECLARE @fecha datetime = GETDATE();
                IF EXISTS (SELECT 1 FROM dbo.EventosImagenes WITH (UPDLOCK, HOLDLOCK) WHERE EventoId = @evento)
                    UPDATE dbo.EventosImagenes SET Nombre = @nombre, Tipo = @tipo, Tamano = @tamano, Contenido = @contenido,
                        Usuario = @usuario, Fecha = @fecha WHERE EventoId = @evento;
                ELSE
                    INSERT INTO dbo.EventosImagenes (EventoId, Nombre, Tipo, Tamano, Contenido, Usuario, Fecha)
                    VALUES (@evento, @nombre, @tipo, @tamano, @contenido, @usuario, @fecha);
                COMMIT;
                SELECT @fecha;",
                new SqlParameter("@evento", eventoId),
                new SqlParameter("@nombre", SqlDbType.NVarChar, 200) { Value = imagen.Nombre },
                new SqlParameter("@tipo", SqlDbType.VarChar, 100) { Value = imagen.Tipo },
                new SqlParameter("@tamano", imagen.Contenido.Length),
                new SqlParameter("@contenido", SqlDbType.VarBinary, -1) { Value = imagen.Contenido },
                new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = (object)Recortar(usuario, 50) ?? DBNull.Value })
                .SingleAsync().ConfigureAwait(false);
        }

        public async Task<bool> Borrar(int eventoId) =>
            await db.Database.ExecuteSqlCommandAsync(SI_EXISTE_LA_TABLA + "DELETE FROM dbo.EventosImagenes WHERE EventoId = @id",
                new SqlParameter("@id", eventoId)).ConfigureAwait(false) > 0;

        private static string Recortar(string texto, int longitud)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string limpio = texto.Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
