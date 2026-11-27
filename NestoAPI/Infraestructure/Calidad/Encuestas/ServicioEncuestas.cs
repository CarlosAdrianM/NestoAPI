using Hangfire;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;

namespace NestoAPI.Infraestructure.Calidad.Encuestas
{
    public class TemaEncuestasDTO
    {
        public string Tema { get; set; }
        public int Candidatas { get; set; }
        public int Encuestas { get; set; }
    }

    public class OrigenEncuestasDTO
    {
        public string Origen { get; set; }
        public string Tema { get; set; }
        public int Candidatas { get; set; }
        public int Encuestas { get; set; }
    }

    public class CanalEncuestasDTO
    {
        public string Canal { get; set; }
        public int Encuestas { get; set; }
    }

    public class DiaEncuestasDTO
    {
        public DateTime Dia { get; set; }
        public int Candidatas { get; set; }
        public int Encuestas { get; set; }
    }

    public class DescarteEncuestasDTO
    {
        public string Motivo { get; set; }
        public int Candidatas { get; set; }
    }

    public class ClientesPorEncuestasDTO
    {
        /// <summary>«1», «2» o «3 o más».</summary>
        public string Encuestas { get; set; }
        public int Clientes { get; set; }
    }

    public class ClienteTopeEncuestasDTO
    {
        public string Cliente { get; set; }
        public int Candidatas { get; set; }
        public int DescartadasPorTope { get; set; }
        public int Encuestas { get; set; }
    }

    /// <summary>NestoAPI#914 (encuestas, E1): el volumen de una simulación o de lo que el job ha decidido en sombra.</summary>
    public class SimulacionEncuestasDTO
    {
        public string Empresa { get; set; }
        /// <summary>Días de candidatura [Desde, Hasta).</summary>
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int DiasEntre { get; set; }
        public int Dias { get; set; }
        public int Candidatas { get; set; }
        public int Encuestas { get; set; }
        public decimal MediaPorDia { get; set; }
        public int MaximoPorDia { get; set; }
        public DateTime? DiaDelMaximo { get; set; }
        public int ClientesConCandidatas { get; set; }
        public int ClientesEncuestados { get; set; }
        public List<ClientesPorEncuestasDTO> ClientesPorNumeroDeEncuestas { get; set; } = new List<ClientesPorEncuestasDTO>();
        public int MaximoEncuestasMismoCliente { get; set; }
        /// <summary>Candidatas descartadas por el tope entre encuestas.</summary>
        public int PerdidasPorTope { get; set; }
        public List<TemaEncuestasDTO> PorTema { get; set; } = new List<TemaEncuestasDTO>();
        public List<CanalEncuestasDTO> PorCanal { get; set; } = new List<CanalEncuestasDTO>();
        public List<OrigenEncuestasDTO> PorOrigen { get; set; } = new List<OrigenEncuestasDTO>();
        public List<DescarteEncuestasDTO> Descartes { get; set; } = new List<DescarteEncuestasDTO>();
        public List<DiaEncuestasDTO> PorDia { get; set; } = new List<DiaEncuestasDTO>();
        /// <summary>Los <see cref="ServicioEncuestas.TOP_CLIENTES_TOPE"/> clientes con más candidatas perdidas por el tope (para ver que no se atosiga a los que más compran).</summary>
        public List<ClienteTopeEncuestasDTO> MasDescartadosPorTope { get; set; } = new List<ClienteTopeEncuestasDTO>();
        /// <summary>De usted: fuentes sin leer, parámetros corregidos, bajas sin leer… (null si nada).</summary>
        public string Aviso { get; set; }
        public DateTime CalculadoEl { get; set; }
        /// <summary>Solo en lo guardado en sombra: cuántos días del rango ha procesado el job (null en la simulación).</summary>
        public int? DiasProcesados { get; set; }
    }

