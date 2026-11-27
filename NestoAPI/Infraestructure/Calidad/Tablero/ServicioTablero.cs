using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Infraestructure.Calidad.Tablero
{
    public class NotaSemanaDTO
    {
        public DateTime Semana { get; set; }
        public decimal? Valor { get; set; }
        /// <summary>POOR, FAIR, GOOD, FANTASTIC o null (sin umbrales aprobados: «sin nota»).</summary>
        public string Nota { get; set; }
    }

    public class SemanaKpiDTO
    {
        /// <summary>El lunes.</summary>
        public DateTime Semana { get; set; }
        public decimal? Valor { get; set; }
        public decimal? Numerador { get; set; }
        public decimal? Denominador { get; set; }
        public string Detalle { get; set; }
        public string Nota { get; set; }
        public DateTime CalculadoEl { get; set; }
    }

    public class ValorEnCursoDTO
    {
        public DateTime Semana { get; set; }
        /// <summary>Siempre true: la semana no ha acabado y el valor puede cambiar.</summary>
        public bool EnCurso { get; set; } = true;
        /// <summary>Hasta qué día (incluido) se ha contado.</summary>
        public DateTime HastaDia { get; set; }
        public decimal? Valor { get; set; }
        public decimal? Numerador { get; set; }
        public decimal? Denominador { get; set; }
        public string Detalle { get; set; }
        /// <summary>La nota que tendría con lo que va de semana (no es definitiva).</summary>
        public string NotaProvisional { get; set; }
        public DateTime CalculadoEl { get; set; }
    }

    public class UmbralesDTO
    {
        public decimal CortePoorFair { get; set; }
        public decimal CorteFairGood { get; set; }
        public decimal CorteGoodFantastic { get; set; }
        public DateTime VigenteDesde { get; set; }
        public string Usuario { get; set; }

        internal static UmbralesDTO De(UmbralesKpi u) => u == null ? null : new UmbralesDTO
        {
            CortePoorFair = u.CortePoorFair,
            CorteFairGood = u.CorteFairGood,
            CorteGoodFantastic = u.CorteGoodFantastic,
            VigenteDesde = u.VigenteDesde,
            Usuario = u.Usuario
        };
    }

    /// <summary>Un KPI de GET api/Calidad/Tablero/Mios.</summary>
    public class KpiMioDTO
    {
        public string Kpi { get; set; }
        public string Nombre { get; set; }
        public string Descripcion { get; set; }
        public string TipoDueno { get; set; }
        public string Dueno { get; set; }
        public string Unidad { get; set; }
        public string Sentido { get; set; }
        /// <summary>False = todavía no hay de dónde sacarlo (<see cref="Aviso"/> dice por qué).</summary>
        public bool Disponible { get; set; }
        public int DiasMaduracion { get; set; }
        /// <summary>La semana actual (null si el KPI madura, si no está disponible o si no se ha podido calcular).</summary>
        public ValorEnCursoDTO EnCurso { get; set; }
        /// <summary>Con maduración: cuándo se podrá cerrar la semana actual.</summary>
        public DateTime? SemanaActualDisponibleEl { get; set; }
        public SemanaKpiDTO UltimaSemana { get; set; }
        /// <summary>Sube, Baja o Igual: la última semana cerrada frente a la media de las 4 anteriores (null sin anteriores).</summary>
        public string Tendencia { get; set; }
        /// <summary>True si la tendencia es a mejor (según el sentido), false a peor, null igual o sin tendencia.</summary>
        public bool? Mejora { get; set; }
        /// <summary>Las últimas 8 semanas cerradas, de la más antigua a la más nueva (para la tira).</summary>
        public List<NotaSemanaDTO> Ultimas { get; set; } = new List<NotaSemanaDTO>();
        public bool TieneUmbrales { get; set; }
        public string Aviso { get; set; }
    }

    public class EvolucionKpiDTO
    {
        public string Kpi { get; set; }
        public string Nombre { get; set; }
        public string Unidad { get; set; }
        public string Sentido { get; set; }
        public string TipoDueno { get; set; }
        public string Dueno { get; set; }
        public int DiasMaduracion { get; set; }
        public string PorQueMadura { get; set; }
        public bool Disponible { get; set; }
        /// <summary>De la más nueva a la más antigua.</summary>
        public List<SemanaKpiDTO> Semanas { get; set; } = new List<SemanaKpiDTO>();
        public ValorEnCursoDTO EnCurso { get; set; }
        public DateTime? SemanaActualDisponibleEl { get; set; }
        public UmbralesDTO Umbrales { get; set; }
        public string Aviso { get; set; }
    }

    public class ValorTableroDTO
    {
        public string TipoDueno { get; set; }
        public string Dueno { get; set; }
        public decimal? Valor { get; set; }
        public decimal? Numerador { get; set; }
        public decimal? Denominador { get; set; }
        public string Detalle { get; set; }
        /// <summary>Semana cerrada: su nota. En curso: null (ver <see cref="NotaProvisional"/>).</summary>
        public string Nota { get; set; }
        public string NotaProvisional { get; set; }
    }

    public class KpiTableroDTO
    {
        public string Kpi { get; set; }
        public string Nombre { get; set; }
        public string Unidad { get; set; }
        public string Sentido { get; set; }
        public bool Disponible { get; set; }
        /// <summary>La semana está cerrada para este KPI (si no, <see cref="DisponibleEl"/>).</summary>
        public bool Cerrada { get; set; }
        public bool EnCurso { get; set; }
        public DateTime? DisponibleEl { get; set; }
        public List<ValorTableroDTO> Valores { get; set; } = new List<ValorTableroDTO>();
        public string Aviso { get; set; }
    }

    public class TableroSemanaDTO
    {
        public DateTime Semana { get; set; }
        public List<KpiTableroDTO> Kpis { get; set; } = new List<KpiTableroDTO>();
    }

    public class DistribucionNotasDTO
    {
        public int POOR { get; set; }
        public int FAIR { get; set; }
        public int GOOD { get; set; }
        public int FANTASTIC { get; set; }
    }

    public class PropuestaTipoDTO
    {
        public string TipoDueno { get; set; }
        /// <summary>Semanas-dueño guardadas con valor.</summary>
        public int SemanasDueno { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public PropuestaUmbralesKpi Propuesta { get; set; }
        /// <summary>Cuántas semanas-dueño caerían en cada nota con la propuesta.</summary>
        public DistribucionNotasDTO Distribucion { get; set; }
        public UmbralesDTO Vigentes { get; set; }
        /// <summary>Y con los vigentes (null si no hay).</summary>
        public DistribucionNotasDTO DistribucionVigentes { get; set; }
        public string Aviso { get; set; }
    }

    public class PropuestaUmbralesDTO
    {
        public string Kpi { get; set; }
        public string Nombre { get; set; }
        public string Unidad { get; set; }
        public string Sentido { get; set; }
        public List<PropuestaTipoDTO> Tipos { get; set; } = new List<PropuestaTipoDTO>();
    }

    /// <summary>Cuerpo de PUT api/Calidad/Tablero/Umbrales.</summary>
    public class AprobarUmbralesDTO
    {
        public string Kpi { get; set; }
        public string TipoDueno { get; set; }
        public decimal? CortePoorFair { get; set; }
        public decimal? CorteFairGood { get; set; }
        public decimal? CorteGoodFantastic { get; set; }
        /// <summary>Opcional. Sin él: el primer umbral de un KPI vale desde siempre; un cambio, desde el lunes de esta semana.</summary>
        public DateTime? VigenteDesde { get; set; }
    }

    public class ResultadoCalculoTablero
    {
        public int SemanasCalculadas { get; set; }
        public int Fallos { get; set; }
        public List<string> Detalle { get; set; } = new List<string>();
    }

    /// <summary>Un error de usted para el controlador (400).</summary>
    public class ErrorTableroException : Exception
    {
        public ErrorTableroException(string mensaje) : base(mensaje)
        {
        }
    }

    /// <summary>
    /// NestoAPI#914, el tablero de KPI con nota semanal. Las llamadas de usuario solo leen las tablas y el valor en curso (cacheado
    /// <see cref="DURACION_CACHE_EN_CURSO"/> por KPI); los cálculos de semanas pasadas, solo en el job o en el recalcular encolado.
    /// </summary>
    public class ServicioTablero
    {
        public static readonly TimeSpan DURACION_CACHE_EN_CURSO = TimeSpan.FromMinutes(15);
        /// <summary>Tira de notas de «Mis indicadores».</summary>
        public const int SEMANAS_TIRA = 8;
        /// <summary>El job recalcula siempre las últimas N semanas cerradas (por si llegan datos tardíos: una resolución, un impagado…).</summary>
        public const int SEMANAS_RECALCULAR_JOB = 8;
        /// <summary>Y mira hasta estas semanas atrás por si falta alguna sin guardar.</summary>
        public const int SEMANAS_BUSCAR_JOB = 12;
        public const int SEMANAS_MAXIMAS = 104;

        private readonly IRepositorioTablero repositorio;
        private readonly Func<IFuentesKpi> fuentes;
        private readonly Func<DateTime> ahora;
        private readonly ObjectCache cache;
        private readonly IReadOnlyList<IKpiTablero> catalogo;
        private readonly Action<Exception> registrar;

        public ServicioTablero(IRepositorioTablero repositorio, Func<IFuentesKpi> fuentes, Func<DateTime> ahora = null, ObjectCache cache = null,
            IReadOnlyList<IKpiTablero> catalogo = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.fuentes = fuentes ?? throw new ArgumentNullException(nameof(fuentes));
            this.ahora = ahora ?? FuentesKpiSql.AhoraMadrid;
            this.cache = cache ?? MemoryCache.Default;
            this.catalogo = catalogo ?? CatalogoKpiTablero.Todos;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public IReadOnlyList<IKpiTablero> Catalogo => catalogo;

        public IKpiTablero Buscar(string clave) =>
            catalogo.FirstOrDefault(k => string.Equals(k.Clave, clave?.Trim(), StringComparison.OrdinalIgnoreCase));

        private DateTime Hoy => ahora().Date;

        private DateTime LunesActual => SemanaKpi.LunesDe(Hoy);

        // ---------------------------------------------------------------- Cálculo (job y recalcular)

        /// <summary>
        /// Calcula y guarda las semanas cerradas: de las últimas <paramref name="semanasBuscar"/> de cada KPI, las que no están guardadas, y siempre
        /// las <paramref name="semanasRecalcular"/> más recientes. Idempotente (cada semana se sustituye entera). Un fallo en una semana no para el resto.
        /// </summary>
        public ResultadoCalculoTablero CalcularSemanas(int semanasBuscar, int semanasRecalcular)
        {
            var resultado = new ResultadoCalculoTablero();
            DateTime hoy = Hoy;
            IFuentesKpi lector = fuentes();
            foreach (IKpiTablero kpi in catalogo.Where(k => k.TieneFuente))
            {
                DateTime ultima = SemanaKpi.UltimaCerrada(kpi.DiasMaduracion, hoy);
                DateTime primera = ultima.AddDays(-7 * (Math.Max(1, semanasBuscar) - 1));
                HashSet<DateTime> guardadas;
                try
                {
                    guardadas = repositorio.SemanasGuardadas(kpi.Clave, primera);
                }
                catch (Exception ex)
                {
                    resultado.Fallos++;
                    registrar(new Exception($"[Tablero #914] No se han podido leer las semanas guardadas de {kpi.Clave}: {ex.Message}", ex));
                    continue;
                }
                for (int i = Math.Max(1, semanasBuscar) - 1; i >= 0; i--)
                {
                    DateTime lunes = ultima.AddDays(-7 * i);
                    if (i >= semanasRecalcular && guardadas.Contains(lunes))
                    {
                        continue;
                    }
                    try
                    {
                        List<ValorKpi> valores = kpi.Calcular(new PeriodoKpi(lunes), lector) ?? new List<ValorKpi>();
                        repositorio.GuardarSemana(kpi.Clave, lunes, valores, ahora());
                        resultado.SemanasCalculadas++;
                        resultado.Detalle.Add($"{kpi.Clave} {lunes:dd/MM/yyyy}: {valores.Count} dueños");
                    }
                    catch (Exception ex)
                    {
                        resultado.Fallos++;
                        resultado.Detalle.Add($"{kpi.Clave} {lunes:dd/MM/yyyy}: FALLO {ex.Message}");
                        registrar(new Exception($"[Tablero #914] No se ha podido calcular {kpi.Clave} de la semana del {lunes:dd/MM/yyyy}: {ex.Message}", ex));
                    }
                }
            }
            return resultado;
        }

        // ---------------------------------------------------------------- Valor en curso

        /// <summary>Lo que va de semana de un KPI que no madura (cacheado 15 min). Null si madura, no tiene fuente o no se ha podido calcular.</summary>
        public ValoresEnCurso EnCurso(IKpiTablero kpi)
        {
            if (kpi == null || !kpi.TieneFuente || kpi.DiasMaduracion > 0)
            {
                return null;
            }
            DateTime lunes = LunesActual;
            string clave = $"CalidadTablero|EnCurso|{kpi.Clave}|{lunes:yyyyMMdd}";
            var nuevo = new Lazy<ValoresEnCurso>(() =>
            {
                var periodo = new PeriodoKpi(lunes, Hoy.AddDays(1));
                return new ValoresEnCurso
                {
                    Periodo = periodo,
                    Valores = kpi.Calcular(periodo, FuentesDeLaSemana(lunes)) ?? new List<ValorKpi>(),
                    CalculadoEl = ahora()
                };
            });
            var usar = (cache.AddOrGetExisting(clave, nuevo, DateTimeOffset.UtcNow.Add(DURACION_CACHE_EN_CURSO)) as Lazy<ValoresEnCurso>) ?? nuevo;
            try
            {
                return usar.Value;
            }
            catch (Exception ex)
            {
                _ = cache.Remove(clave);
                registrar(new Exception($"[Tablero #914] No se ha podido calcular el valor en curso de {kpi.Clave}: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>Unas fuentes compartidas por los KPI en curso de la semana durante 15 min (pérdida desconocida e inventario leen la misma consulta).</summary>
        private IFuentesKpi FuentesDeLaSemana(DateTime lunes)
        {
            string clave = $"CalidadTablero|Fuentes|{lunes:yyyyMMdd}";
            var nuevo = new Lazy<IFuentesKpi>(fuentes);
            var usar = (cache.AddOrGetExisting(clave, nuevo, DateTimeOffset.UtcNow.Add(DURACION_CACHE_EN_CURSO)) as Lazy<IFuentesKpi>) ?? nuevo;
            return usar.Value;
        }

        public class ValoresEnCurso
        {
            public PeriodoKpi Periodo { get; set; }
            public List<ValorKpi> Valores { get; set; } = new List<ValorKpi>();
            public DateTime CalculadoEl { get; set; }
        }

        private ValorEnCursoDTO EnCursoDe(IKpiTablero kpi, ValoresEnCurso enCurso, TipoDuenoKpi tipo, string dueno, List<UmbralesKpi> umbrales)
        {
            if (enCurso == null)
            {
                return null;
            }
            ValorKpi v = enCurso.Valores.FirstOrDefault(x => x.TipoDueno == tipo && string.Equals(x.Dueno?.Trim(), dueno, StringComparison.OrdinalIgnoreCase));
            return new ValorEnCursoDTO
            {
                Semana = enCurso.Periodo.Lunes,
                HastaDia = enCurso.Periodo.UltimoDia,
                Valor = v?.Valor,
                Numerador = v?.Numerador,
                Denominador = v?.Denominador,
                Detalle = v?.Detalle,
                NotaProvisional = ReglaNotaKpi.Nota(v?.Valor, ReglaNotaKpi.Vigente(umbrales, kpi.Clave, tipo, enCurso.Periodo.Lunes), kpi.Sentido)?.ToString(),
                CalculadoEl = enCurso.CalculadoEl
            };
        }

        // ---------------------------------------------------------------- Lectura

        /// <summary>GET Mios: los KPI de quien llama (nunca los de otro). Lista vacía si no tiene ninguno.</summary>
        public List<KpiMioDTO> Mios(IdentidadTablero identidad)
        {
            var resultado = new List<KpiMioDTO>();
            if (identidad == null)
            {
                return resultado;
            }
            var suyos = catalogo.SelectMany(k => identidad.Duenos(k).Select(d => new { Kpi = k, Dueno = d })).ToList();
            if (suyos.Count == 0)
            {
                return resultado;
            }
            DateTime lunesActual = LunesActual;
            DateTime desde = lunesActual.AddDays(-7 * (SEMANAS_TIRA + ReglaNotaKpi.SEMANAS_TENDENCIA + 4));
            List<SemanaKpiGuardada> guardadas = repositorio.LeerSemanas(null, null, null, desde, lunesActual);
            List<UmbralesKpi> umbrales = repositorio.LeerUmbrales();
            var enCursoPorKpi = new Dictionary<string, ValoresEnCurso>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in suyos)
            {
                IKpiTablero kpi = s.Kpi;
                if (!enCursoPorKpi.ContainsKey(kpi.Clave))
                {
                    enCursoPorKpi[kpi.Clave] = EnCurso(kpi);
                }
                List<SemanaKpiGuardada> propias = guardadas
                    .Where(g => string.Equals(g.Kpi, kpi.Clave, StringComparison.OrdinalIgnoreCase) && s.Dueno.Es(g.TipoDueno, g.Dueno))
                    .OrderByDescending(g => g.Semana)
                    .ToList();
                var mio = new KpiMioDTO
                {
                    Kpi = kpi.Clave,
                    Nombre = kpi.Nombre,
                    Descripcion = kpi.Descripcion,
                    TipoDueno = s.Dueno.Tipo.ToString(),
                    Dueno = s.Dueno.Dueno,
                    Unidad = kpi.Unidad.ToString(),
                    Sentido = kpi.Sentido.ToString(),
                    Disponible = kpi.TieneFuente,
                    DiasMaduracion = kpi.DiasMaduracion,
                    TieneUmbrales = umbrales.Any(u => string.Equals(u.Kpi, kpi.Clave, StringComparison.OrdinalIgnoreCase) && u.TipoDueno == s.Dueno.Tipo)
                };
                if (!kpi.TieneFuente)
                {
                    mio.Aviso = "Todavía no disponible: este indicador aún no tiene de dónde sacar los datos.";
                    resultado.Add(mio);
                    continue;
                }
                if (kpi.DiasMaduracion > 0)
                {
                    mio.SemanaActualDisponibleEl = SemanaKpi.CerradaEl(lunesActual, kpi.DiasMaduracion);
                    mio.Aviso = $"Esta semana no tiene valor todavía: {kpi.PorQueMadura} Estará el {mio.SemanaActualDisponibleEl:dd/MM/yyyy}.";
                }
                else
                {
                    mio.EnCurso = EnCursoDe(kpi, enCursoPorKpi[kpi.Clave], s.Dueno.Tipo, s.Dueno.Dueno, umbrales);
                }
                if (propias.Count > 0)
                {
                    SemanaKpiGuardada ultima = propias[0];
                    mio.UltimaSemana = Semana(kpi, ultima, umbrales);
                    TendenciaKpi? tendencia = ReglaNotaKpi.Tendencia(ultima.Valor, propias.Skip(1).Select(p => p.Valor));
                    mio.Tendencia = tendencia?.ToString();
                    mio.Mejora = ReglaNotaKpi.Mejora(tendencia, kpi.Sentido);
                }
                mio.Ultimas = propias.Take(SEMANAS_TIRA).OrderBy(p => p.Semana)
                    .Select(p => new NotaSemanaDTO { Semana = p.Semana, Valor = p.Valor, Nota = NotaDe(kpi, p, umbrales) })
                    .ToList();
                resultado.Add(mio);
            }
            return resultado;
        }

        /// <summary>GET {kpi}/Semanas: la evolución de un dueño (el permiso lo mira el controlador).</summary>
        public EvolucionKpiDTO Evolucion(IKpiTablero kpi, TipoDuenoKpi tipo, string dueno, int semanas)
        {
            semanas = Math.Max(1, Math.Min(SEMANAS_MAXIMAS, semanas));
            DateTime lunesActual = LunesActual;
            List<UmbralesKpi> umbrales = repositorio.LeerUmbrales(kpi.Clave);
            var evolucion = new EvolucionKpiDTO
            {
                Kpi = kpi.Clave,
                Nombre = kpi.Nombre,
                Unidad = kpi.Unidad.ToString(),
                Sentido = kpi.Sentido.ToString(),
                TipoDueno = tipo.ToString(),
                Dueno = dueno?.Trim().ToUpperInvariant(),
                DiasMaduracion = kpi.DiasMaduracion,
                PorQueMadura = kpi.PorQueMadura,
                Disponible = kpi.TieneFuente,
                Umbrales = UmbralesDTO.De(ReglaNotaKpi.Vigente(umbrales, kpi.Clave, tipo, lunesActual))
            };
            if (!kpi.TieneFuente)
            {
                evolucion.Aviso = "Todavía no disponible: este indicador aún no tiene de dónde sacar los datos.";
                return evolucion;
            }
            if (string.IsNullOrWhiteSpace(dueno))
            {
                evolucion.Aviso = "Usted no tiene este indicador.";
                return evolucion;
            }
            evolucion.Semanas = repositorio.LeerSemanas(kpi.Clave, tipo, dueno.Trim(), lunesActual.AddDays(-7 * semanas), lunesActual)
                .OrderByDescending(s => s.Semana)
                .Select(s => Semana(kpi, s, umbrales))
                .ToList();
            if (kpi.DiasMaduracion > 0)
            {
                evolucion.SemanaActualDisponibleEl = SemanaKpi.CerradaEl(lunesActual, kpi.DiasMaduracion);
            }
            else
            {
                evolucion.EnCurso = EnCursoDe(kpi, EnCurso(kpi), tipo, dueno.Trim(), umbrales);
            }
            return evolucion;
        }

        /// <summary>GET api/Calidad/Tablero?semana= (Dirección/Informática): todos los KPI y dueños de la semana, con nota.</summary>
        public TableroSemanaDTO Tablero(DateTime semana)
        {
            DateTime lunes = SemanaKpi.LunesDe(semana);
            DateTime lunesActual = LunesActual;
            List<SemanaKpiGuardada> guardadas = repositorio.LeerSemanas(null, null, null, lunes, lunes.AddDays(7));
            List<UmbralesKpi> umbrales = repositorio.LeerUmbrales();
            var tablero = new TableroSemanaDTO { Semana = lunes };
            foreach (IKpiTablero kpi in catalogo)
            {
                var k = new KpiTableroDTO
                {
                    Kpi = kpi.Clave,
                    Nombre = kpi.Nombre,
                    Unidad = kpi.Unidad.ToString(),
                    Sentido = kpi.Sentido.ToString(),
                    Disponible = kpi.TieneFuente,
                    Cerrada = SemanaKpi.EstaCerrada(lunes, kpi.DiasMaduracion, Hoy)
                };
                tablero.Kpis.Add(k);
                if (!kpi.TieneFuente)
                {
                    k.Aviso = "Todavía no disponible.";
                    continue;
                }
                if (!k.Cerrada)
                {
                    k.DisponibleEl = SemanaKpi.CerradaEl(lunes, kpi.DiasMaduracion);
                }
                if (lunes == lunesActual && kpi.DiasMaduracion == 0)
                {
                    ValoresEnCurso enCurso = EnCurso(kpi);
                    k.EnCurso = true;
                    if (enCurso == null)
                    {
                        k.Aviso = "No se ha podido calcular lo que va de semana. Inténtelo de nuevo en unos minutos.";
                        continue;
                    }
                    k.Valores = enCurso.Valores.Select(v => new ValorTableroDTO
                    {
                        TipoDueno = v.TipoDueno.ToString(),
                        Dueno = v.Dueno,
                        Valor = v.Valor,
                        Numerador = v.Numerador,
                        Denominador = v.Denominador,
                        Detalle = v.Detalle,
                        NotaProvisional = ReglaNotaKpi.Nota(v.Valor, ReglaNotaKpi.Vigente(umbrales, kpi.Clave, v.TipoDueno, lunes), kpi.Sentido)?.ToString()
                    }).ToList();
                    continue;
                }
                if (!k.Cerrada)
                {
                    k.Aviso = $"Esta semana aún no está cerrada para este indicador: {kpi.PorQueMadura}";
                    continue;
                }
                k.Valores = guardadas.Where(g => string.Equals(g.Kpi, kpi.Clave, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(g => g.TipoDueno).ThenBy(g => g.Dueno, StringComparer.Ordinal)
                    .Select(g => new ValorTableroDTO
                    {
                        TipoDueno = g.TipoDueno.ToString(),
                        Dueno = g.Dueno,
                        Valor = g.Valor,
                        Numerador = g.Numerador,
                        Denominador = g.Denominador,
                        Detalle = g.Detalle,
                        Nota = NotaDe(kpi, g, umbrales)
                    }).ToList();
                if (k.Valores.Count == 0)
                {
                    k.Aviso = "Esta semana no está calculada (el job la calcula los lunes de madrugada; para el histórico, POST api/Calidad/Tablero/Recalcular).";
                }
            }
            return tablero;
        }

        /// <summary>GET Umbrales/Propuesta: los cortes propuestos con las semanas guardadas y cuántas semanas-dueño caerían en cada nota.</summary>
        public PropuestaUmbralesDTO Propuesta(IKpiTablero kpi, TipoDuenoKpi? soloTipo)
        {
            List<SemanaKpiGuardada> guardadas = repositorio.LeerSemanas(kpi.Clave, soloTipo, null, ReglaNotaKpi.DESDE_SIEMPRE, LunesActual);
            List<UmbralesKpi> umbrales = repositorio.LeerUmbrales(kpi.Clave);
            var dto = new PropuestaUmbralesDTO { Kpi = kpi.Clave, Nombre = kpi.Nombre, Unidad = kpi.Unidad.ToString(), Sentido = kpi.Sentido.ToString() };
            foreach (TipoDuenoKpi tipo in kpi.TiposDueno.Where(t => !soloTipo.HasValue || t == soloTipo.Value))
            {
                List<SemanaKpiGuardada> delTipo = guardadas.Where(g => g.TipoDueno == tipo && g.Valor.HasValue).ToList();
                PropuestaUmbralesKpi propuesta = ReglaNotaKpi.Proponer(delTipo.Select(g => g.Valor.Value), kpi.Sentido);
                UmbralesKpi vigente = ReglaNotaKpi.Vigente(umbrales, kpi.Clave, tipo, LunesActual);
                dto.Tipos.Add(new PropuestaTipoDTO
                {
                    TipoDueno = tipo.ToString(),
                    SemanasDueno = delTipo.Count,
                    Desde = delTipo.Count == 0 ? (DateTime?)null : delTipo.Min(g => g.Semana),
                    Hasta = delTipo.Count == 0 ? (DateTime?)null : delTipo.Max(g => g.Semana),
                    Propuesta = propuesta,
                    Distribucion = propuesta == null ? null : Distribucion(delTipo, new UmbralesKpi
                    {
                        CortePoorFair = propuesta.CortePoorFair,
                        CorteFairGood = propuesta.CorteFairGood,
                        CorteGoodFantastic = propuesta.CorteGoodFantastic
                    }, kpi.Sentido),
                    Vigentes = UmbralesDTO.De(vigente),
                    DistribucionVigentes = vigente == null ? null : Distribucion(delTipo, vigente, kpi.Sentido),
                    Aviso = propuesta == null
                        ? $"Hacen falta al menos {ReglaNotaKpi.MINIMO_PARA_PROPONER} semanas-dueño guardadas para proponer los cortes (hay {delTipo.Count})."
                        : null
                });
            }
            return dto;
        }

        /// <summary>PUT Umbrales: valida y guarda (con rastro). Lanza <see cref="ErrorTableroException"/> con el error de usted.</summary>
        public UmbralesKpi Aprobar(AprobarUmbralesDTO peticion, string usuario)
        {
            if (peticion == null)
            {
                throw new ErrorTableroException("Falta el cuerpo con el indicador, el tipo de dueño y los tres cortes.");
            }
            IKpiTablero kpi = Buscar(peticion.Kpi);
            if (kpi == null)
            {
                throw new ErrorTableroException($"No existe el indicador «{peticion.Kpi}». Son: {string.Join(", ", catalogo.Select(k => k.Clave))}.");
            }
            if (!Enum.TryParse(peticion.TipoDueno?.Trim(), true, out TipoDuenoKpi tipo) || !kpi.TiposDueno.Contains(tipo))
            {
                throw new ErrorTableroException($"El tipo de dueño de «{kpi.Clave}» tiene que ser {string.Join(" o ", kpi.TiposDueno)}.");
            }
            if (!peticion.CortePoorFair.HasValue || !peticion.CorteFairGood.HasValue || !peticion.CorteGoodFantastic.HasValue)
            {
                throw new ErrorTableroException("Faltan cortes: hay que mandar los tres (CortePoorFair, CorteFairGood y CorteGoodFantastic).");
            }
            string error = ReglaNotaKpi.ValidarCortes(peticion.CortePoorFair.Value, peticion.CorteFairGood.Value, peticion.CorteGoodFantastic.Value, kpi.Sentido);
            if (error != null)
            {
                throw new ErrorTableroException(error);
            }
            List<UmbralesKpi> existentes = repositorio.LeerUmbrales(kpi.Clave).Where(u => u.TipoDueno == tipo).ToList();
            DateTime lunesActual = LunesActual;
            DateTime vigenteDesde;
            if (peticion.VigenteDesde.HasValue)
            {
                vigenteDesde = SemanaKpi.LunesDe(peticion.VigenteDesde.Value);
                if (existentes.Count > 0 && vigenteDesde < lunesActual)
                {
                    throw new ErrorTableroException($"Ya hay umbrales aprobados para este indicador: uno nuevo vale desde esta semana (lunes {lunesActual:dd/MM/yyyy}) " +
                        "en adelante, para no cambiar las notas de las semanas pasadas.");
                }
            }
            else
            {
                vigenteDesde = existentes.Count == 0 ? ReglaNotaKpi.DESDE_SIEMPRE : lunesActual;
            }
            UmbralesKpi anterior = ReglaNotaKpi.Vigente(existentes, kpi.Clave, tipo, vigenteDesde);
            var nuevo = new UmbralesKpi
            {
                Kpi = kpi.Clave,
                TipoDueno = tipo,
                CortePoorFair = peticion.CortePoorFair.Value,
                CorteFairGood = peticion.CorteFairGood.Value,
                CorteGoodFantastic = peticion.CorteGoodFantastic.Value,
                VigenteDesde = vigenteDesde,
                Usuario = usuario,
                FechaModificacion = ahora()
            };
            repositorio.AprobarUmbrales(nuevo, anterior == null ? null : Rastro(anterior), Rastro(nuevo));
            return nuevo;
        }

        private static string Rastro(UmbralesKpi u) => JsonConvert.SerializeObject(new
        {
            u.Kpi,
            TipoDueno = u.TipoDueno.ToString(),
            u.CortePoorFair,
            u.CorteFairGood,
            u.CorteGoodFantastic,
            VigenteDesde = u.VigenteDesde.ToString("yyyy-MM-dd")
        });

        private static DistribucionNotasDTO Distribucion(IEnumerable<SemanaKpiGuardada> semanas, UmbralesKpi umbrales, SentidoKpi sentido)
        {
            var d = new DistribucionNotasDTO();
            foreach (SemanaKpiGuardada s in semanas)
            {
                switch (ReglaNotaKpi.Nota(s.Valor, umbrales, sentido))
                {
                    case NotaKpi.POOR: d.POOR++; break;
                    case NotaKpi.FAIR: d.FAIR++; break;
                    case NotaKpi.GOOD: d.GOOD++; break;
                    case NotaKpi.FANTASTIC: d.FANTASTIC++; break;
                }
            }
            return d;
        }

        private static string NotaDe(IKpiTablero kpi, SemanaKpiGuardada s, List<UmbralesKpi> umbrales) =>
            ReglaNotaKpi.Nota(s.Valor, ReglaNotaKpi.Vigente(umbrales, kpi.Clave, s.TipoDueno, s.Semana), kpi.Sentido)?.ToString();

        private static SemanaKpiDTO Semana(IKpiTablero kpi, SemanaKpiGuardada s, List<UmbralesKpi> umbrales) => new SemanaKpiDTO
        {
            Semana = s.Semana,
            Valor = s.Valor,
            Numerador = s.Numerador,
            Denominador = s.Denominador,
            Detalle = s.Detalle,
            Nota = NotaDe(kpi, s, umbrales),
            CalculadoEl = s.CalculadoEl
        };
    }
}
