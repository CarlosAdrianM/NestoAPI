using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#423 (Slice 2): el DISPARO de las campañas con fechas.
    ///
    /// El Slice 1 hizo que una fila de <see cref="DescuentosProducto"/> caducada dejara de contar,
    /// tanto en el motor de precios como en el mensaje que viaja a la tienda. Pero eso solo arregla
    /// la mitad: <b>una campaña que caduca por fecha no modifica ninguna fila</b>, así que el job
    /// nocturno de stocks (#410), que detecta cambios por <c>[Fecha Modificación]</c>, no la ve —
    /// y la tienda se quedaría anunciando para siempre un descuento que Nesto ya no cobra.
    ///
    /// Es exactamente la misma trampa que un DELETE a mano en la tabla: el dato cambia sin dejar
    /// rastro que ningún disparador pueda detectar. La única salida es preguntar por las FECHAS.
    ///
    /// Este job pregunta cada madrugada: ¿qué campañas han empezado o terminado últimamente? Y
    /// encola sus productos para que la pasada de los 5 minutos los republique con los precios y
    /// descuentos de hoy.
    /// </summary>
    public class VigenciaCampanasJobsService
    {
        /// <summary>
        /// Ventana de 2 días para una pasada diaria, mismo criterio que
        /// <see cref="SincronizacionStocksJobsService.HORAS_VENTANA_NOCTURNA"/>: si una noche el
        /// job no corre (reinicio, error), la siguiente recoge también lo que se perdió. Encolar
        /// de más es gratis —se publica el estado ACTUAL, y hay guarda de no reencolar lo que ya
        /// está pendiente—, pero callar de más deja la tienda con una oferta muerta puesta.
        ///
        /// Regla: ventana ≈ 2-3 veces el intervalo. Si algún día pasa a ser horario, cambiar el
        /// cron Y esta constante a la vez.
        /// </summary>
        internal const int DIAS_VENTANA = 2;

        internal const string USUARIO_ENCOLADO = "Vigencia campañas";

        /// <summary>Punto de entrada para Hangfire (job recurrente).</summary>
        public static async Task EncolarProductosConCampanasQueCambian(int diasVentana)
        {
            Console.WriteLine("🚀 [Hangfire] Encolando productos con campañas que empiezan o terminan...");

            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;

                    List<string> productos = await ProductosARepublicar(db, DateTime.Today, diasVentana).ConfigureAwait(false);

                    // NestoAPI#433: la lista entera en una sentencia
                    _ = await db.EncolarProductosSync(productos, USUARIO_ENCOLADO).ConfigureAwait(false);

                    string resumen = $"Vigencia de campañas: {productos.Count} productos encolados en Nesto_sync " +
                        $"(campañas que empiezan o terminan en los últimos {diasVentana} días); " +
                        $"la sincronización los publicará en los próximos minutos";
                    Console.WriteLine($"✅ [Hangfire] {resumen}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Hangfire] Error encolando productos con campañas que cambian: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }

        /// <summary>
        /// Los productos cuyas campañas han empezado o terminado dentro de la ventana.
        ///
        /// Los filtros son LOS MISMOS que los de
        /// <see cref="ProductoDTO.CargarDescuentosPorAudiencia"/>, y tienen que seguir siéndolo:
        /// no tiene sentido republicar por una fila que de todas formas no viaja (una de cliente,
        /// una escalonada, una con AudienciaOferta = 0, una con FiltroProducto). <b>Los dos
        /// conjuntos de filtros van acoplados a propósito: si uno cambia, el otro también.</b>
        ///
        /// Desde el Slice 3 se resuelven también las campañas por FAMILIA y por familia+grupo,
        /// expandiéndolas a sus productos: una fila de marca cambia el mensaje de las 62
        /// referencias de esa marca, y ninguna de ellas se entera por su cuenta.
        /// </summary>
        internal static async Task<List<string>> ProductosARepublicar(NVEntities db, DateTime hoy, int diasVentana)
        {
            DateTime desde = hoy.AddDays(-diasVentana);

            // Se pregunta por la ventana ENTERA, sin distinguir si la fecha que cayó dentro es la
            // de inicio o la de fin: una campaña que arranca hoy hay que publicarla y una que
            // terminó ayer hay que retirarla, y las dos se resuelven igual — republicando el
            // producto con lo que valga HOY. Una campaña cuyo FechaHasta es justo hoy entra
            // también, y volverá a entrar mañana ya caducada: es el solape que hace la ventana
            // tolerante a fallos, y republicar de más no cambia ningún precio.
            List<DescuentosProducto> campanas = await db.DescuentosProductoes
                .Where(d => d.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && (d.Nº_Cliente == null || d.Nº_Cliente.Trim() == string.Empty)
                    && (d.NºProveedor == null || d.NºProveedor.Trim() == string.Empty)
                    && d.FiltroProducto == null
                    && d.CantidadMínima < 2
                    && d.AudienciaOferta > 0
                    && ((d.FechaDesde != null && d.FechaDesde >= desde && d.FechaDesde <= hoy)
                     || (d.FechaHasta != null && d.FechaHasta >= desde && d.FechaHasta <= hoy)))
                .ToListAsync().ConfigureAwait(false);

            // La expansión a productos es LA MISMA que usa el mantenimiento de campañas al
            // guardar: si divergieran, una campaña de marca republicaría un juego de referencias
            // al crearla y otro distinto al caducar.
            return await AlcanceCampanas.ProductosAfectados(db, campanas).ConfigureAwait(false);
        }
    }
}
