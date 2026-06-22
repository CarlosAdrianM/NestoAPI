using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.Innovatrans
{
    /// <summary>
    /// Control de tasa del WebService DataTrans DTX. El integrador limita a un máximo de 50 operaciones
    /// por ventana de 5 minutos. NO espaciamos cada llamada (eso penalizaba CADA etiqueta con ~2 s sin
    /// necesidad): dejamos pasar las llamadas a velocidad plena y solo esperamos cuando se alcanza el
    /// tope de la ventana, y entonces justo lo necesario para que la llamada más antigua salga de los
    /// 5 minutos (unos segundos en la práctica). Pensado como instancia COMPARTIDA (el límite es por
    /// credencial, global al proceso). El reloj y la espera son inyectables para testearlo sin dormir.
    /// </summary>
    public class ControlTasaDataTrans
    {
        private readonly int _maxPorVentana;
        private readonly TimeSpan _ventana;
        private readonly Func<DateTime> _ahora;
        private readonly Func<TimeSpan, Task> _esperar;

        private readonly SemaphoreSlim _exclusion = new SemaphoreSlim(1, 1);
        private readonly Queue<DateTime> _historial = new Queue<DateTime>();

        public ControlTasaDataTrans(
            int maxPorVentana = 50,
            TimeSpan? ventana = null,
            Func<DateTime> ahora = null,
            Func<TimeSpan, Task> esperar = null)
        {
            _maxPorVentana = maxPorVentana;
            _ventana = ventana ?? TimeSpan.FromMinutes(5);
            _ahora = ahora ?? (() => DateTime.UtcNow);
            _esperar = esperar ?? Task.Delay;
        }

        /// <summary>
        /// Espera (si hace falta) hasta que sea seguro lanzar la siguiente llamada según el tope por
        /// ventana deslizante. Por debajo del tope NO espera (velocidad plena). Solo al alcanzar el tope
        /// espera lo justo a que la llamada más antigua salga de la ventana. Al volver, la llamada queda
        /// "registrada"; el llamante debe hacer su petición HTTP inmediatamente después.
        /// </summary>
        public async Task EsperarTurnoAsync()
        {
            await _exclusion.WaitAsync().ConfigureAwait(false);
            try
            {
                PurgarHistorial();
                if (_historial.Count >= _maxPorVentana)
                {
                    // Tope alcanzado: esperar justo hasta que la llamada más antigua salga de la ventana.
                    TimeSpan esperaVentana = _historial.Peek() + _ventana - _ahora();
                    if (esperaVentana > TimeSpan.Zero)
                    {
                        await _esperar(esperaVentana).ConfigureAwait(false);
                    }
                    PurgarHistorial();
                }

                _historial.Enqueue(_ahora());
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
