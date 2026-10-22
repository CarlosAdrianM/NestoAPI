using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>
        /// NestoAPI#690: inscripción a un evento gratuito. No caduca, no se paga ni se factura y no da cupón: va directa a la
        /// lista de asistencia.
        /// </summary>
        Inscrita,
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
            EstadoInscripcionEvento.Inscrita, EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio
        };

        /// <summary>
        /// Entran en la lista de asistencia: las pagadas (con o sin factura), las gratuitas (NestoAPI#690) y las que ya tienen
        /// lista pasada (para corregirla).
        /// </summary>
        public static readonly IReadOnlyCollection<EstadoInscripcionEvento> EN_LISTA_ASISTENCIA = new[]
        {
            EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada, EstadoInscripcionEvento.Inscrita,
            EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio
        };

        /// <summary>NestoAPI#690: las que esperan al evento (pagadas o gratuitas): a estas les llega el recordatorio.</summary>
        public static readonly IReadOnlyCollection<EstadoInscripcionEvento> ESPERAN_AL_EVENTO = new[]
        {
            EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada, EstadoInscripcionEvento.Inscrita
        };

        /// <summary>NestoAPI#690: pueden contestar «Confirmo asistencia» / «No podré ir» (también las que faltan por pagar).</summary>
        public static readonly IReadOnlyCollection<EstadoInscripcionEvento> PUEDEN_CONFIRMAR_ASISTENCIA = new[]
        {
            EstadoInscripcionEvento.Reservada, EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Facturada,
            EstadoInscripcionEvento.Inscrita
        };

        private static readonly Dictionary<EstadoInscripcionEvento, EstadoInscripcionEvento[]> transiciones =
            new Dictionary<EstadoInscripcionEvento, EstadoInscripcionEvento[]>
            {
                [EstadoInscripcionEvento.Reservada] = new[] { EstadoInscripcionEvento.Pagada, EstadoInscripcionEvento.Caducada, EstadoInscripcionEvento.Anulada },
                // Una caducada que paga tarde (el enlace sigue vivo, o llega la transferencia): manda el dinero.
                [EstadoInscripcionEvento.Caducada] = new[] { EstadoInscripcionEvento.Pagada },
                [EstadoInscripcionEvento.Pagada] = new[] { EstadoInscripcionEvento.Facturada, EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio },
                [EstadoInscripcionEvento.Facturada] = new[] { EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio },
                // NestoAPI#690: la gratuita no se paga; se anula como una reservada (no hay dinero de por medio).
                [EstadoInscripcionEvento.Inscrita] = new[] { EstadoInscripcionEvento.Asistio, EstadoInscripcionEvento.NoAsistio, EstadoInscripcionEvento.Anulada },
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

    /// <summary>
    /// NestoAPI#690: cómo se apunta el cliente. Fija lo que se cobra y si al confirmar el pago se emite cupón. Se guarda como
    /// texto en EventosInscripciones.Modalidad; NULL (las de antes) es Reembolsable.
    /// </summary>
    public enum ModalidadInscripcionEvento
    {
        /// <summary>Señal reembolsable (Evento.ImporteSenal): factura exenta del curso y cupón por ese importe, IVA incluido.</summary>
        Reembolsable,
        /// <summary>Señal no reembolsable (Evento.ImporteSenalNoReembolsable): factura exenta del curso, sin cupón.</summary>
        NoReembolsable,
        /// <summary>Evento gratuito (las dos señales a 0): ni pago, ni factura, ni cupón.</summary>
        Gratuita
    }

    /// <summary>NestoAPI#690: «Confirmo asistencia» o «No podré ir». Se guarda en EventosInscripciones.AsistenciaRespuesta.</summary>
    public enum RespuestaAsistenciaEvento
    {
        Confirma,
        NoPodraIr
    }

    /// <summary>NestoAPI#690: quién apuntó la respuesta: el propio cliente (app o enlace del correo) o la tienda.</summary>
    public enum OrigenRespuestaAsistenciaEvento
    {
        Cliente,
        Tienda
    }

    /// <summary>NestoAPI#690: lo que se ve en la lista de asistencia (pendiente → confirmado → asistió / no asistió).</summary>
    public enum EstadoAsistenciaEvento
    {
        Pendiente,
        Confirmada,
        NoPodraIr,
        Asistio,
        NoAsistio
    }

    /// <summary>NestoAPI#690: las dos señales, el evento gratuito y la modalidad de cada inscripción (sin base de datos).</summary>
    public static class ReglasModalidadEvento
    {
        /// <summary>Carlos (22/10/26): la señal reembolsable de un evento nuevo, si no se dice otra.</summary>
        public const decimal SENAL_REEMBOLSABLE_POR_DEFECTO = 100m;
        /// <summary>Carlos (22/10/26): la señal no reembolsable de un evento nuevo, si no se dice otra.</summary>
        public const decimal SENAL_NO_REEMBOLSABLE_POR_DEFECTO = 60m;

        /// <summary>Las dos señales a 0: se apunta quien quiera, sin pagar.</summary>
        public static bool EsGratuito(Evento evento) =>
            evento != null && evento.ImporteSenal <= 0 && evento.ImporteSenalNoReembolsable <= 0;

        /// <summary>Lo que se puede elegir al inscribirse, en este orden.</summary>
        public static List<ModalidadInscripcionEvento> Disponibles(Evento evento)
        {
            if (evento == null)
            {
                return new List<ModalidadInscripcionEvento>();
            }
            if (EsGratuito(evento))
            {
                return new List<ModalidadInscripcionEvento> { ModalidadInscripcionEvento.Gratuita };
            }
            var disponibles = new List<ModalidadInscripcionEvento>();
            if (evento.ImporteSenal > 0)
            {
                disponibles.Add(ModalidadInscripcionEvento.Reembolsable);
            }
            if (evento.ImporteSenalNoReembolsable > 0)
            {
                disponibles.Add(ModalidadInscripcionEvento.NoReembolsable);
            }
            return disponibles;
        }

        /// <summary>La modalidad guardada; NULL o desconocida (las de antes de #690) es Reembolsable.</summary>
        public static ModalidadInscripcionEvento DeInscripcion(EventoInscripcion inscripcion) =>
            Interpretar(inscripcion?.Modalidad) ?? ModalidadInscripcionEvento.Reembolsable;

        public static ModalidadInscripcionEvento? Interpretar(string texto)
        {
            string limpio = texto?.Trim();
            string nombre = Enum.GetNames(typeof(ModalidadInscripcionEvento))
                .FirstOrDefault(n => string.Equals(n, limpio, StringComparison.OrdinalIgnoreCase));
            if (nombre != null)
            {
                return (ModalidadInscripcionEvento)Enum.Parse(typeof(ModalidadInscripcionEvento), nombre);
            }
            return string.Equals(limpio, "No reembolsable", StringComparison.OrdinalIgnoreCase)
                ? ModalidadInscripcionEvento.NoReembolsable
                : (ModalidadInscripcionEvento?)null;
        }

        /// <summary>Lo que se cobra con esa modalidad (0 si es gratuita).</summary>
        public static decimal Importe(Evento evento, ModalidadInscripcionEvento modalidad)
        {
            if (evento == null)
            {
                return 0;
            }
            switch (modalidad)
            {
                case ModalidadInscripcionEvento.Reembolsable: return Redondear(evento.ImporteSenal);
                case ModalidadInscripcionEvento.NoReembolsable: return Redondear(evento.ImporteSenalNoReembolsable);
                default: return 0;
            }
        }

        /// <summary>Lo que se cobra por esta inscripción, según su modalidad.</summary>
        public static decimal ImporteDe(EventoInscripcion inscripcion, Evento evento) => Importe(evento, DeInscripcion(inscripcion));

        /// <summary>Solo la reembolsable gana el cupón (#613).</summary>
        public static bool EmiteCupon(ModalidadInscripcionEvento modalidad) => modalidad == ModalidadInscripcionEvento.Reembolsable;

        public static bool EmiteCupon(EventoInscripcion inscripcion) => EmiteCupon(DeInscripcion(inscripcion));

        /// <summary>
        /// La modalidad de una inscripción nueva. Sin pedir ninguna: la gratuita si el evento es gratuito y, si no, la
        /// reembolsable (o la no reembolsable si es la única que hay). Lanza si se pide una que el evento no ofrece.
        /// </summary>
        public static ModalidadInscripcionEvento Elegir(Evento evento, string pedida)
        {
            List<ModalidadInscripcionEvento> disponibles = Disponibles(evento);
            string titulo = evento?.Titulo?.Trim();
            if (string.IsNullOrWhiteSpace(pedida))
            {
                return disponibles.Any() ? disponibles.First() : ModalidadInscripcionEvento.Gratuita;
            }
            ModalidadInscripcionEvento modalidad = Interpretar(pedida)
                ?? throw new NestoBusinessException("La modalidad tiene que ser Reembolsable, NoReembolsable o Gratuita.");
            if (!disponibles.Contains(modalidad))
            {
                throw new NestoBusinessException(EsGratuito(evento)
                    ? $"«{titulo}» es gratuito: la inscripción es Gratuita."
                    : $"«{titulo}» no tiene la modalidad {Texto(modalidad).ToLowerInvariant()}. Puede elegir: " +
                      string.Join(" o ", disponibles.Select(m => Texto(m).ToLowerInvariant())) + ".");
            }
            return modalidad;
        }

        /// <summary>
        /// La modalidad de una inscripción que entra pagada por transferencia (#591, #615): la que coincide con lo cobrado;
        /// si no coincide ninguna (o coinciden las dos), la reembolsable, como hasta ahora.
        /// </summary>
        public static ModalidadInscripcionEvento PorImporte(Evento evento, decimal importe)
        {
            decimal cobrado = Redondear(importe);
            bool reembolsable = evento != null && Redondear(evento.ImporteSenal) == cobrado;
            bool noReembolsable = evento != null && evento.ImporteSenalNoReembolsable > 0
                && Redondear(evento.ImporteSenalNoReembolsable) == cobrado;
            return noReembolsable && !reembolsable ? ModalidadInscripcionEvento.NoReembolsable : ModalidadInscripcionEvento.Reembolsable;
        }

        public static string Texto(ModalidadInscripcionEvento modalidad) =>
            modalidad == ModalidadInscripcionEvento.NoReembolsable ? "No reembolsable" : modalidad.ToString();

        /// <summary>«10:00» → 10:00; null o vacío → null. Lanza si no es una hora HH:mm.</summary>
        public static TimeSpan? LeerHora(string texto, string campo)
        {
            string limpio = texto?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            if (DateTime.TryParseExact(limpio, new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime hora))
            {
                return new TimeSpan(hora.Hour, hora.Minute, 0);
            }
            throw new NestoBusinessException($"La hora de {campo} tiene que ir como HH:mm (por ejemplo, 10:00).");
        }

        /// <summary>10:00 → «10:00»; null → null.</summary>
        public static string EscribirHora(TimeSpan? hora) =>
            hora.HasValue ? $"{hora.Value.Hours:00}:{hora.Value.Minutes:00}" : null;

        private static decimal Redondear(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>NestoAPI#690: la confirmación de asistencia (independiente del pago) y el estado que se ve en la lista.</summary>
    public static class ReglasAsistenciaEvento
    {
        public static RespuestaAsistenciaEvento? Respuesta(string texto)
        {
            string limpio = texto?.Trim();
            string nombre = Enum.GetNames(typeof(RespuestaAsistenciaEvento))
                .FirstOrDefault(n => string.Equals(n, limpio, StringComparison.OrdinalIgnoreCase));
            return nombre == null ? (RespuestaAsistenciaEvento?)null : (RespuestaAsistenciaEvento)Enum.Parse(typeof(RespuestaAsistenciaEvento), nombre);
        }

        /// <summary>Asistió / No asistió (la lista pasada) mandan sobre lo que contestó el cliente.</summary>
        public static EstadoAsistenciaEvento Estado(EventoInscripcion inscripcion)
        {
            EstadoInscripcionEvento? estado = ReglasInscripcionEvento.Estado(inscripcion?.Estado);
            if (estado == EstadoInscripcionEvento.Asistio)
            {
                return EstadoAsistenciaEvento.Asistio;
            }
            if (estado == EstadoInscripcionEvento.NoAsistio)
            {
                return EstadoAsistenciaEvento.NoAsistio;
            }
            switch (Respuesta(inscripcion?.AsistenciaRespuesta))
            {
                case RespuestaAsistenciaEvento.Confirma: return EstadoAsistenciaEvento.Confirmada;
                case RespuestaAsistenciaEvento.NoPodraIr: return EstadoAsistenciaEvento.NoPodraIr;
                default: return EstadoAsistenciaEvento.Pendiente;
            }
        }

        public static string Texto(EstadoAsistenciaEvento estado)
        {
            switch (estado)
            {
                case EstadoAsistenciaEvento.NoPodraIr: return "No podrá ir";
                case EstadoAsistenciaEvento.Asistio: return "Asistió";
                case EstadoAsistenciaEvento.NoAsistio: return "No asistió";
                default: return estado.ToString();
            }
        }
    }
}
