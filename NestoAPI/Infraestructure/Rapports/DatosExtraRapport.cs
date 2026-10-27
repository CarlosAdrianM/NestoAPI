using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#716 (2.1): lee y escribe <c>SeguimientoCliente.Resultado</c> y <c>ProximoContacto</c>, columnas que añade
    /// Scripts/Issue716_DatosParaModelos.sql y que no están en el EDMX. Best-effort: si el script no se ha lanzado no hace
    /// nada; si la BD falla, el rapport ya está guardado y el fallo queda en ELMAH.
    /// </summary>
    public static class DatosExtraRapport
    {
        internal const string TABLA = "SeguimientoCliente";
        internal const string COLUMNA_RESULTADO = "Resultado";
        internal const string COLUMNA_PROXIMO_CONTACTO = "ProximoContacto";

        internal const string SQL_GUARDAR =
            "UPDATE dbo.SeguimientoCliente SET Resultado = @p1, ProximoContacto = @p2 WHERE NºOrden = @p0";

        /// <summary>Qué hay que guardar del DTO, o null si no viene nada (los clientes viejos no mandan ninguno de los dos).</summary>
        internal static bool HayAlgoQueGuardar(SeguimientoClienteDTO dto)
        {
            return dto != null && (!string.IsNullOrWhiteSpace(dto.Resultado) || dto.ProximoContacto.HasValue);
        }

        /// <summary>
        /// Guarda Resultado (normalizado al catálogo; se da por validado con <see cref="ResultadoRapport.MotivoParaNoGuardar"/>)
        /// y ProximoContacto (solo la fecha) del rapport <paramref name="numOrden"/>. Devuelve true si ha escrito.
        /// </summary>
        public static bool Guardar(NVEntities db, int numOrden, SeguimientoClienteDTO dto)
        {
            if (db == null || !HayAlgoQueGuardar(dto))
            {
                return false;
            }
            if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNA_RESULTADO, COLUMNA_PROXIMO_CONTACTO))
            {
                return false;
            }
            try
            {
                string resultado = ResultadoRapport.Normalizar(dto.Resultado);
                object proximo = dto.ProximoContacto.HasValue ? (object)dto.ProximoContacto.Value.Date : DBNull.Value;
                _ = db.Database.ExecuteSqlCommand(SQL_GUARDAR, numOrden, (object)resultado ?? DBNull.Value, proximo);
                return true;
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Rapports #716] No se ha podido guardar el resultado del rapport {numOrden}: {ex.Message}", ex));
                return false;
            }
        }

        /// <summary>Fila leída con SQL plano para completar los DTO de los GET.</summary>
        public class Extra
        {
            public int NumOrden { get; set; }
            public string Resultado { get; set; }
            public DateTime? ProximoContacto { get; set; }
        }

        /// <summary>
        /// Completa Resultado y ProximoContacto en los DTO que vienen del EDMX (que no conoce las columnas). Una sola
        /// consulta por lote; sin el script o con error, los DTO se devuelven tal cual.
        /// </summary>
        public static IList<SeguimientoClienteDTO> Completar(NVEntities db, IList<SeguimientoClienteDTO> rapports)
        {
            if (db == null || rapports == null || rapports.Count == 0)
            {
                return rapports;
            }
            if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNA_RESULTADO, COLUMNA_PROXIMO_CONTACTO))
            {
                return rapports;
            }
            try
            {
                foreach (var lote in Trocear(rapports.Select(r => r.Id).Distinct().ToList(), 500))
                {
                    string marcadores = string.Join(",", lote.Select((_, i) => "@p" + i));
                    List<Extra> extras = db.Database.SqlQuery<Extra>(
                        $"SELECT NºOrden AS NumOrden, Resultado, ProximoContacto FROM dbo.SeguimientoCliente WITH (NOLOCK) WHERE NºOrden IN ({marcadores})",
                        lote.Cast<object>().ToArray()).ToList();
                    Aplicar(rapports, extras);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Rapports #716] No se ha podido leer el resultado de los rapports: {ex.Message}", ex));
            }
            return rapports;
        }

        /// <summary>Pura: vuelca los extras en los DTO por Id. Un DTO sin fila se queda como está.</summary>
        internal static void Aplicar(IEnumerable<SeguimientoClienteDTO> rapports, IEnumerable<Extra> extras)
        {
            if (rapports == null || extras == null)
            {
                return;
            }
            Dictionary<int, Extra> porOrden = extras.GroupBy(e => e.NumOrden).ToDictionary(g => g.Key, g => g.First());
            foreach (SeguimientoClienteDTO dto in rapports)
            {
                if (porOrden.TryGetValue(dto.Id, out Extra extra))
                {
                    dto.Resultado = extra.Resultado?.Trim();
                    dto.ProximoContacto = extra.ProximoContacto;
                }
            }
        }

        private static IEnumerable<List<int>> Trocear(List<int> ids, int tamano)
        {
            for (int i = 0; i < ids.Count; i += tamano)
            {
                yield return ids.GetRange(i, Math.Min(tamano, ids.Count - i));
            }
        }
    }
}
