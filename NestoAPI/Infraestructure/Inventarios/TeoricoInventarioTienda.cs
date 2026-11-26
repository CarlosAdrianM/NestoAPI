using NestoAPI.Controllers;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Inventarios
{
    // ------------------------------------------------------------------------------------------------------------------
    // NestoAPI#856, corte 1 (26/11/26): el teórico de una fila NUEVA de inventario en una TIENDA lo pone la API.
    //
    //  - Antes, POST api/Inventarios grababa el StockCalculado que mandara el cliente: Nesto manda 0 para un producto que no
    //    tenía fila (InventarioViewModel.vb), así que una unidad contada de un producto con 1 en stock se apuntaba como +1 al
    //    contabilizar (prdContabilizarInventario apunta StockReal − StockCalculado y no recalcula nada).
    //  - Ahora, en un almacén SIN control de ubicaciones (REI, ALC), StockCalculado = SUM(ExtractoProducto.Cantidad) del
    //    producto en el almacén, empresas 1 y 3 (la misma cuenta que el rellenado, SqlRellenadoInventario.SQL_STOCKS), en el
    //    momento de crear la fila. Algete (con ubicaciones) sigue con lo de la incidencia 1227 (lo libre del hueco) o, si el
    //    producto no se ubica, con lo que mande el cliente, como siempre.
    //  - Solo el POST (la fila nueva). El PUT, las filas rellenadas y refrescar las no contadas al contabilizar son el corte 2
    //    (columna Inventarios.FechaConteo, script para sa); los disparadores, el corte 3.
    //  - Interruptor ParametrosUsuario empresa 1 «(defecto)» Inventario.TeoricoAlContar: sin fila o «1» = encendido; «0»,
    //    false, no, off, apagado = lo de antes. Sin script (el corte 1 de la issue es «solo API»). Caché de 1 minuto.
    //  - Si no se puede leer el stock: lo que mande el cliente (como antes) y ELMAH; nunca rompe el conteo.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>LA REGLA (pura) de quién pone el teórico de una fila nueva.</summary>
    public static class ReglaTeoricoInventarioTienda
    {
        /// <summary>
        /// True si la API pone el teórico con el stock contable del momento: interruptor encendido y almacén sin control de
        /// ubicaciones (en Algete manda el hueco, incidencia 1227).
        /// </summary>
        public static bool LoPoneLaApi(bool interruptorEncendido, bool controlUbicaciones) => interruptorEncendido && !controlUbicaciones;
    }

    public interface ILectorTeoricoInventarioTienda
    {
        /// <summary>El interruptor Inventario.TeoricoAlContar.</summary>
        bool Encendido();
        /// <summary>SUM(ExtractoProducto.Cantidad) del producto en el almacén, empresas 1 y 3 (0 si no hay apuntes).</summary>
        Task<int> StockContable(string almacen, string producto);
    }

    /// <summary>Interruptor por DATO de <see cref="ReglaTeoricoInventarioTienda"/> (se lee sin crear filas, caché de 1 minuto).</summary>
    public static class InterruptorTeoricoAlContar
    {
        public const string CLAVE = "Inventario.TeoricoAlContar";
        internal const string USUARIO_DEFECTO = "(defecto)";
        internal static readonly TimeSpan CACHE = TimeSpan.FromMinutes(1);

        private static readonly object candado = new object();
        private static bool? valor;
        private static DateTime leidoEl;

        public static bool Activo
        {
            get
            {
                lock (candado)
                {
                    if (valor.HasValue && DateTime.UtcNow - leidoEl < CACHE)
                    {
                        return valor.Value;
                    }
                    bool leido;
                    try
                    {
                        leido = Interpretar(ParametrosUsuarioController.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, CLAVE));
                    }
                    catch (Exception ex)
                    {
                        ElmahHelper.Log(new Exception($"[Inventario] No se ha podido leer {CLAVE}: el teórico de las tiendas lo pone la API. {ex.Message}", ex));
                        leido = true;
                    }
                    valor = leido;
                    leidoEl = DateTime.UtcNow;
                    return leido;
                }
            }
        }

        /// <summary>Encendido salvo «0», «false», «no», «off» o «apagado» (vacío o sin fila = encendido).</summary>
        internal static bool Interpretar(string texto)
        {
            string limpio = texto?.Trim().ToLowerInvariant();
            return !(limpio == "0" || limpio == "false" || limpio == "no" || limpio == "off" || limpio == "apagado");
        }
    }

    public class LectorTeoricoInventarioTiendaSql : ILectorTeoricoInventarioTienda
    {
        /// <summary>La cuenta del rellenado (<see cref="SqlRellenadoInventario.SQL_STOCKS"/>) para un producto.</summary>
        internal const string SQL_STOCK = @"SELECT CAST(ISNULL(SUM(CAST(e.Cantidad AS int)), 0) AS int) FROM dbo.ExtractoProducto e
WHERE e.Empresa IN ('1', '3') AND e.Almacén = @p0 AND e.Número = @p1";

        private readonly NVEntities db;

        public LectorTeoricoInventarioTiendaSql(NVEntities db)
        {
            this.db = db;
        }

        public bool Encendido() => InterruptorTeoricoAlContar.Activo;

        public async Task<int> StockContable(string almacen, string producto)
        {
            List<int> filas = await db.Database.SqlQuery<int>(SQL_STOCK, new SqlParameter("@p0", almacen?.Trim() ?? string.Empty),
                new SqlParameter("@p1", producto?.Trim() ?? string.Empty)).ToListAsync().ConfigureAwait(false);
            return filas.FirstOrDefault();
        }
    }
}
