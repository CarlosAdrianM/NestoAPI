using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#611: el estado de una inscripción a un evento. Se guarda como texto (el nombre del valor) en
    /// EventosInscripciones.Estado.
    /// <para>Reservada → Pagada → Facturada → Asistio / NoAsistio; Reservada → Caducada (7 días sin pagar) o Anulada.</para>
    /// </summary>
    public enum EstadoInscripcionEvento
    {
        /// <summary>Inscrita, sin pagar. Caduca en la fecha límite de pago.</summary>
        Reservada,
        /// <summary>El dinero ha llegado (enlace, transferencia o efectivo). Dispara el corte 2 (#612).</summary>
        Pagada,
        /// <summary>El corte 2 ha facturado el curso (exento).</summary>
        Facturada,
        /// <summary>Vino al evento (lista de asistencia, o nadie pasó lista y se da por asistido).</summary>
        Asistio,
        /// <summary>No vino: pierde el cupón (corte 3, #613).</summary>
        NoAsistio,
        /// <summary>Pasó la fecha límite sin pagar.</summary>
        Caducada,
        /// <summary>Anulada a mano antes de pagar.</summary>
        Anulada
    }

    /// <summary>NestoAPI#611: por dónde llegó la inscripción.</summary>
    public enum OrigenInscripcionEvento
    {
        Nesto,
        NestoApp,
        TNV,
        PrestaShop,
        Telefono,
        /// <summary>Llegó una señal (#591) de un cliente sin inscripción previa: la crea el corte 2.</summary>
        Transferencia
    }

    /// <summary>NestoAPI#611 (Carlos, 14/10/26): cómo se paga la inscripción. El enlace de tarjeta es opcional.</summary>
    public enum FormaPagoInscripcionEvento
    {
        /// <summary>Enlace de pago (Redsys).</summary>
        Tarjeta,
        /// <summary>Transferencia (la señal de #591 o marcada a mano).</summary>
        Transferencia,
        /// <summary>En efectivo en la tienda.</summary>
        Efectivo
    }

    /// <summary>NestoAPI#611: las transiciones permitidas y los textos de cada estado.</summary>
    public static class ReglasInscripcionEvento
    {
        /// <summary>Días que tiene el cliente para pagar desde que se inscribe (decisión de la épica #610).</summary>
        public const int DIAS_PARA_PAGAR = 7;

        /// <summary>Ocupan plaza: todas menos las caducadas y las anuladas.</summary>
        public static readonly IReadOnlyCollection<EstadoInscripcionEvento> OCUPAN_PLAZA = new[]
        {
            EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada,
            EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio
        };

        /// <summary>Entran en la lista de asistencia: las pagadas (con o sin factura) y las que ya tienen lista pasada (para corregirla).</summary>
        public static readonly IReadOnlyCollection<EstadoInscripcionEvento> EN_LISTA_ASISTENCIA = new[]
        {
            EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada,
            EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio
        };

        private static readonly Dictionary<EstadoInscripcionEvento, EstadoInscripcionEvento[]> transiciones =
            new Dictionary<EstadoInscripcionEvento, EstadoInscripcionEvento[]>
            {
                [EstadoInscripcionEvento.Reservada] = new[] { EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Caducada, EstadoInscripcionEvento.Anulada },
                // Una caducada que paga tarde (el enlace sigue vivo, o llega la transferencia): manda el dinero.
                [EstadoInscripcionEvento.Caducada] = new[] { EstadoInscripcionEvento.Pagada },
                [EstadoInscripcionEvento.Pagada] = new[] { EstadoInscripcionEvento.Facturada, EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio },
                [EstadoInscripcionEvento.Facturada] = new[] { EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio },
                // La lista se puede corregir mientras no haya nada más (el corte 3 decidirá qué pasa con el cupón ya emitido).
                [EstadoInscripcionEvento.Asistio] = new[] { EstadoInscripcionEvento.NoAsistio },
                [EstadoInscripcionEvento.NoAsistio] = new[] { EstadoInscripcionEvento.Asistio },
                [EstadoInscripcionEvento.Anulada] = new EstadoInscripcionEvento[0]
            };

        public static bool PuedePasar(EstadoInscripcionEvento desde, EstadoInscripcionEvento hasta) =>
            desde == hasta || (transiciones.TryGetValue(desde, out EstadoInscripcionEvento[] destinos) && destinos.Contains(hasta));

        /// <summary>La fecha límite de pago: 7 días desde la inscripción, pero nunca después del día del evento.</summary>
        public static DateTime FechaLimitePago(DateTime inscripcion, DateTime fechaEvento)
        {
            DateTime limite = inscripcion.Date.AddDays(DIAS_PARA_PAGAR);
            return limite > fechaEvento.Date ? fechaEvento.Date : limite;
        }

        /// <summary>Caduca el día siguiente a la fecha límite (el último día todavía se puede pagar).</summary>
        public static bool EstaCaducada(DateTime? fechaLimitePago, DateTime hoy) =>
            fechaLimitePago.HasValue && fechaLimitePago.Value.Date < hoy.Date;

        public static string Texto(EstadoInscripcionEvento estado)
        {
            switch (estado)
            {
                case EstadoInscripcionEvento.Asistio: return "Asistió";
                case EstadoInscripcionEvento.NoAsistio: return "No asistió";
                default: return estado.ToString();
            }
        }

        public static string Texto(OrigenInscripcionEvento origen) =>
            origen == OrigenInscripcionEvento.Telefono ? "Teléfono" : origen.ToString();

        /// <summary>El estado guardado en la BD; null si no es ninguno conocido.</summary>
        public static EstadoInscripcionEvento? Estado(string texto) => PorNombre<EstadoInscripcionEvento>(texto);

        public static OrigenInscripcionEvento? Origen(string texto) =>
            string.Equals(texto?.Trim(), "Teléfono", StringComparison.OrdinalIgnoreCase)
                ? OrigenInscripcionEvento.Telefono
                : PorNombre<OrigenInscripcionEvento>(texto);

        public static FormaPagoInscripcionEvento? FormaPago(string texto) => PorNombre<FormaPagoInscripcionEvento>(texto);

        /// <summary>Solo por nombre (sin distinguir mayúsculas): «1» no es un estado ni un origen aunque Enum.TryParse lo acepte.</summary>
        private static T? PorNombre<T>(string texto) where T : struct
        {
            string limpio = texto?.Trim();
            string nombre = Enum.GetNames(typeof(T)).FirstOrDefault(n => string.Equals(n, limpio, StringComparison.OrdinalIgnoreCase));
            return nombre == null ? (T?)null : (T)Enum.Parse(typeof(T), nombre);
        }
    }
}
