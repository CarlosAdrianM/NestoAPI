using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#829: PUT api/Eventos/Inscripciones/{id}/FormaAsistencia.</summary>
    public class CambiarFormaAsistenciaInscripcionDTO
    {
        /// <summary>«Presencial» u «Online». Null o vacío: sin indicar (se quita).</summary>
        public string FormaAsistencia { get; set; }
    }

    /// <summary>
    /// NestoAPI#829 (Reina, sugerencia 1064; Carlos 05/11/26): en un evento mixto (presencial y online a la vez), cada
    /// inscripción dice si la clienta viene presencial u online. Null = sin indicar. Solo se apunta en los eventos mixtos: en uno
    /// presencial u online la forma es la del evento.
    /// </summary>
    public static class ReglasFormaAsistenciaEvento
    {
        public const string PRESENCIAL = FormatosEvento.PRESENCIAL;
        public const string ONLINE = FormatosEvento.ONLINE;
        public const int LONGITUD = 15;

        /// <summary>«online» → «Online»; null o vacío → null (sin indicar). Lanza si no es ninguna de las dos.</summary>
        public static string Interpretar(string texto)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (string.Equals(limpio, PRESENCIAL, StringComparison.OrdinalIgnoreCase))
            {
                return PRESENCIAL;
            }
            if (string.Equals(limpio, ONLINE, StringComparison.OrdinalIgnoreCase))
            {
                return ONLINE;
            }
            throw new NestoBusinessException("La forma de asistencia tiene que ser Presencial u Online (o ninguna, sin indicar).");
        }

        /// <summary>Lo guardado de una inscripción, como se enseña (null si no hay o no es válido).</summary>
        public static string De(int inscripcion, IReadOnlyDictionary<int, string> formas)
        {
            if (formas == null || !formas.TryGetValue(inscripcion, out string forma))
            {
                return null;
            }
            try
            {
                return Interpretar(forma);
            }
            catch (NestoBusinessException)
            {
                return null;
            }
        }

        /// <summary>Solo se apunta en los eventos mixtos (400 en el resto).</summary>
        public static void ComprobarEventoMixto(Evento evento)
        {
            if (!FormatosEvento.EsMixto(evento))
            {
                throw new NestoBusinessException($"«{evento?.Titulo?.Trim()}» no es un evento mixto: si viene presencial u online solo se " +
                    "apunta en los eventos presenciales y online a la vez. Cambie el formato del evento a «Mixto» si lo es.");
            }
        }

        public static NestoBusinessException FaltaScript() =>
            new NestoBusinessException("Todavía no se puede apuntar si viene presencial u online ni guardar eventos mixtos: falta lanzar el " +
                "script Scripts/Issue829_EventosInscripcionesModalidad.sql. Avise a Informática.");
    }

    /// <summary>
    /// NestoAPI#829: la columna EventosInscripciones.FormaAsistencia (Scripts/Issue829_EventosInscripcionesModalidad.sql), fuera
    /// del EDMX, como Asistentes (#811). Sin la columna: ninguna inscripción la tiene y guardar contesta false. El mismo script
    /// admite «Mixto» en Eventos.Formato, así que <see cref="Disponible"/> también dice si se puede guardar un evento mixto.
    /// </summary>
    public interface IRepositorioFormaAsistenciaEventos
    {
        /// <summary>¿Está la columna (y, con ella, el CHECK de Eventos.Formato con Mixto)?</summary>
        bool Disponible();
        /// <summary>Inscripción → forma de asistencia (solo las que la tienen).</summary>
        Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> inscripciones);
        /// <summary>Guarda la forma (null la quita). False si la columna todavía no existe.</summary>
        Task<bool> Poner(int inscripcion, string forma);
    }

    public class RepositorioFormaAsistenciaEventos : IRepositorioFormaAsistenciaEventos
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.EventosInscripciones', 'FormaAsistencia') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id, FormaAsistencia FROM dbo.EventosInscripciones WITH (NOLOCK) WHERE FormaAsistencia IS NOT NULL AND Id IN ({0})";
        internal const string SQL_PONER = "UPDATE dbo.EventosInscripciones SET FormaAsistencia = @forma WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columna = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private readonly NVEntities db;

        public RepositorioFormaAsistenciaEventos(NVEntities db)
        {
            this.db = db;
        }

        public bool Disponible() => columna.Existe(db);

        public async Task<Dictionary<int, string>> Leer(IReadOnlyCollection<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            var resultado = new Dictionary<int, string>();
            if (!ids.Any() || !columna.Existe(db))
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                foreach (FilaForma fila in await db.Database
                    .SqlQuery<FilaForma>(string.Format(SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false))
                {
                    resultado[fila.Id] = fila.FormaAsistencia?.Trim();
                }
            }
            return resultado;
        }

        public async Task<bool> Poner(int inscripcion, string forma)
        {
            if (!columna.Existe(db))
            {
                return false;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_PONER,
                new SqlParameter("@forma", SqlDbType.VarChar, ReglasFormaAsistenciaEvento.LONGITUD) { Value = (object)forma ?? DBNull.Value },
                new SqlParameter("@id", SqlDbType.Int) { Value = inscripcion }).ConfigureAwait(false);
            return true;
        }

        /// <summary>Para las listas: sin repositorio o si algo falla, ninguna la tiene (a ELMAH). Nunca rompe una lista.</summary>
        internal static async Task<Dictionary<int, string>> LeerSinFallar(IRepositorioFormaAsistenciaEventos repositorio, IEnumerable<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (repositorio == null || !ids.Any())
            {
                return new Dictionary<int, string>();
            }
            try
            {
                return await repositorio.Leer(ids).ConfigureAwait(false) ?? new Dictionary<int, string>();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Eventos #829] No se ha podido leer si los inscritos vienen presencial u online: {ex.Message}", ex));
                return new Dictionary<int, string>();
            }
        }

        private class FilaForma
        {
            public int Id { get; set; }
            public string FormaAsistencia { get; set; }
        }
    }
}
