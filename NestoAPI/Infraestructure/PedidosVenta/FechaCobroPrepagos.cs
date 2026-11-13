using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#860 (corte 1): reglas puras de la fecha real del cobro de un prepago (<c>Prepagos.FechaCobro</c>, date NULL).
    /// Null = como hoy (al contabilizar, la fecha de la factura) y, al modificar, no se toca lo guardado. Con hora, solo cuenta
    /// el día. Nunca puede ser posterior a hoy.
    /// </summary>
    public static class ReglasFechaCobroPrepago
    {
        public const string MENSAJE_FECHA_FUTURA = "La fecha de cobro del prepago no puede ser posterior a hoy.";

        /// <summary>Solo el día (sin hora); null sigue siendo null.</summary>
        public static DateTime? Normalizar(DateTime? fecha)
        {
            return fecha?.Date;
        }

        /// <summary>El motivo para rechazar los prepagos del pedido (400) o null si todos valen. Sin fecha, siempre valen.</summary>
        public static string MotivoRechazo(IEnumerable<PrepagoDTO> prepagos, DateTime hoy)
        {
            if (prepagos == null)
            {
                return null;
            }
            return prepagos.Any(p => p != null && Normalizar(p.FechaCobro) > hoy.Date) ? MENSAJE_FECHA_FUTURA : null;
        }
    }

    /// <summary>
    /// NestoAPI#860: las fechas de cobro que hay que escribir tras el SaveChanges del pedido. Se apunta la fila de Prepago (aún
    /// sin Id si es nueva) con su fecha; las que llegan sin fecha no se apuntan (no se toca lo guardado).
    /// </summary>
    public class FechasCobroPrepagosAGuardar
    {
        private readonly List<KeyValuePair<Prepago, DateTime>> pendientes = new List<KeyValuePair<Prepago, DateTime>>();

        public void Apuntar(Prepago prepago, DateTime? fechaCobro)
        {
            DateTime? fecha = ReglasFechaCobroPrepago.Normalizar(fechaCobro);
            if (prepago == null || !fecha.HasValue)
            {
                return;
            }
            pendientes.Add(new KeyValuePair<Prepago, DateTime>(prepago, fecha.Value));
        }

        public bool HayAlgo => pendientes.Count > 0;

        /// <summary>Por Id de la fila, ya guardada (las que siguen con Id 0 no se han guardado y se quedan fuera).</summary>
        public Dictionary<int, DateTime> PorId()
        {
            Dictionary<int, DateTime> resultado = new Dictionary<int, DateTime>();
            foreach (KeyValuePair<Prepago, DateTime> par in pendientes)
            {
                if (par.Key.Id > 0)
                {
                    resultado[par.Key.Id] = par.Value;
                }
            }
            return resultado;
        }
    }

    /// <summary>NestoAPI#860: la columna Prepagos.FechaCobro, fuera del EDMX.</summary>
    public interface IRepositorioFechaCobroPrepagos
    {
        /// <summary>¿Existe la columna (Scripts/Issue860_Prepagos_FechaCobro.sql lanzado)?</summary>
        bool ColumnaDisponible();
        /// <summary>Las fechas de cobro (no nulas) de los prepagos del pedido, por Id. Vacío sin columna.</summary>
        Dictionary<int, DateTime> Leer(string empresa, int pedido);
        /// <summary>Escribe las fechas por Id. No hace nada sin columna.</summary>
        void Guardar(IDictionary<int, DateTime> fechasPorId);
    }

    /// <summary>
    /// NestoAPI#860: la columna NO está en el EDMX a propósito (regla «EDMX a mano → validar metadatos»): si estuviera y el
    /// script no se hubiera lanzado, EF la pediría en todas las lecturas de Prepagos y tumbaría la API. Se lee y se escribe con
    /// SQL parametrizado, con el MISMO contexto de quien llama (dentro de un TransactionScope, como UnirPedidos, se alista en
    /// él sin abrir otra conexión), y antes se mira si existe (<see cref="ColumnasOpcionales"/>).
    /// </summary>
    public class RepositorioFechaCobroPrepagos : IRepositorioFechaCobroPrepagos
    {
        internal const string TABLA = "Prepagos";
        internal const string COLUMNA = "FechaCobro";

        internal const string SQL_LEER =
            "SELECT Id, FechaCobro FROM dbo.Prepagos WHERE Empresa = @empresa AND Pedido = @pedido AND FechaCobro IS NOT NULL";

        internal const string SQL_GUARDAR =
            "UPDATE dbo.Prepagos SET FechaCobro = @fecha WHERE Id = @id AND (FechaCobro IS NULL OR FechaCobro <> @fecha)";

        private readonly NVEntities db;

        public RepositorioFechaCobroPrepagos(NVEntities db)
        {
            this.db = db;
        }

        public bool ColumnaDisponible()
        {
            return ColumnasOpcionales.Existen(db, TABLA, COLUMNA);
        }

        public Dictionary<int, DateTime> Leer(string empresa, int pedido)
        {
            if (!ColumnaDisponible())
            {
                return new Dictionary<int, DateTime>();
            }
            return db.Database.SqlQuery<FilaFechaCobro>(SQL_LEER,
                    new SqlParameter("@empresa", empresa ?? string.Empty),
                    new SqlParameter("@pedido", pedido))
                .ToList()
                .Where(f => f.FechaCobro.HasValue)
                .ToDictionary(f => f.Id, f => f.FechaCobro.Value.Date);
        }

        public void Guardar(IDictionary<int, DateTime> fechasPorId)
        {
            if (fechasPorId == null || fechasPorId.Count == 0 || !ColumnaDisponible())
            {
                return;
            }
            foreach (KeyValuePair<int, DateTime> par in fechasPorId)
            {
                _ = db.Database.ExecuteSqlCommand(SQL_GUARDAR,
                    new SqlParameter("@fecha", System.Data.SqlDbType.Date) { Value = par.Value.Date },
                    new SqlParameter("@id", par.Key));
            }
        }

        public class FilaFechaCobro
        {
            public int Id { get; set; }
            public DateTime? FechaCobro { get; set; }
        }
    }
}
