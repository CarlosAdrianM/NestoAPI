using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#366: job Hangfire que cierra el bucle de la subida de facturas a Amazon. El feed
    /// UPLOAD_VAT_INVOICE se procesa en diferido, así que las filas quedan en estado ENVIADA y este
    /// job consulta getFeed hasta conocer el resultado (DONE/FATAL/CANCELLED), guardando el informe
    /// de proceso cuando lo hay. Una factura rechazada se ve en la tabla sin ir a Seller Central.
    /// </summary>
    public class AmazonFacturasJobsService
    {
        private readonly IAlmacenFacturasAmazon _almacen;
        private readonly IAmazonFeedsGateway _gateway;

        public AmazonFacturasJobsService(IAlmacenFacturasAmazon almacen, IAmazonFeedsGateway gateway)
        {
            _almacen = almacen;
            _gateway = gateway;
        }

        /// <summary>Punto de entrada del job recurrente (composición por defecto).</summary>
        public static async Task ComprobarResultadosFeeds()
        {
            using (var db = new NVEntities())
            {
                var servicio = new AmazonFacturasJobsService(
                    new AlmacenFacturasAmazon(db),
                    new AmazonFeedsGateway(new AmazonCredencialStore(db)));
                await servicio.ProcesarPendientesAsync().ConfigureAwait(false);
            }
        }

        public async Task ProcesarPendientesAsync()
        {
            IReadOnlyList<AmazonFacturaSubida> pendientes = _almacen.ObtenerPendientesResultado();
            foreach (AmazonFacturaSubida fila in pendientes)
            {
                if (string.IsNullOrWhiteSpace(fila.FeedId))
                {
                    continue;
                }
                try
                {
                    AmazonFeedEstado estado = await _gateway.ObtenerFeedAsync(fila.FeedId.Trim()).ConfigureAwait(false);
                    if (!EsEstadoFinal(estado.ProcessingStatus))
                    {
                        continue; // sigue en cola/proceso: se volverá a mirar en la siguiente pasada
                    }
                    string informe = await _gateway.DescargarInformeFeedAsync(estado.ResultFeedDocumentId).ConfigureAwait(false);
                    _almacen.ActualizarResultado(fila.Id, estado.ProcessingStatus, Recortar(informe));
                }
                catch (Exception ex)
                {
                    // Un fallo puntual (red, throttling) no debe parar el resto; se reintentará en
                    // la siguiente pasada porque la fila sigue en ENVIADA. Fuera de HTTP se loguea
                    // con ErrorLog.GetDefault (mismo patrón que el resto de jobs).
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"AmazonFacturasJobs: error comprobando el feed {fila.FeedId} (pedido {fila.Pedido}): {ex.Message}", ex)));
                }
            }
        }

        internal static bool EsEstadoFinal(string processingStatus)
            => processingStatus == EstadosFacturaAmazon.DONE
            || processingStatus == EstadosFacturaAmazon.FATAL
            || processingStatus == EstadosFacturaAmazon.CANCELLED;

        // El informe de Amazon puede ser largo; con los primeros caracteres basta para diagnosticar.
        private static string Recortar(string informe)
            => informe != null && informe.Length > 4000 ? informe.Substring(0, 4000) : informe;
    }
}
