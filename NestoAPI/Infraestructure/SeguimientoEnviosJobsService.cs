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

        // NestoAPI#264: pausa entre consultas de seguimiento. El poll lanzaba ~124 llamadas a GLS de
        // golpe cada 2h; GLS/asmred lo trataba como ráfaga abusiva y devolvía "no se encuentra la
        // expedición" (HTTP 200) para casi todas, lo que dejaba los envíos sin actualizar (y vaciaba los
        // incidentados). A demanda (una sola llamada) nunca falla. Espaciar las consultas imita ese
        // comportamiento y evita el rate-limit. 250 ms => ~4 req/s, ~35 s extra para 135 envíos (un job
        // de fondo cada 2h, irrelevante).
        public const int PAUSA_ENTRE_CONSULTAS_MS = 250;

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
            // Devuelto (4) son terminales: no se vuelven a consultar. Tope por ejecución. OJO (#265):
            // FechaModificacion es columna Computed (EF nunca la escribe), así que aquí vale la hora
            // de TRAMITACIÓN del envío: se procesan los envíos más antiguos primero, NO "los menos
            // recientemente consultados" (si algún día el conjunto en vuelo supera el tope y hace
            // falta ese orden real, habría que añadir una columna FechaUltimoSeguimiento propia).
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
            int desconocidos = 0;
            // NestoAPI#266: motivo de cada Desconocido (el <Error> de GLS, el mensaje del timeout...),
            // agregado para que el aviso diga POR QUÉ y no haya que adivinar entre uid mal / WS caído /
            // envíos nuevos. Con el aviso a ciegas estuvimos 10 días sin saber que GLS no encontraba
            // NINGUNA expedición desde el servidor.
            Dictionary<string, int> motivosDesconocido = new Dictionary<string, int>();
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
                    // NestoAPI#264: Desconocido = la agencia no devolvió datos del envío. Algún suelto es
                    // normal (envío recién creado aún no registrado), pero MUCHOS a la vez delatan un
                    // problema (rate-limit de GLS por ráfaga, uid mal, WS caído) que antes pasaba
                    // desapercibido porque se tragaba como "Tramitado". Se cuentan para avisar al final.
                    if (seguimiento.Estado == EstadoEnvioSeguimiento.Desconocido)
                    {
                        desconocidos++;
                        string motivo = string.IsNullOrWhiteSpace(seguimiento.Detalle) ? "(sin detalle)" : seguimiento.Detalle.Trim();
                        motivosDesconocido[motivo] = motivosDesconocido.TryGetValue(motivo, out int veces) ? veces + 1 : 1;
                    }
                    if (AplicarSeguimiento(envio, seguimiento))
                    {
                        actualizados++;
                    }
                }
                catch (Exception ex)
                {
                    // Un envío que falla no debe tumbar el job: se loguea y se sigue con el resto. Como el
                    // poll corre en Hangfire (sin usuario HTTP), se estampa el proceso automático para
                    // saber que no hay una persona a quien preguntar (ver ElmahHelper).
                    ElmahHelper.Log(new Exception(
                        $"Seguimiento del envío {envio.Numero} (albarán {envio.CodigoBarras?.Trim()}): {ex.Message}", ex),
                        "Sistema (seguimiento de envíos)");
                }

                // NestoAPI#264: espaciar las consultas para no parecer una ráfaga abusiva a la agencia
                // (GLS rate-limitaba el lote y devolvía "no encontrada" para casi todos). A demanda, que es
                // una sola llamada, nunca falla; esto imita ese ritmo.
                await Task.Delay(PAUSA_ENTRE_CONSULTAS_MS).ConfigureAwait(false);
            }

            if (actualizados > 0)
            {
                await _db.SaveChangesAsync().ConfigureAwait(false);
            }

            // NestoAPI#264: si una parte grande de los envíos no devuelve estado (Desconocido), casi seguro
            // es un fallo de configuración de seguimiento (uid mal, WS caído), no que todos sean nuevos.
            // Avisamos en ELMAH para no volver a quedarnos ciegos (caso GLS, 24-26/06/2026).
            if (desconocidos >= 10 || (envios.Count > 0 && desconocidos > envios.Count / 2))
            {
                // NestoAPI#266: los 3 motivos más frecuentes, con recuento, para diagnosticar de un
                // vistazo (p. ej. 74× "No se encuentra la expedición" = uid mal; timeouts = WS caído).
                string motivos = string.Join("; ", motivosDesconocido
                    .OrderByDescending(m => m.Value)
                    .Take(3)
                    .Select(m => $"{m.Value}× \"{m.Key}\""));
                ElmahHelper.Log(new Exception(
                    $"Seguimiento de agencias: {desconocidos} de {envios.Count} envíos no devolvieron estado " +
                    $"(Desconocido). Suele indicar un problema de configuración del seguimiento (p. ej. uid de " +
                    $"GLS incorrecta) o el WS caído. Motivos más frecuentes: {motivos}."),
                    "Sistema (seguimiento de envíos)");
            }
            return actualizados;
        }

        // Aplica el seguimiento consultado al envío (Estado/FechaEntrega) en memoria. Devuelve true si
        // algo cambió. Lo comparten el job (en bulk) y la actualización a demanda de un solo envío
        // (EnviosAgenciasController, que además audita la llamada SOAP en AgenciasLlamadasWeb).
        public static bool AplicarSeguimiento(EnviosAgencia envio, SeguimientoEnvioRemoto seguimiento)
        {
            // NestoAPI#264: Desconocido = la agencia no pudo determinar el estado (p. ej. GLS no encuentra
            // la expedición por uid mal). NO es un estado real: se deja el envío como está (no se pisa un
            // Incidentado/Entregado con un Tramitado falso). Era lo que vaciaba la pestaña de incidentados.
            if (seguimiento.Estado == EstadoEnvioSeguimiento.Desconocido)
            {
                return false;
            }

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
            // #265: NO sellar FechaModificacion: es columna Computed en el EDMX y EF la ignora
            // (era código muerto que engañaba — parecía que reflejaba la última pasada del poll).
            return true;
        }
    }
}
