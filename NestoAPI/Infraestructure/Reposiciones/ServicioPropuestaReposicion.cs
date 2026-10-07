using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Reposiciones
{
    /// <summary>
    /// Una fila tal como la devuelve prdRellenarReposicionStock (mismos nombres de columna; SqlQuery mapea por
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
    /// NestoAPI#553 (fase 1, parte de lectura): la propuesta de reposición entre dos almacenes. El cálculo sigue
    /// siendo el de Nesto viejo, prdRellenarReposicionStock, que SOLO calcula (trabaja con variables de tabla, no
    /// escribe nada) y se niega si en el destino hay una reposición anterior sin contabilizar. Escribir el
    /// traspaso (PreExtrProducto, contador compartido) es el siguiente corte.
    /// </summary>
    public class ServicioPropuestaReposicion
    {
        private static readonly string[] ALMACENES_REPOSICION =
        {
            Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS
        };

        private readonly Func<string, string, string, Task<List<FilaPropuestaReposicion>>> ejecutar;

        public ServicioPropuestaReposicion(NVEntities db) : this((empresa, origen, destino) => EjecutarProcedimiento(db, empresa, origen, destino))
        {
        }

        /// <param name="ejecutar">Llama al procedimiento (empresa, origen, destino). Sustituible en las pruebas.</param>
        internal ServicioPropuestaReposicion(Func<string, string, string, Task<List<FilaPropuestaReposicion>>> ejecutar)
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

        public async Task<List<LineaPropuestaReposicionDTO>> CalcularPropuesta(string empresa, string origen, string destino)
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

            List<FilaPropuestaReposicion> filas = await ejecutar(empresa?.Trim(), almacenOrigen, almacenDestino).ConfigureAwait(false);
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

        private static async Task<List<FilaPropuestaReposicion>> EjecutarProcedimiento(NVEntities db, string empresa, string origen, string destino)
        {
            try
            {
                return await db.Database
                    .SqlQuery<FilaPropuestaReposicion>("EXEC prdRellenarReposicionStock @p0, @p1, @p2", empresa, origen, destino)
                    .ToListAsync().ConfigureAwait(false);
            }
            catch (SqlException ex) when (ComoErrorDeNegocio(ex.Number, ex.Message) != null)
            {
                throw ComoErrorDeNegocio(ex.Number, ex.Message);
            }
        }

        /// <summary>
        /// Los RAISERROR del procedimiento (número 50000, p. ej. «hay una reposición anterior pendiente de
        /// contabilizar») son avisos para el usuario, no fallos: 400 con su texto y fuera de ELMAH.
        /// </summary>
        internal static Exception ComoErrorDeNegocio(int numeroError, string mensaje)
        {
            return numeroError == 50000 ? new NestoBusinessException(mensaje) : null;
        }
    }
}
