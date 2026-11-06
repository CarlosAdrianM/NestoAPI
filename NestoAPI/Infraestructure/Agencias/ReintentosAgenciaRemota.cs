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
    /// fallo de conexión, HTTP 5xx (AgenciaRemotaException.EsTransitoria) y timeout del HttpClient
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
                .Handle<AgenciaRemotaException>(ex => ex.EsTransitoria)
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

        public bool ModificarCreaEnvioNuevo => _interior.ModificarCreaEnvioNuevo;

        public IReadOnlyList<IntercambioRemoto> Intercambios => _interior.Intercambios;

        // NO idempotente: sin reintentos (ver doc de la clase).
        public Task<ResultadoTramitacionRemota> InsertarYEtiquetarAsync(DatosEnvioRemoto envio)
            => _interior.InsertarYEtiquetarAsync(envio);

        // Sin reintentos (#316): tras un timeout no sabemos si la agencia llegó a anular; el segundo
        // intento devolvería "albarán no existe" y lo tomaríamos por un fallo cuando la anulación
        // en realidad se hizo. Mejor que el usuario reintente a mano con el error de conexión claro.
        public Task<ResultadoOperacionRemota> AnularAsync(string albaran)
            => _interior.AnularAsync(albaran);

        // Idempotente (#317) SOLO si la agencia modifica el mismo albarán (Innovatrans): modificar dos
        // veces con los mismos datos deja el envío igual, así que los transitorios sí se reintentan.
        // Si modificar = anular + crear un albarán nuevo (CTT, ModificarCreaEnvioNuevo, sugerencia 507)
        // NO se reintenta: tras un timeout la agencia puede haber creado ya el envío nuevo y repetir
        // duplicaría expediciones. El usuario recibe el aviso «comprueba en la web de CTT…».
        public Task<ResultadoTramitacionRemota> ModificarYEtiquetarAsync(DatosEnvioRemoto envio, string albaran)
            => _interior.ModificarCreaEnvioNuevo
                ? _interior.ModificarYEtiquetarAsync(envio, albaran)
                : _politica.ExecuteAsync(() => _interior.ModificarYEtiquetarAsync(envio, albaran));

        public Task<EtiquetaDataTrans> ReimprimirAsync(string albaran, int? desdeBulto = null, int? hastaBulto = null)
            => _politica.ExecuteAsync(() => _interior.ReimprimirAsync(albaran, desdeBulto, hastaBulto));

        public Task<SeguimientoEnvioRemoto> ConsultarSeguimientoAsync(string albaran)
            => _politica.ExecuteAsync(() => _interior.ConsultarSeguimientoAsync(albaran));
    }

    /// <summary>
    /// 23/09/26: el mismo decorador para una agencia que además sigue POR LOTES (CTT). La consulta por
    /// lotes es idempotente y reintenta los transitorios igual; un corte por cupo
    /// (<see cref="CupoAgenciaAgotadoException"/>) NO es transitorio y sube tal cual, sin reintentar.
    /// </summary>
    public class AgenciaRemotaPorLotesConReintentos : AgenciaRemotaConReintentos, ISeguimientoPorLotes
    {
        private readonly ISeguimientoPorLotes _lotes;
        private readonly AsyncRetryPolicy _politicaLotes;

        public AgenciaRemotaPorLotesConReintentos(IAgenciaRemota interior, ISeguimientoPorLotes lotes, AsyncRetryPolicy politica = null)
            : base(interior, politica)
        {
            _lotes = lotes ?? throw new ArgumentNullException(nameof(lotes));
            _politicaLotes = politica ?? PoliticasAgenciasRemotas.CrearPoliticaTransitorios();
        }

        public Task<IReadOnlyDictionary<string, SeguimientoEnvioRemoto>> ConsultarSeguimientosAsync(DateTime desde, DateTime hasta, IReadOnlyCollection<string> buscados)
            => _politicaLotes.ExecuteAsync(() => _lotes.ConsultarSeguimientosAsync(desde, hasta, buscados));
    }

    /// <summary>
    /// NestoAPI#726 (06/11/26): el decorador de CTT. <see cref="AgenciaRemotaPorLotesConReintentos"/> solo expone
    /// <see cref="IAgenciaRemota"/> y <see cref="ISeguimientoPorLotes"/>, así que en producción (la fábrica devuelve el
    /// decorador, no <see cref="CTT.AgenciaRemotaCTT"/>) se perdían por el camino las capacidades que comprueban los
    /// llamantes con «is»/«as»: la gestión en tránsito (Gestiones decía «La agencia CTT no tiene gestión en tránsito»)
    /// y la etiqueta en PDF de las recogidas (#494). Aquí se reenvían tal cual. La gestión en tránsito SIN reintentos:
    /// parar en vuelo y el retorno no son idempotentes (tras un timeout CTT puede haberlo hecho ya; el retorno daría
    /// un segundo envío) y la fecha y la dirección se dejan igual por prudencia (el usuario reintenta con el error claro).
    /// La etiqueta en PDF es una lectura: con reintentos.
    /// </summary>
    public class AgenciaRemotaCTTConReintentos : AgenciaRemotaPorLotesConReintentos, IGestionEnTransitoAgenciaRemota, IEtiquetaPdfRemota
    {
        private readonly IGestionEnTransitoAgenciaRemota _gestion;
        private readonly IEtiquetaPdfRemota _etiquetaPdf;
        private readonly AsyncRetryPolicy _politicaPdf;

        public AgenciaRemotaCTTConReintentos(CTT.AgenciaRemotaCTT interior, AsyncRetryPolicy politica = null)
            : base(interior, interior, politica)
        {
            _gestion = interior;
            _etiquetaPdf = interior;
            _politicaPdf = politica ?? PoliticasAgenciasRemotas.CrearPoliticaTransitorios();
        }

        public Task<ResultadoOperacionRemota> PararEnVueloAsync(string albaran) => _gestion.PararEnVueloAsync(albaran);

        public Task<string> CrearRetornoAsync(string albaran) => _gestion.CrearRetornoAsync(albaran);

        public Task<ResultadoOperacionRemota> CambiarFechaEntregaAsync(string albaran, DateTime fecha) => _gestion.CambiarFechaEntregaAsync(albaran, fecha);

        public Task<ResultadoOperacionRemota> CambiarDireccionEntregaAsync(string albaran, DireccionEntregaRemota direccion)
            => _gestion.CambiarDireccionEntregaAsync(albaran, direccion);

        public Task<byte[]> ObtenerEtiquetaPdfAsync(string albaran) => _politicaPdf.ExecuteAsync(() => _etiquetaPdf.ObtenerEtiquetaPdfAsync(albaran));
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
