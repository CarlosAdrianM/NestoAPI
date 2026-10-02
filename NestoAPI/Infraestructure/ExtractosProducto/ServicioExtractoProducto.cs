using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ExtractosProducto
{
    /// <summary>
    /// El dueño de PreExtrProducto y de prdExtrProducto, con la misma filosofía que ContabilidadService con
    /// PreContabilidad y prdContabilizar: crear las líneas del pre-extracto y contabilizar un diario. Las sobrecargas
    /// con <see cref="NVEntities"/> van dentro de la transacción del llamante.
    /// </summary>
    public interface IServicioExtractoProducto
    {
        /// <summary>Crea las líneas con su propio contexto y las guarda.</summary>
        Task<int> CrearLineas(List<PreExtrProducto> lineas);
        /// <summary>Las añade al contexto del llamante, que las guarda con el resto de sus cambios.</summary>
        Task<int> CrearLineas(NVEntities db, List<PreExtrProducto> lineas);
        Task<int> ContabilizarDiario(string empresa, string diario, string usuario);
        /// <summary>prdExtrProducto sobre la conexión y la transacción del llamante. Devuelve lo que devuelve el procedimiento.</summary>
        Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario);
        Task<int> CrearLineasYContabilizarDiario(List<PreExtrProducto> lineas);
        /// <summary>Dentro de la transacción del llamante: añade, guarda y contabiliza.</summary>
        Task<int> CrearLineasYContabilizarDiario(List<PreExtrProducto> lineas, NVEntities db);
    }

    /// <summary>
    /// Aquí vive la ÚNICA llamada de NestoAPI a prdExtrProducto (la usan los kits, las notas de entrega y la entrada
    /// de reposiciones) y los únicos PreExtrProductos.Add.
    /// </summary>
    public class ServicioExtractoProducto : IServicioExtractoProducto
    {
        private const int LONGITUD_TEXTO = 50;

        public async Task<int> CrearLineas(List<PreExtrProducto> lineas)
        {
            using (var db = new NVEntities())
            {
                int creadas = await CrearLineas(db, lineas).ConfigureAwait(false);
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return creadas;
            }
        }

        public Task<int> CrearLineas(NVEntities db, List<PreExtrProducto> lineas)
        {
            int creadas = 0;
            foreach (PreExtrProducto linea in lineas ?? new List<PreExtrProducto>())
            {
                if (linea.Texto != null && linea.Texto.Length > LONGITUD_TEXTO)
                {
                    linea.Texto = linea.Texto.Substring(0, LONGITUD_TEXTO);
                }
                _ = db.PreExtrProductos.Add(linea);
                creadas++;
            }
            return Task.FromResult(creadas);
        }

        public async Task<int> ContabilizarDiario(string empresa, string diario, string usuario)
        {
            using (var db = new NVEntities())
            {
                return await ContabilizarDiario(db, empresa, diario, usuario).ConfigureAwait(false);
            }
        }

        public async Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario)
        {
            // La regla de prdExtrProducto, adelantada con un mensaje que dice qué productos (el procedimiento solo dice
            // «No puede contabilizar una merma sin centro de coste»)
            bool esDiarioDeMermas = await db.DiariosProductos
                .AnyAsync(d => d.Empresa == empresa && d.Número == diario && d.Número.StartsWith("merma"))
                .ConfigureAwait(false);
            if (esDiarioDeMermas)
            {
                List<PreExtrProducto> lineas = await db.PreExtrProductos
                    .Where(p => p.Empresa == empresa && p.Diario == diario)
                    .ToListAsync()
                    .ConfigureAwait(false);
                string error = ErrorAntesDeContabilizar(esDiarioDeMermas, lineas);
                if (error != null)
                {
                    throw new Exception(error);
                }
            }

            var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = empresa };
            var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10) { Value = diario };
            var resultadoParametro = new SqlParameter { ParameterName = "@Resultado", SqlDbType = SqlDbType.Int, Direction = ParameterDirection.Output };
            try
            {
                // Sin usuario va NULL y el procedimiento pone SYSTEM_USER («if @Usuario is null ... set @Usuario =
                // SYSTEM_USER»): el comportamiento de siempre de kits y notas de entrega
                var usuarioParametro = new SqlParameter("@Usuario", SqlDbType.VarChar, 30)
                {
                    Value = string.IsNullOrWhiteSpace(usuario) ? (object)DBNull.Value : usuario
                };
                _ = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdExtrProducto @Empresa, @Diario, @Usuario",
                    resultadoParametro, empresaParametro, diarioParametro, usuarioParametro).ConfigureAwait(false);
                return resultadoParametro.Value is int resultado ? resultado : 0;
            }
            catch (SqlException ex)
            {
                throw new Exception(MensajeDeError(ex.Errors.Cast<SqlError>().Select(e => new KeyValuePair<int, string>(e.Number, e.Message))), ex);
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreExtrProducto> lineas)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int resultado = await CrearLineasYContabilizarDiario(lineas, db).ConfigureAwait(false);
                    if (resultado > 0)
                    {
                        transaccion.Commit();
                    }
                    else
                    {
                        transaccion.RollbackSeguro();
                    }
                    return resultado;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreExtrProducto> lineas, NVEntities db)
        {
            _ = await CrearLineas(db, lineas).ConfigureAwait(false);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return await ContabilizarDiario(db, lineas[0].Empresa, lineas[0].Diario, lineas[0].Usuario).ConfigureAwait(false);
        }

        /// <summary>Lo que prdExtrProducto rechazaría antes de empezar, con un mensaje que dice qué falta. Null si nada.</summary>
        internal static string ErrorAntesDeContabilizar(bool esDiarioDeMermas, IEnumerable<PreExtrProducto> lineas)
        {
            if (!esDiarioDeMermas)
            {
                return null;
            }
            List<string> sinCentro = (lineas ?? Enumerable.Empty<PreExtrProducto>())
                .Where(l => string.IsNullOrWhiteSpace(l.CentroCoste))
                .Select(l => l.Número?.Trim())
                .Distinct()
                .ToList();
            return sinCentro.Any()
                ? $"No puede contabilizar una merma sin centro de coste. Falta en: {string.Join(", ", sinCentro)}."
                : null;
        }

        /// <summary>El motivo del procedimiento sin el ruido de transacciones (el mismo filtro que prdContabilizar).</summary>
        internal static string MensajeDeError(IEnumerable<KeyValuePair<int, string>> errores)
        {
            return ContabilidadService.ComponerMensajeSinRuidoDeTransacciones(errores);
        }
    }
}
