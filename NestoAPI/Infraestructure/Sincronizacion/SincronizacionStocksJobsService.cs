using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#410: cada noche encola en Nesto_sync los productos que han tenido movimientos de
    /// stock, para que el job de sincronización de las 5 minutos los republique y los stocks de
    /// Odoo y PrestaShop no se desvíen de los de Nesto. No publica nada por sí mismo: solo encola;
    /// el mensaje de Productos ya lleva los Stocks calculados.
    ///
    /// Ventana de 2 días en vez de "ayer" o de una marca de agua: si una noche el job no corre
    /// (reinicio, error), la siguiente pasada recoge también los movimientos perdidos, y la
    /// idempotencia (publicar el estado ACTUAL + la guarda de no reencolar lo pendiente) hace que
    /// el solape no cueste nada. Mismo patrón que ComparativaAgenciaSombraJobsService.
    ///
    /// Se encola con que HAYA movimiento (EXISTS), no con que el neto de la ventana sea distinto
    /// de 0: un +5 hoy y -5 mañana netean a 0, pero si entre medias hubo una publicación orgánica
    /// con el +5, el consumidor se quedaría con el stock inflado para siempre. Encolar de más es
    /// gratis; callar de más deja stocks mal.
    ///
    /// NestoAPI#818 (Carlos 05/11/26): los movimientos de ExtractoProducto ya se encolan AL MOMENTO con el disparador
    /// trgExtractoProducto_SyncTienda (Scripts/Issue818_ExtractoProducto_EncolarSyncTienda.sql): todo apunte cuya
    /// sentencia no sume cero por producto y almacén encola el producto y sus kits, y lo recibido se ve en la web en
    /// la siguiente pasada de 5 minutos. Por eso NO se ha pasado este job a horario (la segunda parte de la issue): ya
    /// no hace falta para el stock. Se queda de noche como red para lo que no pasa por el extracto (LinPedidoVta
    /// grabada por fuera de la API, DescuentosProducto) y para el encolado que el disparador pierda (su CATCH no
    /// relanza para no tumbar nunca el apunte).
    /// </summary>
    public class SincronizacionStocksJobsService
    {
        /// <summary>
        /// 48 h para la pasada nocturna. La ventana debe acompañar a la cadencia: si algún día el
        /// job pasa a ser horario (idea de Carlos, 26/08/26), en el registro de Hangfire basta
        /// cambiar el cron y pasar una ventana de ~3 horas — con la de 48 h se reencolarían los
        /// mismos ~500 productos en cada pasada. Regla: ventana ≈ 2-3 veces el intervalo, para
        /// que una pasada fallida quede cubierta por la siguiente.
        /// </summary>
        internal const int HORAS_VENTANA_NOCTURNA = 48;
        internal const string USUARIO_ENCOLADO = "Sync stocks nocturno";

        /// <summary>
        /// Los mismos almacenes y empresas (titular + espejo) que usa
        /// ProductoService.CalcularStockProducto para calcular el stock que viaja.
        /// </summary>
        internal const string SQL_ENCOLAR_PRODUCTOS_CON_MOVIMIENTOS = @"
                        INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
                        SELECT 'Productos', RTRIM(p.Número), @usuario, GETDATE()
                        FROM Productos p
                        WHERE p.Empresa = @empresa
                          AND p.Estado >= 0
                          AND (EXISTS (SELECT 1 FROM ExtractoProducto e
                                      WHERE e.Empresa IN (@empresa, @empresaEspejo)
                                        AND e.Número = p.Número
                                        AND e.Almacén IN (@almacen1, @almacen2, @almacen3)
                                        AND e.Fecha >= @desde)
                               -- NestoAPI#412: un kit también cambia cuando se mueve UN COMPONENTE
                               -- (su CantidadMontable depende del stock de los componentes y el
                               -- kit no tiene movimiento propio), así que se encola igualmente.
                               OR EXISTS (SELECT 1 FROM Kits k
                                          INNER JOIN ExtractoProducto e ON e.Número = k.NúmeroAsociado
                                      WHERE k.Empresa = @empresa
                                        AND k.Número = p.Número
                                        AND e.Empresa IN (@empresa, @empresaEspejo)
                                        AND e.Almacén IN (@almacen1, @almacen2, @almacen3)
                                        AND e.Fecha >= @desde)
                               -- NestoAPI#413: un alta/cambio/borrado lógico en los descuentos de
                               -- tarifa del producto también cambia lo que viaja (incluido pasar
                               -- AudienciaOferta a 0, que debe RETIRAR la oferta de la web), así que
                               -- cualquier fila tocada en la ventana encola el producto.
                               OR EXISTS (SELECT 1 FROM DescuentosProducto d
                                      WHERE d.Empresa = @empresa
                                        AND d.[Nº Producto] = p.Número
                                        AND d.[Fecha Modificación] >= @desde)
                               -- NestoAPI#705: un pedido pendiente compromete stock (PendienteEntregar,
                               -- y con él CantidadDisponibleTotal) sin movimiento en ExtractoProducto.
                               -- La API ya encola al grabar líneas; esto recoge lo que se cambia por
                               -- fuera (Nesto viejo, SPs) o si aquel encolado falló. Sin filtrar por
                               -- estado: pasar una línea a presupuesto (-3) o a albarán también cambia
                               -- el disponible. Sin índice por [Fecha Modificación]: es un único
                               -- recorrido de LinPedidoVta (~140.000 lecturas, ~0,5 s el 27/10/26), que
                               -- de noche no molesta. Los borrados de líneas no se ven aquí (ya no están).
                               OR EXISTS (SELECT 1 FROM LinPedidoVta l
                                      WHERE l.Empresa IN (@empresa, @empresaEspejo)
                                        AND l.Producto = p.Número
                                        AND l.Almacén IN (@almacen1, @almacen2, @almacen3)
                                        AND l.TipoLinea = 1
                                        AND l.[Fecha Modificación] >= @desde))
                          AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                          WHERE ns.Tabla = 'Productos'
                                            AND ns.ModificadoId = RTRIM(p.Número)
                                            AND ns.Sincronizado IS NULL)";

        /// <summary>
        /// Carlos 17/11/26 (Novedades 1308, Enrique: la 41329 salía sin stock en la web con una unidad en Algete): desde el
        /// deploy del 16/10 (incidencia 645, CantidadDisponibleTotal) la tienda calcula el stock con un dato que solo llevan
        /// los mensajes nuevos, y los productos sin movimiento desde entonces no se han vuelto a mandar. Pasada ÚNICA: con
        /// ParametrosUsuario «(defecto)» <see cref="PARAMETRO_REENVIAR_CON_STOCK"/> = «1», la pasada de la noche encola además
        /// TODOS los productos activos con stock en ALG/REI/ALC (empresas 1 + espejo) y deja el parámetro a «0». Así corre
        /// en Hangfire, de noche, sin nadie delante. Para repetirla, volver a poner «1».
        /// </summary>
        internal const string PARAMETRO_REENVIAR_CON_STOCK = "SincronizarStocks.ReenviarConStock";
        internal const string USUARIO_REENVIO_CON_STOCK = "Reenvío con stock (1308)";
        /// <summary>
        /// Los productos sin mensaje desde este día también se reenvían, tengan o no stock en Nesto: el mensaje con
        /// CantidadDisponibleTotal empezó el 16/10 por la tarde, y una ficha con stock en la tienda y a 0 en Nesto se vendería
        /// sin tener nada (agente de PrestaShop, NestoAPI#872: 2.858 fichas activas sin mensaje desde el 19/10).
        /// </summary>
        internal static readonly DateTime FECHA_CORTE_MENSAJE_COMPLETO = new DateTime(2026, 10, 17);

        internal const string SQL_REENVIAR_PRODUCTOS_CON_STOCK = @"
                        DECLARE @encolados int = 0;
                        IF EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = @empresa AND Usuario = '(defecto)'
                                   AND Clave = @parametro AND RTRIM(Valor) = '1')
                        BEGIN
                            INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
                            SELECT 'Productos', RTRIM(p.Número), @usuario, GETDATE()
                            FROM Productos p
                            LEFT JOIN (SELECT e.Número FROM ExtractoProducto e
                                       WHERE e.Empresa IN (@empresa, @empresaEspejo)
                                         AND e.Almacén IN (@almacen1, @almacen2, @almacen3)
                                       GROUP BY e.Número
                                       HAVING SUM(e.Cantidad) <> 0) s ON s.Número = p.Número
                            WHERE p.Empresa = @empresa
                              AND p.Estado >= 0
                              -- con stock, o SIN mensaje desde el corte (agente de PrestaShop, NestoAPI#872: fichas con stock en la
                              -- tienda y a 0 en Nesto se venderían sin tener nada si no reciben su mensaje)
                              AND (s.Número IS NOT NULL
                                   OR NOT EXISTS (SELECT 1 FROM Nesto_sync u
                                                  WHERE u.Tabla = 'Productos' AND u.ModificadoId = RTRIM(p.Número)
                                                    AND u.Sincronizado >= @corte))
                              AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                              WHERE ns.Tabla = 'Productos'
                                                AND ns.ModificadoId = RTRIM(p.Número)
                                                AND ns.Sincronizado IS NULL);
                            SET @encolados = @@ROWCOUNT;
                            UPDATE ParametrosUsuario SET Valor = '0', Usuario2 = @usuario, [Fecha Modificación] = GETDATE()
                            WHERE Empresa = @empresa AND Usuario = '(defecto)' AND Clave = @parametro;
                        END
                        SELECT @encolados;";

        /// <summary>Punto de entrada para Hangfire (job recurrente).</summary>
        public static async Task EncolarProductosConMovimientos(int horasVentana)
        {
            Console.WriteLine("🚀 [Hangfire] Encolando productos con movimientos de stock para sincronizar...");

            try
            {
                using (var db = new NVEntities())
                {
                    int encolados = await db.Database.ExecuteSqlCommandAsync(SQL_ENCOLAR_PRODUCTOS_CON_MOVIMIENTOS,
                        new SqlParameter("@usuario", USUARIO_ENCOLADO),
                        new SqlParameter("@empresa", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                        new SqlParameter("@empresaEspejo", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
                        new SqlParameter("@almacen1", Constantes.Almacenes.ALGETE),
                        new SqlParameter("@almacen2", Constantes.Almacenes.REINA),
                        new SqlParameter("@almacen3", Constantes.Almacenes.ALCOBENDAS),
                        new SqlParameter("@desde", DateTime.Now.AddHours(-horasVentana)));

                    string resumen = $"Sync de stocks: {encolados} productos con movimientos encolados en Nesto_sync " +
                        $"(ventana de {horasVentana} horas); el job de sincronización los publicará en los próximos minutos";
                    Console.WriteLine($"✅ [Hangfire] {resumen}");

                    // Carlos 17/11/26 (1308): la pasada única de los productos con stock, si está pedida
                    db.Database.CommandTimeout = 600;
                    int conStock = (await db.Database.SqlQuery<int>(SQL_REENVIAR_PRODUCTOS_CON_STOCK,
                        new SqlParameter("@usuario", USUARIO_REENVIO_CON_STOCK),
                        new SqlParameter("@parametro", PARAMETRO_REENVIAR_CON_STOCK),
                        new SqlParameter("@corte", FECHA_CORTE_MENSAJE_COMPLETO),
                        new SqlParameter("@empresa", Constantes.Empresas.EMPRESA_POR_DEFECTO),
                        new SqlParameter("@empresaEspejo", Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO),
                        new SqlParameter("@almacen1", Constantes.Almacenes.ALGETE),
                        new SqlParameter("@almacen2", Constantes.Almacenes.REINA),
                        new SqlParameter("@almacen3", Constantes.Almacenes.ALCOBENDAS))
                        .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                    if (conStock > 0)
                    {
                        ElmahHelper.Log(new Exception($"[Sync stocks] (informativo) Reenvío único de productos con stock (1308): {conStock} encolados en Nesto_sync; parámetro {PARAMETRO_REENVIAR_CON_STOCK} vuelto a 0."));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error encolando productos con movimientos: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }
    }
}
