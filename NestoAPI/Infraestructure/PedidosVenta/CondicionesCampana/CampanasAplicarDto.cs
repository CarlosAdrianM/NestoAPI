using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana
{
    /// <summary>Una fila de <c>dbo.CampanasAplicarDto</c>: esa ficha se desbloquea («Aplicar Dto» = 1) mientras dure la campaña.</summary>
    public class FilaCampanaAplicarDto
    {
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        /// <summary>El «Aplicar Dto» que tenía la ficha al activarla (null: aún no activada).</summary>
        public bool? AplicarDtoAnterior { get; set; }
        public DateTime? FechaActivado { get; set; }
        public DateTime? FechaRestaurado { get; set; }
    }

    public enum AccionCampanaAplicarDto
    {
        Nada,
        Activar,
        Restaurar
    }

    /// <summary>
    /// Feria online 12-16/11/26 (Carlos 11/11/26): las fichas con «Aplicar Dto» = 0 que llevan oferta en una campaña se ponen a 1
    /// al empezar (desde las 00:00 del primer día) y vuelven a lo que tenían al acabar (desde las 00:00 del día siguiente al último).
    /// Con «Aplicar Dto» = 0 el motor de precios no aplica nada (<see cref="GestorPrecios.calcularDescuentoProducto(PrecioDescuentoProducto)"/>).
    /// <para>Los datos (y el estado: anterior, activado, restaurado) viven en la tabla, así que el job recurrente
    /// (<see cref="CampanasAplicarDtoJobsService"/>) es a prueba de reinicios y publicaciones: cada pasada mira la tabla y hace lo que
    /// falte. Al restaurar no pisa: si alguien ha cambiado la ficha durante la campaña (ya no está a 1), la deja y lo apunta.</para>
    /// <para>Ojo: mientras esté a 1, a esas fichas también les aplican los descuentos generales del cliente (familia, grupo…).</para>
    /// </summary>
    public static class CampanasAplicarDto
    {
        /// <summary>Qué toca hacer hoy con esa fila (núcleo puro; el repositorio lo vuelve a comprobar con bloqueo).</summary>
        public static AccionCampanaAplicarDto Decidir(FilaCampanaAplicarDto fila, DateTime hoy)
        {
            if (fila == null || string.IsNullOrWhiteSpace(fila.Producto) || fila.FechaRestaurado.HasValue)
            {
                return AccionCampanaAplicarDto.Nada;
            }
            DateTime dia = hoy.Date;
            if (!fila.FechaActivado.HasValue)
            {
                return fila.FechaDesde.Date <= dia && dia <= fila.FechaHasta.Date
                    ? AccionCampanaAplicarDto.Activar
                    : AccionCampanaAplicarDto.Nada;
            }
            return dia > fila.FechaHasta.Date ? AccionCampanaAplicarDto.Restaurar : AccionCampanaAplicarDto.Nada;
        }
    }

    /// <summary>Resultado de activar o restaurar una ficha (lo que de verdad ha hecho la BD).</summary>
    public enum ResultadoCampanaAplicarDto
    {
        /// <summary>No había nada que hacer (ya estaba hecho, la fila no existe o la ficha no existe).</summary>
        Nada,
        /// <summary>Activada: la ficha estaba a 0 y se ha puesto a 1.</summary>
        Activada,
        /// <summary>Activada sin cambiar la ficha: ya estaba a 1.</summary>
        ActivadaSinCambio,
        /// <summary>Restaurada: estaba a 1 y vuelve a 0.</summary>
        Restaurada,
        /// <summary>Restaurada sin cambiar la ficha: antes de la campaña ya estaba a 1.</summary>
        RestauradaSinCambio,
        /// <summary>No se pisa: alguien ha cambiado la ficha durante la campaña (ya no está a 1).</summary>
        NoPisada
    }

    public interface IRepositorioCampanasAplicarDto
    {
        /// <summary>Las filas pendientes (sin restaurar). Vacío si no existe la tabla.</summary>
        Task<List<FilaCampanaAplicarDto>> LeerPendientes();
        Task<ResultadoCampanaAplicarDto> Activar(FilaCampanaAplicarDto fila);
        Task<ResultadoCampanaAplicarDto> Restaurar(FilaCampanaAplicarDto fila);
    }

    public class RepositorioCampanasAplicarDto : IRepositorioCampanasAplicarDto
    {
        internal const string USUARIO = "NestoAPI";

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NOT NULL " +
            "SELECT RTRIM(Campana) AS Campana, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, " +
            "CAST(FechaDesde AS datetime) AS FechaDesde, CAST(FechaHasta AS datetime) AS FechaHasta, AplicarDtoAnterior, " +
            "FechaActivado, FechaRestaurado FROM dbo.CampanasAplicarDto WITH (NOLOCK) WHERE FechaRestaurado IS NULL";

        // Devuelve el resultado como texto (nombre de ResultadoCampanaAplicarDto).
        internal const string SQL_ACTIVAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resultado nvarchar(30) = N'Nada';
DECLARE @anterior bit = (SELECT [Aplicar Dto] FROM dbo.Productos WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Número = @producto);
IF @anterior IS NOT NULL AND EXISTS (SELECT 1 FROM dbo.CampanasAplicarDto WITH (UPDLOCK, HOLDLOCK)
                                     WHERE Campana = @campana AND Empresa = @empresa AND Producto = @producto
                                       AND FechaActivado IS NULL AND FechaRestaurado IS NULL)
BEGIN
    UPDATE dbo.CampanasAplicarDto SET AplicarDtoAnterior = @anterior, FechaActivado = GETDATE()
    WHERE Campana = @campana AND Empresa = @empresa AND Producto = @producto;
    IF @anterior = 0
    BEGIN
        UPDATE dbo.Productos SET [Aplicar Dto] = 1 WHERE Empresa = @empresa AND Número = @producto;
        INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
        VALUES (N'Productos', CONCAT(RTRIM(@empresa), N'/', RTRIM(@producto), N': Aplicar Dto = 0'),
                CONCAT(RTRIM(@empresa), N'/', RTRIM(@producto), N': Aplicar Dto = 1 (campaña ', RTRIM(@campana), N')'), @usuario);
        SET @resultado = N'Activada';
    END
    ELSE
        SET @resultado = N'ActivadaSinCambio';
END
COMMIT TRANSACTION;
SELECT @resultado;";

        internal const string SQL_RESTAURAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resultado nvarchar(30) = N'Nada';
DECLARE @anterior bit, @hay bit = 0;
SELECT @anterior = AplicarDtoAnterior, @hay = 1 FROM dbo.CampanasAplicarDto WITH (UPDLOCK, HOLDLOCK)
WHERE Campana = @campana AND Empresa = @empresa AND Producto = @producto AND FechaActivado IS NOT NULL AND FechaRestaurado IS NULL;
IF @hay = 1
BEGIN
    DECLARE @actual bit = (SELECT [Aplicar Dto] FROM dbo.Productos WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Número = @producto);
    IF @anterior = 1
        SET @resultado = N'RestauradaSinCambio';
    ELSE IF @actual = 1
    BEGIN
        UPDATE dbo.Productos SET [Aplicar Dto] = 0 WHERE Empresa = @empresa AND Número = @producto;
        INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
        VALUES (N'Productos', CONCAT(RTRIM(@empresa), N'/', RTRIM(@producto), N': Aplicar Dto = 1 (campaña ', RTRIM(@campana), N')'),
                CONCAT(RTRIM(@empresa), N'/', RTRIM(@producto), N': Aplicar Dto = 0 (fin de la campaña)'), @usuario);
        SET @resultado = N'Restaurada';
    END
    ELSE
        SET @resultado = N'NoPisada';
    UPDATE dbo.CampanasAplicarDto SET FechaRestaurado = GETDATE()
    WHERE Campana = @campana AND Empresa = @empresa AND Producto = @producto;
END
COMMIT TRANSACTION;
SELECT @resultado;";

        public async Task<List<FilaCampanaAplicarDto>> LeerPendientes()
        {
            using (var db = new NVEntities())
            {
                return await db.Database.SqlQuery<FilaCampanaAplicarDto>(SQL_LEER).ToListAsync().ConfigureAwait(false);
            }
        }

        public Task<ResultadoCampanaAplicarDto> Activar(FilaCampanaAplicarDto fila) => Ejecutar(SQL_ACTIVAR, fila);

        public Task<ResultadoCampanaAplicarDto> Restaurar(FilaCampanaAplicarDto fila) => Ejecutar(SQL_RESTAURAR, fila);

        private static async Task<ResultadoCampanaAplicarDto> Ejecutar(string sql, FilaCampanaAplicarDto fila)
        {
            using (var db = new NVEntities())
            {
                string texto = (await db.Database.SqlQuery<string>(sql,
                    new SqlParameter("@campana", fila.Campana.Trim()),
                    new SqlParameter("@empresa", fila.Empresa.Trim()),
                    new SqlParameter("@producto", fila.Producto.Trim()),
                    new SqlParameter("@usuario", USUARIO)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                return Enum.TryParse(texto, out ResultadoCampanaAplicarDto resultado) ? resultado : ResultadoCampanaAplicarDto.Nada;
            }
        }
    }

    /// <summary>
    /// Job recurrente (cada hora en punto) y al arrancar la API: activa y restaura el «Aplicar Dto» de las campañas de
    /// <c>dbo.CampanasAplicarDto</c>. Idempotente; sin tabla no hace nada. Deja rastro en ELMAH de lo que cambia y de lo que no pisa.
    /// </summary>
    public class CampanasAplicarDtoJobsService
    {
        public const string JOB_ID = "campanas-aplicar-dto";
        /// <summary>Cada hora en punto (la primera pasada del primer día es a las 00:00).</summary>
        public const string CRON = "0 * * * *";

        private readonly IRepositorioCampanasAplicarDto repositorio;
        private readonly Action<Exception> registrar;

        public CampanasAplicarDtoJobsService() : this(new RepositorioCampanasAplicarDto(), ex => ElmahHelper.Log(ex))
        {
        }

        internal CampanasAplicarDtoJobsService(IRepositorioCampanasAplicarDto repositorio, Action<Exception> registrar)
        {
            this.repositorio = repositorio;
            this.registrar = registrar ?? (_ => { });
        }

        /// <summary>Punto de entrada de Hangfire.</summary>
        public static async Task Ejecutar()
        {
            _ = await new CampanasAplicarDtoJobsService().EjecutarAsync(DateTime.Today).ConfigureAwait(false);
        }

        /// <summary>Lo que ha hecho con cada ficha («campaña/empresa/producto» → resultado), solo las que han hecho algo.</summary>
        public async Task<Dictionary<string, ResultadoCampanaAplicarDto>> EjecutarAsync(DateTime hoy)
        {
            var hecho = new Dictionary<string, ResultadoCampanaAplicarDto>();
            List<FilaCampanaAplicarDto> filas = await repositorio.LeerPendientes().ConfigureAwait(false) ?? new List<FilaCampanaAplicarDto>();
            foreach (FilaCampanaAplicarDto fila in filas)
            {
                AccionCampanaAplicarDto accion = CampanasAplicarDto.Decidir(fila, hoy);
                if (accion == AccionCampanaAplicarDto.Nada)
                {
                    continue;
                }
                string clave = $"{fila.Campana?.Trim()}/{fila.Empresa?.Trim()}/{fila.Producto?.Trim()}";
                try
                {
                    ResultadoCampanaAplicarDto resultado = accion == AccionCampanaAplicarDto.Activar
                        ? await repositorio.Activar(fila).ConfigureAwait(false)
                        : await repositorio.Restaurar(fila).ConfigureAwait(false);
                    if (resultado != ResultadoCampanaAplicarDto.Nada)
                    {
                        hecho[clave] = resultado;
                    }
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Campañas Aplicar Dto] No se ha podido {(accion == AccionCampanaAplicarDto.Activar ? "activar" : "restaurar")} {clave}: {ex.Message}", ex));
                }
            }

            List<string> noPisadas = hecho.Where(h => h.Value == ResultadoCampanaAplicarDto.NoPisada).Select(h => h.Key).ToList();
            if (noPisadas.Any())
            {
                registrar(new Exception($"[Campañas Aplicar Dto] Fin de campaña: estas fichas ya no tenían «Aplicar Dto» = 1 (alguien las cambió durante la campaña) y se han dejado como estaban: {string.Join(", ", noPisadas)}"));
            }
            List<string> cambiadas = hecho.Where(h => h.Value == ResultadoCampanaAplicarDto.Activada || h.Value == ResultadoCampanaAplicarDto.Restaurada)
                .Select(h => $"{h.Key} {(h.Value == ResultadoCampanaAplicarDto.Activada ? "→ 1" : "→ 0")}").ToList();
            if (cambiadas.Any())
            {
                registrar(new Exception($"[Campañas Aplicar Dto] Informativo: «Aplicar Dto» cambiado en {cambiadas.Count} fichas: {string.Join(", ", cambiadas)}"));
            }
            return hecho;
        }
    }
}
