using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;

namespace NestoAPI.Infraestructure.Calidad.Almacen
{
    public class IndicadorPreparadorDTO
    {
        public string Indicador { get; set; }
        /// <summary>Linea, Parada o Lecturas: sobre qué se cuenta (el denominador).</summary>
        public string Ambito { get; set; }
        /// <summary>Líneas (o paradas) en las que se ha podido mirar. En Lecturas, null.</summary>
        public int? Evaluadas { get; set; }
        /// <summary>Líneas (o paradas, o lecturas) con el indicador.</summary>
        public int Con { get; set; }
        /// <summary>Sobre las evaluadas (null si ninguna o en Lecturas).</summary>
        public decimal? Porcentaje { get; set; }
    }

    public class PreparadorAlmacenDTO
    {
        /// <summary>Sin dominio. Null en el total.</summary>
        public string Preparador { get; set; }
        public int Pickings { get; set; }
        /// <summary>De esos, en los que también preparó otra persona alguna parada.</summary>
        public int PickingsCompartidos { get; set; }
        public int Pedidos { get; set; }
        /// <summary>Líneas preparadas (producto de un pedido en un picking).</summary>
        public int Lineas { get; set; }
        /// <summary>Paradas del recorrido (producto en un picking).</summary>
        public int Paradas { get; set; }
        /// <summary>Unidades cogidas (leídas en el recorrido, netas).</summary>
        public int Unidades { get; set; }
        /// <summary>
        /// Líneas con algún indicador que es error: desde el 27/11/26, FaltaEnPacking o ErrorDeEnvioReclamado (antes, solo FaltaEnPacking).
        /// Mezcla dos denominadores: para cada uno por separado, <see cref="ErrorDeEnvioReclamado"/> y <see cref="FaltaEnPacking"/>.
        /// </summary>
        public int LineasConError { get; set; }
        /// <summary>Líneas evaluadas para algún indicador de error de línea.</summary>
        public int LineasEvaluadasError { get; set; }
        public decimal? PorcentajeError { get; set; }
        /// <summary>
        /// OFICIAL 1 (Carlos 27/11/26): el error real, lo que el cliente reclamó (reenviado sin coste o cambiado por otro), sobre sus líneas
        /// servidas hace al menos los días de reclamación. La misma fila que la de <see cref="Indicadores"/>.
        /// </summary>
        public IndicadorPreparadorDTO ErrorDeEnvioReclamado { get; set; }
        /// <summary>OFICIAL 2: el error que se cazó antes de salir (falta en el packing), sobre sus líneas empaquetadas con Ariadna.</summary>
        public IndicadorPreparadorDTO FaltaEnPacking { get; set; }
        public List<IndicadorPreparadorDTO> Indicadores { get; set; } = new List<IndicadorPreparadorDTO>();
    }

    public class IndicadorAlmacenDTO
    {
        public string Indicador { get; set; }
        public string Texto { get; set; }
        public bool EsError { get; set; }
        /// <summary>Uno de los dos indicadores oficiales por preparador (<see cref="InfoIndicadoresAlmacen.Oficiales"/>).</summary>
        public bool Oficial { get; set; }
        public string Ambito { get; set; }
        /// <summary>Desde cuándo hay rastro (null: no hay ninguno).</summary>
        public DateTime? EvaluadoDesde { get; set; }
        public int? Evaluadas { get; set; }
        public int Con { get; set; }
        public decimal? Porcentaje { get; set; }
    }

    public class IndicadorNoEvaluadoAlmacenDTO
    {
        public string Indicador { get; set; }
        public string PorQue { get; set; }
        public DateTime? EvaluadoDesde { get; set; }
        /// <summary>Líneas (o paradas) del periodo en las que no se ha podido mirar.</summary>
        public int SinEvaluar { get; set; }
    }