    /// <summary>Una fila de la comparación entre intervalos.</summary>
    public class FilaComparacionEncuestasDTO
    {
        public int DiasEntre { get; set; }
        public int Encuestas { get; set; }
        public decimal MediaPorDia { get; set; }
        public int MaximoPorDia { get; set; }
        public int ClientesEncuestados { get; set; }
        public int ClientesCon1 { get; set; }
        public int ClientesCon2 { get; set; }
        public int ClientesCon3oMas { get; set; }
        public int MaximoEncuestasMismoCliente { get; set; }
        public int PerdidasPorTope { get; set; }
        public List<TemaEncuestasDTO> PorTema { get; set; } = new List<TemaEncuestasDTO>();
    }

    public class ComparacionSimulacionEncuestasDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int Candidatas { get; set; }
        public List<FilaComparacionEncuestasDTO> Comparacion { get; set; } = new List<FilaComparacionEncuestasDTO>();
        public List<SimulacionEncuestasDTO> Simulaciones { get; set; } = new List<SimulacionEncuestasDTO>();
        public string Aviso { get; set; }
        public DateTime CalculadoEl { get; set; }
    }

    /// <summary>Faltan las tablas de Scripts/Issue914e_CalidadEncuestas.sql (el controlador da 503).</summary>
    public class EncuestasSinTablasException : Exception
    {
        public EncuestasSinTablasException() : base(ServicioEncuestas.MENSAJE_SIN_TABLAS)
        {
        }
    }

    public interface IServicioEncuestas
    {
        /// <summary>Con <paramref name="diasEntre"/> null, el del parámetro (60 sin fila).</summary>
        SimulacionEncuestasDTO Simular(string empresa, DateTime desde, DateTime hasta, int? diasEntre);
        ComparacionSimulacionEncuestasDTO Comparar(string empresa, DateTime desde, DateTime hasta, IReadOnlyList<int> intervalos);
        SimulacionEncuestasDTO Sombra(string empresa, int dias);
        /// <summary>Lo guardado en sombra con día en [desde, hasta) (el correo semanal: la semana cerrada).</summary>
        SimulacionEncuestasDTO SombraEntre(string empresa, DateTime desde, DateTime hasta);
        /// <summary>El interruptor Calidad.EncuestasSombra (solo «1» enciende).</summary>
        bool SombraEncendida();
    }

    /// <summary>Las candidatas de un rango ya preparadas (sin clientes excluidos y con su canal) y lo que hay que avisar.</summary>
    public class DatosSimulacionEncuestas
    {
        public List<CandidataEncuesta> Candidatas { get; set; } = new List<CandidataEncuesta>();
        public ContextoClientesEncuesta Contexto { get; set; } = new ContextoClientesEncuesta();
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#914 (encuestas, E1, EN SOMBRA): no envía NADA. Simulación hacia atrás sin escribir, lo decidido por el job y el job diario.
    /// </summary>
    public class ServicioEncuestas : IServicioEncuestas
    {
        public const string MENSAJE_SIN_TABLAS = "Faltan las tablas de las encuestas (Scripts/Issue914e_CalidadEncuestas.sql): todavía no se ha guardado nada en sombra.";
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(30);
        public const int TOP_CLIENTES_TOPE = 20;
        /// <summary>Cuánto historial se lee para decidir (el intervalo como mucho es de un año; con margen para las ignoradas y los 12 meses del tema).</summary>
        public const int DIAS_HISTORIAL = 800;
        private const string PREFIJO = "[Encuestas #914]";

        private readonly Func<ILectorEncuestas> lector;
        private readonly ILectorParametrosUsuario parametros;
        private readonly Func<DateTime> ahora;
        private readonly ObjectCache cache;
        private readonly Action<Exception> registrarError;

        public ServicioEncuestas(Func<ILectorEncuestas> lector = null, ILectorParametrosUsuario parametros = null, Func<DateTime> ahora = null,
            ObjectCache cache = null, Action<Exception> registrarError = null)
        {
            this.lector = lector ?? (() => LectorEncuestasSql.DesdeConfiguracion());
            this.parametros = parametros ?? new LectorParametrosUsuario();
            this.ahora = ahora ?? (() => DateTime.Now);
            this.cache = cache ?? MemoryCache.Default;
            this.registrarError = registrarError ?? (ex => ElmahHelper.Log(ex));
        }

        // ------------------------------------------------------------ parámetros

        private string Parametro(string clave)
        {
            try
            {
                return parametros.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, clave);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"{PREFIJO} No se ha podido leer el parámetro {clave}; se usa el valor por defecto. {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>El intervalo del parámetro Calidad.EncuestasDiasEntre (60 sin fila; con suelo de 30 y techo de 180).</summary>
        public int DiasEntreDelParametro(out string aviso) => ParametrosEncuestas.DiasEntreDesdeParametro(Parametro(ParametrosEncuestas.DIAS_ENTRE), out aviso);

        public ParametrosFuentesEncuesta ParametrosFuentes() => new ParametrosFuentesEncuesta
        {
            DiasTrasAparato = ParametrosEncuestas.DiasDesdeParametro(Parametro(ParametrosEncuestas.DIAS_TRAS_APARATO), ParametrosEncuestas.DIAS_TRAS_APARATO_POR_DEFECTO),
            DiasTrasFormacion = ParametrosEncuestas.DiasDesdeParametro(Parametro(ParametrosEncuestas.DIAS_TRAS_FORMACION), ParametrosEncuestas.DIAS_TRAS_FORMACION_POR_DEFECTO)
        };

        public bool SombraEncendida() => ParametrosEncuestas.Encendido(Parametro(ParametrosEncuestas.SOMBRA));

        // ------------------------------------------------------------ lectura y preparación

        /// <summary>Las candidatas del rango con su contexto, en caché 30 min (lo comparten la simulación y la comparación).</summary>
        internal DatosSimulacionEncuestas Datos(string empresa, DateTime desde, DateTime hasta)
        {
            ParametrosFuentesEncuesta p = ParametrosFuentes();
            string clave = string.Format(CultureInfo.InvariantCulture, "CalidadEncuestas|{0}|{1:yyyyMMdd}|{2:yyyyMMdd}|{3}|{4}", empresa, desde, hasta,
                p.DiasTrasAparato, p.DiasTrasFormacion);
            var nuevo = new Lazy<DatosSimulacionEncuestas>(() => Preparar(lector(), empresa, desde, hasta, p), LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<DatosSimulacionEncuestas> usar = cache.AddOrGetExisting(clave, nuevo, DateTimeOffset.UtcNow.Add(DURACION_CACHE)) as Lazy<DatosSimulacionEncuestas> ?? nuevo;
            try
            {
                return usar.Value;
            }
            catch
            {
                _ = cache.Remove(clave);
                throw;
            }
        }

        /// <summary>Lee las candidatas, quita los clientes que nunca se encuestan y les pone su canal.</summary>
        internal static DatosSimulacionEncuestas Preparar(ILectorEncuestas lector, string empresa, DateTime desde, DateTime hasta, ParametrosFuentesEncuesta p)
        {
            LecturaCandidatasEncuesta lectura = lector.LeerCandidatas(empresa, desde, hasta, p);
            List<CandidataEncuesta> candidatas = (lectura.Candidatas ?? new List<CandidataEncuesta>())
                .Where(c => c != null && !ReglaClientesEncuesta.Excluido(c.Cliente)).ToList();
            ContextoClientesEncuesta contexto = lector.LeerContexto(empresa, candidatas.Select(c => c.Cliente).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                ?? new ContextoClientesEncuesta();
            foreach (CandidataEncuesta c in candidatas)
            {
                ReglaCanalEncuesta.Asignar(contexto, c);
            }
            var datos = new DatosSimulacionEncuestas { Candidatas = candidatas, Contexto = contexto };
            if (lectura.FuentesSinLeer?.Count > 0)
            {
                datos.Avisos.Add("No se han leído estas fuentes: " + string.Join(", ", lectura.FuentesSinLeer) + ".");
            }
            datos.Avisos.AddRange(contexto.Avisos ?? new List<string>());
            return datos;
        }

        // ------------------------------------------------------------ simulación

        /// <summary>
        /// Recorre los días [desde, hasta) como si el gobernador se hubiera encendido en <paramref name="desde"/>: historial en memoria que
        /// empieza vacío; ninguna cuenta como ignorada (en sombra no se manda nada). PURO sobre los datos ya leídos.
        /// </summary>
        public static List<DecisionEncuestaCliente> SimularDias(IEnumerable<CandidataEncuesta> candidatas, ContextoClientesEncuesta contexto,
            DateTime desde, DateTime hasta, int diasEntre)
        {
            var historial = new Dictionary<string, List<EncuestaHistorial>>(StringComparer.OrdinalIgnoreCase);
            var todas = new List<DecisionEncuestaCliente>();
            ILookup<DateTime, CandidataEncuesta> porDia = (candidatas ?? Enumerable.Empty<CandidataEncuesta>()).ToLookup(c => c.Dia.Date);
            for (DateTime dia = desde.Date; dia < hasta.Date; dia = dia.AddDays(1))
            {
                DateTime hoy = dia;
                List<DecisionEncuestaCliente> decisiones = GobernadorEncuestas.DecidirDia(hoy, porDia[hoy], historial,
                    cliente => ReglaCanalEncuesta.Situacion(contexto, cliente, hoy), diasEntre);
                foreach (DecisionEncuestaCliente d in decisiones.Where(d => d.Elegida != null))
                {
                    if (!historial.TryGetValue(d.Elegida.Cliente, out List<EncuestaHistorial> suyo))
                    {
                        suyo = new List<EncuestaHistorial>();
                        historial[d.Elegida.Cliente] = suyo;
                    }
                    suyo.Add(new EncuestaHistorial { Cliente = d.Elegida.Cliente, Dia = hoy, Tema = d.Elegida.Tema, ClaveTema = d.Elegida.ClaveTema, Ignorada = false });
                }
                todas.AddRange(decisiones);
            }
            return todas;
        }

        public SimulacionEncuestasDTO Simular(string empresa, DateTime desde, DateTime hasta, int? diasEntre)
        {
            string avisoParametro = null;
            int dias = diasEntre.HasValue ? GobernadorEncuestas.ConSuelo(diasEntre.Value) : DiasEntreDelParametro(out avisoParametro);
            DatosSimulacionEncuestas datos = Datos(empresa, desde, hasta);
            return SimularConDatos(datos, empresa, desde, hasta, dias, avisoParametro, ahora());
        }

        public ComparacionSimulacionEncuestasDTO Comparar(string empresa, DateTime desde, DateTime hasta, IReadOnlyList<int> intervalos)
        {
            DatosSimulacionEncuestas datos = Datos(empresa, desde, hasta);
            DateTime calculado = ahora();
            var resultado = new ComparacionSimulacionEncuestasDTO
            {
                Empresa = empresa,
                Desde = desde.Date,
                Hasta = hasta.Date,
                Candidatas = datos.Candidatas.Count,
                Aviso = Aviso(datos.Avisos),
                CalculadoEl = calculado
            };
            foreach (int intervalo in (intervalos ?? new List<int>()).Select(GobernadorEncuestas.ConSuelo).Distinct().OrderBy(i => i))
            {
                SimulacionEncuestasDTO simulacion = SimularConDatos(datos, empresa, desde, hasta, intervalo, null, calculado);
                resultado.Simulaciones.Add(simulacion);
                resultado.Comparacion.Add(Fila(simulacion));
            }
            return resultado;
        }

        internal static SimulacionEncuestasDTO SimularConDatos(DatosSimulacionEncuestas datos, string empresa, DateTime desde, DateTime hasta,
            int diasEntre, string avisoParametro, DateTime calculado)
        {
            List<DecisionEncuestaCliente> decisiones = SimularDias(datos.Candidatas, datos.Contexto, desde, hasta, diasEntre);
            var avisos = new List<string>(datos.Avisos);
            if (avisoParametro != null)
            {
                avisos.Insert(0, avisoParametro);
            }
            avisos.Add("Simulación: el canal, las bajas y los impagos son los de HOY (no hay foto de cómo estaban cada día); un impagado ya cobrado no se ve.");
            return Resumir(empresa, desde, hasta, GobernadorEncuestas.ConSuelo(diasEntre),
                datos.Candidatas,
                decisiones.Where(d => d.Elegida != null).Select(d => d.Elegida).ToList(),
                decisiones.SelectMany(d => d.Descartes).ToList(),
                null, avisos, calculado);
        }

        internal static FilaComparacionEncuestasDTO Fila(SimulacionEncuestasDTO s) => new FilaComparacionEncuestasDTO
        {
            DiasEntre = s.DiasEntre,
            Encuestas = s.Encuestas,
            MediaPorDia = s.MediaPorDia,
            MaximoPorDia = s.MaximoPorDia,
            ClientesEncuestados = s.ClientesEncuestados,
            ClientesCon1 = s.ClientesPorNumeroDeEncuestas.Where(c => c.Encuestas == "1").Sum(c => c.Clientes),
            ClientesCon2 = s.ClientesPorNumeroDeEncuestas.Where(c => c.Encuestas == "2").Sum(c => c.Clientes),
            ClientesCon3oMas = s.ClientesPorNumeroDeEncuestas.Where(c => c.Encuestas == "3 o más").Sum(c => c.Clientes),
            MaximoEncuestasMismoCliente = s.MaximoEncuestasMismoCliente,
            PerdidasPorTope = s.PerdidasPorTope,
            PorTema = s.PorTema
        };

        // ------------------------------------------------------------ sombra

        public SimulacionEncuestasDTO Sombra(string empresa, int dias)
        {
            DateTime hasta = ahora().Date;
            return SombraEntre(empresa, hasta.AddDays(-dias), hasta);
        }

        /// <summary>
        /// Lo guardado por el job en sombra con día en [<paramref name="desde"/>, <paramref name="hasta"/>) (el correo semanal de calidad pide la
        /// semana cerrada). Lanza <see cref="EncuestasSinTablasException"/> sin el script.
        /// </summary>
        public SimulacionEncuestasDTO SombraEntre(string empresa, DateTime desde, DateTime hasta)
        {
            ILectorEncuestas l = lector();
            if (!l.ExistenTablas())
            {
                throw new EncuestasSinTablasException();
            }
            desde = desde.Date;
            hasta = hasta.Date;
            LecturaSombraEncuestas sombra = l.LeerSombra(empresa, desde, hasta);
            var candidatas = sombra.Encuestas.Concat(sombra.Descartes.Select(d => d.Key)).ToList();
            var avisos = new List<string>();
            if (!SombraEncendida())
            {
                avisos.Add($"El job en sombra está apagado (ParametrosUsuario «(defecto)» {ParametrosEncuestas.SOMBRA} distinto de «1»).");
            }
            SimulacionEncuestasDTO resumen = Resumir(empresa, desde, hasta, sombra.UltimoIntervalo ?? DiasEntreDelParametro(out _), candidatas, sombra.Encuestas,
                sombra.Descartes, sombra.CandidatasPorDia, avisos, ahora());
            resumen.DiasProcesados = sombra.CandidatasPorDia?.Count ?? 0;
            return resumen;
        }

        // ------------------------------------------------------------ resumen (PURO)

        public static SimulacionEncuestasDTO Resumir(string empresa, DateTime desde, DateTime hasta, int diasEntre,
            IReadOnlyCollection<CandidataEncuesta> candidatas, IReadOnlyCollection<CandidataEncuesta> encuestas,
            IReadOnlyCollection<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>> descartes, IDictionary<DateTime, int> candidatasPorDia,
            IEnumerable<string> avisos, DateTime calculado)
        {
            candidatas = candidatas ?? new List<CandidataEncuesta>();
            encuestas = encuestas ?? new List<CandidataEncuesta>();
            descartes = descartes ?? new List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>>();
            int dias = Math.Max(0, (int)(hasta.Date - desde.Date).TotalDays);

            var porDia = new List<DiaEncuestasDTO>();
            for (DateTime d = desde.Date; d < hasta.Date; d = d.AddDays(1))
            {
                DateTime dia = d;
                porDia.Add(new DiaEncuestasDTO
                {
                    Dia = dia,
                    Candidatas = candidatasPorDia != null && candidatasPorDia.TryGetValue(dia, out int n) ? n : candidatas.Count(c => c.Dia.Date == dia),
                    Encuestas = encuestas.Count(e => e.Dia.Date == dia)
                });
            }
            DiaEncuestasDTO maximo = porDia.OrderByDescending(p => p.Encuestas).ThenBy(p => p.Dia).FirstOrDefault();

            List<IGrouping<string, CandidataEncuesta>> porCliente = encuestas.GroupBy(e => e.Cliente?.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
            List<KeyValuePair<CandidataEncuesta, MotivoDescarteEncuesta>> porTope = descartes.Where(d => d.Value == MotivoDescarteEncuesta.TopeEntreEncuestas).ToList();

            var resultado = new SimulacionEncuestasDTO
            {
                Empresa = empresa,
                Desde = desde.Date,
                Hasta = hasta.Date,
                DiasEntre = diasEntre,
                Dias = dias,
                Candidatas = candidatas.Count,
                Encuestas = encuestas.Count,
                MediaPorDia = dias == 0 ? 0 : Math.Round((decimal)encuestas.Count / dias, 1, MidpointRounding.AwayFromZero),
                MaximoPorDia = maximo?.Encuestas ?? 0,
                DiaDelMaximo = maximo != null && maximo.Encuestas > 0 ? maximo.Dia : (DateTime?)null,
                ClientesConCandidatas = candidatas.Select(c => c.Cliente?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ClientesEncuestados = porCliente.Count,
                MaximoEncuestasMismoCliente = porCliente.Select(g => g.Count()).DefaultIfEmpty(0).Max(),
                PerdidasPorTope = porTope.Count,
                PorDia = porDia,
                CalculadoEl = calculado
            };
            resultado.ClientesPorNumeroDeEncuestas = new List<ClientesPorEncuestasDTO>
            {
                new ClientesPorEncuestasDTO { Encuestas = "1", Clientes = porCliente.Count(g => g.Count() == 1) },
                new ClientesPorEncuestasDTO { Encuestas = "2", Clientes = porCliente.Count(g => g.Count() == 2) },
                new ClientesPorEncuestasDTO { Encuestas = "3 o más", Clientes = porCliente.Count(g => g.Count() >= 3) }
            };
            resultado.PorTema = Enum.GetValues(typeof(TemaEncuesta)).Cast<TemaEncuesta>().Select(t => new TemaEncuestasDTO
            {
                Tema = t.ToString(),
                Candidatas = candidatas.Count(c => c.Tema == t),
                Encuestas = encuestas.Count(e => e.Tema == t)
            }).ToList();
            resultado.PorCanal = new[] { CanalesEncuesta.PUSH, CanalesEncuesta.CORREO }.Select(c => new CanalEncuestasDTO
            {
                Canal = c,
                Encuestas = encuestas.Count(e => e.Canal == c)
            }).ToList();
            resultado.PorOrigen = candidatas.GroupBy(c => new { c.Origen, c.Tema }).OrderBy(g => (int)g.Key.Tema).ThenBy(g => g.Key.Origen, StringComparer.Ordinal)
                .Select(g => new OrigenEncuestasDTO
                {
                    Origen = g.Key.Origen,
                    Tema = g.Key.Tema.ToString(),
                    Candidatas = g.Count(),
                    Encuestas = encuestas.Count(e => e.Origen == g.Key.Origen && e.Tema == g.Key.Tema)
                }).ToList();
            resultado.Descartes = Enum.GetValues(typeof(MotivoDescarteEncuesta)).Cast<MotivoDescarteEncuesta>().Select(m => new DescarteEncuestasDTO
            {
                Motivo = m.ToString(),
                Candidatas = descartes.Count(d => d.Value == m)
            }).ToList();
            Dictionary<string, int> encuestasPorCliente = porCliente.ToDictionary(g => g.Key ?? string.Empty, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> candidatasPorCliente = candidatas.GroupBy(c => c.Cliente?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            resultado.MasDescartadosPorTope = porTope.GroupBy(d => d.Key.Cliente?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ClienteTopeEncuestasDTO
                {
                    Cliente = g.Key,
                    DescartadasPorTope = g.Count(),
                    Candidatas = candidatasPorCliente.TryGetValue(g.Key, out int c) ? c : g.Count(),
                    Encuestas = encuestasPorCliente.TryGetValue(g.Key, out int e) ? e : 0
                })
                .OrderByDescending(x => x.DescartadasPorTope).ThenByDescending(x => x.Candidatas).ThenBy(x => x.Cliente, StringComparer.Ordinal)
                .Take(TOP_CLIENTES_TOPE).ToList();
            resultado.Aviso = Aviso(avisos);
            return resultado;
        }

        private static string Aviso(IEnumerable<string> avisos)
        {
            List<string> lista = (avisos ?? Enumerable.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
            return lista.Count == 0 ? null : string.Join(" ", lista);
        }

        // ------------------------------------------------------------ job diario

        /// <summary>
        /// Lo que hace el job para un día: si está encendido, con las tablas y sin procesar, lee las candidatas de ESE día, decide con el
        /// historial guardado (también las encuestas en sombra, para que la simulación sea fiel) y guarda las decididas con Sombra = 1, los
        /// descartes y el día. No envía nada. Devuelve las encuestas guardadas (null si no ha hecho nada). Nunca lanza.
        /// </summary>
        internal int? ProcesarDia(string empresa, DateTime dia)
        {
            try
            {
                if (!SombraEncendida())
                {
                    return null;
                }
                ILectorEncuestas l = lector();
                if (!l.ExistenTablas())
                {
                    AvisarSinTablas();
                    return null;
                }
                dia = dia.Date;
                if (l.DiaProcesado(empresa, dia))
                {
                    return null;
                }
                int diasEntre = DiasEntreDelParametro(out string aviso);
                if (aviso != null)
                {
                    registrarError(new Exception($"{PREFIJO} {aviso}"));
                }
                DatosSimulacionEncuestas datos = Preparar(l, empresa, dia, dia.AddDays(1), ParametrosFuentes());
                Dictionary<string, List<EncuestaHistorial>> historial = l.LeerHistorial(empresa, dia.AddDays(-DIAS_HISTORIAL), dia)
                    .Where(h => h.Dia.Date < dia)
                    .GroupBy(h => h.Cliente, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                List<DecisionEncuestaCliente> decisiones = GobernadorEncuestas.DecidirDia(dia, datos.Candidatas, historial,
                    cliente => ReglaCanalEncuesta.Situacion(datos.Contexto, cliente, dia), diasEntre);
                l.GuardarDia(empresa, dia, datos.Candidatas.Count, GobernadorEncuestas.ConSuelo(diasEntre), decisiones);
                return decisiones.Count(d => d.Elegida != null);
            }
            catch (Exception ex)
            {
                registrarError(new Exception($"{PREFIJO} No se han podido decidir las encuestas en sombra del {dia:dd/MM/yyyy}: {ex.Message}", ex));
                return null;
            }
        }

        private static int avisadoSinTablas;

        private void AvisarSinTablas()
        {
            if (Interlocked.Exchange(ref avisadoSinTablas, 1) == 0)
            {
                registrarError(new Exception($"{PREFIJO} {MENSAJE_SIN_TABLAS} El job en sombra no hace nada. Este aviso sale una sola vez."));
            }
        }

        /// <summary>Para los tests.</summary>
        internal static void ReiniciarAvisos() => Interlocked.Exchange(ref avisadoSinTablas, 0);
    }

    /// <summary>
    /// NestoAPI#914 (encuestas, E1): el job diario EN SOMBRA (todos los días a las 4:45, fuera de horario, cola «indicadores»: la de los jobs
    /// largos de un hilo). Decide las encuestas de AYER y las guarda con Sombra = 1. Interruptor ParametrosUsuario «(defecto)»
    /// Calidad.EncuestasSombra (solo «1»; nace apagado). No envía nada.
    /// </summary>
    public static class EncuestasSombraJobsService
    {
        public const string ID_JOB = "calidad-encuestas-sombra";
        public const string CRON = "45 4 * * *";

        [Queue(IndicadoresStock.IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static void Procesar()
        {
            DateTime hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Startup.ZonaHorariaPeninsular()).Date;
            _ = new ServicioEncuestas().ProcesarDia(Constantes.Empresas.EMPRESA_POR_DEFECTO, hoy.AddDays(-1));
        }
    }
}
