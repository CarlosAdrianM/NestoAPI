using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Infraestructure.Calidad.PedidoPerfecto
{
    public class MotivoResumenDTO
    {
        public string Motivo { get; set; }
        public string Texto { get; set; }
        public bool EsError { get; set; }
        /// <summary>Se ha podido mirar en algún pedido del periodo.</summary>
        public bool Evaluado { get; set; }
        /// <summary>Desde cuándo hay rastro (null = siempre).</summary>
        public DateTime? EvaluadoDesde { get; set; }
        public int PedidosEvaluados { get; set; }
        /// <summary>Pedidos con este motivo.</summary>
        public int Pedidos { get; set; }
        /// <summary>Sobre los pedidos EVALUADOS para este motivo (null si ninguno).</summary>
        public decimal? Porcentaje { get; set; }
    }

    public class MesResumenDTO
    {
        public string Mes { get; set; }
        public int Pedidos { get; set; }
        public int Perfectos { get; set; }
        public decimal? Porcentaje { get; set; }
    }

    public class CanalResumenDTO
    {
        public string Canal { get; set; }
        public int Pedidos { get; set; }
        public int Perfectos { get; set; }
        public decimal? Porcentaje { get; set; }
    }

    public class MotivoNoEvaluadoDTO
    {
        public string Motivo { get; set; }
        public string PorQue { get; set; }
        public DateTime? EvaluadoDesde { get; set; }
        /// <summary>Pedidos del periodo en los que no se ha podido mirar.</summary>
        public int PedidosSinEvaluar { get; set; }
    }

    public class ResumenPedidoPerfectoDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int Pedidos { get; set; }
        public int Perfectos { get; set; }
        public decimal? PorcentajePerfectos { get; set; }
        /// <summary>Perfectos si no se cuentan los motivos informativos (EsError = false).</summary>
        public int PerfectosSinContarInformativos { get; set; }
        public decimal? PorcentajePerfectosSinContarInformativos { get; set; }
        public List<MotivoResumenDTO> Motivos { get; set; } = new List<MotivoResumenDTO>();
        public List<MesResumenDTO> PorMes { get; set; } = new List<MesResumenDTO>();
        public List<CanalResumenDTO> PorCanal { get; set; } = new List<CanalResumenDTO>();
        public List<MotivoNoEvaluadoDTO> MotivosNoEvaluados { get; set; } = new List<MotivoNoEvaluadoDTO>();
        public DateTime CalculadoEl { get; set; }
        /// <summary>Tablas opcionales que faltan, de usted (null si no falta ninguna).</summary>
        public string Aviso { get; set; }
    }

    public class PedidoPerfectoDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public DateTime Fecha { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Canal { get; set; }
        public string Vendedor { get; set; }
        /// <summary>Quien lo metió (o, sin ese dato, el último que tocó la cabecera: ver <see cref="UsuarioEsDelAlta"/>).</summary>
        public string Usuario { get; set; }
        public bool UsuarioEsDelAlta { get; set; }
        public decimal Importe { get; set; }
        public bool Perfecto { get; set; }
        public bool PerfectoSinContarInformativos { get; set; }
        public List<string> Motivos { get; set; } = new List<string>();
        public List<string> MotivosNoEvaluados { get; set; } = new List<string>();
        public DateTime? PrimerAlbaran { get; set; }
        public int? DiasLaborablesHastaServir { get; set; }
        public int Albaranes { get; set; }
        public byte? ModoServicio { get; set; }
        public string PlazosPago { get; set; }
        public string FormaPago { get; set; }
    }

    public class PaginaPedidosPerfectoDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public string Motivo { get; set; }
        public int Pagina { get; set; }
        public int Tamano { get; set; }
        public int Total { get; set; }
        public List<PedidoPerfectoDTO> Pedidos { get; set; } = new List<PedidoPerfectoDTO>();
    }

    /// <summary>El cálculo de un periodo, cacheado (lo comparten Resumen y Pedidos).</summary>
    public class CalculoPedidoPerfecto
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public DateTime CalculadoEl { get; set; }
        public ContextoPedidoPerfecto Contexto { get; set; }
        public List<string> TablasQueFaltan { get; set; } = new List<string>();
        public List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> Pedidos { get; set; } =
            new List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>>();
    }

    public interface IServicioPedidoPerfecto
    {
        ResumenPedidoPerfectoDTO Resumen(string empresa, DateTime desde, DateTime hasta);
        PaginaPedidosPerfectoDTO Pedidos(string empresa, DateTime desde, DateTime hasta, MotivoPedidoPerfecto? motivo, int pagina, int tamano);
    }

    /// <summary>NestoAPI#914: lee el periodo (<see cref="ILectorPedidoPerfecto"/>), aplica LA REGLA a cada pedido y resume. Caché en memoria de 30 min.</summary>
    public class ServicioPedidoPerfecto : IServicioPedidoPerfecto
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(30);

        private readonly Func<ILectorPedidoPerfecto> lector;
        private readonly Func<DateTime> ahora;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly ObjectCache cache;

        public ServicioPedidoPerfecto(Func<ILectorPedidoPerfecto> lector = null, Func<DateTime> ahora = null,
            Func<DateTime, string, bool> esFestivo = null, ObjectCache cache = null)
        {
            this.lector = lector ?? (() => LectorPedidoPerfectoSql.DesdeConfiguracion());
            this.ahora = ahora ?? (() => DateTime.Now);
            this.esFestivo = esFestivo ?? GestorFestivos.EsFestivo;
            this.cache = cache ?? MemoryCache.Default;
        }

        public CalculoPedidoPerfecto Calcular(string empresa, DateTime desde, DateTime hasta)
        {
            string clave = $"CalidadPedidoPerfecto|{empresa}|{desde:yyyyMMdd}|{hasta:yyyyMMdd}";
            var nuevo = new Lazy<CalculoPedidoPerfecto>(() => CalcularSinCache(empresa, desde, hasta));
            var existente = cache.AddOrGetExisting(clave, nuevo, DateTimeOffset.UtcNow.Add(DURACION_CACHE)) as Lazy<CalculoPedidoPerfecto>;
            Lazy<CalculoPedidoPerfecto> usar = existente ?? nuevo;
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

        internal CalculoPedidoPerfecto CalcularSinCache(string empresa, DateTime desde, DateTime hasta)
        {
            LecturaPedidoPerfecto lectura = lector().Leer(empresa, desde, hasta);
            var contexto = new ContextoPedidoPerfecto
            {
                Hoy = ahora().Date,
                EsFestivo = esFestivo,
                FotosStockDesde = lectura.FotosStockDesde,
                QuitarPickingDesde = lectura.QuitarPickingDesde,
                EntregaIncompletaDesde = lectura.EntregaIncompletaDesde,
                PrimerEventoPorTipo = lectura.PrimerEventoPorTipo ?? new Dictionary<string, DateTime>()
            };
            return new CalculoPedidoPerfecto
            {
                Empresa = empresa,
                Desde = desde,
                Hasta = hasta,
                CalculadoEl = ahora(),
                Contexto = contexto,
                TablasQueFaltan = lectura.TablasQueFaltan ?? new List<string>(),
                Pedidos = (lectura.Pedidos ?? new List<HechosPedidoPerfecto>())
                    .Select(h => new KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>(h, ReglaPedidoPerfecto.Evaluar(h, contexto)))
                    .ToList()
            };
        }

        public ResumenPedidoPerfectoDTO Resumen(string empresa, DateTime desde, DateTime hasta) => Resumir(Calcular(empresa, desde, hasta));

        public PaginaPedidosPerfectoDTO Pedidos(string empresa, DateTime desde, DateTime hasta, MotivoPedidoPerfecto? motivo, int pagina, int tamano) =>
            Paginar(Calcular(empresa, desde, hasta), motivo, pagina, tamano);

        public static ResumenPedidoPerfectoDTO Resumir(CalculoPedidoPerfecto calculo)
        {
            List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> pedidos = calculo.Pedidos;
            int total = pedidos.Count;
            int perfectos = pedidos.Count(p => p.Value.Perfecto);
            int perfectosSinInformativos = pedidos.Count(p => p.Value.PerfectoSinContarInformativos);
            var resumen = new ResumenPedidoPerfectoDTO
            {
                Empresa = calculo.Empresa,
                Desde = calculo.Desde,
                Hasta = calculo.Hasta,
                Pedidos = total,
                Perfectos = perfectos,
                PorcentajePerfectos = Porcentaje(perfectos, total),
                PerfectosSinContarInformativos = perfectosSinInformativos,
                PorcentajePerfectosSinContarInformativos = Porcentaje(perfectosSinInformativos, total),
                CalculadoEl = calculo.CalculadoEl,
                Aviso = calculo.TablasQueFaltan.Count == 0 ? null
                    : $"Faltan tablas que no son imprescindibles ({string.Join(", ", calculo.TablasQueFaltan)}): lo que dependa de ellas no se ha podido mirar."
            };
            foreach (MotivoPedidoPerfecto motivo in InfoMotivosPedidoPerfecto.Todos)
            {
                int evaluados = pedidos.Count(p => !p.Value.NoEvaluados.Contains(motivo));
                int con = pedidos.Count(p => p.Value.Motivos.Contains(motivo));
                DateTime? desde = calculo.Contexto?.EvaluadoDesde(motivo);
                resumen.Motivos.Add(new MotivoResumenDTO
                {
                    Motivo = motivo.ToString(),
                    Texto = InfoMotivosPedidoPerfecto.Texto(motivo),
                    EsError = InfoMotivosPedidoPerfecto.EsError(motivo),
                    Evaluado = evaluados > 0,
                    EvaluadoDesde = desde,
                    PedidosEvaluados = evaluados,
                    Pedidos = con,
                    Porcentaje = Porcentaje(con, evaluados)
                });
                bool sinRastroNunca = ContextoPedidoPerfecto.TieneFechaDeInicio(motivo) && !desde.HasValue;
                if (evaluados < total || (total == 0 && sinRastroNunca))
                {
                    resumen.MotivosNoEvaluados.Add(new MotivoNoEvaluadoDTO
                    {
                        Motivo = motivo.ToString(),
                        PorQue = InfoMotivosPedidoPerfecto.PorQueNoEvaluado(motivo),
                        EvaluadoDesde = desde,
                        PedidosSinEvaluar = total - evaluados
                    });
                }
            }
            resumen.PorMes = pedidos
                .GroupBy(p => new DateTime(p.Key.Fecha.Year, p.Key.Fecha.Month, 1))
                .OrderBy(g => g.Key)
                .Select(g => new MesResumenDTO
                {
                    Mes = g.Key.ToString("yyyy-MM"),
                    Pedidos = g.Count(),
                    Perfectos = g.Count(p => p.Value.Perfecto),
                    Porcentaje = Porcentaje(g.Count(p => p.Value.Perfecto), g.Count())
                }).ToList();
            resumen.PorCanal = pedidos
                .GroupBy(p => p.Value.Canal ?? ReglaPedidoPerfecto.Canales.OTROS)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => new CanalResumenDTO
                {
                    Canal = g.Key,
                    Pedidos = g.Count(),
                    Perfectos = g.Count(p => p.Value.Perfecto),
                    Porcentaje = Porcentaje(g.Count(p => p.Value.Perfecto), g.Count())
                }).ToList();
            return resumen;
        }

        public static PaginaPedidosPerfectoDTO Paginar(CalculoPedidoPerfecto calculo, MotivoPedidoPerfecto? motivo, int pagina, int tamano)
        {
            List<KeyValuePair<HechosPedidoPerfecto, EvaluacionPedidoPerfecto>> filtrados = calculo.Pedidos
                .Where(p => !motivo.HasValue || p.Value.Motivos.Contains(motivo.Value))
                .OrderBy(p => p.Key.Fecha).ThenBy(p => p.Key.Pedido)
                .ToList();
            return new PaginaPedidosPerfectoDTO
            {
                Empresa = calculo.Empresa,
                Desde = calculo.Desde,
                Hasta = calculo.Hasta,
                Motivo = motivo?.ToString(),
                Pagina = pagina,
                Tamano = tamano,
                Total = filtrados.Count,
                Pedidos = filtrados.Skip((pagina - 1) * tamano).Take(tamano).Select(p => Detalle(p.Key, p.Value)).ToList()
            };
        }

        public static PedidoPerfectoDTO Detalle(HechosPedidoPerfecto h, EvaluacionPedidoPerfecto e) => new PedidoPerfectoDTO
        {
            Empresa = h.Empresa?.Trim(),
            Pedido = h.Pedido,
            Fecha = h.Fecha,
            Cliente = h.Cliente?.Trim(),
            Contacto = h.Contacto?.Trim(),
            Canal = e.Canal,
            Vendedor = h.Vendedor?.Trim(),
            Usuario = h.Usuario?.Trim(),
            UsuarioEsDelAlta = h.UsuarioEsDelAlta,
            Importe = h.Importe,
            Perfecto = e.Perfecto,
            PerfectoSinContarInformativos = e.PerfectoSinContarInformativos,
            Motivos = e.Motivos.Select(m => m.ToString()).ToList(),
            MotivosNoEvaluados = e.NoEvaluados.OrderBy(m => (int)m).Select(m => m.ToString()).ToList(),
            PrimerAlbaran = h.PrimerAlbaran,
            DiasLaborablesHastaServir = e.DiasLaborablesHastaServir,
            Albaranes = h.Albaranes,
            ModoServicio = h.ModoServicio,
            PlazosPago = h.PlazosPago?.Trim(),
            FormaPago = h.FormaPago?.Trim()
        };

        internal static decimal? Porcentaje(int parte, int total) =>
            total <= 0 ? (decimal?)null : Math.Round(100m * parte / total, 1, MidpointRounding.AwayFromZero);
    }
}
