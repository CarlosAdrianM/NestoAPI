using System;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>NestoAPI#591: en qué punto está la señal de un evento. No se guarda: se calcula cada vez.</summary>
    public enum EstadoSenalEvento
    {
        /// <summary>Antes de la fecha del evento: la señal sigue a favor del cliente y no se debería liquidar.</summary>
        Pendiente,
        /// <summary>Desde la fecha del evento: se liquida contra la primera compra; lo que sobre sigue a favor.</summary>
        Liberada,
        /// <summary>Liberada hace <see cref="CalculadoraEstadoSenalEvento.DIAS_SIN_COMPRA"/> días o más y sin consumir: Administración decide.</summary>
        SinCompra,
        /// <summary>El apunte del extracto ya no tiene nada pendiente a favor (liquidado entero).</summary>
        Consumida
    }

    /// <summary>
    /// NestoAPI#591: calcula el estado de una señal con la fecha del evento y el pendiente del apunte del extracto.
    /// Los cobros a favor del cliente tienen ImportePdte negativo; cuando se liquidan del todo, ImportePdte vale 0.
    /// </summary>
    public static class CalculadoraEstadoSenalEvento
    {
        /// <summary>Días desde la fecha del evento para que una señal liberada sin consumir pase a «Sin compra» (Carlos, 07/10/26).</summary>
        public const int DIAS_SIN_COMPRA = 15;

        /// <param name="fechaEvento">Fecha del evento (se ignora la hora).</param>
        /// <param name="importePdteApunte">ImportePdte del apunte en ExtractoCliente (negativo = a favor del cliente).</param>
        /// <param name="hoy">Fecha de hoy (se ignora la hora).</param>
        public static EstadoSenalEvento Calcular(DateTime fechaEvento, decimal importePdteApunte, DateTime hoy)
        {
            if (importePdteApunte >= 0)
            {
                return EstadoSenalEvento.Consumida;
            }
            if (hoy.Date < fechaEvento.Date)
            {
                return EstadoSenalEvento.Pendiente;
            }
            return hoy.Date >= FechaSinCompra(fechaEvento)
                ? EstadoSenalEvento.SinCompra
                : EstadoSenalEvento.Liberada;
        }

        /// <summary>Desde qué día una señal sin consumir pasa a «Sin compra».</summary>
        public static DateTime FechaSinCompra(DateTime fechaEvento) => fechaEvento.Date.AddDays(DIAS_SIN_COMPRA);

        /// <summary>"Pendiente", "Liberada", "Sin compra", "Consumida": lo que ve el usuario.</summary>
        public static string Texto(EstadoSenalEvento estado) => estado == EstadoSenalEvento.SinCompra ? "Sin compra" : estado.ToString();

        /// <summary>"pendiente", "Sin compra", "sincompra", "SIN_COMPRA"… → el estado; vacío, "Todas" o desconocido → null (sin filtro).</summary>
        public static EstadoSenalEvento? Interpretar(string estado)
        {
            string limpio = estado?.Replace(" ", "").Replace("_", "").Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            return Enum.TryParse(limpio, true, out EstadoSenalEvento resultado) && Enum.IsDefined(typeof(EstadoSenalEvento), resultado)
                ? resultado
                : (EstadoSenalEvento?)null;
        }
    }
}
