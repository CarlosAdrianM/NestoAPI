using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>Novedades 1233: un PagoTPV de la cadena del enlace de una inscripción (el original y sus reintentos).</summary>
    public class EnlacePagoInscripcionLeido
    {
        public int Id { get; set; }
        /// <summary>El enlace original de la cadena (el que guarda EventosInscripciones.PagoTPVId).</summary>
        public int Raiz { get; set; }
        public string Empresa { get; set; }
        public string Tipo { get; set; }
        public string Estado { get; set; }
    }

    /// <summary>Novedades 1233: lo que hay que hacer al pasar un evento de pago a gratuito (sin base de datos).</summary>
    public class PlanPasoAGratuito
    {
        /// <summary>Reservadas sin dinero ni documentos: pasan a Inscrita / Gratuita.</summary>
        public List<EventoInscripcion> AConvertir { get; } = new List<EventoInscripcion>();
        /// <summary>Caducadas sin dinero ni documentos que caben en las plazas libres: pasan a Inscrita / Gratuita.</summary>
        public List<EventoInscripcion> ARecuperar { get; } = new List<EventoInscripcion>();
        /// <summary>Caducadas que se podrían recuperar pero no caben todas: no se recupera ninguna.</summary>
        public int CaducadasSinRecuperar { get; set; }
        /// <summary>Plazas libres que había para las caducadas (null = sin límite).</summary>
        public int? PlazasLibres { get; set; }
        /// <summary>Las que tienen dinero o documentos de por medio: inscripción → motivo. Con alguna, no se cambia nada.</summary>
        public Dictionary<int, string> Bloqueadas { get; } = new Dictionary<int, string>();
        /// <summary>Enlaces de pago pendientes de las que cambian (empresa → ids): se anulan en la misma transacción.</summary>
        public Dictionary<string, List<int>> EnlacesAAnular { get; } = new Dictionary<string, List<int>>();

        public bool HayBloqueo => Bloqueadas.Any();
        public IEnumerable<EventoInscripcion> QueCambian => AConvertir.Concat(ARecuperar);
    }

    /// <summary>
    /// Novedades 1233 (Carlos, 12/11/26: «si no hay riesgo de dejar datos inconsistentes»): un curso que pasa de pago a gratuito
    /// no deja reservas «de pago» colgando. Las Reservadas sin dinero ni documentos pasan a Inscrita / Gratuita; las Caducadas
    /// igual, solo si caben TODAS en las plazas libres; con alguna inscripción con dinero o documentos (pagada, facturada, con
    /// cobro, señal, factura del curso, cupón o un enlace cobrado o compartido con otro evento), no se cambia nada: todo o nada.
    /// Nunca se deshacen facturas ni cupones aquí.
    /// </summary>
    public static class ReglasPasoAGratuitoEvento
    {
        public const string MOTIVO_PAGADA = "pagada";
        public const string MOTIVO_FACTURADA = "facturada";
        public const string MOTIVO_LISTA_PASADA = "con la lista pasada (pagada)";
        public const string MOTIVO_COBRO = "con un cobro apuntado";
        public const string MOTIVO_SENAL = "con una señal asociada";
        public const string MOTIVO_DOCUMENTOS = "con factura del curso o cupón";
        public const string MOTIVO_ENLACE_COBRADO = "con el enlace de pago cobrado o devuelto";
        public const string MOTIVO_ENLACE_COMPARTIDO = "con un enlace de pago compartido con otro evento (pedido de cursos)";

        /// <summary>
        /// Null si la inscripción no tiene nada que impida pasarla a gratuita (o no hay que tocarla: anulada o ya inscrita); si
        /// no, el motivo.
        /// </summary>
        /// <param name="conDocumentos">Tiene fila de facturación del curso o cupón.</param>
        /// <param name="cadenaEnlace">Los PagosTPV de su enlace (vacío si no tiene).</param>
        /// <param name="enlaceCompartido">Su enlace lo usa también una inscripción de otro evento.</param>
        public static string MotivoBloqueo(EventoInscripcion inscripcion, bool conDocumentos, IEnumerable<EnlacePagoInscripcionLeido> cadenaEnlace,
            bool enlaceCompartido)
        {
            EstadoInscripcionEvento estado = ReglasInscripcionEvento.Estado(inscripcion?.Estado) ?? EstadoInscripcionEvento.Anulada;
            switch (estado)
            {
                case EstadoInscripcionEvento.Anulada:
                case EstadoInscripcionEvento.Inscrita:
                    return null;
                case EstadoInscripcionEvento.Pagada:
                    return MOTIVO_PAGADA;
                case EstadoInscripcionEvento.Facturada:
                    return MOTIVO_FACTURADA;
                case EstadoInscripcionEvento.Asistio:
                case EstadoInscripcionEvento.NoAsistio:
                    return MOTIVO_LISTA_PASADA;
            }
            if (inscripcion.ImportePagado.HasValue || inscripcion.FechaPago.HasValue)
            {
                return MOTIVO_COBRO;
            }
            if (inscripcion.EventoSenalId.HasValue)
            {
                return MOTIVO_SENAL;
            }
            if (conDocumentos)
            {
                return MOTIVO_DOCUMENTOS;
            }
            if (inscripcion.PagoTPVId.HasValue)
            {
                if ((cadenaEnlace ?? Enumerable.Empty<EnlacePagoInscripcionLeido>()).Any(p => EsCobrado(p.Estado)))
                {
                    return MOTIVO_ENLACE_COBRADO;
                }
                if (enlaceCompartido)
                {
                    return MOTIVO_ENLACE_COMPARTIDO;
                }
            }
            return null;
        }

        /// <summary>Autorizado o Devuelto: ha habido dinero.</summary>
        internal static bool EsCobrado(string estado) =>
            string.Equals(estado?.Trim(), Constantes.EstadosPagoTPV.AUTORIZADO, StringComparison.OrdinalIgnoreCase)
            || string.Equals(estado?.Trim(), Constantes.EstadosPagoTPV.DEVUELTO, StringComparison.OrdinalIgnoreCase);

        /// <summary>Lo que se puede anular sin riesgo: un enlace de pago (TPV virtual) que sigue pendiente.</summary>
        internal static bool SeAnula(EnlacePagoInscripcionLeido enlace) =>
            string.Equals(enlace?.Tipo?.Trim(), Constantes.TiposPagoTPV.TPV_VIRTUAL, StringComparison.OrdinalIgnoreCase)
            && string.Equals(enlace?.Estado?.Trim(), Constantes.EstadosPagoTPV.PENDIENTE, StringComparison.OrdinalIgnoreCase);

        /// <param name="inscripciones">TODAS las del evento.</param>
        /// <param name="conDocumentos">Ids con facturación del curso o cupón.</param>
        /// <param name="enlaces">Raíz del enlace → su cadena de PagosTPV.</param>
        /// <param name="enlacesCompartidos">Raíces que usa también alguna inscripción de otro evento.</param>
        /// <param name="asistentes">Plazas de cada inscripción (sin la columna, 1).</param>
        /// <param name="ocupadas">Plazas ocupadas ahora (las Reservadas siguen ocupándolas al pasar a Inscrita).</param>
        public static PlanPasoAGratuito Planificar(Evento evento, IEnumerable<EventoInscripcion> inscripciones, ISet<int> conDocumentos,
            IReadOnlyDictionary<int, List<EnlacePagoInscripcionLeido>> enlaces, ISet<int> enlacesCompartidos,
            IReadOnlyDictionary<int, int> asistentes, int ocupadas)
        {
            var plan = new PlanPasoAGratuito();
            List<EventoInscripcion> todas = (inscripciones ?? Enumerable.Empty<EventoInscripcion>()).ToList();
            foreach (EventoInscripcion i in todas)
            {
                List<EnlacePagoInscripcionLeido> cadena = i.PagoTPVId.HasValue && enlaces != null
                    && enlaces.TryGetValue(i.PagoTPVId.Value, out List<EnlacePagoInscripcionLeido> c) ? c : new List<EnlacePagoInscripcionLeido>();
                string motivo = MotivoBloqueo(i, conDocumentos?.Contains(i.Id) ?? false, cadena,
                    i.PagoTPVId.HasValue && (enlacesCompartidos?.Contains(i.PagoTPVId.Value) ?? false));
                if (motivo != null)
                {
                    plan.Bloqueadas[i.Id] = motivo;
                }
            }
            if (plan.HayBloqueo)
            {
                return plan;
            }

            plan.AConvertir.AddRange(todas.Where(i => Estado(i) == EstadoInscripcionEvento.Reservada).OrderBy(i => i.Id));

            // Las caducadas: no las que dijeron «No podré ir», ni las de un cliente que ya tiene otra viva en el evento; de un mismo
            // cliente y contacto, solo la última.
            var vivas = new HashSet<string>(todas
                .Where(i => Estado(i) != EstadoInscripcionEvento.Caducada && Estado(i) != EstadoInscripcionEvento.Anulada)
                .Select(Clave));
            List<EventoInscripcion> caducadas = todas
                .Where(i => Estado(i) == EstadoInscripcionEvento.Caducada)
                .Where(i => ReglasAsistenciaEvento.Respuesta(i.AsistenciaRespuesta) != RespuestaAsistenciaEvento.NoPodraIr)
                .Where(i => !vivas.Contains(Clave(i)))
                .GroupBy(Clave)
                .Select(g => g.OrderByDescending(i => i.FechaCreacion).ThenByDescending(i => i.Id).First())
                .OrderBy(i => i.Id)
                .ToList();
            if (caducadas.Any())
            {
                int necesarias = caducadas.Sum(i => ReglasAsistentesEvento.De(i.Id, asistentes ?? new Dictionary<int, int>()));
                plan.PlazasLibres = evento?.Plazas.HasValue == true ? Math.Max(0, evento.Plazas.Value - ocupadas) : (int?)null;
                if (plan.PlazasLibres.HasValue && necesarias > plan.PlazasLibres.Value)
                {
                    plan.CaducadasSinRecuperar = caducadas.Count;
                }
                else
                {
                    plan.ARecuperar.AddRange(caducadas);
                }
            }

            foreach (EventoInscripcion i in plan.QueCambian.Where(i => i.PagoTPVId.HasValue))
            {
                if (enlaces == null || !enlaces.TryGetValue(i.PagoTPVId.Value, out List<EnlacePagoInscripcionLeido> cadena))
                {
                    continue;
                }
                foreach (EnlacePagoInscripcionLeido enlace in cadena.Where(SeAnula))
                {
                    string empresa = enlace.Empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO;
                    if (!plan.EnlacesAAnular.TryGetValue(empresa, out List<int> ids))
                    {
                        ids = new List<int>();
                        plan.EnlacesAAnular[empresa] = ids;
                    }
                    if (!ids.Contains(enlace.Id))
                    {
                        ids.Add(enlace.Id);
                    }
                }
            }
            return plan;
        }

        /// <summary>El 400 cuando hay alguna con dinero o documentos: cuántas, por qué, y que antes hay que resolverlas a mano.</summary>
        public static string MensajeRechazo(PlanPasoAGratuito plan)
        {
            int n = plan.Bloqueadas.Count;
            string detalle = string.Join(", ", plan.Bloqueadas.GroupBy(b => b.Value).OrderByDescending(g => g.Count())
                .Select(g => $"{g.Count()} {g.Key}"));
            return $"Hay {n} {(n == 1 ? "inscripción" : "inscripciones")} con dinero o documentos de por medio ({detalle}): el evento no puede " +
                "pasar a ser gratuito. Antes hay que abonarlas o anularlas a mano (factura, cupón, cobro o enlace); no se ha cambiado nada.";
        }

        /// <summary>Lo que se le dice a quien ha guardado el evento (null si no ha cambiado ninguna inscripción ni ha quedado ninguna fuera).</summary>
        public static string MensajeResultado(PlanPasoAGratuito plan, int enlacesAnulados)
        {
            var partes = new List<string>();
            int c = plan.AConvertir.Count;
            if (c > 0)
            {
                partes.Add(c == 1
                    ? "1 reserva sin pagar ha pasado a inscrita gratuita."
                    : $"{c} reservas sin pagar han pasado a inscritas gratuitas.");
            }
            int r = plan.ARecuperar.Count;
            if (r > 0)
            {
                partes.Add(r == 1
                    ? "1 inscripción caducada se ha recuperado como inscrita gratuita."
                    : $"{r} inscripciones caducadas se han recuperado como inscritas gratuitas.");
            }
            if (enlacesAnulados > 0)
            {
                partes.Add(enlacesAnulados == 1 ? "Se ha anulado 1 enlace de pago pendiente." : $"Se han anulado {enlacesAnulados} enlaces de pago pendientes.");
            }
            if (plan.CaducadasSinRecuperar > 0)
            {
                partes.Add(string.Format(CultureInfo.InvariantCulture,
                    "Hay {0} {1} que no caben en las plazas libres ({2}): no se ha recuperado ninguna; si quiere, vuelva a inscribirlas a mano.",
                    plan.CaducadasSinRecuperar, plan.CaducadasSinRecuperar == 1 ? "inscripción caducada" : "inscripciones caducadas",
                    plan.PlazasLibres ?? 0));
            }
            return partes.Any() ? string.Join(" ", partes) : null;
        }

        /// <summary>El rastro en Modificaciones de cada una que cambia.</summary>
        public static Modificacion Rastro(EventoInscripcion inscripcion, EstadoInscripcionEvento antes, string modalidadAntes, string formaPagoAntes,
            DateTime? limiteAntes, IEnumerable<int> enlacesAnulados, string usuario)
        {
            string limite = limiteAntes.HasValue ? $", límite de pago {limiteAntes.Value:dd/MM/yyyy}" : "";
            string forma = string.IsNullOrWhiteSpace(formaPagoAntes) ? "" : $", {formaPagoAntes.Trim()}";
            string enlace = inscripcion.PagoTPVId.HasValue ? $", enlace de pago {inscripcion.PagoTPVId.Value}" : "";
            List<int> anulados = (enlacesAnulados ?? Enumerable.Empty<int>()).ToList();
            string nuevoEnlace = anulados.Any()
                ? $"; enlace anulado ({string.Join(", ", anulados)})"
                : inscripcion.PagoTPVId.HasValue ? "; el enlace no estaba pendiente: no se ha anulado" : "";
            return new Modificacion
            {
                Tabla = "EventosInscripciones",
                Anterior = $"Inscripción {inscripcion.Id} (evento {inscripcion.EventoId}, cliente {inscripcion.Cliente?.Trim()}/{inscripcion.Contacto?.Trim()}) " +
                    $"{ReglasInscripcionEvento.Texto(antes)}, {modalidadAntes?.Trim() ?? "Reembolsable"}{forma}{limite}{enlace}",
                Nuevo = $"Inscrita, Gratuita: el evento ha pasado a ser gratuito (Novedades 1233){nuevoEnlace}",
                Usuario = usuario
            };
        }

        private static EstadoInscripcionEvento Estado(EventoInscripcion i) =>
            ReglasInscripcionEvento.Estado(i.Estado) ?? EstadoInscripcionEvento.Anulada;

        private static string Clave(EventoInscripcion i) => $"{i.Empresa?.Trim()}|{i.Cliente?.Trim()}|{i.Contacto?.Trim()}";
    }
}
