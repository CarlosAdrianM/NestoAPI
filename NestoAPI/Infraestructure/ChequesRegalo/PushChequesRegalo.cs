using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>Un cheque al que se le puede mandar una push (generado o recordatorio).</summary>
    public class ChequeRegaloParaPush
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Codigo { get; set; }
        public decimal ImporteBase { get; set; }
        public decimal MinimoCanje { get; set; }
        public DateTime CanjeHasta { get; set; }
        public DateTime? FechaActivacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#593 (TNV): los textos de las push a las clientas. Importe, mínimo y fecha salen de la campaña.
    /// <c>Datos.tipo = chequeregalo</c>: la app abre el carrito, que es donde ve el cheque y lo usa.
    /// </summary>
    public static class PlantillaPushChequeRegalo
    {
        public const string TIPO = "chequeregalo";
        public const int DIAS_RECORDATORIO_POR_DEFECTO = 5;

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static NotificacionPushDTO Generado(ChequeRegaloParaPush cheque, DateTime ahora)
        {
            string importe = PlantillaCorreoChequeRegalo.Euros(cheque.ImporteBase);
            string minimo = PlantillaCorreoChequeRegalo.Euros(cheque.MinimoCanje);
            string hasta = PlantillaCorreoChequeRegalo.Fecha(cheque.CanjeHasta);
            string uso = cheque.FechaActivacion != null && cheque.FechaActivacion.Value.Date > ahora.Date
                ? $"Podrás usarlo desde el {PlantillaCorreoChequeRegalo.Fecha(cheque.FechaActivacion.Value)} hasta el {hasta}."
                : $"Úsalo hasta el {hasta}.";
            return Notificacion($"Tienes un cheque regalo de {importe} €",
                $"Tienes un cheque regalo de {importe} € para tu próximo pedido de más de {minimo} €. {uso}", cheque);
        }

        public static NotificacionPushDTO Recordatorio(ChequeRegaloParaPush cheque, DateTime hoy)
        {
            string importe = PlantillaCorreoChequeRegalo.Euros(cheque.ImporteBase);
            int dias = DiasQueQuedan(cheque, hoy);
            string titulo = dias <= 0
                ? $"Hoy es el último día para usar tu cheque regalo de {importe} €"
                : dias == 1
                    ? $"Te queda 1 día para usar tu cheque regalo de {importe} €"
                    : $"Te quedan {dias} días para usar tu cheque regalo de {importe} €";
            return Notificacion(titulo,
                $"Úsalo en un pedido de más de {PlantillaCorreoChequeRegalo.Euros(cheque.MinimoCanje)} € de producto hasta el " +
                $"{PlantillaCorreoChequeRegalo.Fecha(cheque.CanjeHasta)}. Lo tienes en el carrito de la app.", cheque);
        }

        /// <summary>Días hasta CanjeHasta (que se incluye): el 2/11 con fecha límite el 7/11 son 5.</summary>
        public static int DiasQueQuedan(ChequeRegaloParaPush cheque, DateTime hoy) => (cheque.CanjeHasta.Date - hoy.Date).Days;

        private static NotificacionPushDTO Notificacion(string titulo, string cuerpo, ChequeRegaloParaPush cheque) => new NotificacionPushDTO
        {
            Titulo = titulo,
            Cuerpo = cuerpo,
            Tipo = TIPO,
            Datos = new Dictionary<string, string>
            {
                { "tipo", TIPO },
                { "campana", cheque.Codigo?.Trim() ?? string.Empty }
            }
        };
    }

    public interface IRepositorioPushChequesRegalo
    {
        /// <summary>False mientras no se lance Issue593_ChequeRegalo_TNV.sql (sin columnas, no hay push).</summary>
        Task<bool> ColumnasDisponibles();
        /// <summary>Cheques Generado, sin push del aviso, de campañas activas y en plazo, de clientes con la app
        /// (algún dispositivo activo de NestoTiendas). Cliente null = todos.</summary>
        Task<List<ChequeRegaloParaPush>> LeerSinPushGenerado(string cliente);
        /// <summary>Cheques Generado y activados, sin recordatorio, a los que les quedan DiasRecordatorioPush días o
        /// menos (por defecto 5), de clientes con la app y sin push del aviso hoy mismo.</summary>
        Task<List<ChequeRegaloParaPush>> LeerParaRecordatorio(DateTime hoy);
        /// <summary>UPDATE … WHERE FechaPushGenerado IS NULL: false si otro ya lo mandó (no se manda dos veces).</summary>
        Task<bool> ReservarPushGenerado(int id);
        Task AnularPushGenerado(int id);
        Task<bool> ReservarPushRecordatorio(int id);
        Task AnularPushRecordatorio(int id);
    }

    public interface IAvisadorPushChequesRegalo
    {
        /// <summary>La push «tienes un cheque» de los cheques del cliente aún sin ella (la llama la factura que lo genera).</summary>
        Task<int> AvisarCliente(string cliente);
        /// <summary>La push «tienes un cheque» de los que falten (job de la mañana).</summary>
        Task<int> AvisarPendientes();
        /// <summary>El recordatorio, una sola vez, a los que siguen sin usar el cheque cuando faltan N días.</summary>
        Task<int> Recordar(DateTime hoy);
    }

    /// <summary>
    /// NestoAPI#593 (TNV): push a la clienta (app TiendasNuevaVision, proyecto Firebase de NestoTiendas, por
    /// <see cref="IServicioNotificacionesPush.EnviarACliente"/>, que además la deja en su buzón). Best-effort, como el
    /// correo: un fallo va a ELMAH y no toca la factura. Sin dispositivos no se manda nada ni se marca: si instala la
    /// app después, el job de la mañana se lo manda. Se marca ANTES de mandar (UPDATE condicionado) y se deshace si la
    /// push revienta; si sale sin errores pero FCM no la entrega, queda marcada (el buzón ya la tiene, como los avisos
    /// de pedidos).
    /// </summary>
    public class AvisadorPushChequesRegalo : IAvisadorPushChequesRegalo
    {
        private readonly IRepositorioPushChequesRegalo repositorio;
        private readonly IServicioNotificacionesPush push;
        private readonly Func<DateTime> ahora;
        private readonly Action<Exception> registrarError;

        public AvisadorPushChequesRegalo(IRepositorioPushChequesRegalo repositorio, IServicioNotificacionesPush push,
            Func<DateTime> ahora = null, Action<Exception> registrarError = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.push = push ?? throw new ArgumentNullException(nameof(push));
            this.ahora = ahora ?? (() => DateTime.Now);
            this.registrarError = registrarError ?? (ex => ElmahHelper.Log(ex));
        }

        public static AvisadorPushChequesRegalo Crear(NVEntities db)
            => new AvisadorPushChequesRegalo(new RepositorioPushChequesRegalo(db), new ServicioNotificacionesPush());

        public async Task<int> AvisarCliente(string cliente)
        {
            if (string.IsNullOrWhiteSpace(cliente) || !await repositorio.ColumnasDisponibles().ConfigureAwait(false))
            {
                return 0;
            }
            return await AvisarGenerados(await repositorio.LeerSinPushGenerado(cliente.Trim()).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task<int> AvisarPendientes()
        {
            if (!await repositorio.ColumnasDisponibles().ConfigureAwait(false))
            {
                return 0;
            }
            return await AvisarGenerados(await repositorio.LeerSinPushGenerado(null).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task<int> Recordar(DateTime hoy)
        {
            if (!await repositorio.ColumnasDisponibles().ConfigureAwait(false))
            {
                return 0;
            }
            int enviados = 0;
            foreach (ChequeRegaloParaPush cheque in await repositorio.LeerParaRecordatorio(hoy.Date).ConfigureAwait(false) ?? new List<ChequeRegaloParaPush>())
            {
                if (await Enviar(cheque, PlantillaPushChequeRegalo.Recordatorio(cheque, hoy),
                    repositorio.ReservarPushRecordatorio, repositorio.AnularPushRecordatorio, "el recordatorio").ConfigureAwait(false))
                {
                    enviados++;
                }
            }
            return enviados;
        }

        private async Task<int> AvisarGenerados(List<ChequeRegaloParaPush> cheques)
        {
            int enviados = 0;
            foreach (ChequeRegaloParaPush cheque in cheques ?? new List<ChequeRegaloParaPush>())
            {
                if (await Enviar(cheque, PlantillaPushChequeRegalo.Generado(cheque, ahora()),
                    repositorio.ReservarPushGenerado, repositorio.AnularPushGenerado, "la push").ConfigureAwait(false))
                {
                    enviados++;
                }
            }
            return enviados;
        }

        /// <summary>Uno que falle no para a los demás.</summary>
        private async Task<bool> Enviar(ChequeRegaloParaPush cheque, NotificacionPushDTO notificacion,
            Func<int, Task<bool>> reservar, Func<int, Task> anular, string que)
        {
            try
            {
                if (!await reservar(cheque.Id).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"[Cheques regalo #593] No se pudo marcar {que} del cheque {cheque.Id}: {ex.Message}", ex));
                return false;
            }
            try
            {
                _ = await push.EnviarACliente(cheque.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    cheque.Cliente?.Trim(), notificacion).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    await anular(cheque.Id).ConfigureAwait(false);
                }
                catch (Exception exAnular)
                {
                    registrarError(new Exception($"[Cheques regalo #593] No se pudo deshacer la marca de {que} del cheque {cheque.Id}: " +
                        exAnular.Message, exAnular));
                }
                registrarError(new Exception($"[Cheques regalo #593] Falló {que} del cheque de {cheque.Cliente?.Trim()} " +
                    $"({cheque.Codigo?.Trim()}): {ex.Message}", ex));
                return false;
            }
        }
    }

    /// <summary>NestoAPI#593 (TNV): las marcas de las push por SQL directo (sin EDMX), como el aviso por correo.</summary>
    public class RepositorioPushChequesRegalo : IRepositorioPushChequesRegalo
    {
        private readonly NVEntities db;

        public RepositorioPushChequesRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_COLUMNAS_DISPONIBLES = @"
SELECT CAST(CASE WHEN COL_LENGTH('dbo.ChequesRegalo', 'FechaPushGenerado') IS NOT NULL
                  AND COL_LENGTH('dbo.ChequesRegalo', 'FechaPushRecordatorio') IS NOT NULL
                  AND COL_LENGTH('dbo.ChequesRegaloCampanas', 'DiasRecordatorioPush') IS NOT NULL THEN 1 ELSE 0 END AS bit)";

        // Solo clientes con la app: sin dispositivo no hay a quién mandarla (y no se marca: si la instala, le llega)
        private const string CON_LA_APP = @"
      AND EXISTS (SELECT 1 FROM dbo.DispositivosNotificaciones d
                  WHERE d.Aplicacion = @app AND d.Activo = 1 AND d.Empresa = c.Empresa AND d.Cliente = c.Cliente)";

        private const string COLUMNAS = @"
SELECT c.Id, RTRIM(c.Empresa) AS Empresa, RTRIM(c.Cliente) AS Cliente, RTRIM(k.Codigo) AS Codigo, k.ImporteBase, k.MinimoCanje,
       CAST(k.CanjeHasta AS datetime) AS CanjeHasta, c.FechaActivacion
FROM dbo.ChequesRegalo c
     INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana";

        internal const string SQL_SIN_PUSH_GENERADO = COLUMNAS + @"
WHERE c.FechaPushGenerado IS NULL AND c.Estado = 'Generado' AND k.Activa = 1
      AND k.CanjeHasta >= CAST(GETDATE() AS date)
      AND (@p0 = '' OR c.Cliente = @p0)" + CON_LA_APP + @"
ORDER BY c.Id";

        // Una vez, cuando faltan DiasRecordatorioPush días o menos (5 por defecto; 0 = sin recordatorio), al cheque
        // que sigue libre y ya se puede usar. El que acaba de recibir hoy la push del aviso no recibe también el
        // recordatorio: le llegará mañana.
        internal const string SQL_PARA_RECORDATORIO = COLUMNAS + @"
WHERE c.FechaPushRecordatorio IS NULL AND c.Estado = 'Generado' AND k.Activa = 1
      AND c.FechaActivacion IS NOT NULL AND c.FechaActivacion <= GETDATE()
      AND ISNULL(k.DiasRecordatorioPush, 5) > 0
      AND k.CanjeHasta >= @p0
      AND DATEDIFF(day, @p0, k.CanjeHasta) <= ISNULL(k.DiasRecordatorioPush, 5)
      AND (c.FechaPushGenerado IS NULL OR c.FechaPushGenerado < @p0)" + CON_LA_APP + @"
ORDER BY c.Id";

        internal const string SQL_RESERVAR_GENERADO = "UPDATE dbo.ChequesRegalo SET FechaPushGenerado = GETDATE() WHERE Id = @p0 AND FechaPushGenerado IS NULL";
        internal const string SQL_ANULAR_GENERADO = "UPDATE dbo.ChequesRegalo SET FechaPushGenerado = NULL WHERE Id = @p0";
        internal const string SQL_RESERVAR_RECORDATORIO = "UPDATE dbo.ChequesRegalo SET FechaPushRecordatorio = GETDATE() WHERE Id = @p0 AND FechaPushRecordatorio IS NULL";
        internal const string SQL_ANULAR_RECORDATORIO = "UPDATE dbo.ChequesRegalo SET FechaPushRecordatorio = NULL WHERE Id = @p0";

        private static SqlParameter App() => new SqlParameter("@app", Constantes.Aplicaciones.NESTO_TIENDAS);

        public async Task<bool> ColumnasDisponibles()
            => await db.Database.SqlQuery<bool>(SQL_COLUMNAS_DISPONIBLES).FirstAsync().ConfigureAwait(false);

        public async Task<List<ChequeRegaloParaPush>> LeerSinPushGenerado(string cliente)
            => await db.Database.SqlQuery<ChequeRegaloParaPush>(SQL_SIN_PUSH_GENERADO,
                new SqlParameter("@p0", cliente?.Trim() ?? string.Empty), App()).ToListAsync().ConfigureAwait(false);

        public async Task<List<ChequeRegaloParaPush>> LeerParaRecordatorio(DateTime hoy)
            => await db.Database.SqlQuery<ChequeRegaloParaPush>(SQL_PARA_RECORDATORIO,
                new SqlParameter("@p0", hoy.Date), App()).ToListAsync().ConfigureAwait(false);

        public async Task<bool> ReservarPushGenerado(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR_GENERADO, new SqlParameter("@p0", id)).ConfigureAwait(false) > 0;

        public async Task AnularPushGenerado(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR_GENERADO, new SqlParameter("@p0", id)).ConfigureAwait(false);

        public async Task<bool> ReservarPushRecordatorio(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR_RECORDATORIO, new SqlParameter("@p0", id)).ConfigureAwait(false) > 0;

        public async Task AnularPushRecordatorio(int id)
            => await db.Database.ExecuteSqlCommandAsync(SQL_ANULAR_RECORDATORIO, new SqlParameter("@p0", id)).ConfigureAwait(false);
    }

    /// <summary>
    /// NestoAPI#593 (TNV): job de la mañana (10:00). Ligero (dos consultas acotadas por los cheques en plazo y un
    /// puñado de push): el servidor principal de Hangfire tiene un solo hilo. Primero las push del aviso que falten
    /// (cheques generados en la reconciliación de la noche o de clientas que han instalado la app después), luego los
    /// recordatorios. No depende del interruptor ChequesRegalo:Generar (eso es para generar): basta la campaña activa.
    /// </summary>
    public static class ChequesRegaloPushJobsService
    {
        public const string ID_JOB = "cheques-regalo-push";
        public const string CRON = "0 10 * * *";

        public static async Task EnviarPushDelDia()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    AvisadorPushChequesRegalo avisador = AvisadorPushChequesRegalo.Crear(db);
                    int avisos = await avisador.AvisarPendientes().ConfigureAwait(false);
                    int recordatorios = await avisador.Recordar(DateTime.Today).ConfigureAwait(false);
                    Console.WriteLine($"✅ [Hangfire] Cheques regalo: {avisos} push de aviso y {recordatorios} recordatorios");
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cheques regalo #593] Falló el job de push de la mañana: {ex.Message}", ex));
                throw;
            }
        }
    }
}
