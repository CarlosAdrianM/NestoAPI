using NestoAPI.Infraestructure.Agencias.Innovatrans;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Política de reintentos para TRANSITORIOS de transporte contra los servicios de las agencias
    /// (NestoAPI#288, punto 1). Reintenta SOLO excepciones que pueden desaparecer en segundos:
    /// fallo de conexión, HTTP 5xx (DataTransException.EsTransitoria) y timeout del HttpClient
    /// (TaskCanceledException). 2 reintentos con backoff corto (1s, 2s): un hipo puntual se salva,
    /// una degradación larga (asmred 15-50 min, #266) NO se combate aquí — eso lo resuelve
    /// re-programar la pasada entera (Hangfire, aa389e9), no alargarla con reintentos.
    /// Nota GLS: su ConsultarSeguimientoAsync se traga los transitorios y devuelve Desconocido
    /// (#264), que NO se reintenta a propósito: Desconocido también significa "expedición no
    /// encontrada" y reintentarlo triplicaría las consultas en cada pasada (ráfagas que GLS castiga).
    /// </summary>
    public static class PoliticasAgenciasRemotas
    {
        public const int REINTENTOS = 2;

        public static AsyncRetryPolicy CrearPoliticaTransitorios()
        {
            return Policy
                .Handle<DataTransException>(ex => ex.EsTransitoria)
                .Or<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(REINTENTOS, intento => TimeSpan.FromSeconds(intento));
        }
    }

    /// <summary>
    /// Decorador de <see cref="IAgenciaRemota"/> que reintenta los transitorios en las operaciones
    /// IDEMPOTENTES (consultar seguimiento, reimprimir etiqueta). InsertarYEtiquetarAsync NO se
    /// reintenta jamás: crea un envío real y tras un timeout no sabemos si la agencia llegó a
    /// registrarlo — reintentar podría duplicar expediciones. Se aplica en FabricaAgenciasRemotas
    /// (el punto único de #258), así que todo llamante queda cubierto sin saber de Polly.
    /// </summary>
    public class AgenciaRemotaConReintentos : IAgenciaRemota
    {
        private readonly IAgenciaRemota _interior;
        private readonly AsyncRetryPolicy _politica;

        public AgenciaRemotaConReintentos(IAgenciaRemota interior, AsyncRetryPolicy politica = null)
        {
            _interior = interior ?? throw new ArgumentNullException(nameof(interior));
            _politica = politica ?? PoliticasAgenciasRemotas.CrearPoliticaTransitorios();
        }

        public bool LoggingDetallado => _interior.LoggingDetallado;

        public IReadOnlyList<IntercambioRemoto> Intercambios => _interior.Intercambios;

        // NO idempotente: sin reintentos (ver doc de la clase).
        public Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
            => _interior.InsertarYEtiquetarAsync(envio);

        // Sin reintentos (#316): tras un timeout no sabemos si la agencia llegó a anular; el segundo
        // intento devolvería "albarán no existe" y lo tomaríamos por un fallo cuando la anulación
        // en realidad se hizo. Mejor que el usuario reintente a mano con el error de conexión claro.
        public Task<ResultadoOperacionRemota> AnularAsync(string albaran)
            => _interior.AnularAsync(albaran);

        // Idempotente (#317): modificar dos veces con los mismos datos deja el envío igual, así que
        // los transitorios sí se reintentan.
        public Task<ResultadoTramitacionRemota> ModificarYEtiquetarAsync(DatosEnvioRemoto envio, string albaran)
            => _politica.ExecuteAsync(() => _interior.ModificarYEtiquetarAsync(envio, albaran));

        public Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null)
            => _politica.ExecuteAsync(() => _interior.ReimprimirAsync(albaran, desdeBulto, hastaBulto));

        public Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
            => _politica.ExecuteAsync(() => _interior.ConsultarSeguimientoAsync(albaran));
    }

    /// <summary>
    /// Decorador de <see cref="ISeguimientoAgenciaRemota"/> (agencias que solo siguen, hoy GLS) con
    /// la misma política de transitorios. Hoy GLS no deja escapar transitorios (los convierte en
    /// Desconocido), pero el decorador cubre cualquier excepción transitoria futura del cliente.
    /// </summary>
    public class SeguimientoAgenciaRemotaConReintentos : ISeguimientoAgenciaRemota
    {
        private readonly ISeguimientoAgenciaRemota _interior;
        private readonly AsyncRetryPolicy _politica;

        public SeguimientoAgenciaRemotaConReintentos(ISeguimientoAgenciaRemota interior, AsyncRetryPolicy politica = null)
        {
            _interior = interior ?? throw new ArgumentNullException(nameof(interior));
            _politica = politica ?? PoliticasAgenciasRemotas.CrearPoliticaTransitorios();
        }

        public bool LoggingDetallado => _interior.LoggingDetallado;

        public Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
            => _politica.ExecuteAsync(() => _interior.ConsultarSeguimientoAsync(albaran));
    }
}
