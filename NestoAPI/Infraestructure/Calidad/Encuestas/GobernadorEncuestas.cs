using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.Encuestas
{
    /// <summary>
    /// NestoAPI#914 (encuestas, corte E1): EL GOBERNADOR. Toda encuesta pasa por aquí. PURO: dado el historial de encuestas de un cliente, su
    /// situación y las candidatas de un día, elige como mucho UNA y dice por qué las demás no. Reglas (Carlos 27/11/26):
    /// <list type="number">
    /// <item>Fuera quien tenga baja de comunicaciones (<see cref="MotivoDescarteEncuesta.Baja"/>) o un impago en gestión (<see cref="MotivoDescarteEncuesta.Impago"/>).</item>
    /// <item>Como mucho una encuesta cada <c>diasEntre</c> días (el que se le pase; nunca menos de <see cref="ParametrosEncuestas.DIAS_ENTRE_MINIMO"/>).
    /// Si ignoró las dos últimas, el intervalo se duplica, y se sigue duplicando con cada ignorada más (tope <see cref="INTERVALO_MAXIMO_DIAS"/>).</item>
    /// <item>Nunca el mismo tema dos veces seguidas ni el mismo tema en menos de 12 meses (en el de Vendedor, por vendedor: <see cref="CandidataEncuesta.ClaveTema"/>).</item>
    /// <item>Sin canal (ni push ni correo válido), fuera.</item>
    /// <item>Entre las que quedan: problema resuelto &gt; un cambio &gt; una primera vez &gt; el resto; a igualdad, el tema por el que hace más
    /// tiempo que no se le pregunta (nunca = el que más); después, el evento más reciente.</item>
    /// </list>
    /// </summary>
    public static class GobernadorEncuestas
    {
        /// <summary>Tope de la duplicación por ignoradas: como mucho una encuesta al año.</summary>
        public const int INTERVALO_MAXIMO_DIAS = 365;
        public const int MESES_SIN_REPETIR_TEMA = 12;
        /// <summary>Cuántas ignoradas seguidas (las últimas) hacen falta para empezar a duplicar.</summary>
        public const int IGNORADAS_PARA_DUPLICAR = 2;

        /// <summary>El intervalo base con el suelo aplicado (ningún valor lo baja de 30).</summary>
        public static int ConSuelo(int diasEntre) => Math.Max(diasEntre, ParametrosEncuestas.DIAS_ENTRE_MINIMO);

        /// <summary>
        /// El intervalo que toca con ese historial: el base (con suelo) y, si las <see cref="IGNORADAS_PARA_DUPLICAR"/> últimas fueron ignoradas,
        /// ×2, ×4, ×8… con cada ignorada más, hasta <see cref="INTERVALO_MAXIMO_DIAS"/>.
        /// </summary>
        public static int Intervalo(IEnumerable<EncuestaHistorial> historial, int diasEntre)
        {
            int baseDias = ConSuelo(diasEntre);
            int ignoradasSeguidas = 0;
            foreach (EncuestaHistorial h in (historial ?? Enumerable.Empty<EncuestaHistorial>()).OrderByDescending(h => h.Dia))
            {
                if (!h.Ignorada)
                {
                    break;
                }
                ignoradasSeguidas++;
            }
            if (ignoradasSeguidas < IGNORADAS_PARA_DUPLICAR)
            {
                return Math.Min(baseDias, INTERVALO_MAXIMO_DIAS);
            }
            long dias = baseDias;
            for (int i = IGNORADAS_PARA_DUPLICAR; i <= ignoradasSeguidas && dias < INTERVALO_MAXIMO_DIAS; i++)
            {
                dias *= 2;
            }
            return (int)Math.Min(dias, INTERVALO_MAXIMO_DIAS);
        }

        /// <summary>Las candidatas de UN cliente para UN día.</summary>
        public static DecisionEncuestaCliente Decidir(DateTime dia, IReadOnlyCollection<EncuestaHistorial> historial,
            SituacionClienteEncuesta situacion, IReadOnlyCollection<CandidataEncuesta> candidatas, int diasEntre)
        {
            dia = dia.Date;
            List<EncuestaHistorial> previas = (historial ?? new List<EncuestaHistorial>()).Where(h => h != null && h.Dia.Date <= dia)
                .OrderByDescending(h => h.Dia).ToList();
            var decision = new DecisionEncuestaCliente { IntervaloDias = Intervalo(previas, diasEntre) };
            EncuestaHistorial ultima = previas.FirstOrDefault();
            bool enTope = ultima != null && (dia - ultima.Dia.Date).TotalDays < decision.IntervaloDias;
            DateTime haceUnAno = dia.AddMonths(-MESES_SIN_REPETIR_TEMA);

            var validas = new List<CandidataEncuesta>();
            foreach (CandidataEncuesta c in (candidatas ?? new List<CandidataEncuesta>()).Where(c => c != null))
            {
                MotivoDescarteEncuesta? motivo = null;
                if (situacion?.DeBaja == true)
                {
                    motivo = MotivoDescarteEncuesta.Baja;
                }
                else if (situacion?.ImpagoEnGestion == true)
                {
                    motivo = MotivoDescarteEncuesta.Impago;
                }
                else if (enTope)
                {
                    motivo = MotivoDescarteEncuesta.TopeEntreEncuestas;
                }
                else if (EsTemaRepetido(c.ClaveTema, ultima, previas, haceUnAno))
                {
                    motivo = MotivoDescarteEncuesta.TemaRepetido;
                }
                else if (string.IsNullOrWhiteSpace(c.Canal) || c.Canal == CanalesEncuesta.SIN_CANAL)
                {
                    motivo = MotivoDescarteEncuesta.SinCanal;
                }
                if (motivo.HasValue)
                {
                    decision.Descartes.Add(new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(c, motivo.Value));
                }
                else
                {
                    validas.Add(c);
                }
            }

            List<CandidataEncuesta> ordenadas = Ordenar(validas, previas);
            decision.Elegida = ordenadas.FirstOrDefault();
            foreach (CandidataEncuesta otra in ordenadas.Skip(1))
            {
                decision.Descartes.Add(new KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>(otra, MotivoDescarteEncuesta.OtraCandidataElegida));
            }
            return decision;
        }

        internal static bool EsTemaRepetido(string clave, EncuestaHistorial ultima, IEnumerable<EncuestaHistorial> previas, DateTime haceUnAno)
        {
            if (ultima != null && string.Equals(ultima.ClaveTema, clave, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return previas.Any(h => string.Equals(h.ClaveTema, clave, StringComparison.OrdinalIgnoreCase) && h.Dia.Date > haceUnAno);
        }

        /// <summary>Por prioridad (la primera gana). Determinista.</summary>
        internal static List<CandidataEncuesta> Ordenar(IEnumerable<CandidataEncuesta> candidatas, IReadOnlyCollection<EncuestaHistorial> previas)
        {
            DateTime UltimaDelTema(TemaEncuesta tema) =>
                previas.Where(h => h.Tema == tema).Select(h => h.Dia).DefaultIfEmpty(DateTime.MinValue).Max();
            return candidatas
                .OrderBy(c => (int)c.Clase)
                .ThenBy(c => UltimaDelTema(c.Tema))
                .ThenByDescending(c => c.FechaEvento)
                .ThenBy(c => (int)c.Tema)
                .ThenBy(c => c.Origen, StringComparer.Ordinal)
                .ThenBy(c => c.Pedido ?? int.MaxValue)
                .ThenBy(c => c.Vendedor ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(c => c.Contacto ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Un día entero: agrupa las candidatas por cliente y decide cada uno con su historial (el de <paramref name="historial"/>, que NO se
        /// modifica: quien llama añade las elegidas si sigue simulando).
        /// </summary>
        public static List<DecisionEncuestaCliente> DecidirDia(DateTime dia, IEnumerable<CandidataEncuesta> candidatas,
            IReadOnlyDictionary<string, List<EncuestaHistorial>> historial, Func<string, SituacionClienteEncuesta> situacion, int diasEntre)
        {
            var decisiones = new List<DecisionEncuestaCliente>();
            foreach (IGrouping<string, CandidataEncuesta> cliente in (candidatas ?? Enumerable.Empty<CandidataEncuesta>())
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Cliente))
                .GroupBy(c => c.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                List<EncuestaHistorial> suyo = null;
                _ = historial != null && historial.TryGetValue(cliente.Key, out suyo);
                decisiones.Add(Decidir(dia, suyo, situacion?.Invoke(cliente.Key), cliente.ToList(), diasEntre));
            }
            return decisiones;
        }
    }
}
