using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    /// <summary>
    /// Incidencia 1227 (Algete, dos días seguidos): desde Nesto se creaba una fila de inventario SIN ubicación de un producto que se
    /// ubica en un almacén con control de ubicaciones; el comprobador la da por bloqueante (<see cref="ComprobadorInventario"/>, SinUbicacion)
    /// y, si se queda de un día para otro, bloquea el picking. En esos almacenes el POST api/Inventarios exige el hueco.
    /// </summary>
    public interface ILectorHuecoInventario
    {
        /// <summary>Almacenes.ControlUbicaciones (false si no existe el almacén).</summary>
        Task<bool> ControlUbicaciones(string empresa, string almacen);
        /// <summary>Lo LIBRE del producto en ese hueco (Ubicaciones Estado 0), 0 si no estaba. Como el rellenado de Ariadna (#784).</summary>
        Task<int> LibreEnHueco(string almacen, string producto, string pasillo, string fila, string columna);
    }

    public static class ReglasHuecoInventario
    {
        /// <summary>En un almacén con ubicaciones, un producto que se ubica va por huecos (la condición del comprobador: Pasillo null y Ubicar).</summary>
        public static bool ExigeHueco(bool controlUbicaciones, bool productoSeUbica) => controlUbicaciones && productoSeUbica;

        /// <summary>Pasillo, fila y columna, los tres.</summary>
        public static bool TraeHueco(string pasillo, string fila, string columna) =>
            !string.IsNullOrWhiteSpace(pasillo) && !string.IsNullOrWhiteSpace(fila) && !string.IsNullOrWhiteSpace(columna);

        public static string MensajeSinHueco(string almacen) =>
            $"En {almacen?.Trim()} el inventario va por huecos: indique el pasillo, la fila y la columna donde está el producto (o cuéntelo con la PDA en su hueco).";
    }

    public class LectorHuecoInventarioSql : ILectorHuecoInventario
    {
        internal const string SQL_CONTROL = "SELECT CAST(ISNULL(ControlUbicaciones, 0) AS bit) FROM dbo.Almacenes WHERE Empresa = @p0 AND Número = @p1";

        // Igual que RepositorioInventarioUbicaciones.SQL_TEORICO_UBICACION (sin empresa: las filas de Ubicaciones son del almacén)
        internal const string SQL_LIBRE = @"SELECT CAST(ISNULL(SUM(u.Cantidad), 0) AS int) FROM dbo.Ubicaciones u
WHERE u.Almacén = @p0 AND u.Número = @p1 AND u.Estado = 0 AND u.Pasillo = @p2 AND u.Fila = @p3 AND u.Columna = @p4";

        private readonly NVEntities db;

        public LectorHuecoInventarioSql(NVEntities db)
        {
            this.db = db;
        }

        public async Task<bool> ControlUbicaciones(string empresa, string almacen)
        {
            List<bool> filas = await db.Database.SqlQuery<bool>(SQL_CONTROL, new SqlParameter("@p0", empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", almacen?.Trim() ?? string.Empty)).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }

        public async Task<int> LibreEnHueco(string almacen, string producto, string pasillo, string fila, string columna)
            => (await db.Database.SqlQuery<int>(SQL_LIBRE, new SqlParameter("@p0", almacen?.Trim() ?? string.Empty),
                new SqlParameter("@p1", producto?.Trim() ?? string.Empty), new SqlParameter("@p2", pasillo), new SqlParameter("@p3", fila),
                new SqlParameter("@p4", columna)).ToListAsync().ConfigureAwait(false)).FirstOrDefault();
    }
}
