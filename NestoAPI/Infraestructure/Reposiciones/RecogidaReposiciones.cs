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
    /// <summary>Sugerencia 581 (Carlos, 13/10/26): en qué punto está hoy la ruta de la tienda a Algete (ver <see cref="EstadoReposicionPuntualDTO"/>).</summary>
    public static class SituacionesReposicionPuntual
    {
        /// <summary>Hoy no hay viaje de la tienda a Algete en el calendario (o es festivo en alguno de los dos): no la recogería nadie.</summary>
        public const string SIN_VIAJE = "SinViaje";
        /// <summary>Hay viaje, pero el job aún no ha rellenado la de hoy: lo que se mandara podría ir en la automática.</summary>
        public const string SIN_RELLENAR = "SinRellenar";
        /// <summary>La de hoy está rellena pero en preparación: primero se envía esa.</summary>
        public const string SIN_ENVIAR = "SinEnviar";
        /// <summary>La tienda ha marcado que el repartidor ya se ha llevado la de hoy: lo que quede va en la siguiente.</summary>
        public const string RECOGIDA = "Recogida";
        /// <summary>La de hoy está enviada y el repartidor no ha pasado: se puede hacer la puntual (y marcar la recogida).</summary>
        public const string ABIERTA = "Abierta";
    }

    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26): GET api/Reposiciones/Puntual/Estado. Si hoy se puede hacer una reposición puntual de
    /// la tienda a Algete y, si no, por qué; y si el repartidor ya se ha llevado la de hoy (quién lo marcó y a qué hora).
    /// </summary>
    public class EstadoReposicionPuntualDTO
    {
        public string Origen { get; set; }
        public string Destino { get; set; }
        /// <summary>Ver <see cref="SituacionesReposicionPuntual"/>.</summary>
        public string Situacion { get; set; }
        /// <summary>Se puede hacer la puntual ahora (<see cref="SituacionesReposicionPuntual.ABIERTA"/>).</summary>
        public bool SePuede { get; set; }
        /// <summary>Por qué no se puede, para enseñarlo tal cual (el mismo texto que el 409). Null si se puede.</summary>
        public string Motivo { get; set; }
        /// <summary>Cuándo se rellena (o se rellenó) sola la de hoy: el corte del calendario. Null sin viaje hoy.</summary>
        public DateTime? CierreHoy { get; set; }
        /// <summary>Cuándo se marcó que el repartidor se ha llevado la de hoy. Null si no se ha marcado.</summary>
        public DateTime? FechaRecogida { get; set; }
        /// <summary>Quién lo marcó.</summary>
        public string UsuarioRecogida { get; set; }
        /// <summary>
        /// Se puede marcar ahora que el repartidor se la ha llevado (POST api/Reposiciones/Recogida): la de hoy enviada, sin
        /// marcar todavía y con la columna en la base de datos.
        /// </summary>
        public bool PuedeMarcarRecogida { get; set; }
    }

    /// <summary>POST api/Reposiciones/Recogida: el repartidor ya se ha llevado la reposición de hoy de esta ruta.</summary>
    public class MarcarRecogidaReposicionDTO
    {
        public string Empresa { get; set; }
        /// <summary>La tienda (de momento solo tienda → Algete).</summary>
        public string Origen { get; set; }
        /// <summary>Null: Algete.</summary>
        public string Destino { get; set; }
    }

    /// <summary>Lo que se sabe en ReposicionesTraspasos del viaje de hoy de una ruta.</summary>
    public class ViajeReposicionDelDia
    {
        /// <summary>El job ya ha tratado el viaje (cabecera 'Automatico' con el corte ese día: rellena, vacía, ya cubierta…).</summary>
        public bool HayCabecera { get; set; }
        /// <summary>Existen las columnas FechaRecogida y UsuarioRecogida (Scripts/Issue581_ReposicionesTraspasos_Recogida.sql).</summary>
        public bool HayColumnaRecogida { get; set; }
        public DateTime? FechaRecogida { get; set; }
        public string UsuarioRecogida { get; set; }
    }

    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26: «no pasa todos los días a la misma hora»): el viaje de hoy de una ruta para la
    /// reposición puntual: el calendario, si el job ya ha rellenado la de hoy y si la tienda ha marcado que el repartidor se la
    /// ha llevado (ReposicionesTraspasos.FechaRecogida / UsuarioRecogida).
    /// </summary>
    public interface IViajesReposicionPuntual
    {
        /// <summary>Las filas activas de ReposicionesCalendario de la empresa.</summary>
        Task<List<ReposicionCalendario>> LeerCalendario(string empresa);

        /// <summary>El viaje cuyo corte cae el día <paramref name="diaCorte"/>. Sin las columnas de la recogida, sin recogida.</summary>
        Task<ViajeReposicionDelDia> LeerViajeDelDia(string empresa, string origen, string destino, DateTime diaCorte);

        /// <summary>
        /// Apunta la recogida en las cabeceras del viaje (las del job con el corte ese día) y en las puntuales de la ruta creadas
        /// <paramref name="fecha"/> (el mismo día), las que aún no la tienen. No hace nada sin las columnas.
        /// </summary>
        Task MarcarRecogida(string empresa, string origen, string destino, DateTime diaCorte, DateTime fecha, string usuario);
    }

    /// <summary>Para las pruebas que no lo miran: hay viaje todos los días, ya relleno y sin recoger (sin columnas).</summary>
    internal sealed class ViajesReposicionPuntualSinControl : IViajesReposicionPuntual
    {
        internal static readonly ViajesReposicionPuntualSinControl Instancia = new ViajesReposicionPuntualSinControl();

        public Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            var filas = new List<ReposicionCalendario>();
            foreach (string origen in new[] { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS })
            {
                for (byte dia = 1; dia <= 7; dia++)
                {
                    filas.Add(new ReposicionCalendario
                    {
                        Empresa = empresa, AlmacenOrigen = origen, AlmacenDestino = Constantes.Almacenes.ALGETE, DiaSemana = dia,
                        HoraCierre = TimeSpan.Zero, HoraLlegadaHabitual = new TimeSpan(23, 59, 0), Activo = true
                    });
                }
            }
            return Task.FromResult(filas);
        }

        public Task<ViajeReposicionDelDia> LeerViajeDelDia(string empresa, string origen, string destino, DateTime diaCorte)
            => Task.FromResult(new ViajeReposicionDelDia { HayCabecera = true });

        public Task MarcarRecogida(string empresa, string origen, string destino, DateTime diaCorte, DateTime fecha, string usuario)
            => Task.CompletedTask;
    }

    public class ViajesReposicionPuntualSql : IViajesReposicionPuntual
    {
        // Las columnas pueden no existir todavía (script sin lanzar): lo que las nombra va por sp_executesql, que solo se
        // compila si existen. Siempre devuelve una fila.
        internal const string SQL_VIAJE_DEL_DIA = @"
DECLARE @hayColumna bit = CASE WHEN COL_LENGTH('dbo.ReposicionesTraspasos', 'FechaRecogida') IS NULL
                                 OR COL_LENGTH('dbo.ReposicionesTraspasos', 'UsuarioRecogida') IS NULL THEN 0 ELSE 1 END;
DECLARE @hayCabecera bit = 0, @fecha datetime = NULL, @usuario varchar(30) = NULL;
IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL
    SET @hayCabecera = CASE WHEN EXISTS (
        SELECT 1 FROM dbo.ReposicionesTraspasos
        WHERE Empresa = @p0 AND Origen = @p1 AND Destino = @p2 AND Herramienta = 'Automatico'
          AND FechaCorte >= @p3 AND FechaCorte < DATEADD(day, 1, @p3)) THEN 1 ELSE 0 END;
IF @hayColumna = 1
    EXEC sp_executesql N'SELECT TOP 1 @fecha = FechaRecogida, @usuario = UsuarioRecogida
        FROM dbo.ReposicionesTraspasos
        WHERE Empresa = @e AND Origen = @o AND Destino = @d AND Herramienta = ''Automatico''
          AND FechaCorte >= @dia AND FechaCorte < DATEADD(day, 1, @dia) AND FechaRecogida IS NOT NULL
        ORDER BY FechaRecogida',
        N'@e char(3), @o char(3), @d char(3), @dia datetime, @fecha datetime OUTPUT, @usuario varchar(30) OUTPUT',
        @e = @p0, @o = @p1, @d = @p2, @dia = @p3, @fecha = @fecha OUTPUT, @usuario = @usuario OUTPUT;
SELECT @hayCabecera AS HayCabecera, @hayColumna AS HayColumnaRecogida, @fecha AS FechaRecogida, @usuario AS UsuarioRecogida;";

        internal const string SQL_MARCAR_RECOGIDA = @"
IF COL_LENGTH('dbo.ReposicionesTraspasos', 'FechaRecogida') IS NOT NULL AND COL_LENGTH('dbo.ReposicionesTraspasos', 'UsuarioRecogida') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.ReposicionesTraspasos SET FechaRecogida = @fecha, UsuarioRecogida = @usuario
        WHERE Empresa = @e AND Origen = @o AND Destino = @d AND FechaRecogida IS NULL
          AND ((Herramienta = ''Automatico'' AND FechaCorte >= @dia AND FechaCorte < DATEADD(day, 1, @dia))
            OR (Herramienta = ''Puntual'' AND FechaCreacion >= @hoy AND FechaCreacion < DATEADD(day, 1, @hoy)))',
        N'@e char(3), @o char(3), @d char(3), @dia datetime, @hoy datetime, @fecha datetime, @usuario varchar(30)',
        @e = @p0, @o = @p1, @d = @p2, @dia = @p3, @hoy = @p4, @fecha = @p5, @usuario = @p6";

        private readonly NVEntities db;

        public ViajesReposicionPuntualSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            return await db.ReposicionesCalendario.AsNoTracking()
                .Where(f => f.Empresa == empresa && f.Activo)
                .ToListAsync().ConfigureAwait(false);
        }

        public Task<ViajeReposicionDelDia> LeerViajeDelDia(string empresa, string origen, string destino, DateTime diaCorte)
        {
            return db.Database.SqlQuery<ViajeReposicionDelDia>(SQL_VIAJE_DEL_DIA,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", destino, 3), RegistroReposicionesTraspasosSql.Fecha("@p3", diaCorte.Date))
                .SingleAsync();
        }

        public Task MarcarRecogida(string empresa, string origen, string destino, DateTime diaCorte, DateTime fecha, string usuario)
        {
            return db.Database.ExecuteSqlCommandAsync(SQL_MARCAR_RECOGIDA,
                RegistroReposicionesTraspasosSql.Char("@p0", empresa, 3), RegistroReposicionesTraspasosSql.Char("@p1", origen, 3),
                RegistroReposicionesTraspasosSql.Char("@p2", destino, 3), RegistroReposicionesTraspasosSql.Fecha("@p3", diaCorte.Date),
                RegistroReposicionesTraspasosSql.Fecha("@p4", fecha.Date), RegistroReposicionesTraspasosSql.Fecha("@p5", fecha),
                RegistroReposicionesTraspasosSql.VarChar("@p6", UsuarioAuditoriaHelper.ParaAuditoria(usuario), 30));
        }
    }

    /// <summary>
    /// Sugerencia 581 (Carlos, 13/10/26): las reglas de la reposición puntual, sin BD. Solo tiene sentido si alguien se la va a
    /// llevar hoy y no puede ir en la automática:
    /// <list type="number">
    /// <item>Hoy hay viaje de la tienda a Algete (fila activa del calendario que llega hoy, laborable en los dos almacenes,
    /// como el job). Si no, no la recoge nadie.</item>
    /// <item>El job ya ha rellenado la de hoy (su cabecera del día: rellena, vacía, ya cubierta…). Antes, esos productos
    /// pueden entrar en la automática.</item>
    /// <item>La de hoy no está en preparación (ya enviada).</item>
    /// <item>El repartidor no se la ha llevado todavía (lo marca la tienda: no pasa siempre a la misma hora).</item>
    /// </list>
    /// </summary>
    public static class ReglasReposicionPuntual
    {
        /// <param name="viajeHoy">El viaje de hoy de la ruta (CortesQueLleganElDia). Null si no hay.</param>
        /// <param name="festivo">Hay fila para hoy en el calendario, pero no viaje: es festivo en alguno de los dos.</param>
        /// <param name="viaje">Lo apuntado en ReposicionesTraspasos del viaje de hoy (null sin viaje).</param>
        /// <param name="enPreparacion">La tienda tiene una reposición en preparación (sin enviar).</param>
        public static EstadoReposicionPuntualDTO Calcular(string origen, string destino, string nombreOrigen, string nombreDestino,
            CorteReposicion viajeHoy, bool festivo, ViajeReposicionDelDia viaje, bool enPreparacion, DateTime ahora)
        {
            var estado = new EstadoReposicionPuntualDTO
            {
                Origen = origen,
                Destino = destino,
                CierreHoy = viajeHoy?.Corte,
                FechaRecogida = viaje?.FechaRecogida,
                UsuarioRecogida = viaje?.UsuarioRecogida?.Trim()
            };
            if (viajeHoy == null)
            {
                return Con(estado, SituacionesReposicionPuntual.SIN_VIAJE, TextoSinViaje(origen, nombreOrigen, destino, nombreDestino, festivo));
            }
            if (viajeHoy.Corte > ahora || viaje?.HayCabecera != true)
            {
                return Con(estado, SituacionesReposicionPuntual.SIN_RELLENAR, TextoSinRellenar(viajeHoy.Corte));
            }
            if (viaje.FechaRecogida.HasValue)
            {
                return Con(estado, SituacionesReposicionPuntual.RECOGIDA, TextoRecogida(viaje.FechaRecogida.Value));
            }
            if (enPreparacion)
            {
                return Con(estado, SituacionesReposicionPuntual.SIN_ENVIAR, TEXTO_SIN_ENVIAR);
            }
            estado.Situacion = SituacionesReposicionPuntual.ABIERTA;
            estado.SePuede = true;
            estado.PuedeMarcarRecogida = viaje.HayColumnaRecogida;
            return estado;
        }

        public const string TEXTO_SIN_ENVIAR = "Primero envía la de hoy.";

        public static string TextoSinViaje(string origen, string nombreOrigen, string destino, string nombreDestino, bool festivo)
        {
            string de = string.IsNullOrWhiteSpace(nombreOrigen) ? origen : nombreOrigen.Trim();
            string a = string.IsNullOrWhiteSpace(nombreDestino) ? destino : nombreDestino.Trim();
            return festivo ? $"Hoy no hay viaje de {de} a {a}: es festivo." : $"Hoy no hay viaje de {de} a {a}.";
        }

        public static string TextoSinRellenar(DateTime corte)
            => $"Todavía no se ha rellenado la de hoy (se rellena sola a las {corte:H:mm}).";

        public static string TextoRecogida(DateTime fechaRecogida)
            => $"El repartidor ya se ha llevado la reposición de hoy (a las {fechaRecogida:H:mm}): lo que quede va en la siguiente.";

        private static EstadoReposicionPuntualDTO Con(EstadoReposicionPuntualDTO estado, string situacion, string motivo)
        {
            estado.Situacion = situacion;
            estado.SePuede = false;
            estado.Motivo = motivo;
            estado.PuedeMarcarRecogida = false;
            return estado;
        }
    }
}
