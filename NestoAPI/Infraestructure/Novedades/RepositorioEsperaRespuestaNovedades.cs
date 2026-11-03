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
    /// Recordatorios de respuesta (Carlos, 16/10/26): la espera de respuesta del autor en las sugerencias e
    /// incidencias (filas de Novedades sin versión). Columnas de Scripts/NovedadesRecordatoriosRespuesta.sql.
    /// </summary>
    public interface IRepositorioEsperaRespuestaNovedades
    {
        /// <summary>La sugerencia con su espera, o null si no existe o ya tiene versión.</summary>
        EsperaRespuestaNovedad Leer(int novedadId);

        /// <summary>Las abiertas (Pendiente o Aceptada), sin versión, que esperan respuesta del autor.</summary>
        List<EsperaRespuestaNovedad> LeerEsperando();

        /// <summary>
        /// Empieza (o vuelve a empezar) a esperar: fecha de la pregunta y recordatorios a 0. NestoAPI#783:
        /// <paramref name="esperandoDe"/>, los @mencionados en la pregunta (<see cref="ReglasEsperaRespuestaNovedades.SerializarEsperados"/>);
        /// null = al autor. false si no es una sugerencia.
        /// </summary>
        bool MarcarEspera(int novedadId, DateTime desde, string esperandoDe);

        /// <summary>Ha contestado quien se esperaba: fuera la espera y, si estaba Cerrada, vuelve a Pendiente.</summary>
        void BorrarEspera(int novedadId, bool reabrir, string usuario);

        void GuardarRecordatorio(int novedadId, byte recordatorios, DateTime fecha);

        /// <summary>
        /// Sin respuesta tras el 2.º recordatorio: pasa a Cerrada y deja de esperar. Guarda a quién se esperaba
        /// (EsperandoRespuestaDe) para que, si esa persona escribe, se reabra.
        /// </summary>
        void Cerrar(int novedadId, DateTime fecha);
    }

    public class RepositorioEsperaRespuestaNovedadesSql : IRepositorioEsperaRespuestaNovedades
    {
        private const string COLUMNAS =
            "Id, Version, Estado, Categoria, Ambito, Titulo, SugeridaPor, SugeridaNombre, " +
            "EsperandoRespuestaDesde, RecordatoriosRespuesta, UltimoRecordatorioRespuesta, EsperandoRespuestaDe";

        public EsperaRespuestaNovedad Leer(int novedadId)
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<EsperaRespuestaNovedad>(
                    "SELECT " + COLUMNAS + " FROM Novedades WHERE Id = @id AND Version IS NULL AND Publicada = 1",
                    new SqlParameter("@id", novedadId)).FirstOrDefault();
            }
        }

        public List<EsperaRespuestaNovedad> LeerEsperando()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<EsperaRespuestaNovedad>(
                    "SELECT " + COLUMNAS + " FROM Novedades " +
                    "WHERE EsperandoRespuestaDesde IS NOT NULL AND Version IS NULL AND Publicada = 1 " +
                    "AND Estado IN ('Pendiente', 'Aceptada') ORDER BY Id").ToList();
            }
        }

        public bool MarcarEspera(int novedadId, DateTime desde, string esperandoDe)
        {
            using (var db = new NVEntities())
            {
                return db.Database.ExecuteSqlCommand(@"
                    UPDATE Novedades SET
                        EsperandoRespuestaDesde = @desde,
                        RecordatoriosRespuesta = 0,
                        UltimoRecordatorioRespuesta = NULL,
                        EsperandoRespuestaDe = @de
                    WHERE Id = @id AND Version IS NULL AND TextoOriginal IS NOT NULL;",
                    new SqlParameter("@desde", SqlDbType.DateTime) { Value = desde },
                    new SqlParameter("@de", SqlDbType.NVarChar, ReglasEsperaRespuestaNovedades.LONGITUD_ESPERANDO_RESPUESTA_DE)
                    {
                        Value = string.IsNullOrWhiteSpace(esperandoDe) ? (object)DBNull.Value : esperandoDe
                    },
                    new SqlParameter("@id", novedadId)) > 0;
            }
        }

        public void BorrarEspera(int novedadId, bool reabrir, string usuario)
        {
            using (var db = new NVEntities())
            {
                _ = db.Database.ExecuteSqlCommand(@"
                    UPDATE Novedades SET
                        EsperandoRespuestaDesde = NULL,
                        RecordatoriosRespuesta = 0,
                        UltimoRecordatorioRespuesta = NULL,
                        EsperandoRespuestaDe = NULL,
                        Estado = CASE WHEN @reabrir = 1 AND Estado = 'Cerrada' THEN 'Pendiente' ELSE Estado END,
                        Usuario = CASE WHEN @reabrir = 1 AND Estado = 'Cerrada' THEN @usuario ELSE Usuario END,
                        Fecha_Modificación = CASE WHEN @reabrir = 1 AND Estado = 'Cerrada' THEN GETDATE() ELSE Fecha_Modificación END
                    WHERE Id = @id AND Version IS NULL;",
                    new SqlParameter("@reabrir", SqlDbType.Bit) { Value = reabrir },
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) },
                    new SqlParameter("@id", novedadId));
            }
        }

        public void GuardarRecordatorio(int novedadId, byte recordatorios, DateTime fecha)
        {
            using (var db = new NVEntities())
            {
                _ = db.Database.ExecuteSqlCommand(@"
                    UPDATE Novedades SET RecordatoriosRespuesta = @n, UltimoRecordatorioRespuesta = @fecha
                    WHERE Id = @id AND EsperandoRespuestaDesde IS NOT NULL;",
                    new SqlParameter("@n", SqlDbType.TinyInt) { Value = recordatorios },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha },
                    new SqlParameter("@id", novedadId));
            }
        }

        public void Cerrar(int novedadId, DateTime fecha)
        {
            using (var db = new NVEntities())
            {
                _ = db.Database.ExecuteSqlCommand(@"
                    UPDATE Novedades SET
                        Estado = 'Cerrada',
                        EsperandoRespuestaDesde = NULL,
                        RecordatoriosRespuesta = 0,
                        UltimoRecordatorioRespuesta = NULL,
                        Usuario = @usuario,
                        Fecha_Modificación = @fecha
                    WHERE Id = @id AND Version IS NULL AND Estado IN ('Pendiente', 'Aceptada');",
                    new SqlParameter("@usuario", SqlDbType.NVarChar, 50) { Value = ReglasFeedbackNovedades.USUARIO_ASISTENTE },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha },
                    new SqlParameter("@id", novedadId));
            }
        }

        private static string Recortar(string texto, int longitud)
        {
            string limpio = (texto ?? string.Empty).Trim();
            return limpio.Length <= longitud ? limpio : limpio.Substring(0, longitud);
        }
    }
}
