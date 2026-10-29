using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#742: interruptor «el reembolso de las agencias de fuera se contabiliza al ENTREGAR, no al tramitar»,
    /// <c>Agencias:ReembolsoAlEntregar</c> en Web.config. Solo "true" enciende. Apagado (o sin el script
    /// Scripts/Issue742_ReembolsosAlEntregar.sql), TramitarAsync contabiliza al tramitar como siempre.
    ///
    /// Apagarlo solo cambia lo que se tramite a partir de ese momento: los envíos que se tramitaron con él encendido siguen
    /// pendientes en la tabla y se contabilizan al entregarse igual (si no, ese reembolso no se contabilizaría nunca).
    /// </summary>
    public static class InterruptorReembolsoAlEntregar
    {
        public const string CLAVE = "Agencias:ReembolsoAlEntregar";

        /// <summary>Lee el Web.config en cada llamada.</summary>
        public static bool EstaActivo() => EstaActivo(ConfigurationManager.AppSettings[CLAVE]);

        public static bool EstaActivo(string valor) => string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>NestoAPI#742: la fila de un envío en EnviosAgenciaReembolsosAlEntregar.</summary>
    public class RegistroReembolsoAlEntregar
    {
        public int EnvioId { get; set; }
        /// <summary>Null = pendiente de contabilizar (se tramitó con el reembolso diferido y aún no se ha entregado).</summary>
        public int? Asiento { get; set; }
        /// <summary>La fecha contable del apunte (la de <c>PreContabilidad.Fecha</c>), para buscarlo al modificar el reembolso.</summary>
        public DateTime? FechaContable { get; set; }

        public bool Contabilizado => Asiento.HasValue;
    }

    /// <summary>
    /// NestoAPI#742: el control de «este envío tiene el reembolso diferido y ¿está ya contabilizado?». Va en una tabla propia
    /// (fuera del EDMX, por SQL y comprobando que existe) porque ni EnviosAgencia ni el asiento lo dicen de forma fiable: el
    /// apunte solo se reconoce por concepto, fecha e importe, y el concepto cambia si cambia el nombre de la agencia.
    ///
    /// La fila se crea al tramitar SOLO si el reembolso se difiere. Un envío sin fila es un envío de antes del cambio (o de
    /// la 14, o tramitado con el interruptor apagado): su reembolso ya se contabilizó al tramitar y NUNCA se vuelve a
    /// contabilizar. Así la decisión 1 de Carlos (los ya tramitados no se contabilizan otra vez) no depende de fechas.
    /// </summary>
    public interface IRegistroReembolsosAlEntregar
    {
        /// <summary>¿Existe la tabla (está aplicado el script)?</summary>
        bool TablaDisponible();

        /// <summary>¿Se difiere el reembolso de lo que se tramite ahora? Interruptor encendido Y tabla disponible.</summary>
        bool Activo();

        /// <summary>La fila del envío, o null si no tiene (o no hay tabla).</summary>
        Task<RegistroReembolsoAlEntregar> LeerAsync(int envio);

        /// <summary>Crea la fila pendiente (si no existe ya). Dentro de la transacción de tramitar.</summary>
        Task MarcarPendienteAsync(int envio, string usuario);

        /// <summary>
        /// Reserva el envío para contabilizarlo: pasa la fila de pendiente a «en curso» si y solo si seguía pendiente. Dentro
        /// de la transacción del asiento, así que el bloqueo de la fila dura hasta el commit: otra pasada simultánea espera y
        /// después ya no la encuentra pendiente. Devuelve false si no estaba pendiente (ya contabilizado o sin fila).
        /// </summary>
        Task<bool> ReservarAsync(int envio);

        /// <summary>Apunta el asiento, el importe y la fecha contable. Dentro de la misma transacción.</summary>
        Task MarcarContabilizadoAsync(int envio, int asiento, decimal importe, DateTime fechaContable, string usuario);

        /// <summary>Los envíos con el reembolso pendiente de contabilizar (todos; son pocos: los que están en vuelo).</summary>
        Task<HashSet<int>> LeerPendientesAsync();

        /// <summary>Los envíos ya ENTREGADOS cuyo reembolso diferido sigue pendiente: lo que el seguimiento tiene que contabilizar.</summary>
        Task<List<int>> LeerPendientesEntregadosAsync();
    }

    public class RegistroReembolsosAlEntregar : IRegistroReembolsosAlEntregar
    {
        public const string TABLA = "EnviosAgenciaReembolsosAlEntregar";

        /// <summary>Valor de Asiento mientras se contabiliza (la reserva). Si llegara a quedar grabado, cuenta como contabilizado: nunca se duplica.</summary>
        internal const int ASIENTO_EN_CURSO = 0;

        private const int LONGITUD_USUARIO = 50;

        private readonly NVEntities db;

        public RegistroReembolsosAlEntregar(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool TablaDisponible() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public bool Activo() => InterruptorReembolsoAlEntregar.EstaActivo() && TablaDisponible();

        public async Task<RegistroReembolsoAlEntregar> LeerAsync(int envio)
        {
            if (!TablaDisponible())
            {
                return null;
            }
            List<RegistroReembolsoAlEntregar> filas = await db.Database.SqlQuery<RegistroReembolsoAlEntregar>(
                $"SELECT EnvioId, Asiento, FechaContable FROM dbo.{TABLA} WHERE EnvioId = @p0", envio)
                .ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public Task MarcarPendienteAsync(int envio, string usuario)
            => db.Database.ExecuteSqlCommandAsync(
                $"IF NOT EXISTS (SELECT 1 FROM dbo.{TABLA} WITH (UPDLOCK, HOLDLOCK) WHERE EnvioId = @p0) " +
                $"INSERT INTO dbo.{TABLA} (EnvioId, UsuarioTramitacion, FechaTramitacion) VALUES (@p0, @p1, GETDATE())",
                envio, Recortar(usuario));

        public async Task<bool> ReservarAsync(int envio)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(
                $"UPDATE dbo.{TABLA} SET Asiento = @p1 WHERE EnvioId = @p0 AND Asiento IS NULL", envio, ASIENTO_EN_CURSO)
                .ConfigureAwait(false);
            return filas == 1;
        }

        public Task MarcarContabilizadoAsync(int envio, int asiento, decimal importe, DateTime fechaContable, string usuario)
            => db.Database.ExecuteSqlCommandAsync(
                $"UPDATE dbo.{TABLA} SET Asiento = @p1, Importe = @p2, FechaContable = @p3, UsuarioContabilizacion = @p4, " +
                "FechaContabilizacion = GETDATE() WHERE EnvioId = @p0",
                envio, asiento, importe, fechaContable, Recortar(usuario));

        public async Task<HashSet<int>> LeerPendientesAsync()
        {
            if (!TablaDisponible())
            {
                return new HashSet<int>();
            }
            List<int> envios = await db.Database.SqlQuery<int>($"SELECT EnvioId FROM dbo.{TABLA} WHERE Asiento IS NULL")
                .ToListAsync().ConfigureAwait(false);
            return new HashSet<int>(envios);
        }

        public async Task<List<int>> LeerPendientesEntregadosAsync()
        {
            if (!TablaDisponible())
            {
                return new List<int>();
            }
            return await db.Database.SqlQuery<int>(
                $"SELECT r.EnvioId FROM dbo.{TABLA} r INNER JOIN dbo.EnviosAgencia e ON e.Numero = r.EnvioId " +
                "WHERE r.Asiento IS NULL AND e.Estado = @p0 AND e.Reembolso > 0 AND e.FechaPagoReembolso IS NULL ORDER BY r.EnvioId",
                Constantes.Agencias.ESTADO_ENTREGADO)
                .ToListAsync().ConfigureAwait(false);
        }

        private static string Recortar(string usuario)
        {
            string limpio = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            return limpio.Length <= LONGITUD_USUARIO ? limpio : limpio.Substring(0, LONGITUD_USUARIO);
        }
    }
}
