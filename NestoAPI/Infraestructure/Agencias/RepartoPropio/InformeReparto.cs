using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    // ==================================================================================================== DTO

    /// <summary>NestoAPI#881 (corte 4): GET api/RepartoPropio/Informe. Por repartidor y día, sus totales y medias por día de la semana, y por código postal.</summary>
    public class InformeRepartoDTO
    {
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public DateTime Generado { get; set; }
        /// <summary>Lo que no se ha podido leer (tablas opcionales sin crear); null si nada.</summary>
        public string Aviso { get; set; }
        /// <summary>Uno por repartidor (por nombre), con sus días.</summary>
        public List<InformeRepartidorDTO> Repartidores { get; set; } = new List<InformeRepartidorDTO>();
        /// <summary>De todos (página final de totales).</summary>
        public TotalesInformeRepartoDTO Totales { get; set; } = new TotalesInformeRepartoDTO();
        /// <summary>De todos: medias por día de la semana (lunes a domingo, solo los que tienen jornadas).</summary>
        public List<DiaSemanaInformeRepartoDTO> PorDiaSemana { get; set; } = new List<DiaSemanaInformeRepartoDTO>();
        /// <summary>De todos: por código postal y día de la semana (para equilibrar las rutas).</summary>
        public List<CodigoPostalInformeRepartoDTO> PorCodigoPostal { get; set; } = new List<CodigoPostalInformeRepartoDTO>();
    }

    public class InformeRepartidorDTO
    {
        /// <summary>Sin dominio.</summary>
        public string Repartidor { get; set; }
        public List<JornadaInformeRepartoDTO> Dias { get; set; } = new List<JornadaInformeRepartoDTO>();
        public TotalesInformeRepartoDTO Totales { get; set; } = new TotalesInformeRepartoDTO();
        public List<DiaSemanaInformeRepartoDTO> PorDiaSemana { get; set; } = new List<DiaSemanaInformeRepartoDTO>();
    }

    /// <summary>Un repartidor un día.</summary>
    public class JornadaInformeRepartoDTO
    {
        public string Repartidor { get; set; }
        public DateTime Dia { get; set; }
        /// <summary>1 = lunes … 7 = domingo.</summary>
        public int DiaSemana { get; set; }
        /// <summary>«lunes».</summary>
        public string NombreDiaSemana { get; set; }

        /// <summary>La primera carga de un bulto en su furgoneta (lectura del repartidor); null si no leyó ninguna.</summary>
        public DateTime? PrimeraCarga { get; set; }
        /// <summary>El último movimiento suyo del día: bultos (cargar, recoger, entregar, descargar), paradas cerradas o «Terminar jornada».</summary>
        public DateTime? UltimoMovimiento { get; set; }
        /// <summary>«Terminar jornada» de la PDA (el último de ese día), si lo pulsó.</summary>
        public DateTime? FinJornada { get; set; }
        /// <summary>Inicio de las horas: la primera carga o, sin ella, su primer movimiento.</summary>
        public DateTime? Inicio { get; set; }
        public DateTime? Fin { get; set; }
        /// <summary>De Inicio a Fin, sin descontar la comida (Carlos 19/11/26).</summary>
        public decimal Horas { get; set; }
        /// <summary>La primera y la última parada cerrada (entrega, recogida o incidencia).</summary>
        public DateTime? PrimeraParada { get; set; }
        public DateTime? UltimaParada { get; set; }
        public decimal HorasEnRuta { get; set; }

        /// <summary>Paradas hechas: entregadas y recogidas (sin las incidencias).</summary>
        public int Paradas { get; set; }
        public int Entregas { get; set; }
        public int Recogidas { get; set; }
        /// <summary>Visitas sin hacer («ausente»…): aparte de las paradas.</summary>
        public int Incidencias { get; set; }
        /// <summary>Hechas por tipo: Envio, NotaEntrega, NotaRecogida, NotaCobro, Reposicion, RecogidaReposicion, EnvioInterno.</summary>
        public Dictionary<string, int> ParadasPorTipo { get; set; } = new Dictionary<string, int>();
        /// <summary>Los bultos de las paradas entregadas (no las recogidas).</summary>
        public int BultosEntregados { get; set; }
        /// <summary>Los bultos de las recogidas (notas de recogida y recogidas de reposición).</summary>
        public int BultosRecogidos { get; set; }
        /// <summary>Los bultos que leyó al cargar (RepartoBultosMovimientos); null sin la tabla.</summary>
        public int? BultosCargados { get; set; }

        /// <summary>Reembolsos cobrados en efectivo (envíos y notas de entrega).</summary>
        public decimal CobradoEfectivo { get; set; }
        /// <summary>Reembolsos cobrados con tarjeta (enlace o tarjeta).</summary>
        public decimal CobradoTarjeta { get; set; }
        /// <summary>Otros cobros: las notas de cobro (en cualquier forma).</summary>
        public decimal OtrosCobros { get; set; }
        public decimal TotalCobrado { get; set; }
        /// <summary>Paradas con cobro que se entregaron sin cobrar (autorizado).</summary>
        public int SinCobrar { get; set; }

        /// <summary>Minutos de parada que daba la regla (suma) y los reales (fin − llegada, solo las que tienen llegada).</summary>
        public decimal MinutosParadaPrevistos { get; set; }
        public decimal MinutosParadaReales { get; set; }
        public int ParadasConLlegada { get; set; }
        /// <summary>Medias por parada (null sin datos).</summary>
        public decimal? MinutosParadaMedioPrevisto { get; set; }
        public decimal? MinutosParadaMedioReal { get; set; }
        /// <summary>Media de minutos entre el fin de una parada y el de la siguiente (conducción + parada; hasta 120).</summary>
        public decimal? MinutosEntreParadasMedio { get; set; }

        /// <summary>Corte 5 (km por mapa): todavía null.</summary>
        public decimal? Km { get; set; }
        /// <summary>El fin de ruta que se previó en la PRIMERA estimación del día (RepartoEstimaciones), y si pasaba del cierre.</summary>
        public DateTime? FinPrevisto { get; set; }
        public DateTime? HoraCierre { get; set; }
        public bool? PrevistoPasabaDelCierre { get; set; }
        /// <summary>Minutos de Fin real − FinPrevisto (positivo = acabó más tarde de lo previsto).</summary>
        public int? MinutosSobrePrevisto { get; set; }
    }

    /// <summary>Totales y medias por jornada (de un repartidor o de todos).</summary>
    public class TotalesInformeRepartoDTO
    {
        public int Jornadas { get; set; }
        public int Paradas { get; set; }
        public int Entregas { get; set; }
        public int Recogidas { get; set; }
        public int Incidencias { get; set; }
        public int BultosEntregados { get; set; }
        public int BultosRecogidos { get; set; }
        public decimal CobradoEfectivo { get; set; }
        public decimal CobradoTarjeta { get; set; }
        public decimal OtrosCobros { get; set; }
        public decimal TotalCobrado { get; set; }
        public decimal Horas { get; set; }
        public decimal HorasEnRuta { get; set; }
        public decimal? MinutosParadaMedioPrevisto { get; set; }
        public decimal? MinutosParadaMedioReal { get; set; }
        public decimal? MediaParadas { get; set; }
        public decimal? MediaIncidencias { get; set; }
        public decimal? MediaBultos { get; set; }
        public decimal? MediaHoras { get; set; }
        public decimal? MediaHorasEnRuta { get; set; }
        public decimal? MediaCobrado { get; set; }
    }

    /// <summary>Medias por jornada de un día de la semana.</summary>
    public class DiaSemanaInformeRepartoDTO : TotalesInformeRepartoDTO
    {
        public int DiaSemana { get; set; }
        public string NombreDiaSemana { get; set; }
    }

    public class CodigoPostalInformeRepartoDTO
    {
        public string CodigoPostal { get; set; }
        public int DiaSemana { get; set; }
        public string NombreDiaSemana { get; set; }
        /// <summary>Cuántos días distintos hubo paradas de ese CP ese día de la semana.</summary>
        public int Dias { get; set; }
        public int Paradas { get; set; }
        public int Incidencias { get; set; }
        public int Bultos { get; set; }
        public decimal? MinutosParadaMedioReal { get; set; }
        public decimal? MediaParadasPorDia { get; set; }
    }

    // ==================================================================================================== datos

    /// <summary>Los movimientos de bultos de un usuario un día (una fila agregada por la consulta).</summary>
    public class MovimientosDiaReparto
    {
        public string Usuario { get; set; }
        public DateTime Dia { get; set; }
        public DateTime? PrimeraCarga { get; set; }
        public DateTime? PrimerMovimiento { get; set; }
        public DateTime? UltimoMovimiento { get; set; }
        public int BultosCargados { get; set; }
    }

    public class JornadaTerminadaReparto
    {
        public string Usuario { get; set; }
        public DateTime Dia { get; set; }
        public DateTime FechaFin { get; set; }
    }

    public class PrimeraEstimacionReparto
    {
        public string Repartidor { get; set; }
        public DateTime Dia { get; set; }
        public DateTime FinEstimado { get; set; }
        public DateTime? HoraCierre { get; set; }
        public bool PasaDelCierre { get; set; }
    }

    /// <summary>Lo que lee el informe (sustituible en los tests).</summary>
    public class DatosInformeReparto
    {
        public List<TiempoParadaReparto> Tiempos { get; set; } = new List<TiempoParadaReparto>();
        /// <summary>Null = sin la tabla de movimientos.</summary>
        public List<MovimientosDiaReparto> Movimientos { get; set; }
        /// <summary>Null = sin la tabla.</summary>
        public List<JornadaTerminadaReparto> Jornadas { get; set; }
        /// <summary>Null = sin la tabla.</summary>
        public List<PrimeraEstimacionReparto> Estimaciones { get; set; }
    }

    public interface IRepositorioInformeReparto
    {
        /// <summary>dbo.RepartoParadasTiempos (corte 3): sin ella, 503.</summary>
        bool TablaTiemposExiste();
        /// <summary>Cuatro consultas (paradas, movimientos agregados, jornadas, primera estimación), sin N+1; las opcionales a null sin su tabla.</summary>
        Task<DatosInformeReparto> Leer(DateTime desde, DateTime hasta);
    }

    // ==================================================================================================== LA REGLA (pura)

    /// <summary>
    /// NestoAPI#881 (corte 4): el cálculo del informe, puro. Horas = de la primera carga (o primer movimiento) al último movimiento del día
    /// (bultos, paradas y «Terminar jornada»), sin descontar la comida; horas en ruta = de la primera a la última parada cerrada. Dinero:
    /// reembolsos en efectivo, con tarjeta (enlace o tarjeta) y otros cobros (notas de cobro). Incidencias aparte, como visitas.
    /// </summary>
    public static class CalculoInformeReparto
    {
        public const int MAX_MINUTOS_INTERVALO = 120;
        public const int MAX_DIAS = 366;

        private static readonly CultureInfo castellano = new CultureInfo("es-ES");

        public static int DiaSemana(DateTime dia) => dia.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dia.DayOfWeek;

        public static string NombreDiaSemana(int diaSemana)
            => castellano.DateTimeFormat.GetDayName(diaSemana == 7 ? DayOfWeek.Sunday : (DayOfWeek)diaSemana);

        /// <summary>Qué no se ha podido leer (tablas opcionales sin crear), o null.</summary>
        public static string Aviso(DatosInformeReparto datos)
        {
            var faltan = new List<string>();
            if (datos?.Movimientos == null)
            {
                faltan.Add("los movimientos de bultos (sin primera carga ni bultos cargados)");
            }
            if (datos?.Jornadas == null)
            {
                faltan.Add("los «Terminar jornada»");
            }
            if (datos?.Estimaciones == null)
            {
                faltan.Add("las previsiones de fin de ruta");
            }
            return faltan.Count == 0 ? null : "No se han podido leer " + string.Join(", ", faltan) + ".";
        }

        /// <summary>Null si el rango vale; si no, el motivo (de usted).</summary>
        public static string MotivoRangoNoValido(DateTime desde, DateTime hasta)
        {
            if (hasta.Date < desde.Date)
            {
                return "La fecha «hasta» no puede ser anterior a «desde».";
            }
            if ((hasta.Date - desde.Date).TotalDays >= MAX_DIAS)
            {
                return $"Pida como mucho {MAX_DIAS} días de informe.";
            }
            return null;
        }

        public static InformeRepartoDTO Calcular(DateTime desde, DateTime hasta, DatosInformeReparto datos, string soloRepartidor, DateTime ahora)
        {
            datos = datos ?? new DatosInformeReparto();
            string filtro = Nombre(soloRepartidor);
            bool Vale(string r) => !string.IsNullOrEmpty(r) && (filtro == null || string.Equals(r, filtro, StringComparison.OrdinalIgnoreCase));
            bool EnRango(DateTime d) => d.Date >= desde.Date && d.Date <= hasta.Date;

            var claves = new Dictionary<string, JornadaInformeRepartoDTO>(StringComparer.OrdinalIgnoreCase);
            JornadaInformeRepartoDTO Jornada(string repartidor, DateTime dia)
            {
                string clave = repartidor + "|" + dia.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                if (!claves.TryGetValue(clave, out JornadaInformeRepartoDTO j))
                {
                    j = new JornadaInformeRepartoDTO
                    {
                        Repartidor = repartidor,
                        Dia = dia.Date,
                        DiaSemana = DiaSemana(dia.Date),
                        NombreDiaSemana = NombreDiaSemana(DiaSemana(dia.Date)),
                        BultosCargados = datos.Movimientos == null ? (int?)null : 0
                    };
                    claves[clave] = j;
                }
                return j;
            }

            List<TiempoParadaReparto> tiempos = (datos.Tiempos ?? new List<TiempoParadaReparto>())
                .Where(t => t != null && Vale(Nombre(t.Repartidor)) && EnRango(t.Dia)).ToList();
            foreach (IGrouping<string, TiempoParadaReparto> grupo in tiempos.GroupBy(t => Nombre(t.Repartidor) + "|" + t.Dia.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                StringComparer.OrdinalIgnoreCase))
            {
                TiempoParadaReparto primera = grupo.First();
                Sumar(Jornada(Nombre(primera.Repartidor), primera.Dia), grupo.ToList());
            }
            foreach (MovimientosDiaReparto m in (datos.Movimientos ?? new List<MovimientosDiaReparto>()).Where(m => m != null && Vale(Nombre(m.Usuario)) && EnRango(m.Dia)))
            {
                JornadaInformeRepartoDTO j = Jornada(Nombre(m.Usuario), m.Dia);
                j.PrimeraCarga = Min(j.PrimeraCarga, m.PrimeraCarga);
                j.Inicio = Min(j.Inicio, m.PrimeraCarga ?? m.PrimerMovimiento);
                j.UltimoMovimiento = Max(j.UltimoMovimiento, m.UltimoMovimiento);
                j.BultosCargados = (j.BultosCargados ?? 0) + m.BultosCargados;
            }
            foreach (JornadaTerminadaReparto t in (datos.Jornadas ?? new List<JornadaTerminadaReparto>()).Where(t => t != null && Vale(Nombre(t.Usuario)) && EnRango(t.Dia)))
            {
                JornadaInformeRepartoDTO j = Jornada(Nombre(t.Usuario), t.Dia);
                // «Terminar jornada» cuenta si es del mismo día (hay días con dos cierres o ninguno)
                if (t.FechaFin.Date == t.Dia.Date)
                {
                    j.FinJornada = Max(j.FinJornada, t.FechaFin);
                }
            }
            foreach (PrimeraEstimacionReparto e in (datos.Estimaciones ?? new List<PrimeraEstimacionReparto>()).Where(e => e != null && Vale(Nombre(e.Repartidor)) && EnRango(e.Dia)))
            {
                JornadaInformeRepartoDTO j = Jornada(Nombre(e.Repartidor), e.Dia);
                if (!j.FinPrevisto.HasValue)
                {
                    j.FinPrevisto = e.FinEstimado;
                    j.HoraCierre = e.HoraCierre;
                    j.PrevistoPasabaDelCierre = e.PasaDelCierre;
                }
            }

            foreach (JornadaInformeRepartoDTO j in claves.Values)
            {
                Cerrar(j);
            }

            var informe = new InformeRepartoDTO { Desde = desde.Date, Hasta = hasta.Date, Generado = ahora };
            foreach (IGrouping<string, JornadaInformeRepartoDTO> porRepartidor in claves.Values.GroupBy(j => j.Repartidor, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                List<JornadaInformeRepartoDTO> dias = porRepartidor.OrderBy(j => j.Dia).ToList();
                informe.Repartidores.Add(new InformeRepartidorDTO
                {
                    Repartidor = porRepartidor.Key,
                    Dias = dias,
                    Totales = Totales<TotalesInformeRepartoDTO>(dias, tiempos.Where(t => string.Equals(Nombre(t.Repartidor), porRepartidor.Key, StringComparison.OrdinalIgnoreCase))),
                    PorDiaSemana = PorDiaSemana(dias, tiempos.Where(t => string.Equals(Nombre(t.Repartidor), porRepartidor.Key, StringComparison.OrdinalIgnoreCase)))
                });
            }
            List<JornadaInformeRepartoDTO> todas = claves.Values.ToList();
            informe.Totales = Totales<TotalesInformeRepartoDTO>(todas, tiempos);
            informe.PorDiaSemana = PorDiaSemana(todas, tiempos);
            informe.PorCodigoPostal = PorCodigoPostal(tiempos);
            return informe;
        }

        private static void Sumar(JornadaInformeRepartoDTO j, List<TiempoParadaReparto> paradas)
        {
            var reales = new List<decimal>();
            var entre = new List<decimal>();
            int conPrevisto = 0;
            foreach (TiempoParadaReparto t in paradas)
            {
                bool incidencia = t.Resultado == TiempoParadaReparto.RESULTADO_INCIDENCIA;
                bool recogida = EsRecogida(t);
                j.PrimeraParada = Min(j.PrimeraParada, t.FechaHoraFin);
                j.UltimaParada = Max(j.UltimaParada, t.FechaHoraFin);
                if (incidencia)
                {
                    j.Incidencias++;
                }
                else
                {
                    j.Paradas++;
                    if (recogida)
                    {
                        j.Recogidas++;
                        j.BultosRecogidos += t.Bultos ?? 0;
                    }
                    else
                    {
                        j.Entregas++;
                        j.BultosEntregados += t.Bultos ?? 0;
                    }
                    string tipo = string.IsNullOrWhiteSpace(t.TipoParada) ? TiposParadaReparto.ENVIO : t.TipoParada.Trim();
                    j.ParadasPorTipo[tipo] = (j.ParadasPorTipo.TryGetValue(tipo, out int n) ? n : 0) + 1;
                    SumarCobro(j, t);
                }
                if (t.MinutosPrevistos.HasValue)
                {
                    j.MinutosParadaPrevistos += t.MinutosPrevistos.Value;
                    conPrevisto++;
                }
                decimal? real = Minutos(t.FechaHoraLlegada, t.FechaHoraFin);
                if (real.HasValue)
                {
                    reales.Add(real.Value);
                }
                decimal? intervalo = Minutos(t.FechaHoraAnterior, t.FechaHoraFin);
                if (intervalo.HasValue)
                {
                    entre.Add(intervalo.Value);
                }
            }
            j.MinutosParadaReales += reales.Sum();
            j.ParadasConLlegada += reales.Count;
            j.MinutosParadaMedioPrevisto = conPrevisto == 0 ? (decimal?)null : Math.Round(j.MinutosParadaPrevistos / conPrevisto, 1);
            j.MinutosParadaMedioReal = reales.Count == 0 ? (decimal?)null : Math.Round(reales.Average(), 1);
            j.MinutosEntreParadasMedio = entre.Count == 0 ? (decimal?)null : Math.Round(entre.Average(), 1);
        }

        /// <summary>Reembolsos (efectivo / tarjeta o enlace) y otros cobros (notas de cobro). «NoCobrado» no suma.</summary>
        internal static void SumarCobro(JornadaInformeRepartoDTO j, TiempoParadaReparto t)
        {
            string forma = t.FormaCobro?.Trim();
            if (string.IsNullOrEmpty(forma))
            {
                return;
            }
            if (forma == PruebaEntregaReparto.COBRO_NO_COBRADO)
            {
                j.SinCobrar++;
                return;
            }
            decimal importe = t.ImporteCobro ?? 0;
            if (importe == 0)
            {
                return;
            }
            if (t.TipoParada == TiposParadaReparto.NOTA_COBRO)
            {
                j.OtrosCobros += importe;
            }
            else if (forma == PruebaEntregaReparto.COBRO_EFECTIVO)
            {
                j.CobradoEfectivo += importe;
            }
            else
            {
                j.CobradoTarjeta += importe;
            }
            j.TotalCobrado = j.CobradoEfectivo + j.CobradoTarjeta + j.OtrosCobros;
        }

        private static bool EsRecogida(TiempoParadaReparto t)
            => t.Resultado == TiempoParadaReparto.RESULTADO_RECOGIDA || t.TipoParada == TiposParadaReparto.NOTA_RECOGIDA
                || t.TipoParada == TiempoParadaReparto.TIPO_RECOGIDA_REPOSICION;

        private static void Cerrar(JornadaInformeRepartoDTO j)
        {
            j.Inicio = j.Inicio ?? j.PrimeraParada;
            j.UltimoMovimiento = Max(Max(j.UltimoMovimiento, j.UltimaParada), j.FinJornada);
            j.Fin = j.UltimoMovimiento;
            j.Horas = Horas(j.Inicio, j.Fin);
            j.HorasEnRuta = Horas(j.PrimeraParada, j.UltimaParada);
            j.TotalCobrado = j.CobradoEfectivo + j.CobradoTarjeta + j.OtrosCobros;
            if (j.FinPrevisto.HasValue && j.Fin.HasValue)
            {
                j.MinutosSobrePrevisto = (int)Math.Round((j.Fin.Value - j.FinPrevisto.Value).TotalMinutes);
            }
        }

        private static T Totales<T>(IReadOnlyCollection<JornadaInformeRepartoDTO> dias, IEnumerable<TiempoParadaReparto> tiempos) where T : TotalesInformeRepartoDTO, new()
        {
            var t = new T
            {
                Jornadas = dias.Count,
                Paradas = dias.Sum(d => d.Paradas),
                Entregas = dias.Sum(d => d.Entregas),
                Recogidas = dias.Sum(d => d.Recogidas),
                Incidencias = dias.Sum(d => d.Incidencias),
                BultosEntregados = dias.Sum(d => d.BultosEntregados),
                BultosRecogidos = dias.Sum(d => d.BultosRecogidos),
                CobradoEfectivo = dias.Sum(d => d.CobradoEfectivo),
                CobradoTarjeta = dias.Sum(d => d.CobradoTarjeta),
                OtrosCobros = dias.Sum(d => d.OtrosCobros),
                TotalCobrado = dias.Sum(d => d.TotalCobrado),
                Horas = dias.Sum(d => d.Horas),
                HorasEnRuta = dias.Sum(d => d.HorasEnRuta)
            };
            List<TiempoParadaReparto> lista = (tiempos ?? Enumerable.Empty<TiempoParadaReparto>()).ToList();
            List<decimal> previstos = lista.Where(x => x.MinutosPrevistos.HasValue).Select(x => x.MinutosPrevistos.Value).ToList();
            List<decimal> reales = lista.Select(x => Minutos(x.FechaHoraLlegada, x.FechaHoraFin)).Where(x => x.HasValue).Select(x => x.Value).ToList();
            t.MinutosParadaMedioPrevisto = previstos.Count == 0 ? (decimal?)null : Math.Round(previstos.Average(), 1);
            t.MinutosParadaMedioReal = reales.Count == 0 ? (decimal?)null : Math.Round(reales.Average(), 1);
            if (t.Jornadas > 0)
            {
                t.MediaParadas = Math.Round((decimal)t.Paradas / t.Jornadas, 1);
                t.MediaIncidencias = Math.Round((decimal)t.Incidencias / t.Jornadas, 1);
                t.MediaBultos = Math.Round((decimal)t.BultosEntregados / t.Jornadas, 1);
                t.MediaHoras = Math.Round(t.Horas / t.Jornadas, 2);
                t.MediaHorasEnRuta = Math.Round(t.HorasEnRuta / t.Jornadas, 2);
                t.MediaCobrado = Math.Round(t.TotalCobrado / t.Jornadas, 2);
            }
            return t;
        }

        private static List<DiaSemanaInformeRepartoDTO> PorDiaSemana(IEnumerable<JornadaInformeRepartoDTO> dias, IEnumerable<TiempoParadaReparto> tiempos)
        {
            List<TiempoParadaReparto> lista = (tiempos ?? Enumerable.Empty<TiempoParadaReparto>()).ToList();
            return dias.GroupBy(d => d.DiaSemana).OrderBy(g => g.Key).Select(g =>
            {
                DiaSemanaInformeRepartoDTO d = Totales<DiaSemanaInformeRepartoDTO>(g.ToList(), lista.Where(t => DiaSemana(t.Dia) == g.Key));
                d.DiaSemana = g.Key;
                d.NombreDiaSemana = NombreDiaSemana(g.Key);
                return d;
            }).ToList();
        }

        private static List<CodigoPostalInformeRepartoDTO> PorCodigoPostal(IEnumerable<TiempoParadaReparto> tiempos)
        {
            return tiempos
                .GroupBy(t => new { CP = string.IsNullOrWhiteSpace(t.CodigoPostal) ? "(sin CP)" : t.CodigoPostal.Trim(), Dia = DiaSemana(t.Dia) })
                .OrderBy(g => g.Key.CP, StringComparer.Ordinal).ThenBy(g => g.Key.Dia)
                .Select(g =>
                {
                    List<decimal> reales = g.Select(x => Minutos(x.FechaHoraLlegada, x.FechaHoraFin)).Where(x => x.HasValue).Select(x => x.Value).ToList();
                    int dias = g.Select(x => x.Dia.Date).Distinct().Count();
                    int paradas = g.Count(x => x.Resultado != TiempoParadaReparto.RESULTADO_INCIDENCIA);
                    return new CodigoPostalInformeRepartoDTO
                    {
                        CodigoPostal = g.Key.CP,
                        DiaSemana = g.Key.Dia,
                        NombreDiaSemana = NombreDiaSemana(g.Key.Dia),
                        Dias = dias,
                        Paradas = paradas,
                        Incidencias = g.Count(x => x.Resultado == TiempoParadaReparto.RESULTADO_INCIDENCIA),
                        Bultos = g.Where(x => x.Resultado != TiempoParadaReparto.RESULTADO_INCIDENCIA).Sum(x => x.Bultos ?? 0),
                        MinutosParadaMedioReal = reales.Count == 0 ? (decimal?)null : Math.Round(reales.Average(), 1),
                        MediaParadasPorDia = dias == 0 ? (decimal?)null : Math.Round((decimal)paradas / dias, 1)
                    };
                }).ToList();
        }

        /// <summary>Minutos de <paramref name="desde"/> a <paramref name="hasta"/> si están entre 0 (exclusivo) y 120; si no, null.</summary>
        internal static decimal? Minutos(DateTime? desde, DateTime hasta)
        {
            if (!desde.HasValue)
            {
                return null;
            }
            double m = (hasta - desde.Value).TotalMinutes;
            return m > 0 && m <= MAX_MINUTOS_INTERVALO ? Math.Round((decimal)m, 1) : (decimal?)null;
        }

        private static decimal Horas(DateTime? desde, DateTime? hasta)
            => desde.HasValue && hasta.HasValue && hasta.Value > desde.Value ? Math.Round((decimal)(hasta.Value - desde.Value).TotalHours, 2) : 0;

        private static DateTime? Min(DateTime? a, DateTime? b) => !a.HasValue ? b : !b.HasValue ? a : (a.Value <= b.Value ? a : b);

        private static DateTime? Max(DateTime? a, DateTime? b) => !a.HasValue ? b : !b.HasValue ? a : (a.Value >= b.Value ? a : b);

        /// <summary>El repartidor sin dominio (como RepartoParadasTiempos).</summary>
        internal static string Nombre(string usuario) => ServicioRepartoPropio.NombreRepartidor(usuario);
    }

    // ==================================================================================================== lectura

    /// <summary>NestoAPI#881 (corte 4): las cuatro consultas del informe. ADO como RepositorioTiemposReparto.</summary>
    public class RepositorioInformeReparto : IRepositorioInformeReparto
    {
        internal const string NOMBRE_APLICACION = "NestoAPI-InformeReparto";
        private const int TIMEOUT = 60;

        internal const string SQL_TIEMPOS = @"
SELECT RTRIM(Repartidor), Dia, RTRIM(TipoParada), RTRIM(Resultado), RTRIM(CodigoPostal), Bultos, RTRIM(FormaCobro), ImporteCobro,
    FechaHoraLlegada, FechaHoraFin, FechaHoraAnterior, MinutosPrevistos, EnvioId
FROM dbo." + RepositorioTiemposReparto.TABLA_TIEMPOS + @" WITH (NOLOCK) WHERE Dia >= @desde AND Dia <= @hasta";

        internal const string SQL_MOVIMIENTOS = @"
SELECT RTRIM(Usuario) AS Usuario, CAST(FechaHora AS date) AS Dia, MIN(CASE WHEN Tipo = 'Cargado' THEN FechaHora END) AS PrimeraCarga,
    MIN(FechaHora) AS PrimerMovimiento, MAX(FechaHora) AS UltimoMovimiento, SUM(CASE WHEN Tipo = 'Cargado' THEN 1 ELSE 0 END) AS BultosCargados
FROM dbo." + RepositorioFurgoneta.TABLA + @" WITH (NOLOCK)
WHERE Origen = 'Repartidor' AND FechaHora >= @desde AND FechaHora < @hastaMasUno
GROUP BY RTRIM(Usuario), CAST(FechaHora AS date)";

        internal const string SQL_JORNADAS = @"
SELECT RTRIM(Usuario), Fecha, MAX(FechaFin) FROM dbo." + RepositorioFurgoneta.TABLA_JORNADAS + @" WITH (NOLOCK) WHERE Fecha >= @desde AND Fecha <= @hasta GROUP BY RTRIM(Usuario), Fecha";

        internal const string SQL_ESTIMACIONES = @"
SELECT Repartidor, Dia, FinEstimado, HoraCierre, PasaDelCierre FROM (
    SELECT RTRIM(Repartidor) AS Repartidor, Dia, FinEstimado, HoraCierre, PasaDelCierre,
        ROW_NUMBER() OVER (PARTITION BY RTRIM(Repartidor), Dia ORDER BY FechaAlta, Id) AS n
    FROM dbo." + RepositorioTiemposReparto.TABLA_ESTIMACIONES + @" WITH (NOLOCK) WHERE Dia >= @desde AND Dia <= @hasta) x
WHERE x.n = 1";

        private static readonly Dictionary<string, bool> existe = new Dictionary<string, bool>();
        private static readonly object bloqueo = new object();
        private static bool avisadoSinTabla;

        private readonly string cadenaConexion;

        public RepositorioInformeReparto(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = cadenaConexion;
        }

        public static RepositorioInformeReparto DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"];
            if (origen == null)
            {
                throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            }
            return new RepositorioInformeReparto(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaTiemposExiste()
        {
            bool hay = Existe(RepositorioTiemposReparto.TABLA_TIEMPOS);
            if (!hay)
            {
                bool avisar;
                lock (bloqueo)
                {
                    avisar = !avisadoSinTabla;
                    avisadoSinTabla = true;
                }
                if (avisar)
                {
                    ElmahHelper.Log(new Exception("[Reparto #881] Informe de reparto: falta dbo.RepartoParadasTiempos (Scripts/Issue881_RepartoFinRutaYTiempos.sql): " +
                        "GET api/RepartoPropio/Informe da 503."));
                }
            }
            return hay;
        }

        public async Task<DatosInformeReparto> Leer(DateTime desde, DateTime hasta)
        {
            var datos = new DatosInformeReparto();
            using (var conexion = new SqlConnection(cadenaConexion))
            {
                await conexion.OpenAsync().ConfigureAwait(false);
                datos.Tiempos = await LeerFilas(conexion, SQL_TIEMPOS, desde, hasta, l => new TiempoParadaReparto
                {
                    Repartidor = Cadena(l, 0),
                    Dia = l.GetDateTime(1),
                    TipoParada = Cadena(l, 2),
                    Resultado = Cadena(l, 3),
                    CodigoPostal = Cadena(l, 4),
                    Bultos = l.IsDBNull(5) ? (int?)null : Convert.ToInt32(l.GetValue(5)),
                    FormaCobro = Cadena(l, 6),
                    ImporteCobro = l.IsDBNull(7) ? (decimal?)null : Convert.ToDecimal(l.GetValue(7)),
                    FechaHoraLlegada = l.IsDBNull(8) ? (DateTime?)null : l.GetDateTime(8),
                    FechaHoraFin = l.GetDateTime(9),
                    FechaHoraAnterior = l.IsDBNull(10) ? (DateTime?)null : l.GetDateTime(10),
                    MinutosPrevistos = l.IsDBNull(11) ? (decimal?)null : Convert.ToDecimal(l.GetValue(11)),
                    EnvioId = l.IsDBNull(12) ? (int?)null : Convert.ToInt32(l.GetValue(12))
                }).ConfigureAwait(false);
                if (Existe(RepositorioFurgoneta.TABLA))
                {
                    datos.Movimientos = await LeerFilas(conexion, SQL_MOVIMIENTOS, desde, hasta, l => new MovimientosDiaReparto
                    {
                        Usuario = Cadena(l, 0),
                        Dia = l.GetDateTime(1),
                        PrimeraCarga = l.IsDBNull(2) ? (DateTime?)null : l.GetDateTime(2),
                        PrimerMovimiento = l.IsDBNull(3) ? (DateTime?)null : l.GetDateTime(3),
                        UltimoMovimiento = l.IsDBNull(4) ? (DateTime?)null : l.GetDateTime(4),
                        BultosCargados = l.IsDBNull(5) ? 0 : Convert.ToInt32(l.GetValue(5))
                    }).ConfigureAwait(false);
                }
                if (Existe(RepositorioFurgoneta.TABLA_JORNADAS))
                {
                    datos.Jornadas = await LeerFilas(conexion, SQL_JORNADAS, desde, hasta, l => new JornadaTerminadaReparto
                    {
                        Usuario = Cadena(l, 0),
                        Dia = l.GetDateTime(1),
                        FechaFin = l.GetDateTime(2)
                    }).ConfigureAwait(false);
                }
                if (Existe(RepositorioTiemposReparto.TABLA_ESTIMACIONES))
                {
                    datos.Estimaciones = await LeerFilas(conexion, SQL_ESTIMACIONES, desde, hasta, l => new PrimeraEstimacionReparto
                    {
                        Repartidor = Cadena(l, 0),
                        Dia = l.GetDateTime(1),
                        FinEstimado = l.GetDateTime(2),
                        HoraCierre = l.IsDBNull(3) ? (DateTime?)null : l.GetDateTime(3),
                        PasaDelCierre = !l.IsDBNull(4) && Convert.ToBoolean(l.GetValue(4))
                    }).ConfigureAwait(false);
                }
            }
            return datos;
        }

        private static async Task<List<T>> LeerFilas<T>(SqlConnection conexion, string sql, DateTime desde, DateTime hasta, Func<SqlDataReader, T> leer)
        {
            var filas = new List<T>();
            using (var comando = new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT })
            {
                comando.Parameters.Add("@desde", SqlDbType.DateTime).Value = desde.Date;
                comando.Parameters.Add("@hasta", SqlDbType.DateTime).Value = hasta.Date;
                comando.Parameters.Add("@hastaMasUno", SqlDbType.DateTime).Value = hasta.Date.AddDays(1);
                using (SqlDataReader l = await comando.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await l.ReadAsync().ConfigureAwait(false))
                    {
                        filas.Add(leer(l));
                    }
                }
            }
            return filas;
        }

        /// <summary>Solo se recuerda el «sí»; ante error, no.</summary>
        private bool Existe(string tabla)
        {
            lock (bloqueo)
            {
                if (existe.TryGetValue(tabla, out bool si) && si)
                {
                    return true;
                }
            }
            try
            {
                using (var conexion = new SqlConnection(cadenaConexion))
                using (var comando = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@tabla, 'U') IS NULL THEN 0 ELSE 1 END", conexion) { CommandTimeout = TIMEOUT })
                {
                    conexion.Open();
                    comando.Parameters.Add("@tabla", SqlDbType.NVarChar, 200).Value = "dbo." + tabla;
                    bool hay = Convert.ToInt32(comando.ExecuteScalar()) == 1;
                    if (hay)
                    {
                        lock (bloqueo)
                        {
                            existe[tabla] = true;
                        }
                    }
                    return hay;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Cadena(SqlDataReader l, int i) => l.IsDBNull(i) ? null : Convert.ToString(l.GetValue(i), CultureInfo.InvariantCulture)?.Trim();
    }
}
