using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#763: una franja del «horario preferible» de un cliente/contacto (una fila de dbo.ClientesHorarioPreferible).
    /// Se intenta respetar, pero sin compromiso.
    /// </summary>
    public class FranjaHorario
    {
        public FranjaHorario(int diaSemana, TimeSpan desde, TimeSpan hasta)
        {
            DiaSemana = diaSemana;
            Desde = desde;
            Hasta = hasta;
        }

        /// <summary>1 = lunes … 7 = domingo.</summary>
        public int DiaSemana { get; }
        public TimeSpan Desde { get; }
        public TimeSpan Hasta { get; }

        public bool Contiene(TimeSpan hora) => hora >= Desde && hora <= Hasta;

        public string TextoHoras => FormatoHorarioPreferible.Hora(Desde) + "-" + FormatoHorarioPreferible.Hora(Hasta);

        public FranjaHorarioPreferibleDTO ADto() => new FranjaHorarioPreferibleDTO
        {
            DiaSemana = DiaSemana,
            Desde = FormatoHorarioPreferible.Hora(Desde),
            Hasta = FormatoHorarioPreferible.Hora(Hasta)
        };

        public override string ToString() => FormatoHorarioPreferible.LETRAS[DiaSemana - 1] + " " + TextoHoras;
    }

    /// <summary>
    /// NestoAPI#763: el horario preferible en texto («L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00») y al revés, y las reglas de
    /// lo que se puede guardar: días 1..7, «Desde» menor que «Hasta», como mucho <see cref="MAXIMO_FRANJAS_DIA"/> franjas por día
    /// y sin solaparse.
    /// </summary>
    public static class FormatoHorarioPreferible
    {
        public const int MAXIMO_FRANJAS_DIA = 2;
        /// <summary>Lunes … domingo (X = miércoles).</summary>
        public static readonly string[] LETRAS = { "L", "M", "X", "J", "V", "S", "D" };

        private static readonly Regex GRUPO = new Regex(
            @"^\s*(?<dias>[LMXJVSD](\s*-\s*[LMXJVSD])?(\s*,\s*[LMXJVSD](\s*-\s*[LMXJVSD])?)*)\s+(?<franjas>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FRANJA = new Regex(@"^\s*(?<desde>\d{1,2}(:\d{2})?)\s*-\s*(?<hasta>\d{1,2}(:\d{2})?)\s*$", RegexOptions.CultureInvariant);

        /// <summary>1 = lunes … 7 = domingo.</summary>
        public static int DiaSemana(DayOfWeek dia) => dia == DayOfWeek.Sunday ? 7 : (int)dia;

        public static string Hora(TimeSpan hora) => hora.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        /// <summary>«HH:mm» o «H» → hora; null si no es una hora válida (00:00 a 23:59).</summary>
        public static TimeSpan? LeerHora(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string[] partes = texto.Trim().Split(':');
            if (partes.Length > 2 || !int.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out int horas))
            {
                return null;
            }
            int minutos = 0;
            if (partes.Length == 2 && (partes[1].Length != 2 || !int.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutos)))
            {
                return null;
            }
            if (horas < 0 || horas > 23 || minutos < 0 || minutos > 59)
            {
                return null;
            }
            return new TimeSpan(horas, minutos, 0);
        }

        /// <summary>
        /// Las franjas en texto, agrupando los días con las mismas horas: «L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00». Vacío si
        /// no hay franjas.
        /// </summary>
        public static string Texto(IEnumerable<FranjaHorario> franjas)
        {
            List<FranjaHorario> lista = Ordenar(franjas);
            if (lista.Count == 0)
            {
                return string.Empty;
            }
            // Día → «10:00-14:00, 17:00-20:00»; los días con el mismo texto van juntos.
            var porDia = lista.GroupBy(f => f.DiaSemana)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(f => f.TextoHoras)));
            var grupos = porDia.GroupBy(p => p.Value)
                .Select(g => new { Horas = g.Key, Dias = g.Select(p => p.Key).OrderBy(d => d).ToList() })
                .OrderBy(g => g.Dias[0]);
            return string.Join("; ", grupos.Select(g => Dias(g.Dias) + " " + g.Horas));
        }

        /// <summary>Las franjas de un solo día en texto («10:00-14:00, 17:00-20:00»), o vacío.</summary>
        public static string TextoDia(IEnumerable<FranjaHorario> franjas, int diaSemana)
            => string.Join(", ", Ordenar(franjas).Where(f => f.DiaSemana == diaSemana).Select(f => f.TextoHoras));

        /// <summary>Días seguidos como intervalo: [1,2,3,4,5,7] → «L-V, D»; dos seguidos, con coma: [1,2] → «L, M».</summary>
        private static string Dias(List<int> dias)
        {
            var trozos = new List<string>();
            int i = 0;
            while (i < dias.Count)
            {
                int j = i;
                while (j + 1 < dias.Count && dias[j + 1] == dias[j] + 1)
                {
                    j++;
                }
                if (j - i >= 2)
                {
                    trozos.Add(LETRAS[dias[i] - 1] + "-" + LETRAS[dias[j] - 1]);
                }
                else
                {
                    for (int k = i; k <= j; k++)
                    {
                        trozos.Add(LETRAS[dias[k] - 1]);
                    }
                }
                i = j + 1;
            }
            return string.Join(", ", trozos);
        }

        public static List<FranjaHorario> Ordenar(IEnumerable<FranjaHorario> franjas)
            => (franjas ?? Enumerable.Empty<FranjaHorario>()).Where(f => f != null).OrderBy(f => f.DiaSemana).ThenBy(f => f.Desde).ToList();

        /// <summary>
        /// El texto «L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00» → franjas validadas. Vacío o en blanco → sin franjas (bien).
        /// Devuelve null y <paramref name="error"/> si no se entiende o no es válido.
        /// </summary>
        public static List<FranjaHorario> Leer(string texto, out string error)
        {
            error = null;
            var franjas = new List<FranjaHorario>();
            if (string.IsNullOrWhiteSpace(texto))
            {
                return franjas;
            }
            foreach (string grupo in texto.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                Match m = GRUPO.Match(grupo);
                if (!m.Success)
                {
                    error = $"No se entiende «{grupo.Trim()}». Escriba los días y las horas así: «L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00».";
                    return null;
                }
                List<int> dias = LeerDias(m.Groups["dias"].Value);
                if (dias == null)
                {
                    error = $"Los días de «{grupo.Trim()}» no son válidos: use L, M, X, J, V, S, D y guiones para los intervalos (L-V).";
                    return null;
                }
                foreach (string trozo in Regex.Split(m.Groups["franjas"].Value, @"\s*,\s*|\s+y\s+", RegexOptions.IgnoreCase))
                {
                    Match f = FRANJA.Match(trozo);
                    TimeSpan? desde = f.Success ? LeerHora(f.Groups["desde"].Value) : null;
                    TimeSpan? hasta = f.Success ? LeerHora(f.Groups["hasta"].Value) : null;
                    if (!desde.HasValue || !hasta.HasValue)
                    {
                        error = $"La franja «{trozo.Trim()}» no es válida: escríbala como 10:00-14:00.";
                        return null;
                    }
                    franjas.AddRange(dias.Select(d => new FranjaHorario(d, desde.Value, hasta.Value)));
                }
            }
            error = Validar(franjas);
            return error == null ? Ordenar(franjas) : null;
        }

        private static List<int> LeerDias(string texto)
        {
            var dias = new SortedSet<int>();
            foreach (string trozo in texto.Split(','))
            {
                string[] extremos = trozo.Split('-');
                int desde = Array.IndexOf(LETRAS, extremos[0].Trim().ToUpperInvariant()) + 1;
                int hasta = extremos.Length > 1 ? Array.IndexOf(LETRAS, extremos[1].Trim().ToUpperInvariant()) + 1 : desde;
                if (desde < 1 || hasta < desde)
                {
                    return null;
                }
                for (int d = desde; d <= hasta; d++)
                {
                    _ = dias.Add(d);
                }
            }
            return dias.ToList();
        }

        /// <summary>Las franjas de la API (DTO) → franjas validadas; null y <paramref name="error"/> si alguna no vale.</summary>
        public static List<FranjaHorario> Leer(IEnumerable<FranjaHorarioPreferibleDTO> franjas, out string error)
        {
            error = null;
            var lista = new List<FranjaHorario>();
            foreach (FranjaHorarioPreferibleDTO f in franjas ?? Enumerable.Empty<FranjaHorarioPreferibleDTO>())
            {
                if (f == null)
                {
                    continue;
                }
                TimeSpan? desde = LeerHora(f.Desde);
                TimeSpan? hasta = LeerHora(f.Hasta);
                if (f.DiaSemana < 1 || f.DiaSemana > 7)
                {
                    error = $"El día {f.DiaSemana} no es válido: va de 1 (lunes) a 7 (domingo).";
                    return null;
                }
                if (!desde.HasValue || !hasta.HasValue)
                {
                    error = $"Las horas del {NombreDia(f.DiaSemana)} no son válidas: escríbalas como 10:00.";
                    return null;
                }
                lista.Add(new FranjaHorario(f.DiaSemana, desde.Value, hasta.Value));
            }
            error = Validar(lista);
            return error == null ? Ordenar(lista) : null;
        }

        /// <summary>Null si se pueden guardar; si no, por qué (de usted).</summary>
        public static string Validar(IEnumerable<FranjaHorario> franjas)
        {
            foreach (IGrouping<int, FranjaHorario> dia in Ordenar(franjas).GroupBy(f => f.DiaSemana))
            {
                if (dia.Key < 1 || dia.Key > 7)
                {
                    return $"El día {dia.Key} no es válido: va de 1 (lunes) a 7 (domingo).";
                }
                List<FranjaHorario> delDia = dia.ToList();
                FranjaHorario alReves = delDia.FirstOrDefault(f => f.Desde >= f.Hasta);
                if (alReves != null)
                {
                    return $"En la franja {alReves.TextoHoras} del {NombreDia(dia.Key)} la hora de inicio tiene que ser anterior a la de fin.";
                }
                if (delDia.Count > MAXIMO_FRANJAS_DIA)
                {
                    return $"El {NombreDia(dia.Key)} tiene {delDia.Count} franjas: como mucho puede tener {MAXIMO_FRANJAS_DIA} (mañana y tarde).";
                }
                for (int i = 1; i < delDia.Count; i++)
                {
                    if (delDia[i].Desde <= delDia[i - 1].Hasta)
                    {
                        return $"Las franjas {delDia[i - 1].TextoHoras} y {delDia[i].TextoHoras} del {NombreDia(dia.Key)} se solapan: júntelas en una o sepárelas.";
                    }
                }
            }
            return null;
        }

        public static string NombreDia(int diaSemana)
        {
            string[] nombres = { "lunes", "martes", "miércoles", "jueves", "viernes", "sábado", "domingo" };
            return diaSemana >= 1 && diaSemana <= 7 ? nombres[diaSemana - 1] : diaSemana.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// NestoAPI#763 (corte 2): propone el horario preferible a partir de lo que hay escrito en los comentarios de la ficha
    /// (Comentarios y ComentarioRuta), con reglas. Solo es una PROPUESTA: queda «por revisar» hasta que una persona la acepta en
    /// Nesto. Ante la duda (más de dos franjas, horarios distintos por día, «horario comercial»…) no propone nada (null) y la
    /// persona lo escribe a mano.
    /// <para>Reglas: las horas «9», «9h», «9:30», «9.30», «9,30», «9'30», «9´30»; los intervalos «9 a 14», «9-14», «de 9 hasta
    /// 14»; las horas de 1 a 6 son de la tarde («de 11 a 2 y de 5 a 8» → 11:00-14:00, 17:00-20:00); los intervalos de «cierra de
    /// 14 a 16» no cuentan; los días, de lunes a viernes salvo que diga otros («lunes a jueves», «martes a viernes», «L-V») o que
    /// cierra algún día («lunes cerrado», «cierra los martes»).</para>
    /// </summary>
    public static class ExtractorHorarioComentarios
    {
        /// <summary>Lo que pone el script de carga entre Comentarios y ComentarioRuta.</summary>
        public const string SEPARADOR_CAMPOS = "||";
        private const string HORA = @"\d{1,2}(?::\d{2})?";
        private static readonly Regex INTERVALO = new Regex(
            @"(?<!\d|\d:|\d-)(?<cierra>(?:CIERRA|CERRAD[OA])\s+(?:DE\s+)?)?(?<desde>" + HORA + @")\s*H?\s*(?:A|-|HASTA(?:\s+LAS)?)\s*(?:LAS\s+)?(?<hasta>" + HORA + @")(?![\d:]|-\d)"
            + @"|(?<!\d|\d:|\d-)(?<desde>\d{1,2}:\d{2})\s+(?<hasta>\d{1,2}:\d{2})(?![\d:]|-\d)",
            RegexOptions.CultureInvariant);
        private static readonly string[] DIAS = { "LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES", "SABADO", "DOMINGO" };
        private static readonly Regex INTERVALO_DIAS = new Regex(
            @"\b(?:DE\s+)?(?<desde>LUNES|MARTES|MIERCOLES|JUEVES|VIERNES|SABADO)S?\s+(?:A|-)\s+(?<hasta>MARTES|MIERCOLES|JUEVES|VIERNES|SABADO|DOMINGO)S?\b"
            + @"|\b(?<desde>[LMXJ])\s*-\s*(?<hasta>[MXJVS])\b(?!\s*\d)",
            RegexOptions.CultureInvariant);
        /// <summary>Un horario distinto por días («L- 10 a 19h. M - 10 A 13h.»): no se propone.</summary>
        private static readonly Regex DIA_CON_HORAS = new Regex(@"(?:^|[\s.,;])[LMXJVSD]\s*-\s*\d", RegexOptions.CultureInvariant);

        /// <summary>El horario propuesto en franjas, o null si las reglas no lo ven claro.</summary>
        public static List<FranjaHorario> Proponer(string comentarios)
        {
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return null;
            }
            string texto = Normalizar(comentarios);
            if (DIA_CON_HORAS.IsMatch(texto))
            {
                return null;
            }
            // Primero lo que va detrás de «HORARIO» en cada campo (Comentarios || ComentarioRuta); si ahí no hay horas, todo el texto.
            string[] trozos = texto.Split(new[] { SEPARADOR_CAMPOS }, StringSplitOptions.None);
            List<(TimeSpan, TimeSpan)> horas = Intervalos(string.Join(" " + SEPARADOR_CAMPOS + " ", trozos
                .Where(t => t.Contains("HORARIO"))
                .Select(t => t.Substring(t.IndexOf("HORARIO", StringComparison.Ordinal)))));
            if (horas == null)
            {
                return null;
            }
            if (horas.Count == 0)
            {
                horas = Intervalos(texto);
            }
            if (horas == null || horas.Count == 0 || horas.Count > FormatoHorarioPreferible.MAXIMO_FRANJAS_DIA)
            {
                return null;
            }
            List<int> dias = Dias(texto);
            if (dias.Count == 0)
            {
                return null;
            }
            var franjas = dias.SelectMany(d => horas.Select(h => new FranjaHorario(d, h.Item1, h.Item2))).ToList();
            return FormatoHorarioPreferible.Validar(franjas) == null ? FormatoHorarioPreferible.Ordenar(franjas) : null;
        }

        /// <summary>El horario propuesto en texto («L-V 10:00-14:00, 17:00-20:00»), o null.</summary>
        public static string ProponerTexto(string comentarios)
        {
            List<FranjaHorario> franjas = Proponer(comentarios);
            return franjas == null ? null : FormatoHorarioPreferible.Texto(franjas);
        }

        /// <summary>Mayúsculas, sin tildes, y «9.30», «9,30», «9'30», «9´30», «9h30» → «9:30».</summary>
        internal static string Normalizar(string texto)
        {
            string descompuesto = texto.ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c == '\r' || c == '\n' ? ' ' : c);
                }
            }
            string limpio = sb.ToString().Normalize(NormalizationForm.FormC);
            // «13,30H»: detrás de los minutos puede venir la H pegada (no hay \b), por eso (?!\d) y no \b.
            limpio = Regex.Replace(limpio, @"(?<=(?<!\d|\d:)\d{1,2})(?:[.,'´`’]|H)(?=\d{2}(?!\d))", ":");
            return limpio;
        }

        /// <summary>Los intervalos de apertura (sin los de «cierra de…»), corregidos a 24 h. Null si alguno no tiene sentido.</summary>
        private static List<(TimeSpan, TimeSpan)> Intervalos(string texto)
        {
            var resultado = new List<(TimeSpan, TimeSpan)>();
            foreach (Match m in INTERVALO.Matches(texto ?? string.Empty))
            {
                if (m.Groups["cierra"].Success)
                {
                    continue;
                }
                TimeSpan? desde = FormatoHorarioPreferible.LeerHora(m.Groups["desde"].Value);
                TimeSpan? hasta = FormatoHorarioPreferible.LeerHora(m.Groups["hasta"].Value);
                if (!desde.HasValue || !hasta.HasValue)
                {
                    return null;
                }
                TimeSpan d = Tarde(desde.Value);
                TimeSpan h = Tarde(hasta.Value);
                if (h <= d && h.Hours < 12)
                {
                    h = h.Add(TimeSpan.FromHours(12));
                }
                if (h <= d || h.TotalHours >= 24 || d.Hours < 6)
                {
                    return null;
                }
                if (!resultado.Contains((d, h)))
                {
                    resultado.Add((d, h));
                }
            }
            return resultado.OrderBy(r => r.Item1).ToList();
        }

        /// <summary>De 1 a 6 es de la tarde (nadie abre a las 2 de la madrugada).</summary>
        private static TimeSpan Tarde(TimeSpan hora) => hora.Hours >= 1 && hora.Hours <= 6 ? hora.Add(TimeSpan.FromHours(12)) : hora;

        private static List<int> Dias(string texto)
        {
            List<int> dias = Enumerable.Range(1, 5).ToList();
            MatchCollection intervalos = INTERVALO_DIAS.Matches(texto);
            if (intervalos.Count > 1)
            {
                return new List<int>();
            }
            if (intervalos.Count == 1)
            {
                int desde = Dia(intervalos[0].Groups["desde"].Value);
                int hasta = Dia(intervalos[0].Groups["hasta"].Value);
                if (desde < 1 || hasta < desde)
                {
                    return new List<int>();
                }
                dias = Enumerable.Range(desde, hasta - desde + 1).ToList();
            }
            for (int d = 1; d <= DIAS.Length; d++)
            {
                string nombre = DIAS[d - 1];
                if (Regex.IsMatch(texto, $@"\b(?:{nombre}S?\s+CERRADO|CIERRA\s+(?:LOS\s+|EL\s+)?{nombre}S?)\b", RegexOptions.CultureInvariant))
                {
                    _ = dias.Remove(d);
                }
            }
            return dias;
        }

        private static int Dia(string texto)
        {
            int porNombre = Array.IndexOf(DIAS, texto) + 1;
            return porNombre > 0 ? porNombre : Array.IndexOf(FormatoHorarioPreferible.LETRAS, texto) + 1;
        }
    }
}
