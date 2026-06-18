using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Control de tasa del WebService DataTrans DTX. El integrador limita a un máximo de
    /// 50 operaciones por ventana de 5 minutos y recomienda ~2 segundos entre llamadas.
    /// Serializa el "permiso de salida" de cada llamada (no la llamada HTTP en sí) para
    /// respetar ambos límites. Pensado como instancia COMPARTIDA (el límite es por credencial,
    /// global al proceso). El reloj y la espera son inyectables para poder testearlo sin dormir.
    /// </summary>
    public class ControlTasaDataTrans
    {
        private readonly TimeSpan _intervaloMinimo;
        private readonly int _maxPorVentana;
        private readonly TimeSpan _ventana;
        private readonly Func<DateTime> _ahora;
        private readonly Func<TimeSpan, Task> _esperar;

        private readonly SemaphoreSlim _exclusion = new SemaphoreSlim(1, 1);
        private readonly Queue<DateTime> _historial = new Queue<DateTime>();
        private DateTime _ultimaLlamada = DateTime.MinValue;

        public ControlTasaDataTrans(
            TimeSpan? intervaloMinimo = null,
            int maxPorVentana = 50,
            TimeSpan? ventana = null,
            Func<DateTime> ahora = null,
            Func<TimeSpan, Task> esperar = null)
        {
            _intervaloMinimo = intervaloMinimo ?? TimeSpan.FromSeconds(2);
            _maxPorVentana = maxPorVentana;
            _ventana = ventana ?? TimeSpan.FromMinutes(5);
            _ahora = ahora ?? (() => DateTime.UtcNow);
            _esperar = esperar ?? Task.Delay;
        }

        /// <summary>
        /// Espera (si hace falta) hasta que sea seguro lanzar la siguiente llamada: respeta el
        /// intervalo mínimo entre llamadas y el tope por ventana deslizante. Al volver, la llamada
        /// queda "registrada"; el llamante debe hacer su petición HTTP inmediatamente después.
        /// </summary>
        public async Task EsperarTurnoAsync()
        {
            await _exclusion.WaitAsync().ConfigureAwait(false);
            try
            {
                // 1) Intervalo mínimo entre llamadas consecutivas.
                TimeSpan desdeUltima = _ahora() - _ultimaLlamada;
                if (desdeUltima < _intervaloMinimo)
                {
                    await _esperar(_intervaloMinimo - desdeUltima).ConfigureAwait(false);
                }

                // 2) Tope por ventana deslizante (máx N llamadas en los últimos T).
                PurgarHistorial();
                if (_historial.Count >= _maxPorVentana)
                {
                    TimeSpan esperaVentana = _historial.Peek() + _ventana - _ahora();
                    if (esperaVentana > TimeSpan.Zero)
                    {
                        await _esperar(esperaVentana).ConfigureAwait(false);
                    }
                    PurgarHistorial();
                }

                DateTime momento = _ahora();
                _ultimaLlamada = momento;
                _historial.Enqueue(momento);
            }
            finally
            {
                _exclusion.Release();
            }
        }

        private void PurgarHistorial()
        {
            DateTime limite = _ahora() - _ventana;
            while (_historial.Count > 0 && _historial.Peek() <= limite)
            {
                _historial.Dequeue();
            }
        }
    }
}
