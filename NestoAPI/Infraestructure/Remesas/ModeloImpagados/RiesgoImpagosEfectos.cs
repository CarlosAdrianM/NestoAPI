using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Remesas.ModeloImpagados
{
    /// <summary>Un efecto con sus rasgos y su predicción (lo que da <see cref="CalculoRiesgoImpagos"/>).</summary>
    public class RiesgoEfectoImpago
    {
        public ReciboGirado Efecto { get; set; }
        public RasgosImpago Rasgos { get; set; }
        public PrediccionRiesgoImpago Prediccion { get; set; }
    }

    /// <summary>
    /// NestoAPI#896 (27/11/26): EL NÚCLEO COMÚN de la predicción de una lista de efectos, venga de donde venga: los de una remesa ya
    /// creada (<see cref="SombraImpagosRemesa"/>) o los candidatos a la que se va a crear (<see cref="CandidatosRiesgoImpagos"/>). Lee la
    /// historia de SUS clientes (por tandas, en el repositorio) y el nivel general, enlaza los impagados, saca los rasgos de cada efecto el
    /// día de SU <see cref="ReciboGirado.FechaGiro"/> y predice (modelo + calibración, regla y motivos). Solo lee.
    /// </summary>
    public static class CalculoRiesgoImpagos
    {
        /// <param name="hoy">Hoy: de él salen los años de historia que se leen y el nivel general.</param>
        public static List<RiesgoEfectoImpago> Calcular(IRepositorioModeloImpagos repositorio, IPredictorImpagos predictor, string empresa,
            IReadOnlyList<ReciboGirado> efectos, DateTime hoy)
        {
            var resultado = new List<RiesgoEfectoImpago>();
            if (efectos == null || efectos.Count == 0)
            {
                return resultado;
            }
            DateTime dia = hoy.Date;
            DateTime desde = dia.AddYears(-ModeloImpagosRemesasJobsService.ANIOS_HISTORICO);
            HistoricoImpagos historia = repositorio.LeerHistorialClientes(empresa, efectos.Select(e => e.Cliente).ToList(), desde);
            _ = EnlazadorImpagados.Enlazar(historia.Recibos, historia.Impagados);
            NivelGeneralImpagos general = repositorio.LeerNivelGeneral(dia);
            ILookup<string, ReciboGirado> porCliente = historia.Recibos.ToLookup(r => r.ClaveCliente);

            // El día del giro es el de cada efecto: el de la remesa (hoy, si se apunta al crearla) o hoy para un candidato. Los rasgos
            // solo miran lo girado ANTES de ese día, así que el propio efecto (girado hoy o sin girar aún) nunca cuenta como historia.
            List<RasgosImpago> rasgos = efectos.Select(e => ConstructorRasgosImpagos.Rasgos(e, e.FechaGiro, porCliente[e.ClaveCliente], general)).ToList();
            List<PrediccionRiesgoImpago> predicciones = predictor.Predecir(rasgos);
            for (int i = 0; i < efectos.Count && i < predicciones.Count; i++)
            {
                resultado.Add(new RiesgoEfectoImpago { Efecto = efectos[i], Rasgos = rasgos[i], Prediccion = predicciones[i] });
            }
            return resultado;
        }
    }

    /// <summary>Un efecto candidato con su riesgo (GET api/Remesas/ModeloImpagados/Candidatos).</summary>
    public class EfectoRiesgoImpagoDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Documento { get; set; }
        public string Efecto { get; set; }
        /// <summary>Lo que se giraría (el importe pendiente del candidato).</summary>
        public decimal Importe { get; set; }
        public DateTime? FechaVto { get; set; }
        public int NumOrden { get; set; }
        /// <summary>Si EfectosCandidatos lo deja preseleccionado (entraría en la remesa); si no, <see cref="MotivoRetencion"/>.</summary>
        public bool Preseleccionado { get; set; }
        public string MotivoRetencion { get; set; }
        /// <summary>La del modelo; sin modelo, la de la regla; null sin ninguno.</summary>
        public double? Probabilidad { get; set; }
        public double? ProbabilidadRegla { get; set; }
        public string Tramo { get; set; }
        public List<string> Motivos { get; set; } = new List<string>();
    }

    public class TotalRiesgoImpagosDTO
    {
        public int Recibos { get; set; }
        public decimal Importe { get; set; }
        /// <summary>Suma de las probabilidades (2 decimales); null sin probabilidades.</summary>
        public double? DevolucionesEsperadas { get; set; }
        /// <summary>Suma de importe × probabilidad (2 decimales); null sin probabilidades.</summary>
        public decimal? ImporteEnRiesgo { get; set; }
    }

    public class TramoRiesgoImpagosDTO
    {
        public string Tramo { get; set; }
        public int Recibos { get; set; }
        public decimal Importe { get; set; }
        public double DevolucionesEsperadas { get; set; }
    }

    /// <summary>Respuesta de GET api/Remesas/ModeloImpagados/Candidatos.</summary>
    public class CandidatosRiesgoImpagosDTO
    {
        public DateTime Fecha { get; set; }
        public string VersionModelo { get; set; }
        public bool ConModelo { get; set; }
        public string Mensaje { get; set; }
        public TotalRiesgoImpagosDTO Total { get; set; } = new TotalRiesgoImpagosDTO();
        public List<TramoRiesgoImpagosDTO> Tramos { get; set; } = new List<TramoRiesgoImpagosDTO>();
        public List<EfectoRiesgoImpagoDTO> Efectos { get; set; } = new List<EfectoRiesgoImpagoDTO>();
    }

    /// <summary>
    /// NestoAPI#896 (27/11/26): el riesgo de los candidatos a remesa ANTES de girar, para Dirección. Lo mismo que apuntaría la sombra si
    /// la remesa se creara hoy (día del giro = hoy), con el núcleo común <see cref="CalculoRiesgoImpagos"/>. Solo lee: no apunta nada.
    /// </summary>
    public static class CandidatosRiesgoImpagos
    {
        public const string MENSAJE_SIN_MODELO = "Todavía no hay regla ni modelo de impagados guardados: hay que entrenar una vez (POST api/Remesas/ModeloImpagados/Entrenar).";
        public const string MENSAJE_SOLO_REGLA = "No hay modelo guardado (o no ha podido predecir): la probabilidad es la de la regla.";

        public static CandidatosRiesgoImpagosDTO Construir(IRepositorioModeloImpagos repositorio, IPredictorImpagos predictor, string empresa,
            IReadOnlyList<EfectoCandidatoDTO> candidatos, DateTime ahora)
        {
            string empresaLimpia = (empresa ?? string.Empty).Trim();
            DateTime hoy = ahora.Date;
            List<EfectoCandidatoDTO> lista = (candidatos ?? new List<EfectoCandidatoDTO>()).Where(c => c != null).ToList();
            if (!predictor.HayAlgo)
            {
                return Armar(ahora, empresaLimpia, lista, new Dictionary<int, RiesgoEfectoImpago>(), MENSAJE_SIN_MODELO);
            }
            Dictionary<int, ReciboGirado> cartera = repositorio.LeerEfectosCartera(empresaLimpia, lista.Select(c => c.Id).ToList())
                .GroupBy(r => r.NumOrden).ToDictionary(g => g.Key, g => g.First());
            List<ReciboGirado> efectos = lista.Select(c => cartera.TryGetValue(c.Id, out ReciboGirado r) ? r : DesdeCandidato(empresaLimpia, c)).ToList();
            foreach (ReciboGirado e in efectos)
            {
                ComoSiSeGiraraHoy(e, hoy);
            }
            Dictionary<int, RiesgoEfectoImpago> riesgos = CalculoRiesgoImpagos.Calcular(repositorio, predictor, empresaLimpia, efectos, hoy)
                .GroupBy(r => r.Efecto.NumOrden).ToDictionary(g => g.Key, g => g.First());
            return Armar(ahora, empresaLimpia, lista, riesgos, null);
        }

        /// <summary>
        /// El efecto tal como estaría en la remesa creada hoy: el día del giro es el de la remesa (<see cref="ReglaFechaGiro"/> con hoy) y
        /// aún no tiene remesa. La historia del cliente no lo trae (no está girado), así que «recibos en 45 días» y «regiro» salen de lo
        /// girado ANTES, igual que en la sombra (que lo excluye por ser del mismo día).
        /// </summary>
        internal static void ComoSiSeGiraraHoy(ReciboGirado efecto, DateTime hoy)
        {
            efecto.FechaGiro = ReglaFechaGiro.Calcular(efecto.FechaFactura, efecto.FechaVto, hoy.Date);
            efecto.Remesa = null;
            efecto.FechaImpago = null;
            efecto.MotivoImpago = null;
        }

        /// <summary>Si la cartera no lo devolviera (no debería), lo que se sabe del candidato: sin vendedor, forma de venta ni entidad.</summary>
        private static ReciboGirado DesdeCandidato(string empresa, EfectoCandidatoDTO c) => new ReciboGirado
        {
            Empresa = empresa,
            NumOrden = c.Id,
            Cliente = c.Cliente?.Trim(),
            Contacto = c.Contacto?.Trim(),
            FechaFactura = c.Fecha.Date,
            FechaVto = c.Vencimiento?.Date,
            Documento = c.Documento?.Trim(),
            Efecto = c.Efecto?.Trim(),
            Importe = (double)c.ImportePendiente,
            Ccc = c.Ccc?.Trim()
        };

        /// <summary>PURA: tramos, totales y orden (de mayor a menor probabilidad).</summary>
        internal static CandidatosRiesgoImpagosDTO Armar(DateTime ahora, string empresa, IReadOnlyList<EfectoCandidatoDTO> candidatos,
            IReadOnlyDictionary<int, RiesgoEfectoImpago> riesgos, string mensaje)
        {
            var dto = new CandidatosRiesgoImpagosDTO { Fecha = ahora, Mensaje = mensaje };
            foreach (EfectoCandidatoDTO c in candidatos)
            {
                _ = riesgos.TryGetValue(c.Id, out RiesgoEfectoImpago riesgo);
                PrediccionRiesgoImpago p = riesgo?.Prediccion;
                double? probabilidad = Redondear(p?.Probabilidad ?? p?.ProbabilidadRegla);
                dto.Efectos.Add(new EfectoRiesgoImpagoDTO
                {
                    Empresa = (riesgo?.Efecto?.Empresa ?? empresa)?.Trim(),
                    Cliente = c.Cliente?.Trim(),
                    Contacto = c.Contacto?.Trim(),
                    Nombre = string.IsNullOrWhiteSpace(riesgo?.Efecto?.Nombre) ? null : riesgo.Efecto.Nombre.Trim(),
                    Documento = c.Documento?.Trim(),
                    Efecto = c.Efecto?.Trim(),
                    Importe = c.ImportePendiente,
                    FechaVto = c.Vencimiento,
                    NumOrden = c.Id,
                    Preseleccionado = c.Preseleccionado,
                    MotivoRetencion = c.Motivo?.Trim(),
                    Probabilidad = probabilidad,
                    ProbabilidadRegla = Redondear(p?.ProbabilidadRegla),
                    Tramo = probabilidad.HasValue ? NombreTramo(probabilidad.Value) : null,
                    Motivos = p?.Motivos?.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToList() ?? new List<string>()
                });
            }
            dto.VersionModelo = riesgos.Values.Select(r => r.Prediccion?.VersionModelo).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
            dto.ConModelo = riesgos.Values.Any(r => r.Prediccion?.Probabilidad.HasValue == true);
            if (!dto.ConModelo)
            {
                dto.VersionModelo = null;
                if (dto.Mensaje == null && riesgos.Count > 0)
                {
                    dto.Mensaje = MENSAJE_SOLO_REGLA;
                }
            }
            dto.Efectos = dto.Efectos.OrderByDescending(e => e.Probabilidad ?? -1).ThenByDescending(e => e.Importe).ThenBy(e => e.NumOrden).ToList();

            List<EfectoRiesgoImpagoDTO> conProbabilidad = dto.Efectos.Where(e => e.Probabilidad.HasValue).ToList();
            dto.Total.Recibos = dto.Efectos.Count;
            dto.Total.Importe = dto.Efectos.Sum(e => e.Importe);
            if (conProbabilidad.Count > 0)
            {
                dto.Total.DevolucionesEsperadas = Math.Round(conProbabilidad.Sum(e => e.Probabilidad.Value), 2, MidpointRounding.AwayFromZero);
                dto.Total.ImporteEnRiesgo = Math.Round(conProbabilidad.Sum(e => e.Importe * (decimal)e.Probabilidad.Value), 2, MidpointRounding.AwayFromZero);
                foreach (string nombre in CalculoMetricasImpagos.NombresTramos)
                {
                    List<EfectoRiesgoImpagoDTO> grupo = conProbabilidad.Where(e => e.Tramo == nombre).ToList();
                    dto.Tramos.Add(new TramoRiesgoImpagosDTO
                    {
                        Tramo = nombre,
                        Recibos = grupo.Count,
                        Importe = grupo.Sum(e => e.Importe),
                        DevolucionesEsperadas = Math.Round(grupo.Sum(e => e.Probabilidad.Value), 2, MidpointRounding.AwayFromZero)
                    });
                }
            }
            return dto;
        }

        /// <summary>El tramo de <see cref="CalculoMetricasImpagos"/> (los mismos cuatro límites) en que cae una probabilidad.</summary>
        internal static string NombreTramo(double probabilidad)
        {
            for (int i = 0; i < CalculoMetricasImpagos.NombresTramos.Length; i++)
            {
                if (probabilidad >= CalculoMetricasImpagos.LimitesTramos[i] && probabilidad < CalculoMetricasImpagos.LimitesTramos[i + 1])
                {
                    return CalculoMetricasImpagos.NombresTramos[i];
                }
            }
            return probabilidad < 0 ? CalculoMetricasImpagos.NombresTramos[0] : CalculoMetricasImpagos.NombresTramos[CalculoMetricasImpagos.NombresTramos.Length - 1];
        }

        private static double? Redondear(double? p) => p.HasValue ? Math.Round(p.Value, 6) : (double?)null;
    }
}
