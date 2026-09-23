using System;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// NestoAPI#517: interruptor que se lee de ParámetrosUsuario y se recuerda un rato, para poder apagar
    /// una función desde la base de datos SIN publicar y sin añadir una consulta en cada llamada.
    /// El 23/09/26 hubo que publicar de urgencia para apagar las sugerencias de ofertas porque el endpoint
    /// no tenía interruptor.
    ///   - Sin fila, o con cualquier valor que no sea "0": ACTIVO (la función está pensada para estar encendida).
    ///   - "0": apagado.
    ///   - Si la lectura falla: se mantiene lo último que se supo (o activo si nunca se leyó).
    /// Un cambio en la BD tarda como mucho <see cref="Duracion"/> en notarse.
    /// </summary>
    public class InterruptorCacheado
    {
        private readonly Func<string> _leerValor;
        private readonly Func<DateTime> _ahora;
        private readonly object _cerrojo = new object();
        private bool? _activo;
        private DateTime _leidoUtc;

        public InterruptorCacheado(Func<string> leerValor, TimeSpan duracion, Func<DateTime> ahoraUtc = null)
        {
            _leerValor = leerValor ?? throw new ArgumentNullException(nameof(leerValor));
            Duracion = duracion;
            _ahora = ahoraUtc ?? (() => DateTime.UtcNow);
        }

        public TimeSpan Duracion { get; }

        public bool EstaActivo()
        {
            lock (_cerrojo)
            {
                DateTime ahora = _ahora();
                if (_activo.HasValue && ahora - _leidoUtc < Duracion)
                {
                    return _activo.Value;
                }
                try
                {
                    _activo = Interpretar(_leerValor());
                }
                catch
                {
                    _activo = _activo ?? true;
                }
                _leidoUtc = ahora;
                return _activo.Value;
            }
        }

        public static bool Interpretar(string valor) => valor?.Trim() != "0";
    }
}
