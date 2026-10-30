using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#746 (política de Pilar aprobada por Carlos, 30/10/26): «se reserva sin pagar MIENTRAS HAYA PLAZAS». Los valores
    /// se ajustan sin publicar en ParametrosUsuario (empresa 1, usuario «(defecto)»).
    /// </summary>
    public class ParametrosPlazasLlenasEventos
    {
        /// <summary>«1» (o sin fila): encendido; «0»/«false»/«no»: apagado. Sin las columnas del script, apagado siempre.</summary>
        public const string CLAVE_ACTIVO = "EventosPlazasLlenasActivo";
        /// <summary>Horas que tiene la reserva sin pagar para pagar desde el aviso de «evento lleno». Sin fila: 48.</summary>
        public const string CLAVE_HORAS_PAGO = "EventosPlazasLlenasHorasPago";
        public const int HORAS_PAGO_POR_DEFECTO = 48;
        internal const string USUARIO_DEFECTO = "(defecto)";

        public bool Activo { get; set; } = true;
        public int HorasPago { get; set; } = HORAS_PAGO_POR_DEFECTO;

        public static ParametrosPlazasLlenasEventos Interpretar(string activo, string horas)
        {
            string a = activo?.Trim().ToLowerInvariant();
            bool apagado = a == "0" || a == "false" || a == "no";
            int h = int.TryParse(horas?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int leidas) && leidas > 0
                ? leidas
                : HORAS_PAGO_POR_DEFECTO;
            return new ParametrosPlazasLlenasEventos { Activo = !apagado, HorasPago = h };
        }

        /// <summary>Las filas «(defecto)» de la empresa por defecto. Si no se pueden leer, los valores por defecto.</summary>
        public static ParametrosPlazasLlenasEventos Leer(NVEntities db)
        {
            try
            {
                string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
                // Where + ToList: los filtros en SQL, que ignora el relleno de los char
                var filas = db.ParametrosUsuario
                    .Where(p => p.Empresa == empresa && p.Usuario == USUARIO_DEFECTO && (p.Clave == CLAVE_ACTIVO || p.Clave == CLAVE_HORAS_PAGO))
                    .Select(p => new { p.Clave, p.Valor })
                    .ToList();
                return Interpretar(
                    filas.FirstOrDefault(f => f.Clave?.Trim() == CLAVE_ACTIVO)?.Valor,
                    filas.FirstOrDefault(f => f.Clave?.Trim() == CLAVE_HORAS_PAGO)?.Valor);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se han podido leer los parámetros: {ex.Message}", ex));
                return new ParametrosPlazasLlenasEventos();
            }
        }
    }

    /// <summary>Cuándo vence una reserva sin pagar: con hora (aviso de «evento lleno») o solo el último día para pagar.</summary>
    public class VencimientoReservaEvento
    {
        public DateTime Momento { get; set; }
        public bool ConHora { get; set; }
    }

    /// <summary>NestoAPI#746: las reglas de las reservas sin pagar con el evento lleno (sin base de datos).</summary>
    public static class ReglasPlazasLlenas
    {
        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Reserva sin pagar que ocupa plaza (Reservada y no ha dicho «No podré ir»).</summary>
        public static bool EsReservaSinPagar(EventoInscripcion inscripcion) =>
            ReglasInscripcionEvento.Estado(inscripcion?.Estado) == EstadoInscripcionEvento.Reservada
            && ReglasInscripcionEvento.OcupaPlaza(inscripcion);

        /// <summary>Solo los eventos con Plazas: lleno cuando las que ocupan plaza llegan a las plazas.</summary>
        public static bool EstaLleno(Evento evento, int ocupadas) =>
            evento?.Plazas.HasValue == true && ocupadas >= evento.Plazas.Value;

        /// <summary>
        /// Hasta cuándo puede pagar la reserva avisada: <paramref name="horas"/> desde el aviso, pero nunca después de su último
        /// día para pagar (a las 23:59) ni de la hora en que empieza el evento.
        /// </summary>
        public static DateTime Plazo(DateTime ahora, int horas, DateTime? ultimoDiaPago, Evento evento)
        {
            DateTime plazo = ahora.AddHours(horas);
            if (ultimoDiaPago.HasValue)
            {
                DateTime tope = ultimoDiaPago.Value.Date.AddDays(1).AddMinutes(-1);
                plazo = tope < plazo ? tope : plazo;
            }
            if (evento != null)
            {
                DateTime inicio = evento.Fecha.Date + (evento.HoraInicio ?? new TimeSpan(23, 59, 0));
                plazo = inicio < plazo ? inicio : plazo;
            }
            // Siempre un plazo por delante (nunca se avisa de algo ya vencido)
            return plazo > ahora ? plazo : ahora.AddHours(1);
        }

        /// <summary>El vencimiento de una reserva: el plazo del aviso si lo tiene; si no, su último día para pagar.</summary>
        public static VencimientoReservaEvento Vencimiento(EventoInscripcion inscripcion, Evento evento, DateTime? plazoAviso)
        {
            if (plazoAviso.HasValue)
            {
                return new VencimientoReservaEvento { Momento = plazoAviso.Value, ConHora = true };
            }
            DateTime? limite = ReglasInscripcionEvento.LimitePagoEfectivo(inscripcion, evento);
            return limite.HasValue ? new VencimientoReservaEvento { Momento = limite.Value.Date, ConHora = false } : null;
        }

        /// <summary>«el lunes 2 de noviembre a las 18:00».</summary>
        public static string FechaYHora(DateTime momento) => "el " + DiaYHora(momento);

        /// <summary>«lunes 2 de noviembre a las 18:00» (para «antes del …»).</summary>
        public static string DiaYHora(DateTime momento) =>
            momento.ToString("dddd d 'de' MMMM", es) + " a las " + momento.ToString("HH:mm", es);

        public static string Texto(VencimientoReservaEvento vencimiento) =>
            vencimiento == null
                ? null
                : vencimiento.ConHora
                    ? FechaYHora(vencimiento.Momento)
                    : "el " + vencimiento.Momento.ToString("dddd d 'de' MMMM", es) + " (último día para pagarla)";

        /// <summary>
        /// El rechazo de una inscripción nueva sin pagar con el evento lleno (de usted, vale para la tienda online y para Nesto):
        /// cuántas plazas son reservas sin pagar y cuándo vence la primera.
        /// </summary>
        public static string MensajeSinPlazas(Evento evento, IReadOnlyCollection<VencimientoReservaEvento> reservasSinPagar)
        {
            string titulo = evento?.Titulo?.Trim();
            int plazas = evento?.Plazas ?? 0;
            string ocupadas = plazas == 1 ? "su única plaza está ocupada" : $"las {plazas} plazas están ocupadas";
            List<VencimientoReservaEvento> reservas = (reservasSinPagar ?? new List<VencimientoReservaEvento>()).ToList();
            if (!reservas.Any())
            {
                return $"No quedan plazas en «{titulo}»: {ocupadas}.";
            }
            VencimientoReservaEvento primera = reservas.Where(r => r != null).OrderBy(r => r.Momento).FirstOrDefault();
            string cuantas = reservas.Count == 1
                ? "una de ellas es una reserva pendiente de pago"
                : $"{reservas.Count} de ellas son reservas pendientes de pago";
            string vence = primera == null
                ? ""
                : reservas.Count == 1 ? $", que vence {Texto(primera)}" : $"; la primera vence {Texto(primera)}";
            return $"No quedan plazas en «{titulo}»: {ocupadas} y {cuantas}{vence}. " +
                "Si no se pagan a tiempo, esas plazas quedarán libres y podrá inscribirse entonces.";
        }
    }

    /// <summary>NestoAPI#746: el aviso de «evento lleno» de una reserva (columnas fuera del EDMX).</summary>
    public class MarcaPlazasLlenas
    {
        public int Id { get; set; }
        public DateTime? AvisoPlazasLlenas { get; set; }
        public DateTime? PlazoPagoPlazasLlenas { get; set; }
    }

    /// <summary>
    /// NestoAPI#746: EventosInscripciones.AvisoPlazasLlenas y PlazoPagoPlazasLlenas (fuera del EDMX, SQL directo). Sin las
    /// columnas (script sin lanzar), <see cref="Disponible"/> es false y la política no avisa ni anula nada.
    /// </summary>
    public interface IRepositorioPlazasLlenas
    {
        bool Disponible();
        Task<Dictionary<int, MarcaPlazasLlenas>> Leer(IReadOnlyCollection<int> inscripciones);
        Task Apuntar(int inscripcion, DateTime? aviso, DateTime? plazo);
    }

    public class RepositorioPlazasLlenas : IRepositorioPlazasLlenas
    {
        internal const string SQL_EXISTE = "SELECT CAST(CASE WHEN COL_LENGTH('dbo.EventosInscripciones', 'AvisoPlazasLlenas') IS NULL " +
            "OR COL_LENGTH('dbo.EventosInscripciones', 'PlazoPagoPlazasLlenas') IS NULL THEN 0 ELSE 1 END AS bit)";
        internal const string SQL_LEER = "SELECT Id, AvisoPlazasLlenas, PlazoPagoPlazasLlenas FROM dbo.EventosInscripciones WITH (NOLOCK) " +
            "WHERE (AvisoPlazasLlenas IS NOT NULL OR PlazoPagoPlazasLlenas IS NOT NULL) AND Id IN ({0})";
        internal const string SQL_APUNTAR = "UPDATE dbo.EventosInscripciones SET AvisoPlazasLlenas = @aviso, PlazoPagoPlazasLlenas = @plazo WHERE Id = @id";

        private static readonly ComprobadorEsquemaEventos columnas = new ComprobadorEsquemaEventos(SQL_EXISTE);
        private readonly NVEntities db;

        public RepositorioPlazasLlenas(NVEntities db)
        {
            this.db = db;
        }

        public bool Disponible() => columnas.Existe(db);

        public async Task<Dictionary<int, MarcaPlazasLlenas>> Leer(IReadOnlyCollection<int> inscripciones)
        {
            List<int> ids = (inscripciones ?? new List<int>()).Distinct().ToList();
            var resultado = new Dictionary<int, MarcaPlazasLlenas>();
            if (!ids.Any() || !Disponible())
            {
                return resultado;
            }
            foreach (List<int> tanda in RepositorioPersonaAsistenteEventos.Tandas(ids, 500))
            {
                List<MarcaPlazasLlenas> filas = await db.Database
                    .SqlQuery<MarcaPlazasLlenas>(string.Format(CultureInfo.InvariantCulture, SQL_LEER, string.Join(",", tanda)))
                    .ToListAsync().ConfigureAwait(false);
                foreach (MarcaPlazasLlenas fila in filas)
                {
                    resultado[fila.Id] = fila;
                }
            }
            return resultado;
        }

        public async Task Apuntar(int inscripcion, DateTime? aviso, DateTime? plazo)
        {
            if (!Disponible())
            {
                return;
            }
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_APUNTAR,
                new SqlParameter("@aviso", SqlDbType.DateTime) { Value = (object)aviso ?? DBNull.Value },
                new SqlParameter("@plazo", SqlDbType.DateTime) { Value = (object)plazo ?? DBNull.Value },
                new SqlParameter("@id", SqlDbType.Int) { Value = inscripcion }).ConfigureAwait(false);
        }
    }

    /// <summary>NestoAPI#746: una reserva sin pagar avisada (o anulada) por el evento lleno.</summary>
    public class AvisoPlazasLlenasInscripcion
    {
        public Evento Evento { get; set; }
        public EventoInscripcion Inscripcion { get; set; }
        /// <summary>Hasta cuándo puede pagar (el del aviso).</summary>
        public DateTime Plazo { get; set; }
        /// <summary>La página del enlace de pago, si lo tiene.</summary>
        public string UrlPago { get; set; }
    }

    /// <summary>NestoAPI#746: lo que ha hecho una pasada de la política en uno o en todos los eventos.</summary>
    public class ResultadoPlazasLlenas
    {
        /// <summary>Reservas a las que avisar ahora (ya con su plazo apuntado en la BD).</summary>
        public List<AvisoPlazasLlenasInscripcion> Avisos { get; set; } = new List<AvisoPlazasLlenasInscripcion>();
        /// <summary>Reservas avisadas que han pasado a Caducada por no pagar a tiempo.</summary>
        public List<AvisoPlazasLlenasInscripcion> Anuladas { get; set; } = new List<AvisoPlazasLlenasInscripcion>();
        /// <summary>Plazos que han vencido con plazas libres: no se anulan y se les quita la marca (por si se vuelve a llenar).</summary>
        public int Indultadas { get; set; }
        /// <summary>El próximo plazo pendiente de cada evento (para programar su anulación).</summary>
        public Dictionary<int, DateTime> ProximosPlazos { get; set; } = new Dictionary<int, DateTime>();
        /// <summary>Por qué no se ha hecho nada (apagado, sin script). Null si se ha procesado.</summary>
        public string MotivoNoSeProcesa { get; set; }
    }
}
