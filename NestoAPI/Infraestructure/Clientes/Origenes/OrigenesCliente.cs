using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>NestoAPI#623: una fila del catálogo <c>dbo.OrigenesCliente</c> (también es el DTO de GET api/Clientes/Origenes).</summary>
    public class OrigenCliente
    {
        public string Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Campana { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }
        public bool Activo { get; set; }
        /// <summary>Plazo para la primera llamada, en horas de días laborables (<see cref="ReglasLeadsCampana.PlazoLlamadaLead"/>).</summary>
        public int HorasParaLlamar { get; set; }
    }

    /// <summary>NestoAPI#623: el origen de un cliente (<c>dbo.ClientesOrigen</c> con la descripción del catálogo).</summary>
    public class OrigenAsignadoCliente
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Origen { get; set; }
        public string Descripcion { get; set; }
        public DateTime FechaOrigen { get; set; }
        public string UsuarioOrigen { get; set; }
        public DateTime? FechaAvisoRetraso { get; set; }
    }

    /// <summary>
    /// NestoAPI#623 (corte 5): un cliente con origen de campaña activa, con su contacto principal y la fecha de su último rapport
    /// (Estado 0 «se habló» o 1 «no contestó», de cualquier tipo, usuario y contacto). Lo lee
    /// <see cref="IRepositorioOrigenesCliente.LeerLeads"/>; si está pendiente de llamar lo decide <see cref="ReglasLeadsCampana.PendienteDeLlamar"/>.
    /// </summary>
    public class LeadCampana
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Telefono { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Vendedor { get; set; }
        public string Origen { get; set; }
        public string Descripcion { get; set; }
        public DateTime FechaOrigen { get; set; }
        public int HorasParaLlamar { get; set; }
        public DateTime? FechaAvisoRetraso { get; set; }
        public DateTime? UltimoRapport { get; set; }
    }

    /// <summary>
    /// NestoAPI#623: las reglas de los leads de campaña (núcleo común de «clientes para contactar» y del job
    /// <c>leads-sin-llamar</c>).
    /// </summary>
    public static class ReglasLeadsCampana
    {
        /// <summary>Un lead sin rapport deja de salir arriba (y de avisarse) a los 30 días de su origen.</summary>
        public const int DIAS_MAXIMOS_LEAD = 30;
        public const int HORAS_POR_DEFECTO = 24;

        /// <summary>
        /// NestoAPI#623 (Carlos 05/11/26): hasta cuándo hay para llamar al lead. Se cuentan días LABORABLES (de lunes a viernes no
        /// festivos; <paramref name="esLaborable"/>): primero los días enteros de <paramref name="horas"/> (cada 24 h = un día
        /// laborable más, saltando los que no lo son) y luego el resto de horas; si el resultado cae en un día no laborable,
        /// pasa al siguiente laborable A LA MISMA HORA.
        /// <list type="bullet">
        /// <item>Martes 10:00 + 24 h → miércoles 10:00.</item>
        /// <item>Viernes 16:00 + 24 h → lunes 16:00 (sábado y domingo no cuentan).</item>
        /// <item>Miércoles 16:00 con el jueves festivo → viernes 16:00.</item>
        /// <item>Sábado 10:00 → lunes 10:00 (cuenta desde el domingo, que tampoco es laborable).</item>
        /// </list>
        /// Horas &lt;= 0 → <see cref="HORAS_POR_DEFECTO"/>. Sin <paramref name="esLaborable"/>, de lunes a viernes.
        /// </summary>
        public static DateTime PlazoLlamadaLead(DateTime fechaOrigen, int horas, Func<DateTime, bool> esLaborable)
        {
            if (horas <= 0)
            {
                horas = HORAS_POR_DEFECTO;
            }
            Func<DateTime, bool> laborable = esLaborable ?? EsLaborableSinFestivos;
            DateTime plazo = fechaOrigen;
            for (int i = 0; i < horas / 24; i++)
            {
                plazo = SiguienteLaborable(plazo.AddDays(1), laborable);
            }
            plazo = plazo.AddHours(horas % 24);
            return SiguienteLaborable(plazo, laborable);
        }

        /// <summary>Esa fecha si su día es laborable; si no, el mismo instante del siguiente día laborable (máx. 30 días).</summary>
        private static DateTime SiguienteLaborable(DateTime fecha, Func<DateTime, bool> laborable)
        {
            int vueltas = 0;
            while (!laborable(fecha.Date) && vueltas++ < 30)
            {
                fecha = fecha.AddDays(1);
            }
            return fecha;
        }

        /// <summary>De lunes a viernes (sin mirar festivos).</summary>
        public static bool EsLaborableSinFestivos(DateTime dia) => dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday;

        /// <summary>De lunes a viernes y no festivo en la delegación (GestorFestivos.EsFestivo).</summary>
        public static Func<DateTime, bool> Laborable(Func<DateTime, string, bool> esFestivo, string delegacion) =>
            dia => EsLaborableSinFestivos(dia) && (esFestivo == null || !esFestivo(dia, delegacion));

        /// <summary>
        /// Sin rapport (Estado 0 o 1) desde el día de su origen —cuenta uno del mismo día: los rapports no siempre llevan
        /// hora— y con menos de <see cref="DIAS_MAXIMOS_LEAD"/> días.
        /// </summary>
        public static bool PendienteDeLlamar(LeadCampana lead, DateTime ahora)
        {
            if (lead == null)
            {
                return false;
            }
            if (lead.UltimoRapport.HasValue && lead.UltimoRapport.Value >= lead.FechaOrigen.Date)
            {
                return false;
            }
            return lead.FechaOrigen >= ahora.AddDays(-DIAS_MAXIMOS_LEAD);
        }

        /// <summary>«Lead de Meta» si el código del origen empieza por META; si no, «Lead».</summary>
        public static string Etiqueta(string origen) =>
            origen != null && origen.Trim().StartsWith("META", StringComparison.OrdinalIgnoreCase) ? "Lead de Meta" : "Lead";

        /// <summary>
        /// «Lead de Meta: llamar antes de las 16:00 del 09/11» o, pasado el plazo, «Lead de Meta: llamada atrasada (desde el 09/11 16:00)».
        /// </summary>
        public static string Frase(LeadCampana lead, DateTime plazo, DateTime ahora)
        {
            string etiqueta = Etiqueta(lead?.Origen);
            return ahora <= plazo
                ? $"{etiqueta}: llamar antes de las {plazo:HH:mm} del {plazo:dd/MM}"
                : $"{etiqueta}: llamada atrasada (desde el {plazo:dd/MM HH:mm})";
        }

        /// <summary>El texto de la campana del job: «El lead de Meta 12345 PELUQUERÍA X lleva más de 24 h sin llamar».</summary>
        public static string TextoAvisoRetraso(LeadCampana lead)
        {
            string etiqueta = Etiqueta(lead?.Origen) == "Lead de Meta" ? "lead de Meta" : "lead";
            int horas = lead == null || lead.HorasParaLlamar <= 0 ? HORAS_POR_DEFECTO : lead.HorasParaLlamar;
            return $"El {etiqueta} {lead?.Cliente?.Trim()} {lead?.Nombre?.Trim()} lleva más de {horas} h sin llamar";
        }

        /// <summary>Solo los orígenes activos (y, si tienen FechaHasta, sin pasar), salvo <paramref name="todos"/>. Por descripción.</summary>
        public static List<OrigenCliente> Filtrar(IEnumerable<OrigenCliente> catalogo, bool todos, DateTime hoy) =>
            (catalogo ?? Enumerable.Empty<OrigenCliente>())
                .Where(o => o != null && (todos || EstaActivo(o, hoy)))
                .OrderBy(o => o.Descripcion, StringComparer.CurrentCulture)
                .ToList();

        public static bool EstaActivo(OrigenCliente origen, DateTime hoy) =>
            origen != null && origen.Activo && (!origen.FechaHasta.HasValue || origen.FechaHasta.Value.Date >= hoy.Date);
    }
}
