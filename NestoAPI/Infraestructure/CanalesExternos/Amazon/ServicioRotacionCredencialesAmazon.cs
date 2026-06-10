using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NestoAPI.Models.CanalesExternos;

namespace NestoAPI.Infraestructure.CanalesExternos.Amazon
{
    /// <summary>
    /// NestoAPI#225: orquesta la rotación. Diseño:
    ///  - El secreto nuevo SIEMPRE se persiste en cuanto llega por SQS (independiente de quién rotó).
    ///  - El disparo de rotación es por tiempo (SecretExpiry dentro de umbral), no por depender de
    ///    cazar la notificación EXPIRY transitoria. Las EXPIRY recibidas se borran (informativas).
    ///  - Guard anti doble-rotación: OldSecretExpiry futuro = rotación pendiente (gracia 7 días).
    /// </summary>
    public class ServicioRotacionCredencialesAmazon : IServicioRotacionCredencialesAmazon
    {
        private const string UsuarioRotacion = "rotacion-automatica";

        private readonly IAmazonSpApiGateway _gateway;
        private readonly IAmazonCredencialStore _store;
        private readonly int _diasAntesRotar;
        private readonly Func<DateTime> _reloj;

        public ServicioRotacionCredencialesAmazon(
            IAmazonSpApiGateway gateway,
            IAmazonCredencialStore store,
            int diasAntesRotar = 15,
            Func<DateTime> reloj = null)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _diasAntesRotar = diasAntesRotar > 0 ? diasAntesRotar : 15;
            _reloj = reloj ?? (() => DateTime.UtcNow);
        }

        public AmazonSpApiCredencial ObtenerCredencialActual()
        {
            return _store.Obtener();
        }

        public async Task<ResultadoProcesoRotacion> ProcesarColaAsync()
        {
            ResultadoProcesoRotacion r = new ResultadoProcesoRotacion();

            AmazonSpApiCredencial cred = _store.Obtener();
            if (cred == null)
            {
                r.Detalles.Add("No hay credencial en BD; nada que procesar.");
                return r;
            }

            // 1. Vaciar la cola: persistir NEW_SECRET, descartar EXPIRY/otros.
            var mensajes = await _gateway.RecibirMensajesColaAsync().ConfigureAwait(false);
            r.MensajesRecibidos = mensajes.Count;

            foreach (AmazonSqsMessage m in mensajes)
            {
                AmazonNotificationPayload n = TryParse(m.Body);
                if (n == null)
                {
                    r.Detalles.Add("Mensaje no parseable; se ignora (no se borra).");
                    continue;
                }

                if (n.NotificationType == AmazonNotificationPayload.TipoNuevoSecreto)
                {
                    var ns = n.Payload?.ApplicationOAuthClientNewSecret;
                    if (ns == null || ns.ClientId != cred.ClientId)
                    {
                        r.Detalles.Add("NEW_SECRET ignorado (clientId no coincide).");
                        continue;
                    }
                    _store.GuardarSecretoNuevo(cred.ClientId, ns.NewClientSecret,
                        ns.NewClientSecretExpiryTime, ns.OldClientSecretExpiryTime, UsuarioRotacion);
                    r.SecretosPersistidos++;
                    r.Detalles.Add($"Secreto nuevo persistido (caduca {ns.NewClientSecretExpiryTime:yyyy-MM-dd}).");
                    await BorrarSeguro(m, r).ConfigureAwait(false);
                }
                else if (n.NotificationType == AmazonNotificationPayload.TipoCaducidadSecreto)
                {
                    r.Detalles.Add("EXPIRY recibido (informativo); el disparo de rotación es por tiempo.");
                    await BorrarSeguro(m, r).ConfigureAwait(false);
                }
                else
                {
                    r.Detalles.Add($"Tipo no manejado: {n.NotificationType}; se ignora.");
                }
            }

            // 2. Releer si persistimos, para evaluar el umbral con el SecretExpiry actualizado.
            if (r.SecretosPersistidos > 0)
            {
                cred = _store.Obtener();
            }

            // 3. ¿Toca rotar? (por tiempo, con guard anti doble-rotación)
            if (DebeRotar(cred))
            {
                await RotarInternoAsync(cred).ConfigureAwait(false);
                _store.MarcarRotacionSolicitada(cred.ClientId);
                r.RotacionDisparada = true;
                r.Detalles.Add("Rotación disparada (secreto dentro del umbral de caducidad).");
            }

            return r;
        }

        public async Task<bool> RotarAhoraAsync()
        {
            AmazonSpApiCredencial cred = _store.Obtener();
            if (cred == null)
            {
                return false;
            }
            await RotarInternoAsync(cred).ConfigureAwait(false);
            _store.MarcarRotacionSolicitada(cred.ClientId);
            return true;
        }

        private bool DebeRotar(AmazonSpApiCredencial cred)
        {
            if (cred?.SecretExpiry == null)
            {
                return false; // sin fecha de caducidad conocida no rotamos automáticamente
            }
            DateTime ahora = _reloj();
            bool cercaDeCaducar = cred.SecretExpiry.Value <= ahora.AddDays(_diasAntesRotar);
            bool rotacionPendiente = cred.OldSecretExpiry.HasValue && cred.OldSecretExpiry.Value > ahora;
            return cercaDeCaducar && !rotacionPendiente;
        }

        private async Task RotarInternoAsync(AmazonSpApiCredencial cred)
        {
            string token = await _gateway.ObtenerTokenRotacionAsync(cred.ClientId, cred.ClientSecret).ConfigureAwait(false);
            await _gateway.RotarClientSecretAsync(token).ConfigureAwait(false);
        }

        private async Task BorrarSeguro(AmazonSqsMessage m, ResultadoProcesoRotacion r)
        {
            if (string.IsNullOrEmpty(m.ReceiptHandle))
            {
                return;
            }
            try
            {
                await _gateway.BorrarMensajeColaAsync(m.ReceiptHandle).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // No es crítico: el mensaje volverá a verse y se reprocesará de forma idempotente.
                r.Detalles.Add($"No se pudo borrar un mensaje de SQS: {ex.Message}");
            }
        }

        private static AmazonNotificationPayload TryParse(string body)
        {
            try
            {
                return JsonConvert.DeserializeObject<AmazonNotificationPayload>(body);
            }
            catch
            {
                return null;
            }
        }
    }
}
