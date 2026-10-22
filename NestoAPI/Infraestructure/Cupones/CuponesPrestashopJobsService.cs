using Hangfire;
using NestoAPI.Models;
using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>
    /// NestoAPI#614: job cupones-prestashop (cada 15 minutos). Lleva a la tienda PrestaShop los cupones que no coinciden (nuevos,
    /// usados, anulados, caducados, que vuelven a valer) y avisa de los que llevan más de Cupones:HorasAvisoEspejo horas sin
    /// poder llevarse. Con Cupones:EspejoPrestashop apagado no hace nada (ni abre la base de datos).
    /// </summary>
    public static class CuponesPrestashopJobsService
    {
        public const string CRON = "*/15 * * * *";

        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task Procesar()
        {
            if (!ReglasEspejoPrestashop.Encendido)
            {
                return;
            }
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    _ = await new ServicioEspejoCuponesPrestashop(db).ProcesarPendientes(ReglasEspejoPrestashop.HorasAviso).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Cupones #614] Ha fallado el job cupones-prestashop: {ex.Message}", ex));
            }
        }
    }
}
