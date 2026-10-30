using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#767: los jobs que rellenan DireccionesCoordenadas con Nominatim. La PDA NUNCA espera a Nominatim: GET Envios y
    /// POST OrdenSugerido leen la caché y, si falta alguna, piden <see cref="EncolarParadas"/> (un job, como mucho uno cada
    /// <see cref="INTERVALO_MINIMO_ENCOLAR"/>), que busca las paradas en reparto a 1 petición/s. De noche,
    /// <see cref="GeocodificarClientesRutasPropias"/> adelanta las direcciones de los clientes activos de las rutas propias
    /// (parámetro RutasRepartoPropio), con tope para no hacer «bulk geocoding» (política de Nominatim).
    /// </summary>
    public static class GeocodificacionRepartoJobsService
    {
        public const string JOB_NOCTURNO = "geocodificar-clientes-rutas-propias";
        internal const int MAXIMO_PARADAS_POR_PASADA = 100;
        internal const int MAXIMO_CLIENTES_POR_NOCHE = 300;
        internal static readonly TimeSpan INTERVALO_MINIMO_ENCOLAR = TimeSpan.FromMinutes(2);

        private static readonly object candado = new object();
        private static DateTime ultimoEncolado = DateTime.MinValue;

        /// <summary>Encola <see cref="GeocodificarParadas"/> si no se ha encolado en los últimos 2 minutos. Nunca lanza.</summary>
        public static void EncolarParadas()
        {
            lock (candado)
            {
                if (DateTime.UtcNow - ultimoEncolado < INTERVALO_MINIMO_ENCOLAR)
                {
                    return;
                }
                ultimoEncolado = DateTime.UtcNow;
            }
            try
            {
                _ = BackgroundJob.Enqueue(() => GeocodificarParadas());
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto propio #767] No se pudo encolar la geocodificación de las paradas: {ex.Message}", ex));
            }
        }

        /// <summary>Las direcciones de los envíos del reparto propio en reparto (los 30 días de la lista) que aún no están en la caché.</summary>
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 600)]
        public static async Task GeocodificarParadas()
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                var servicio = new ServicioGeocodificacion(new RepositorioCoordenadas(db), new GeocodificadorNominatim());
                DateTime hoy = DateTime.Today;
                List<EnviosAgencia> envios = await new RepositorioRepartoPropio(db)
                    .LeerEnviosEnReparto(hoy.AddDays(-ServicioRepartoPropio.DIAS_ATRAS_LISTA), hoy.AddDays(2)).ConfigureAwait(false);
                _ = await servicio.Geocodificar(envios.Select(e => new DireccionParada(e.Direccion, e.CodPostal, e.Poblacion)), MAXIMO_PARADAS_POR_PASADA)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// De noche: primero las paradas en reparto (como <see cref="GeocodificarParadas"/>) y luego los clientes activos
        /// (Estado ≥ 0) de las rutas propias, como mucho 300 direcciones nuevas por noche.
        /// </summary>
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task GeocodificarClientesRutasPropias()
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                var servicio = new ServicioGeocodificacion(new RepositorioCoordenadas(db), new GeocodificadorNominatim());
                DateTime hoy = DateTime.Today;
                List<EnviosAgencia> envios = await new RepositorioRepartoPropio(db)
                    .LeerEnviosEnReparto(hoy.AddDays(-ServicioRepartoPropio.DIAS_ATRAS_LISTA), hoy.AddDays(2)).ConfigureAwait(false);
                _ = await servicio.Geocodificar(envios.Select(e => new DireccionParada(e.Direccion, e.CodPostal, e.Poblacion)), MAXIMO_PARADAS_POR_PASADA)
                    .ConfigureAwait(false);
                List<string> rutas = RutasPropiasConfiguradas.Actuales.Select(r => r.Trim()).ToList();
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                var clientes = await db.Clientes.AsNoTracking()
                    .Where(c => c.Empresa == empresa && c.Estado >= 0 && rutas.Contains(c.Ruta.Trim()))
                    .Select(c => new { c.Dirección, c.CodPostal, c.Población })
                    .ToListAsync().ConfigureAwait(false);
                _ = await servicio.Geocodificar(clientes.Select(c => new DireccionParada(c.Dirección, c.CodPostal, c.Población)), MAXIMO_CLIENTES_POR_NOCHE)
                    .ConfigureAwait(false);
            }
        }
    }
}
