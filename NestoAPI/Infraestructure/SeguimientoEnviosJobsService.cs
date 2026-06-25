using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Poll de seguimiento de envíos (#248): actualiza EnviosAgencia.Estado (Entregado/Incidentado) y
    /// la FechaEntrega real consultando a cada agencia. POLIMÓRFICO: no sabe de agencias concretas —
    /// resuelve la estrategia con <see cref="IFabricaAgenciasRemotas"/> y llama a
    /// <see cref="IAgenciaRemota.ConsultarSeguimientoAsync"/>. Añadir GLS u otra agencia no toca este
    /// servicio: basta con que la factory sepa crearla.
    ///
    /// Acotado para no recorrer el histórico: solo agencias con gestión remota (las antiguas de GLS,
    /// miles, quedan fuera) y solo envíos desde una FECHA DE CORTE fija (<see cref="FECHA_CORTE"/>).
    /// </summary>
    public class SeguimientoEnviosJobsService
    {
        // Fecha de corte FIJA, no ventana móvil: se vigilan los envíos desde aquí en adelante. Con una
        // ventana de N días, un envío que llevara >N días incidentado se dejaría de vigilar y se perdería
        // el control; con fecha fija no. Innovatrans (primera agencia con seguimiento) arrancó en jun/2026.
        public static readonly System.DateTime FECHA_CORTE = new System.DateTime(2026, 6, 1);

        // Tope de envíos por ejecución: GLS tiene miles de tramitados antiguos; sin tope, la 1ª ejecución
        // dispararía miles de consultas de golpe. Se procesan los menos-recientemente-tocados primero
        // (los antiguos, casi todos ya entregados, pasan a terminal y SALEN del conjunto); en pocas
        // ejecuciones el backlog se vacía y queda solo el conjunto activo (pequeño), que sí cabe entero.
        public const int MAX_POR_EJECUCION = 500;

        private readonly NVEntities _db;
        private readonly IFabricaAgenciasRemotas _fabrica;

        public SeguimientoEnviosJobsService(NVEntities db, IFabricaAgenciasRemotas fabrica)
        {
            _db = db;
            _fabrica = fabrica;
        }

        /// <summary>Punto de entrada para Hangfire (compone sus propias dependencias).</summary>
        public static Task ProcesarSeguimientosAsync()
        {
            var db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            return new SeguimientoEnviosJobsService(db, new FabricaAgenciasRemotas(db))
                .ActualizarSeguimientosAsync(FECHA_CORTE);
        }

        /// <summary>
        /// Recorre los envíos "en vuelo" (Tramitado o Incidentado) de agencias con gestión remota,
        /// desde <paramref name="fechaCorte"/>, y actualiza su estado/fecha de entrega. Devuelve cuántos
        /// cambiaron. Best-effort por envío: un fallo de una agencia no detiene el resto (se loguea).
        /// </summary>
        public async Task<int> ActualizarSeguimientosAsync(DateTime fechaCorte)
        {
            int[] remotas = _fabrica.AgenciasConSeguimiento.ToArray();
            if (remotas.Length == 0)
            {
                return 0;
            }

            short tramitado = Constantes.Agencias.ESTADO_TRAMITADO;
            short incidentado = Constantes.Agencias.ESTADO_INCIDENTADO;

            // En vuelo = Tramitado (1) o Incidentado (3, por si se resuelve/entrega). Entregado (2) y
            // Devuelto (4) son terminales: no se vuelven a consultar. Tope por ejecución, los menos
            // recientemente tocados primero (FechaModificacion asc) para vaciar el backlog antiguo.
            List<EnviosAgencia> envios = await _db.EnviosAgencias
                .Where(e => remotas.Contains(e.Agencia)
                    && (e.Estado == tramitado || e.Estado == incidentado)
                    && e.Fecha >= fechaCorte
                    && e.CodigoBarras != null)
                .OrderBy(e => e.FechaModificacion)
                .Take(MAX_POR_EJECUCION)
                .ToListAsync().ConfigureAwait(false);

            if (envios.Count == 0)
            {
                return 0;
            }

            // Una estrategia (de seguimiento) por agencia distinta, no una por envío.
            Dictionary<int, ISeguimientoAgenciaRemota> estrategias = envios
                .Select(e => e.Agencia).Distinct()
                .ToDictionary(id => id, id => _fabrica.CrearSeguimiento(id));

            int actualizados = 0;
            foreach (EnviosAgencia envio in envios)
            {
                ISeguimientoAgenciaRemota agencia = estrategias[envio.Agencia];
                if (agencia == null)
                {
                    continue;
                }
                try
                {
                    SeguimientoEnvioRemoto seguimiento = await agencia
                        .ConsultarSeguimientoAsync(envio.CodigoBarras.Trim()).ConfigureAwait(false);
                    if (AplicarSeguimiento(envio, seguimiento))
                    {
                        actualizados++;
                    }
                }
                catch (Exception ex)
                {
                    // Un envío que falla no debe tumbar el job: se loguea y se sigue con el resto.
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"Seguimiento del envío {envio.Numero} (albarán {envio.CodigoBarras?.Trim()}): {ex.Message}", ex)));
                }
            }

            if (actualizados > 0)
            {
                await _db.SaveChangesAsync().ConfigureAwait(false);
            }
            return actualizados;
        }

        /// <summary>
        /// Actualiza el seguimiento de UN envío a demanda (sin esperar al job de Hangfire): consulta la
        /// agencia por su albarán y aplica Estado/FechaEntrega EN MEMORIA (el llamante hace SaveChanges).
        /// Devuelve el seguimiento consultado, o null si la agencia no tiene seguimiento remoto o el
        /// envío no tiene albarán. Comparte con el job la misma lógica de aplicación (<see cref="AplicarSeguimiento"/>).
        /// </summary>
        public async Task<SeguimientoEnvioRemoto> ActualizarSeguimientoEnvioAsync(EnviosAgencia envio)
        {
            if (envio == null || string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                return null;
            }
            ISeguimientoAgenciaRemota agencia = _fabrica.CrearSeguimiento(envio.Agencia);
            if (agencia == null)
            {
                return null;
            }
            SeguimientoEnvioRemoto seguimiento = await agencia
                .ConsultarSeguimientoAsync(envio.CodigoBarras.Trim()).ConfigureAwait(false);
            AplicarSeguimiento(envio, seguimiento);
            return seguimiento;
        }

        // Aplica el seguimiento consultado al envío (Estado/FechaEntrega) en memoria. Devuelve true si
        // algo cambió. Lo comparten el job (en bulk) y la actualización a demanda de un solo envío.
        private static bool AplicarSeguimiento(EnviosAgencia envio, SeguimientoEnvioRemoto seguimiento)
        {
            bool cambiaEstado = envio.Estado != (short)seguimiento.Estado;
            bool cambiaFecha = seguimiento.FechaEntrega.HasValue && envio.FechaEntrega != seguimiento.FechaEntrega.Value;
            if (!cambiaEstado && !cambiaFecha)
            {
                return false;
            }
            envio.Estado = (short)seguimiento.Estado;
            if (seguimiento.FechaEntrega.HasValue)
            {
                envio.FechaEntrega = seguimiento.FechaEntrega.Value;
            }
            envio.FechaModificacion = DateTime.Now;
            return true;
        }
    }
}
