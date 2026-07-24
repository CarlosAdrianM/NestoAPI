using System;
using System.Configuration;
using System.Threading.Tasks;
using Elmah;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: job de Hangfire para la rotación automática del client_secret LWA de Amazon.
    /// Se ejecuta a diario: sondea la cola SQS (persiste secretos nuevos) y rota si el secreto
    /// almacenado está cerca de caducar. Si dispara una rotación, reprograma un sondeo en ~5 min
    /// para capturar el secreto nuevo sin esperar al run del día siguiente.
    /// </summary>
    public class AmazonCredencialRotacionJobsService
    {
        public static async Task ProcesarRotacionCredenciales()
        {
            try
            {
                AmazonSpApiOpciones opciones = AmazonSpApiOpciones.DesdeConfiguracion();
                int diasAntes = int.TryParse(ConfigurationManager.AppSettings["AmazonSpApi:RotacionDiasAntes"], out int d) ? d : 15;

                // Guard: si aún no está configurado (cola SQS o credenciales AWS IAM en secretos.config),
                // omitir sin error para no spamear ELMAH a diario hasta que se complete el despliegue.
                if (string.IsNullOrEmpty(opciones.SqsQueueUrl) ||
                    string.IsNullOrEmpty(opciones.AwsAccessKey) ||
                    string.IsNullOrEmpty(opciones.AwsSecretKey))
                {
                    ErrorLog.GetDefault(null)?.Log(new Error(new Exception(
                        "[AmazonRotacion] Omitido: falta configuración (AmazonSpApi:SqsQueueUrl o credenciales AWS IAM en secretos.config).")));
                    return;
                }

                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;

                    IAmazonSpApiGateway gateway = new AmazonSpApiGateway(opciones);
                    IAmazonCredencialStore store = new AmazonCredencialStore(db);
                    IServicioRotacionCredencialesAmazon servicio =
                        new ServicioRotacionCredencialesAmazon(gateway, store, diasAntes);

                    ResultadoProcesoRotacion resultado = await servicio.ProcesarColaAsync().ConfigureAwait(false);

                    // NestoAPI#361: no ensuciar ELMAH con el resultado cuando la pasada es un no-op
                    // (cola vacía, nada que rotar): solo se registra si hubo mensajes o rotación real.
                    // El "ha corrido el job" ya se ve en el panel de Hangfire.
                    if (resultado.MensajesRecibidos > 0 || resultado.RotacionDisparada)
                    {
                        ErrorLog.GetDefault(null)?.Log(new Error(
                            new Exception("[AmazonRotacion] " + resultado.Resumen())));
                    }

                    if (resultado.RotacionDisparada)
                    {
                        // El secreto nuevo llega a SQS en segundos; capturarlo pronto sin esperar al run diario.
                        Hangfire.BackgroundJob.Schedule(
                            () => ProcesarRotacionCredenciales(),
                            TimeSpan.FromMinutes(5));
                    }
                }
            }
            catch (Exception)
            {
                throw; // Re-lanzar para que Hangfire lo registre y reintente
            }
        }
    }
}
