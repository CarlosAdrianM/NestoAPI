using NestoAPI.Models;
using System;
using System.Data.SqlClient;
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

        /// <summary>Punto de entrada para Hangfire (job recurrente).</summary>
        public static async Task EncolarProductosConMovimientos(int horasVentana)
        {
            Console.WriteLine("🚀 [Hangfire] Encolando productos con movimientos de stock para sincronizar...");

            try
            {
                using (var db = new NVEntities())
                {
                    // Los mismos almacenes y empresas (titular + espejo) que usa
                    // ProductoService.CalcularStockProducto para calcular el stock que viaja.
                    int encolados = await db.Database.ExecuteSqlCommandAsync(@"
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
                                        AND d.[Fecha Modificación] >= @desde))
                          AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                                          WHERE ns.Tabla = 'Productos'
                                            AND ns.ModificadoId = RTRIM(p.Número)
                                            AND ns.Sincronizado IS NULL)",
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
