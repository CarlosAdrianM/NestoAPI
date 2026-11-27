using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// El «momento» de una push de campaña (Carlos 27/11/26: mandar más de una push de la MISMA campaña sin repetir lo mismo; vale para
    /// cualquier campaña, nada de una en concreto). Va en la petición (<see cref="PeticionCampanaPushDTO.Momento"/>).
    /// </summary>
    public static class MomentosPushCampana
    {
        /// <summary>La primera push (por defecto): todo como antes del 27/11/26.</summary>
        public const string ARRANQUE = "Arranque";
        /// <summary>Otra push a mitad de campaña: sin lo ya anunciado por push y primero lo que convierte.</summary>
        public const string RECORDATORIO = "Recordatorio";
        /// <summary>La de los últimos días (la campaña acaba hoy o mañana): primero lo que miró y no compró.</summary>
        public const string CIERRE = "Cierre";

        /// <summary>El momento con su nombre del contrato (sin dato, <see cref="ARRANQUE"/>); null si no es ninguno.</summary>
        public static string Normalizar(string momento)
        {
            string m = momento?.Trim();
            if (string.IsNullOrEmpty(m) || string.Equals(m, ARRANQUE, StringComparison.OrdinalIgnoreCase))
            {
                return ARRANQUE;
            }
            if (string.Equals(m, RECORDATORIO, StringComparison.OrdinalIgnoreCase))
            {
                return RECORDATORIO;
            }
            return string.Equals(m, CIERRE, StringComparison.OrdinalIgnoreCase) ? CIERRE : null;
        }
    }

    /// <summary>Lo que el registro en sombra (dbo.OfertasClienteMostradas) sabe de un producto de la campaña para un cliente.</summary>
    public class InteresOfertaCliente
    {
        public string Producto { get; set; }
        /// <summary>Ya se le anunció por PUSH de esta campaña (cualquier día).</summary>
        public bool AnunciadaPorPush { get; set; }
        public DateTime? VistaEl { get; set; }
        public DateTime? AbiertaEl { get; set; }
        public DateTime? AnadidaEl { get; set; }
    }

    /// <summary>Lo leído del registro para todos los destinatarios de un envío (una sola lectura).</summary>
    public class InteresesPushCampana
    {
        /// <summary>False si no existe dbo.OfertasClienteMostradas: entonces no se quita nada.</summary>
        public bool TablaExiste { get; set; }
        /// <summary>Cliente → producto → lo que se sabe.</summary>
        public Dictionary<string, Dictionary<string, InteresOfertaCliente>> PorCliente { get; set; } =
            new Dictionary<string, Dictionary<string, InteresOfertaCliente>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, InteresOfertaCliente> DelCliente(string cliente)
            => cliente != null && PorCliente != null && PorCliente.TryGetValue(cliente.Trim(), out Dictionary<string, InteresOfertaCliente> suyos)
                ? suyos
                : new Dictionary<string, InteresOfertaCliente>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>La lista que se recorre (con stock y precio) para la push de un cliente, y cuántas se han quitado por ya anunciadas.</summary>
    public class ListaPushCliente
    {
        public List<OfertaElegida> Lista { get; set; } = new List<OfertaElegida>();
        public int QuitadasYaAnunciadas { get; set; }
    }

    /// <summary>
    /// LA REGLA (pura) del momento de la push de campaña: qué se admite, cuándo acaba la campaña y qué lista de su ranking se recorre.
    /// <list type="bullet">
    /// <item>Arranque: lo «Personal» del ranking, como siempre.</item>
    /// <item>Recordatorio: fuera lo que ya se le anunció por push de esta campaña; primero lo que convierte (le toca, lo suele pedir, de
    /// sus marcas), después lo demás, cada grupo en el orden del ranking.</item>
    /// <item>Cierre: primero lo que añadió al carrito, abrió o vio de la campaña (en ese orden; a igualdad, el del ranking), aunque ya se
    /// le anunciase, con el motivo <see cref="MotivosOfertaParaTi.LO_MIRASTE"/>; después lo «Personal» sin lo ya anunciado.</item>
    /// </list>
    /// Lo comprado desde el inicio de la campaña y lo pendiente no está en el ranking (<see cref="ReglaOfertasParaTi.Evaluar"/>), así que
    /// no vuelve. Stock y «mejora su precio» los sigue mirando quien recorre la lista.
    /// </summary>
    public static class ReglaMomentoPushCampana
    {
        public const string MOTIVO_YA_ANUNCIADAS = "Ya se le han anunciado todas sus ofertas.";
        public const string AVISO_SIN_REGISTRO = "No existe dbo.OfertasClienteMostradas: no se ha quitado nada de lo ya anunciado ni se sabe lo que miró cada cliente.";

        public const string TEXTO_ANADIDA = "Lo añadiste al carrito y sigue con descuento";
        public const string TEXTO_ABIERTA = "Lo miraste y sigue con descuento";
        public const string TEXTO_VISTA = "Sigue con descuento";

        /// <summary>El 400 de usted si se pide un momento que no existe.</summary>
        public const string MENSAJE_MOMENTO_DESCONOCIDO = "El momento tiene que ser «Arranque», «Recordatorio» o «Cierre».";

        /// <summary>Los motivos que más convierten (datos de la primera campaña): primero en el Recordatorio.</summary>
        public static bool Convierte(OfertaElegida oferta)
            => oferta != null && (oferta.Motivo == MotivosOfertaParaTi.TE_TOCA || oferta.Motivo == MotivosOfertaParaTi.LO_SUELES_PEDIR
                || oferta.Motivo == MotivosOfertaParaTi.DE_TUS_MARCAS);

        /// <summary>
        /// El fin de la campaña con su hora: el más tardío de sus filas vigentes; una fila sin hora (a las 0:00) acaba al final del día
        /// (23:59). Null si alguna fila no tiene fin o no hay filas.
        /// </summary>
        public static DateTime? Fin(IEnumerable<CandidataOferta> candidatas, string campana)
        {
            List<CandidataOferta> suyas = (candidatas ?? Enumerable.Empty<CandidataOferta>())
                .Where(c => c != null && ReglaOfertasParaTi.Igual(c.Campana, campana)).ToList();
            if (suyas.Count == 0 || suyas.Any(c => c.FechaHasta == null))
            {
                return null;
            }
            return suyas.Max(c => FinDeFila(c.FechaHastaExacta ?? c.FechaHasta.Value));
        }

        /// <summary>Una fecha de fin sin hora (0:00) es el día entero: hasta las 23:59.</summary>
        public static DateTime FinDeFila(DateTime fechaHasta)
            => fechaHasta.TimeOfDay == TimeSpan.Zero ? fechaHasta.Date.AddHours(23).AddMinutes(59) : fechaHasta;

        /// <summary>
        /// Si se puede mandar una push de cierre: solo si la campaña acaba hoy o mañana (y no ha acabado ya). Null si vale; si no, el
        /// mensaje del 400 (de usted).
        /// </summary>
        public static string ValidarCierre(DateTime? fin, DateTime ahora)
        {
            if (fin == null)
            {
                return "La push de cierre solo se puede mandar si la campaña acaba hoy o mañana, y esta no tiene fecha de fin.";
            }
            DateTime dia = fin.Value.Date;
            if (dia < ahora.Date || fin.Value < ahora)
            {
                return $"La campaña ya ha terminado ({fin.Value:dd/MM/yyyy HH:mm}): no se puede mandar la push de cierre.";
            }
            if (dia > ahora.Date.AddDays(1))
            {
                return $"La push de cierre solo se puede mandar si la campaña acaba hoy o mañana, y esta acaba el {fin.Value:dd/MM/yyyy}. Use «Recordatorio».";
            }
            return null;
        }

        /// <summary>La lista que se recorre para la push de este cliente, según el momento.</summary>
        public static ListaPushCliente Lista(string momento, IReadOnlyList<OfertaElegida> ranking, IReadOnlyDictionary<string, InteresOfertaCliente> intereses)
        {
            string m = MomentosPushCampana.Normalizar(momento) ?? MomentosPushCampana.ARRANQUE;
            List<OfertaElegida> todas = (ranking ?? new List<OfertaElegida>()).Where(o => o?.Candidata?.Producto != null).ToList();
            List<OfertaElegida> personales = todas.Where(o => o.Personal).ToList();
            var resultado = new ListaPushCliente();
            if (m == MomentosPushCampana.ARRANQUE)
            {
                resultado.Lista = personales;
                return resultado;
            }
            IReadOnlyDictionary<string, InteresOfertaCliente> suyos = intereses ?? new Dictionary<string, InteresOfertaCliente>(StringComparer.OrdinalIgnoreCase);
            bool Anunciada(OfertaElegida o) => suyos.TryGetValue(o.Candidata.Producto.Trim(), out InteresOfertaCliente i) && i != null && i.AnunciadaPorPush;

            var delante = new List<OfertaElegida>();
            var yaDelante = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (m == MomentosPushCampana.CIERRE)
            {
                // Lo que miró y no ha comprado: puede repetir lo ya anunciado (es justo el recordatorio de lo que le interesó)
                delante = todas
                    .Select((o, k) => new { o, k, nivel = Nivel(suyos, o) })
                    .Where(x => x.nivel < 3)
                    .OrderBy(x => x.nivel)
                    .ThenBy(x => x.k)
                    .Select(x => LoMiraste(x.o, x.nivel))
                    .Where(o => yaDelante.Add(o.Candidata.Producto.Trim()))
                    .ToList();
            }
            List<OfertaElegida> resto = personales.Where(o => !yaDelante.Contains(o.Candidata.Producto.Trim())).ToList();
            resultado.QuitadasYaAnunciadas = resto.Count(Anunciada);
            resto = resto.Where(o => !Anunciada(o)).ToList();
            if (m == MomentosPushCampana.RECORDATORIO)
            {
                // Desempate de grupo sobre el ranking que ya hay: primero lo que convierte, cada grupo en su orden
                resto = resto.Select((o, k) => new { o, k }).OrderBy(x => Convierte(x.o) ? 0 : 1).ThenBy(x => x.k).Select(x => x.o).ToList();
            }
            resultado.Lista = delante.Concat(resto).ToList();
            return resultado;
        }

        /// <summary>0 añadida, 1 abierta, 2 vista, 3 nada.</summary>
        internal static int Nivel(IReadOnlyDictionary<string, InteresOfertaCliente> intereses, OfertaElegida o)
        {
            if (intereses == null || !intereses.TryGetValue(o.Candidata.Producto.Trim(), out InteresOfertaCliente i) || i == null)
            {
                return 3;
            }
            return i.AnadidaEl.HasValue ? 0 : i.AbiertaEl.HasValue ? 1 : i.VistaEl.HasValue ? 2 : 3;
        }

        /// <summary>Una copia (el ranking va guardado en el contexto y lo comparten la franja y «Ver todas»: no se toca).</summary>
        private static OfertaElegida LoMiraste(OfertaElegida o, int nivel)
            => new OfertaElegida
            {
                Candidata = o.Candidata,
                Motivo = MotivosOfertaParaTi.LO_MIRASTE,
                Texto = nivel == 0 ? TEXTO_ANADIDA : nivel == 1 ? TEXTO_ABIERTA : TEXTO_VISTA,
                CantidadSugerida = o.CantidadSugerida,
                Habitual = o.Habitual,
                Probabilidad = o.Probabilidad,
                Personal = true,
                Grupo = o.Grupo
            };
    }
}