    /// <summary>Lo que se querría medir y hoy no deja rastro: qué evento habría que empezar a apuntar y dónde.</summary>
    public class IndicadorSinRastroAlmacenDTO
    {
        public string Indicador { get; set; }
        public string Que { get; set; }
        public string HaceFalta { get; set; }
    }

    public class PickingsSinRastroDTO
    {
        public int Pickings { get; set; }
        public int Pedidos { get; set; }
        public int Lineas { get; set; }
        /// <summary>Por almacén: «ALG» → pickings.</summary>
        public Dictionary<string, int> PorAlmacen { get; set; } = new Dictionary<string, int>();
    }

    public class ResumenCalidadAlmacenDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public PreparadorAlmacenDTO Total { get; set; }
        /// <summary>Los dos indicadores oficiales por preparador, en orden: ErrorDeEnvioReclamado y FaltaEnPacking.</summary>
        public List<string> IndicadoresOficiales { get; set; } = new List<string>();
        /// <summary>Los días después del albarán que se mira si el cliente reclamó (Calidad.DiasReclamacionEnvio, 45 sin fila).</summary>
        public int DiasReclamacionEnvio { get; set; }
        public List<PreparadorAlmacenDTO> Preparadores { get; set; } = new List<PreparadorAlmacenDTO>();
        public List<IndicadorAlmacenDTO> Indicadores { get; set; } = new List<IndicadorAlmacenDTO>();
        public List<IndicadorNoEvaluadoAlmacenDTO> IndicadoresNoEvaluados { get; set; } = new List<IndicadorNoEvaluadoAlmacenDTO>();
        public List<IndicadorSinRastroAlmacenDTO> SinRastro { get; set; } = new List<IndicadorSinRastroAlmacenDTO>();
        /// <summary>Líneas de los pickings del periodo sin ninguna lectura de su producto en el recorrido (sin preparador).</summary>
        public int LineasSinPreparador { get; set; }
        /// <summary>Pickings del rango de números del periodo sin ninguna lectura del recorrido en Ariadna (en papel): no entran.</summary>
        public PickingsSinRastroDTO PickingsSinRastro { get; set; } = new PickingsSinRastroDTO();
        /// <summary>Desde cuándo hay preparador de Ariadna (la primera lectura del recorrido de un picking).</summary>
        public DateTime? PreparadorDesde { get; set; }
        public DateTime CalculadoEl { get; set; }
        public string Aviso { get; set; }
    }

    public class LineaCalidadAlmacenDTO
    {
        public int Picking { get; set; }
        public DateTime FechaPicking { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Producto { get; set; }
        /// <summary>LinPedidoVta.[Nº Orden].</summary>
        public List<int> LineasPedido { get; set; } = new List<int>();
        public int Cantidad { get; set; }
        public string Almacen { get; set; }
        public string Factura { get; set; }
        public string Preparador { get; set; }
        public bool PackingEvaluado { get; set; }
        public string Empaquetador { get; set; }
        public int MetidasEnPacking { get; set; }
        public int FaltasEnPacking { get; set; }
        public decimal UnidadesRectificadas { get; set; }
        public List<string> Indicadores { get; set; } = new List<string>();
        public List<string> NoEvaluados { get; set; } = new List<string>();
        /// <summary>Lo que le pasó después de servirla (ReenviadoSinCoste, CambiadoPorOtro, AbonadoSinReposicion), aunque aún no cuente.</summary>
        public List<string> SubMotivosReclamacion { get; set; } = new List<string>();
        /// <summary>Ya han pasado los días de reclamación desde el albarán (si no, los sub-motivos se ven pero no cuentan).</summary>
        public bool ReclamacionEvaluada { get; set; }
        /// <summary>Las líneas posteriores que lo demuestran (pedido, fecha, producto, cantidad, importe) o la rectificativa.</summary>
        public List<NestoAPI.Infraestructure.Calidad.Reclamaciones.PruebaReclamacion> PruebasReclamacion { get; set; } =
            new List<NestoAPI.Infraestructure.Calidad.Reclamaciones.PruebaReclamacion>();
    }

    public class ParadaCalidadAlmacenDTO
    {
        public int Picking { get; set; }
        public DateTime FechaPicking { get; set; }
        public string Producto { get; set; }
        public string Preparador { get; set; }
        public List<string> OtrosQueLeyeron { get; set; } = new List<string>();
        public int Cogidas { get; set; }
        public int Faltas { get; set; }
        public string DioLaFalta { get; set; }
        public int CantidadEnLineas { get; set; }
        public int MetidasEnPacking { get; set; }
        public int FaltasEnPacking { get; set; }
        public List<string> Indicadores { get; set; } = new List<string>();
        public List<string> NoEvaluados { get; set; } = new List<string>();
    }

    public class NoCasadasPreparadorDTO
    {
        public int Picking { get; set; }
        public string Usuario { get; set; }
        public string Pantalla { get; set; }
        public int Lecturas { get; set; }
    }

    public class DetalleCalidadAlmacenDTO
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public string Preparador { get; set; }
        public string Indicador { get; set; }
        public int Pagina { get; set; }
        public int Tamano { get; set; }
        public int TotalLineas { get; set; }
        public int TotalParadas { get; set; }
        /// <summary>Las líneas (de la página) del preparador; con indicador de línea, solo las que lo tienen.</summary>
        public List<LineaCalidadAlmacenDTO> Lineas { get; set; } = new List<LineaCalidadAlmacenDTO>();
        /// <summary>Las paradas (de la página) del preparador (imputadas a él); con indicador de parada, solo las que lo tienen.</summary>
        public List<ParadaCalidadAlmacenDTO> Paradas { get; set; } = new List<ParadaCalidadAlmacenDTO>();
        public List<NoCasadasPreparadorDTO> NoCasadas { get; set; } = new List<NoCasadasPreparadorDTO>();
    }

    /// <summary>El cálculo de un periodo, cacheado (lo comparten Resumen y Detalle).</summary>
    public class CalculoCalidadAlmacen
    {
        public string Empresa { get; set; }
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public DateTime CalculadoEl { get; set; }
        public LecturaCalidadAlmacen Lectura { get; set; }
        public EvaluacionCalidadAlmacen Evaluacion { get; set; }
    }

    public interface IServicioCalidadAlmacen
    {
        ResumenCalidadAlmacenDTO Resumen(string empresa, DateTime desde, DateTime hasta);
        DetalleCalidadAlmacenDTO Detalle(string empresa, DateTime desde, DateTime hasta, string preparador, IndicadorAlmacen? indicador, int pagina, int tamano);
    }

    /// <summary>NestoAPI#914 (corte 2): lee el periodo (<see cref="ILectorCalidadAlmacen"/>), aplica LA REGLA y resume por preparador. Caché de 30 min.</summary>
    public class ServicioCalidadAlmacen : IServicioCalidadAlmacen
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(30);

        /// <summary>Lo que se querría medir y hoy no deja rastro (o no es del preparador). Ver docs/claude/calidad-kpi.md.</summary>
        public static readonly IReadOnlyList<IndicadorSinRastroAlmacenDTO> SIN_RASTRO = new List<IndicadorSinRastroAlmacenDTO>
        {
            new IndicadorSinRastroAlmacenDTO
            {
                Indicador = "FaltaQueSiEstaba",
                Que = "Una falta que quedó como falta al terminar el picking y luego el producto sí estaba en el hueco. Las lecturas del recorrido no guardan el hueco " +
                    "de lo cogido (solo el de la falta), y un recuento posterior del hueco no dice si se repuso entre medias.",
                HaceFalta = "Guardar el hueco en cada lectura del recorrido (columna en dbo.PreparacionEscaneos, que mande Ariadna) y apuntar un evento cuando un recuento " +
                    "del hueco (inventario por huecos o «No está» de otra salida) encuentre unidades de un producto dado por falta en ese hueco. Desde hoy se apunta " +
                    "«NoEstabaEnSuHueco» al cambiar de hueco en el recorrido (ServicioCambioHuecoPicking / ServicioCambioHuecoReposicion), que es la otra cara."
            },
            // «ProductoOCantidadEquivocados» y «ReenvioPorPreparacion» ya se miran con los datos (Carlos 27/11/26): CambiadoPorOtro y
            // ReenviadoSinCoste, que forman ErrorDeEnvioReclamado. Una cantidad de menos que se repone sin coste es ReenviadoSinCoste.
            new IndicadorSinRastroAlmacenDTO
            {
                Indicador = "EtiquetaConPackingIncompleto",
                Que = "La etiqueta de agencia se imprimió con el packing sin completar (productos sin meter en las cajas y sin falta del packing). Hoy se puede ver después " +
                    "como «DescuadrePickingPacking», pero no se sabe si se imprimió así.",
                HaceFalta = "Apuntar un evento en ServicioEtiquetaPacking.Imprimir cuando el estado del packing (LeerEstadoPedido) no esté completo. No se ha conectado: " +
                    "es una lectura más en mitad de la etiqueta y Carlos tiene que decidir si se avisa o se frena."
            },
            new IndicadorSinRastroAlmacenDTO
            {
                Indicador = "EntregaIncompleta (no imputable)",
                Que = "La nota de entrega de lo pendiente (#542) sale de las líneas con «Recoger», que se deciden ANTES del picking (el picking ya no las recoge). " +
                    "No es un fallo de preparación.",
                HaceFalta = "Nada: se queda en el pedido perfecto."
            },
            new IndicadorSinRastroAlmacenDTO
            {
                Indicador = "TocadoTrasElPicking (no imputable)",
                Que = "Quitar el picking o ampliar un pedido en preparación lo hace Almacén, pero a petición de otro (vendedor o cliente). No es un fallo de preparación.",
                HaceFalta = "Nada: se queda en el pedido perfecto (y en el corte de vendedores)."
            }
        };

        private readonly Func<ILectorCalidadAlmacen> lector;
        private readonly Func<DateTime> ahora;
        private readonly ObjectCache cache;

        public ServicioCalidadAlmacen(Func<ILectorCalidadAlmacen> lector = null, Func<DateTime> ahora = null, ObjectCache cache = null)
        {
            this.lector = lector ?? (() => LectorCalidadAlmacenSql.DesdeConfiguracion());
            this.ahora = ahora ?? (() => DateTime.Now);
            this.cache = cache ?? MemoryCache.Default;
        }

        public CalculoCalidadAlmacen Calcular(string empresa, DateTime desde, DateTime hasta)
        {
            string clave = $"CalidadAlmacen|{empresa}|{desde:yyyyMMdd}|{hasta:yyyyMMdd}";
            var nuevo = new Lazy<CalculoCalidadAlmacen>(() => CalcularSinCache(empresa, desde, hasta));
            var existente = cache.AddOrGetExisting(clave, nuevo, DateTimeOffset.UtcNow.Add(DURACION_CACHE)) as Lazy<CalculoCalidadAlmacen>;
            Lazy<CalculoCalidadAlmacen> usar = existente ?? nuevo;
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

        internal CalculoCalidadAlmacen CalcularSinCache(string empresa, DateTime desde, DateTime hasta)
        {
            LecturaCalidadAlmacen lectura = lector().Leer(empresa, desde, hasta) ?? new LecturaCalidadAlmacen();
            return new CalculoCalidadAlmacen
            {
                Empresa = empresa,
                Desde = desde,
                Hasta = hasta,
                CalculadoEl = ahora(),
                Lectura = lectura,
                Evaluacion = ReglaCalidadAlmacen.Evaluar(lectura, ahora().Date)
            };
        }

        public ResumenCalidadAlmacenDTO Resumen(string empresa, DateTime desde, DateTime hasta) => Resumir(Calcular(empresa, desde, hasta));

        public DetalleCalidadAlmacenDTO Detalle(string empresa, DateTime desde, DateTime hasta, string preparador, IndicadorAlmacen? indicador, int pagina, int tamano) =>
            Detallar(Calcular(empresa, desde, hasta), preparador, indicador, pagina, tamano);

        /// <summary>Desde cuándo hay rastro de cada indicador.</summary>
        public static DateTime? EvaluadoDesde(IndicadorAlmacen indicador, LecturaCalidadAlmacen lectura)
        {
            switch (indicador)
            {
                case IndicadorAlmacen.FaltaEnPacking:
                case IndicadorAlmacen.DescuadrePickingPacking:
                    return Mayor(lectura?.PrimeraLecturaPicking, lectura?.PrimeraLecturaPacking);
                case IndicadorAlmacen.LecturaNoCasada:
                    return Mayor(lectura?.PrimeraLecturaPicking, lectura?.PrimeraNoCasada);
                default:
                    return lectura?.PrimeraLecturaPicking;
            }
        }

        private static DateTime? Mayor(DateTime? a, DateTime? b) => !a.HasValue || !b.HasValue ? (DateTime?)null : (a.Value > b.Value ? a : b);

        public static ResumenCalidadAlmacenDTO Resumir(CalculoCalidadAlmacen calculo)
        {
            EvaluacionCalidadAlmacen e = calculo.Evaluacion ?? new EvaluacionCalidadAlmacen();
            LecturaCalidadAlmacen lectura = calculo.Lectura ?? new LecturaCalidadAlmacen();
            List<NoCasadasCalidadAlmacen> noCasadas = lectura.NoCasadas ?? new List<NoCasadasCalidadAlmacen>();
            bool hayNoCasadas = !lectura.TablasQueFaltan.Contains("dbo.LecturasNoCasadasAlmacen");
            var resumen = new ResumenCalidadAlmacenDTO
            {
                Empresa = calculo.Empresa,
                Desde = calculo.Desde,
                Hasta = calculo.Hasta,
                CalculadoEl = calculo.CalculadoEl,
                PreparadorDesde = lectura.PrimeraLecturaPicking,
                LineasSinPreparador = e.Lineas.Count(l => l.Preparador == null),
                Total = Preparador(null, e, noCasadas, hayNoCasadas),
                IndicadoresOficiales = InfoIndicadoresAlmacen.Oficiales.Select(i => i.ToString()).ToList(),
                DiasReclamacionEnvio = lectura.DiasReclamacionEnvio,
                SinRastro = SIN_RASTRO.ToList(),
                Aviso = lectura.TablasQueFaltan.Count == 0 ? null
                    : $"Faltan tablas ({string.Join(", ", lectura.TablasQueFaltan)}): lo que dependa de ellas no se ha podido mirar."
            };
            List<PickingSinLecturasAlmacen> sinLecturas = lectura.PickingsSinLecturas ?? new List<PickingSinLecturasAlmacen>();
            resumen.PickingsSinRastro = new PickingsSinRastroDTO
            {
                Pickings = sinLecturas.Count,
                Pedidos = sinLecturas.Sum(p => p.Pedidos),
                Lineas = sinLecturas.Sum(p => p.Lineas),
                PorAlmacen = sinLecturas.GroupBy(p => p.Almacen ?? "(sin almacén)").OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count())
            };

            var personas = e.Paradas.Select(p => p.Preparador)
                .Concat(e.Paradas.Select(p => p.DioLaFalta))
                .Concat(noCasadas.Select(n => ReglaCalidadAlmacen.Persona(n.Usuario)))
                .Where(p => p != null)
                .GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Select(g => g.First());
            resumen.Preparadores = personas.Select(p => Preparador(p, e, noCasadas, hayNoCasadas))
                .OrderByDescending(p => p.Lineas).ThenBy(p => p.Preparador, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (IndicadorAlmacen indicador in InfoIndicadoresAlmacen.Todos)
            {
                IndicadorPreparadorDTO total = resumen.Total.Indicadores.First(i => i.Indicador == indicador.ToString());
                DateTime? desde = EvaluadoDesde(indicador, lectura);
                resumen.Indicadores.Add(new IndicadorAlmacenDTO
                {
                    Indicador = indicador.ToString(),
                    Texto = InfoIndicadoresAlmacen.Texto(indicador),
                    EsError = InfoIndicadoresAlmacen.EsError(indicador),
                    Oficial = InfoIndicadoresAlmacen.Oficiales.Contains(indicador),
                    Ambito = total.Ambito,
                    EvaluadoDesde = desde,
                    Evaluadas = total.Evaluadas,
                    Con = total.Con,
                    Porcentaje = total.Porcentaje
                });
                int sinEvaluar = SinEvaluar(indicador, e, hayNoCasadas);
                if (sinEvaluar > 0 || !desde.HasValue)
                {
                    resumen.IndicadoresNoEvaluados.Add(new IndicadorNoEvaluadoAlmacenDTO
                    {
                        Indicador = indicador.ToString(),
                        PorQue = InfoIndicadoresAlmacen.PorQueNoEvaluado(indicador),
                        EvaluadoDesde = desde,
                        SinEvaluar = sinEvaluar
                    });
                }
            }
            return resumen;
        }

        private static int SinEvaluar(IndicadorAlmacen indicador, EvaluacionCalidadAlmacen e, bool hayNoCasadas)
        {
            switch (InfoIndicadoresAlmacen.Ambito(indicador))
            {
                case AmbitoIndicadorAlmacen.Linea:
                    return e.Lineas.Count(l => l.NoEvaluados.Contains(indicador));
                case AmbitoIndicadorAlmacen.Parada:
                    return e.Paradas.Count(p => p.NoEvaluados.Contains(indicador));
                default:
                    return hayNoCasadas ? 0 : e.Paradas.Count;
            }
        }

        /// <summary>Lo de una persona (o el total con <paramref name="persona"/> null).</summary>
        internal static PreparadorAlmacenDTO Preparador(string persona, EvaluacionCalidadAlmacen e, List<NoCasadasCalidadAlmacen> noCasadas, bool hayNoCasadas)
        {
            bool EsSuya(string quien) => persona == null || string.Equals(quien, persona, StringComparison.OrdinalIgnoreCase);
            List<LineaPreparadaAlmacen> lineas = e.Lineas.Where(l => l.Preparador != null && EsSuya(l.Preparador)).ToList();
            List<ParadaPreparadaAlmacen> paradas = e.Paradas.Where(p => EsSuya(p.Preparador)).ToList();
            var pickings = new HashSet<int>(paradas.Select(p => p.Picking));
            List<IndicadorAlmacen> deError = InfoIndicadoresAlmacen.Todos
                .Where(i => InfoIndicadoresAlmacen.EsError(i) && InfoIndicadoresAlmacen.Ambito(i) == AmbitoIndicadorAlmacen.Linea).ToList();
            List<LineaPreparadaAlmacen> evaluadasError = lineas.Where(l => deError.Any(i => !l.NoEvaluados.Contains(i))).ToList();
            int conError = evaluadasError.Count(l => deError.Any(i => l.Indicadores.Contains(i)));
            var dto = new PreparadorAlmacenDTO
            {
                Preparador = persona,
                Pickings = pickings.Count,
                PickingsCompartidos = pickings.Count(p => e.PickingsCompartidos.Contains(p)),
                Pedidos = lineas.Select(l => l.Pedido).Distinct().Count(),
                Lineas = lineas.Count,
                Paradas = paradas.Count,
                Unidades = paradas.Sum(p => p.Cogidas),
                LineasEvaluadasError = evaluadasError.Count,
                LineasConError = conError,
                PorcentajeError = Porcentaje(conError, evaluadasError.Count)
            };
            foreach (IndicadorAlmacen indicador in InfoIndicadoresAlmacen.Todos)
            {
                AmbitoIndicadorAlmacen ambito = InfoIndicadoresAlmacen.Ambito(indicador);
                var fila = new IndicadorPreparadorDTO { Indicador = indicador.ToString(), Ambito = ambito.ToString() };
                if (ambito == AmbitoIndicadorAlmacen.Linea)
                {
                    List<LineaPreparadaAlmacen> evaluadas = lineas.Where(l => !l.NoEvaluados.Contains(indicador)).ToList();
                    fila.Evaluadas = evaluadas.Count;
                    fila.Con = evaluadas.Count(l => l.Indicadores.Contains(indicador));
                    fila.Porcentaje = Porcentaje(fila.Con, evaluadas.Count);
                }
                else if (ambito == AmbitoIndicadorAlmacen.Parada)
                {
                    // Denominador: las paradas que hizo; numerador: las que se le imputan (la falta, a quien la dio)
                    List<ParadaPreparadaAlmacen> evaluadas = e.Paradas.Where(p => !p.NoEvaluados.Contains(indicador) && EsSuya(p.Imputado(indicador))).ToList();
                    int deSusParadas = paradas.Count(p => !p.NoEvaluados.Contains(indicador));
                    fila.Evaluadas = deSusParadas;
                    fila.Con = evaluadas.Count(p => p.Indicadores.Contains(indicador));
                    fila.Porcentaje = Porcentaje(fila.Con, deSusParadas);
                }
                else
                {
                    fila.Evaluadas = null;
                    fila.Con = hayNoCasadas ? noCasadas.Where(n => EsSuya(ReglaCalidadAlmacen.Persona(n.Usuario))).Sum(n => n.Lecturas) : 0;
                }
                dto.Indicadores.Add(fila);
            }
            dto.ErrorDeEnvioReclamado = dto.Indicadores.First(i => i.Indicador == IndicadorAlmacen.ErrorDeEnvioReclamado.ToString());
            dto.FaltaEnPacking = dto.Indicadores.First(i => i.Indicador == IndicadorAlmacen.FaltaEnPacking.ToString());
            return dto;
        }

        public static DetalleCalidadAlmacenDTO Detallar(CalculoCalidadAlmacen calculo, string preparador, IndicadorAlmacen? indicador, int pagina, int tamano)
        {
            EvaluacionCalidadAlmacen e = calculo.Evaluacion ?? new EvaluacionCalidadAlmacen();
            string persona = ReglaCalidadAlmacen.Persona(preparador);
            bool EsSuya(string quien) => persona == null || string.Equals(quien, persona, StringComparison.OrdinalIgnoreCase);
            AmbitoIndicadorAlmacen? ambito = indicador.HasValue ? InfoIndicadoresAlmacen.Ambito(indicador.Value) : (AmbitoIndicadorAlmacen?)null;

            List<LineaPreparadaAlmacen> lineas = ambito == null || ambito == AmbitoIndicadorAlmacen.Linea
                ? e.Lineas.Where(l => (persona == null || EsSuya(l.Preparador)) && (indicador == null || l.Indicadores.Contains(indicador.Value))).ToList()
                : new List<LineaPreparadaAlmacen>();
            List<ParadaPreparadaAlmacen> paradas = ambito == null || ambito == AmbitoIndicadorAlmacen.Parada
                ? e.Paradas.Where(p => indicador == null
                    ? EsSuya(p.Preparador) || EsSuya(p.DioLaFalta)
                    : p.Indicadores.Contains(indicador.Value) && EsSuya(p.Imputado(indicador.Value))).ToList()
                : new List<ParadaPreparadaAlmacen>();
            List<NoCasadasCalidadAlmacen> noCasadas = ambito == null || ambito == AmbitoIndicadorAlmacen.Lecturas
                ? (calculo.Lectura?.NoCasadas ?? new List<NoCasadasCalidadAlmacen>()).Where(n => EsSuya(ReglaCalidadAlmacen.Persona(n.Usuario))).ToList()
                : new List<NoCasadasCalidadAlmacen>();

            return new DetalleCalidadAlmacenDTO
            {
                Empresa = calculo.Empresa,
                Desde = calculo.Desde,
                Hasta = calculo.Hasta,
                Preparador = persona,
                Indicador = indicador?.ToString(),
                Pagina = pagina,
                Tamano = tamano,
                TotalLineas = lineas.Count,
                TotalParadas = paradas.Count,
                Lineas = lineas.Skip((pagina - 1) * tamano).Take(tamano).Select(DetalleLinea).ToList(),
                Paradas = paradas.Skip((pagina - 1) * tamano).Take(tamano).Select(DetalleParada).ToList(),
                NoCasadas = noCasadas.OrderBy(n => n.Picking).ThenBy(n => n.Usuario, StringComparer.OrdinalIgnoreCase)
                    .Select(n => new NoCasadasPreparadorDTO
                    {
                        Picking = n.Picking,
                        Usuario = ReglaCalidadAlmacen.Persona(n.Usuario),
                        Pantalla = n.Pantalla,
                        Lecturas = n.Lecturas
                    }).ToList()
            };
        }

        private static LineaCalidadAlmacenDTO DetalleLinea(LineaPreparadaAlmacen l) => new LineaCalidadAlmacenDTO
        {
            Picking = l.Picking,
            FechaPicking = l.FechaPicking,
            Pedido = l.Pedido,
            Cliente = l.Cliente,
            Contacto = l.Contacto,
            Producto = l.Producto,
            LineasPedido = l.LineasPedido.ToList(),
            Cantidad = l.Cantidad,
            Almacen = l.Almacen,
            Factura = l.Factura,
            Preparador = l.Preparador,
            PackingEvaluado = l.PackingEvaluado,
            Empaquetador = l.Empaquetador,
            MetidasEnPacking = l.MetidasEnPacking,
            FaltasEnPacking = l.FaltasEnPacking,
            UnidadesRectificadas = l.UnidadesRectificadas,
            Indicadores = l.Indicadores.OrderBy(i => (int)i).Select(i => i.ToString()).ToList(),
            NoEvaluados = l.NoEvaluados.OrderBy(i => (int)i).Select(i => i.ToString()).ToList(),
            SubMotivosReclamacion = l.Reclamaciones.SelectMany(r => r.SubMotivos).Distinct().OrderBy(s => (int)s).Select(s => s.ToString()).ToList(),
            ReclamacionEvaluada = l.Reclamaciones.Count > 0 && l.Reclamaciones.All(r => r.Evaluada),
            PruebasReclamacion = l.Reclamaciones.SelectMany(r => r.Pruebas).ToList()
        };

        private static ParadaCalidadAlmacenDTO DetalleParada(ParadaPreparadaAlmacen p) => new ParadaCalidadAlmacenDTO
        {
            Picking = p.Picking,
            FechaPicking = p.FechaPicking,
            Producto = p.Producto,
            Preparador = p.Preparador,
            OtrosQueLeyeron = p.OtrosQueLeyeron.ToList(),
            Cogidas = p.Cogidas,
            Faltas = p.Faltas,
            DioLaFalta = p.DioLaFalta,
            CantidadEnLineas = p.CantidadEnLineas,
            MetidasEnPacking = p.MetidasEnPacking,
            FaltasEnPacking = p.FaltasEnPacking,
            Indicadores = p.Indicadores.OrderBy(i => (int)i).Select(i => i.ToString()).ToList(),
            NoEvaluados = p.NoEvaluados.OrderBy(i => (int)i).Select(i => i.ToString()).ToList()
        };

        internal static decimal? Porcentaje(int parte, int total) =>
            total <= 0 ? (decimal?)null : Math.Round(100m * parte / total, 1, MidpointRounding.AwayFromZero);
    }
}
