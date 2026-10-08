using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// Una fila tal como la devuelve prdRellenarReposicionStock2 (mismos nombres de columna; SqlQuery mapea por
    /// nombre y el procedimiento devuelve más columnas que estas, que se ignoran).
    /// </summary>
    public class FilaPropuestaReposicion
    {
        public string Número { get; set; }
        public string Grupo { get; set; }
        public string Texto { get; set; }
        public short StockOrigen { get; set; }
        public short StockDestino { get; set; }
        public short CantidadMaximaDestino { get; set; }
        public short CantidadPendienteServirOrigen { get; set; }
        public short CantidadPendienteServirDestino { get; set; }
        public short CantidadReposicion { get; set; }
    }

    /// <summary>NestoAPI#553: una línea de la propuesta de reposición de una tienda, para Nesto.</summary>
    public class LineaPropuestaReposicionDTO
    {
        public string Producto { get; set; }
        public string Grupo { get; set; }
        public string Texto { get; set; }
        public int StockOrigen { get; set; }
        public int StockDestino { get; set; }
        public int StockMaximoDestino { get; set; }
        public int PendienteServirOrigen { get; set; }
        public int PendienteServirDestino { get; set; }
        public int CantidadReposicion { get; set; }
    }

    /// <summary>
    /// NestoAPI#553 (fase 1, parte de lectura): la propuesta de reposición entre dos almacenes, calculada por
    /// prdRellenarReposicionStock2, que SOLO calcula (trabaja con variables de tabla, no escribe nada).
    ///
    /// <para>NestoAPI#577 (corte 3a): antes era prdRellenarReposicionStock (el de Nesto viejo), que se negaba si en el
    /// destino había una reposición anterior sin recibir («No se puede rellenar porque hay una reposición anterior
    /// pendiente de contabilizar»: el 08/10/26 no dejó a Alfredo calcular Algete → Alcobendas con la 80915 en camino). El
    /// nuevo no tiene ese freno: cada reposición es independiente y lo que ya está en camino se cuenta (en el destino, lo
    /// que va hacia él; en el origen, lo comprometido para salir que aún no está en el extracto). Con <c>corte</c>, de los
    /// pedidos solo cuentan las líneas creadas antes de ese instante (el job del corte 3b); null = todas.</para>
    /// </summary>
    public class ServicioPropuestaReposicion
    {
        private static readonly string[] ALMACENES_REPOSICION =
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        private readonly Func<string, string, string, DateTime?, Task<List<FilaPropuestaReposicion>>> ejecutar;

        public ServicioPropuestaReposicion(NVEntities db)
            : this((empresa, origen, destino, corte) => ProcedimientoPropuestaReposicion.Leer<FilaPropuestaReposicion>(db, empresa, origen, destino, corte))
        {
        }

        /// <param name="ejecutar">Llama al procedimiento (empresa, origen, destino, corte). Sustituible en las pruebas.</param>
        internal ServicioPropuestaReposicion(Func<string, string, string, DateTime?, Task<List<FilaPropuestaReposicion>>> ejecutar)
        {
            this.ejecutar = ejecutar ?? throw new ArgumentNullException(nameof(ejecutar));
        }

        /// <summary>
        /// NestoAPI#600: el título de una reposición, «De Algete a Alcobendas», con los nombres de Almacenes (o el código
        /// si no lo hay). Lo usan GET api/Reposiciones/Ruta y la reposición creada / en preparación.
        /// </summary>
        public static string TituloReposicion(string origen, string nombreOrigen, string destino, string nombreDestino)
        {
            string de = string.IsNullOrWhiteSpace(nombreOrigen) ? origen?.Trim() : nombreOrigen.Trim();
            string a = string.IsNullOrWhiteSpace(nombreDestino) ? destino?.Trim() : nombreDestino.Trim();
            return $"De {de} a {a}";
        }

        /// <param name="corte">
        /// NestoAPI#577: de los pedidos pendientes, solo las líneas creadas antes de este instante
        /// (ISNULL(LinPedidoVta.FechaCreacion, [Fecha Modificación])). Null (el endpoint manual): todas.
        /// </param>
        public async Task<List<LineaPropuestaReposicionDTO>> CalcularPropuesta(string empresa, string origen, string destino, DateTime? corte = null)
        {
            string almacenOrigen = origen?.Trim().ToUpperInvariant();
            string almacenDestino = destino?.Trim().ToUpperInvariant();
            if (!ALMACENES_REPOSICION.Contains(almacenOrigen) || !ALMACENES_REPOSICION.Contains(almacenDestino))
            {
                throw new NestoBusinessException($"Los almacenes de una reposición tienen que ser {string.Join(", ", ALMACENES_REPOSICION)}.");
            }
            if (almacenOrigen == almacenDestino)
            {
                throw new NestoBusinessException("El almacén de origen y el de destino no pueden ser el mismo.");
            }

            List<FilaPropuestaReposicion> filas = await ejecutar(empresa?.Trim(), almacenOrigen, almacenDestino, corte).ConfigureAwait(false);
            return filas
                .Select(f => new LineaPropuestaReposicionDTO
                {
                    Producto = f.Número?.Trim(),
                    Grupo = f.Grupo?.Trim(),
                    Texto = f.Texto?.Trim(),
                    StockOrigen = f.StockOrigen,
                    StockDestino = f.StockDestino,
                    StockMaximoDestino = f.CantidadMaximaDestino,
                    PendienteServirOrigen = f.CantidadPendienteServirOrigen,
                    PendienteServirDestino = f.CantidadPendienteServirDestino,
                    CantidadReposicion = f.CantidadReposicion
                })
                .OrderBy(l => l.Producto, StringComparer.Ordinal)
                .ToList();
        }

    }

    /// <summary>
    /// NestoAPI#577 (corte 3a): el ÚNICO punto de llamada a prdRellenarReposicionStock2 (cada procedimiento, un solo
    /// sitio). Lo usan GET api/Reposiciones/Propuesta y POST api/Reposiciones sin líneas (vía
    /// <see cref="ServicioPropuestaReposicion"/>) y GET api/Traspasos/Propuesta (vía RepositorioTraspasos), cada uno con su
    /// fila: SqlQuery mapea por nombre de columna y el procedimiento devuelve las mismas columnas que el viejo. El viejo,
    /// prdRellenarReposicionStock, se queda para Nesto viejo: la API ya no lo llama.
    /// </summary>
    public static class ProcedimientoPropuestaReposicion
    {
        public const string PROCEDIMIENTO = "prdRellenarReposicionStock2";

        internal const string SQL = "EXEC " + PROCEDIMIENTO + " @Empresa, @AlmacenOrigen, @AlmacenDestino, @Corte";

        /// <summary>El procedimiento recorre con un cursor los pedidos pendientes: más que el tiempo por defecto.</summary>
        internal const int SEGUNDOS_MAXIMOS = 120;

        public static async Task<List<T>> Leer<T>(NVEntities db, string empresa, string origen, string destino, DateTime? corte)
        {
            int? tiempoAnterior = db.Database.CommandTimeout;
            db.Database.CommandTimeout = Math.Max(tiempoAnterior ?? 0, SEGUNDOS_MAXIMOS);
            try
            {
                // Parámetros siempre parametrizados (#553, seguridad): nunca concatenar
                return await db.Database.SqlQuery<T>(SQL, Parametros(empresa, origen, destino, corte)).ToListAsync().ConfigureAwait(false);
            }
            catch (SqlException ex) when (ComoErrorDeNegocio(ex.Number, ex.Message) != null)
            {
                throw ComoErrorDeNegocio(ex.Number, ex.Message);
            }
            finally
            {
                db.Database.CommandTimeout = tiempoAnterior;
            }
        }

        internal static SqlParameter[] Parametros(string empresa, string origen, string destino, DateTime? corte)
        {
            return new[]
            {
                new SqlParameter("@Empresa", SqlDbType.Char, 3) { Value = (object)empresa ?? DBNull.Value },
                new SqlParameter("@AlmacenOrigen", SqlDbType.Char, 3) { Value = (object)origen ?? DBNull.Value },
                new SqlParameter("@AlmacenDestino", SqlDbType.Char, 3) { Value = (object)destino ?? DBNull.Value },
                new SqlParameter("@Corte", SqlDbType.DateTime) { Value = corte.HasValue ? (object)corte.Value : DBNull.Value }
            };
        }

        /// <summary>
        /// Un RAISERROR del procedimiento (número 50000) es un aviso para el usuario, no un fallo: 400 con su texto y fuera
        /// de ELMAH. prdRellenarReposicionStock2 ya no tiene ninguno (el freno de «reposición anterior pendiente» era del
        /// viejo); se deja por si se añade alguno.
        /// </summary>
        internal static Exception ComoErrorDeNegocio(int numeroError, string mensaje)
        {
            return numeroError == 50000 ? new NestoBusinessException(mensaje) : null;
        }
    }
}
