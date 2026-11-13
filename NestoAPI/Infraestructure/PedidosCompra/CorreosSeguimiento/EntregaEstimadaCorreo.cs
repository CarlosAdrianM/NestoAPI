using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857: la entrega que anuncia el propio correo («Entrega estimada: lunes 16/11/2026 entre 10:45 - 14:45»).</summary>
    public class EntregaEstimada
    {
        public DateTime Dia { get; set; }
        /// <summary>Principio del tramo horario; null si el correo no da tramo (o solo da el final).</summary>
        public TimeSpan? Desde { get; set; }
        /// <summary>Final del tramo horario («antes de las 14:00» = solo Hasta); null si no da hora.</summary>
        public TimeSpan? Hasta { get; set; }
    }

    /// <summary>
    /// NestoAPI#857: saca del texto de un correo (ya sin HTML) el día y el tramo de entrega que anuncia. PURA: la fecha del correo
    /// entra por parámetro. Solo cuenta una fecha que vaya tras una etiqueta («Entrega estimada», «Fecha prevista de entrega»,
    /// «Entrega reprogramada», «entrega está programada para», «Estimated delivery»…) o una frase («te lo entregaremos, como máximo, el…», «llegará el…», «will be delivered on…») y
    /// dentro de <see cref="VENTANA"/> caracteres (y <see cref="LINEAS_VENTANA"/> saltos de línea); una fecha suelta no cuenta.
    /// Fechas siempre día/mes (España). Se descarta si el día es anterior al del correo o más de <see cref="DIAS_MAXIMOS"/> posterior.
    /// </summary>
    public static class ExtractorEntregaEstimada
    {
        public const int VENTANA = 120;
        public const int LINEAS_VENTANA = 4;
        public const int VENTANA_TRAMO = 70;
        public const int DIAS_MAXIMOS = 60;
        /// <summary>Una fecha sin año que quedaría más de estos meses antes del correo es del año siguiente.</summary>
        public const int MESES_ATRAS_SIN_ANIO = 2;

        // Todo sobre el texto normalizado (minúsculas y sin tildes)
        private static readonly Regex Etiqueta = new Regex(
            @"(?<![a-z])(?:fecha\s+(?:de\s+entrega\s+)?(?:estimada|prevista)(?:\s+de\s+entrega)?|fecha\s+de\s+entrega"
            + @"|entrega\s+(?:estimada|prevista)(?:\s+para)?"
            // 13/11/26: «Entrega reprogramada» + fecha en la línea siguiente (UPS) y «Tu entrega está programada para el…» (Nacex).
            // Siempre con «entrega» delante: «recogida programada para…» no es una entrega
            + @"|entrega\s+(?:esta\s+|ha\s+sido\s+|se\s+ha\s+)?(?:re)?programad[oa](?:\s+para)?"
            + @"|(?:estimated|expected|scheduled|planned)\s+delivery(?:\s+date)?|delivery\s+date"
            + @"|entregaremos|seran?\s+entregad[oa]s?|se\s+entregaran?|llegaran?"
            + @"|will\s+be\s+delivered(?:\s+(?:on|by))?|will\s+arrive(?:\s+(?:on|by))?)(?![a-z])",
            RegexOptions.Compiled);

        private const string MESES = "enero|ene|febrero|feb|marzo|mar|abril|abr|mayo|may|junio|jun|julio|jul|agosto|ago|septiembre|setiembre|sept|sep"
            + "|octubre|oct|noviembre|nov|diciembre|dic|january|jan|february|march|april|apr|june|july|august|aug|september|october|november|december|dec";

        private static readonly Regex FechaNumerica = new Regex(
            @"(?<![\d/.\-])(\d{1,2})([/\-.])(\d{1,2})(?:\2(\d{4}|\d{2}))?(?![\d/.\-]*\d)", RegexOptions.Compiled);

        private static readonly Regex FechaEnLetras = new Regex(
            @"(?<!\d)(\d{1,2})(?:\s*de\s+|\s*-\s*|\s+)(" + MESES + @")(?![a-z])\.?(?:\s*(?:de\s+|del\s+|-|,)?\s*(\d{4}))?(?!\d)",
            RegexOptions.Compiled);

        private static readonly Regex FechaEnIngles = new Regex(
            @"(?<![a-z])(" + MESES + @")(?![a-z])\.?\s+(\d{1,2})(?:st|nd|rd|th)?(?!\d)(?:,?\s*(\d{4}))?(?!\d)", RegexOptions.Compiled);

        private const string HORA_1 = @"(\d{1,2})(?:[:.h](\d{2})|\s*h(?:oras|rs|r)?(?![a-z]))";
        private const string HORA_2 = @"(\d{1,2})(?:[:.h](\d{2})|\s*h(?:oras|rs|r)?(?![a-z]))";

        private static readonly Regex Tramo = new Regex(
            @"^[\s,;:.]*(?:(?:en\s+)?(?:la\s+)?(?:franja(?:\s+horaria)?|horario)\s*:?\s*)?(?:(?:entre|de|desde)(?:\s+las)?|between|from)?\s*"
            + HORA_1 + @"\s*(?:-|a(?:\s+las)?|y(?:\s+las)?|hasta(?:\s+las)?|and|to)\s*" + HORA_2,
            RegexOptions.Compiled);

        private static readonly Regex SoloHasta = new Regex(
            @"^[\s,;:.]*(?:antes\s+de(?:\s+las)?|hasta(?:\s+las)?|no\s+mas\s+tarde\s+de(?:\s+las)?|before|by)\s*" + HORA_1,
            RegexOptions.Compiled);

        private static readonly Dictionary<string, int> NumeroMes = new Dictionary<string, int>
        {
            ["enero"] = 1, ["ene"] = 1, ["january"] = 1, ["jan"] = 1,
            ["febrero"] = 2, ["feb"] = 2, ["february"] = 2,
            ["marzo"] = 3, ["mar"] = 3, ["march"] = 3,
            ["abril"] = 4, ["abr"] = 4, ["april"] = 4, ["apr"] = 4,
            ["mayo"] = 5, ["may"] = 5,
            ["junio"] = 6, ["jun"] = 6, ["june"] = 6,
            ["julio"] = 7, ["jul"] = 7, ["july"] = 7,
            ["agosto"] = 8, ["ago"] = 8, ["august"] = 8, ["aug"] = 8,
            ["septiembre"] = 9, ["setiembre"] = 9, ["sept"] = 9, ["sep"] = 9, ["september"] = 9,
            ["octubre"] = 10, ["oct"] = 10, ["october"] = 10,
            ["noviembre"] = 11, ["nov"] = 11, ["november"] = 11,
            ["diciembre"] = 12, ["dic"] = 12, ["december"] = 12, ["dec"] = 12
        };

        /// <summary>La primera etiqueta o frase de entrega con una fecha válida detrás; null si no hay ninguna.</summary>
        public static EntregaEstimada Extraer(string texto, DateTime fechaCorreo)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string t = Normalizar(texto);
            foreach (Match etiqueta in Etiqueta.Matches(t))
            {
                int inicio = etiqueta.Index + etiqueta.Length;
                string ventana = Ventana(t, inicio);
                FechaEncontrada fecha = PrimeraFecha(ventana, fechaCorreo);
                if (fecha == null)
                {
                    continue;
                }
                var entrega = new EntregaEstimada { Dia = fecha.Dia };
                int finFecha = inicio + fecha.Fin;
                string despues = t.Substring(finFecha, Math.Min(VENTANA_TRAMO, t.Length - finFecha));
                LeerTramo(despues, entrega);
                return entrega;
            }
            return null;
        }

        private class FechaEncontrada
        {
            public DateTime Dia { get; set; }
            public int Inicio { get; set; }
            public int Fin { get; set; }
        }

        private static string Ventana(string t, int inicio)
        {
            string ventana = t.Substring(inicio, Math.Min(VENTANA, t.Length - inicio));
            int saltos = 0;
            for (int i = 0; i < ventana.Length; i++)
            {
                if (ventana[i] == '\n' && ++saltos > LINEAS_VENTANA)
                {
                    return ventana.Substring(0, i);
                }
            }
            return ventana;
        }

        /// <summary>De las fechas válidas de la ventana, la que va antes.</summary>
        private static FechaEncontrada PrimeraFecha(string ventana, DateTime fechaCorreo)
        {
            var encontradas = new List<FechaEncontrada>();
            foreach (Match m in FechaNumerica.Matches(ventana))
            {
                bool conAnio = m.Groups[4].Success;
                // Sin año solo con barra («16/11»): «10.45» o «10-12» suelen ser otra cosa
                if (!conAnio && m.Groups[2].Value != "/")
                {
                    continue;
                }
                Anadir(encontradas, m, Entero(m.Groups[1].Value), Entero(m.Groups[3].Value), conAnio ? m.Groups[4].Value : null, fechaCorreo);
            }
            foreach (Match m in FechaEnLetras.Matches(ventana))
            {
                Anadir(encontradas, m, Entero(m.Groups[1].Value), NumeroMes[m.Groups[2].Value], m.Groups[3].Success ? m.Groups[3].Value : null, fechaCorreo);
            }
            foreach (Match m in FechaEnIngles.Matches(ventana))
            {
                Anadir(encontradas, m, Entero(m.Groups[2].Value), NumeroMes[m.Groups[1].Value], m.Groups[3].Success ? m.Groups[3].Value : null, fechaCorreo);
            }
            // La primera de la ventana manda: si no vale (fuera de rango), no se busca otra más lejos
            FechaEncontrada primera = encontradas.OrderBy(f => f.Inicio).FirstOrDefault();
            return primera != null && primera.Dia != DateTime.MinValue ? primera : null;
        }

        private static void Anadir(List<FechaEncontrada> lista, Match m, int dia, int mes, string anio, DateTime fechaCorreo)
        {
            DateTime? fecha = Resolver(dia, mes, anio, fechaCorreo);
            if (fecha.HasValue && !lista.Any(f => f.Inicio == m.Index))
            {
                lista.Add(new FechaEncontrada { Dia = EnRango(fecha.Value, fechaCorreo) ? fecha.Value : DateTime.MinValue, Inicio = m.Index, Fin = m.Index + m.Length });
            }
        }

        /// <summary>Año de 2 cifras = 20xx; sin año, el del correo (o el siguiente si quedaría más de 2 meses atrás).</summary>
        internal static DateTime? Resolver(int dia, int mes, string anio, DateTime fechaCorreo)
        {
            if (mes < 1 || mes > 12 || dia < 1 || dia > 31)
            {
                return null;
            }
            int a;
            if (string.IsNullOrEmpty(anio))
            {
                a = fechaCorreo.Year;
                if (!EsFecha(a, mes, dia))
                {
                    return null;
                }
                if (new DateTime(a, mes, dia) < fechaCorreo.Date.AddMonths(-MESES_ATRAS_SIN_ANIO))
                {
                    a++;
                }
            }
            else
            {
                a = Entero(anio);
                if (anio.Length == 2)
                {
                    a += 2000;
                }
            }
            return EsFecha(a, mes, dia) ? new DateTime(a, mes, dia) : (DateTime?)null;
        }

        private static bool EsFecha(int anio, int mes, int dia) =>
            anio >= 2000 && anio <= 2100 && mes >= 1 && mes <= 12 && dia >= 1 && dia <= DateTime.DaysInMonth(anio, mes);

        internal static bool EnRango(DateTime dia, DateTime fechaCorreo) =>
            dia >= fechaCorreo.Date && dia <= fechaCorreo.Date.AddDays(DIAS_MAXIMOS);

        private static void LeerTramo(string despues, EntregaEstimada entrega)
        {
            Match tramo = Tramo.Match(despues);
            if (tramo.Success)
            {
                TimeSpan? desde = Hora(tramo.Groups[1].Value, tramo.Groups[2]);
                TimeSpan? hasta = Hora(tramo.Groups[3].Value, tramo.Groups[4]);
                if (desde.HasValue && hasta.HasValue && desde <= hasta)
                {
                    entrega.Desde = desde;
                    entrega.Hasta = hasta;
                }
                return;
            }
            Match solo = SoloHasta.Match(despues);
            if (solo.Success)
            {
                entrega.Hasta = Hora(solo.Groups[1].Value, solo.Groups[2]);
            }
        }

        private static TimeSpan? Hora(string horas, Group minutos)
        {
            int h = Entero(horas);
            int m = minutos.Success ? Entero(minutos.Value) : 0;
            return h >= 0 && h <= 23 && m >= 0 && m <= 59 ? new TimeSpan(h, m, 0) : (TimeSpan?)null;
        }

        private static int Entero(string valor) => int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;

        /// <summary>Minúsculas, sin tildes, espacios duros como espacios y rayas como guiones. Misma longitud que el original.</summary>
        internal static string Normalizar(string texto)
        {
            var sb = new StringBuilder(texto.Length);
            foreach (char c in texto)
            {
                char limpio;
                switch (c)
                {
                    case ' ':
                    case ' ':
                    case ' ':
                    case '\t':
                        limpio = ' ';
                        break;
                    case '–':
                    case '—':
                    case '‒':
                        limpio = '-';
                        break;
                    case '\r':
                        limpio = '\n';
                        break;
                    default:
                        string d = c.ToString().Normalize(NormalizationForm.FormD);
                        limpio = char.ToLowerInvariant(d.Length > 0 ? d[0] : c);
                        break;
                }
                _ = sb.Append(limpio);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// NestoAPI#857: desde cuándo se puede servir lo que llega (decisión de Carlos, 12/11/26: siempre el final del tramo, pesimista).
    /// Si llega ANTES de la hora de corte del picking en un día laborable de Algete, ese mismo día; si no (en el corte o después, sin
    /// hora, o día no laborable), el siguiente día laborable de Algete. PURA: la hora de corte y los festivos entran por parámetro.
    /// </summary>
    public static class ReglaDisponibleCompra
    {
        private const int DIAS_MAXIMOS_BUSQUEDA = 60;

        /// <param name="esFestivo">Festivo de Algete (los sábados y domingos ya no cuentan aquí).</param>
        public static DateTime Calcular(DateTime dia, TimeSpan? hasta, TimeSpan horaCorte, Func<DateTime, bool> esFestivo)
        {
            Func<DateTime, bool> festivo = esFestivo ?? (_ => false);
            DateTime d = dia.Date;
            if (hasta.HasValue && hasta.Value < horaCorte && EsLaborable(d, festivo))
            {
                return d;
            }
            DateTime siguiente = d.AddDays(1);
            for (int i = 0; i < DIAS_MAXIMOS_BUSQUEDA && !EsLaborable(siguiente, festivo); i++)
            {
                siguiente = siguiente.AddDays(1);
            }
            return siguiente;
        }

        public static DateTime? Calcular(EntregaEstimada entrega, TimeSpan horaCorte, Func<DateTime, bool> esFestivo) =>
            entrega == null ? (DateTime?)null : Calcular(entrega.Dia, entrega.Hasta, horaCorte, esFestivo);

        private static bool EsLaborable(DateTime dia, Func<DateTime, bool> esFestivo) =>
            dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && !esFestivo(dia);
    }

    /// <summary>
    /// NestoAPI#857: ¿es un envío NUESTRO de salida que llega en copia al buzón de Compras (el aviso de GLS de un pedido de venta)?
    /// Cuenta que el texto diga que el pedido o envío es «de NUEVA VISION» dirigiéndose al que lo recibe («tu/su pedido … de NUEVA
    /// VISION», «your order … from Nueva Vision») o que nos ponga de remitente («remitente: NUEVA VISION», «enviado por NUEVA VISION»,
    /// «from NUEVA VISION», «sender/shipper: NUEVA VISION»). Que nos nombre como DESTINATARIO («¡Hola, Nueva Visión!», «destinatario:
    /// NUEVA VISION») no cuenta: eso es una entrada. «Pedido de NUEVA VISION» sin «tu/su» tampoco: un proveedor puede llamar así a
    /// nuestro pedido de compra. «From:» con dos puntos tampoco (es la cabecera de un reenvío). PURA, sin mayúsculas ni tildes.
    /// </summary>
    public static class EnvioNuestroDeSalida
    {
        private const string NOSOTROS = @"nueva\s+vision(?![a-z])";

        private static readonly Regex Patron = new Regex(
            @"(?<![a-z])(?:(?:tu|su|vuestro|your)\s+(?:pedido|envio|paquete|order|parcel|shipment)(?![a-z])[^\n]{0,50}?(?<![a-z])(?:de|from)\s+" + NOSOTROS
            + @"|(?:remitente|sender|shipper|expedidor)\s*:?\s*" + NOSOTROS
            + @"|(?:enviad[oa]s?|remitid[oa]s?|expedid[oa]s?|sent|shipped)\s+(?:por|by)\s*:?\s*" + NOSOTROS
            + @"|from\s+" + NOSOTROS + ")",
            RegexOptions.Compiled);

        public static bool Es(string texto) =>
            !string.IsNullOrWhiteSpace(texto) && Patron.IsMatch(ExtractorEntregaEstimada.Normalizar(texto));
    }
}
