using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Transactions;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#693: la puerta por la que los movimientos avisan de que algo puede haber roto promesas. Quien mueve stock (o
    /// cambia un pedido, o retrasa un pedido a proveedor) llama a <see cref="Anotar"/> y sigue: NO se recalcula en la
    /// petición. Se programa un job de Hangfire (<see cref="PromesaRiesgoJobsService.ProcesarEvento"/>) para dentro de
    /// <see cref="RETRASO"/>, que da tiempo a que la transacción de quien llama se confirme (si se deshace, el recálculo no
    /// encuentra nada distinto y no pasa nada).
    /// <list type="bullet">
    /// <item>Con el interruptor Apagado no hace NADA (ni lee ni programa).</item>
    /// <item>Nunca lanza: un fallo aquí no puede romper la reposición, el pedido o el movimiento que lo llama.</item>
    /// <item>Hangfire guarda el job en SQL: se programa fuera de la transacción ambiente (TransactionScope Suppress), como el
    /// resto de lecturas de apoyo (sugerencia 579: una segunda conexión dentro del TransactionScope de UnirPedidos escala a
    /// distribuida y MSDTC está deshabilitado).</item>
    /// <item><see cref="Ambito"/> junta los eventos de una operación (la reposición que se termina contabiliza su diario y,
    /// además, sabe qué mandó la tienda): un solo job con la causa más precisa.</item>
    /// </list>
    /// </summary>
    public static class EventosPromesaRiesgo
    {
        public static readonly TimeSpan RETRASO = TimeSpan.FromSeconds(60);

        /// <summary>Para los tests: qué se programa (por defecto, BackgroundJob.Schedule del job).</summary>
        internal static Action<EventoPromesaRiesgo, TimeSpan> Programar { get; set; } = ProgramarConHangfire;

        /// <summary>Para los tests: el modo (por defecto, el interruptor del Web.config).</summary>
        internal static Func<ModoAvisoPromesaRiesgo> LeerModo { get; set; } = () => InterruptorAvisoPromesaRiesgo.Modo;

        private static readonly AsyncLocal<AmbitoEventosPromesaRiesgo> ambitoActual = new AsyncLocal<AmbitoEventosPromesaRiesgo>();

        /// <summary>True si hay que hacer algo (interruptor en Sombra o Activo). Nunca lanza.</summary>
        public static bool Encendido
        {
            get
            {
                try
                {
                    return LeerModo() != ModoAvisoPromesaRiesgo.Apagado;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Anota un evento. Dentro de un <see cref="Ambito"/>, se junta con el resto; si no, se programa ya. Nunca lanza.</summary>
        public static void Anotar(string empresa, IEnumerable<string> productos, string descripcion,
            IDictionary<string, CausaPromesaRiesgo> causas = null)
        {
            try
            {
                if (!Encendido)
                {
                    return;
                }
                EventoPromesaRiesgo evento = Crear(empresa, productos, descripcion, causas);
                if (!evento.Productos.Any())
                {
                    return;
                }
                AmbitoEventosPromesaRiesgo ambito = ambitoActual.Value;
                if (ambito != null && !ambito.Cerrado)
                {
                    ambito.Juntar(evento);
                    return;
                }
                Enviar(evento);
            }
            catch (Exception ex)
            {
                Registrar(ex);
            }
        }

        /// <summary>Junta los eventos hasta el Dispose y entonces programa uno por empresa.</summary>
        public static IDisposable Ambito()
        {
            var ambito = new AmbitoEventosPromesaRiesgo(ambitoActual.Value);
            ambitoActual.Value = ambito;
            return ambito;
        }

        internal static void CerrarAmbito(AmbitoEventosPromesaRiesgo ambito)
        {
            ambitoActual.Value = ambito.Anterior;
            foreach (EventoPromesaRiesgo evento in ambito.Eventos)
            {
                try
                {
                    if (ambito.Anterior != null && !ambito.Anterior.Cerrado)
                    {
                        ambito.Anterior.Juntar(evento);
                    }
                    else
                    {
                        Enviar(evento);
                    }
                }
                catch (Exception ex)
                {
                    Registrar(ex);
                }
            }
        }

        internal static EventoPromesaRiesgo Crear(string empresa, IEnumerable<string> productos, string descripcion,
            IDictionary<string, CausaPromesaRiesgo> causas)
        {
            var evento = new EventoPromesaRiesgo
            {
                Empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                Descripcion = descripcion,
                Productos = RepositorioPromesaRiesgo.Limpios(productos)
            };
            foreach (KeyValuePair<string, CausaPromesaRiesgo> causa in causas ?? new Dictionary<string, CausaPromesaRiesgo>())
            {
                if (!string.IsNullOrWhiteSpace(causa.Key) && causa.Value != null)
                {
                    evento.Causas[causa.Key.Trim()] = causa.Value;
                    if (!evento.Productos.Contains(causa.Key.Trim(), StringComparer.OrdinalIgnoreCase))
                    {
                        evento.Productos.Add(causa.Key.Trim());
                    }
                }
            }
            return evento;
        }

        private static void Enviar(EventoPromesaRiesgo evento)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
            {
                Programar(evento, RETRASO);
            }
        }

        private static void ProgramarConHangfire(EventoPromesaRiesgo evento, TimeSpan retraso)
        {
            _ = Hangfire.BackgroundJob.Schedule(() => PromesaRiesgoJobsService.ProcesarEvento(evento), retraso);
        }

        private static void Registrar(Exception ex)
        {
            try
            {
                ElmahHelper.Log(new Exception($"[Promesa en riesgo #693] No se ha podido anotar el evento: {ex.Message}", ex), "Sistema (promesa en riesgo)");
            }
            catch
            {
                // nunca romper a quien llama
            }
        }
    }

    /// <summary>NestoAPI#693: los eventos de una operación, juntos (ver <see cref="EventosPromesaRiesgo.Ambito"/>).</summary>
    internal sealed class AmbitoEventosPromesaRiesgo : IDisposable
    {
        internal AmbitoEventosPromesaRiesgo Anterior { get; }
        internal bool Cerrado { get; private set; }
        internal List<EventoPromesaRiesgo> Eventos { get; } = new List<EventoPromesaRiesgo>();

        internal AmbitoEventosPromesaRiesgo(AmbitoEventosPromesaRiesgo anterior)
        {
            Anterior = anterior;
        }

        /// <summary>Uno por empresa: los productos se suman, la causa más precisa (la que viene después) manda, y las descripciones se juntan.</summary>
        internal void Juntar(EventoPromesaRiesgo evento)
        {
            EventoPromesaRiesgo existente = Eventos.FirstOrDefault(e => e.Empresa == evento.Empresa);
            if (existente == null)
            {
                Eventos.Add(evento);
                return;
            }
            foreach (string producto in evento.Productos.Where(p => !existente.Productos.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                existente.Productos.Add(producto);
            }
            foreach (KeyValuePair<string, CausaPromesaRiesgo> causa in evento.Causas)
            {
                existente.Causas[causa.Key] = causa.Value;
            }
            if (!string.IsNullOrWhiteSpace(evento.Descripcion)
                && (existente.Descripcion == null || !existente.Descripcion.Contains(evento.Descripcion)))
            {
                existente.Descripcion = string.IsNullOrWhiteSpace(existente.Descripcion) ? evento.Descripcion : $"{evento.Descripcion}; {existente.Descripcion}";
            }
        }

        public void Dispose()
        {
            if (Cerrado)
            {
                return;
            }
            Cerrado = true;
            EventosPromesaRiesgo.CerrarAmbito(this);
        }
    }
}
